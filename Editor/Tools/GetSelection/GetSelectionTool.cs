using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class GetSelectionTool : IEliTool
    {
        public string Name => "get_selection";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);
            bool includeComponents = input.include_components;
            bool includeProperties = input.include_properties;

            var activeObject = Selection.activeObject;
            var selectedObjects = Selection.objects;

            if (activeObject == null && (selectedObjects == null || selectedObjects.Length == 0))
                return ToolResult.Success("Nothing is currently selected.");

            var sb = new StringBuilder();

            // Report all selected objects
            if (selectedObjects != null && selectedObjects.Length > 1)
            {
                sb.AppendLine($"{selectedObjects.Length} objects selected:");
                sb.AppendLine();
            }

            foreach (var obj in selectedObjects)
            {
                if (obj == null) continue;
                AppendObjectInfo(sb, obj, includeComponents, includeProperties);
                sb.AppendLine();
            }

            return ToolResult.Success(sb.ToString().TrimEnd());
        }

        private static void AppendObjectInfo(StringBuilder sb, UnityEngine.Object obj, bool includeComponents, bool includeProperties)
        {
            var assetPath = AssetDatabase.GetAssetPath(obj);
            bool isAsset = !string.IsNullOrEmpty(assetPath);

            if (obj is GameObject go)
            {
                if (isAsset)
                {
                    // Prefab or other asset
                    sb.AppendLine($"[Asset] {go.name} ({obj.GetType().Name})");
                    sb.AppendLine($"  Path: {assetPath}");
                }
                else
                {
                    // Scene GameObject
                    sb.AppendLine($"[Scene] {go.name}");
                    sb.AppendLine($"  Position: {go.transform.position}");
                    sb.AppendLine($"  Rotation: {go.transform.eulerAngles}");
                    sb.AppendLine($"  Scale: {go.transform.localScale}");

                    var tag = go.tag != "Untagged" ? go.tag : null;
                    var layer = go.layer != 0 ? LayerMask.LayerToName(go.layer) : null;
                    if (tag != null) sb.AppendLine($"  Tag: {tag}");
                    if (layer != null) sb.AppendLine($"  Layer: {layer}");
                    if (!go.activeSelf) sb.AppendLine("  Active: false");

                    if (go.transform.parent != null)
                        sb.AppendLine($"  Parent: {go.transform.parent.name}");

                    if (go.transform.childCount > 0)
                        sb.AppendLine($"  Children: {go.transform.childCount}");
                }

                if (includeComponents)
                {
                    var components = go.GetComponents<Component>();
                    foreach (var comp in components)
                    {
                        if (comp == null || comp is Transform) continue;
                        sb.AppendLine($"  [{comp.GetType().Name}]");

                        if (includeProperties)
                            AppendSerializedProperties(sb, comp, "    ");
                    }
                }
            }
            else
            {
                // Non-GameObject asset (ScriptableObject, Material, Script, Texture, etc.)
                sb.AppendLine($"[Asset] {obj.name} ({obj.GetType().Name})");
                if (isAsset)
                    sb.AppendLine($"  Path: {assetPath}");

                if (includeProperties)
                    AppendSerializedProperties(sb, obj, "  ");
            }
        }

        private static void AppendSerializedProperties(StringBuilder sb, UnityEngine.Object obj, string indent)
        {
            var so = new SerializedObject(obj);
            var iter = so.GetIterator();

            if (!iter.NextVisible(true)) return;

            do
            {
                if (iter.name == "m_Script") continue;
                if (iter.name == "m_ObjectHideFlags") continue;

                var value = ReadPropertyValue(iter);
                sb.AppendLine($"{indent}{iter.displayName}: {value}");
            } while (iter.NextVisible(false));
        }

        private static string ReadPropertyValue(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.Boolean:
                    return property.boolValue.ToString();
                case SerializedPropertyType.Integer:
                    return property.intValue.ToString();
                case SerializedPropertyType.Float:
                    return property.floatValue.ToString("G");
                case SerializedPropertyType.String:
                    return string.IsNullOrEmpty(property.stringValue) ? "(empty)" : $"\"{property.stringValue}\"";
                case SerializedPropertyType.Enum:
                    if (property.enumValueIndex >= 0 && property.enumValueIndex < property.enumDisplayNames.Length)
                        return property.enumDisplayNames[property.enumValueIndex];
                    return property.enumValueIndex.ToString();
                case SerializedPropertyType.Vector2:
                    var v2 = property.vector2Value;
                    return $"({v2.x}, {v2.y})";
                case SerializedPropertyType.Vector3:
                    var v3 = property.vector3Value;
                    return $"({v3.x}, {v3.y}, {v3.z})";
                case SerializedPropertyType.Vector4:
                    var v4 = property.vector4Value;
                    return $"({v4.x}, {v4.y}, {v4.z}, {v4.w})";
                case SerializedPropertyType.Color:
                    var c = property.colorValue;
                    return $"({c.r:F2}, {c.g:F2}, {c.b:F2}, {c.a:F2})";
                case SerializedPropertyType.ObjectReference:
                    if (property.objectReferenceValue == null)
                        return "None";
                    var refPath = AssetDatabase.GetAssetPath(property.objectReferenceValue);
                    if (!string.IsNullOrEmpty(refPath))
                        return $"{property.objectReferenceValue.name} ({property.objectReferenceValue.GetType().Name}) at '{refPath}'";
                    return $"{property.objectReferenceValue.name} ({property.objectReferenceValue.GetType().Name})";
                case SerializedPropertyType.LayerMask:
                    return property.intValue.ToString();
                case SerializedPropertyType.Rect:
                    var r = property.rectValue;
                    return $"(x:{r.x}, y:{r.y}, w:{r.width}, h:{r.height})";
                case SerializedPropertyType.AnimationCurve:
                    return $"AnimationCurve ({property.animationCurveValue.length} keys)";
                default:
                    return $"({property.propertyType})";
            }
        }

        [Serializable]
        private class Input
        {
            public bool include_components;
            public bool include_properties;
        }
    }
}
