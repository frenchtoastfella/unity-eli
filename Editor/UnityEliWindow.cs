using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor
{
    public class UnityEliWindow : EditorWindow
    {
        private string _inputText = string.Empty;
        private Vector2 _scrollPosition;
        private bool _isProcessing;
        private bool _lastRequestFailed;
        private string _sessionId;
        private List<DisplayMessage> _messages = new List<DisplayMessage>();
        private GUIStyle _inputTextAreaStyle;
        private GUIStyle _sendButtonStyle;
        private Texture2D _sendIcon;
        private Texture2D _copyIcon;

        // ── Message list cap ──────────────────────────────────────────────────
        /// <summary>Maximum number of display messages to keep. Oldest entries (after the
        /// first <see cref="MessageKeepPrefix"/> are trimmed when this limit is exceeded.</summary>
        private const int MaxMessages = 1000;
        /// <summary>Number of leading messages to preserve when trimming (e.g. system/init messages).</summary>
        private const int MessageKeepPrefix = 5;

        // ── Context usage ─────────────────────────────────────────────────────
        private const int ContextWindowSize = 200000;
        private int _contextTokens;

        // ── Play mode error tracking ──────────────────────────────────────────
        private const string PlayModeLogStartKey = "UnityEli_PlayModeLogStart";
        private const int MaxPlayModeIssues = 10;

        // ── Tool batch expand state ──────────────────────────────────────────
        private HashSet<int> _expandedBatches = new HashSet<int>();

        // ── Wizard state ──────────────────────────────────────────────────────
        private struct WizardQuestion
        {
            public string Title;  // bold text, e.g. "Game type"
            public string Body;   // remainder of line, e.g. "What genre is this?"
        }

        private List<WizardQuestion> _wizardQuestions; // null = not in wizard mode
        private List<string> _wizardAnswers = new List<string>();
        private int _wizardStep;
        private string _wizardInput = string.Empty;

        [MenuItem("Window/Unity Eli")]
        public static void ShowWindow()
        {
            if (!SetupWizardWindow.IsSetupCompleted)
            {
                SetupWizardWindow.ShowWizard();
                return;
            }

            var window = GetWindow<UnityEliWindow>();
            window.titleContent = new GUIContent("Unity Eli");
            window.minSize = new Vector2(400, 300);
        }

        // ── Model notification ────────────────────────────────────────────────
        private const string ModelNotificationShownKey = "UnityEli_ModelNotificationShown";
        private const string ModelsFetchedKey = "UnityEli_ModelsFetchedThisSession";

        private void OnEnable()
        {
            LoadIcons();
            _messages = SimpleSessionState.LoadDisplayMessages();
            _sessionId = SimpleSessionState.LoadSessionId();
            ClaudeCodeProcess.OnStreamEvent += HandleStreamEvent;
            ClaudeCodeProcess.OnComplete += HandleComplete;
            McpServer.ToolExecuted += HandleToolExecuted;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            if (!McpServer.IsRunning) McpServer.Start();
            if (SimpleSessionState.LoadIsProcessing())
            {
                // Domain reload while processing — the Claude process survives reloads.
                // ClaudeCodeProcess.TryReconnect() will resume reading its output.
                // Keep _isProcessing true so the UI shows it's still working.
                _isProcessing = true;
            }
            else if (_sessionId == null && _messages.Count == 0)
            {
                // SessionState is empty — either fresh editor launch or first open.
                // Try to restore the last active session from persistent history.
                var lastId = SessionHistory.ActiveSessionId;
                if (!string.IsNullOrEmpty(lastId))
                {
                    var restored = SessionHistory.LoadSession(lastId);
                    if (restored.Count > 0)
                    {
                        _messages = restored;
                        _sessionId = lastId;
                        SimpleSessionState.SaveAll(_messages, _sessionId, false);
                    }
                    else
                    {
                        EditorApplication.delayCall += AutoInitialize;
                    }
                }
                else
                {
                    EditorApplication.delayCall += AutoInitialize;
                }
            }

            if (!SessionState.GetBool(ModelsFetchedKey, false))
                EditorApplication.delayCall += StartBackgroundModelRefresh;
        }

        private void StartBackgroundModelRefresh()
        {
            SessionState.SetBool(ModelsFetchedKey, true);


            UnityEliSettings.RefreshModelsInBackground();
            EditorApplication.update += PollModelRefresh;
        }

        private void PollModelRefresh()
        {
            if (UnityEliSettings.IsRefreshingModels) return;

            EditorApplication.update -= PollModelRefresh;



            var hasNewModels = UnityEliSettings.FinishBackgroundRefresh();
            if (hasNewModels)
            {
                ShowNotification(
                    new GUIContent("New models available!\nCheck Preferences > Unity Eli to select a model."), 5.0);
            }
            else if (string.IsNullOrEmpty(UnityEliSettings.SelectedModelId)
                     && UnityEliSettings.GetCachedModels().Length > 0
                     && !SessionState.GetBool(ModelNotificationShownKey, false))
            {
                SessionState.SetBool(ModelNotificationShownKey, true);
                ShowNotification(
                    new GUIContent("No model selected.\nGo to Preferences > Unity Eli to choose a model."), 5.0);
            }

            Repaint();
        }

        private void OnDisable()
        {
            ClaudeCodeProcess.OnStreamEvent -= HandleStreamEvent;
            ClaudeCodeProcess.OnComplete -= HandleComplete;
            McpServer.ToolExecuted -= HandleToolExecuted;
            EditorApplication.update -= PollModelRefresh;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnGUI()
        {
            InitializeStyles();
            DrawHeader();

            GUILayout.Space(5);
            if (_wizardQuestions != null)
                DrawWizardInput();
            else
                DrawInputArea();
            GUILayout.Space(5);
            EditorGUILayout.LabelField(string.Empty, GUI.skin.horizontalSlider);
            DrawChatHistory();
        }

        private void ArchiveCurrentSession()
        {
            if (!string.IsNullOrEmpty(_sessionId) && _messages.Count > 0)
            {
                var title = SessionHistory.DeriveTitle(_messages);
                SessionHistory.SaveSession(_sessionId, title, _messages);
            }
        }

        private void StartNewSession()
        {
            ArchiveCurrentSession();
            _messages.Clear();
            _sessionId = null;
            _isProcessing = false;
            _lastRequestFailed = false;
            _contextTokens = 0;
            _wizardQuestions = null;
            _wizardAnswers.Clear();
            _wizardStep = 0;
            _wizardInput = string.Empty;
            ClaudeCodeProcess.Cancel();
            SimpleSessionState.Clear();
            SessionHistory.ActiveSessionId = null;
            Repaint();
        }

        private void ClearCurrentSession()
        {
            _messages.Clear();
            _sessionId = null;
            _isProcessing = false;
            _lastRequestFailed = false;
            _contextTokens = 0;
            _wizardQuestions = null;
            _wizardAnswers.Clear();
            _wizardStep = 0;
            _wizardInput = string.Empty;
            ClaudeCodeProcess.Cancel();
            SimpleSessionState.Clear();
            SessionHistory.ActiveSessionId = null;
            Repaint();
        }

        private void LoadSessionFromHistory(string sessionId)
        {
            ArchiveCurrentSession();
            ClaudeCodeProcess.Cancel();

            _messages = SessionHistory.LoadSession(sessionId);
            _sessionId = sessionId;
            _isProcessing = false;
            _lastRequestFailed = false;
            _contextTokens = 0;
            _wizardQuestions = null;
            _wizardAnswers.Clear();
            _wizardStep = 0;
            _wizardInput = string.Empty;
            SessionHistory.ActiveSessionId = sessionId;
            SimpleSessionState.SaveAll(_messages, _sessionId, false);
            Repaint();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Unity Eli", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("New", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                StartNewSession();
            }
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(40)))
            {
                ClearCurrentSession();
            }
            if (GUILayout.Button("History", EditorStyles.miniButton, GUILayout.Width(55)))
            {
                ShowHistoryMenu();
            }
            var settingsIcon = EditorGUIUtility.IconContent("_Popup");
            if (GUILayout.Button(settingsIcon, EditorStyles.iconButton, GUILayout.Width(24), GUILayout.Height(24)))
                SettingsService.OpenUserPreferences("Preferences/Unity Eli");
            EditorGUILayout.EndHorizontal();
            if (_contextTokens > 0)
            {
                var t = Mathf.Clamp01((float)_contextTokens / ContextWindowSize);
                var barRect = EditorGUILayout.GetControlRect(GUILayout.Height(10));
                EditorGUI.DrawRect(barRect, new Color(0.15f, 0.15f, 0.15f));
                var fillColor = t < 0.5f
                    ? new Color(0.35f, 0.35f, 0.4f)
                    : t < 0.8f
                        ? new Color(0.6f, 0.5f, 0.15f)
                        : new Color(0.7f, 0.2f, 0.2f);
                EditorGUI.DrawRect(new Rect(barRect.x, barRect.y, barRect.width * t, barRect.height), fillColor);
                var label = $"  Context: {_contextTokens / 1000}k / {ContextWindowSize / 1000}k ({t * 100f:0}%)";
                GUI.Label(barRect, label, EditorStyles.miniLabel);
            }

            if (!McpServer.IsRunning)
            {
                EditorGUILayout.HelpBox("MCP server not running. Click below to restart.", MessageType.Warning);
                if (GUILayout.Button("Start MCP Server")) McpServer.Start();
            }
        }

        private void ShowHistoryMenu()
        {
            var index = SessionHistory.LoadIndex();
            var menu = new GenericMenu();

            if (index.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No previous sessions"));
            }
            else
            {
                foreach (var entry in index)
                {
                    var sid = entry.SessionId;
                    var isCurrent = sid == _sessionId;
                    var ts = "";
                    if (DateTime.TryParse(entry.Timestamp, out var dt))
                        ts = dt.ToString("MMM d, HH:mm") + " — ";
                    var label = ts + entry.Title;
                    if (isCurrent) label += " (current)";

                    if (isCurrent)
                        menu.AddDisabledItem(new GUIContent(label));
                    else
                        menu.AddItem(new GUIContent(label), false, () => LoadSessionFromHistory(sid));
                }

                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Clear All History"), false, () =>
                {
                    if (EditorUtility.DisplayDialog("Clear History",
                        "Delete all saved chat sessions? This cannot be undone.", "Clear", "Cancel"))
                    {
                        var all = SessionHistory.LoadIndex();
                        foreach (var e in all)
                            SessionHistory.DeleteSession(e.SessionId);
                    }
                });
            }

            menu.ShowAsContext();
        }

        private void DrawInputArea()
        {
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isProcessing;
            var e = Event.current;

            // Ctrl+Enter = submit, plain Enter = new line (handled by TextArea naturally).
            bool enterPressed = false;
            var isChatInputFocused = GUI.GetNameOfFocusedControl() == "ChatInput";
            if (e.type == EventType.KeyDown && isChatInputFocused && IsEnterKey(e))
            {
                if (e.control || e.command)
                {
                    enterPressed = true;
                    e.Use();
                }
                // Plain Enter / Shift+Enter: let TextArea handle it (inserts newline).
            }

            var lineHeight = _inputTextAreaStyle.lineHeight > 0 ? _inputTextAreaStyle.lineHeight : 16f;
            var content = new GUIContent(_inputText);
            var textWidth = position.width - 60 - 20;
            var calculatedHeight = _inputTextAreaStyle.CalcHeight(content, textWidth);
            var minHeight = lineHeight * 3 + _inputTextAreaStyle.padding.top + _inputTextAreaStyle.padding.bottom;
            var maxHeight = lineHeight * 10 + _inputTextAreaStyle.padding.top + _inputTextAreaStyle.padding.bottom;
            var textAreaHeight = Mathf.Clamp(calculatedHeight, minHeight, maxHeight);
            GUI.SetNextControlName("ChatInput");
            _inputText = EditorGUILayout.TextArea(_inputText, _inputTextAreaStyle, GUILayout.ExpandWidth(true), GUILayout.Height(textAreaHeight));
            var buttonContent = _sendIcon != null ? new GUIContent(_sendIcon) : new GUIContent("Send");
            var sendClicked = GUILayout.Button(buttonContent, _sendButtonStyle, GUILayout.Width(40), GUILayout.Height(textAreaHeight));
            GUI.enabled = true;
            if ((enterPressed || sendClicked) && !string.IsNullOrWhiteSpace(_inputText) && !_isProcessing)
                SendUserMessage();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Ctrl+Enter to send", EditorStyles.miniLabel, GUILayout.Width(140));
            EditorGUILayout.EndHorizontal();
        }

        private void DrawWizardInput()
        {
            var q = _wizardQuestions[_wizardStep];
            var total = _wizardQuestions.Count;
            var isLast = _wizardStep == total - 1;

            // Progress label
            var labelText = $"{_wizardStep + 1}/{total}: {q.Title}";
            if (!string.IsNullOrEmpty(q.Body)) labelText += $" — {q.Body}";

            var labelStyle = new GUIStyle(EditorStyles.wordWrappedLabel) { fontStyle = FontStyle.Bold };
            EditorGUILayout.LabelField(labelText, labelStyle);
            GUILayout.Space(2);

            // Answer textarea
            var e = Event.current;
            var modifierPressed = e.control || e.command;
            var enterPressed = e.type == EventType.KeyDown &&
                               e.keyCode == KeyCode.Return &&
                               modifierPressed &&
                               GUI.GetNameOfFocusedControl() == "WizardInput";

            var lineHeight = _inputTextAreaStyle.lineHeight > 0 ? _inputTextAreaStyle.lineHeight : 16f;
            var minHeight = lineHeight * 2 + _inputTextAreaStyle.padding.top + _inputTextAreaStyle.padding.bottom;
            EditorGUILayout.BeginHorizontal();
            GUI.SetNextControlName("WizardInput");
            _wizardInput = EditorGUILayout.TextArea(_wizardInput, _inputTextAreaStyle,
                GUILayout.ExpandWidth(true), GUILayout.Height(minHeight));

            var btnLabel = isLast ? "Submit" : "Next";
            var wizardBtnStyle = new GUIStyle(GUI.skin.button) { fontSize = 11, alignment = TextAnchor.MiddleCenter };
            var nextClicked = GUILayout.Button(btnLabel, wizardBtnStyle, GUILayout.Width(60), GUILayout.Height(minHeight));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Ctrl+Enter to advance", EditorStyles.miniLabel, GUILayout.Width(130));
            EditorGUILayout.EndHorizontal();

            if ((nextClicked || enterPressed) && !string.IsNullOrWhiteSpace(_wizardInput))
            {
                AdvanceWizard();
                if (e.type != EventType.Used) e.Use();
            }
        }

        // ── Wizard logic ──────────────────────────────────────────────────────

        private void TryActivateWizard(string text)
        {
            // Only activate wizard during auto-initialization (no session yet).
            // Once a session is established, numbered lists are informational, not questions.
            if (_sessionId != null) return;

            var questions = ParseNumberedQuestions(text);
            if (questions.Count < 2) return;

            // Require that at least half the items look like actual questions
            int questionCount = 0;
            foreach (var q in questions)
            {
                var combined = q.Title + " " + q.Body;
                if (combined.Contains("?") ||
                    combined.StartsWith("What", StringComparison.OrdinalIgnoreCase) ||
                    combined.StartsWith("How", StringComparison.OrdinalIgnoreCase) ||
                    combined.StartsWith("Which", StringComparison.OrdinalIgnoreCase) ||
                    combined.StartsWith("Do ", StringComparison.OrdinalIgnoreCase) ||
                    combined.StartsWith("Are ", StringComparison.OrdinalIgnoreCase) ||
                    combined.StartsWith("Is ", StringComparison.OrdinalIgnoreCase) ||
                    combined.StartsWith("Will ", StringComparison.OrdinalIgnoreCase))
                    questionCount++;
            }
            if (questionCount < questions.Count / 2) return;

            _wizardQuestions = questions;
            _wizardAnswers.Clear();
            _wizardStep = 0;
            _wizardInput = string.Empty;
            Repaint();
        }

        private void AdvanceWizard()
        {
            _wizardAnswers.Add(_wizardInput.Trim());
            _wizardInput = string.Empty;
            // Clear keyboard focus so IMGUI drops its cached text buffer
            // and picks up the empty _wizardInput on the next repaint.
            GUIUtility.keyboardControl = 0;
            _wizardStep++;

            if (_wizardStep < _wizardQuestions.Count)
            {
                Repaint();
                return;
            }

            // All answered — compile and send
            var sb = new StringBuilder();
            for (int i = 0; i < _wizardQuestions.Count; i++)
                sb.AppendLine($"{i + 1}. {_wizardQuestions[i].Title}: {_wizardAnswers[i]}");

            var compiled = sb.ToString().TrimEnd();
            _wizardQuestions = null;
            _wizardAnswers.Clear();
            _wizardStep = 0;

            _messages.Add(new DisplayMessage("User", compiled));
            _isProcessing = true;
            _lastRequestFailed = false;
            _scrollPosition = Vector2.zero;
            GUI.FocusControl(null);
            SaveState();
            if (!McpServer.IsRunning) McpServer.Start();
            ClaudeCodeProcess.SendMessage(compiled, _sessionId, McpServer.Port);
            Repaint();
        }

        /// <summary>
        /// Extracts sequential numbered questions from a markdown-formatted assistant message.
        /// Detects lines like "1. **Title** — body" or "1. Title body".
        /// Returns a list only if 2+ sequential questions were found.
        /// </summary>
        private static List<WizardQuestion> ParseNumberedQuestions(string text)
        {
            var questions = new List<WizardQuestion>();
            var lines = text.Split('\n');
            int expected = 1;

            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                var m = Regex.Match(line, @"^(\d+)[.)]\s+(.+)$");
                if (!m.Success) continue;
                if (!int.TryParse(m.Groups[1].Value, out int num) || num != expected) break;

                var content = m.Groups[2].Value;

                // Extract **bold** title and the text after it
                var boldMatch = Regex.Match(content, @"^\*\*(.+?)\*\*\s*[—\-]?\s*(.*)$");
                string title, body;
                if (boldMatch.Success)
                {
                    title = boldMatch.Groups[1].Value.Trim();
                    body = boldMatch.Groups[2].Value.Trim();
                }
                else
                {
                    title = content.Trim();
                    body = string.Empty;
                }

                questions.Add(new WizardQuestion { Title = title, Body = body });
                expected++;
            }

            return questions.Count >= 2 ? questions : new List<WizardQuestion>();
        }

        // ── Chat history ──────────────────────────────────────────────────────

        private void DrawChatHistory()
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));

            // Show "Thinking..." only when processing and no assistant message has started yet
            if (_isProcessing)
            {
                var hasCurrentAssistant = _messages.Count > 0 &&
                                          _messages[_messages.Count - 1].Role == "Assistant";
                if (!hasCurrentAssistant)
                    EditorGUILayout.HelpBox("Thinking...", MessageType.Info);
            }

            if (_lastRequestFailed && !_isProcessing)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox("Last request failed or was interrupted.", MessageType.Warning);
                if (GUILayout.Button("Retry", GUILayout.Width(60), GUILayout.Height(38))) RetryLastMessage();
                EditorGUILayout.EndHorizontal();
            }
            for (int i = _messages.Count - 1; i >= 0; i--) DrawMessage(_messages[i]);
            EditorGUILayout.EndScrollView();
        }

        private void DrawMessage(DisplayMessage message)
        {
            var style = new GUIStyle(EditorStyles.helpBox) { wordWrap = true, richText = true };
            Color backgroundColor;
            Color contentTextColor = EditorStyles.label.normal.textColor;
            switch (message.Role)
            {
                case "User": backgroundColor = new Color(0.2f, 0.4f, 0.7f, 0.25f); contentTextColor = new Color(0.6f, 0.8f, 1f); break;
                case "Error": backgroundColor = new Color(0.6f, 0.2f, 0.2f, 0.3f); break;
                case "Assistant": backgroundColor = new Color(0.3f, 0.3f, 0.3f, 0.3f); contentTextColor = new Color(1f, 0.9f, 0.4f); break;
                case "System": backgroundColor = new Color(0.4f, 0.3f, 0.1f, 0.3f); contentTextColor = new Color(1f, 0.8f, 0.5f); break;
                default: backgroundColor = new Color(0.3f, 0.3f, 0.3f, 0.3f); break;
            }
            var prevColor = GUI.backgroundColor;
            GUI.backgroundColor = backgroundColor;
            EditorGUILayout.BeginVertical(style);
            GUILayout.Label(string.Format("<b>{0}:</b>", message.Role),
                new GUIStyle(EditorStyles.label) { richText = true });

            var contentStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true };
            contentStyle.normal.textColor = contentTextColor;

            // Render ordered content blocks, grouping consecutive tool calls into collapsible batches
            int i = 0;
            while (i < message.Blocks.Count)
            {
                var block = message.Blocks[i];
                if (block.Type == BlockType.Text)
                {
                    if (!string.IsNullOrEmpty(block.Text))
                        GUILayout.Label(ParseMarkdown(block.Text), contentStyle);
                    i++;
                }
                else if (block.Type == BlockType.Tool && block.Tool != null)
                {
                    // Collect consecutive tool blocks into a batch
                    int batchStart = i;
                    while (i < message.Blocks.Count && message.Blocks[i].Type == BlockType.Tool)
                        i++;
                    int batchCount = i - batchStart;

                    // Check if there's text after this batch (meaning the batch is "done")
                    bool hasTextAfter = false;
                    for (int j = i; j < message.Blocks.Count; j++)
                    {
                        if (message.Blocks[j].Type == BlockType.Text && !string.IsNullOrEmpty(message.Blocks[j].Text))
                        { hasTextAfter = true; break; }
                    }

                    // Also consider done if we're not currently processing
                    bool batchDone = hasTextAfter || (!_isProcessing && batchCount > 0);
                    bool allResolved = true;
                    int errorCount = 0;
                    for (int j = batchStart; j < batchStart + batchCount; j++)
                    {
                        var t = message.Blocks[j].Tool;
                        if (t != null && string.IsNullOrEmpty(t.Result)) allResolved = false;
                        if (t != null && t.IsError) errorCount++;
                    }

                    if (batchDone && allResolved && batchCount > 1)
                    {
                        // Render as a collapsible single-line summary
                        DrawToolBatch(message.Blocks, batchStart, batchCount, errorCount);
                    }
                    else
                    {
                        // Still running or single tool — render individually
                        for (int j = batchStart; j < batchStart + batchCount; j++)
                        {
                            if (message.Blocks[j].Tool != null)
                                DrawToolEntry(message.Blocks[j].Tool);
                        }
                    }
                }
                else
                {
                    i++;
                }
            }

            // Live streaming indicator on the current assistant message
            bool isCurrentProcessing = _isProcessing && _messages.Count > 0 &&
                                       message == _messages[_messages.Count - 1] &&
                                       message.Role == "Assistant";
            if (isCurrentProcessing)
            {
                var thinkStyle = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic };
                thinkStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
                GUILayout.Label("Working...", thinkStyle);
            }

            EditorGUILayout.EndVertical();

            // Copy on hover/click
            var messageRect = GUILayoutUtility.GetLastRect();
            var isHovered = messageRect.Contains(Event.current.mousePosition);
            if (isHovered && _copyIcon != null)
            {
                GUI.DrawTexture(new Rect(messageRect.xMax - 20f, messageRect.yMin + 4f, 16f, 16f), _copyIcon);
                Repaint();
            }
            if (isHovered && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                EditorGUIUtility.systemCopyBuffer = message.Content;
                ShowNotification(new GUIContent("Copied to clipboard"), 1.0);
                Event.current.Use();
            }
            GUI.backgroundColor = prevColor;
            GUILayout.Space(5);
        }

        private void DrawToolEntry(ToolEntry tool)
        {
            var displayName = CleanToolName(tool.Name);

            // Build header: "ToolName: description (status)"
            var sb = new StringBuilder(displayName);
            if (!string.IsNullOrEmpty(tool.Description))
                sb.Append(": ").Append(tool.Description);
            if (string.IsNullOrEmpty(tool.Result))
                sb.Append(" (running...)");
            else if (tool.IsError)
                sb.Append(" (error)");
            var header = sb.ToString();

            EditorGUI.indentLevel++;
            tool.IsExpanded = EditorGUILayout.Foldout(tool.IsExpanded, header, true);
            if (tool.IsExpanded && !string.IsNullOrEmpty(tool.Result))
            {
                EditorGUI.indentLevel++;
                var resultStyle = new GUIStyle(EditorStyles.label) { wordWrap = true, fontSize = 10 };
                resultStyle.normal.textColor = tool.IsError
                    ? new Color(1f, 0.4f, 0.4f)
                    : new Color(0.6f, 0.6f, 0.6f);
                var displayResult = tool.Result.Length > 500
                    ? tool.Result.Substring(0, 500) + "..."
                    : tool.Result;
                GUILayout.Label(displayResult, resultStyle);
                EditorGUI.indentLevel--;
            }
            EditorGUI.indentLevel--;
        }

        private void DrawToolBatch(List<MessageBlock> blocks, int start, int count, int errorCount)
        {
            // Use a hash of start index + first tool name as a stable key
            var batchKey = start * 31 + (blocks[start].Tool?.Name?.GetHashCode() ?? 0);
            var isExpanded = _expandedBatches.Contains(batchKey);

            EditorGUI.indentLevel++;
            var batchStyle = new GUIStyle(EditorStyles.foldout);
            batchStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);

            var label = $"{count} tool calls";
            if (errorCount > 0) label += $" ({errorCount} error{(errorCount > 1 ? "s" : "")})";
            var newExpanded = EditorGUILayout.Foldout(isExpanded, label, true, batchStyle);

            if (newExpanded != isExpanded)
            {
                if (newExpanded) _expandedBatches.Add(batchKey);
                else _expandedBatches.Remove(batchKey);
            }

            if (newExpanded)
            {
                for (int j = start; j < start + count; j++)
                {
                    if (blocks[j].Tool != null)
                        DrawToolEntry(blocks[j].Tool);
                }
            }
            EditorGUI.indentLevel--;
        }

        private static string CleanToolName(string name)
        {
            if (name != null && name.StartsWith("mcp__unity__"))
                return name.Substring("mcp__unity__".Length);
            return name ?? "";
        }

        // ── Prompt sending ────────────────────────────────────────────────────

        private void AutoInitialize()
        {
            if (_isProcessing || _sessionId != null || _messages.Count > 0) return;
            if (!McpServer.IsRunning) McpServer.Start();

            var readmePath = Path.Combine(Application.dataPath, "README.md");
            if (!File.Exists(readmePath))
            {
                SendInternalPrompt(
                    "You are Unity Eli, an AI game development assistant embedded in the Unity Editor. " +
                    "This Unity project does not have a README.md yet. " +
                    "Introduce yourself very briefly, then ask the developer a few focused questions to understand their game. " +
                    "Format the questions as a numbered list with bold titles, like:\n" +
                    "1. **Game type** — What genre is this?\n" +
                    "2. **2D or 3D?**\n" +
                    "Ask about: game type/genre, 2D vs 3D, target platforms, core mechanics, and major planned features. " +
                    "Once you have their answers, use your tools to create README.md in the Assets folder.");
            }
            else
            {
                SendInternalPrompt(
                    "You are Unity Eli, an AI game development assistant embedded in the Unity Editor. " +
                    "This project already has a README.md — read it with your tools to understand the project context. " +
                    "Then introduce yourself very briefly and ask the developer what they'd like to work on first.");
            }
        }

        private void SendInternalPrompt(string prompt)
        {
            _isProcessing = true;
            _lastRequestFailed = false;
            _scrollPosition = Vector2.zero;
            SaveState();
            ClaudeCodeProcess.SendMessage(prompt, _sessionId, McpServer.Port);
            Repaint();
        }

        private void SendUserMessage()
        {
            var userText = _inputText.Trim();
            _messages.Add(new DisplayMessage("User", userText));
            _inputText = string.Empty;
            _isProcessing = true; _lastRequestFailed = false; _scrollPosition = Vector2.zero;
            GUI.FocusControl(null);
            SaveState();
            if (!McpServer.IsRunning) McpServer.Start();
            ClaudeCodeProcess.SendMessage(userText, _sessionId, McpServer.Port);
            Repaint();
        }

        private void RetryLastMessage()
        {
            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                if (_messages[i].Role != "User") continue;
                var userText = _messages[i].Content;
                _isProcessing = true; _lastRequestFailed = false;
                SaveState();
                if (!McpServer.IsRunning) McpServer.Start();
                ClaudeCodeProcess.SendMessage(userText, _sessionId, McpServer.Port);
                Repaint(); return;
            }
        }

        private static bool IsEnterKey(Event e)
        {
            return e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter;
        }

        // ── Event handlers ────────────────────────────────────────────────────

        private void HandleStreamEvent(StreamEvent evt)
        {
            switch (evt.Type)
            {
                case "system":
                    if (!string.IsNullOrEmpty(evt.SessionId))
                    {
                        _sessionId = evt.SessionId;
                        SimpleSessionState.SaveSessionId(_sessionId);
                        SessionHistory.ActiveSessionId = _sessionId;
                    }
                    break;
                case "assistant":
                    var msg = GetOrCreateCurrentAssistantMessage();
                    if (!string.IsNullOrEmpty(evt.AssistantText))
                    {
                        msg.AppendText(evt.AssistantText);
                        TryActivateWizard(evt.AssistantText);
                    }
                    if (!string.IsNullOrEmpty(evt.ToolName))
                        msg.AddTool(evt.ToolName, evt.ToolDescription);
                    if (evt.InputTokens > 0)
                        _contextTokens = Math.Min(evt.InputTokens, ContextWindowSize);
                    break;
                case "result":
                    if (!string.IsNullOrEmpty(evt.SessionId))
                    { _sessionId = evt.SessionId; SimpleSessionState.SaveSessionId(_sessionId); }
                    // Don't track tokens from result — it reports aggregate turn usage
                    // (sum of all API calls), not current context window size.
                    break;
                case "error":
                    _messages.Add(new DisplayMessage("Error", evt.ErrorText ?? "Unknown error"));
                    break;
            }
            TrimMessages();
            SimpleSessionState.SaveDisplayMessages(_messages);
            Repaint();
        }

        /// <summary>
        /// Returns the current assistant message being built during this turn,
        /// or creates a new one if none exists yet.
        /// </summary>
        private DisplayMessage GetOrCreateCurrentAssistantMessage()
        {
            if (_isProcessing && _messages.Count > 0)
            {
                var last = _messages[_messages.Count - 1];
                if (last.Role == "Assistant") return last;
            }
            var msg = new DisplayMessage("Assistant");
            _messages.Add(msg);
            return msg;
        }

        private void HandleComplete(bool success, string errorMessage)
        {
            _isProcessing = false; _lastRequestFailed = !success;
            if (!success && !string.IsNullOrEmpty(errorMessage))
                _messages.Add(new DisplayMessage("Error", errorMessage));
            TrimMessages();
            SaveState();

            // Persist to session history so it survives editor restarts
            if (!string.IsNullOrEmpty(_sessionId) && _messages.Count > 0)
            {
                var title = SessionHistory.DeriveTitle(_messages);
                SessionHistory.SaveSession(_sessionId, title, _messages);
                SessionHistory.ActiveSessionId = _sessionId;
            }

            Repaint();
        }

        private void HandleToolExecuted(string toolName, string result, bool isError)
        {
            // Find the first unresolved tool entry in the last assistant message
            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                if (_messages[i].Role != "Assistant") continue;
                var unresolved = _messages[i].FindUnresolvedTool();
                if (unresolved != null)
                {
                    unresolved.Result = result;
                    unresolved.IsError = isError;
                    SimpleSessionState.SaveDisplayMessages(_messages);
                    Repaint();
                    return;
                }
                break; // only check the last assistant message
            }
            // Fallback: no matching entry, append to current assistant message
            var msg = GetOrCreateCurrentAssistantMessage();
            var tool = msg.AddTool(toolName);
            tool.Result = result;
            tool.IsError = isError;
            SimpleSessionState.SaveDisplayMessages(_messages);
            Repaint();
        }

        private void SaveState() { SimpleSessionState.SaveAll(_messages, _sessionId, _isProcessing); }

        // ── Play mode error tracking ──────────────────────────────────────────

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode)
            {
                // About to enter play mode — record current console entry count
                SessionState.SetInt(PlayModeLogStartKey, GetConsoleEntryCount());
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                // Just returned to edit mode — check for errors after a frame
                // (delayCall ensures console is fully flushed)
                EditorApplication.delayCall += CheckPlayModeErrors;
            }
        }

        private void CheckPlayModeErrors()
        {
            // Check the setting first
            if (!UnityEliSettings.PlayModeErrorReport) return;
            // Only send if there's an active session
            if (string.IsNullOrEmpty(_sessionId)) return;
            // Don't interrupt an in-progress request
            if (_isProcessing) return;

            var startIndex = SessionState.GetInt(PlayModeLogStartKey, -1);
            if (startIndex < 0) return;

            var report = BuildPlayModeErrorReport(startIndex);
            if (report == null) return;

            // Show a brief visible message (don't expose the full internal prompt)
            _messages.Add(new DisplayMessage("System", "Analyzing console reports from play mode."));
            SaveState();
            Repaint();

            SendInternalPrompt(report);
        }

        private static string BuildPlayModeErrorReport(int startIndex)
        {
            var unityEditorAssembly = typeof(UnityEditor.Editor).Assembly;

            var logEntriesType = unityEditorAssembly.GetType("UnityEditor.LogEntries")
                              ?? unityEditorAssembly.GetType("UnityEditorInternal.LogEntries");
            if (logEntriesType == null) return null;

            var getCountMethod = logEntriesType.GetMethod("GetCount",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            var startMethod = logEntriesType.GetMethod("StartGettingEntries",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            var endMethod = logEntriesType.GetMethod("EndGettingEntries",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            var getEntryMethod = logEntriesType.GetMethod("GetEntryInternal",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
            if (getCountMethod == null || getEntryMethod == null) return null;

            var logEntryType = unityEditorAssembly.GetType("UnityEditor.LogEntry")
                            ?? unityEditorAssembly.GetType("UnityEditorInternal.LogEntry");
            if (logEntryType == null) return null;

            var messageField = logEntryType.GetField("message",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            var modeField = logEntryType.GetField("mode",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            if (messageField == null) return null;

            int totalCount = (int)getCountMethod.Invoke(null, null);
            if (totalCount <= startIndex) return null; // No new entries

            startMethod?.Invoke(null, null);

            // Collect unique errors and warnings (deduplicated, capped)
            const int errorBits   = 1 | 2 | 8 | 256 | 512 | 2048 | (1 << 21);
            const int warningBits = 128 | (1 << 22);

            var issues = new Dictionary<string, int>(); // message -> occurrence count
            int totalErrors = 0, totalWarnings = 0;

            try
            {
                for (int i = startIndex; i < totalCount; i++)
                {
                    var entry = Activator.CreateInstance(logEntryType);
                    getEntryMethod.Invoke(null, new object[] { i, entry });

                    bool isError = false, isWarning = false;
                    if (modeField != null)
                    {
                        int mode = (int)modeField.GetValue(entry);
                        isError = (mode & errorBits) != 0;
                        isWarning = !isError && (mode & warningBits) != 0;
                    }

                    if (!isError && !isWarning) continue;

                    if (isError) totalErrors++;
                    else totalWarnings++;

                    var message = (string)messageField.GetValue(entry);
                    if (string.IsNullOrEmpty(message)) continue;

                    // Use first line as dedup key (stack traces vary)
                    var newlineIdx = message.IndexOf('\n');
                    var key = newlineIdx > 0 ? message.Substring(0, newlineIdx) : message;
                    if (key.Length > 300) key = key.Substring(0, 300);

                    if (issues.ContainsKey(key))
                        issues[key]++;
                    else if (issues.Count < MaxPlayModeIssues)
                        issues[key] = 1;
                }
            }
            finally
            {
                endMethod?.Invoke(null, null);
            }

            if (issues.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("Play mode just ended. There were runtime issues during the session:");
            sb.AppendLine($"Total: {totalErrors} error(s), {totalWarnings} warning(s).");
            sb.AppendLine();

            foreach (var kvp in issues)
            {
                var suffix = kvp.Value > 1 ? $" (x{kvp.Value})" : "";
                sb.AppendLine($"- {kvp.Key}{suffix}");
            }

            if (issues.Count >= MaxPlayModeIssues && (totalErrors + totalWarnings) > issues.Count)
                sb.AppendLine($"\n({totalErrors + totalWarnings - issues.Count} additional issues not shown.)");

            sb.AppendLine("\nBriefly summarize what went wrong. Do NOT call any tools or investigate further unless the user asks you to.");
            return sb.ToString();
        }

        private static int GetConsoleEntryCount()
        {
            try
            {
                var unityEditorAssembly = typeof(UnityEditor.Editor).Assembly;
                var logEntriesType = unityEditorAssembly.GetType("UnityEditor.LogEntries")
                                  ?? unityEditorAssembly.GetType("UnityEditorInternal.LogEntries");
                if (logEntriesType == null) return 0;

                var getCountMethod = logEntriesType.GetMethod("GetCount",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                if (getCountMethod == null) return 0;

                return (int)getCountMethod.Invoke(null, null);
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Trims the message list if it exceeds <see cref="MaxMessages"/>.
        /// Keeps the first <see cref="MessageKeepPrefix"/> messages (system/init) and the most recent entries.
        /// </summary>
        private void TrimMessages()
        {
            if (_messages.Count <= MaxMessages) return;
            var trimCount = _messages.Count - MaxMessages;
            _messages.RemoveRange(MessageKeepPrefix, trimCount);
        }

        private void LoadIcons()
        {
            var packagePath = "Packages/com.frenchtoastfella.unityeli/Editor";
            _sendIcon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{packagePath}/T_Send.png");
            _copyIcon = AssetDatabase.LoadAssetAtPath<Texture2D>($"{packagePath}/T_Copy.png");
        }

        // ── Markdown renderer ─────────────────────────────────────────────────

        private static string ParseMarkdown(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            var sb = new StringBuilder();
            var lines = text.Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) sb.Append('\n');
                var line = lines[i];
                var trimmed = line.TrimStart();
                var indent = line.Substring(0, line.Length - trimmed.Length);

                if (trimmed.StartsWith("### "))
                    sb.Append(indent + "<b>" + ApplyInlineMarkdown(trimmed.Substring(4)) + "</b>");
                else if (trimmed.StartsWith("## "))
                    sb.Append(indent + "<b>" + ApplyInlineMarkdown(trimmed.Substring(3)) + "</b>");
                else if (trimmed.StartsWith("# "))
                    sb.Append(indent + "<b>" + ApplyInlineMarkdown(trimmed.Substring(2)) + "</b>");
                else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                    sb.Append(indent + "• " + ApplyInlineMarkdown(trimmed.Substring(2)));
                else if (trimmed == "---" || trimmed == "***")
                    sb.Append("────────────────────");
                else
                    sb.Append(indent + ApplyInlineMarkdown(trimmed));
            }

            return sb.ToString();
        }

        private static string ApplyInlineMarkdown(string text)
        {
            // Escape any existing rich text angle brackets before applying tags
            text = text.Replace("<", "\u003C").Replace(">", "\u003E");

            // Bold: **text** or __text__
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<b>$1</b>");
            text = Regex.Replace(text, @"__(.+?)__", "<b>$1</b>");

            // Italic: *text* (single star, not adjacent to another star)
            text = Regex.Replace(text, @"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", "<i>$1</i>");

            // Italic: _text_ (single underscore)
            text = Regex.Replace(text, @"(?<!_)_(?!_)(.+?)(?<!_)_(?!_)", "<i>$1</i>");

            // Inline code: `text`
            text = Regex.Replace(text, @"`([^`]+)`", "<color=#9cdcfe>$1</color>");

            return text;
        }

        private void InitializeStyles()
        {
            if (_inputTextAreaStyle == null)
                _inputTextAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true, padding = new RectOffset(8, 8, 8, 8) };
            if (_sendButtonStyle == null)
                _sendButtonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        }
    }
}
