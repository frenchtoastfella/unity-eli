using System;
using System.IO;
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

            var go = EliToolHelpers.FindGameObject(input.game_object);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object}' not found in the scene.");

            // Determine save path
            var folder = input.path;
            if (string.IsNullOrWhiteSpace(folder))
                folder = "Assets/Prefabs";

            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);

            var fileName = input.file_name;
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = go.name;

            if (!fileName.EndsWith(".prefab"))
                fileName += ".prefab";

            var fullPath = Path.Combine(folder, fileName);

            // Check for existing prefab
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(fullPath);
            if (existing != null)
            {
                if (input.overwrite)
                {
                    PrefabUtility.SaveAsPrefabAssetAndConnect(go, fullPath, InteractionMode.UserAction);
                    return ToolResult.Success(
                        $"Overwrote prefab at '{fullPath}' from scene GameObject '{go.name}'. " +
                        $"The scene instance is now linked to this prefab.");
                }
                else
                {
                    return ToolResult.Error(
                        $"Prefab already exists at '{fullPath}'. Set overwrite to true to replace it, " +
                        "or use a different file_name.");
                }
            }

            // Create the prefab and connect the scene instance to it
            var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(go, fullPath, InteractionMode.UserAction);
            if (prefab == null)
                return ToolResult.Error($"Failed to create prefab at '{fullPath}'.");

            int componentCount = prefab.GetComponents<Component>().Length;
            int childCount = prefab.transform.childCount;

            var summary = $"Created prefab at '{fullPath}' from scene GameObject '{go.name}' " +
                          $"({componentCount} components, {childCount} children). " +
                          "The scene instance is now linked to this prefab.";

            return ToolResult.Success(summary);
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
