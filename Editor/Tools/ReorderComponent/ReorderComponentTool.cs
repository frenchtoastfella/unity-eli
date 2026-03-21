using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class ReorderComponentTool : IEliTool
    {
        public string Name => "reorder_component";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object))
                return ToolResult.Error("game_object is required.");
            if (string.IsNullOrWhiteSpace(input.component_type))
                return ToolResult.Error("component_type is required.");
            if (string.IsNullOrWhiteSpace(input.direction))
                return ToolResult.Error("direction is required: 'up' or 'down'.");

            var go = EliToolHelpers.FindGameObject(input.game_object);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object}' not found.");

            var type = EliToolHelpers.ResolveType(input.component_type);
            if (type == null)
                return ToolResult.Error($"Component type '{input.component_type}' not found.");

            var component = go.GetComponent(type);
            if (component == null)
                return ToolResult.Error(
                    $"GameObject '{input.game_object}' does not have a '{input.component_type}' component.");

            bool moved;
            switch (input.direction.ToLowerInvariant())
            {
                case "up":
                    moved = ComponentUtility.MoveComponentUp(component);
                    break;
                case "down":
                    moved = ComponentUtility.MoveComponentDown(component);
                    break;
                default:
                    return ToolResult.Error($"Invalid direction '{input.direction}'. Use 'up' or 'down'.");
            }

            if (!moved)
                return ToolResult.Success(
                    $"'{input.component_type}' could not move {input.direction} further (already at limit).");

            return ToolResult.Success(
                $"Moved '{input.component_type}' {input.direction} on '{input.game_object}'.");
        }

        [Serializable]
        private class Input
        {
            public string game_object;
            public string component_type;
            public string direction;
        }
    }
}
