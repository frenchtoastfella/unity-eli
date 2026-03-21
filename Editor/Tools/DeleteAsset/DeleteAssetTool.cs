using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class DeleteAssetTool : IEliTool
    {
        public string Name => "delete_asset";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.asset_path))
                return ToolResult.Error("asset_path is required.");

            // Verify the asset exists
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(input.asset_path);
            if (asset == null)
                return ToolResult.Error($"Asset not found at '{input.asset_path}'.");

            var assetType = asset.GetType().Name;
            var assetName = asset.name;

            // Move to trash (recoverable) rather than permanent delete
            if (!AssetDatabase.MoveAssetToTrash(input.asset_path))
                return ToolResult.Error($"Failed to delete asset at '{input.asset_path}'.");

            return ToolResult.Success($"Deleted {assetType} '{assetName}' from '{input.asset_path}' (moved to OS trash).");
        }

        [Serializable]
        private class Input
        {
            public string asset_path;
        }
    }
}
