using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class MeshyDownloadModelTool : IEliTool
    {
        public string Name => "meshy_download_model";
        public bool NeedsAssetRefresh => true;

        private const string OutputDir = "Assets/Meshes/Generated";

        public string Execute(string inputJson)
        {
            var apiKey = UnityEliSettings.MeshyApiKey;
            if (string.IsNullOrEmpty(apiKey))
                return ToolResult.Error("Meshy API key not configured. Set it in Preferences > Unity Eli > Meshy.ai Integration.");

            var taskId = JsonHelper.ExtractString(inputJson, "task_id");
            if (string.IsNullOrWhiteSpace(taskId))
                return ToolResult.Error("'task_id' is required.");

            var source = JsonHelper.ExtractString(inputJson, "source") ?? "text-to-3d";
            var format = JsonHelper.ExtractString(inputJson, "format") ?? "fbx";
            var filename = JsonHelper.ExtractString(inputJson, "filename");

            try
            {
                // 1. Get task details to find download URLs
                MeshyTaskStatus status;
                switch (source)
                {
                    case "remesh":    status = MeshyApiClient.CheckRemeshTask(apiKey, taskId); break;
                    case "retexture": status = MeshyApiClient.CheckRetextureTask(apiKey, taskId); break;
                    default:          status = MeshyApiClient.CheckTask(apiKey, taskId); break;
                }
                if (status.Status != "SUCCEEDED")
                    return ToolResult.Error(
                        $"Task is not complete (status: {status.Status}). " +
                        "Wait for SUCCEEDED before downloading.");

                var modelUrl = status.GetModelUrl(format);
                if (string.IsNullOrEmpty(modelUrl))
                    return ToolResult.Error(
                        $"No download URL for format '{format}'. " +
                        $"Available: {status.AvailableFormats}");

                // 2. Determine and sanitize filename
                filename = SanitizeFilename(
                    string.IsNullOrWhiteSpace(filename) ? (status.Name ?? taskId) : filename);

                // 3. Ensure output directories exist
                EliToolHelpers.EnsureDirectoryExists(OutputDir);

                // 4. Download and save model file
                var ext = format.ToLowerInvariant();
                var modelPath = GetUniquePath($"{OutputDir}/{filename}.{ext}");
                var modelBytes = MeshyApiClient.DownloadFile(modelUrl);
                File.WriteAllBytes(modelPath, modelBytes);

                var result = new StringBuilder();
                result.AppendLine($"Model downloaded: {modelPath} ({modelBytes.Length / 1024}KB)");

                // 5. Download texture files if available (refine tasks)
                if (status.HasTextures)
                {
                    var texDir = $"{OutputDir}/{filename}";
                    EliToolHelpers.EnsureDirectoryExists(texDir);

                    DownloadTexture(status.TextureBaseColor, texDir, "base_color", result);
                    DownloadTexture(status.TextureMetallic, texDir, "metallic", result);
                    DownloadTexture(status.TextureNormal, texDir, "normal", result);
                    DownloadTexture(status.TextureRoughness, texDir, "roughness", result);
                }

                result.AppendLine("\nAssets will be imported on the next asset refresh.");
                return ToolResult.Success(result.ToString());
            }
            catch (Exception e)
            {
                return ToolResult.Error($"Failed to download model: {e.Message}");
            }
        }

        private static void DownloadTexture(string url, string directory, string name, StringBuilder result)
        {
            if (string.IsNullOrEmpty(url)) return;

            try
            {
                // Determine extension from URL, default to png
                var ext = "png";
                if (url.Contains(".jpg") || url.Contains(".jpeg"))
                    ext = "jpg";

                var texPath = $"{directory}/{name}.{ext}";
                var bytes = MeshyApiClient.DownloadFile(url);
                File.WriteAllBytes(texPath, bytes);
                result.AppendLine($"Texture downloaded: {texPath} ({bytes.Length / 1024}KB)");
            }
            catch (Exception e)
            {
                result.AppendLine($"Warning: Failed to download {name} texture: {e.Message}");
            }
        }

        private static string GetUniquePath(string path)
        {
            if (!File.Exists(path))
                return path;

            var dir = Path.GetDirectoryName(path).Replace("\\", "/");
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);

            var counter = 1;
            string candidate;
            do
            {
                candidate = $"{dir}/{name}_{counter}{ext}";
                counter++;
            }
            while (File.Exists(candidate));

            return candidate;
        }

        private static string SanitizeFilename(string name)
        {
            var sanitized = Regex.Replace(name, @"[^\w\s\-]", "");
            sanitized = Regex.Replace(sanitized, @"\s+", "_");
            sanitized = sanitized.Trim('_');
            if (sanitized.Length > 60)
                sanitized = sanitized.Substring(0, 60).TrimEnd('_');
            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "meshy_model";
            return sanitized;
        }
    }
}
