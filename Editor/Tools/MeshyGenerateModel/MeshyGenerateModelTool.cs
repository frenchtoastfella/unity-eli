using System;

namespace UnityEli.Editor.Tools
{
    public class MeshyGenerateModelTool : IEliTool
    {
        public string Name => "meshy_generate_model";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var apiKey = UnityEliSettings.MeshyApiKey;
            if (string.IsNullOrEmpty(apiKey))
                return ToolResult.Error(
                    "Meshy API key not configured. " +
                    "The user needs to set it in Preferences > Unity Eli > Meshy.ai Integration.");

            var refineTaskId = JsonHelper.ExtractString(inputJson, "refine_task_id");

            if (!string.IsNullOrEmpty(refineTaskId))
                return CreateRefineTask(apiKey, inputJson, refineTaskId);

            return CreatePreviewTask(apiKey, inputJson);
        }

        private static string CreatePreviewTask(string apiKey, string inputJson)
        {
            var prompt = JsonHelper.ExtractString(inputJson, "prompt");
            if (string.IsNullOrWhiteSpace(prompt))
                return ToolResult.Error("'prompt' is required when creating a preview task.");

            var aiModel = JsonHelper.ExtractString(inputJson, "ai_model") ?? "meshy-4";
            var topology = JsonHelper.ExtractString(inputJson, "topology") ?? "triangle";
            var targetPolycount = JsonHelper.ExtractInt(inputJson, "target_polycount");

            try
            {
                var taskId = MeshyApiClient.CreatePreviewTask(apiKey, prompt, aiModel, topology, targetPolycount);
                return ToolResult.Success(
                    $"Preview task created: {taskId}\n" +
                    $"Prompt: {prompt}\n" +
                    $"AI Model: {aiModel}, Topology: {topology}" +
                    (targetPolycount > 0 ? $", Target polycount: {targetPolycount}" : "") +
                    "\n\nUse meshy_check_task with task_id '" + taskId + "' to poll for completion. " +
                    "Generation typically takes 30-120 seconds. Check every ~10 seconds.");
            }
            catch (Exception e)
            {
                return ToolResult.Error($"Failed to create preview task: {e.Message}");
            }
        }

        private static string CreateRefineTask(string apiKey, string inputJson, string previewTaskId)
        {
            var enablePbr = true;
            // Only override if explicitly set to false
            if (inputJson.Contains("\"enable_pbr\""))
                enablePbr = JsonHelper.ExtractBool(inputJson, "enable_pbr");

            var texturePrompt = JsonHelper.ExtractString(inputJson, "texture_prompt");

            try
            {
                var taskId = MeshyApiClient.CreateRefineTask(apiKey, previewTaskId, enablePbr, texturePrompt);
                return ToolResult.Success(
                    $"Refine (texturing) task created: {taskId}\n" +
                    $"Preview task: {previewTaskId}\n" +
                    $"PBR maps: {(enablePbr ? "enabled" : "disabled")}" +
                    (!string.IsNullOrEmpty(texturePrompt) ? $"\nTexture prompt: {texturePrompt}" : "") +
                    "\n\nUse meshy_check_task with task_id '" + taskId + "' to poll for completion. " +
                    "Texturing typically takes 30-120 seconds. Check every ~10 seconds.");
            }
            catch (Exception e)
            {
                return ToolResult.Error($"Failed to create refine task: {e.Message}");
            }
        }
    }
}
