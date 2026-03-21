using System.IO;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class EditScriptTool : IEliTool
    {
        public string Name => "edit_script";
        public bool NeedsAssetRefresh => true;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.file_path))
            {
                return ToolResult.Error("file_path is required.");
            }

            if (string.IsNullOrWhiteSpace(input.content))
            {
                return ToolResult.Error("content is required.");
            }

            var fullPath = Path.Combine("Assets", input.file_path);

            if (!File.Exists(fullPath))
            {
                return ToolResult.Error($"File not found at '{fullPath}'. Use create_script to create new files.");
            }

            File.WriteAllText(fullPath, input.content);

            return ToolResult.Success($"Script updated at '{fullPath}'.");
        }

        [System.Serializable]
        private class Input
        {
            public string file_path;
            public string content;
        }
    }
}
