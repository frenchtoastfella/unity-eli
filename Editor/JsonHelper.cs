using System.Collections.Generic;
using System.Text;

namespace UnityEli.Editor
{
    /// <summary>
    /// Shared JSON parsing and building utilities used across UnityEli editor classes.
    /// Hand-rolled to avoid any external JSON dependency.
    /// </summary>
    internal static class JsonHelper
    {
        public static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }

        /// <summary>
        /// Extracts the value of a string field from a JSON object.
        /// Returns null if the key is not found or the value is not a string.
        /// </summary>
        public static string ExtractString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;

            var searchKey = $"\"{key}\"";
            var keyIndex = json.IndexOf(searchKey, System.StringComparison.Ordinal);
            if (keyIndex < 0) return null;

            var colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return null;

            var valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;

            if (valueStart >= json.Length || json[valueStart] == 'n') return null;
            if (json[valueStart] != '"') return null;

            var sb = new StringBuilder();
            var i = valueStart + 1;
            while (i < json.Length)
            {
                if (json[i] == '\\' && i + 1 < json.Length)
                {
                    var next = json[i + 1];
                    switch (next)
                    {
                        case '"':  sb.Append('"');  break;
                        case '\\': sb.Append('\\'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        default:   sb.Append(next); break;
                    }
                    i += 2;
                }
                else if (json[i] == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(json[i]);
                    i++;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Extracts an integer field. Returns 0 if not found or not a number.
        /// </summary>
        public static int ExtractInt(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return 0;

            var searchKey = $"\"{key}\"";
            var keyIndex = json.IndexOf(searchKey, System.StringComparison.Ordinal);
            if (keyIndex < 0) return 0;

            var colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return 0;

            var valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;

            var valueEnd = valueStart;
            while (valueEnd < json.Length && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '-')) valueEnd++;

            if (valueEnd == valueStart) return 0;
            int.TryParse(json.Substring(valueStart, valueEnd - valueStart), out int result);
            return result;
        }

        /// <summary>
        /// Extracts a boolean field. Returns false if not found.
        /// </summary>
        public static bool ExtractBool(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return false;

            var searchKey = $"\"{key}\"";
            var keyIndex = json.IndexOf(searchKey, System.StringComparison.Ordinal);
            if (keyIndex < 0) return false;

            var colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return false;

            var valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;

            return valueStart < json.Length && json[valueStart] == 't';
        }

        /// <summary>
        /// Extracts a float field. Returns defaultValue if the key is not found.
        /// </summary>
        public static float ExtractFloat(string json, string key, float defaultValue)
        {
            if (string.IsNullOrEmpty(json)) return defaultValue;

            var searchKey = $"\"{key}\"";
            var keyIndex = json.IndexOf(searchKey, System.StringComparison.Ordinal);
            if (keyIndex < 0) return defaultValue;

            var colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return defaultValue;

            var valueStart = colonIndex + 1;
            while (valueStart < json.Length && char.IsWhiteSpace(json[valueStart])) valueStart++;

            var valueEnd = valueStart;
            while (valueEnd < json.Length && (char.IsDigit(json[valueEnd]) || json[valueEnd] == '-' || json[valueEnd] == '.' || json[valueEnd] == 'e' || json[valueEnd] == 'E' || json[valueEnd] == '+')) valueEnd++;

            if (valueEnd == valueStart) return defaultValue;
            if (float.TryParse(json.Substring(valueStart, valueEnd - valueStart),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result))
                return result;
            return defaultValue;
        }

        /// <summary>
        /// Extracts the raw JSON for an object-valued field.
        /// Returns "{}" if not found.
        /// </summary>
        public static string ExtractObject(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return "{}";

            var searchKey = $"\"{key}\"";
            var keyIndex = json.IndexOf(searchKey, System.StringComparison.Ordinal);
            if (keyIndex < 0) return "{}";

            var colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return "{}";

            var objStart = json.IndexOf('{', colonIndex);
            if (objStart < 0) return "{}";

            return ExtractBracketedBlock(json, objStart, '{', '}') ?? "{}";
        }

        /// <summary>
        /// Extracts the raw JSON for an array-valued field.
        /// Returns "[]" if not found.
        /// </summary>
        public static string ExtractArray(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return "[]";

            var searchKey = $"\"{key}\"";
            var keyIndex = json.IndexOf(searchKey, System.StringComparison.Ordinal);
            if (keyIndex < 0) return "[]";

            var colonIndex = json.IndexOf(':', keyIndex + searchKey.Length);
            if (colonIndex < 0) return "[]";

            var arrStart = json.IndexOf('[', colonIndex);
            if (arrStart < 0) return "[]";

            return ExtractBracketedBlock(json, arrStart, '[', ']') ?? "[]";
        }

        /// <summary>
        /// Extracts a JSON array starting at startIndex (must point at '[').
        /// </summary>
        public static string ExtractArrayAt(string json, int startIndex)
        {
            return ExtractBracketedBlock(json, startIndex, '[', ']');
        }

        /// <summary>
        /// Splits a JSON array string into individual top-level item strings (objects or arrays).
        /// </summary>
        public static List<string> ParseArray(string arrayJson)
        {
            var items = new List<string>();
            if (string.IsNullOrEmpty(arrayJson) || arrayJson.Length < 2) return items;

            var inner = arrayJson.Substring(1, arrayJson.Length - 2).Trim();
            if (string.IsNullOrEmpty(inner)) return items;

            var depth = 0;
            var itemStart = -1;
            var inString = false;

            for (int i = 0; i < inner.Length; i++)
            {
                var c = inner[i];

                if (inString)
                {
                    if (c == '\\') i++; // skip escaped char
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') { inString = true; continue; }

                if (c == '{' || c == '[')
                {
                    if (depth == 0) itemStart = i;
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 0 && itemStart >= 0)
                    {
                        items.Add(inner.Substring(itemStart, i - itemStart + 1));
                        itemStart = -1;
                    }
                }
            }

            return items;
        }

        /// <summary>
        /// Parses a JSON array of strings, e.g. ["a","b","c"], returning the unescaped string values.
        /// </summary>
        public static List<string> ParseStringArray(string arrayJson)
        {
            var items = new List<string>();
            if (string.IsNullOrEmpty(arrayJson) || arrayJson.Length < 2) return items;

            var inner = arrayJson.Substring(1, arrayJson.Length - 2).Trim();
            if (string.IsNullOrEmpty(inner)) return items;

            var inString = false;
            var start = -1;

            for (int i = 0; i < inner.Length; i++)
            {
                var c = inner[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"')
                    {
                        items.Add(UnescapeJson(inner.Substring(start, i - start)));
                        inString = false;
                    }
                }
                else if (c == '"')
                {
                    inString = true;
                    start = i + 1;
                }
            }

            return items;
        }

        private static string UnescapeJson(string s)
        {
            if (s.IndexOf('\\') < 0) return s;
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '\\' && i + 1 < s.Length)
                {
                    i++;
                    switch (s[i])
                    {
                        case '"':  sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/':  sb.Append('/'); break;
                        case 'n':  sb.Append('\n'); break;
                        case 'r':  sb.Append('\r'); break;
                        case 't':  sb.Append('\t'); break;
                        default:   sb.Append('\\'); sb.Append(s[i]); break;
                    }
                }
                else
                {
                    sb.Append(s[i]);
                }
            }
            return sb.ToString();
        }

        private static string ExtractBracketedBlock(string json, int startIndex, char open, char close)
        {
            var depth = 0;
            var inString = false;

            for (int i = startIndex; i < json.Length; i++)
            {
                var c = json[i];

                if (inString)
                {
                    if (c == '\\') i++;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') { inString = true; continue; }

                if (c == open) depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0) return json.Substring(startIndex, i - startIndex + 1);
                }
            }

            return null;
        }
    }
}
