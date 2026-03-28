using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

            // Support comma-separated names for batch deletion
            var names = input.game_object.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();

            var deleted = new List<string>();
            var errors = new List<string>();

            foreach (var name in names)
            {
                var go = EliToolHelpers.FindGameObject(name);
                if (go == null)
                {
                    errors.Add($"'{name}' not found");
                    continue;
                }

                int childCount = go.transform.childCount;
                Undo.DestroyObjectImmediate(go);
                var info = childCount > 0 ? $"'{name}' (+{childCount} children)" : $"'{name}'";
                deleted.Add(info);
            }

            if (deleted.Count == 0)
                return ToolResult.Error($"Not found: {string.Join(", ", errors)}.");

            var sb = new StringBuilder();
            sb.Append($"Deleted {deleted.Count}: {string.Join(", ", deleted)}.");
            if (errors.Count > 0)
                sb.Append($" Not found: {string.Join(", ", errors)}.");

            return ToolResult.Success(sb.ToString());
        }

        [Serializable]
        private class Input
        {
            public string game_object;
        }
    }
}
