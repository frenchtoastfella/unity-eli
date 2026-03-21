using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class SetAssetPropertyTool : IEliTool
    {
        public string Name => "set_asset_property";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.asset_path))
                return ToolResult.Error("asset_path is required.");
            if (string.IsNullOrWhiteSpace(input.property))
                return ToolResult.Error("property is required.");
            if (string.IsNullOrWhiteSpace(input.value))
                return ToolResult.Error("value is required.");

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(input.asset_path);
            if (asset == null)
                return ToolResult.Error($"Asset not found at '{input.asset_path}'.");

            var so = new SerializedObject(asset);
            var property = EliToolHelpers.FindProperty(so, input.property);

            if (property == null)
                return ToolResult.Error(
                    $"Property '{input.property}' not found on asset. " +
                    $"Available properties: {EliToolHelpers.ListProperties(so)}");

            so.Update();

            var error = EliToolHelpers.TrySetPropertyValue(property, input.value, out var resultDesc);
            if (error != null)
                return ToolResult.Error(error);

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();

            return ToolResult.Success(
                $"Set '{property.name}' on '{input.asset_path}' to {resultDesc}.");
        }

        [Serializable]
        private class Input
        {
            public string asset_path;
            public string property;
            public string value;
        }
    }
}
