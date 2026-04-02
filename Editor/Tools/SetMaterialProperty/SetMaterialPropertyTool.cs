using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class SetMaterialPropertyTool : IEliTool
    {
        public string Name => "set_material_property";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var assetPath = JsonHelper.ExtractString(inputJson, "asset_path");
            if (string.IsNullOrWhiteSpace(assetPath))
                return ToolResult.Error("'asset_path' is required.");

            var property = JsonHelper.ExtractString(inputJson, "property");
            if (string.IsNullOrWhiteSpace(property))
                return ToolResult.Error("'property' is required.");

            var value = JsonHelper.ExtractString(inputJson, "value");
            if (string.IsNullOrWhiteSpace(value))
                return ToolResult.Error("'value' is required.");

            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
                return ToolResult.Error($"Material not found at '{assetPath}'.");

            var type = JsonHelper.ExtractString(inputJson, "type");
            if (string.IsNullOrEmpty(type))
                type = InferType(property);

            string resultDesc;
            switch (type)
            {
                case "texture":
                    resultDesc = SetTexture(material, property, value);
                    break;
                case "color":
                    resultDesc = SetColor(material, property, value);
                    break;
                case "float":
                    resultDesc = SetFloat(material, property, value);
                    break;
                case "int":
                    resultDesc = SetInt(material, property, value);
                    break;
                case "vector":
                    resultDesc = SetVector(material, property, value);
                    break;
                case "keyword":
                    resultDesc = SetKeyword(material, property, value);
                    break;
                default:
                    return ToolResult.Error($"Unknown type '{type}'. Use: texture, color, float, int, vector, keyword.");
            }

            if (resultDesc.StartsWith("ERROR:"))
                return resultDesc;

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();

            return ToolResult.Success($"Set '{property}' on '{assetPath}' to {resultDesc}.");
        }

        private static string InferType(string property)
        {
            var lower = property.ToLowerInvariant();
            if (lower.Contains("map") || lower.Contains("tex"))
                return "texture";
            if (lower.Contains("color"))
                return "color";
            return "float";
        }

        private static string SetTexture(Material material, string property, string texturePath)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
            if (texture == null)
                return ToolResult.Error($"Texture not found at '{texturePath}'.");

            if (!material.HasProperty(property))
                return ToolResult.Error($"Material does not have property '{property}'.");

            // Mark normal maps so Unity imports them correctly
            if (property == "_BumpMap" || property == "_NormalMap" ||
                property.ToLowerInvariant().Contains("normal"))
            {
                MarkAsNormalMap(texturePath);
            }

            material.SetTexture(property, texture);
            return $"texture '{texturePath}'";
        }

        private static void MarkAsNormalMap(string texturePath)
        {
            var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
        }

        private static string SetColor(Material material, string property, string value)
        {
            if (!material.HasProperty(property))
                return ToolResult.Error($"Material does not have property '{property}'.");

            var parts = value.Split(',');
            if (parts.Length < 3)
                return ToolResult.Error("Color value must be 'r,g,b' or 'r,g,b,a' (0-1 floats).");

            if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ||
                !float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float g) ||
                !float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float b))
                return ToolResult.Error("Failed to parse color components as floats.");

            float a = 1f;
            if (parts.Length >= 4)
                float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out a);

            material.SetColor(property, new Color(r, g, b, a));
            return $"color ({r:F2}, {g:F2}, {b:F2}, {a:F2})";
        }

        private static string SetFloat(Material material, string property, string value)
        {
            if (!material.HasProperty(property))
                return ToolResult.Error($"Material does not have property '{property}'.");

            if (!float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                return ToolResult.Error($"Failed to parse '{value}' as a float.");

            material.SetFloat(property, f);
            return $"float {f}";
        }

        private static string SetInt(Material material, string property, string value)
        {
            if (!material.HasProperty(property))
                return ToolResult.Error($"Material does not have property '{property}'.");

            if (!int.TryParse(value.Trim(), out int i))
                return ToolResult.Error($"Failed to parse '{value}' as an integer.");

            material.SetInt(property, i);
            return $"int {i}";
        }

        private static string SetVector(Material material, string property, string value)
        {
            if (!material.HasProperty(property))
                return ToolResult.Error($"Material does not have property '{property}'.");

            var parts = value.Split(',');
            if (parts.Length < 2)
                return ToolResult.Error("Vector value must be 'x,y', 'x,y,z', or 'x,y,z,w'.");

            float x = 0, y = 0, z = 0, w = 0;
            float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x);
            float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out y);
            if (parts.Length >= 3) float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out z);
            if (parts.Length >= 4) float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out w);

            material.SetVector(property, new Vector4(x, y, z, w));
            return $"vector ({x}, {y}, {z}, {w})";
        }

        private static string SetKeyword(Material material, string property, string value)
        {
            var enable = value.Trim().ToLowerInvariant();
            if (enable == "enable" || enable == "true" || enable == "1")
            {
                material.EnableKeyword(property);
                return $"keyword '{property}' enabled";
            }
            else if (enable == "disable" || enable == "false" || enable == "0")
            {
                material.DisableKeyword(property);
                return $"keyword '{property}' disabled";
            }
            return ToolResult.Error("Keyword value must be 'enable' or 'disable'.");
        }
    }
}
