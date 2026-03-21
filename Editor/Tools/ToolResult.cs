namespace UnityEli.Editor.Tools
{
    /// <summary>
    /// Helper for building consistent tool result strings.
    /// </summary>
    public static class ToolResult
    {
        public static string Success(string message)
        {
            return message;
        }

        public static string Error(string message)
        {
            return $"ERROR: {message}";
        }
    }
}
