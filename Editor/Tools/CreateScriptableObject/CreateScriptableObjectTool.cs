using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class CreateScriptableObjectTool : IEliTool
    {
        public string Name => "create_scriptable_object";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.type))
                return ToolResult.Error("type is required.");

            // Default path to Resources folder
            var path = input.path;
            if (string.IsNullOrWhiteSpace(path))
                path = "Assets/Resources";

            // Resolve the ScriptableObject type
            var soType = EliToolHelpers.ResolveType(input.type);
            if (soType == null)
                return ToolResult.Error($"Type '{input.type}' not found. Make sure the script has compiled.");

            if (!typeof(ScriptableObject).IsAssignableFrom(soType))
                return ToolResult.Error($"Type '{input.type}' is not a ScriptableObject.");

            // Determine file name
            var fileName = input.file_name;
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = input.type;

            if (!fileName.EndsWith(".asset"))
                fileName += ".asset";

            // Ensure directory exists
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);

            var fullPath = Path.Combine(path, fileName);

            // Check for existing asset
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fullPath) != null)
                return ToolResult.Error($"Asset already exists at '{fullPath}'.");

            // Create the ScriptableObject instance
            var instance = ScriptableObject.CreateInstance(soType);
            AssetDatabase.CreateAsset(instance, fullPath);
            AssetDatabase.SaveAssets();

            return ToolResult.Success($"Created {input.type} asset at '{fullPath}'.");
        }

        [Serializable]
        private class Input
        {
            public string type;
            public string path;
            public string file_name;
        }
    }
}
