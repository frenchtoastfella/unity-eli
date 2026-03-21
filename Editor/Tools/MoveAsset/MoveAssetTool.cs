using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class MoveAssetTool : IEliTool
    {
        public string Name => "move_asset";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.from_path))
                return ToolResult.Error("from_path is required.");
            if (string.IsNullOrWhiteSpace(input.to_path))
                return ToolResult.Error("to_path is required.");

            // Verify source exists
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(input.from_path);
            var folderExists = AssetDatabase.IsValidFolder(input.from_path);
            if (asset == null && !folderExists)
                return ToolResult.Error($"Asset or folder not found at '{input.from_path}'.");

            var error = AssetDatabase.MoveAsset(input.from_path, input.to_path);
            if (!string.IsNullOrEmpty(error))
                return ToolResult.Error($"Move failed: {error}");

            return ToolResult.Success($"Moved '{input.from_path}' to '{input.to_path}'.");
        }

        [Serializable]
        private class Input
        {
            public string from_path;
            public string to_path;
        }
    }
}
