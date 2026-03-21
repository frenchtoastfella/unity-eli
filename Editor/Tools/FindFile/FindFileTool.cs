using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace UnityEli.Editor.Tools
{
    public class FindFileTool : IEliTool
    {
        public string Name => "find_file";
        public bool NeedsAssetRefresh => false;

        private const int MaxResults = 25;

        public string Execute(string inputJson)
        {
            var input = JsonUtility.FromJson<Input>(inputJson);

            if (string.IsNullOrWhiteSpace(input.query))
                return ToolResult.Error("query is required.");

            var searchRoot = "Assets";
            if (!string.IsNullOrWhiteSpace(input.folder))
            {
                if (!Directory.Exists(input.folder))
                    return ToolResult.Error($"Folder '{input.folder}' not found.");
                searchRoot = input.folder;
            }

            var pattern = "*.*";
            if (!string.IsNullOrWhiteSpace(input.extension))
            {
                var ext = input.extension.StartsWith(".") ? input.extension : "." + input.extension;
                pattern = "*" + ext;
            }

            // Get all files matching the extension filter
            string[] allFiles;
            try
            {
                allFiles = Directory.GetFiles(searchRoot, pattern, SearchOption.AllDirectories);
            }
            catch (Exception e)
            {
                return ToolResult.Error($"Error searching files: {e.Message}");
            }

            var query = input.query;
            var results = new List<string>();

            // First pass: exact file name match (case-insensitive)
            foreach (var file in allFiles)
            {
                var fileName = Path.GetFileName(file);
                if (string.Equals(fileName, query, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileNameWithoutExtension(file), query, StringComparison.OrdinalIgnoreCase))
                {
                    results.Add(file.Replace("\\", "/"));
                }
            }

            // Second pass: file name contains query (case-insensitive)
            if (results.Count < MaxResults)
            {
                foreach (var file in allFiles)
                {
                    var normalized = file.Replace("\\", "/");
                    if (results.Contains(normalized)) continue;

                    var fileName = Path.GetFileName(file);
                    if (fileName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        results.Add(normalized);
                        if (results.Count >= MaxResults) break;
                    }
                }
            }

            // Third pass: full path contains query (case-insensitive)
            if (results.Count < MaxResults)
            {
                foreach (var file in allFiles)
                {
                    var normalized = file.Replace("\\", "/");
                    if (results.Contains(normalized)) continue;

                    if (normalized.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        results.Add(normalized);
                        if (results.Count >= MaxResults) break;
                    }
                }
            }

            if (results.Count == 0)
            {
                var msg = $"No files found matching '{query}'";
                if (!string.IsNullOrWhiteSpace(input.extension))
                    msg += $" with extension '{input.extension}'";
                if (!string.IsNullOrWhiteSpace(input.folder))
                    msg += $" in '{input.folder}'";
                return ToolResult.Success(msg + ".");
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Found {results.Count} file(s) matching '{query}':");
            sb.AppendLine();
            foreach (var path in results)
            {
                sb.AppendLine($"  {path}");
            }

            if (results.Count >= MaxResults)
                sb.AppendLine($"\n(Results limited to {MaxResults}. Narrow your query for more specific results.)");

            return ToolResult.Success(sb.ToString());
        }

        [Serializable]
        private class Input
        {
            public string query;
            public string extension;
            public string folder;
        }
    }
}
