using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class SetQualitySettingsTool : IEliTool
    {
        public string Name => "set_quality_settings";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            var action = (input.action ?? "set_level").ToLowerInvariant();

            if (action == "get_levels")
            {
                var names = QualitySettings.names;
                var current = QualitySettings.GetQualityLevel();
                var list = names.Select((n, i) => i == current ? $"[{i}] {n} (current)" : $"[{i}] {n}");
                return ToolResult.Success("Quality levels:\n" + string.Join("\n", list));
            }

            if (action == "set_level")
            {
                if (string.IsNullOrWhiteSpace(input.level))
                    return ToolResult.Error("level is required for action 'set_level'. Provide an index or name.");

                int index;
                if (!int.TryParse(input.level, out index))
                {
                    // Try to find by name
                    var names = QualitySettings.names;
                    index = -1;
                    for (int i = 0; i < names.Length; i++)
                    {
                        if (string.Equals(names[i], input.level, StringComparison.OrdinalIgnoreCase))
                        {
                            index = i;
                            break;
                        }
                    }
                    if (index < 0)
                        return ToolResult.Error(
                            $"Quality level '{input.level}' not found. Available: {string.Join(", ", names)}.");
                }

                if (index < 0 || index >= QualitySettings.names.Length)
                    return ToolResult.Error($"Quality level index {index} is out of range (0–{QualitySettings.names.Length - 1}).");

                QualitySettings.SetQualityLevel(index, applyExpensiveChanges: true);
                return ToolResult.Success($"Quality level set to [{index}] '{QualitySettings.names[index]}'.");
            }

            return ToolResult.Error($"Unknown action '{input.action}'. Use: get_levels, set_level.");
        }

        [Serializable]
        private class Input
        {
            public string action;
            public string level;
        }
    }
}
