using System;

namespace UnityEli.Editor.Tools
{
    public class MeshyRemeshTool : IEliTool
    {
        public string Name => "meshy_remesh";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var apiKey = UnityEliSettings.MeshyApiKey;
            if (string.IsNullOrEmpty(apiKey))
                return ToolResult.Error(
                    "Meshy API key not configured. " +
                    "The user needs to set it in Preferences > Unity Eli > Meshy.ai Integration.");

            var inputTaskId = JsonHelper.ExtractString(inputJson, "input_task_id");
            var modelUrl = JsonHelper.ExtractString(inputJson, "model_url");

            if (string.IsNullOrWhiteSpace(inputTaskId) && string.IsNullOrWhiteSpace(modelUrl))
                return ToolResult.Error("Either 'input_task_id' or 'model_url' is required.");

            if (!string.IsNullOrWhiteSpace(inputTaskId) && !string.IsNullOrWhiteSpace(modelUrl))
                return ToolResult.Error("Provide either 'input_task_id' or 'model_url', not both.");

            var topology = JsonHelper.ExtractString(inputJson, "topology");
            var targetPolycount = JsonHelper.ExtractInt(inputJson, "target_polycount");

            try
            {
                var taskId = MeshyApiClient.CreateRemeshTask(apiKey, inputTaskId, modelUrl, topology, targetPolycount);

                var source = !string.IsNullOrWhiteSpace(inputTaskId) ? $"task {inputTaskId}" : "model URL";
                return ToolResult.Success(
                    $"Remesh task created: {taskId}\n" +
                    $"Source: {source}\n" +
                    (!string.IsNullOrEmpty(topology) ? $"Topology: {topology}\n" : "") +
                    (targetPolycount > 0 ? $"Target polycount: {targetPolycount}\n" : "") +
                    $"\nUse meshy_check_task with task_id '{taskId}' and source='remesh' to poll for completion. " +
                    "Then use meshy_download_model with task_id and source='remesh' to download.");
            }
            catch (Exception e)
            {
                return ToolResult.Error($"Failed to create remesh task: {e.Message}");
            }
        }
    }
}
