using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace UnityEli.Editor
{
    /// <summary>
    /// Manages the Claude Code CLI subprocess for a single conversation turn.
    ///
    /// Output is redirected to temp files so the process survives Unity domain reloads
    /// (script recompilation). After a reload, the read loop resumes from where it left off.
    ///
    /// Stream-json events are parsed and dispatched to the Unity main thread
    /// via EditorApplication.update so that UI updates happen safely.
    /// </summary>
    [InitializeOnLoad]
    public static class ClaudeCodeProcess
    {
        public static bool IsRunning { get; private set; }

        /// <summary>Fired on the main thread for each parsed stream event.</summary>
        public static event Action<StreamEvent> OnStreamEvent;

        /// <summary>Fired on the main thread when the process exits. (success, errorMessage)</summary>
        public static event Action<bool, string> OnComplete;

        private static readonly ConcurrentQueue<StreamEvent> _eventQueue = new ConcurrentQueue<StreamEvent>();
        private static CancellationTokenSource _cts;
        private static volatile int _readOffset;

        // UTF-8 without BOM — critical for JSON files (BOM breaks JSON parsers) and batch scripts
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

        // SessionState keys for surviving domain reloads
        private const string PidKey = "UnityEli_ProcessPid";
        private const string StdoutPathKey = "UnityEli_StdoutPath";
        private const string StderrPathKey = "UnityEli_StderrPath";
        private const string PromptPathKey = "UnityEli_PromptPath";
        private const string ReadOffsetKey = "UnityEli_ReadOffset";

        static ClaudeCodeProcess()
        {
            // Save read offset before assembly reload so we can resume after.
            AssemblyReloadEvents.beforeAssemblyReload += SaveReadOffset;
            // Only kill on editor quit — let the process survive recompilation.
            EditorApplication.quitting += KillProcess;
            // After domain reload, try to reconnect to a running process.
            EditorApplication.delayCall += TryReconnect;
        }

        /// <summary>
        /// Saves the current file read offset to SessionState (main thread only).
        /// Called before assembly reload so the read loop can resume at the right position.
        /// </summary>
        private static void SaveReadOffset()
        {
            if (IsRunning)
                SessionState.SetInt(ReadOffsetKey, _readOffset);
        }

        /// <summary>
        /// Starts a new Claude Code turn.
        /// </summary>
        public static void SendMessage(string prompt, string sessionId, int mcpPort)
        {
            if (IsRunning)
            {
                Debug.LogWarning("[Unity Eli] SendMessage called while already running.");
                return;
            }

            // Write .mcp.json to project root (auto-discovered by Claude Code)
            WriteMcpJsonToProject(mcpPort);

            // Write prompt to a temp file (avoids stdin pipe issues across domain reloads)
            var promptPath = Path.Combine(Application.temporaryCachePath, "unity-eli-prompt.txt");
            try { File.WriteAllText(promptPath, prompt, Utf8NoBom); }
            catch (Exception e)
            {
                DeliverErrorEvent($"Failed to write prompt file: {e.Message}");
                return;
            }

            var stdoutPath = Path.Combine(Application.temporaryCachePath, "unity-eli-stdout.jsonl");
            var stderrPath = Path.Combine(Application.temporaryCachePath, "unity-eli-stderr.txt");

            // Clear previous output files
            try { File.WriteAllText(stdoutPath, "", Utf8NoBom); } catch { }
            try { File.WriteAllText(stderrPath, "", Utf8NoBom); } catch { }

            var cliArgs = BuildArguments(sessionId);

            try
            {
                var pid = LaunchProcess(cliArgs, promptPath, stdoutPath, stderrPath);

                // Persist state for domain reload recovery
                SessionState.SetInt(PidKey, pid);
                SessionState.SetString(StdoutPathKey, stdoutPath);
                SessionState.SetString(StderrPathKey, stderrPath);
                SessionState.SetString(PromptPathKey, promptPath);
                _readOffset = 0;
                SessionState.SetInt(ReadOffsetKey, 0);

                IsRunning = true;
                _cts = new CancellationTokenSource();
                EditorApplication.update += DrainEventQueue;

                var token = _cts.Token;
                Task.Run(() => ReadLoop(pid, stdoutPath, stderrPath, promptPath, 0, token));
            }
            catch (Exception e) when (
                e is System.ComponentModel.Win32Exception ||
                e is FileNotFoundException)
            {
                CleanupFiles(promptPath, stdoutPath, stderrPath);
                DeliverErrorEvent(
                    "Claude Code CLI not found. Install it with:\n" +
                    "  npm install -g @anthropic-ai/claude-code\n\n" +
                    "Make sure 'claude' is available in your PATH.");
            }
            catch (Exception e)
            {
                CleanupFiles(promptPath, stdoutPath, stderrPath);
                DeliverErrorEvent($"Failed to start Claude Code: {e.Message}");
            }
        }

        /// <summary>Cancels the running subprocess (user-initiated).</summary>
        public static void Cancel()
        {
            if (!IsRunning) return;
            _cts?.Cancel();
            KillProcess();
            Cleanup();
            ClearSessionState();
        }

        // ── Process launch ───────────────────────────────────────────────────

        /// <summary>
        /// Launches claude via shell with output redirected to files.
        /// Returns the PID of the shell process.
        /// </summary>
        private static int LaunchProcess(string cliArgs, string promptPath, string stdoutPath, string stderrPath)
        {
            // Write a small launcher script to avoid cmd.exe quoting issues with pipes and redirects.
            var scriptPath = Path.Combine(Application.temporaryCachePath, "unity-eli-launch");

            // Set working directory to project root so Claude discovers .mcp.json
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            var psi = new ProcessStartInfo
            {
                UseShellExecute = false,
                RedirectStandardInput = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                CreateNoWindow = true,
                WorkingDirectory = projectRoot,
            };

#if UNITY_EDITOR_WIN
            scriptPath += ".bat";
            var claudePath = ResolveClaudePath();
            var script = $"@echo off\r\ntype \"{promptPath}\" | \"{claudePath}\" {cliArgs} > \"{stdoutPath}\" 2> \"{stderrPath}\"\r\n";
            File.WriteAllText(scriptPath, script, Utf8NoBom);
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/c \"{scriptPath}\"";
#else
            scriptPath += ".sh";
            var claudePath = ResolveClaudePath();
            var script = $"#!/bin/sh\ncat \"{promptPath}\" | \"{claudePath}\" {cliArgs} > \"{stdoutPath}\" 2> \"{stderrPath}\"\n";
            File.WriteAllText(scriptPath, script, Utf8NoBom);
            // Make executable
            try { Process.Start("chmod", $"+x \"{scriptPath}\"")?.WaitForExit(2000); } catch { }
            psi.FileName = "/bin/sh";
            psi.Arguments = scriptPath;
#endif

            Debug.Log($"[Unity Eli] Launch script:\n{script}");

            var process = new Process { StartInfo = psi };
            process.Start();

            var pid = process.Id;
            Debug.Log($"[Unity Eli] Launched claude (PID {pid}): claude {cliArgs}");
            process.Dispose(); // Release the managed handle; OS process continues running
            return pid;
        }

        // ── Domain reload reconnection ───────────────────────────────────────

        private static void TryReconnect()
        {
            var pid = SessionState.GetInt(PidKey, 0);
            if (pid == 0) return;

            var stdoutPath = SessionState.GetString(StdoutPathKey, "");
            var stderrPath = SessionState.GetString(StderrPathKey, "");
            var promptPath = SessionState.GetString(PromptPathKey, "");
            var readOffset = SessionState.GetInt(ReadOffsetKey, 0);

            // Bail out if files are missing (e.g. editor restarted and temp files were cleaned)
            if (string.IsNullOrEmpty(stdoutPath) || !File.Exists(stdoutPath))
            {
                ClearSessionState();
                return;
            }

            // Check if the process is still alive
            if (!IsProcessAlive(pid))
            {
                // Process finished during reload — read any remaining output
                IsRunning = true;
                _cts = new CancellationTokenSource();
                EditorApplication.update += DrainEventQueue;
                var token = _cts.Token;
                Task.Run(() => ReadLoop(pid, stdoutPath, stderrPath, promptPath, readOffset, token));
                return;
            }

            // Process still running — resume reading
            Debug.Log($"[Unity Eli] Reconnecting to Claude process (PID {pid}) after domain reload");
            IsRunning = true;
            _cts = new CancellationTokenSource();
            EditorApplication.update += DrainEventQueue;
            var t = _cts.Token;
            Task.Run(() => ReadLoop(pid, stdoutPath, stderrPath, promptPath, readOffset, t));
        }

        // ── File-based read loop ─────────────────────────────────────────────

        private static void ReadLoop(int pid, string stdoutPath, string stderrPath,
            string promptPath, int startOffset, CancellationToken token)
        {
            long fileOffset = startOffset;

            try
            {
                // Poll the stdout file for new lines
                using (var fs = new FileStream(stdoutPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(fileOffset, SeekOrigin.Begin);
                    using (var reader = new StreamReader(fs, Encoding.UTF8))
                    {
                        while (!token.IsCancellationRequested)
                        {
                            var line = reader.ReadLine();
                            if (line != null)
                            {
                                if (!string.IsNullOrWhiteSpace(line))
                                {
                                    var evt = ParseStreamLine(line);
                                    if (evt != null) _eventQueue.Enqueue(evt);
                                }
                                // Track read position for domain reload recovery
                                fileOffset = fs.Position;
                                _readOffset = (int)fileOffset;
                                continue;
                            }

                            // No new data — check if process is still alive
                            if (!IsProcessAlive(pid))
                            {
                                // Process finished — read any final data
                                Thread.Sleep(200); // Brief wait for filesystem flush
                                while ((line = reader.ReadLine()) != null)
                                {
                                    if (!string.IsNullOrWhiteSpace(line))
                                    {
                                        var evt = ParseStreamLine(line);
                                        if (evt != null) _eventQueue.Enqueue(evt);
                                    }
                                }
                                break; // Done
                            }

                            // Process still running, wait for more output
                            Thread.Sleep(50);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                if (!token.IsCancellationRequested)
                    _eventQueue.Enqueue(new StreamEvent { Type = "error", ErrorText = $"Read error: {e.Message}" });
            }
            finally
            {
                // Read stderr for error reporting and diagnostics
                string stderr = null;
                try
                {
                    if (File.Exists(stderrPath))
                    {
                        stderr = File.ReadAllText(stderrPath, Encoding.UTF8).Trim();
                        if (!string.IsNullOrEmpty(stderr))
                            Debug.Log($"[Unity Eli] Claude stderr:\n{(stderr.Length > 2000 ? stderr.Substring(0, 2000) + "..." : stderr)}");
                    }
                }
                catch { }

                // Determine success
                bool success = !token.IsCancellationRequested;
                int exitCode = -1;
                try
                {
                    var proc = Process.GetProcessById(pid);
                    if (proc.HasExited) exitCode = proc.ExitCode;
                    proc.Dispose();
                }
                catch { } // Process already gone

                if (exitCode != -1)
                    success = success && exitCode == 0;

                // Clean up temp files (only when process is done, not during domain reload)
                if (!token.IsCancellationRequested)
                {
                    CleanupFiles(promptPath, stdoutPath, stderrPath);
                    ClearSessionStateFromBackground();
                }

                _eventQueue.Enqueue(new StreamEvent
                {
                    Type = "_done",
                    Success = success,
                    ErrorText = !string.IsNullOrEmpty(stderr) ? stderr : null
                });
            }
        }

        // ── Process utilities ────────────────────────────────────────────────

        private static bool IsProcessAlive(int pid)
        {
            try
            {
                var proc = Process.GetProcessById(pid);
                var alive = !proc.HasExited;
                proc.Dispose();
                return alive;
            }
            catch
            {
                return false; // Process doesn't exist
            }
        }

        private static void KillProcess()
        {
            var pid = SessionState.GetInt(PidKey, 0);
            if (pid == 0) return;

            try
            {
                var proc = Process.GetProcessById(pid);
                if (!proc.HasExited)
                {
                    proc.Kill();
                    Debug.Log($"[Unity Eli] Killed Claude process (PID {pid})");
                }
                proc.Dispose();
            }
            catch { } // Process already gone

            // Also clean up files
            var promptFile = SessionState.GetString(PromptPathKey, "");
            var stdoutFile = SessionState.GetString(StdoutPathKey, "");
            var stderrFile = SessionState.GetString(StderrPathKey, "");
            CleanupFiles(promptFile, stdoutFile, stderrFile);
            ClearSessionState();
        }

        // ── Main thread event drain ───────────────────────────────────────────

        private static void DrainEventQueue()
        {
            while (_eventQueue.TryDequeue(out var evt))
            {
                if (evt.Type == "_done")
                {
                    var success = evt.Success;
                    var error = evt.ErrorText;
                    Cleanup();
                    OnComplete?.Invoke(success, error);
                    return; // Stop draining; Cleanup unregistered DrainEventQueue
                }

                OnStreamEvent?.Invoke(evt);
            }
        }

        // ── Stream-json parsing ───────────────────────────────────────────────

        private static StreamEvent ParseStreamLine(string line)
        {
            try
            {
                var type = JsonHelper.ExtractString(line, "type");
                if (string.IsNullOrEmpty(type)) return null;

                var evt = new StreamEvent { Type = type };

                switch (type)
                {
                    case "system":
                        evt.Subtype = JsonHelper.ExtractString(line, "subtype");
                        evt.SessionId = JsonHelper.ExtractString(line, "session_id");
                        break;

                    case "assistant":
                        var messageJson = JsonHelper.ExtractObject(line, "message");
                        var contentArray = JsonHelper.ExtractArray(messageJson, "content");
                        ParseAssistantContent(contentArray, evt);
                        // Usage can be inside message.usage or at the event top level
                        evt.InputTokens = ExtractInputTokens(messageJson);
                        if (evt.InputTokens == 0)
                            evt.InputTokens = ExtractInputTokens(line);
                        break;

                    case "result":
                        evt.Subtype = JsonHelper.ExtractString(line, "subtype");
                        evt.SessionId = JsonHelper.ExtractString(line, "session_id");
                        evt.ResultText = JsonHelper.ExtractString(line, "result");
                        evt.Success = evt.Subtype == "success";
                        evt.InputTokens = ExtractInputTokens(line);
                        break;

                    case "error":
                        var errorObj = JsonHelper.ExtractObject(line, "error");
                        evt.ErrorText = JsonHelper.ExtractString(errorObj, "message")
                                        ?? JsonHelper.ExtractString(line, "message")
                                        ?? "Unknown error";
                        break;
                }

                return evt;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Unity Eli] Failed to parse stream line: {e.Message}\nLine: {line}");
                return null;
            }
        }

        /// <summary>
        /// Extracts total context tokens from a JSON fragment. With prompt caching,
        /// input_tokens only counts non-cached tokens. The real context usage is the
        /// sum of input_tokens + cache_creation_input_tokens + cache_read_input_tokens.
        /// </summary>
        private static int ExtractInputTokens(string json)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            var usageJson = JsonHelper.ExtractObject(json, "usage");
            if (usageJson == "{}")
                usageJson = json; // Fallback: fields at the current level

            var tokens = JsonHelper.ExtractInt(usageJson, "input_tokens");
            tokens += JsonHelper.ExtractInt(usageJson, "cache_creation_input_tokens");
            tokens += JsonHelper.ExtractInt(usageJson, "cache_read_input_tokens");
            return tokens;
        }

        private static void ParseAssistantContent(string contentArray, StreamEvent evt)
        {
            var blocks = JsonHelper.ParseArray(contentArray);
            var textSb = new StringBuilder();

            foreach (var block in blocks)
            {
                var blockType = JsonHelper.ExtractString(block, "type");

                if (blockType == "text")
                {
                    var text = JsonHelper.ExtractString(block, "text");
                    if (!string.IsNullOrEmpty(text))
                    {
                        if (textSb.Length > 0) textSb.Append("\n");
                        textSb.Append(text);
                    }
                }
                else if (blockType == "tool_use")
                {
                    evt.ToolName = JsonHelper.ExtractString(block, "name");
                    evt.ToolInputJson = JsonHelper.ExtractObject(block, "input");
                    evt.ToolDescription = SummarizeToolInput(evt.ToolName, evt.ToolInputJson);
                }
            }

            if (textSb.Length > 0)
                evt.AssistantText = textSb.ToString();
        }

        /// <summary>
        /// Extracts a short human-readable description from tool input JSON.
        /// </summary>
        private static string SummarizeToolInput(string toolName, string inputJson)
        {
            if (string.IsNullOrEmpty(inputJson)) return null;

            try
            {
                string summary = null;
                switch (toolName)
                {
                    case "Bash":
                        summary = JsonHelper.ExtractString(inputJson, "command");
                        break;
                    case "Read":
                        summary = JsonHelper.ExtractString(inputJson, "file_path");
                        break;
                    case "Write":
                        summary = JsonHelper.ExtractString(inputJson, "file_path");
                        break;
                    case "Edit":
                        summary = JsonHelper.ExtractString(inputJson, "file_path");
                        break;
                    case "Glob":
                        summary = JsonHelper.ExtractString(inputJson, "pattern");
                        break;
                    case "Grep":
                        summary = JsonHelper.ExtractString(inputJson, "pattern");
                        break;
                    case "Agent":
                        summary = JsonHelper.ExtractString(inputJson, "prompt");
                        break;
                    default:
                        if (toolName != null && toolName.StartsWith("mcp__unity__"))
                        {
                            summary = JsonHelper.ExtractString(inputJson, "game_object_name")
                                      ?? JsonHelper.ExtractString(inputJson, "name")
                                      ?? JsonHelper.ExtractString(inputJson, "path");
                        }
                        break;
                }

                if (string.IsNullOrEmpty(summary)) return null;

                if (summary.Length > 120)
                    summary = summary.Substring(0, 117) + "...";

                var nl = summary.IndexOfAny(new[] { '\n', '\r' });
                if (nl >= 0) summary = summary.Substring(0, nl) + "...";

                return summary;
            }
            catch
            {
                return null;
            }
        }

        // ── Argument building ────────────────────────────────────────────────

        private static string BuildArguments(string sessionId)
        {
            var sb = new StringBuilder();

            sb.Append("-p");
            sb.Append(" --output-format stream-json");
            sb.Append(" --verbose"); // required by --output-format stream-json in print mode

            sb.Append(" --allowedTools ");
            sb.Append(UnityEliSettings.BuildAllowedToolsArgs());

            var modelId = UnityEliSettings.SelectedModelId;
            if (!string.IsNullOrEmpty(modelId))
                sb.Append($" --model \"{modelId}\"");

            var instructionsPath = UnityEliSettings.ResolveInstructionsFilePath();
            if (instructionsPath != null)
                sb.Append($" --append-system-prompt-file \"{instructionsPath}\"");

            if (!string.IsNullOrEmpty(sessionId))
                sb.Append($" --resume \"{sessionId}\"");

            var args = sb.ToString();
            Debug.Log($"[Unity Eli] claude {args}");
            return args;
        }

        // ── MCP config helpers ────────────────────────────────────────────────

        /// <summary>
        /// Writes .mcp.json to the project root so Claude auto-discovers the MCP server.
        /// This is the most reliable method — Claude always checks for .mcp.json in the working directory.
        /// </summary>
        private static void WriteMcpJsonToProject(int port)
        {
            try
            {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var mcpJsonPath = Path.Combine(projectRoot, ".mcp.json");
                var config = BuildMcpConfigJson(port);
                File.WriteAllText(mcpJsonPath, config, Utf8NoBom);
                Debug.Log($"[Unity Eli] Wrote {mcpJsonPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Unity Eli] Failed to write .mcp.json: {e.Message}");
            }
        }

        private static string BuildMcpConfigJson(int port)
        {
            return "{\"mcpServers\":{\"unity\":{\"type\":\"http\",\"url\":\"http://localhost:" +
                   port + "/mcp/\"}}}";
        }

        // ── Claude CLI path resolution ───────────────────────────────────────

        private static string _cachedClaudePath;

        /// <summary>
        /// Resolves the full path to the 'claude' CLI executable.
        /// Unity often doesn't inherit the full user PATH (especially npm global bin),
        /// so we probe common install locations before falling back to 'where'/'which'.
        /// </summary>
        private static string ResolveClaudePath()
        {
            if (_cachedClaudePath != null) return _cachedClaudePath;

#if UNITY_EDITOR_WIN
            // Common Windows locations for npm global installs
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var candidates = new[]
            {
                Path.Combine(appData, "npm", "claude.cmd"),
                Path.Combine(appData, "npm", "claude"),
            };
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                {
                    _cachedClaudePath = c;
                    Debug.Log($"[Unity Eli] Resolved claude CLI at: {c}");
                    return c;
                }
            }

            // Fallback: ask the system via 'where'
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "where",
                    Arguments = "claude",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                using (var proc = Process.Start(psi))
                {
                    var output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(5000);
                    if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        // 'where' may return multiple lines; take the first .cmd match or first result
                        foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            if (line.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                            {
                                _cachedClaudePath = line;
                                Debug.Log($"[Unity Eli] Resolved claude CLI via 'where': {line}");
                                return line;
                            }
                        }
                        var first = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)[0];
                        _cachedClaudePath = first;
                        Debug.Log($"[Unity Eli] Resolved claude CLI via 'where': {first}");
                        return first;
                    }
                }
            }
            catch { }
#else
            // macOS / Linux: check common locations
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new[]
            {
                "/usr/local/bin/claude",
                "/opt/homebrew/bin/claude",
                Path.Combine(home, ".npm-global", "bin", "claude"),
                Path.Combine(home, ".nvm", "versions"),  // handled below
            };
            foreach (var c in candidates)
            {
                if (c.Contains(".nvm")) continue; // skip placeholder
                if (File.Exists(c))
                {
                    _cachedClaudePath = c;
                    Debug.Log($"[Unity Eli] Resolved claude CLI at: {c}");
                    return c;
                }
            }

            // Fallback: ask the system via 'which'
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    Arguments = "-c \"which claude\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                };
                using (var proc = Process.Start(psi))
                {
                    var output = proc.StandardOutput.ReadToEnd().Trim();
                    proc.WaitForExit(5000);
                    if (proc.ExitCode == 0 && !string.IsNullOrEmpty(output))
                    {
                        _cachedClaudePath = output;
                        Debug.Log($"[Unity Eli] Resolved claude CLI via 'which': {output}");
                        return output;
                    }
                }
            }
            catch { }
