using System;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class GetComponentPropertyTool : IEliTool
    {
        public string Name => "get_component_property";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object))
                return ToolResult.Error("game_object is required.");
            if (string.IsNullOrWhiteSpace(input.component_type))
                return ToolResult.Error("component_type is required.");

            var go = EliToolHelpers.FindGameObject(input.game_object);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object}' not found in the scene.");

            var compType = EliToolHelpers.ResolveType(input.component_type);
            if (compType == null)
                return ToolResult.Error($"Component type '{input.component_type}' not found.");

            var component = go.GetComponent(compType);
            if (component == null)
                return ToolResult.Error($"GameObject '{input.game_object}' does not have a '{input.component_type}' component.");

            var so = new SerializedObject(component);

            // If no property specified, list all properties and their values
            if (string.IsNullOrWhiteSpace(input.property_name))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Component: {input.component_type} on '{input.game_object}'");
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
            var property = EliToolHelpers.FindProperty(so, input.property_name);
            if (property == null)
                return ToolResult.Error(
                    $"Property '{input.property_name}' not found on '{input.component_type}'. " +
                    $"Available: {EliToolHelpers.ListProperties(so)}");

            return ToolResult.Success(
                $"{property.name} ({property.propertyType}) = {EliToolHelpers.ReadPropertyValue(property)}");
        }

        [Serializable]
        private class Input
        {
            public string game_object;
            public string component_type;
            public string property_name;
        }
    }
}
