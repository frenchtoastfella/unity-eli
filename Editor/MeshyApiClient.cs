using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using UnityEngine;

namespace UnityEli.Editor
{
    /// <summary>
    /// HTTP client for the Meshy.ai text-to-3D API (v2).
    /// All methods are synchronous — called from IEliTool.Execute() on the main thread.
    /// Each call completes in 1-3 seconds (single HTTP request, no polling).
    /// </summary>
    internal static class MeshyApiClient
    {
        private const string BaseUrl = "https://api.meshy.ai/openapi/v2/text-to-3d";
        private const string RemeshUrl = "https://api.meshy.ai/openapi/v1/remesh";
        private const string RetextureUrl = "https://api.meshy.ai/openapi/v1/retexture";
        private const int TimeoutMs = 30000;

        /// <summary>
        /// Creates a preview (untextured mesh) task. Returns the task ID.
        /// </summary>
        public static string CreatePreviewTask(string apiKey, string prompt,
            string aiModel, string topology, int targetPolycount)
        {
            var body = new StringBuilder("{");
            body.Append("\"mode\":\"preview\"");
            body.Append($",\"prompt\":\"{JsonHelper.EscapeJson(prompt)}\"");
            body.Append($",\"ai_model\":\"{JsonHelper.EscapeJson(aiModel)}\"");
            body.Append($",\"topology\":\"{JsonHelper.EscapeJson(topology)}\"");
            if (targetPolycount > 0)
                body.Append($",\"target_polycount\":{targetPolycount}");
            body.Append(",\"target_formats\":[\"fbx\",\"glb\",\"obj\"]");
            body.Append("}");

            var responseJson = Post(apiKey, BaseUrl, body.ToString());
            return ExtractTaskId(responseJson);
        }

        /// <summary>
        /// Creates a refine (texturing) task for a completed preview. Returns the task ID.
        /// </summary>
        public static string CreateRefineTask(string apiKey, string previewTaskId,
            bool enablePbr, string texturePrompt)
        {
            var body = new StringBuilder("{");
            body.Append("\"mode\":\"refine\"");
            body.Append($",\"preview_task_id\":\"{JsonHelper.EscapeJson(previewTaskId)}\"");
            body.Append($",\"enable_pbr\":{(enablePbr ? "true" : "false")}");
            if (!string.IsNullOrEmpty(texturePrompt))
                body.Append($",\"texture_prompt\":\"{JsonHelper.EscapeJson(texturePrompt)}\"");
            body.Append(",\"target_formats\":[\"fbx\",\"glb\",\"obj\"]");
            body.Append("}");

            var responseJson = Post(apiKey, BaseUrl, body.ToString());
            return ExtractTaskId(responseJson);
        }

        /// <summary>
        /// Creates a remesh task. Accepts either a Meshy task ID or a public model URL.
        /// Returns the remesh task ID.
        /// </summary>
        public static string CreateRemeshTask(string apiKey, string inputTaskId, string modelUrl,
            string topology, int targetPolycount)
        {
            var body = new StringBuilder("{");
            if (!string.IsNullOrEmpty(inputTaskId))
                body.Append($"\"input_task_id\":\"{JsonHelper.EscapeJson(inputTaskId)}\"");
            else
                body.Append($"\"model_url\":\"{JsonHelper.EscapeJson(modelUrl)}\"");

            if (!string.IsNullOrEmpty(topology))
                body.Append($",\"topology\":\"{JsonHelper.EscapeJson(topology)}\"");
            if (targetPolycount > 0)
                body.Append($",\"target_polycount\":{targetPolycount}");
            body.Append(",\"target_formats\":[\"fbx\",\"glb\",\"obj\"]");
            body.Append("}");

            var responseJson = Post(apiKey, RemeshUrl, body.ToString());
            return ExtractTaskId(responseJson);
        }

