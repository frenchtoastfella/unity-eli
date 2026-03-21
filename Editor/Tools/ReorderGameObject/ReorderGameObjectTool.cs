using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class ReorderGameObjectTool : IEliTool
    {
        public string Name => "reorder_game_object";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object))
                return ToolResult.Error("game_object is required.");

            var go = EliToolHelpers.FindGameObject(input.game_object);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object}' not found.");

            Undo.RecordObject(go.transform, "Unity Eli: Reorder");

            if (!string.IsNullOrWhiteSpace(input.direction))
            {
                switch (input.direction.ToLowerInvariant())
                {
                    case "up":
                        var curIdx = go.transform.GetSiblingIndex();
                        go.transform.SetSiblingIndex(Mathf.Max(0, curIdx - 1));
                        break;
                    case "down":
                        var maxIdx = go.transform.parent != null
                            ? go.transform.parent.childCount - 1
                            : go.transform.root.gameObject.scene.rootCount - 1;
                        go.transform.SetSiblingIndex(Mathf.Min(maxIdx, go.transform.GetSiblingIndex() + 1));
                        break;
                    case "first":
                        go.transform.SetAsFirstSibling();
                        break;
                    case "last":
                        go.transform.SetAsLastSibling();
                        break;
                    default:
                        return ToolResult.Error($"Unknown direction '{input.direction}'. Use: up, down, first, last.");
                }
            }
            else if (input.sibling_index >= 0)
            {
                go.transform.SetSiblingIndex(input.sibling_index);
            }
            else
            {
                return ToolResult.Error("Either direction (up/down/first/last) or sibling_index must be provided.");
            }

            return ToolResult.Success(
                $"'{input.game_object}' is now at sibling index {go.transform.GetSiblingIndex()}.");
        }

        [Serializable]
        private class Input
        {
            public string game_object;
            public string direction;
            public int sibling_index = -1;
        }
    }
}
