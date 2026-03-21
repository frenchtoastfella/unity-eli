using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class SetComponentPropertyTool : IEliTool
    {
        public string Name => "set_component_property";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object))
                return ToolResult.Error("game_object is required.");
            if (string.IsNullOrWhiteSpace(input.component))
                return ToolResult.Error("component is required.");
            if (string.IsNullOrWhiteSpace(input.property))
                return ToolResult.Error("property is required.");
            if (string.IsNullOrWhiteSpace(input.value))
                return ToolResult.Error("value is required.");

            var go = EliToolHelpers.FindGameObject(input.game_object);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object}' not found in the scene.");

            var componentType = EliToolHelpers.ResolveType(input.component);
            if (componentType == null)
                return ToolResult.Error($"Component type '{input.component}' not found.");

            // Redirect Transform/RectTransform to the dedicated set_transform tool
            if (componentType == typeof(Transform) || componentType == typeof(RectTransform))
                return ToolResult.Error(
                    $"Transform properties cannot be set with this tool. " +
                    $"Use the 'set_transform' tool instead (supports position, rotation, scale, and RectTransform properties).");

            var component = go.GetComponent(componentType);
            if (component == null)
                return ToolResult.Error($"GameObject '{input.game_object}' does not have a '{input.component}' component.");

            var so = new SerializedObject(component);
            var property = EliToolHelpers.FindProperty(so, input.property);

            if (property == null)
                return ToolResult.Error(
                    $"Property '{input.property}' not found on '{input.component}'. " +
                    "Use the serialized field name (e.g. 'm_UseGravity', 'm_IsKinematic', 'm_Mass').");

            so.Update();

            var error = EliToolHelpers.TrySetPropertyValue(property, input.value, out var resultDesc);
            if (error != null)
                return ToolResult.Error(error);

            so.ApplyModifiedProperties();

            return ToolResult.Success(
                $"Set '{input.component}.{property.name}' on '{input.game_object}' to {resultDesc}.");
        }

        [Serializable]
        private class Input
        {
            public string game_object;
            public string component;
            public string property;
            public string value;
        }
    }
}
