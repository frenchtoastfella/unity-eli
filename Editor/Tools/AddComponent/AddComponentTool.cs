using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            if (EditorApplication.isCompiling)
            {
                return ToolResult.Error(
                    "Unity is currently compiling scripts. Wait for compilation to finish " +
                    "(call refresh_assets with wait_for_compilation=true) then try again.");
            }

            if (EditorApplication.isPlaying)
            {
                return ToolResult.Error(
                    "Unity is in Play mode. Scene changes made during Play mode are lost when Play mode stops. " +
                    "Use play_mode action='stop' first, then retry.");
            }

            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object_name))
                return ToolResult.Error("game_object_name is required.");
            if (string.IsNullOrWhiteSpace(input.component_type))
                return ToolResult.Error("component_type is required.");

            // Resolve the component type once
            var componentType = EliToolHelpers.ResolveType(input.component_type);
            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                return ToolResult.Error(
                    $"Component type '{input.component_type}' not found. " +
                    "Make sure the script exists and has compiled successfully. " +
                    "If you just created the script, call refresh_assets with wait_for_compilation=true first.");
            }

            // Support comma-separated names for batch add
            var names = input.game_object_name.Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToArray();
            var added = new List<string>();
            var errors = new List<string>();

            foreach (var name in names)
            {
                var go = EliToolHelpers.FindGameObject(name);
                if (go == null)
                {
                    errors.Add($"'{name}' not found");
                    continue;
                }

                if (!AllowMultiple(componentType) && go.GetComponent(componentType) != null)
                {
                    errors.Add($"'{name}' already has '{input.component_type}'");
                    continue;
                }

                var component = Undo.AddComponent(go, componentType);
                if (component == null)
                {
                    errors.Add($"Failed to add to '{name}'");
                    continue;
                }

                added.Add(name);
            }

            if (added.Count == 0 && errors.Count > 0)
                return ToolResult.Error($"Failed: {string.Join("; ", errors)}.");

            var sb = new StringBuilder();
            sb.Append($"Added '{input.component_type}' to {added.Count} object(s): {string.Join(", ", added)}.");
            if (errors.Count > 0)
                sb.Append($" Errors: {string.Join("; ", errors)}.");

            return ToolResult.Success(sb.ToString());
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
