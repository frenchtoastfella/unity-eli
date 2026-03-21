using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class AddTagOrLayerTool : IEliTool
    {
        public string Name => "add_tag_or_layer";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            bool hasTag = !string.IsNullOrWhiteSpace(input.tag);
            bool hasLayer = !string.IsNullOrWhiteSpace(input.layer);

            if (!hasTag && !hasLayer)
                return ToolResult.Error("At least one of 'tag' or 'layer' is required.");

            var tagManager = AssetDatabase.LoadMainAssetAtPath("ProjectSettings/TagManager.asset");
            if (tagManager == null)
                return ToolResult.Error("Could not load TagManager.asset from ProjectSettings.");

            var serializedObject = new SerializedObject(tagManager);
            var results = new System.Collections.Generic.List<string>();

            // --- Add Tag ---
            if (hasTag)
            {
                var tagResult = AddTag(serializedObject, input.tag);
                results.Add(tagResult);
            }

            // --- Add Layer ---
            if (hasLayer)
            {
                var layerResult = AddLayer(serializedObject, input.layer);
                results.Add(layerResult);
            }

            // --- Optionally assign the tag to a GameObject ---
            if (hasTag && !string.IsNullOrWhiteSpace(input.game_object))
            {
                var go = EliToolHelpers.FindGameObject(input.game_object);
                if (go == null)
                {
                    results.Add($"WARNING: GameObject '{input.game_object}' not found. Tag was added but not assigned.");
                }
                else
                {
                    // Verify the tag exists before assigning
                    bool tagExists = false;
                    try
                    {
                        // This will throw if the tag doesn't exist
                        go.CompareTag(input.tag);
                        tagExists = true;
                    }
                    catch
                    {
                        // Check our tags array directly
                        var tagsProperty = serializedObject.FindProperty("tags");
                        for (int i = 0; i < tagsProperty.arraySize; i++)
                        {
                            if (tagsProperty.GetArrayElementAtIndex(i).stringValue == input.tag)
                            {
                                tagExists = true;
                                break;
                            }
                        }
                    }

                    if (tagExists)
                    {
                        Undo.RecordObject(go, $"Unity Eli: Set tag '{input.tag}' on '{input.game_object}'");
                        go.tag = input.tag;
                        results.Add($"Assigned tag '{input.tag}' to GameObject '{input.game_object}'.");
                    }
                    else
                    {
                        results.Add($"WARNING: Could not verify tag '{input.tag}' exists. Tag assignment skipped.");
                    }
                }
            }

            return ToolResult.Success(string.Join(" ", results));
        }

        private static string AddTag(SerializedObject serializedObject, string tagName)
        {
            // Check built-in tags first
            string[] builtinTags = { "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController" };
            foreach (var bt in builtinTags)
            {
                if (string.Equals(bt, tagName, StringComparison.OrdinalIgnoreCase))
                    return $"Tag '{tagName}' is a built-in Unity tag and already exists.";
            }

            var tagsProperty = serializedObject.FindProperty("tags");
            if (tagsProperty == null)
                return "ERROR: Could not find 'tags' property in TagManager.";

            // Check if tag already exists
            for (int i = 0; i < tagsProperty.arraySize; i++)
            {
                if (tagsProperty.GetArrayElementAtIndex(i).stringValue == tagName)
                    return $"Tag '{tagName}' already exists.";
            }

            // Add the tag
            var index = tagsProperty.arraySize;
            tagsProperty.InsertArrayElementAtIndex(index);
            tagsProperty.GetArrayElementAtIndex(index).stringValue = tagName;
            serializedObject.ApplyModifiedProperties();

            return $"Tag '{tagName}' added successfully.";
        }

        private static string AddLayer(SerializedObject serializedObject, string layerName)
        {
            var layersProperty = serializedObject.FindProperty("layers");
            if (layersProperty == null)
                return "ERROR: Could not find 'layers' property in TagManager.";

            // Check if the layer already exists
            for (int i = 0; i < layersProperty.arraySize; i++)
            {
                if (layersProperty.GetArrayElementAtIndex(i).stringValue == layerName)
                    return $"Layer '{layerName}' already exists at index {i}.";
            }

            // Find the first empty user layer slot (indices 6-31; 0-5 are built-in)
            int emptySlot = -1;
            for (int i = 6; i < layersProperty.arraySize && i < 32; i++)
            {
                if (string.IsNullOrEmpty(layersProperty.GetArrayElementAtIndex(i).stringValue))
                {
                    emptySlot = i;
                    break;
                }
            }

            if (emptySlot < 0)
                return "ERROR: No empty user layer slots available (layers 6-31 are all in use).";

            layersProperty.GetArrayElementAtIndex(emptySlot).stringValue = layerName;
            serializedObject.ApplyModifiedProperties();

            return $"Layer '{layerName}' added at index {emptySlot}.";
        }

        [Serializable]
        private class Input
        {
            public string tag;
            public string layer;
            public string game_object;
        }
    }
}
