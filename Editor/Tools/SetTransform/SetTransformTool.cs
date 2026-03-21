using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class SetTransformTool : IEliTool
    {
        public string Name => "set_transform";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_objects))
                return ToolResult.Error("game_objects is required (single name or comma-separated list).");
            if (string.IsNullOrWhiteSpace(input.channels))
                return ToolResult.Error("channels is required: position, rotation, scale, position_rotation, or all.");

            var names = input.game_objects.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
            if (names.Length == 0)
                return ToolResult.Error("No valid game object names found in game_objects.");

            var mode = input.mode?.ToLowerInvariant() ?? "absolute";
            if (mode != "absolute" && mode != "relative")
                return ToolResult.Error($"Invalid mode '{input.mode}'. Use 'absolute' or 'relative'.");

            var channels = input.channels.ToLowerInvariant();
            bool applyPos = channels == "position" || channels == "position_rotation" || channels == "all";
            bool applyRot = channels == "rotation" || channels == "position_rotation" || channels == "all";
            bool applyScale = channels == "scale" || channels == "all";

            if (!applyPos && !applyRot && !applyScale)
                return ToolResult.Error($"Unknown channels '{input.channels}'. Use: position, rotation, scale, position_rotation, all.");

            var results = new System.Collections.Generic.List<string>();
            var errors = new System.Collections.Generic.List<string>();

            foreach (var name in names)
            {
                var go = EliToolHelpers.FindGameObject(name);
                if (go == null)
                {
                    errors.Add($"'{name}' not found");
                    continue;
                }

                Undo.RecordObject(go.transform, "Unity Eli: Set Transform");

                if (applyPos)
                {
                    if (mode == "absolute")
                        go.transform.position = new Vector3(input.x, input.y, input.z);
                    else
                        go.transform.position += new Vector3(input.x, input.y, input.z);
                }

                if (applyRot)
                {
                    if (mode == "absolute")
                        go.transform.eulerAngles = new Vector3(input.x, input.y, input.z);
                    else
                        go.transform.Rotate(input.x, input.y, input.z, Space.Self);
                }

                if (applyScale)
                {
                    // Scale is always local-space
                    if (mode == "absolute")
                        go.transform.localScale = new Vector3(
                            input.x == 0f && input.y == 0f && input.z == 0f ? 1f : input.x,
                            input.x == 0f && input.y == 0f && input.z == 0f ? 1f : input.y,
                            input.x == 0f && input.y == 0f && input.z == 0f ? 1f : input.z);
                    else
                        go.transform.localScale += new Vector3(input.x, input.y, input.z);
                }

                results.Add(name);
            }

            if (errors.Count > 0)
                return ToolResult.Error($"Not found: {string.Join(", ", errors)}.");

            return ToolResult.Success(
                $"Applied {channels} ({mode}) [{input.x}, {input.y}, {input.z}] to: {string.Join(", ", results)}.");
        }

        [Serializable]
        private class Input
        {
            public string game_objects;
            public string channels;
            public float x;
            public float y;
            public float z;
            public string mode;
        }
    }
}
