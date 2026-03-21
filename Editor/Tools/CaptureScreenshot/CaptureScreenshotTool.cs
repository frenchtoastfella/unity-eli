using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class CaptureScreenshotTool : IEliTool
    {
        public string Name => "capture_screenshot";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var width = JsonHelper.ExtractInt(inputJson, "width");
            var height = JsonHelper.ExtractInt(inputJson, "height");
            var source = JsonHelper.ExtractString(inputJson, "source") ?? "scene";

            if (width <= 0) width = 1920;
            if (height <= 0) height = 1080;

            // Cap resolution to avoid memory issues
            width = Mathf.Min(width, 3840);
            height = Mathf.Min(height, 2160);

            Camera cam;
            string sourceDesc;

            if (source == "game")
            {
                cam = Camera.main;
                if (cam == null)
                {
                    // Fallback: find any camera
                    cam = UnityEngine.Object.FindFirstObjectByType<Camera>();
                }
                if (cam == null)
                    return ToolResult.Error("No camera found in the scene. Add a Camera component to capture a game view screenshot.");
                sourceDesc = $"game camera '{cam.name}'";
            }
            else
            {
                var sceneView = SceneView.lastActiveSceneView;
                if (sceneView == null)
                    return ToolResult.Error("No active Scene View found. Open the Scene View window in the editor.");
                cam = sceneView.camera;
                if (cam == null)
                    return ToolResult.Error("Scene View camera is not available.");
                sourceDesc = "scene view";
            }

            try
            {
                var bytes = RenderCamera(cam, width, height);
                var outputPath = Path.Combine(Application.temporaryCachePath, "eli-screenshot.png");
                File.WriteAllBytes(outputPath, bytes);

                return ToolResult.Success(
                    $"Screenshot captured from {sourceDesc} ({width}x{height}). " +
                    $"Saved to: {outputPath}\n" +
                    $"Use your Read tool on this path to view the image.");
            }
            catch (Exception e)
            {
                return ToolResult.Error($"Failed to capture screenshot: {e.Message}");
            }
        }

        private static byte[] RenderCamera(Camera cam, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var prevTarget = cam.targetTexture;
            var prevActive = RenderTexture.active;

            try
            {
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                RenderTexture.active = prevActive;

                var bytes = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                return bytes;
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                RenderTexture.ReleaseTemporary(rt);
            }
        }
    }
}
