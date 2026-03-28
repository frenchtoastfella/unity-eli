using System.IO;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class EditScriptTool : IEliTool
    {
        public string Name => "edit_script";
        public bool NeedsAssetRefresh => true;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.file_path))
            {
                return ToolResult.Error("file_path is required.");
            }

            var fullPath = Path.Combine("Assets", input.file_path);

            if (!File.Exists(fullPath))
            {
                return ToolResult.Error($"File not found at '{fullPath}'. Use create_script to create new files.");
            }

            // Find/replace mode
            if (!string.IsNullOrEmpty(input.find))
            {
                if (input.replace == null)
                    return ToolResult.Error("'replace' is required when using 'find'.");

                var existing = File.ReadAllText(fullPath);
                var count = CountOccurrences(existing, input.find);

                if (count == 0)
                    return ToolResult.Error($"Could not find the specified text in '{fullPath}'. Make sure 'find' matches the exact text including whitespace.");

                var updated = existing.Replace(input.find, input.replace);
                File.WriteAllText(fullPath, updated);

                return ToolResult.Success(
                    $"Replaced {count} occurrence(s) in '{fullPath}'. " +
                    "Script compilation will begin — wait for it to complete before adding components.");
            }

            // Full rewrite mode
            if (string.IsNullOrWhiteSpace(input.content))
            {
                return ToolResult.Error("Either 'content' (full rewrite) or 'find'+'replace' (targeted edit) is required.");
            }

            File.WriteAllText(fullPath, input.content);

            return ToolResult.Success(
                $"Script updated at '{fullPath}'. " +
                "Script compilation will begin — wait for it to complete before adding components.");
        }

        private static int CountOccurrences(string text, string search)
        {
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(search, index, System.StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += search.Length;
            }
            return count;
        }

        [System.Serializable]
        private class Input
        {
            public string file_path;
            public string content;
            public string find;
            public string replace;
        }
    }
}
