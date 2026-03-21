using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityEli.Editor
{
    /// <summary>
    /// Controls which Claude Code tools are pre-allowed (no permission prompt).
    /// Since Claude Code runs non-interactively, tools not in this list will silently fail.
    /// </summary>
    public enum ToolPermissionLevel
    {
        /// <summary>Only MCP unity tools (most restrictive — Claude cannot read/write files).</summary>
        McpOnly = 0,
        /// <summary>MCP tools + file operations (Read, Write, Edit, Glob, Grep). Recommended default.</summary>
        FileOperations = 1,
        /// <summary>All tools including Bash shell commands (most permissive).</summary>
        AllTools = 2,
    }

    public static class UnityEliSettings
    {
        private const string McpBasePortPref = "UnityEli_McpBasePort";
        private const string SelectedModelIdPref = "UnityEli_SelectedModelId";
        private const string CachedModelsPref = "UnityEli_CachedModels";
        private const string ToolPermissionPref = "UnityEli_ToolPermission";
        private const string InstructionsFilePathPref = "UnityEli_InstructionsFilePath";
        private const string PlayModeErrorReportPref = "UnityEli_PlayModeErrorReport";

        /// <summary>
        /// Base port for the MCP HTTP server. If this port is taken, subsequent ports are tried.
        /// </summary>
        public static int McpBasePort
        {
            get => EditorPrefs.GetInt(McpBasePortPref, 47880);
            set => EditorPrefs.SetInt(McpBasePortPref, value);
        }

        /// <summary>
        /// Optional Claude model ID to pass to Claude Code via --model flag.
        /// Leave empty to use Claude Code's default model.
        /// </summary>
        public static string SelectedModelId
        {
            get => EditorPrefs.GetString(SelectedModelIdPref, "");
            set => EditorPrefs.SetString(SelectedModelIdPref, value);
        }

        /// <summary>
        /// Which tools to pre-allow for Claude Code. Defaults to FileOperations.
        /// </summary>
        public static ToolPermissionLevel ToolPermission
        {
            get => (ToolPermissionLevel)EditorPrefs.GetInt(ToolPermissionPref, (int)ToolPermissionLevel.FileOperations);
            set => EditorPrefs.SetInt(ToolPermissionPref, (int)value);
        }

        /// <summary>
        /// Whether to automatically report runtime errors/warnings to Eli when Play mode ends.
        /// </summary>
        public static bool PlayModeErrorReport
        {
            get => EditorPrefs.GetBool(PlayModeErrorReportPref, true);
            set => EditorPrefs.SetBool(PlayModeErrorReportPref, value);
        }

        /// <summary>
        /// Path to a project-specific instructions file, relative to the project root.
        /// Contents are appended to Claude's system prompt via --append-system-prompt-file.
        /// </summary>
        public static string InstructionsFilePath
        {
            get => EditorPrefs.GetString(InstructionsFilePathPref, "Assets/UnityEli/.eli-instructions");
            set => EditorPrefs.SetString(InstructionsFilePathPref, value);
        }

        /// <summary>
        /// Returns the absolute path to the instructions file if it exists, or null otherwise.
        /// </summary>
        public static string ResolveInstructionsFilePath()
        {
            var relativePath = InstructionsFilePath;
            if (string.IsNullOrWhiteSpace(relativePath)) return null;

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var absolutePath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));

            return File.Exists(absolutePath) ? absolutePath : null;
        }

        /// <summary>
        /// Returns the value for the --allowedTools argument based on the current permission level.
        /// Uses comma-separated patterns in a single flag for CLI compatibility.
        /// </summary>
        public static string BuildAllowedToolsArgs()
        {
            switch (ToolPermission)
            {
                case ToolPermissionLevel.McpOnly:
                    return "\"mcp__unity__*\"";
                case ToolPermissionLevel.AllTools:
                    return "\"mcp__unity__*,Read,Write,Edit,Glob,Grep,Bash\"";
                case ToolPermissionLevel.FileOperations:
                default:
                    return "\"mcp__unity__*,Read,Write,Edit,Glob,Grep\"";
            }
        }

        // ── Instructions template ────────────────────────────────────────────

        internal static readonly string InstructionsTemplate =
