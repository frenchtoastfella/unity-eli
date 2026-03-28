using System.Collections.Generic;
using System.Text;

namespace UnityEli.Editor.Tools
{
    /// <summary>
    /// Executes multiple tool calls in a single MCP round trip.
    /// Each entry in the "calls" array specifies a tool name and its arguments.
    /// Tools are executed sequentially on the main thread so earlier results
    /// (e.g. created GameObjects) are available to later calls.
    /// </summary>
    public class BatchExecuteTool : IEliTool
    {
        public string Name => "batch_execute";
        public bool NeedsAssetRefresh => true; // conservative — some sub-tools may need it

        public string Execute(string inputJson)
        {
            var callsArray = JsonHelper.ExtractArray(inputJson, "calls");
            if (callsArray == "[]")
                return ToolResult.Error("'calls' array is required and must not be empty.");

            var items = JsonHelper.ParseArray(callsArray);
            if (items.Count == 0)
                return ToolResult.Error("'calls' array is empty.");

            var results = new List<string>();
            int successCount = 0;
            int errorCount = 0;

            for (int i = 0; i < items.Count; i++)
            {
                var callJson = items[i];
                var toolName = JsonHelper.ExtractString(callJson, "tool");
                var argsJson = JsonHelper.ExtractObject(callJson, "arguments");

                if (string.IsNullOrEmpty(toolName))
                {
                    results.Add($"[{i + 1}] ERROR: missing 'tool' name");
                    errorCount++;
                    continue;
                }

                // Prevent recursive batch calls
                if (toolName == "batch_execute")
                {
                    results.Add($"[{i + 1}] ERROR: cannot nest batch_execute");
                    errorCount++;
                    continue;
                }

                var tool = ToolRegistry.GetTool(toolName);
                if (tool == null)
                {
                    results.Add($"[{i + 1}] ERROR: unknown tool '{toolName}'");
                    errorCount++;
                    continue;
                }

                try
                {
                    var result = tool.Execute(argsJson != "{}" ? argsJson : "{}");
                    if (string.IsNullOrEmpty(result))
                        result = "OK";

                    var isError = result.StartsWith("ERROR:");
                    if (isError)
                        errorCount++;
                    else
                        successCount++;

                    results.Add($"[{i + 1}] {toolName}: {result}");
                }
                catch (System.Exception e)
                {
                    results.Add($"[{i + 1}] {toolName}: ERROR: {e.Message}");
                    errorCount++;
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Batch complete: {successCount} succeeded, {errorCount} failed out of {items.Count} calls.");
            foreach (var r in results)
                sb.AppendLine(r);

            return errorCount == items.Count
                ? ToolResult.Error(sb.ToString())
                : ToolResult.Success(sb.ToString());
        }
    }
}
