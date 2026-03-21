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
                    if (!EditorApplication.isPlaying)
                        return ToolResult.Success("Edit mode (not playing).");
                    return ToolResult.Success(EditorApplication.isPaused ? "Play mode (paused)." : "Play mode (running).");

                default:
                    return ToolResult.Error($"Unknown action '{action}'. Valid actions: play, stop, pause, status.");
            }
        }
    }
}
