using System;

namespace UnityEli.Editor.Tools
{
    public class MeshyCheckTaskTool : IEliTool
    {
        public string Name => "meshy_check_task";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var apiKey = UnityEliSettings.MeshyApiKey;
            if (string.IsNullOrEmpty(apiKey))
                return ToolResult.Error("Meshy API key not configured. Set it in Preferences > Unity Eli > Meshy.ai Integration.");

            var taskId = JsonHelper.ExtractString(inputJson, "task_id");
            if (string.IsNullOrWhiteSpace(taskId))
                return ToolResult.Error("'task_id' is required.");

            var source = JsonHelper.ExtractString(inputJson, "source") ?? "text-to-3d";

            try
            {
                MeshyTaskStatus status;
                switch (source)
                {
                    case "remesh":    status = MeshyApiClient.CheckRemeshTask(apiKey, taskId); break;
                    case "retexture": status = MeshyApiClient.CheckRetextureTask(apiKey, taskId); break;
                    default:          status = MeshyApiClient.CheckTask(apiKey, taskId); break;
                }

                var sourceArg = source != "text-to-3d" ? $" and source='{source}'" : "";

                switch (status.Status)
                {
                    case "SUCCEEDED":
                        return HandleSucceeded(taskId, status, source);
                    case "FAILED":
                        return ToolResult.Error(
                            $"Task {taskId}: FAILED.\n" +
                            (!string.IsNullOrEmpty(status.ErrorMessage)
                                ? status.ErrorMessage
                                : "No error details available."));
                    case "CANCELED":
                        return ToolResult.Error($"Task {taskId}: CANCELED.");
                    default:
                        return ToolResult.Success(
                            $"Task {taskId}: {status.Status} ({status.Progress}%)\n" +
                            $"Wait ~10 seconds and call meshy_check_task again with task_id='{taskId}'{sourceArg}.");
                }
            }
            catch (Exception e)
            {
                return ToolResult.Error($"Failed to check task: {e.Message}");
            }
        }

        private static string HandleSucceeded(string taskId, MeshyTaskStatus status, string source)
        {
            if (source == "remesh" || source == "retexture")
            {
                var label = source == "remesh" ? "Remesh" : "Retexture";
                var dlSource = $" and source='{source}'";
                return ToolResult.Success(
                    $"Task {taskId}: SUCCEEDED (100%) — {label} complete.\n" +
                    $"Available formats: {status.AvailableFormats}\n" +
                    (status.HasTextures ? "Texture maps available.\n" : "") +
                    $"\nCall meshy_download_model with task_id '{taskId}'{dlSource} to download.");
            }

            var mode = status.Mode ?? "unknown";

            if (mode == "refine")
            {
                return ToolResult.Success(
                    $"Task {taskId}: SUCCEEDED (100%) — Texturing complete.\n" +
                    $"Available formats: {status.AvailableFormats}\n" +
                    (status.HasTextures ? "Texture maps available.\n" : "") +
                    $"\nCall meshy_download_model with task_id '{taskId}' to download the textured model.");
            }

            // Preview completed
            return ToolResult.Success(
                $"Task {taskId}: SUCCEEDED (100%) — Preview (untextured mesh) complete.\n" +
                $"Available formats: {status.AvailableFormats}\n" +
                $"\nNext step: Call meshy_generate_model with refine_task_id='{taskId}' to add textures.\n" +
                $"Or call meshy_download_model with task_id '{taskId}' to download the untextured mesh.");
        }
    }
}
