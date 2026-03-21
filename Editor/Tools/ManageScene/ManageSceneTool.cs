using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEli.Editor.Tools
{
    public class ManageSceneTool : IEliTool
    {
        public string Name => "manage_scene";
        public bool NeedsAssetRefresh => true;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            var action = string.IsNullOrWhiteSpace(input.action) ? "status" : input.action;

            switch (action.ToLowerInvariant())
            {
                case "status":
                    return GetStatus();
                case "open":
                    return OpenScene(input);
                case "new":
                    return NewScene(input);
                case "save":
                    return SaveScene();
                case "close":
                    return CloseScene(input);
                case "load_additive":
                    return LoadAdditive(input);
                default:
                    return ToolResult.Error($"Unknown action '{action}'. Valid: status, open, new, save, close, load_additive.");
            }
        }

        private static string GetStatus()
        {
            var sb = new StringBuilder();
            var activeScene = SceneManager.GetActiveScene();
            sb.AppendLine($"Active scene: '{activeScene.name}' ({activeScene.path})");
            sb.AppendLine($"  Dirty (unsaved changes): {activeScene.isDirty}");

            var count = SceneManager.sceneCount;
            if (count > 1)
            {
                sb.AppendLine();
                sb.AppendLine($"All loaded scenes ({count}):");
                for (int i = 0; i < count; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    var active = scene == activeScene ? " [ACTIVE]" : "";
                    var dirty = scene.isDirty ? " [UNSAVED]" : "";
                    sb.AppendLine($"  {i}: '{scene.name}' ({scene.path}){active}{dirty}");
                }
            }

            return ToolResult.Success(sb.ToString());
        }

        private static string OpenScene(Input input)
        {
            if (string.IsNullOrWhiteSpace(input.path))
                return ToolResult.Error("path is required for action 'open'.");

            if (!File.Exists(input.path))
                return ToolResult.Error($"Scene file not found: '{input.path}'.");

            var previousScene = SceneManager.GetActiveScene();
            var previousName = previousScene.name;
            var wasDirty = previousScene.isDirty;

            // Save automatically — no dialog (MCP context has no user to click)
            if (input.save_current)
            {
                EditorSceneManager.SaveOpenScenes();
            }
            else if (wasDirty)
            {
                // Warn that unsaved changes will be lost
                return ToolResult.Error(
                    $"Scene '{previousName}' has unsaved changes. " +
                    $"Set save_current=true to save before switching, or save manually first.");
            }

            var scene = EditorSceneManager.OpenScene(input.path, OpenSceneMode.Single);
            var savedNote = input.save_current && wasDirty ? $" (saved '{previousName}' first)" : "";
            return ToolResult.Success($"Opened scene '{scene.name}' (previously: '{previousName}'){savedNote}.");
        }

        private static string NewScene(Input input)
        {
            if (string.IsNullOrWhiteSpace(input.path))
                return ToolResult.Error("path is required for action 'new' (e.g. 'Assets/Scenes/MyScene.unity').");

            var previousScene = SceneManager.GetActiveScene();
            var previousName = previousScene.name;
            var wasDirty = previousScene.isDirty;

            // Save automatically — no dialog (MCP context has no user to click)
            if (input.save_current)
            {
                EditorSceneManager.SaveOpenScenes();
            }
            else if (wasDirty)
            {
                return ToolResult.Error(
                    $"Scene '{previousName}' has unsaved changes. " +
                    $"Set save_current=true to save before switching, or save manually first.");
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Ensure the directory exists
            var dir = Path.GetDirectoryName(input.path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!EditorSceneManager.SaveScene(scene, input.path))
                return ToolResult.Error($"Failed to save new scene to '{input.path}'.");

            var savedNote = input.save_current && wasDirty ? $" (saved '{previousName}' first)" : "";
            return ToolResult.Success(
                $"Created and saved new scene '{scene.name}' at '{input.path}' (previously: '{previousName}'){savedNote}.");
        }

        private static string SaveScene()
        {
            var sb = new StringBuilder();
            var count = SceneManager.sceneCount;
            var dirtyCount = 0;

            for (int i = 0; i < count; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                    dirtyCount++;
            }

            if (dirtyCount == 0)
                return ToolResult.Success("No scenes have unsaved changes — nothing to save.");

            if (!EditorSceneManager.SaveOpenScenes())
                return ToolResult.Error("Failed to save open scenes.");

            sb.AppendLine($"Saved {dirtyCount} scene(s):");
            for (int i = 0; i < count; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                sb.AppendLine($"  '{scene.name}' ({scene.path})");
            }

            return ToolResult.Success(sb.ToString());
        }

        private static string CloseScene(Input input)
        {
            if (string.IsNullOrWhiteSpace(input.path))
                return ToolResult.Error("path is required for action 'close'.");

            var scene = SceneManager.GetSceneByPath(input.path);
            if (!scene.IsValid())
                return ToolResult.Error($"No loaded scene found at path '{input.path}'.");

            if (SceneManager.sceneCount <= 1)
                return ToolResult.Error("Cannot close the only open scene.");

            var sceneName = scene.name;

            if (!EditorSceneManager.CloseScene(scene, removeScene: true))
                return ToolResult.Error($"Failed to close scene '{sceneName}'.");

            var activeScene = SceneManager.GetActiveScene();
            return ToolResult.Success($"Closed scene '{sceneName}'. Active scene is now '{activeScene.name}' ({activeScene.path}).");
        }

        private static string LoadAdditive(Input input)
        {
            if (string.IsNullOrWhiteSpace(input.path))
                return ToolResult.Error("path is required for action 'load_additive'.");

            if (!File.Exists(input.path))
                return ToolResult.Error($"Scene file not found: '{input.path}'.");

            var scene = EditorSceneManager.OpenScene(input.path, OpenSceneMode.Additive);
            var activeScene = SceneManager.GetActiveScene();
            return ToolResult.Success(
                $"Loaded scene '{scene.name}' additively. Active scene remains '{activeScene.name}'. " +
                $"Total loaded scenes: {SceneManager.sceneCount}.");
        }

        [Serializable]
        private class Input
        {
            public string action;
            public string path;
            public bool save_current = true;
        }
    }
}
