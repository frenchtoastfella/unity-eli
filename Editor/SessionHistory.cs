using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor
{
    /// <summary>
    /// Manages persistent chat session history stored in Library/UnityEli/Sessions/.
    /// Sessions survive editor restarts. An index of session metadata is kept in EditorPrefs
    /// while the full message data lives in per-session files on disk.
    /// </summary>
    public static class SessionHistory
    {
        private const string IndexKey = "UnityEli_SessionIndex";
        private const string ActiveSessionKey = "UnityEli_ActiveSessionId";
        private const int MaxSessions = 50;

        private static string SessionsDir
        {
            get
            {
                // Library/ is project-local and git-ignored
                var dir = Path.Combine(Application.dataPath, "..", "Library", "UnityEli", "Sessions");
                return Path.GetFullPath(dir);
            }
        }

        // ── Session entry ────────────────────────────────────────────────────

        public class SessionEntry
        {
            public string SessionId;
            public string Title;
            public string Timestamp; // ISO 8601
        }

        // ── Active session tracking ──────────────────────────────────────────

        /// <summary>
        /// Gets or sets the active session ID (persisted in EditorPrefs so it survives restarts).
        /// </summary>
        public static string ActiveSessionId
        {
            get
            {
                var id = EditorPrefs.GetString(ActiveSessionKey, "");
                return string.IsNullOrEmpty(id) ? null : id;
            }
            set
            {
                if (value != null)
                    EditorPrefs.SetString(ActiveSessionKey, value);
                else
                    EditorPrefs.DeleteKey(ActiveSessionKey);
            }
        }

        // ── Save / Load sessions ─────────────────────────────────────────────

        /// <summary>
        /// Saves a session's messages to disk and updates the index.
        /// </summary>
        public static void SaveSession(string sessionId, string title, List<DisplayMessage> messages)
        {
            if (string.IsNullOrEmpty(sessionId) || messages == null || messages.Count == 0)
                return;

            EnsureDirectory();

            // Write messages to file
            var messagesJson = SerializeMessages(messages);
            var path = GetSessionPath(sessionId);
            File.WriteAllText(path, messagesJson, Encoding.UTF8);

            // Update index
            var index = LoadIndex();

            // Remove existing entry for this session if present
            index.RemoveAll(e => e.SessionId == sessionId);

            // Insert at the top (most recent first)
            index.Insert(0, new SessionEntry
            {
                SessionId = sessionId,
                Title = title ?? "Untitled",
                Timestamp = DateTime.Now.ToString("o")
            });

            // Trim old sessions
            while (index.Count > MaxSessions)
            {
                var removed = index[index.Count - 1];
                index.RemoveAt(index.Count - 1);
                TryDeleteSessionFile(removed.SessionId);
            }

            SaveIndex(index);
        }

        /// <summary>
        /// Loads a session's messages from disk.
        /// </summary>
        public static List<DisplayMessage> LoadSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return new List<DisplayMessage>();

            var path = GetSessionPath(sessionId);
            if (!File.Exists(path)) return new List<DisplayMessage>();

            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                return DeserializeMessages(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Unity Eli] Failed to load session {sessionId}: {e.Message}");
                return new List<DisplayMessage>();
            }
        }

        /// <summary>
        /// Deletes a session from history (both index and file).
        /// </summary>
        public static void DeleteSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId)) return;

            var index = LoadIndex();
            index.RemoveAll(e => e.SessionId == sessionId);
            SaveIndex(index);
            TryDeleteSessionFile(sessionId);

            if (ActiveSessionId == sessionId)
                ActiveSessionId = null;
        }

        // ── Index management ─────────────────────────────────────────────────

        public static List<SessionEntry> LoadIndex()
        {
            var json = EditorPrefs.GetString(IndexKey, "");
            if (string.IsNullOrEmpty(json)) return new List<SessionEntry>();

            var entries = new List<SessionEntry>();
            try
            {
                var items = JsonHelper.ParseArray(json);
                foreach (var item in items)
                {
                    entries.Add(new SessionEntry
                    {
                        SessionId = JsonHelper.ExtractString(item, "id") ?? "",
                        Title = JsonHelper.ExtractString(item, "title") ?? "Untitled",
                        Timestamp = JsonHelper.ExtractString(item, "ts") ?? ""
                    });
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Unity Eli] Failed to load session index: {e.Message}");
            }
            return entries;
        }

        private static void SaveIndex(List<SessionEntry> entries)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < entries.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var e = entries[i];
                sb.Append("{\"id\":\"").Append(JsonHelper.EscapeJson(e.SessionId))
                  .Append("\",\"title\":\"").Append(JsonHelper.EscapeJson(e.Title))
                  .Append("\",\"ts\":\"").Append(JsonHelper.EscapeJson(e.Timestamp))
                  .Append("\"}");
            }
            sb.Append("]");
            EditorPrefs.SetString(IndexKey, sb.ToString());
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        /// <summary>
        /// Derives a short title from the first user message in a session.
        /// </summary>
        public static string DeriveTitle(List<DisplayMessage> messages)
        {
            foreach (var msg in messages)
            {
                if (msg.Role != "User") continue;
                var text = msg.Content;
                if (string.IsNullOrWhiteSpace(text)) continue;
                // Take first line, truncate to 60 chars
                var firstLine = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                if (firstLine.Length > 60)
                    firstLine = firstLine.Substring(0, 57) + "...";
                return firstLine;
            }
            return "Untitled";
        }

        private static string GetSessionPath(string sessionId)
        {
            // Sanitize session ID for use as a filename
            var safe = sessionId;
            foreach (var c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c, '_');
            return Path.Combine(SessionsDir, safe + ".json");
        }

        private static void EnsureDirectory()
        {
            if (!Directory.Exists(SessionsDir))
                Directory.CreateDirectory(SessionsDir);
        }

        private static void TryDeleteSessionFile(string sessionId)
        {
            try
            {
                var path = GetSessionPath(sessionId);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        // ── Serialization (reuses same format as SimpleSessionState) ─────────

        private static string SerializeMessages(List<DisplayMessage> messages)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(",");
                var msg = messages[i];
                sb.Append("{\"role\":\"").Append(JsonHelper.EscapeJson(msg.Role)).Append("\",\"blocks\":[");
                for (int j = 0; j < msg.Blocks.Count; j++)
                {
                    if (j > 0) sb.Append(",");
                    var block = msg.Blocks[j];
                    if (block.Type == BlockType.Text)
                    {
                        sb.Append("{\"type\":\"text\",\"text\":\"")
                          .Append(JsonHelper.EscapeJson(block.Text ?? ""))
                          .Append("\"}");
                    }
                    else
                    {
                        var t = block.Tool;
                        sb.Append("{\"type\":\"tool\",\"name\":\"")
                          .Append(JsonHelper.EscapeJson(t?.Name ?? ""))
                          .Append("\",\"desc\":\"")
                          .Append(JsonHelper.EscapeJson(t?.Description ?? ""))
                          .Append("\",\"result\":\"")
                          .Append(JsonHelper.EscapeJson(t?.Result ?? ""))
                          .Append("\",\"isError\":")
                          .Append(t?.IsError == true ? "true" : "false")
                          .Append("}");
                    }
                }
                sb.Append("]}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static List<DisplayMessage> DeserializeMessages(string json)
        {
            var messages = new List<DisplayMessage>();
            try
            {
                var items = JsonHelper.ParseArray(json);
                foreach (var item in items)
                {
                    var role = JsonHelper.ExtractString(item, "role") ?? "Assistant";
                    var msg = new DisplayMessage(role);

                    var blocksJson = JsonHelper.ExtractArray(item, "blocks");
                    var blocks = JsonHelper.ParseArray(blocksJson);
                    if (blocks.Count > 0)
                    {
                        foreach (var blockJson in blocks)
                        {
                            var blockType = JsonHelper.ExtractString(blockJson, "type");
                            if (blockType == "tool")
                            {
                                var tool = msg.AddTool(
                                    JsonHelper.ExtractString(blockJson, "name") ?? "",
                                    JsonHelper.ExtractString(blockJson, "desc"));
                                tool.Result = JsonHelper.ExtractString(blockJson, "result");
                                tool.IsError = JsonHelper.ExtractBool(blockJson, "isError");
                            }
                            else
                            {
                                msg.Blocks.Add(MessageBlock.CreateText(
                                    JsonHelper.ExtractString(blockJson, "text") ?? ""));
                            }
                        }
                    }
                    else
                    {
                        var content = JsonHelper.ExtractString(item, "content");
                        if (!string.IsNullOrEmpty(content))
                            msg.Blocks.Add(MessageBlock.CreateText(content));
                    }

                    messages.Add(msg);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Unity Eli] Failed to deserialize session messages: {e.Message}");
            }
            return messages;
        }
    }
}
