using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class SetPlayerSettingsTool : IEliTool
    {
        public string Name => "set_player_settings";
        public bool NeedsAssetRefresh => false;

        private static readonly Dictionary<string, string[]> PropertyAliases = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "company_name",        new[] { "company_name", "companyname", "company" } },
            { "product_name",        new[] { "product_name", "productname", "display_name", "displayname" } },
            { "version",             new[] { "version", "bundle_version", "bundleversion" } },
            { "bundle_id",           new[] { "bundle_id", "bundleid", "application_identifier", "appid" } },
            { "default_orientation", new[] { "default_orientation", "defaultorientation", "orientation" } },
        };

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.property))
                return ToolResult.Error("property is required. Supported: company_name, product_name, version, bundle_id, default_orientation.");
            if (string.IsNullOrWhiteSpace(input.value))
                return ToolResult.Error("value is required.");

            var prop = ResolveProperty(input.property);

            switch (prop)
            {
                case "company_name":
                    PlayerSettings.companyName = input.value;
                    return ToolResult.Success($"Company name set to '{input.value}'.");

                case "product_name":
                    PlayerSettings.productName = input.value;
                    return ToolResult.Success($"Product name set to '{input.value}'.");

                case "version":
                    PlayerSettings.bundleVersion = input.value;
                    return ToolResult.Success($"Version set to '{input.value}'.");

                case "bundle_id":
                {
                    var target = ResolveNamedBuildTarget(input.build_target);
                    PlayerSettings.SetApplicationIdentifier(target, input.value);
                    return ToolResult.Success(
                        $"Bundle ID set to '{input.value}' for {target}.");
                }

                case "default_orientation":
                {
                    if (!Enum.TryParse<UIOrientation>(input.value, true, out var orientation))
                        return ToolResult.Error(
                            $"Invalid orientation '{input.value}'. Valid values: Portrait, PortraitUpsideDown, LandscapeRight, LandscapeLeft, AutoRotation.");
                    PlayerSettings.defaultInterfaceOrientation = orientation;
                    return ToolResult.Success($"Default orientation set to '{orientation}'.");
                }

                default:
                    return ToolResult.Error(
                        $"Unknown property '{input.property}'. Supported: company_name, product_name, version, bundle_id, default_orientation.");
            }
        }

        private static string ResolveProperty(string name)
        {
            foreach (var kvp in PropertyAliases)
            {
                foreach (var alias in kvp.Value)
                {
                    if (string.Equals(alias, name, StringComparison.OrdinalIgnoreCase))
                        return kvp.Key;
                }
            }
            return name.ToLowerInvariant();
        }

        private static NamedBuildTarget ResolveNamedBuildTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
                return NamedBuildTarget.Standalone;

            switch (target.ToLowerInvariant())
            {
                case "android": return NamedBuildTarget.Android;
                case "ios":     return NamedBuildTarget.iOS;
                case "webgl":   return NamedBuildTarget.WebGL;
                default:        return NamedBuildTarget.Standalone;
            }
        }

        [Serializable]
        private class Input
        {
            public string property;
            public string value;
            public string build_target;
        }
    }
}
