using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class LookAtGameObjectTool : IEliTool
    {
        public string Name => "look_at_game_object";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object))
                return ToolResult.Error("game_object is required.");
            if (string.IsNullOrWhiteSpace(input.target_game_object))
                return ToolResult.Error("target_game_object is required.");

            var go = EliToolHelpers.FindGameObject(input.game_object);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object}' not found in the scene.");

            var targetGo = EliToolHelpers.FindGameObject(input.target_game_object);
            if (targetGo == null)
                return ToolResult.Error($"Target GameObject '{input.target_game_object}' not found in the scene.");

            Undo.RecordObject(go.transform, "Unity Eli: Look At GameObject");

            go.transform.LookAt(targetGo.transform);

            var euler = go.transform.eulerAngles;
            return ToolResult.Success(
                $"'{input.game_object}' is now looking at '{input.target_game_object}'. " +
                $"Rotation: ({euler.x:F1}, {euler.y:F1}, {euler.z:F1}).");
        }

        [Serializable]
        private class Input
        {
            public string game_object;
            public string target_game_object;
        }
    }
}
