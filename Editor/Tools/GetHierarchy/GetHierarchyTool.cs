using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityEli.Editor.Tools
{
    public class GetHierarchyTool : IEliTool
    {
        public string Name => "get_hierarchy";
        public bool NeedsAssetRefresh => false;

        private const int DefaultMaxDepth = 3;
        private const int DefaultMaxCount = 200;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            int maxDepth = input.max_depth > 0 ? input.max_depth : DefaultMaxDepth;
            int maxCount = input.max_count > 0 ? input.max_count : DefaultMaxCount;
            bool includeComponents = input.include_components;
            int emitted = 0;

            // If a specific root is requested, find it and print its subtree
            if (!string.IsNullOrWhiteSpace(input.root))
            {
                var rootGo = EliToolHelpers.FindGameObject(input.root);
                if (rootGo == null)
                    return ToolResult.Error($"GameObject '{input.root}' not found in the scene.");

                int totalCount = CountDescendants(rootGo.transform) + 1;
                var sb = new StringBuilder();
                sb.AppendLine($"Hierarchy under '{rootGo.name}' ({totalCount} objects total):");
                sb.AppendLine();
                AppendGameObject(sb, rootGo, 0, maxDepth, maxCount, includeComponents, ref emitted);
                if (emitted >= maxCount)
                    sb.AppendLine($"\n(output capped at {maxCount} objects — use 'root' to drill into a subtree, or increase 'max_count')");
                return ToolResult.Success(sb.ToString());
            }

            // Otherwise, return the full scene hierarchy
            var scene = SceneManager.GetActiveScene();
            var rootObjects = scene.GetRootGameObjects();

            if (rootObjects.Length == 0)
                return ToolResult.Success($"Scene '{scene.name}' is empty (no GameObjects).");

            int sceneTotal = 0;
            foreach (var root in rootObjects)
                sceneTotal += CountDescendants(root.transform) + 1;

            var result = new StringBuilder();
            result.AppendLine($"Scene: {scene.name} ({rootObjects.Length} root objects, {sceneTotal} total)");
            result.AppendLine();

            foreach (var root in rootObjects)
            {
                if (emitted >= maxCount)
                {
                    result.AppendLine($"(... {rootObjects.Length - Array.IndexOf(rootObjects, root)} more root objects not shown)");
                    break;
                }
                AppendGameObject(result, root, 0, maxDepth, maxCount, includeComponents, ref emitted);
            }

            if (emitted >= maxCount)
                result.AppendLine($"\n(output capped at {maxCount} objects — use 'root' to drill into a subtree, or increase 'max_count')");

            return ToolResult.Success(result.ToString());
        }

        private static void AppendGameObject(StringBuilder sb, GameObject go, int depth, int maxDepth,
            int maxCount, bool includeComponents, ref int emitted)
        {
            if (emitted >= maxCount) return;
            emitted++;

            var indent = new string(' ', depth * 2);
            var activeMarker = go.activeSelf ? "" : " [inactive]";
            var tag = (go.tag != "Untagged") ? $" (tag: {go.tag})" : "";
            var layer = (go.layer != 0) ? $" (layer: {LayerMask.LayerToName(go.layer)})" : "";

            sb.Append($"{indent}- {go.name}{activeMarker}{tag}{layer}");

            if (includeComponents)
            {
                var components = go.GetComponents<Component>();
                var componentNames = new List<string>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    // Skip Transform since every GO has one
                    if (comp is Transform) continue;
                    componentNames.Add(comp.GetType().Name);
                }
                if (componentNames.Count > 0)
                {
                    sb.Append($"  [{string.Join(", ", componentNames)}]");
                }
            }

            var transform = go.transform;
            int childCount = transform.childCount;

            // At the depth limit, annotate with child count instead of recursing
            if (depth >= maxDepth)
            {
                if (childCount > 0)
                    sb.Append($"  ({childCount} children)");
                sb.AppendLine();
                return;
            }

            sb.AppendLine();

            // Recurse into children
            for (int i = 0; i < childCount; i++)
            {
                if (emitted >= maxCount)
                {
                    sb.AppendLine($"{indent}  (... {childCount - i} more children not shown)");
                    break;
                }
                AppendGameObject(sb, transform.GetChild(i).gameObject, depth + 1, maxDepth, maxCount, includeComponents, ref emitted);
            }
        }

        private static int CountDescendants(Transform t)
        {
            int count = 0;
            for (int i = 0; i < t.childCount; i++)
            {
                count++; // the child itself
                count += CountDescendants(t.GetChild(i));
            }
            return count;
        }

        [Serializable]
        private class Input
        {
            public string root;
            public int max_depth;
            public int max_count;
            public bool include_components;
        }
    }
}
