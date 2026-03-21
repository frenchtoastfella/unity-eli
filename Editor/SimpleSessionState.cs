using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor
{
    /// <summary>
    /// Persists chat UI state across domain reloads (script recompilation) using SessionState.
    /// Much simpler than the old ChatSessionState because conversation history is now managed
    /// by Claude Code's own session persistence — we only need to store display messages and
    /// the session ID string.
    /// </summary>
    public static class SimpleSessionState
    {
        private const string DisplayMessagesKey = "UnityEli_DisplayMessages";
        private const string SessionIdKey = "UnityEli_SessionId";
        private const string IsProcessingKey = "UnityEli_IsProcessing";

        // ── Save ─────────────────────────────────────────────────────────────

        public static void SaveDisplayMessages(List<DisplayMessage> messages)
        {
            SessionState.SetString(DisplayMessagesKey, SerializeMessages(messages));
        }

        public static void SaveSessionId(string sessionId)
        {
            if (sessionId != null)
                SessionState.SetString(SessionIdKey, sessionId);
        }

        public static void SaveIsProcessing(bool value)
        {
            SessionState.SetBool(IsProcessingKey, value);
        }

        public static void SaveAll(List<DisplayMessage> messages, string sessionId, bool isProcessing)
        {
            SaveDisplayMessages(messages);
            SaveSessionId(sessionId);
            SaveIsProcessing(isProcessing);
        }

        // ── Load ─────────────────────────────────────────────────────────────

        public static List<DisplayMessage> LoadDisplayMessages()
        {
            var json = SessionState.GetString(DisplayMessagesKey, "");
            return string.IsNullOrEmpty(json) ? new List<DisplayMessage>() : DeserializeMessages(json);
        }

        public static string LoadSessionId()
        {
            var id = SessionState.GetString(SessionIdKey, "");
            return string.IsNullOrEmpty(id) ? null : id;
        }

        public static bool LoadIsProcessing()
        {
            return SessionState.GetBool(IsProcessingKey, false);
        }

        // ── Clear ─────────────────────────────────────────────────────────────

        public static void Clear()
        {
            SessionState.EraseString(DisplayMessagesKey);
            SessionState.EraseString(SessionIdKey);
            SessionState.EraseBool(IsProcessingKey);
        }

        // ── Serialization ─────────────────────────────────────────────────────

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

                    // New format: blocks array
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
                        // Legacy format: simple "content" string
                        var content = JsonHelper.ExtractString(item, "content");
                        if (!string.IsNullOrEmpty(content))
                            msg.Blocks.Add(MessageBlock.CreateText(content));
                    }

                    messages.Add(msg);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Unity Eli] Failed to restore display messages: {e.Message}");
            }
            return messages;
        }
    }

    // ── Data model ────────────────────────────────────────────────────────────

    public enum BlockType { Text, Tool }

    public class MessageBlock
    {
        public BlockType Type;
        public string Text;
        public ToolEntry Tool;

        public static MessageBlock CreateText(string text) =>
            new MessageBlock { Type = BlockType.Text, Text = text };

        public static MessageBlock CreateTool(ToolEntry tool) =>
            new MessageBlock { Type = BlockType.Tool, Tool = tool };
    }

    public class ToolEntry
    {
        public string Name;
        /// <summary>Short human-readable description of what the tool is doing (e.g. "ls -la src/").</summary>
        public string Description;
        public string Result;
        public bool IsError;
        public bool IsExpanded;
    }

    /// <summary>
    /// A display-only message for the chat UI.
    /// Contains an ordered list of content blocks (text and tool calls) to preserve
    /// the natural interleaving of assistant text and tool usage within a single turn.
    /// </summary>
    public class DisplayMessage
    {
        public string Role { get; set; }
        public List<MessageBlock> Blocks { get; }

        /// <summary>All text content concatenated (for copy-to-clipboard, backward compat).</summary>
        public string Content
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var b in Blocks)
                {
                    if (b.Type != BlockType.Text || string.IsNullOrEmpty(b.Text)) continue;
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(b.Text);
                }
                return sb.ToString();
            }
        }

        public DisplayMessage(string role, string content = null)
        {
            Role = role;
            Blocks = new List<MessageBlock>();
            if (!string.IsNullOrEmpty(content))
                Blocks.Add(MessageBlock.CreateText(content));
        }

        /// <summary>
        /// Appends text. If the last block is text, extends it. Otherwise adds a new text block.
        /// </summary>
        public void AppendText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var last = Blocks.Count > 0 ? Blocks[Blocks.Count - 1] : null;
            if (last != null && last.Type == BlockType.Text)
                last.Text += "\n\n" + text;
            else
                Blocks.Add(MessageBlock.CreateText(text));
        }

        /// <summary>Adds a tool call entry and returns it for later result assignment.</summary>
        public ToolEntry AddTool(string name, string description = null)
        {
            var entry = new ToolEntry { Name = name, Description = description };
            Blocks.Add(MessageBlock.CreateTool(entry));
            return entry;
        }

        /// <summary>Finds the first tool entry with no result yet.</summary>
        public ToolEntry FindUnresolvedTool()
        {
            foreach (var b in Blocks)
                if (b.Type == BlockType.Tool && b.Tool != null && string.IsNullOrEmpty(b.Tool.Result))
                    return b.Tool;
            return null;
        }
    }
}
