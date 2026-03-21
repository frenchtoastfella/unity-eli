using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class GetAssetPropertyTool : IEliTool
    {
        public string Name => "get_asset_property";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.asset_path))
                return ToolResult.Error("asset_path is required.");

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(input.asset_path);
            if (asset == null)
                return ToolResult.Error($"Asset not found at '{input.asset_path}'.");

            var so = new SerializedObject(asset);

            // If no property specified, list all properties and their values
            if (string.IsNullOrWhiteSpace(input.property))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Asset: {input.asset_path} ({so.targetObject.GetType().Name})");
                sb.AppendLine();

                var iter = so.GetIterator();
                if (iter.NextVisible(true))
                {
                    do
                    {
                        if (iter.name == "m_Script") continue;
                        sb.AppendLine($"  {iter.name} ({iter.propertyType}) = {EliToolHelpers.ReadPropertyValue(iter)}");
                    } while (iter.NextVisible(false));
                }

                return ToolResult.Success(sb.ToString());
            }

            // Find the specific property
            var property = EliToolHelpers.FindProperty(so, input.property);
            if (property == null)
                return ToolResult.Error(
                    $"Property '{input.property}' not found on asset. " +
                    $"Available properties: {EliToolHelpers.ListProperties(so)}");

            return ToolResult.Success(
                $"{property.name} ({property.propertyType}) = {EliToolHelpers.ReadPropertyValue(property)}");
        }

        [Serializable]
        private class Input
        {
            public string asset_path;
            public string property;
        }
    }
}
