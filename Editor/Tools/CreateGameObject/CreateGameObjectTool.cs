using System;
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
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.name))
            {
                return ToolResult.Error("name is required.");
            }

            GameObject go;

            if (!string.IsNullOrEmpty(input.primitive_type) && input.primitive_type != "None")
            {
                if (!Enum.TryParse<PrimitiveType>(input.primitive_type, true, out var primitiveType))
                {
                    return ToolResult.Error($"Invalid primitive_type: '{input.primitive_type}'. Valid values: Cube, Sphere, Capsule, Cylinder, Plane, Quad.");
                }

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

            // Reparent if requested
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
