using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class SetRendererColorTool : IEliTool
    {
        public string Name => "set_renderer_color";
        public bool NeedsAssetRefresh => true;

        private const string MaterialsFolder = "Assets/Materials/FlatColor";
        private const string MaterialPrefix = "M_FlatColor_";
        private const string ShaderName = "Universal Render Pipeline/Lit";

        public string Execute(string inputJson)
        {
            // Check for batch mode: "objects" array
            var objectsArray = JsonHelper.ExtractArray(inputJson, "objects");
            if (objectsArray != "[]")
            {
                return ExecuteBatch(objectsArray);
            }

            // Single mode
            var input = JsonUtility.FromJson<Input>(inputJson);
            return ApplyColor(input);
        }

        private string ExecuteBatch(string objectsArrayJson)
        {
            var items = JsonHelper.ParseArray(objectsArrayJson);
            if (items.Count == 0)
                return ToolResult.Error("'objects' array is empty.");

            var results = new List<string>();
            var errors = new List<string>();

            foreach (var itemJson in items)
            {
                var input = JsonUtility.FromJson<Input>(itemJson);
                if (string.IsNullOrWhiteSpace(input.game_object_name))
                {
                    errors.Add("(skipped entry with no game_object_name)");
                    continue;
                }

                var result = ApplyColor(input);
                if (result.StartsWith("ERROR:"))
                    errors.Add($"'{input.game_object_name}': {result.Substring(6).Trim()}");
                else
                    results.Add(input.game_object_name);
            }

            var sb = new StringBuilder();
            sb.Append($"Set color on {results.Count} object(s): {string.Join(", ", results)}.");
            if (errors.Count > 0)
                sb.Append($" Errors: {string.Join("; ", errors)}.");

            return results.Count > 0 ? ToolResult.Success(sb.ToString()) : ToolResult.Error(sb.ToString());
        }

        private string ApplyColor(Input input)
        {
            if (string.IsNullOrWhiteSpace(input.game_object_name))
                return ToolResult.Error("game_object_name is required.");
            if (string.IsNullOrWhiteSpace(input.color_name))
                return ToolResult.Error("color_name is required.");

            var go = EliToolHelpers.FindGameObject(input.game_object_name);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object_name}' not found in the scene.");

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return ToolResult.Error($"GameObject '{input.game_object_name}' does not have a Renderer component.");

            var alpha = input.a == 0f ? 1f : input.a;
            var color = new Color(input.r, input.g, input.b, alpha);

            var materialName = MaterialPrefix + input.color_name;
            var materialPath = $"{MaterialsFolder}/{materialName}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (material == null)
            {
                material = CreateFlatColorMaterial(materialName, materialPath, color);
                if (material == null)
                    return ToolResult.Error("Failed to create material. Is the 'Universal Render Pipeline/Lit' shader available?");
            }
            else
            {
                SetMaterialColor(material, color);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }

            Undo.RecordObject(renderer, $"Unity Eli: Set color on {input.game_object_name}");
            renderer.sharedMaterial = material;

            return ToolResult.Success(
                $"Set '{input.game_object_name}' color to {input.color_name} " +
                $"(R:{input.r:F2} G:{input.g:F2} B:{input.b:F2} A:{alpha:F2}).");
        }

        private Material CreateFlatColorMaterial(string materialName, string materialPath, Color color)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[Unity Eli] Shader '{ShaderName}' not found.");
                return null;
            }

            if (!Directory.Exists(MaterialsFolder))
            {
                Directory.CreateDirectory(MaterialsFolder);
                AssetDatabase.ImportAsset(MaterialsFolder);
            }

            var material = new Material(shader) { name = materialName };
            SetMaterialColor(material, color);
            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();

            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);

            if (color.a < 1f)
            {
                if (material.HasProperty("_Surface"))
                {
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_Blend", 0f);
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    material.SetInt("_ZWrite", 0);
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
            }
            else
            {
                if (material.HasProperty("_Surface"))
                {
                    material.SetFloat("_Surface", 0f);
                    material.SetOverrideTag("RenderType", "Opaque");
                    material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
                    material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
                    material.SetInt("_ZWrite", 1);
                    material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
                    material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                }
            }
        }

        [Serializable]
        private class Input
        {
            public string game_object_name;
            public string color_name;
            public float r;
            public float g;
            public float b;
            public float a;
        }
    }
}
