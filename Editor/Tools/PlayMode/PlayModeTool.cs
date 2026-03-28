using System;
using UnityEditor;

namespace UnityEli.Editor.Tools
{
    public class PlayModeTool : IEliTool
    {
        public string Name => "play_mode";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var action = JsonHelper.ExtractString(inputJson, "action") ?? "status";

            switch (action.ToLowerInvariant())
            {
                case "play":
                    if (EditorApplication.isCompiling)
                        return ToolResult.Error("Cannot enter Play mode while scripts are compiling. Wait for compilation to finish first.");
                    if (EditorApplication.isPlaying)
                        return ToolResult.Success("Already in Play mode.");
                    EditorApplication.isPlaying = true;
                    return ToolResult.Success("Entering Play mode. Note: domain reload may occur — the session will resume automatically.");

                case "stop":
                    if (!EditorApplication.isPlaying)
                        return ToolResult.Success("Already in Edit mode (not playing).");
                    EditorApplication.isPlaying = false;
                    return ToolResult.Success("Exiting Play mode.");

                case "pause":
                    if (!EditorApplication.isPlaying)
                        return ToolResult.Error("Cannot pause — not in Play mode. Use action 'play' first.");
                    EditorApplication.isPaused = !EditorApplication.isPaused;
                    return ToolResult.Success(EditorApplication.isPaused ? "Play mode paused." : "Play mode resumed.");

                case "status":
                    return GetStatus();

                default:
                    return ToolResult.Error($"Unknown action '{action}'. Valid actions: play, stop, pause, status.");
            }
        }

        private static string GetStatus()
        {
            var parts = new System.Collections.Generic.List<string>();

            // Play mode
            if (!EditorApplication.isPlaying)
                parts.Add("Edit mode (not playing)");
            else if (EditorApplication.isPaused)
                parts.Add("Play mode (paused)");
            else
                parts.Add("Play mode (running)");

            // Compilation
            if (EditorApplication.isCompiling)
                parts.Add("Scripts are compiling");
            else
                parts.Add("No compilation in progress");

            return ToolResult.Success(string.Join(". ", parts) + ".");
        }
    }
}
