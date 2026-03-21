using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class DeleteGameObjectTool : IEliTool
    {
        public string Name => "delete_game_object";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object))
                return ToolResult.Error("game_object is required.");

            var go = EliToolHelpers.FindGameObject(input.game_object);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object}' not found in the scene.");

            int childCount = go.transform.childCount;
            string summary = childCount > 0
                ? $"Deleted '{input.game_object}' and its {childCount} child object(s)."
                : $"Deleted '{input.game_object}'.";

            Undo.DestroyObjectImmediate(go);

            return ToolResult.Success(summary);
        }

        [Serializable]
        private class Input
        {
            public string game_object;
        }
    }
}
