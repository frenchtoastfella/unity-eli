using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class SetSkyboxTool : IEliTool
    {
        public string Name => "set_skybox";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.material_path))
            {
                // Set skybox to none
                RenderSettings.skybox = null;

                // Mark the scene as dirty so the change is saved
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene());

                return ToolResult.Success("Skybox set to none.");
            }

            // Load a specific skybox material
            var fullPath = Path.Combine("Assets", input.material_path);
            var material = AssetDatabase.LoadAssetAtPath<Material>(fullPath);

            if (material == null)
            {
                return ToolResult.Error($"Material not found at '{fullPath}'.");
            }

            RenderSettings.skybox = material;

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            return ToolResult.Success($"Skybox set to '{fullPath}'.");
        }

        [Serializable]
        private class Input
        {
            public string material_path;
        }
    }
}
