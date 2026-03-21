using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor
{
    public class SetupWizardWindow : EditorWindow
    {
        private const string SetupCompletedPref = "UnityEli_SetupCompleted";

        private enum Page { Welcome, Prerequisites, Settings, Model, Instructions, Complete }

        private Page _currentPage = Page.Welcome;
        private Vector2 _scrollPosition;

        // ── Prerequisites ────────────────────────────────────────────────────
        private string _installCheckResult;
        private bool _installCheckPassed;
        private bool _loginClicked;

        // ── Settings ─────────────────────────────────────────────────────────
        private int _permissionLevel;
        private int _port;
        private bool _playModeErrorReport;

        // ── Model ────────────────────────────────────────────────────────────
        private string[] _models;
        private int _selectedModelIndex;
        private string _modelFetchStatus;

        // ── Instructions ─────────────────────────────────────────────────────
        private string _instructionsText;

        // ── Styles ───────────────────────────────────────────────────────────
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _bodyStyle;

        public static bool IsSetupCompleted
        {
            get => EditorPrefs.GetBool(SetupCompletedPref, false);
            set => EditorPrefs.SetBool(SetupCompletedPref, value);
        }

        public static void ShowWizard()
        {
            var window = GetWindow<SetupWizardWindow>(true, "Unity Eli Setup");
            window.minSize = new Vector2(600, 500);
            window.maxSize = new Vector2(600, 500);
        }

        private void OnEnable()
        {
            _permissionLevel = (int)UnityEliSettings.ToolPermission;
            _port = UnityEliSettings.McpBasePort;
            _playModeErrorReport = UnityEliSettings.PlayModeErrorReport;

            _models = UnityEliSettings.GetCachedModels();
            if (_models.Length == 0)
            {
                _models = new[] { "claude-opus-4-6", "claude-sonnet-4-6", "claude-haiku-4-5-20251001" };
                UnityEliSettings.SetCachedModels(_models);
            }
            _selectedModelIndex = FindModelIndex(UnityEliSettings.SelectedModelId);

            LoadInstructions();
        }

        private void LoadInstructions()
        {
            var resolved = UnityEliSettings.ResolveInstructionsFilePath();
            if (resolved != null)
            {
                try { _instructionsText = File.ReadAllText(resolved); }
                catch { _instructionsText = UnityEliSettings.InstructionsTemplate; }
            }
            else
            {
                _instructionsText = UnityEliSettings.InstructionsTemplate;
            }
        }

        private int FindModelIndex(string modelId)
        {
            if (string.IsNullOrEmpty(modelId)) return 0;
            for (int i = 0; i < _models.Length; i++)
            {
                if (_models[i] == modelId) return i + 1;
            }
            return 0;
        }

        private void InitStyles()
        {
            if (_titleStyle != null) return;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                margin = new RectOffset(20, 20, 10, 5)
            };

            _subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
                margin = new RectOffset(40, 40, 0, 10)
            };

            _bodyStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = true,
                richText = true,
                margin = new RectOffset(20, 20, 5, 5)
            };
        }

        private void OnGUI()
        {
            InitStyles();

            switch (_currentPage)
            {
                case Page.Welcome:       DrawWelcome(); break;
                case Page.Prerequisites: DrawPrerequisites(); break;
                case Page.Settings:      DrawSettings(); break;
                case Page.Model:         DrawModel(); break;
                case Page.Instructions:  DrawInstructions(); break;
                case Page.Complete:      DrawComplete(); break;
            }
        }

        // ── Welcome ──────────────────────────────────────────────────────────

        private void DrawWelcome()
        {
            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Welcome to Unity Eli", _titleStyle);
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField(
                "Thank you for giving Unity Eli a chance.\n\n" +
                "This wizard will guide you through the rest of the\n" +
                "setup process.",
                _subtitleStyle);

            GUILayout.FlexibleSpace();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Skip", GUILayout.Width(100), GUILayout.Height(30)))
            {
                CompleteSetup(false);
            }
            GUILayout.Space(10);
            if (GUILayout.Button("Begin Setup", GUILayout.Width(140), GUILayout.Height(30)))
            {
                _currentPage = Page.Prerequisites;
            }
            GUILayout.Space(20);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(20);
        }

        // ── Prerequisites ────────────────────────────────────────────────────

        private void DrawPrerequisites()
        {
            DrawStepHeader("Step 1 of 4", "Prerequisites");
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField(
                "Unity Eli uses Claude Code CLI as its AI backend. " +
                "No API key required \u2014 it uses your Claude Code subscription.",
                _bodyStyle);

            EditorGUILayout.Space(15);

            // ── Installation check ───────────────────────────────────────────
            EditorGUILayout.LabelField("1. Verify Claude Code is installed", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Check Installation", GUILayout.Width(160)))
            {
                _installCheckResult = UnityEliSettings.CheckClaudeVersion();
                _installCheckPassed = _installCheckResult != null &&
                                      !_installCheckResult.StartsWith("ERROR");
            }
            if (!string.IsNullOrEmpty(_installCheckResult))
            {
                var color = _installCheckPassed
                    ? new Color(0.2f, 0.8f, 0.3f)
                    : new Color(0.9f, 0.2f, 0.2f);
                var style = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = color },
                    fontStyle = FontStyle.Bold
                };
                GUILayout.Label(_installCheckResult, style);
            }
            EditorGUILayout.EndHorizontal();

            if (!_installCheckPassed && string.IsNullOrEmpty(_installCheckResult))
            {
                EditorGUILayout.HelpBox(
                    "Install Claude Code CLI:\n  npm install -g @anthropic-ai/claude-code",
                    MessageType.Info);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);

            // ── Login ────────────────────────────────────────────────────────
            EditorGUILayout.LabelField("2. Authenticate with Claude", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Open Claude Login", GUILayout.Width(160)))
            {
                UnityEliSettings.RunClaudeLogin();
                _loginClicked = true;
            }
            if (_loginClicked)
            {
                var style = new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(0.2f, 0.8f, 0.3f) },
                    fontStyle = FontStyle.Bold
                };
                GUILayout.Label("Login terminal opened", style);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "This opens a terminal where you can log in to Claude.\n" +
                "Complete the login there, then continue here.",
                MessageType.Info);

            EditorGUI.indentLevel--;

            GUILayout.FlexibleSpace();
            DrawNavigation(true, _installCheckPassed && _loginClicked);
        }

        // ── Settings ─────────────────────────────────────────────────────────

        private static readonly string[] PermissionLabels =
        {
            "MCP Tools Only (most restrictive)",
            "File Operations (recommended)",
            "All Tools incl. Shell (most permissive)",
        };

        private static readonly string[] PermissionDescriptions =
        {
            "Claude can only use MCP unity tools. It cannot read or write project files directly.",
            "Claude can read, write, and edit project files via MCP and built-in file tools. " +
                "Recommended for most users.",
            "Claude can use all tools including shell commands. Use with caution.",
        };

        private void DrawSettings()
        {
            DrawStepHeader("Step 2 of 4", "Settings");
            EditorGUILayout.Space(10);

            // Permissions
            EditorGUILayout.LabelField("Tool Permissions", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _permissionLevel = EditorGUILayout.Popup("Permission Level", _permissionLevel, PermissionLabels);
            EditorGUILayout.HelpBox(PermissionDescriptions[_permissionLevel], MessageType.Info);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // Port
            EditorGUILayout.LabelField("MCP Server Port", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _port = EditorGUILayout.IntField("Base Port", _port);
            EditorGUILayout.HelpBox(
                "The MCP server listens on this port for tool calls from Claude Code. " +
                "If the port is busy, the next available port in range is used automatically.",
                MessageType.Info);
            EditorGUI.indentLevel--;

            EditorGUILayout.Space(10);

            // Play mode error reporting
            EditorGUILayout.LabelField("Behaviour", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;
            _playModeErrorReport = EditorGUILayout.Toggle(
                new GUIContent("Report Play Mode Errors"),
                _playModeErrorReport);
            EditorGUILayout.HelpBox(
                "When enabled, Eli automatically receives a summary of runtime errors and " +
                "warnings after Play mode ends, so it can help you fix issues without " +
                "you having to copy-paste console output.",
                MessageType.Info);
            EditorGUI.indentLevel--;

            GUILayout.FlexibleSpace();
            DrawNavigation(true, true);
        }

        // ── Model ────────────────────────────────────────────────────────────

        private void DrawModel()
        {
            DrawStepHeader("Step 3 of 4", "Agent Model");
            EditorGUILayout.Space(10);

            EditorGUILayout.LabelField(
                "Select the Claude model that Eli will use. " +
                "Leave as Default to use whatever model your Claude Code subscription provides.",
                _bodyStyle);

            EditorGUILayout.Space(10);

            var options = new List<string> { "Default (subscription default)" };
            foreach (var m in _models)
                options.Add(m);

            _selectedModelIndex = EditorGUILayout.Popup("Model", _selectedModelIndex, options.ToArray());

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Models", GUILayout.Width(130)))
            {
                var fetched = UnityEliSettings.FetchAvailableModels();
                if (fetched.Length > 0)
                {
                    _models = fetched;
                    UnityEliSettings.SetCachedModels(_models);
                    _modelFetchStatus = $"Found {fetched.Length} model(s).";
                }
                else
                {
                    _modelFetchStatus = "Could not fetch models. Using built-in list.";
                }
            }
            if (!string.IsNullOrEmpty(_modelFetchStatus))
                EditorGUILayout.LabelField(_modelFetchStatus, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
            DrawNavigation(true, true);
        }

        // ── Instructions ─────────────────────────────────────────────────────

        private void DrawInstructions()
        {
            DrawStepHeader("Step 4 of 4", "Project Instructions");
            EditorGUILayout.Space(5);

            EditorGUILayout.LabelField(
                "These instructions are appended to Claude's system prompt. " +
                "Customize them with your project's naming conventions, coding standards, and rules.",
                _bodyStyle);

            EditorGUILayout.Space(5);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.ExpandHeight(true));
            _instructionsText = EditorGUILayout.TextArea(_instructionsText, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();

            GUILayout.Space(5);
            DrawNavigation(true, true);
        }

        // ── Complete ─────────────────────────────────────────────────────────

        private void DrawComplete()
        {
            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("You're all set!", _titleStyle);
            EditorGUILayout.Space(20);
            EditorGUILayout.LabelField(
                "Unity Eli is configured and ready to use.\n\n" +
                "Open the Unity Eli window from the menu to start\n" +
                "working with your AI-powered development assistant.\n\n" +
                "You can change any of these settings later in\n" +
                "Preferences \u25B8 Unity Eli.",
                _subtitleStyle);

            EditorGUILayout.Space(25);

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Open Unity Eli", GUILayout.Width(180), GUILayout.Height(35)))
            {
                CompleteSetup(true);
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();
        }

        // ── Shared UI helpers ────────────────────────────────────────────────

        private void DrawStepHeader(string step, string title)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField(step, _subtitleStyle);
            EditorGUILayout.LabelField(title, _titleStyle);
        }

        private void DrawNavigation(bool showBack, bool nextEnabled)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (showBack)
            {
                if (GUILayout.Button("Back", GUILayout.Width(80), GUILayout.Height(28)))
                {
                    _currentPage = (Page)((int)_currentPage - 1);
                }
            }

            GUILayout.Space(10);

            var label = _currentPage == Page.Instructions ? "Finish" : "Next";
            EditorGUI.BeginDisabledGroup(!nextEnabled);
            if (GUILayout.Button(label, GUILayout.Width(80), GUILayout.Height(28)))
            {
                ApplyCurrentPage();
                _currentPage = (Page)((int)_currentPage + 1);
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(20);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(15);
        }

        private void ApplyCurrentPage()
        {
            switch (_currentPage)
            {
                case Page.Settings:
                    UnityEliSettings.ToolPermission = (ToolPermissionLevel)_permissionLevel;
                    if (_port > 1024 && _port < 65535)
                        UnityEliSettings.McpBasePort = _port;
                    UnityEliSettings.PlayModeErrorReport = _playModeErrorReport;
                    break;

                case Page.Model:
                    UnityEliSettings.SelectedModelId =
                        _selectedModelIndex > 0 && _selectedModelIndex <= _models.Length
                            ? _models[_selectedModelIndex - 1]
                            : "";
                    break;

                case Page.Instructions:
                    SaveInstructions();
                    break;
            }
        }

        private void SaveInstructions()
        {
            var relativePath = UnityEliSettings.InstructionsFilePath;
            if (string.IsNullOrWhiteSpace(relativePath)) return;

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var absolutePath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));

            try
            {
                File.WriteAllText(absolutePath, _instructionsText, new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Debug.LogError($"[Unity Eli] Failed to save instructions: {e.Message}");
            }
        }

        private void CompleteSetup(bool openWindow)
        {
            IsSetupCompleted = true;
            Close();
            if (openWindow)
                UnityEliWindow.ShowWindow();
        }
    }
}
