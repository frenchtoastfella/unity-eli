using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    /// <summary>
    /// Discovers and manages all available tools.
    /// Loads JSON definitions from .tool.json files and instantiates matching C# handlers.
    /// Tool definitions are served to Claude Code via the MCP server.
    /// </summary>
    public static class ToolRegistry
    {
        private static Dictionary<string, IEliTool> _tools;
        private static List<ToolDefinition> _toolDefinitions;
        private static bool _isInitialized;

        public static void Initialize()
        {
            _tools = new Dictionary<string, IEliTool>();
            _toolDefinitions = new List<ToolDefinition>();

            // Discover all IEliTool implementations via reflection
            var toolInterfaceType = typeof(IEliTool);
            var toolTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .Where(t => toolInterfaceType.IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var toolType in toolTypes)
            {
                try
                {
                    var tool = (IEliTool)Activator.CreateInstance(toolType);
                    _tools[tool.Name] = tool;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Unity Eli] Failed to instantiate tool {toolType.Name}: {e.Message}");
                }
            }

            // Load JSON definitions from .tool.json files
            var toolsRoot = "Assets/UnityEli/Editor/Tools";
            if (Directory.Exists(toolsRoot))
            {
                var jsonFiles = Directory.GetFiles(toolsRoot, "*.tool.json", SearchOption.AllDirectories);
                foreach (var jsonFile in jsonFiles)
                {
                    try
                    {
                        var json = File.ReadAllText(jsonFile);
                        var definition = ToolDefinition.FromJson(json);

                        if (definition != null && !string.IsNullOrEmpty(definition.name))
                        {
                            if (_tools.ContainsKey(definition.name))
                            {
                                _toolDefinitions.Add(definition);
                            }
                            else
                            {
                                Debug.LogWarning($"[Unity Eli] Tool definition '{definition.name}' in {jsonFile} has no matching C# handler.");
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[Unity Eli] Failed to load tool definition from {jsonFile}: {e.Message}");
                    }
                }
            }

            _isInitialized = true;
            Debug.Log($"[Unity Eli] Tool registry initialized: {_toolDefinitions.Count} tools available.");
        }

        public static List<ToolDefinition> GetToolDefinitions()
        {
            if (!_isInitialized) Initialize();
            return _toolDefinitions;
        }

        public static IEliTool GetTool(string name)
        {
            if (!_isInitialized) Initialize();
            _tools.TryGetValue(name, out var tool);
            return tool;
        }

        public static string ExecuteTool(string name, string inputJson)
        {
            return ExecuteTool(name, inputJson, out _);
        }

        public static string ExecuteTool(string name, string inputJson, out bool needsRefresh)
        {
            needsRefresh = false;

            var tool = GetTool(name);
            if (tool == null)
                return ToolResult.Error($"Unknown tool: '{name}'.");

            try
            {
                needsRefresh = tool.NeedsAssetRefresh;
                var result = tool.Execute(inputJson);
                // Guard against tools returning null/empty — always return something
                if (string.IsNullOrEmpty(result))
                    return ToolResult.Success($"Tool '{name}' completed.");
                return result;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Unity Eli] Tool '{name}' execution failed: {e}");
                return ToolResult.Error($"Tool execution failed: {e.Message}");
            }
        }

        public static void Reload()
        {
            _isInitialized = false;
            _tools = null;
            _toolDefinitions = null;
        }
    }

    /// <summary>
    /// Represents a tool definition loaded from a .tool.json file.
    /// Keeps the raw JSON for serving to Claude Code via MCP.
    /// </summary>
    public class ToolDefinition
    {
        public string name;
        public string description;

        /// <summary>
        /// The raw JSON from the .tool.json file.
        /// </summary>
        public string RawJson { get; private set; }

        public static ToolDefinition FromJson(string json)
        {
            var def = UnityEngine.JsonUtility.FromJson<ToolDefinition>(json);
            if (def != null)
                def.RawJson = json;
            return def;
        }
    }
}
