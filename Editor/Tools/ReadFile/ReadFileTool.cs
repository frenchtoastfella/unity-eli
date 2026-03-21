using System.IO;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class ReadFileTool : IEliTool
    {
        public string Name => "read_file";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.file_path))
            {
                return ToolResult.Error("file_path is required.");
            }

            var fullPath = Path.Combine("Assets", input.file_path);

            if (!File.Exists(fullPath))
            {
                return ToolResult.Error($"File not found at '{fullPath}'.");
            }

            var content = File.ReadAllText(fullPath);
            return ToolResult.Success(content);
        }

        [System.Serializable]
        private class Input
        {
            public string file_path;
        }
    }
}