@"# Project Instructions for Unity Eli
# Edit these rules to match your project. Delete anything that doesn't apply.

## Restrictions
- Do not modify files under Assets/UnityEli/
- Do not edit files in Library/, Temp/, obj/, or .vs/ folders

## Asset Naming Conventions
- Materials: M_Name (e.g., M_BrickWall, M_Character_Skin)
- Textures: T_Name_Suffix (e.g., T_BrickWall_AL for albedo, T_BrickWall_N for normal)
  - Suffixes: _AL (Albedo), _N (Normal), _MT (Metallic), _R (Roughness), _AO (Ambient Occlusion), _EM (Emission), _H (Height), _M (Mask), _SP (Specular)
- Meshes: SM_Name (e.g., SM_Rock_Large)
- Skeletal Meshes: SK_Name (e.g., SK_Character)
- Prefabs: P_Name (e.g., P_Enemy_Goblin)
- Animations: A_Name_Action (e.g., A_Character_Idle, A_Character_Run)
- Animator Controllers: AC_Name (e.g., AC_Character)
- Audio Clips: SFX_Name or MUS_Name (e.g., SFX_Explosion, MUS_MainTheme)
- ScriptableObjects: SO_Name (e.g., SO_WeaponStats)
- Shaders: SH_Name (e.g., SH_Toon, SH_Water)
- Sprites: SPR_Name (e.g., SPR_Icon_Health)
- Use PascalCase for all asset names. No spaces — use underscores as separators.

