# UnityEli

AI-powered Unity editor assistant that uses **Claude Code CLI** as its backend. No Anthropic API key required — it uses your Claude Code subscription. This is a **standalone Unity package** — it must work in any Unity project without external dependencies.

## Prerequisites

- Claude Code CLI installed: `npm install -g @anthropic-ai/claude-code`
- Logged in: `claude auth login`
- Claude Code available in PATH

## Architecture

```
UnityEliWindow (in-editor chat UI)
  ├── McpServer.cs         — local HTTP server (MCP protocol) exposing IEliTools to Claude Code
  └── ClaudeCodeProcess.cs — spawns `claude` subprocess, streams JSON events back to UI
```

All code lives under `Editor/` (editor-only, not included in builds):

- `UnityEliWindow.cs` — Main chat window (`Window > Unity Eli`). Handles UI, subscribes to subprocess events. Also manages auto-initialization on first open.
- `McpServer.cs` — HTTP listener implementing MCP JSON-RPC 2.0. Receives tool calls from Claude Code and dispatches them to `IEliTool` implementations on the Unity main thread.
- `ClaudeCodeProcess.cs` — Manages the `claude` subprocess per conversation turn. Writes prompt via stdin, reads `--output-format stream-json` events from stdout, fires `OnStreamEvent` / `OnComplete` callbacks.
- `SimpleSessionState.cs` — Persists display messages and Claude Code `session_id` across domain reloads using `SessionState`. Defines `DisplayMessage`.
- `EliToolHelpers.cs` — Shared static helpers used by tools: `FindGameObject`, `ResolveType`, `ParseVector`, `TrySetPropertyValue`, `ReadPropertyValue`, `FindProperty`, `ListProperties`. All new tools should use these instead of duplicating logic.
- `JsonHelper.cs` — Shared JSON parsing/building utilities (no external dependencies).
- `UnityEliSettings.cs` — Settings UI (`Preferences > Unity Eli`) and `EditorPrefs` storage for MCP base port and optional model override.
- `Tools/` — 51 MCP tools for Unity editor operations that require UnityEditor/UnityEngine APIs. File I/O (reading, writing, editing, searching files) is handled by Claude Code's native tools (Read, Write, Edit, Glob, Grep) which are pre-allowed via the `--allowedTools` flag.
- `MeshyApiClient.cs` — HTTP client for Meshy.ai text-to-3D API (v2). Used by the three Meshy tools.

## How a conversation turn works

1. User types in `UnityEliWindow` and hits Send.
2. `McpServer` starts (if not running) on an auto-selected port in range `47880–47889`.
3. `ClaudeCodeProcess` spawns: `claude -p --output-format stream-json --verbose --allowedTools "mcp__unity__*,Read,Write,Edit,Glob,Grep" [--model <id>] [--resume <session_id>]`
4. User prompt is written to subprocess stdin; stdin is closed so Claude reads to EOF.
5. Claude Code connects to the MCP server, calls `tools/list` to discover Unity tools, then calls tools as needed.
6. `McpServer` receives `tools/call` requests, queues them for the Unity main thread, executes `IEliTool.Execute()`, returns results to Claude Code.
7. Stream-json events flow back through stdout → `ClaudeCodeProcess` → `UnityEliWindow` for display.
8. The `session_id` is saved to `SessionState`; the next message uses `--resume` to continue the conversation.

Stderr is drained concurrently on a background thread to prevent pipe-buffer deadlock. Any stderr output is surfaced as the error message if the process exits with a non-zero code.

## Auto-initialization / Onboarding

When the window opens with no prior session and no chat history, `UnityEliWindow.AutoInitialize()` fires via `EditorApplication.delayCall`. It checks for `Assets/README.md`:

- **No README**: sends a silent internal prompt instructing Claude to introduce itself and ask the developer about their game (type, 2D/3D, platforms, mechanics, features) before creating `README.md` in the Assets folder.
- **README exists**: sends a silent internal prompt instructing Claude to read the README, introduce itself briefly, and ask what to work on first.

