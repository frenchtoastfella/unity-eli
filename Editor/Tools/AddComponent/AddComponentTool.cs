using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class AddComponentTool : IEliTool
    {
        public string Name => "add_component";
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

            // Find the GameObject in the scene
            var go = EliToolHelpers.FindGameObject(input.game_object_name);
            if (go == null)
            {
                return ToolResult.Error($"GameObject '{input.game_object_name}' not found in the scene.");
            }

            // Resolve the component type
            var componentType = EliToolHelpers.ResolveType(input.component_type);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                return ToolResult.Error($"Component type '{input.component_type}' not found. Make sure the script exists and has compiled successfully.");
            }

            // Check if a component of this type is already on the GameObject
            if (!AllowMultiple(componentType) && go.GetComponent(componentType) != null)
            {
                return ToolResult.Error($"GameObject '{input.game_object_name}' already has a '{input.component_type}' component.");
            }

            // Add the component
            var component = Undo.AddComponent(go, componentType);
            if (component == null)
            {
                return ToolResult.Error($"Failed to add '{input.component_type}' to '{input.game_object_name}'.");
            }

            return ToolResult.Success($"Added '{input.component_type}' to GameObject '{input.game_object_name}'.");
        }

        private static bool AllowMultiple(Type componentType)
        {
            var attrs = componentType.GetCustomAttributes(typeof(DisallowMultipleComponent), true);
            return attrs.Length == 0;
        }

        [Serializable]
        private class Input
        {
            public string game_object_name;
            public string component_type;
        }
    }
}
