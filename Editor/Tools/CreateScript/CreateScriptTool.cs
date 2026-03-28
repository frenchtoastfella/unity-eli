using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class CreateScriptTool : IEliTool
    {
        public string Name => "create_script";
        public bool NeedsAssetRefresh => true;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.file_path))
            {
                return ToolResult.Error("file_path is required.");
            }

            if (!input.file_path.EndsWith(".cs"))
            {
                return ToolResult.Error("file_path must end with '.cs'.");
            }

            if (string.IsNullOrWhiteSpace(input.content))
            {
                return ToolResult.Error("content is required.");
            }

            var fullPath = Path.Combine("Assets", input.file_path);
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(fullPath))
            {
                return ToolResult.Error($"File already exists at '{fullPath}'. Use edit_script to modify existing files.");
            }

            File.WriteAllText(fullPath, input.content);

            return ToolResult.Success(
                $"Script created at '{fullPath}'. " +
                "IMPORTANT: Unity will now compile this script. Wait for compilation to complete " +
                "(call refresh_assets with wait_for_compilation=true) before creating GameObjects " +
                "or adding this component — otherwise the scene may reload and lose objects.");
        }

        [System.Serializable]
        private class Input
        {
            public string file_path;
            public string content;
        }
    }
}