## C# Coding Conventions
- PascalCase for public members, methods, classes, namespaces, and properties
- camelCase for private/local variables and parameters
- Prefix private fields with underscore: _myField
- Prefix interfaces with I: IInteractable, IDamageable
- Suffix events/actions with callback pattern: OnDeath, OnHealthChanged
- Always use explicit access modifiers (write 'private' even though it's default)
- One class per file; filename must match the class name
- Always use braces for if/for/while, even single-line bodies
- Opening braces on their own line (Allman style)

## Namespaces & Organization
- Put all scripts in namespaces (e.g., MyGame.Core, MyGame.UI, MyGame.Enemies)
- Use Assembly Definitions for major folders to speed up compilation
- Group scripts by feature, not by type (e.g., Enemies/ not Scripts/MonoBehaviours/)

## Performance
- Cache GetComponent<T>() results — never call GetComponent in Update, FixedUpdate, or LateUpdate
- Avoid GameObject.Find() and FindObjectOfType() at runtime; cache references in Awake/Start or use dependency injection
- Use object pooling (UnityEngine.Pool.ObjectPool<T>) for frequently spawned/destroyed objects (bullets, particles, enemies)
- Use CompareTag(""tag"") instead of == ""tag"" for tag comparisons (avoids GC allocation)
- Prefer TryGetComponent<T>(out var c) over GetComponent<T>() + null check
- Avoid string concatenation in hot paths — use StringBuilder or interpolated strings
- Use NonAlloc physics queries (RaycastNonAlloc, OverlapSphereNonAlloc)
- Avoid LINQ in Update loops (causes GC allocations)

## Architecture
- Use ScriptableObjects for shared configuration data and game settings
- Use events (System.Action, UnityEvent) for decoupled communication between systems
- Use [SerializeField] for private fields that need Inspector access (never make fields public just for the Inspector)
- Prefer composition over inheritance for game entity behaviors
- Keep MonoBehaviour scripts focused — split large classes into components
";

        // ── CLI helpers ──────────────────────────────────────────────────────

        internal static string CheckClaudeVersion()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

#if UNITY_EDITOR_WIN
                psi.FileName = "cmd.exe";
                psi.Arguments = "/c claude --version";
#else
                psi.FileName = "claude";
                psi.Arguments = "--version";
#endif

                using (var p = Process.Start(psi))
                {
                    var output = p.StandardOutput.ReadToEnd().Trim();
                    var error = p.StandardError.ReadToEnd().Trim();
                    p.WaitForExit(3000);

                    if (!string.IsNullOrEmpty(output)) return $"Claude Code {output}";
                    if (!string.IsNullOrEmpty(error)) return $"ERROR: {error}";
                    return "ERROR: Could not determine version.";
                }
            }
            catch (Exception e)
            {
                return $"ERROR: Claude Code not found in PATH.\n{e.Message}";
            }
        }

        internal static void RunClaudeLogin()
        {
            try
            {
#if UNITY_EDITOR_WIN
                Process.Start(new ProcessStartInfo("cmd.exe", "/c start cmd /k claude auth login") { UseShellExecute = true });
#elif UNITY_EDITOR_OSX
                Process.Start(new ProcessStartInfo("open", "-a Terminal --args claude auth login") { UseShellExecute = true });
#else
                Process.Start(new ProcessStartInfo("xterm", "-e claude auth login") { UseShellExecute = true });
#endif
            }
            catch (Exception e)
            {
                Debug.LogError($"[Unity Eli] Could not open terminal for claude login: {e.Message}");
            }
        }

        /// <summary>
        /// Creates the .eli-instructions template file at the given relative path.
        /// </summary>
        internal static void CreateInstructionsFile(string relativePath, string content = null)
        {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var absolutePath = Path.GetFullPath(Path.Combine(projectRoot, relativePath));

            try
            {
                File.WriteAllText(absolutePath, content ?? InstructionsTemplate, new UTF8Encoding(false));
                Debug.Log($"[Unity Eli] Created instructions file: {absolutePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[Unity Eli] Failed to create instructions file: {e.Message}");
            }
        }

        // ── Uninstall ────────────────────────────────────────────────────────

        /// <summary>All EditorPrefs keys used by Unity Eli.</summary>
        private static readonly string[] AllEditorPrefsKeys =
        {
            McpBasePortPref, SelectedModelIdPref, CachedModelsPref,
            ToolPermissionPref, InstructionsFilePathPref, PlayModeErrorReportPref,
            "UnityEli_SessionIndex", "UnityEli_ActiveSessionId",
            "UnityEli_SetupCompleted",
        };

        /// <summary>
        /// Shows a confirmation dialog and, if confirmed, removes all Unity Eli files,
        /// preferences, and the package folder itself.
        /// </summary>
        internal static void Uninstall()
        {
            // Confirm uninstall
            if (!EditorUtility.DisplayDialog(
                "Uninstall Unity Eli",
                "This will:\n\n" +
                "\u2022 Stop the MCP server and Claude process\n" +
                "\u2022 Deregister the MCP server from Claude CLI\n" +
                "\u2022 Remove .mcp.json from the project root\n" +
                "\u2022 Delete chat history (Library/UnityEli/)\n" +
                "\u2022 Clear all Unity Eli preferences\n" +
                "\u2022 Delete temp files\n" +
                "\u2022 Delete the Assets/UnityEli/ folder (includes .eli-instructions)\n\n" +
                "This cannot be undone.",
                "Uninstall", "Cancel"))
            {
                return;
            }

            Debug.Log("[Unity Eli] Uninstalling...");

            // 1. Stop running services
            McpServer.Stop();
            ClaudeCodeProcess.Cancel();

            // 2. Deregister MCP server from Claude CLI
            try { RunClaudeCommand("mcp remove unity"); } catch { }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            // 3. Delete .mcp.json
            TryDeleteFile(Path.Combine(projectRoot, ".mcp.json"));

            // 4. Delete .eli-instructions if stored outside Assets/UnityEli/
            var instructionsPath = ResolveInstructionsFilePath();
            if (instructionsPath != null)
            {
                var unityEliDir = Path.GetFullPath(Path.Combine(Application.dataPath, "UnityEli"));
                if (!instructionsPath.StartsWith(unityEliDir))
                    TryDeleteFile(instructionsPath);
            }

            // 5. Delete Library/UnityEli/
            var libraryDir = Path.Combine(projectRoot, "Library", "UnityEli");
            try
            {
                if (Directory.Exists(libraryDir))
                    Directory.Delete(libraryDir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Unity Eli] Could not delete {libraryDir}: {e.Message}");
            }

            // 6. Delete temp files
            try
            {
                var tempDir = Application.temporaryCachePath;
                foreach (var f in Directory.GetFiles(tempDir, "unity-eli-*"))
                    TryDeleteFile(f);
                TryDeleteFile(Path.Combine(tempDir, "eli-screenshot.png"));
            }
            catch { }

            // 7. Clear all EditorPrefs
            foreach (var key in AllEditorPrefsKeys)
                EditorPrefs.DeleteKey(key);

            // 8. Clear SessionState
            SimpleSessionState.Clear();

            // 9. Delete Assets/UnityEli/ (triggers domain reload)
            Debug.Log("[Unity Eli] Removing package folder...");
            AssetDatabase.DeleteAsset("Assets/UnityEli");
            AssetDatabase.Refresh();

            Debug.Log("[Unity Eli] Uninstall complete.");
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { }
        }

        // ── Model list ──────────────────────────────────────────────────────

        public static string[] GetCachedModels()
        {
            var data = EditorPrefs.GetString(CachedModelsPref, "");
            if (string.IsNullOrEmpty(data)) return new string[0];
            return data.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        }

        public static void SetCachedModels(string[] models)
        {
            EditorPrefs.SetString(CachedModelsPref,
                models != null && models.Length > 0 ? string.Join("\n", models) : "");
        }

        private static readonly string[] KnownModels =
        {
            "claude-opus-4-6",
            "claude-sonnet-4-6",
            "claude-haiku-4-5-20251001",
        };

        /// <summary>
        /// Fetches the list of available models from the Claude CLI.
        /// Tries known CLI subcommands first, falls back to a built-in list.
        /// </summary>
        public static string[] FetchAvailableModels()
        {
            string[] cliCommands = { "model list", "models list", "models" };
            foreach (var cmd in cliCommands)
            {
                try
                {
                    var output = RunClaudeCommand(cmd);
                    if (!string.IsNullOrEmpty(output))
                    {
                        var models = ParseModelsOutput(output);
                        if (models.Length > 0) return models;
                    }
                }
                catch
                {
                    // command not supported, try next
                }
            }

            // CLI model listing commands aren't supported in current Claude Code versions.
            // Silently fall back to the built-in model list.
            return (string[])KnownModels.Clone();
        }

        /// <summary>
        /// Refreshes the cached model list from the CLI.
        /// Returns true if new models (not previously cached) were found.
        /// </summary>
        public static bool RefreshModels()
        {
            var newModels = FetchAvailableModels();
            if (newModels.Length == 0) return false;

            var oldModels = GetCachedModels();
            SetCachedModels(newModels);

            if (oldModels.Length == 0) return true;

            var oldSet = new HashSet<string>(oldModels);
            foreach (var m in newModels)
            {
                if (!oldSet.Contains(m)) return true;
            }
            return false;
        }

        // ── Background model refresh ─────────────────────────────────────────

        /// <summary>True while a background model refresh is in progress.</summary>
        public static bool IsRefreshingModels => _refreshThread != null && _refreshThread.IsAlive;

        private static Thread _refreshThread;
        private static string[] _bgFetchedModels;
        private static volatile bool _bgRefreshDone;

        /// <summary>
        /// Starts fetching models on a background thread so the editor is not blocked.
        /// Poll <see cref="IsRefreshingModels"/> and call <see cref="FinishBackgroundRefresh"/>
        /// from the main thread once it returns false.
        /// </summary>
        public static void RefreshModelsInBackground()
        {
            if (IsRefreshingModels) return;
            _bgRefreshDone = false;
            _bgFetchedModels = null;
            _refreshThread = new Thread(() =>
            {
                try { _bgFetchedModels = FetchAvailableModels(); }
                catch { _bgFetchedModels = null; }
                finally { _bgRefreshDone = true; }
            })
            { IsBackground = true, Name = "UnityEli_ModelRefresh" };
            _refreshThread.Start();
        }

        /// <summary>
        /// Call from the main thread after <see cref="IsRefreshingModels"/> becomes false.
        /// Applies the fetched models to the cache and returns true if new models were found.
        /// </summary>
        public static bool FinishBackgroundRefresh()
        {
            if (!_bgRefreshDone) return false;
            _refreshThread = null;

            var newModels = _bgFetchedModels ?? Array.Empty<string>();
            _bgFetchedModels = null;
            _bgRefreshDone = false;

            if (newModels.Length == 0) return false;

            var oldModels = GetCachedModels();
            SetCachedModels(newModels);

            if (oldModels.Length == 0) return true;

            var oldSet = new HashSet<string>(oldModels);
            foreach (var m in newModels)
            {
                if (!oldSet.Contains(m)) return true;
            }
            return false;
        }

        private static string RunClaudeCommand(string args)
        {
            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true
            };

