using System;
using System.Text;
using UnityEditor;
using UnityEditor.Build;

namespace UnityEli.Editor.Tools
{
    public class ManageBuildSettingsTool : IEliTool
    {
        public string Name => "manage_build_settings";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var action = JsonHelper.ExtractString(inputJson, "action") ?? "status";

            switch (action.ToLowerInvariant())
            {
                case "status":     return GetStatus();
                case "set_target": return SetTarget(inputJson);
                case "set_option": return SetOption(inputJson);
                default:
                    return ToolResult.Error($"Unknown action '{action}'. Valid: status, set_target, set_option.");
            }
        }

        private static string GetStatus()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Build Settings:");
            sb.AppendLine($"  Active target: {EditorUserBuildSettings.activeBuildTarget}");
            sb.AppendLine($"  Target group: {BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget)}");
            sb.AppendLine($"  Development build: {EditorUserBuildSettings.development}");
            sb.AppendLine($"  Script debugging: {EditorUserBuildSettings.allowDebugging}");
            sb.AppendLine($"  Autoconnect profiler: {EditorUserBuildSettings.connectProfiler}");
            sb.AppendLine($"  Deep profiling: {EditorUserBuildSettings.buildWithDeepProfilingSupport}");

            // List installed platforms
            sb.AppendLine();
            sb.AppendLine("Installed platforms:");
            foreach (BuildTarget target in Enum.GetValues(typeof(BuildTarget)))
            {
                if ((int)target < 0) continue;
                var group = BuildPipeline.GetBuildTargetGroup(target);
                if (group == BuildTargetGroup.Unknown) continue;
                if (!BuildPipeline.IsBuildTargetSupported(group, target)) continue;
                sb.AppendLine($"  {target}");
            }

            return ToolResult.Success(sb.ToString());
        }

        private static string SetTarget(string inputJson)
        {
            var targetStr = JsonHelper.ExtractString(inputJson, "target");
            if (string.IsNullOrWhiteSpace(targetStr))
                return ToolResult.Error("'target' is required. Examples: StandaloneWindows64, WebGL, Android, iOS.");

            if (!Enum.TryParse<BuildTarget>(targetStr, true, out var buildTarget))
                return ToolResult.Error(
                    $"Unknown build target '{targetStr}'. " +
                    $"Use action 'status' to see installed platforms.");

            var group = BuildPipeline.GetBuildTargetGroup(buildTarget);
            if (group == BuildTargetGroup.Unknown)
                return ToolResult.Error($"Could not determine target group for '{buildTarget}'.");

            if (!BuildPipeline.IsBuildTargetSupported(group, buildTarget))
                return ToolResult.Error($"Build target '{buildTarget}' is not installed. Install it via Unity Hub.");

            if (EditorUserBuildSettings.activeBuildTarget == buildTarget)
                return ToolResult.Success($"Already on target '{buildTarget}'.");

            var switched = EditorUserBuildSettings.SwitchActiveBuildTarget(group, buildTarget);
            if (!switched)
                return ToolResult.Error($"Failed to switch to '{buildTarget}'. Check the console for details.");

            return ToolResult.Success($"Switched build target to '{buildTarget}' (group: {group}).");
        }

        private static string SetOption(string inputJson)
        {
            var option = JsonHelper.ExtractString(inputJson, "option");
            var valueStr = JsonHelper.ExtractString(inputJson, "value");

            if (string.IsNullOrWhiteSpace(option))
                return ToolResult.Error("'option' is required. Supported: development, allowDebugging, connectProfiler, deepProfiling.");
            if (string.IsNullOrWhiteSpace(valueStr))
                return ToolResult.Error("'value' is required (e.g. 'true' or 'false').");

            if (!bool.TryParse(valueStr, out var boolVal))
                return ToolResult.Error($"Cannot parse '{valueStr}' as boolean. Use 'true' or 'false'.");

            switch (option.ToLowerInvariant())
            {
                case "development":
                    EditorUserBuildSettings.development = boolVal;
                    return ToolResult.Success($"Development build: {boolVal}");

                case "allowdebugging":
                    EditorUserBuildSettings.allowDebugging = boolVal;
                    return ToolResult.Success($"Script debugging: {boolVal}");

                case "connectprofiler":
                    EditorUserBuildSettings.connectProfiler = boolVal;
                    return ToolResult.Success($"Autoconnect profiler: {boolVal}");

                case "deepprofiling":
                    EditorUserBuildSettings.buildWithDeepProfilingSupport = boolVal;
                    return ToolResult.Success($"Deep profiling support: {boolVal}");

                default:
                    return ToolResult.Error(
                        $"Unknown option '{option}'. Supported: development, allowDebugging, connectProfiler, deepProfiling.");
            }
        }
    }
}
