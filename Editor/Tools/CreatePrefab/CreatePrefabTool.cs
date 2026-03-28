using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class CreatePrefabTool : IEliTool
    {
        public string Name => "create_prefab";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object))
                return ToolResult.Error("game_object is required.");

            var folder = input.path;
            if (string.IsNullOrWhiteSpace(folder))
                folder = "Assets/Prefabs";

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            // Support comma-separated names for batch prefab creation
            var names = input.game_object.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
            var created = new List<string>();
            var errors = new List<string>();

            foreach (var name in names)
            {
                var go = EliToolHelpers.FindGameObject(name);
                if (go == null)
                {
                    errors.Add($"'{name}' not found");
                    continue;
                }

                var fileName = name;
                if (!fileName.EndsWith(".prefab"))
                    fileName += ".prefab";

                var fullPath = Path.Combine(folder, fileName);

                var existing = AssetDatabase.LoadAssetAtPath<GameObject>(fullPath);
                if (existing != null)
                {
                    if (input.overwrite)
                    {
                        PrefabUtility.SaveAsPrefabAssetAndConnect(go, fullPath, InteractionMode.UserAction);
                        created.Add($"'{name}' (overwritten)");
                    }
                    else
                    {
                        errors.Add($"'{name}' already exists at '{fullPath}' (set overwrite=true to replace)");
                    }
                    continue;
                }

                var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, fullPath, InteractionMode.UserAction);
                if (prefab == null)
                {
                    errors.Add($"Failed to create '{fullPath}'");
                    continue;
                }

                created.Add($"'{name}' → {fullPath}");
            }

            // Handle single-object mode with custom file_name
            if (names.Length == 1 && created.Count == 0 && errors.Count == 0)
            {
                // This shouldn't happen but handle gracefully
                return ToolResult.Error($"GameObject '{names[0]}' not found.");
            }

            if (created.Count == 0 && errors.Count > 0)
                return ToolResult.Error($"Failed: {string.Join("; ", errors)}.");

            var sb = new StringBuilder();
            sb.Append($"Created {created.Count} prefab(s): {string.Join(", ", created)}.");
            if (errors.Count > 0)
                sb.Append($" Errors: {string.Join("; ", errors)}.");

            return ToolResult.Success(sb.ToString());
        }

        [Serializable]
        private class Input
        {
            public string game_object;
            public string path;
            public string file_name;
            public bool overwrite;
        }
    }
}
