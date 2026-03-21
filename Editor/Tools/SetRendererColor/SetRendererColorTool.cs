using System;
using System.IO;
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
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.game_object_name))
            {
                return ToolResult.Error("game_object_name is required.");
            }

            if (string.IsNullOrWhiteSpace(input.color_name))
            {
                return ToolResult.Error("color_name is required.");
            }

            // Find the GameObject
            var go = EliToolHelpers.FindGameObject(input.game_object_name);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object_name}' not found in the scene.");

            // Get the Renderer component (MeshRenderer, SkinnedMeshRenderer, etc.)
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                return ToolResult.Error($"GameObject '{input.game_object_name}' does not have a Renderer component.");
            }

            // Resolve the color
            var alpha = input.a == 0f ? 1f : input.a; // Default to opaque if not specified
            var color = new Color(input.r, input.g, input.b, alpha);

            // Try to find an existing material with this name
            var materialName = MaterialPrefix + input.color_name;
            var materialPath = $"{MaterialsFolder}/{materialName}.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);

            if (material == null)
            {
                // Create the material
                material = CreateFlatColorMaterial(materialName, materialPath, color);
                if (material == null)
                {
                    return ToolResult.Error("Failed to create material. Is the 'Universal Render Pipeline/Lit' shader available?");
                }
            }
            else
            {
                // Material exists � update its color in case it differs
                SetMaterialColor(material, color);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();
            }

            // Assign the material to the renderer
            Undo.RecordObject(renderer, $"Unity Eli: Set color on {input.game_object_name}");
            renderer.sharedMaterial = material;

            return ToolResult.Success(
                $"Set '{input.game_object_name}' renderer color to {input.color_name} " +
                $"(R:{input.r:F2} G:{input.g:F2} B:{input.b:F2} A:{alpha:F2}) " +
                $"using material '{materialPath}'.");
        }

        private Material CreateFlatColorMaterial(string materialName, string materialPath, Color color)
        {
            // Find the URP Lit shader
            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError($"[Unity Eli] Shader '{ShaderName}' not found.");
                return null;
            }

            // Ensure the directory exists
            if (!Directory.Exists(MaterialsFolder))
            {
                Directory.CreateDirectory(MaterialsFolder);
                // We need to import the folder so Unity knows about it
                AssetDatabase.ImportAsset(MaterialsFolder);
            }

            // Create the material
            var material = new Material(shader)
            {
                name = materialName
            };

            SetMaterialColor(material, color);

            // Save as asset
            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[Unity Eli] Created material at '{materialPath}'.");
            return material;
        }

        private static void SetMaterialColor(Material material, Color color)
        {
            // URP Lit shader uses _BaseColor for the albedo color
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            // Also set _Color for compatibility (some shaders and the material inspector use this)
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            // If alpha < 1, set the surface type to Transparent
            if (color.a < 1f)
            {
                // URP Lit: _Surface 0=Opaque, 1=Transparent
                if (material.HasProperty("_Surface"))
                {
                    material.SetFloat("_Surface", 1f);
                    material.SetFloat("_Blend", 0f); // Alpha blend
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