        /// <summary>
        /// Creates a retexture task. Accepts a Meshy task ID or public model URL plus a style prompt or image.
        /// Returns the retexture task ID.
        /// </summary>
        public static string CreateRetextureTask(string apiKey, string inputTaskId, string modelUrl,
            string textStylePrompt, string imageStyleUrl, bool enablePbr)
        {
            var body = new StringBuilder("{");
            if (!string.IsNullOrEmpty(inputTaskId))
                body.Append($"\"input_task_id\":\"{JsonHelper.EscapeJson(inputTaskId)}\"");
            else
                body.Append($"\"model_url\":\"{JsonHelper.EscapeJson(modelUrl)}\"");

            if (!string.IsNullOrEmpty(textStylePrompt))
                body.Append($",\"text_style_prompt\":\"{JsonHelper.EscapeJson(textStylePrompt)}\"");
            if (!string.IsNullOrEmpty(imageStyleUrl))
                body.Append($",\"image_style_url\":\"{JsonHelper.EscapeJson(imageStyleUrl)}\"");

            body.Append($",\"enable_pbr\":{(enablePbr ? "true" : "false")}");
            body.Append(",\"enable_original_uv\":true");
            body.Append(",\"target_formats\":[\"fbx\",\"glb\",\"obj\"]");
            body.Append("}");

            var responseJson = Post(apiKey, RetextureUrl, body.ToString());
            return ExtractTaskId(responseJson);
        }

        /// <summary>
        /// Checks the status of a retexture task.
        /// </summary>
        public static MeshyTaskStatus CheckRetextureTask(string apiKey, string taskId)
        {
            var responseJson = Get(apiKey, $"{RetextureUrl}/{taskId}");
            return MeshyTaskStatus.Parse(responseJson);
        }

        /// <summary>
        /// Checks the status of a remesh task.
        /// </summary>
        public static MeshyTaskStatus CheckRemeshTask(string apiKey, string taskId)
        {
            var responseJson = Get(apiKey, $"{RemeshUrl}/{taskId}");
            return MeshyTaskStatus.Parse(responseJson);
        }

        /// <summary>
        /// Checks the status of a text-to-3D task (preview or refine).
        /// </summary>
        public static MeshyTaskStatus CheckTask(string apiKey, string taskId)
        {
            var responseJson = Get(apiKey, $"{BaseUrl}/{taskId}");
            return MeshyTaskStatus.Parse(responseJson);
        }

