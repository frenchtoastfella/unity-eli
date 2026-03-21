using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class SetTimeSettingsTool : IEliTool
    {
        public string Name => "set_time_settings";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.property))
                return ToolResult.Error(
                    "property is required. Supported: fixed_timestep, max_timestep, time_scale, maximum_particle_timestep.");
            if (string.IsNullOrWhiteSpace(input.value))
                return ToolResult.Error("value is required.");

            if (!float.TryParse(input.value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var floatVal))
                return ToolResult.Error($"value must be a number. Got: '{input.value}'.");

            switch (input.property.ToLowerInvariant())
            {
                case "fixed_timestep":
                case "fixedtimestep":
                case "fixed_delta_time":
                    if (floatVal <= 0f)
                        return ToolResult.Error("fixed_timestep must be greater than 0.");
                    Time.fixedDeltaTime = floatVal;
                    return ToolResult.Success($"Time.fixedDeltaTime set to {floatVal}.");

                case "max_timestep":
                case "maxtimestep":
                case "maximum_delta_time":
                    if (floatVal <= 0f)
                        return ToolResult.Error("max_timestep must be greater than 0.");
                    Time.maximumDeltaTime = floatVal;
                    return ToolResult.Success($"Time.maximumDeltaTime set to {floatVal}.");

                case "time_scale":
                case "timescale":
                    if (floatVal < 0f)
                        return ToolResult.Error("time_scale cannot be negative.");
                    Time.timeScale = floatVal;
                    return ToolResult.Success($"Time.timeScale set to {floatVal}.");

                case "maximum_particle_timestep":
                case "maximumparticletimestep":
                    if (floatVal <= 0f)
                        return ToolResult.Error("maximum_particle_timestep must be greater than 0.");
                    Time.maximumParticleDeltaTime = floatVal;
                    return ToolResult.Success($"Time.maximumParticleDeltaTime set to {floatVal}.");

                default:
                    return ToolResult.Error(
                        $"Unknown property '{input.property}'. Supported: fixed_timestep, max_timestep, time_scale, maximum_particle_timestep.");
            }
        }

        [Serializable]
        private class Input
        {
            public string property;
            public string value;
        }
    }
}
