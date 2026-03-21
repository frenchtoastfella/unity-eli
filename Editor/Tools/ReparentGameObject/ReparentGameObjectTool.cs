using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class ReparentGameObjectTool : IEliTool
    {
        public string Name => "reparent_game_object";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object))
                return ToolResult.Error("game_object is required.");

            var go = EliToolHelpers.FindGameObject(input.game_object);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object}' not found.");

            Transform newParentTransform = null;
            if (!string.IsNullOrWhiteSpace(input.new_parent))
            {
                var parentGo = EliToolHelpers.FindGameObject(input.new_parent);
                if (parentGo == null)
                    return ToolResult.Error($"Parent GameObject '{input.new_parent}' not found.");
                newParentTransform = parentGo.transform;
            }

            Undo.SetTransformParent(go.transform, newParentTransform, "Unity Eli: Reparent");
            go.transform.SetParent(newParentTransform, input.world_position_stays);

            var parentName = newParentTransform != null ? $"'{input.new_parent}'" : "scene root";
            return ToolResult.Success($"'{input.game_object}' reparented to {parentName}.");
        }

        [Serializable]
        private class Input
        {
            public string game_object;
            public string new_parent;
            public bool world_position_stays = true;
        }
    }
}
