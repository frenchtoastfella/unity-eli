using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class CreateGameObjectTool : IEliTool
    {
        public string Name => "create_gameobject";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            // Block if Unity is compiling — domain reload will wipe newly created objects
            if (EditorApplication.isCompiling)
            {
                return ToolResult.Error(
                    "Unity is currently compiling scripts. Creating GameObjects now will likely be lost " +
                    "when the domain reloads. Wait for compilation to finish " +
                    "(call refresh_assets with wait_for_compilation=true) then try again.");
            }

            if (EditorApplication.isPlaying)
            {
                return ToolResult.Error(
                    "Unity is in Play mode. Scene changes made during Play mode are lost when Play mode stops. " +
                    "Use play_mode action='stop' first, then retry.");
            }

            // Check for batch mode: "objects" array
            var objectsArray = JsonHelper.ExtractArray(inputJson, "objects");
            if (objectsArray != "[]")
            {
                return ExecuteBatch(objectsArray);
            }

            // Single object mode
            var input = JsonUtility.FromJson<Input>(inputJson);
            return CreateSingle(input);
        }

        private string ExecuteBatch(string objectsArrayJson)
        {
            var items = JsonHelper.ParseArray(objectsArrayJson);
            if (items.Count == 0)
                return ToolResult.Error("'objects' array is empty.");

            var results = new List<string>();
            var errors = new List<string>();

            foreach (var itemJson in items)
            {
                var input = JsonUtility.FromJson<Input>(itemJson);
                if (string.IsNullOrWhiteSpace(input.name))
                {
                    errors.Add("(skipped entry with no name)");
                    continue;
                }

                var result = CreateSingle(input);
                if (result.StartsWith("ERROR:"))
                    errors.Add($"'{input.name}': {result.Substring(6).Trim()}");
                else
                    results.Add(input.name);
            }

            var sb = new StringBuilder();
            sb.Append($"Created {results.Count} GameObject(s): {string.Join(", ", results)}.");
            if (errors.Count > 0)
                sb.Append($" Errors: {string.Join("; ", errors)}.");

            return results.Count > 0 ? ToolResult.Success(sb.ToString()) : ToolResult.Error(sb.ToString());
        }

        private string CreateSingle(Input input)
        {
            if (string.IsNullOrWhiteSpace(input.name))
                return ToolResult.Error("name is required.");

            GameObject go;

            if (!string.IsNullOrEmpty(input.primitive_type) && input.primitive_type != "None")
            {
                if (!Enum.TryParse<PrimitiveType>(input.primitive_type, true, out var primitiveType))
                    return ToolResult.Error($"Invalid primitive_type: '{input.primitive_type}'.");

                go = GameObject.CreatePrimitive(primitiveType);
            }
            else
            {
                go = new GameObject();
            }

            go.name = input.name;
            go.transform.position = new Vector3(input.position_x, input.position_y, input.position_z);
            go.transform.eulerAngles = new Vector3(input.rotation_x, input.rotation_y, input.rotation_z);

            var scaleX = input.scale_x == 0f ? 1f : input.scale_x;
            var scaleY = input.scale_y == 0f ? 1f : input.scale_y;
            var scaleZ = input.scale_z == 0f ? 1f : input.scale_z;
            go.transform.localScale = new Vector3(scaleX, scaleY, scaleZ);

            if (!string.IsNullOrWhiteSpace(input.parent))
            {
                var parentGo = EliToolHelpers.FindGameObject(input.parent);
                if (parentGo == null)
                    return ToolResult.Error($"Parent GameObject '{input.parent}' not found.");
                Undo.SetTransformParent(go.transform, parentGo.transform, $"Unity Eli: Create {input.name}");
            }

            Undo.RegisterCreatedObjectUndo(go, $"Unity Eli: Create {input.name}");
            Selection.activeGameObject = go;

            var parentInfo = string.IsNullOrWhiteSpace(input.parent) ? "" : $" under '{input.parent}'";
            return ToolResult.Success(
                $"GameObject '{input.name}' created at ({input.position_x}, {input.position_y}, {input.position_z}){parentInfo}.");
        }

        [Serializable]
        private class Input
        {
            public string name;
            public string primitive_type;
            public string parent;
            public float position_x;
            public float position_y;
            public float position_z;
            public float rotation_x;
            public float rotation_y;
            public float rotation_z;
            public float scale_x;
            public float scale_y;
            public float scale_z;
        }
    }
}
