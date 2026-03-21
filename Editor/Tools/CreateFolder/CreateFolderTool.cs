using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class CreateFolderTool : IEliTool
    {
        public string Name => "create_folder";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.path))
                return ToolResult.Error("path is required (e.g. 'Assets/Scripts/Enemies').");

            var path = input.path.TrimEnd('/').ToString();

            if (AssetDatabase.IsValidFolder(path))
                return ToolResult.Success($"Folder '{path}' already exists.");

            // Ensure each segment of the path exists
            var segments = path.Split('/');
            var current = segments[0];
            for (int i = 1; i < segments.Length; i++)
            {
                var next = current + "/" + segments[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    var guid = AssetDatabase.CreateFolder(current, segments[i]);
                    if (string.IsNullOrEmpty(guid))
                        return ToolResult.Error($"Failed to create folder segment '{next}'.");
                }
                current = next;
            }

            return ToolResult.Success($"Folder '{path}' created.");
        }

        [Serializable]
        private class Input
        {
            public string path;
        }
    }
}
