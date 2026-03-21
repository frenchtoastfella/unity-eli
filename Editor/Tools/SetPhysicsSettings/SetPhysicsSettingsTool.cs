using System;
using UnityEditor;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class SetPhysicsSettingsTool : IEliTool
    {
        public string Name => "set_physics_settings";
        public bool NeedsAssetRefresh => false;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.property))
                return ToolResult.Error(
                    "property is required. Supported: gravity, gravity_y, bounce_threshold, sleep_threshold, " +
                    "default_contact_offset, layer_collision.");
            if (string.IsNullOrWhiteSpace(input.value))
                return ToolResult.Error("value is required.");

            switch (input.property.ToLowerInvariant())
            {
                case "gravity":
                {
                    var v = EliToolHelpers.ParseVector(input.value, 3);
                    if (v == null)
                        return ToolResult.Error("gravity requires a Vector3 value, e.g. '0,-9.81,0'.");
                    Physics.gravity = new Vector3(v[0], v[1], v[2]);
                    return ToolResult.Success($"Physics.gravity set to ({v[0]}, {v[1]}, {v[2]}).");
                }

                case "gravity_y":
                {
                    if (!float.TryParse(input.value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var gy))
                        return ToolResult.Error("gravity_y requires a float value, e.g. '-9.81'.");
                    Physics.gravity = new Vector3(0f, gy, 0f);
                    return ToolResult.Success($"Physics.gravity set to (0, {gy}, 0).");
                }

                case "bounce_threshold":
                {
                    if (!float.TryParse(input.value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var bt))
                        return ToolResult.Error("bounce_threshold requires a float value.");
                    Physics.bounceThreshold = bt;
                    return ToolResult.Success($"Physics.bounceThreshold set to {bt}.");
                }

                case "sleep_threshold":
                {
                    if (!float.TryParse(input.value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var st))
                        return ToolResult.Error("sleep_threshold requires a float value.");
                    Physics.sleepThreshold = st;
                    return ToolResult.Success($"Physics.sleepThreshold set to {st}.");
                }

                case "default_contact_offset":
                {
                    if (!float.TryParse(input.value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var co))
                        return ToolResult.Error("default_contact_offset requires a float value.");
                    Physics.defaultContactOffset = co;
                    return ToolResult.Success($"Physics.defaultContactOffset set to {co}.");
                }

                case "layer_collision":
                {
                    // Format: "LayerA,LayerB,true" (true = ignore collision, false = enable collision)
                    var parts = input.value.Split(',');
                    if (parts.Length < 3)
                        return ToolResult.Error(
                            "layer_collision format: 'LayerA,LayerB,true/false' where true=ignore, false=collide.");

                    var layerA = LayerMask.NameToLayer(parts[0].Trim());
                    var layerB = LayerMask.NameToLayer(parts[1].Trim());
                    if (layerA < 0)
                        return ToolResult.Error($"Layer '{parts[0].Trim()}' not found.");
                    if (layerB < 0)
                        return ToolResult.Error($"Layer '{parts[1].Trim()}' not found.");

                    if (!bool.TryParse(parts[2].Trim(), out var ignore))
                        return ToolResult.Error("Third argument must be 'true' (ignore) or 'false' (collide).");

                    Physics.IgnoreLayerCollision(layerA, layerB, ignore);
                    var action = ignore ? "will ignore" : "will collide with";
                    return ToolResult.Success(
                        $"Layer '{parts[0].Trim()}' {action} layer '{parts[1].Trim()}'.");
                }

                default:
                    return ToolResult.Error(
                        $"Unknown property '{input.property}'. Supported: gravity, gravity_y, " +
                        "bounce_threshold, sleep_threshold, default_contact_offset, layer_collision.");
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