        /// <summary>
        /// Downloads a file from a URL (model or texture). No auth header needed (CDN URLs).
        /// </summary>
        public static byte[] DownloadFile(string url)
        {
            if (string.IsNullOrEmpty(url))
                throw new Exception("Download URL is empty or null.");

            var request = WebRequest.Create(url) as HttpWebRequest;
            request.Method = "GET";
            request.Timeout = 120000; // 2 minutes for large files

            using (var response = request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // ── Private helpers ─────────────────────────────────────────────────

        private static string ExtractTaskId(string responseJson)
        {
            // Meshy v2 returns {"result":"<task-id>"} for task creation
            var taskId = JsonHelper.ExtractString(responseJson, "result");
            if (string.IsNullOrEmpty(taskId))
            {
                // Fallback: some endpoints may return {"id":"<task-id>"}
                taskId = JsonHelper.ExtractString(responseJson, "id");
            }
            if (string.IsNullOrEmpty(taskId))
                throw new Exception($"No task ID in API response: {Truncate(responseJson, 200)}");
            return taskId;
        }

        private static string Post(string apiKey, string url, string jsonBody)
        {
            var request = BuildRequest("POST", url, apiKey);
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            request.ContentLength = bodyBytes.Length;
            using (var reqStream = request.GetRequestStream())
                reqStream.Write(bodyBytes, 0, bodyBytes.Length);
            return ReadResponse(request);
        }

        private static string Get(string apiKey, string url)
        {
            var request = BuildRequest("GET", url, apiKey);
            return ReadResponse(request);
        }

        private static HttpWebRequest BuildRequest(string method, string url, string apiKey)
        {
            var request = WebRequest.Create(url) as HttpWebRequest;
            request.Method = method;
            request.ContentType = "application/json";
            request.Headers.Add("Authorization", $"Bearer {apiKey}");
            request.Timeout = TimeoutMs;
            return request;
        }

        private static string ReadResponse(HttpWebRequest request)
        {
            try
            {
                using (var response = request.GetResponse())
                using (var stream = response.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    return reader.ReadToEnd();
            }
            catch (WebException ex) when (ex.Response is HttpWebResponse errorResponse)
            {
                var statusCode = (int)errorResponse.StatusCode;
                string errorBody;
                using (var stream = errorResponse.GetResponseStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    errorBody = reader.ReadToEnd();

                if (statusCode == 401)
                    throw new Exception("Invalid Meshy API key. Check your key in Preferences > Unity Eli.");
                if (statusCode == 429)
                    throw new Exception("Meshy API rate limit exceeded. Wait a moment and try again.");
                throw new Exception($"Meshy API error ({statusCode}): {Truncate(errorBody, 300)}");
            }
        }

        private static string Truncate(string s, int maxLength)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLength) return s;
            return s.Substring(0, maxLength) + "...";
        }
    }

    /// <summary>
    /// Parsed status of a Meshy task (preview or refine).
    /// </summary>
    internal class MeshyTaskStatus
    {
        public string Status;
        public int Progress;
        public string Name;
        public string Mode;
        public string ErrorMessage;

        // Model download URLs
        public string ModelUrlFbx;
        public string ModelUrlGlb;
        public string ModelUrlObj;

        // Texture download URLs (populated for refine tasks)
        public string TextureBaseColor;
        public string TextureMetallic;
        public string TextureNormal;
        public string TextureRoughness;

        public string AvailableFormats
        {
            get
            {
                var formats = new List<string>();
                if (!string.IsNullOrEmpty(ModelUrlFbx)) formats.Add("fbx");
                if (!string.IsNullOrEmpty(ModelUrlGlb)) formats.Add("glb");
                if (!string.IsNullOrEmpty(ModelUrlObj)) formats.Add("obj");
                return formats.Count > 0 ? string.Join(", ", formats) : "none";
            }
        }

        public bool HasTextures =>
            !string.IsNullOrEmpty(TextureBaseColor) ||
            !string.IsNullOrEmpty(TextureMetallic) ||
            !string.IsNullOrEmpty(TextureNormal) ||
            !string.IsNullOrEmpty(TextureRoughness);

        public string GetModelUrl(string format)
        {
            switch (format.ToLowerInvariant())
            {
                case "fbx": return ModelUrlFbx;
                case "glb": return ModelUrlGlb;
                case "obj": return ModelUrlObj;
                default: return null;
            }
        }

        public static MeshyTaskStatus Parse(string json)
        {
            var status = new MeshyTaskStatus
            {
                Status = JsonHelper.ExtractString(json, "status") ?? "UNKNOWN",
                Progress = JsonHelper.ExtractInt(json, "progress"),
                Name = JsonHelper.ExtractString(json, "name"),
                Mode = JsonHelper.ExtractString(json, "mode"),
                ErrorMessage = JsonHelper.ExtractString(json, "task_error")
                               ?? JsonHelper.ExtractString(json, "message"),
            };

            // Parse model_urls object
            var modelUrls = JsonHelper.ExtractObject(json, "model_urls");
            if (modelUrls != "{}")
            {
                status.ModelUrlFbx = JsonHelper.ExtractString(modelUrls, "fbx");
                status.ModelUrlGlb = JsonHelper.ExtractString(modelUrls, "glb");
                status.ModelUrlObj = JsonHelper.ExtractString(modelUrls, "obj");
            }

            // Parse texture_urls array — supports two formats:
            // Text-to-3D refine: [{"base_color":"url", "metallic":"url", ...}]
            // Retexture:         [{"name":"base_color", "url":"url"}, ...]
            var textureUrls = JsonHelper.ExtractArray(json, "texture_urls");
            if (textureUrls != "[]")
            {
                var items = JsonHelper.ParseArray(textureUrls);
                if (items.Count > 0)
                {
                    // Check which format: if first item has "name" field, it's the name/url pair format
                    var firstName = JsonHelper.ExtractString(items[0], "name");
                    if (firstName != null)
                    {
                        // Retexture format: [{name, url}, ...]
                        foreach (var item in items)
                        {
                            var texName = JsonHelper.ExtractString(item, "name");
                            var texUrl = JsonHelper.ExtractString(item, "url");
                            if (texName == null || texUrl == null) continue;
                            switch (texName)
                            {
                                case "base_color": status.TextureBaseColor = texUrl; break;
                                case "metallic":   status.TextureMetallic = texUrl; break;
                                case "normal":     status.TextureNormal = texUrl; break;
                                case "roughness":  status.TextureRoughness = texUrl; break;
                            }
                        }
                    }
                    else
                    {
                        // Text-to-3D refine format: [{base_color, metallic, ...}]
                        var tex = items[0];
                        status.TextureBaseColor = JsonHelper.ExtractString(tex, "base_color");
                        status.TextureMetallic = JsonHelper.ExtractString(tex, "metallic");
                        status.TextureNormal = JsonHelper.ExtractString(tex, "normal");
                        status.TextureRoughness = JsonHelper.ExtractString(tex, "roughness");
                    }
                }
            }

            return status;
        }
    }
}
