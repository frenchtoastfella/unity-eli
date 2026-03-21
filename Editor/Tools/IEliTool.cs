namespace UnityEli.Editor.Tools
{
    /// <summary>
    /// Interface for all Unity Eli tools that Claude can invoke.
    /// Each tool has a name matching its JSON definition and an Execute method.
    /// </summary>
    public interface IEliTool
    {
        /// <summary>
        /// The tool name. Must match the "name" field in the corresponding .tool.json file.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether this tool writes to the AssetDatabase and needs a Refresh after execution.
        /// Tools that create/edit scripts or assets should return true.
        /// The executor will batch all refreshes after all tools in a response are done.
        /// </summary>
        bool NeedsAssetRefresh { get; }

        /// <summary>
        /// Execute the tool with the given JSON input from Claude's tool_use call.
        /// Returns a result string to send back as tool_result.
        /// </summary>
        string Execute(string inputJson);
    }
}
