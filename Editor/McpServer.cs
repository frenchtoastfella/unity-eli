using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEli.Editor.Tools;

namespace UnityEli.Editor
{
    /// <summary>
    /// Hosts a local HTTP server implementing the MCP (Model Context Protocol) JSON-RPC 2.0
    /// protocol over HTTP. Claude Code connects to this server to discover and invoke Unity
    /// editor tools without requiring a separate Anthropic API key.
    ///
    /// The server handles three MCP methods:
    ///   initialize  - handshake
    ///   tools/list  - returns all IEliTool definitions in MCP format
    ///   tools/call  - executes an IEliTool on the Unity main thread, returns the result
    /// </summary>
    [InitializeOnLoad]
    public static class McpServer
    {
        private const int BasePort = 47880;
        private const int MaxPortAttempts = 10;

        public static int Port { get; private set; }
        public static bool IsRunning { get; private set; }

        /// <summary>
        /// Fired on the main thread after a tool executes.
        /// Parameters: toolName, result, isError
        /// </summary>
        public static event Action<string, string, bool> ToolExecuted;

        private static HttpListener _listener;
        private static CancellationTokenSource _cts;
        private static readonly object _lock = new object();
        private static readonly ConcurrentQueue<PendingToolCall> _queue = new ConcurrentQueue<PendingToolCall>();

        static McpServer()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
            EditorApplication.quitting += Stop;
        }

