using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class CreateMaterialTool : IEliTool
    {
        public string Name => "create_material";
        public bool NeedsAssetRefresh => true;

        private const string DefaultShader = "Universal Render Pipeline/Lit";

        public string Execute(string inputJson)
        {
            var assetPath = JsonHelper.ExtractString(inputJson, "asset_path");
            if (string.IsNullOrWhiteSpace(assetPath))
                return ToolResult.Error("'asset_path' is required.");

            if (!assetPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                return ToolResult.Error("'asset_path' must end with '.mat'.");

            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                return ToolResult.Error("'asset_path' must start with 'Assets/'.");

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                return ToolResult.Error($"Asset already exists at '{assetPath}'. Use set_material_property to modify it.");

            var shaderName = JsonHelper.ExtractString(inputJson, "shader") ?? DefaultShader;
            var shader = Shader.Find(shaderName);
            if (shader == null)
                return ToolResult.Error($"Shader '{shaderName}' not found. Make sure the shader is included in your project.");

            // Ensure parent directories exist
            var directory = Path.GetDirectoryName(assetPath).Replace("\\", "/");
            EliToolHelpers.EnsureDirectoryExists(directory);

            var material = new Material(shader) { name = Path.GetFileNameWithoutExtension(assetPath) };
            AssetDatabase.CreateAsset(material, assetPath);
            AssetDatabase.SaveAssets();

            return ToolResult.Success(
                $"Material created at '{assetPath}' with shader '{shaderName}'.\n" +
                "Use set_material_property to assign textures, colors, and other shader properties.");
        }

    }
}
