using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEli.Editor.Tools
{
    public class ManagePrefabTool : IEliTool
    {
        public string Name => "manage_prefab";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.action))
                return ToolResult.Error(
                    "action is required: instantiate, open_edit_mode, close_edit_mode, apply_overrides, revert_overrides.");

            switch (input.action.ToLowerInvariant())
            {
                case "instantiate":     return Instantiate(input);
                case "open_edit_mode":  return OpenEditMode(input);
                case "close_edit_mode": return CloseEditMode();
                case "apply_overrides": return ApplyOverrides(input);
                case "revert_overrides": return RevertOverrides(input);
                default:
                    return ToolResult.Error(
                        $"Unknown action '{input.action}'. Valid: instantiate, open_edit_mode, close_edit_mode, apply_overrides, revert_overrides.");
            }
        }

        // ── instantiate ───────────────────────────────────────────────────────

        private static string Instantiate(Input input)
        {
            if (string.IsNullOrWhiteSpace(input.prefab_path))
                return ToolResult.Error("prefab_path is required for action 'instantiate'.");

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(input.prefab_path);
            if (prefabAsset == null)
                return ToolResult.Error($"Prefab not found at '{input.prefab_path}'.");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset);
            if (instance == null)
                return ToolResult.Error("Failed to instantiate prefab.");

            // Apply name override if provided
            if (!string.IsNullOrWhiteSpace(input.name))
                instance.name = input.name;

            // Apply position
            instance.transform.position = new Vector3(input.position_x, input.position_y, input.position_z);

            // Reparent if requested
            if (!string.IsNullOrWhiteSpace(input.parent))
            {
                var parentGo = EliToolHelpers.FindGameObject(input.parent);
                if (parentGo == null)
                    return ToolResult.Error($"Parent GameObject '{input.parent}' not found.");
                instance.transform.SetParent(parentGo.transform, worldPositionStays: true);
            }

            Undo.RegisterCreatedObjectUndo(instance, $"Unity Eli: Instantiate {prefabAsset.name}");
            Selection.activeGameObject = instance;

            return ToolResult.Success(
                $"Instantiated '{prefabAsset.name}' from '{input.prefab_path}' as '{instance.name}' at ({input.position_x}, {input.position_y}, {input.position_z}).");
        }

        // ── open_edit_mode ────────────────────────────────────────────────────

        private static string OpenEditMode(Input input)
        {
            if (string.IsNullOrWhiteSpace(input.prefab_path))
                return ToolResult.Error("prefab_path is required for action 'open_edit_mode'.");

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(input.prefab_path);
            if (asset == null)
                return ToolResult.Error($"Prefab not found at '{input.prefab_path}'.");

            AssetDatabase.OpenAsset(asset);

            return ToolResult.Success($"Opened '{input.prefab_path}' in Prefab Mode.");
        }

        // ── close_edit_mode ───────────────────────────────────────────────────

        private static string CloseEditMode()
        {
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage == null)
                return ToolResult.Error("Not currently in Prefab Mode.");

            var prefabName = stage.prefabContentsRoot?.name ?? "prefab";
            StageUtility.GoBackToPreviousStage();

            return ToolResult.Success($"Exited Prefab Mode for '{prefabName}'.");
        }

        // ── apply_overrides ───────────────────────────────────────────────────

        private static string ApplyOverrides(Input input)
        {
            if (string.IsNullOrWhiteSpace(input.game_object))
                return ToolResult.Error("game_object is required for action 'apply_overrides' (the scene instance name).");

            var go = EliToolHelpers.FindGameObject(input.game_object);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object}' not found in the scene.");

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ToolResult.Error($"'{input.game_object}' is not a prefab instance.");

            var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
            PrefabUtility.ApplyPrefabInstance(go, InteractionMode.AutomatedAction);

            return ToolResult.Success(
                $"Applied all overrides from '{input.game_object}' to prefab asset '{assetPath}'.");
        }

        // ── revert_overrides ──────────────────────────────────────────────────

        private static string RevertOverrides(Input input)
        {
            if (string.IsNullOrWhiteSpace(input.game_object))
                return ToolResult.Error("game_object is required for action 'revert_overrides' (the scene instance name).");

            var go = EliToolHelpers.FindGameObject(input.game_object);
            if (go == null)
                return ToolResult.Error($"GameObject '{input.game_object}' not found in the scene.");

            if (!PrefabUtility.IsPartOfPrefabInstance(go))
                return ToolResult.Error($"'{input.game_object}' is not a prefab instance.");

            PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);

            return ToolResult.Success($"Reverted all overrides on '{input.game_object}'.");
        }

        [Serializable]
        private class Input
        {
            public string action;
            public string prefab_path;
            public string game_object;
            public string name;
            public string parent;
            public float position_x;
            public float position_y;
            public float position_z;
        }
    }
}
