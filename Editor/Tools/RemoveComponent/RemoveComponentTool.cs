using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class RemoveComponentTool : IEliTool
    {
        public string Name => "remove_component";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object_name))
            {
                return ToolResult.Error("game_object_name is required.");
            }

            if (string.IsNullOrWhiteSpace(input.component_type))
            {
                return ToolResult.Error("component_type is required.");
            }

            // Prevent removing Transform
            if (input.component_type == "Transform" || input.component_type == "RectTransform")
            {
                return ToolResult.Error("Cannot remove Transform or RectTransform components.");
            }

            // Find the GameObject in the scene
            var go = EliToolHelpers.FindGameObject(input.game_object_name);
            if (go == null)
            {
                return ToolResult.Error($"GameObject '{input.game_object_name}' not found in the scene.");
            }

            // Find the component on the GameObject
            var componentType = EliToolHelpers.ResolveType(input.component_type);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                return ToolResult.Error($"Component type '{input.component_type}' not found.");
            }

            var component = go.GetComponent(componentType);
            if (component == null)
            {
                return ToolResult.Error($"GameObject '{input.game_object_name}' does not have a '{input.component_type}' component.");
            }

            // Check for dependencies - other components might require this one
            var dependentComponents = go.GetComponents<Component>();
            foreach (var other in dependentComponents)
            {
                if (other == null || other == component) continue;

                var requireAttrs = other.GetType().GetCustomAttributes(typeof(RequireComponent), true);
                foreach (RequireComponent req in requireAttrs)
                {
                    if (req.m_Type0 == componentType || req.m_Type1 == componentType || req.m_Type2 == componentType)
                    {
                        return ToolResult.Error(
                            $"Cannot remove '{input.component_type}' because '{other.GetType().Name}' depends on it. Remove '{other.GetType().Name}' first.");
                    }
                }
            }

            Undo.DestroyObjectImmediate(component);

            return ToolResult.Success($"Removed '{input.component_type}' from GameObject '{input.game_object_name}'.");
        }

        [Serializable]
        private class Input
        {
            public string game_object_name;
            public string component_type;
        }
    }
}