#if UNITY_EDITOR_WIN
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c claude {args}";
#else
            psi.FileName = "claude";
            psi.Arguments = args;
#endif

            using (var p = Process.Start(psi))
            {
                // Close stdin immediately to prevent interactive mode
                p.StandardInput.Close();

                var output = p.StandardOutput.ReadToEnd().Trim();

                if (!p.WaitForExit(5000))
                {
                    try { p.Kill(); } catch { }
                    return null;
                }

                return p.ExitCode == 0 ? output : null;
            }
        }

        private static string[] ParseModelsOutput(string output)
        {
            var models = new List<string>();
            var lines = output.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();

                // Remove common list markers
                if (line.StartsWith("- ")) line = line.Substring(2).Trim();
                else if (line.StartsWith("* ")) line = line.Substring(2).Trim();

                if (string.IsNullOrEmpty(line)) continue;
                if (line.EndsWith(":")) continue; // header line

                // Take first token if line contains whitespace (model ID may be followed by description)
                var spaceIdx = line.IndexOf(' ');
                var token = spaceIdx >= 0 ? line.Substring(0, spaceIdx) : line;

                if (LooksLikeModelId(token))
                    models.Add(token);
            }
            return models.ToArray();
        }

        private static bool LooksLikeModelId(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 5) return false;

            bool hasHyphen = false;
            bool hasDigit = false;
            foreach (var c in s)
            {
                if (c == '-') hasHyphen = true;
                else if (char.IsDigit(c)) hasDigit = true;
                else if (!char.IsLetter(c) && c != '_' && c != '.' && c != ':')
                    return false;
            }

            // Real model IDs like "claude-sonnet-4-6" have hyphens and digits
            return hasHyphen && hasDigit;
        }
    }

    public class UnityEliSettingsProvider : SettingsProvider
    {
        private const string SettingsPath = "Preferences/Unity Eli";

        private string _claudeVersionOutput = "";
        private bool _checkedVersion;
        private string _fetchStatus = "";

        public UnityEliSettingsProvider(string path, SettingsScope scope)
            : base(path, scope) { }

        public override void OnActivate(string searchContext, UnityEngine.UIElements.VisualElement rootElement)
        {
            _checkedVersion = false;
            _claudeVersionOutput = "";
            _fetchStatus = "";
        }

        public override void OnGUI(string searchContext)
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Unity Eli Settings", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            // Claude Code status
            EditorGUILayout.LabelField("Claude Code CLI", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                "Unity Eli uses Claude Code as its AI backend. No API key required - " +
                "it uses your Claude Code subscription.\n\n" +
                "Install: npm install -g @anthropic-ai/claude-code\n" +
                "Login:   claude login",
                MessageType.Info);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Check Installation", GUILayout.Width(160)))
            {
                _claudeVersionOutput = UnityEliSettings.CheckClaudeVersion();
                _checkedVersion = true;
            }
            if (GUILayout.Button("Open Claude Login", GUILayout.Width(160)))
            {
                UnityEliSettings.RunClaudeLogin();
            }
            EditorGUILayout.EndHorizontal();

            if (_checkedVersion && !string.IsNullOrEmpty(_claudeVersionOutput))
            {
                var msgType = _claudeVersionOutput.StartsWith("ERROR") ? MessageType.Error : MessageType.Info;
                EditorGUILayout.HelpBox(_claudeVersionOutput, msgType);
            }

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);

            // MCP Server
            EditorGUILayout.LabelField("MCP Server", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var newPort = EditorGUILayout.IntField("Base Port", UnityEliSettings.McpBasePort);
            if (newPort != UnityEliSettings.McpBasePort && newPort > 1024 && newPort < 65535)
                UnityEliSettings.McpBasePort = newPort;

            if (McpServer.IsRunning)
                EditorGUILayout.HelpBox($"MCP server running on port {McpServer.Port}.", MessageType.Info);
            else
                EditorGUILayout.HelpBox("MCP server not running. Open the Unity Eli window to start it.", MessageType.Warning);

            EditorGUI.indentLevel--;
            EditorGUILayout.Space(10);

            // Permissions
            DrawPermissionsSection();
            EditorGUILayout.Space(10);

            // Behaviour
            DrawBehaviourSection();
            EditorGUILayout.Space(10);

            // Project Instructions
            DrawInstructionsSection();
            EditorGUILayout.Space(10);

            // Model selection
            DrawModelSection();
            EditorGUILayout.Space(20);

            // Uninstall
            DrawUninstallSection();
        }

        private static readonly string[] PermissionLabels =
        {
            "MCP Tools Only (most restrictive)",
            "File Operations (recommended)",
            "All Tools incl. Shell (most permissive)",
        };

        private void DrawPermissionsSection()
        {
            EditorGUILayout.LabelField("Tool Permissions", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var current = (int)UnityEliSettings.ToolPermission;
            var newVal = EditorGUILayout.Popup("Permission Level", current, PermissionLabels);
            if (newVal != current)
                UnityEliSettings.ToolPermission = (ToolPermissionLevel)newVal;

            string description;
            switch (UnityEliSettings.ToolPermission)
            {
                case ToolPermissionLevel.McpOnly:
                    description = "Claude can only use MCP unity tools. It cannot read or write project files directly.";
                    break;
                case ToolPermissionLevel.AllTools:
                    description = "Claude can use all tools including shell commands. Use with caution.";
                    break;
                default:
                    description = "Claude can read, write, and edit project files via MCP and built-in file tools.";
                    break;
            }
            EditorGUILayout.HelpBox(description, MessageType.Info);
            EditorGUILayout.HelpBox(
                "Changes take effect on the next message. If Claude is still blocked, " +
                "click Clear in the Unity Eli window to start a fresh session.",
                MessageType.None);

            EditorGUI.indentLevel--;
        }

        private static void DrawBehaviourSection()
        {
            EditorGUILayout.LabelField("Behaviour", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var playModeReport = EditorGUILayout.Toggle(
                new GUIContent("Report Play Mode Errors",
                    "When enabled, Eli automatically receives a summary of runtime errors and warnings after Play mode ends."),
                UnityEliSettings.PlayModeErrorReport);
            if (playModeReport != UnityEliSettings.PlayModeErrorReport)
                UnityEliSettings.PlayModeErrorReport = playModeReport;

            EditorGUI.indentLevel--;
        }

        private void DrawInstructionsSection()
        {
            EditorGUILayout.LabelField("Project Instructions", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                "Provide project-specific instructions appended to Claude's system prompt. " +
                "Use this for naming conventions, coding standards, and project rules.\n\n" +
                "The file path is relative to the project root.",
                MessageType.Info);

            var currentPath = UnityEliSettings.InstructionsFilePath;
            var newPath = EditorGUILayout.TextField("File Path", currentPath);
            if (newPath != currentPath)
                UnityEliSettings.InstructionsFilePath = newPath;

            var resolvedPath = UnityEliSettings.ResolveInstructionsFilePath();

            if (string.IsNullOrWhiteSpace(newPath))
            {
                EditorGUILayout.HelpBox("Instructions disabled (empty path).", MessageType.None);
            }
            else if (resolvedPath != null)
            {
                EditorGUILayout.HelpBox("Instructions file found. Contents will be appended to Claude's system prompt.", MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "File not found. Click 'Create Template' to generate a starter file.",
                    MessageType.Warning);
            }

            EditorGUILayout.BeginHorizontal();

            if (resolvedPath != null)
            {
                if (GUILayout.Button("Open in Editor", GUILayout.Width(120)))
                {
                    System.Diagnostics.Process.Start(resolvedPath);
                }
            }

            if (resolvedPath == null && !string.IsNullOrWhiteSpace(newPath))
            {
                if (GUILayout.Button("Create Template", GUILayout.Width(120)))
                {
                    UnityEliSettings.CreateInstructionsFile(newPath);
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUI.indentLevel--;
        }

        private void DrawModelSection()
        {
            EditorGUILayout.LabelField("Model", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var cachedModels = UnityEliSettings.GetCachedModels();
            var currentModel = UnityEliSettings.SelectedModelId;

            // Build dropdown options: "Default" + cached models (+ current if not in list)
            var options = new List<string> { "Default (no override)" };
            var modelIds = new List<string> { "" };

            foreach (var m in cachedModels)
            {
                options.Add(m);
                modelIds.Add(m);
            }

            // If the current selection isn't in the cached list, preserve it as an option
            int currentIndex = 0;
            if (!string.IsNullOrEmpty(currentModel))
            {
                var idx = modelIds.IndexOf(currentModel);
                if (idx >= 0)
                {
                    currentIndex = idx;
                }
                else
                {
                    options.Add(currentModel + " (custom)");
                    modelIds.Add(currentModel);
                    currentIndex = options.Count - 1;
                }
            }

            var newIndex = EditorGUILayout.Popup("Model", currentIndex, options.ToArray());
            if (newIndex != currentIndex)
                UnityEliSettings.SelectedModelId = modelIds[newIndex];

            // Refresh button + status
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Models", GUILayout.Width(130)))
            {
                var models = UnityEliSettings.FetchAvailableModels();
                if (models.Length > 0)
                {
                    UnityEliSettings.SetCachedModels(models);
                    _fetchStatus = $"Found {models.Length} model(s).";
                }
                else
                {
                    _fetchStatus = "No models found. Is Claude CLI installed and logged in?";
                }
            }
            if (!string.IsNullOrEmpty(_fetchStatus))
                EditorGUILayout.LabelField(_fetchStatus, EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();

            if (cachedModels.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No models loaded yet. Click 'Refresh Models' or open the Unity Eli window to auto-fetch.",
                    MessageType.Info);
            }

            EditorGUI.indentLevel--;
        }

        private static void DrawUninstallSection()
        {
            EditorGUILayout.LabelField("Uninstall", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUILayout.HelpBox(
                "Completely remove Unity Eli from this project, including all preferences, " +
                "chat history, temp files, and the package folder.",
                MessageType.None);

            if (GUILayout.Button("Uninstall Unity Eli", GUILayout.Width(180)))
            {
                UnityEliSettings.Uninstall();
            }

            EditorGUI.indentLevel--;
        }

        [SettingsProvider]
        public static SettingsProvider CreateSettingsProvider()
        {
            return new UnityEliSettingsProvider(SettingsPath, SettingsScope.User)
            {
                keywords = new[] { "Unity Eli", "AI", "Claude", "Claude Code", "MCP", "Chat", "Assistant", "Model", "Instructions", "Prompt" }
            };
        }
    }
}
