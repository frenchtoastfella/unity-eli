using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class SetComponentReferenceTool : IEliTool
    {
        public string Name => "set_component_reference";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.target_game_object))
                return ToolResult.Error("target_game_object is required.");
            if (string.IsNullOrWhiteSpace(input.target_component))
                return ToolResult.Error("target_component is required.");
            if (string.IsNullOrWhiteSpace(input.field_name))
                return ToolResult.Error("field_name is required.");
            if (string.IsNullOrWhiteSpace(input.source_game_object))
                return ToolResult.Error("source_game_object is required.");

            // Find the target GameObject
            var targetGo = EliToolHelpers.FindGameObject(input.target_game_object);
            if (targetGo == null)
                return ToolResult.Error($"Target GameObject '{input.target_game_object}' not found in the scene.");

            // Find the component on the target
            var componentType = EliToolHelpers.ResolveType(input.target_component);
            if (componentType == null)
                return ToolResult.Error($"Component type '{input.target_component}' not found.");

            var component = targetGo.GetComponent(componentType);
            if (component == null)
                return ToolResult.Error($"GameObject '{input.target_game_object}' does not have a '{input.target_component}' component.");

            // Open a SerializedObject to access the field
            var serializedObject = new SerializedObject(component);
            var property = serializedObject.FindProperty(input.field_name);
            if (property == null)
                return ToolResult.Error($"Field '{input.field_name}' not found on '{input.target_component}'. Make sure the field is serialized ([SerializeField] or public).");

            if (property.propertyType != SerializedPropertyType.ObjectReference)
                return ToolResult.Error($"Field '{input.field_name}' is not an object reference field (type: {property.propertyType}).");

            // Find the source GameObject
            var sourceGo = EliToolHelpers.FindGameObject(input.source_game_object);
            if (sourceGo == null)
                return ToolResult.Error($"Source GameObject '{input.source_game_object}' not found in the scene.");

            // Resolve what to assign
            UnityEngine.Object valueToAssign = null;
            string assignedTypeName;

            if (!string.IsNullOrWhiteSpace(input.source_component))
            {
                // Explicit component type specified
                var sourceCompType = EliToolHelpers.ResolveType(input.source_component);
                if (sourceCompType == null)
                    return ToolResult.Error($"Source component type '{input.source_component}' not found.");

                if (typeof(Component).IsAssignableFrom(sourceCompType))
                {
                    var comp = sourceGo.GetComponent(sourceCompType);
                    if (comp == null)
                        return ToolResult.Error($"Source GameObject '{input.source_game_object}' does not have a '{input.source_component}' component.");
                    valueToAssign = comp;
                    assignedTypeName = input.source_component;
                }
                else
                {
                    return ToolResult.Error($"'{input.source_component}' is not a Component type.");
                }
            }
            else
            {
                // Auto-detect: figure out the field's expected type and find the matching component
                var fieldTypeName = GetFieldTypeName(property);
                if (string.IsNullOrEmpty(fieldTypeName))
                    return ToolResult.Error(
                        $"Could not determine the expected type for field '{input.field_name}'. " +
                        $"Try specifying 'source_component' explicitly.");

                valueToAssign = ResolveSourceValue(sourceGo, fieldTypeName);
                assignedTypeName = fieldTypeName;

                if (valueToAssign == null)
                    return ToolResult.Error(
                        $"Could not find a matching reference on '{input.source_game_object}' for field '{input.field_name}' " +
                        $"(expected type: {fieldTypeName}). Try specifying 'source_component' explicitly.");
            }

            // Assign the value
            serializedObject.Update();
            property.objectReferenceValue = valueToAssign;
            serializedObject.ApplyModifiedProperties();

            return ToolResult.Success(
                $"Set '{input.target_component}.{input.field_name}' on '{input.target_game_object}' " +
                $"to {assignedTypeName} from '{input.source_game_object}'.");
        }

        private static UnityEngine.Object ResolveSourceValue(GameObject sourceGo, string fieldTypeName)
        {
            if (string.IsNullOrEmpty(fieldTypeName))
                return null;

            // If the field type is GameObject, return the GameObject itself
            if (fieldTypeName == "GameObject" || fieldTypeName == "UnityEngine.GameObject")
                return sourceGo;

            // If the field type is Transform, return the transform
            if (fieldTypeName == "Transform" || fieldTypeName == "UnityEngine.Transform")
                return sourceGo.transform;

            // Try to find a component matching the field type name on the source GameObject
            var resolvedType = EliToolHelpers.ResolveType(fieldTypeName);
            if (resolvedType != null && typeof(Component).IsAssignableFrom(resolvedType))
            {
                var comp = sourceGo.GetComponent(resolvedType);
                if (comp != null)
                    return comp;
            }

            // Fallback: search all components on the source and find one whose type name matches
            foreach (var comp in sourceGo.GetComponents<Component>())
            {
                if (comp == null) continue;
                var compType = comp.GetType();
                if (compType.Name == fieldTypeName || compType.FullName == fieldTypeName)
                    return comp;
            }

            return null;
        }

        private static string GetFieldTypeName(SerializedProperty property)
        {
            // SerializedProperty.type for object references looks like "PPtr<$TextMeshProUGUI>"
            // We extract the type name from it
            var typeStr = property.type;
            if (typeStr.StartsWith("PPtr<$") && typeStr.EndsWith(">"))
            {
                return typeStr.Substring(6, typeStr.Length - 7);
            }
            if (typeStr.StartsWith("PPtr<") && typeStr.EndsWith(">"))
            {
                return typeStr.Substring(5, typeStr.Length - 6);
            }
            return typeStr;
        }

        [Serializable]
        private class Input
        {
            public string target_game_object;
            public string target_component;
            public string field_name;
            public string source_game_object;
            public string source_component;
        }
    }
}
