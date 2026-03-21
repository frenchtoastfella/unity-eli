using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;

namespace UnityEli.Editor.Tools
{
    public class ManageBuildScenesTool : IEliTool
    {
        public string Name => "manage_build_scenes";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var action = JsonHelper.ExtractString(inputJson, "action") ?? "list";

            switch (action.ToLowerInvariant())
            {
                case "list":    return ListScenes();
                case "add":     return AddScene(inputJson);
                case "remove":  return RemoveScene(inputJson);
                case "set":     return SetScenes(inputJson);
                case "enable":  return SetEnabled(inputJson, true);
                case "disable": return SetEnabled(inputJson, false);
                default:
                    return ToolResult.Error($"Unknown action '{action}'. Valid: list, add, remove, set, enable, disable.");
            }
        }

        private static string ListScenes()
        {
            var scenes = EditorBuildSettings.scenes;
            if (scenes.Length == 0)
                return ToolResult.Success("No scenes in build settings.");

            var sb = new StringBuilder();
            sb.AppendLine("Build scenes:");
            for (int i = 0; i < scenes.Length; i++)
                sb.AppendLine($"  [{i}] {scenes[i].path} (enabled: {scenes[i].enabled})");
            return ToolResult.Success(sb.ToString());
        }

        private static string AddScene(string inputJson)
        {
            var scenePath = JsonHelper.ExtractString(inputJson, "scene_path");
            if (string.IsNullOrWhiteSpace(scenePath))
                return ToolResult.Error("scene_path is required for 'add'.");

            var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            if (asset == null)
                return ToolResult.Error($"Scene not found at '{scenePath}'.");

            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            // Check if already present
            for (int i = 0; i < scenes.Count; i++)
            {
                if (scenes[i].path == scenePath)
                    return ToolResult.Success($"Scene '{scenePath}' already in build settings at index {i}.");
            }

            var enabled = inputJson.Contains("\"enabled\"") ? JsonHelper.ExtractBool(inputJson, "enabled") : true;
            var newScene = new EditorBuildSettingsScene(scenePath, enabled);

            var indexStr = JsonHelper.ExtractString(inputJson, "index");
            if (indexStr != null && int.TryParse(indexStr, out var index) && index >= 0 && index <= scenes.Count)
                scenes.Insert(index, newScene);
            else
                scenes.Add(newScene);

            EditorBuildSettings.scenes = scenes.ToArray();
            var finalIdx = scenes.IndexOf(newScene);
            return ToolResult.Success($"Added '{scenePath}' at index {finalIdx} (enabled: {enabled}).");
        }

        private static string RemoveScene(string inputJson)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Count == 0)
                return ToolResult.Error("Build scene list is empty.");

            var scenePath = JsonHelper.ExtractString(inputJson, "scene_path");
            var indexStr = JsonHelper.ExtractString(inputJson, "index");

            int removeIdx = -1;

            if (!string.IsNullOrWhiteSpace(scenePath))
            {
                for (int i = 0; i < scenes.Count; i++)
                {
                    if (scenes[i].path == scenePath) { removeIdx = i; break; }
                }
                if (removeIdx < 0)
                    return ToolResult.Error($"Scene '{scenePath}' not found in build settings.");
            }
            else if (indexStr != null && int.TryParse(indexStr, out var idx))
            {
                if (idx < 0 || idx >= scenes.Count)
                    return ToolResult.Error($"Index {idx} out of range (0–{scenes.Count - 1}).");
                removeIdx = idx;
            }
            else
            {
                return ToolResult.Error("Provide 'scene_path' or 'index' to identify the scene to remove.");
            }

            var removed = scenes[removeIdx].path;
            scenes.RemoveAt(removeIdx);
            EditorBuildSettings.scenes = scenes.ToArray();
            return ToolResult.Success($"Removed '{removed}' from build settings.");
        }

        private static string SetScenes(string inputJson)
        {
            var scenesArray = JsonHelper.ExtractArray(inputJson, "scenes");
            if (string.IsNullOrEmpty(scenesArray) || scenesArray == "[]")
                return ToolResult.Error("'scenes' array is required for 'set'. Pass an ordered array of scene paths.");

            var paths = JsonHelper.ParseStringArray(scenesArray);
            if (paths.Count == 0)
                return ToolResult.Error("'scenes' array is empty.");

            var newScenes = new List<EditorBuildSettingsScene>();
            var errors = new List<string>();

            foreach (var path in paths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                if (asset == null)
                    errors.Add(path);
                else
                    newScenes.Add(new EditorBuildSettingsScene(path, true));
            }

            if (errors.Count > 0 && newScenes.Count == 0)
                return ToolResult.Error($"No valid scenes found. Invalid: {string.Join(", ", errors)}");

            EditorBuildSettings.scenes = newScenes.ToArray();

            var sb = new StringBuilder();
            sb.AppendLine($"Build scenes set ({newScenes.Count}):");
            for (int i = 0; i < newScenes.Count; i++)
                sb.AppendLine($"  [{i}] {newScenes[i].path}");

            if (errors.Count > 0)
                sb.AppendLine($"Skipped (not found): {string.Join(", ", errors)}");

            return ToolResult.Success(sb.ToString());
        }

        private static string SetEnabled(string inputJson, bool enabled)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Count == 0)
                return ToolResult.Error("Build scene list is empty.");

            var scenePath = JsonHelper.ExtractString(inputJson, "scene_path");
            var indexStr = JsonHelper.ExtractString(inputJson, "index");

            int targetIdx = -1;

            if (!string.IsNullOrWhiteSpace(scenePath))
            {
                for (int i = 0; i < scenes.Count; i++)
                {
                    if (scenes[i].path == scenePath) { targetIdx = i; break; }
                }
                if (targetIdx < 0)
                    return ToolResult.Error($"Scene '{scenePath}' not found in build settings.");
            }
            else if (indexStr != null && int.TryParse(indexStr, out var idx))
            {
                if (idx < 0 || idx >= scenes.Count)
                    return ToolResult.Error($"Index {idx} out of range (0–{scenes.Count - 1}).");
                targetIdx = idx;
            }
            else
            {
                return ToolResult.Error("Provide 'scene_path' or 'index' to identify the scene.");
            }

            scenes[targetIdx] = new EditorBuildSettingsScene(scenes[targetIdx].path, enabled);
            EditorBuildSettings.scenes = scenes.ToArray();
            return ToolResult.Success($"Scene '{scenes[targetIdx].path}' is now {(enabled ? "enabled" : "disabled")}.");
        }
    }
}
