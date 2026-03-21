using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class GetConsoleLogsTool : IEliTool
    {
        public string Name => "get_console_logs";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            var filter = string.IsNullOrWhiteSpace(input.filter) ? "all" : input.filter.ToLowerInvariant();
            var maxEntries = input.max_entries > 0 ? input.max_entries : 50;

            bool includeErrors = filter == "all" || filter == "errors";
            bool includeWarnings = filter == "all" || filter == "warnings";
            bool includeLogs = filter == "all" || filter == "logs";

            try
            {
                return ReadLogsViaReflection(includeErrors, includeWarnings, includeLogs, maxEntries);
            }
            catch (Exception e)
            {
                return ToolResult.Error($"Failed to read console logs: {e.Message}");
            }
        }

        private static string ReadLogsViaReflection(bool includeErrors, bool includeWarnings, bool includeLogs, int maxEntries)
        {
            var unityEditorAssembly = typeof(UnityEditor.Editor).Assembly;

            // Use reflection to access LogEntries since the internal API varies across Unity versions.
            var logEntriesType = unityEditorAssembly.GetType("UnityEditor.LogEntries")
                              ?? unityEditorAssembly.GetType("UnityEditorInternal.LogEntries");

            if (logEntriesType == null)
                return ToolResult.Error("Could not find LogEntries type. This Unity version may not be supported.");

            var getCountMethod = logEntriesType.GetMethod("GetCount",
                BindingFlags.Static | BindingFlags.Public);
            var startMethod = logEntriesType.GetMethod("StartGettingEntries",
                BindingFlags.Static | BindingFlags.Public);
            var endMethod = logEntriesType.GetMethod("EndGettingEntries",
                BindingFlags.Static | BindingFlags.Public);
            var getEntryMethod = logEntriesType.GetMethod("GetEntryInternal",
                BindingFlags.Static | BindingFlags.Public);

            if (getCountMethod == null || getEntryMethod == null)
                return ToolResult.Error("Could not find required LogEntries methods.");

            // Find the LogEntry type
            var logEntryType = unityEditorAssembly.GetType("UnityEditor.LogEntry")
                            ?? unityEditorAssembly.GetType("UnityEditorInternal.LogEntry");

            if (logEntryType == null)
                return ToolResult.Error("Could not find LogEntry type.");

            var messageField = logEntryType.GetField("message",
                BindingFlags.Instance | BindingFlags.Public);
            var modeField = logEntryType.GetField("mode",
                BindingFlags.Instance | BindingFlags.Public);

            if (messageField == null)
                return ToolResult.Error("Could not find LogEntry.message field.");

            int totalCount = (int)getCountMethod.Invoke(null, null);

            if (totalCount == 0)
                return ToolResult.Success("The Unity console is empty. No logs, warnings, or errors.");

            startMethod?.Invoke(null, null);

            var results = new List<string>();
            int errorCount = 0;
            int warningCount = 0;
            int logCount = 0;

            try
            {
                // Read entries from newest to oldest (end of list is newest)
                for (int i = totalCount - 1; i >= 0 && results.Count < maxEntries; i--)
                {
                    var entry = Activator.CreateInstance(logEntryType);
                    getEntryMethod.Invoke(null, new object[] { i, entry });

                    var message = (string)messageField.GetValue(entry);

                    var severity = ClassifySeverity(entry, modeField, message);

                    switch (severity)
                    {
                        case LogSeverity.Error:
                            errorCount++;
                            if (!includeErrors) continue;
                            break;
                        case LogSeverity.Warning:
                            warningCount++;
                            if (!includeWarnings) continue;
                            break;
                        default:
                            logCount++;
                            if (!includeLogs) continue;
                            break;
                    }

                    // Trim excessively long messages
                    if (message != null && message.Length > 1000)
                        message = message.Substring(0, 1000) + "... (truncated)";

                    results.Add($"[{severity.ToString().ToUpperInvariant()}] {message}");
                }
            }
            finally
            {
                endMethod?.Invoke(null, null);
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Console summary: {errorCount} error(s), {warningCount} warning(s), {logCount} log(s) (total: {totalCount} entries).");
            sb.AppendLine($"Showing {results.Count} entries (newest first):");
            sb.AppendLine();

            foreach (var line in results)
            {
                sb.AppendLine(line);
            }

            return ToolResult.Success(sb.ToString());
        }

        private enum LogSeverity { Log, Warning, Error }

        private static LogSeverity ClassifySeverity(object entry, FieldInfo modeField, string message)
        {
            // Primary: use the mode bitmask from LogEntry
            if (modeField != null)
            {
                int mode = (int)modeField.GetValue(entry);

                // Unity internal mode flags:
                // Bit 0 (1)   = Error / Fatal
                // Bit 1 (2)   = Assert
                // Bit 2 (4)   = Log
                // Bit 3 (8)   = Fatal (also an error)
                // Bit 4 (16)  = ... 
                // Bit 5 (32)  = AssetImport Log
                // Bit 7 (128) = Warning (ScriptingWarning)
                // Bit 8 (256) = Error (ScriptingError)
                // Bit 9 (512) = Sticky Error
                // Bit 11 (2048)   = ScriptingAssertion
                // Bit 21 (1<<21)  = ScriptCompileError
                // Bit 22 (1<<22)  = ScriptCompileWarning

                const int errorBits   = 1 | 2 | 8 | 256 | 512 | 2048 | (1 << 21);
                const int warningBits = 128 | (1 << 22);

                if ((mode & errorBits) != 0)
                    return LogSeverity.Error;
                if ((mode & warningBits) != 0)
                    return LogSeverity.Warning;

                return LogSeverity.Log;
            }

            // Fallback: guess from message text
            if (message != null)
            {
                if (message.StartsWith("error", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("error CS") || message.Contains("Error CS") ||
                    message.Contains(": error ") ||
                    message.Contains("NullReferenceException") ||
                    message.Contains("Exception:"))
                    return LogSeverity.Error;

                if (message.StartsWith("warning", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("warning CS") || message.Contains("Warning CS") ||
                    message.Contains(": warning "))
                    return LogSeverity.Warning;
            }

            return LogSeverity.Log;
        }

        [Serializable]
        private class Input
        {
            public string filter;
            public int max_entries;
        }
    }
}