Internal prompts are not shown as user messages in the chat — only Claude's response appears.

## Tool System

Each tool consists of:
- A `.tool.json` file (Anthropic tool schema — served to Claude Code via MCP `tools/list`)
- A `.cs` file implementing `IEliTool` with `Name`, `NeedsAssetRefresh`, and `Execute(string inputJson)`
- Auto-discovered via reflection in `ToolRegistry.cs`; results wrapped in `ToolResult.cs`

When adding new tools, create a subfolder in `Tools/` with both `.tool.json` and `.cs` files. Auto-discovered — no changes to `ToolRegistry` required.

The `input_schema` key in `.tool.json` is automatically translated to `inputSchema` when served over MCP.

MCP tools cover: inspection, asset management, scene management, tags/layers, GameObject lifecycle, components, transforms, asset properties, UI, rendering, project settings (player/quality/physics/time), prefab management, animator controllers, asset refresh/compilation, build settings, event wiring, screenshots, play mode control, and Meshy.ai 3D model generation. File/script reading, writing, editing, and searching are delegated to Claude Code's native file tools — after creating or editing scripts, Claude Code should call `refresh_assets` (with `wait_for_compilation=true`) to trigger Unity recompilation.

## Meshy.ai Integration

Five tools provide AI-powered 3D model generation and processing via Meshy.ai:

1. `meshy_generate_model` — Creates a preview (mesh) or refine (texturing) task. Costs credits.
2. `meshy_check_task` — Polls task status (text-to-3D, remesh, or retexture). Returns next-step guidance based on stage.
3. `meshy_download_model` — Downloads completed model + textures to `Assets/Meshes/Generated/`.
4. `meshy_remesh` — Remeshes a model to optimize topology and polycount (5 credits).
5. `meshy_retexture` — Applies new textures to existing geometry via text prompt or reference image (10 credits). Works on any completed Meshy task or external model URL.

**Workflow:** generate (preview) → poll → generate (refine) → poll → download. Optionally remesh and/or retexture after download.

Two material tools support the post-download workflow:
- `create_material` — Creates a Material asset with a specified shader (defaults to URP/Lit).
- `set_material_property` — Sets shader properties (textures, colors, floats) on a Material.

Requires a Meshy API key configured in `Preferences > Unity Eli`. The generate tool's description instructs Claude to ask user confirmation before using it unprompted (credit cost protection). Meshy tools are hidden from Claude when no API key is configured.

## Domain reload behaviour

`AssemblyReloadEvents.beforeAssemblyReload` stops `McpServer` and `ClaudeCodeProcess`. The Claude Code session is persisted by the CLI. On the next `OnEnable`, both restart and `session_id` is used to resume.

## Thread safety

MCP HTTP requests arrive on a background thread. Tool execution always happens on the Unity main thread via a `ConcurrentQueue<PendingToolCall>` drained by `EditorApplication.update`.

## Key Rules

- **No dependencies on game code.** This package must be self-contained and portable.
- All code goes under `Editor/` (uses `UnityEditor` namespace).
- Settings stored via `EditorPrefs` — never serialized to assets or committed.
- Minimum Unity version: 2021.3 (per `package.json`).
- When adding new tools: create a subfolder in `Tools/` with both `.tool.json` and `.cs` files.
- Do **not** add a lazy-activation system — Claude Code handles tool selection via `tools/list` automatically.

## Project Instructions

Users can create a `.eli-instructions` file with project-specific rules and conventions (naming, coding standards, restrictions). When present, its contents are appended to Claude's system prompt via `--append-system-prompt-file`. The default location is `Assets/UnityEli/.eli-instructions` so it lives with the package and is cleaned up on uninstall. The file path is configurable in `Preferences > Unity Eli` (stored in `EditorPrefs`). If the file does not exist, the feature is silently skipped.
