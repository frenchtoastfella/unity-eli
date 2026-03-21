using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class RemoveTagOrLayerTool : IEliTool
    {
        public string Name => "remove_tag_or_layer";
        public bool NeedsAssetRefresh => false;

        private static readonly string[] BuiltinTags =
            { "Untagged", "Respawn", "Finish", "EditorOnly", "MainCamera", "Player", "GameController" };

        private static readonly string[] BuiltinLayers =
            { "Default", "TransparentFX", "Ignore Raycast", "Water", "UI" };

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

            if (hasTag)
            {
                results.Add(RemoveTag(serializedObject, input.tag, input.untag_game_objects));
            }

            if (hasLayer)
            {
                results.Add(RemoveLayer(serializedObject, input.layer));
            }

            return ToolResult.Success(string.Join(" ", results));
        }

        private static string RemoveTag(SerializedObject serializedObject, string tagName, bool untagGameObjects)
        {
            // Cannot remove built-in tags
            foreach (var bt in BuiltinTags)
            {
                if (string.Equals(bt, tagName, StringComparison.OrdinalIgnoreCase))
                    return $"ERROR: Tag '{tagName}' is a built-in Unity tag and cannot be removed.";
            }

            var tagsProperty = serializedObject.FindProperty("tags");
            if (tagsProperty == null)
                return "ERROR: Could not find 'tags' property in TagManager.";

            // Find the tag
            int tagIndex = -1;
            for (int i = 0; i < tagsProperty.arraySize; i++)
            {
                if (tagsProperty.GetArrayElementAtIndex(i).stringValue == tagName)
                {
                    tagIndex = i;
                    break;
                }
            }

            if (tagIndex < 0)
                return $"Tag '{tagName}' does not exist in the project.";

            // Optionally untag all GameObjects using this tag before removing it
            if (untagGameObjects)
            {
                var allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
                int untaggedCount = 0;
                foreach (var go in allObjects)
                {
                    if (!go.scene.isLoaded) continue;
                    try
                    {
                        if (go.CompareTag(tagName))
                        {
                            Undo.RecordObject(go, $"Unity Eli: Untag '{go.name}'");
                            go.tag = "Untagged";
                            untaggedCount++;
                        }
                    }
                    catch
                    {
                        // CompareTag can throw if the tag is already invalid
                    }
                }

                if (untaggedCount > 0)
                {
                    tagsProperty.DeleteArrayElementAtIndex(tagIndex);
                    serializedObject.ApplyModifiedProperties();
                    return $"Tag '{tagName}' removed. Untagged {untaggedCount} GameObject(s) that were using it.";
                }
            }

            tagsProperty.DeleteArrayElementAtIndex(tagIndex);
            serializedObject.ApplyModifiedProperties();

            return $"Tag '{tagName}' removed successfully.";
        }

        private static string RemoveLayer(SerializedObject serializedObject, string layerName)
        {
            // Cannot remove built-in layers
            foreach (var bl in BuiltinLayers)
            {
                if (string.Equals(bl, layerName, StringComparison.OrdinalIgnoreCase))
                    return $"ERROR: Layer '{layerName}' is a built-in Unity layer and cannot be removed.";
            }

            var layersProperty = serializedObject.FindProperty("layers");
            if (layersProperty == null)
                return "ERROR: Could not find 'layers' property in TagManager.";

            // Find the layer in user slots (6-31)
            int layerIndex = -1;
            for (int i = 6; i < layersProperty.arraySize && i < 32; i++)
            {
                if (layersProperty.GetArrayElementAtIndex(i).stringValue == layerName)
                {
                    layerIndex = i;
                    break;
                }
            }

            if (layerIndex < 0)
                return $"Layer '{layerName}' does not exist in the project.";

            // Clear the slot (layers are fixed-size, so we just empty the string)
            layersProperty.GetArrayElementAtIndex(layerIndex).stringValue = "";
            serializedObject.ApplyModifiedProperties();

            return $"Layer '{layerName}' removed from index {layerIndex}.";
        }

        [Serializable]
        private class Input
        {
            public string tag;
            public string layer;
            public bool untag_game_objects;
        }
    }
}