        public static void Start()
        {
            lock (_lock)
            {
                if (IsRunning) return;

                ToolRegistry.Initialize();

                for (int port = BasePort; port < BasePort + MaxPortAttempts; port++)
                {
                    try
                    {
                        _listener = new HttpListener();
                        _listener.Prefixes.Add($"http://localhost:{port}/mcp/");
                        _listener.Start();
                        Port = port;
                        break;
                    }
                    catch (Exception)
                    {
                        _listener = null;
                    }
                }

                if (_listener == null)
                {
                    Debug.LogError($"[Unity Eli] MCP server could not find a free port in range {BasePort}-{BasePort + MaxPortAttempts - 1}.");
                    return;
                }

                _cts = new CancellationTokenSource();
                IsRunning = true;

                EditorApplication.update += DrainQueue;

                // Run the HTTP listener loop on a background thread
                var token = _cts.Token;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ListenLoop(token);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[Unity Eli] MCP listen loop failed: {e.Message}");
                    }
                });

                Debug.Log($"[Unity Eli] MCP server started on port {Port}.");
            }
        }

        public static void Stop()
        {
            lock (_lock)
            {
                if (!IsRunning) return;

                EditorApplication.update -= DrainQueue;

                _cts?.Cancel();
                _cts = null;

                try { _listener?.Stop(); } catch { }
                try { _listener?.Close(); } catch { }
                _listener = null;

                IsRunning = false;
                Debug.Log("[Unity Eli] MCP server stopped.");
            }
        }

        // ── Background listener loop ──────────────────────────────────────────

        private static async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch
                {
                    // Listener was stopped
                    break;
                }

                // Handle each request on a thread-pool thread so we don't block the loop
                _ = Task.Run(() => HandleRequest(ctx), token);
            }
        }

        private static async Task HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;

            try
            {
                // Allow cross-origin requests from Claude Code
                res.Headers.Add("Access-Control-Allow-Origin", "*");
                res.Headers.Add("Access-Control-Allow-Methods", "GET, POST, DELETE, OPTIONS");
                res.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Accept");

                Debug.Log($"[Unity Eli] MCP {req.HttpMethod} {req.Url}");

                if (req.HttpMethod == "OPTIONS")
                {
                    res.StatusCode = 204;
                    res.Close();
                    return;
                }

                if (req.HttpMethod == "GET")
                {
                    // Streamable HTTP: GET is for server-initiated events (SSE stream).
                    // Return empty SSE stream that stays open briefly for auto-detection,
                    // or just return 200 with server info for non-SSE clients.
                    var accept = req.Headers["Accept"] ?? "";
                    if (accept.Contains("text/event-stream"))
                    {
                        // SSE auto-detection probe — return valid SSE with no events
                        res.ContentType = "text/event-stream";
                        res.Headers.Add("Cache-Control", "no-cache");
                        res.StatusCode = 200;
                        res.Close();
                    }
                    else
                    {
                        // Simple GET probe — return server info
                        var info = "{\"name\":\"unity-eli\",\"version\":\"2.0\",\"protocol\":\"mcp\"}";
                        var infoBytes = Encoding.UTF8.GetBytes(info);
                        res.ContentType = "application/json";
                        res.ContentLength64 = infoBytes.Length;
                        await res.OutputStream.WriteAsync(infoBytes, 0, infoBytes.Length);
                    }
                    return;
                }

                if (req.HttpMethod == "DELETE")
                {
                    // Streamable HTTP session cleanup — acknowledge
                    res.StatusCode = 200;
                    res.Close();
                    return;
                }

                if (req.HttpMethod != "POST")
                {
                    res.StatusCode = 405;
                    res.Close();
                    return;
                }

                string body;
                using (var reader = new StreamReader(req.InputStream, req.ContentEncoding))
                    body = await reader.ReadToEndAsync();

                Debug.Log($"[Unity Eli] MCP request: {(body.Length > 200 ? body.Substring(0, 200) + "..." : body)}");

                var responseJson = await DispatchRequest(body);

                var bytes = Encoding.UTF8.GetBytes(responseJson);
                res.ContentType = "application/json";
                res.ContentLength64 = bytes.Length;
                await res.OutputStream.WriteAsync(bytes, 0, bytes.Length);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Unity Eli] MCP request error: {e.Message}");
                try
                {
                    res.StatusCode = 500;
                }
                catch { }
            }
            finally
            {
                try { res.Close(); } catch { }
            }
        }

        private static async Task<string> DispatchRequest(string body)
        {
            // Handle JSON-RPC batch (array) or single request
            body = body.Trim();
            if (body.StartsWith("["))
            {
                // Batch: process each and return array
                var items = JsonHelper.ParseArray(body);
                var responses = new System.Text.StringBuilder("[");
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) responses.Append(",");
                    responses.Append(await HandleSingleRequest(items[i]));
                }
                responses.Append("]");
                return responses.ToString();
            }

            return await HandleSingleRequest(body);
        }

        private static async Task<string> HandleSingleRequest(string body)
        {
            var method = JsonHelper.ExtractString(body, "method");
            // id can be number or string in JSON-RPC; use ExtractInt which handles both bare numbers
            // and falls back for strings
            var id = JsonHelper.ExtractInt(body, "id");
            if (id == 0)
            {
                // Fallback: try string extraction for string-typed ids like "id":"1"
                var idStr = JsonHelper.ExtractString(body, "id");
                if (idStr != null) int.TryParse(idStr, out id);
            }

            switch (method)
            {
                case "initialize":
                case "notifications/initialized":
                    return MakeInitializeResponse(id);

                case "tools/list":
                    return MakeToolsListResponse(id);

                case "tools/call":
                    var paramsJson = JsonHelper.ExtractObject(body, "params");
                    return await HandleToolsCall(paramsJson, id);

                case "ping":
                    return MakeSuccessResponse(id, "{}");

                default:
                    // Notifications and unknown methods: return null (no response for notifications)
                    if (body.Contains("\"id\""))
                        return MakeErrorResponse(id, -32601, $"Method not found: {method}");
                    return ""; // notification, no response
            }
        }

        private static string MakeInitializeResponse(int id)
        {
            var result =
                "{" +
                "\"protocolVersion\":\"2024-11-05\"," +
                "\"capabilities\":{\"tools\":{}}," +
                "\"serverInfo\":{\"name\":\"unity-eli\",\"version\":\"2.0\"}" +
                "}";
            return MakeSuccessResponse(id, result);
        }

        private static string MakeToolsListResponse(int id)
        {
            var defs = ToolRegistry.GetToolDefinitions();
            var sb = new StringBuilder();
            sb.Append("{\"tools\":[");

            bool first = true;
            foreach (var def in defs)
            {
                if (!first) sb.Append(",");
                first = false;
                sb.Append(BuildMcpToolJson(def));
            }

            sb.Append("]}");
            return MakeSuccessResponse(id, sb.ToString());
        }

        /// <summary>
        /// Converts a ToolDefinition (Anthropic .tool.json format) to MCP tool format.
        /// The only structural difference is the property name:
        ///   Anthropic: "input_schema"
        ///   MCP:       "inputSchema"
        /// </summary>
        private static string BuildMcpToolJson(ToolDefinition def)
        {
            // Replace the key in the raw JSON. The raw JSON already has the full schema.
            return def.RawJson.Replace("\"input_schema\"", "\"inputSchema\"");
        }

        private static async Task<string> HandleToolsCall(string paramsJson, int id)
        {
            var toolName = JsonHelper.ExtractString(paramsJson, "name");
            var argumentsJson = JsonHelper.ExtractObject(paramsJson, "arguments");

            if (string.IsNullOrEmpty(toolName))
                return MakeErrorResponse(id, -32602, "Missing tool name in tools/call params.");

            // Dispatch to main thread and wait for result
            var tcs = new TaskCompletionSource<string>();
            var pending = new PendingToolCall
            {
                ToolName = toolName,
                ArgumentsJson = argumentsJson,
                ResultTcs = tcs
            };
            _queue.Enqueue(pending);

            var result = await tcs.Task;
            var isError = result.StartsWith("ERROR:");

            // Build MCP tools/call response
            var escapedResult = JsonHelper.EscapeJson(result);
            var resultJson =
                "{" +
                $"\"content\":[{{\"type\":\"text\",\"text\":\"{escapedResult}\"}}]," +
                $"\"isError\":{(isError ? "true" : "false")}" +
                "}";

            return MakeSuccessResponse(id, resultJson);
        }

        // ── Main thread queue drain ───────────────────────────────────────────

        private static void DrainQueue()
        {
            bool anyRefresh = false;

            while (_queue.TryDequeue(out var pending))
            {
                string result;
                bool needsRefresh;

                try
                {
                    result = ToolRegistry.ExecuteTool(pending.ToolName, pending.ArgumentsJson, out needsRefresh);
                    anyRefresh = anyRefresh || needsRefresh;
                }
                catch (Exception e)
                {
                    result = ToolResult.Error($"Tool execution failed: {e.Message}");
                    needsRefresh = false;
                }

                // Safety net: never let a null/empty result through
                if (string.IsNullOrEmpty(result))
                    result = ToolResult.Success($"Tool '{pending.ToolName}' completed.");

                var isError = result.StartsWith("ERROR:");
                ToolExecuted?.Invoke(pending.ToolName, result, isError);

                pending.ResultTcs.TrySetResult(result);
            }

            if (anyRefresh)
            {
                // Defer refresh so all tool results are sent back to Claude Code first
                EditorApplication.delayCall += () =>
                {
                    AssetDatabase.Refresh();
                };
            }
        }

        // ── JSON-RPC helpers ─────────────────────────────────────────────────

        private static string MakeSuccessResponse(int id, string resultJson)
        {
            return $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"result\":{resultJson}}}";
        }

        private static string MakeErrorResponse(int id, int code, string message)
        {
            var escaped = JsonHelper.EscapeJson(message);
            return $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"error\":{{\"code\":{code},\"message\":\"{escaped}\"}}}}";
        }
    }

    internal class PendingToolCall
    {
        public string ToolName;
        public string ArgumentsJson;
        public TaskCompletionSource<string> ResultTcs;
    }
}