#endif

            // Last resort: bare name, hope PATH works
            Debug.LogWarning("[Unity Eli] Could not resolve full path to 'claude' CLI. Falling back to bare name.");
            _cachedClaudePath = "claude";
            return "claude";
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void TryDelete(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static void CleanupFiles(params string[] paths)
        {
            foreach (var p in paths) TryDelete(p);
        }

        private static void Cleanup()
        {
            EditorApplication.update -= DrainEventQueue;
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
        }

        private static void ClearSessionState()
        {
            SessionState.EraseInt(PidKey);
            SessionState.EraseString(StdoutPathKey);
            SessionState.EraseString(StderrPathKey);
            SessionState.EraseString(PromptPathKey);
            SessionState.EraseInt(ReadOffsetKey);
        }

        /// <summary>
        /// Clears session state from a background thread by scheduling it on the main thread.
        /// </summary>
        private static void ClearSessionStateFromBackground()
        {
            EditorApplication.delayCall += ClearSessionState;
        }

        private static void DeliverErrorEvent(string message)
        {
            _eventQueue.Enqueue(new StreamEvent { Type = "_done", Success = false, ErrorText = message });
            EditorApplication.update += DrainEventQueue;
        }
    }

    /// <summary>
    /// Represents a single parsed event from Claude Code's stream-json output.
    /// </summary>
    public class StreamEvent
    {
        /// <summary>"system", "assistant", "result", "error", "_done" (internal sentinel)</summary>
        public string Type;
        /// <summary>For "system": "init". For "result": "success" or "error_during_execution".</summary>
        public string Subtype;
        public string SessionId;
        /// <summary>Text content from an assistant message.</summary>
        public string AssistantText;
        /// <summary>Tool name from a tool_use block in an assistant message.</summary>
        public string ToolName;
        /// <summary>Raw JSON input from a tool_use block.</summary>
        public string ToolInputJson;
        /// <summary>Short description derived from tool input (e.g. the command, file path, pattern).</summary>
        public string ToolDescription;
        /// <summary>Final summary text from a "result" event.</summary>
        public string ResultText;
        /// <summary>Error message from an "error" event or internal failure.</summary>
        public string ErrorText;
        /// <summary>Set on "_done" and "result" events.</summary>
        public bool Success;
        /// <summary>Total input tokens used in this turn (from "result" usage). Used for context bar.</summary>
        public int InputTokens;
    }
}
