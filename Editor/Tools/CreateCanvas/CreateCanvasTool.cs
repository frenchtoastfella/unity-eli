using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace UnityEli.Editor.Tools
{
    public class CreateCanvasTool : IEliTool
    {
        public string Name => "create_canvas";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            var canvasName = string.IsNullOrWhiteSpace(input.name) ? "Canvas" : input.name;

            var go = new GameObject(canvasName);

            // Canvas component
            var canvas = go.AddComponent<Canvas>();

            if (!string.IsNullOrEmpty(input.render_mode))
            {
                switch (input.render_mode)
                {
                    case "ScreenSpaceCamera":
                        canvas.renderMode = RenderMode.ScreenSpaceCamera;
                        break;
                    case "WorldSpace":
                        canvas.renderMode = RenderMode.WorldSpace;
                        break;
                    default:
                        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                        break;
                }
            }
            else
            {
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            canvas.sortingOrder = input.sort_order;

            // CanvasScaler component
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;

            var refX = input.reference_resolution_x > 0 ? input.reference_resolution_x : 1920f;
            var refY = input.reference_resolution_y > 0 ? input.reference_resolution_y : 1080f;
            scaler.referenceResolution = new Vector2(refX, refY);

            // match_width_or_height defaults to 0.5 — but JsonUtility deserializes missing floats as 0
            // so we only override if explicitly non-zero, or use 0.5 as default
            scaler.matchWidthOrHeight = input.match_width_or_height > 0f ? input.match_width_or_height : 0.5f;

            // GraphicRaycaster component
            go.AddComponent<GraphicRaycaster>();

            // Ensure there's an EventSystem in the scene
            if (UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var eventSystemGo = new GameObject("EventSystem");
                eventSystemGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                eventSystemGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                Undo.RegisterCreatedObjectUndo(eventSystemGo, "Unity Eli: Create EventSystem");
            }

            Undo.RegisterCreatedObjectUndo(go, $"Unity Eli: Create Canvas '{canvasName}'");
            Selection.activeGameObject = go;

            return ToolResult.Success($"Canvas '{canvasName}' created with CanvasScaler ({refX}x{refY}) and GraphicRaycaster.");
        }

        [Serializable]
        private class Input
        {
            public string name;
            public string render_mode;
            public int sort_order;
            public float reference_resolution_x;
            public float reference_resolution_y;
            public float match_width_or_height;
        }
    }
}
