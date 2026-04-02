using System;

namespace UnityEli.Editor.Tools
{
    public class MeshyRetextureTool : IEliTool
    {
        public string Name => "meshy_retexture";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var apiKey = UnityEliSettings.MeshyApiKey;
            if (string.IsNullOrEmpty(apiKey))
                return ToolResult.Error(
                    "Meshy API key not configured. Set it in Preferences > Unity Eli > Meshy.ai Integration.");

            var inputTaskId = JsonHelper.ExtractString(inputJson, "input_task_id");
            var modelUrl = JsonHelper.ExtractString(inputJson, "model_url");

            if (string.IsNullOrWhiteSpace(inputTaskId) && string.IsNullOrWhiteSpace(modelUrl))
                return ToolResult.Error("Either 'input_task_id' or 'model_url' is required.");

            if (!string.IsNullOrWhiteSpace(inputTaskId) && !string.IsNullOrWhiteSpace(modelUrl))
                return ToolResult.Error("Provide either 'input_task_id' or 'model_url', not both.");

            var textStylePrompt = JsonHelper.ExtractString(inputJson, "text_style_prompt");
            var imageStyleUrl = JsonHelper.ExtractString(inputJson, "image_style_url");

            if (string.IsNullOrWhiteSpace(textStylePrompt) && string.IsNullOrWhiteSpace(imageStyleUrl))
                return ToolResult.Error("Either 'text_style_prompt' or 'image_style_url' is required.");

            var enablePbr = true;
            if (inputJson.Contains("\"enable_pbr\""))
                enablePbr = JsonHelper.ExtractBool(inputJson, "enable_pbr");

            try
            {
                var taskId = MeshyApiClient.CreateRetextureTask(
                    apiKey, inputTaskId, modelUrl, textStylePrompt, imageStyleUrl, enablePbr);

                var source = !string.IsNullOrWhiteSpace(inputTaskId) ? $"task {inputTaskId}" : "model URL";
                return ToolResult.Success(
                    $"Retexture task created: {taskId}\n" +
                    $"Source: {source}\n" +
                    (!string.IsNullOrEmpty(textStylePrompt) ? $"Style prompt: {textStylePrompt}\n" : "") +
                    (!string.IsNullOrEmpty(imageStyleUrl) ? "Style: reference image\n" : "") +
                    $"PBR maps: {(enablePbr ? "enabled" : "disabled")}\n" +
                    $"\nUse meshy_check_task with task_id '{taskId}' and source='retexture' to poll for completion. " +
                    "Then use meshy_download_model with task_id and source='retexture' to download.");
            }
            catch (Exception e)
            {
                return ToolResult.Error($"Failed to create retexture task: {e.Message}");
            }
        }
    }
}
