# Changelog

All notable changes to this package will be documented in this file.

The format is based on Keep a Changelog and this project adheres to Semantic Versioning.

## [1.2.0] - 2026-03-28

### Added
- **`batch_execute` tool** — execute multiple tool calls in a single MCP round trip, dramatically reducing latency when performing multi-step operations (e.g. create 3 objects and color them in one call instead of 6).
- **Batch/multi-object support** across core tools — `add_component`, `create_gameobject`, `create_prefab`, `delete_game_object`, and `set_renderer_color` now accept comma-separated names or `objects` arrays to operate on multiple GameObjects in one call.
- **Find/replace mode** for `edit_script` — new `find` and `replace` parameters allow targeted text substitution without rewriting the entire file.
- **Asset path references** in `set_component_reference` — new `asset_path` parameter to assign project assets (prefabs, materials, sprites, ScriptableObjects) to serialized fields, not just scene objects.
- **Stop button & Escape key** in the chat window — cancel a running Claude request at any time.
- **Compilation and Play mode guards** on `add_component`, `create_gameobject`, and `play_mode` — tools now return clear error messages when Unity is compiling or in Play mode, preventing silent data loss.
- **Enhanced `play_mode` status** — the `status` action now reports both play mode state and whether scripts are currently compiling.
- `create_script` and `edit_script` now remind Claude to wait for compilation before adding components.

### Changed
- Simplified MCP server registration — removed `claude mcp add/remove` and `--mcp-config` flag approaches; now relies solely on `.mcp.json` auto-discovery, which is more reliable.
- Send button font size reduced (16 → 12) for better fit.

### Removed
- `EnsureMcpServerRegistered()`, `RunClaudeSync()`, and `WriteMcpConfig()` helper methods from `ClaudeCodeProcess` (replaced by `.mcp.json` auto-discovery).

## [1.0.1] - 2026-03-23

### Fixed
- Setup wizard crash when saving `.eli-instructions` if `Assets/UnityEli/` directory doesn't exist.
- Send and copy button icons not loading when installed as a package via Git URL.

## [1.0.0] - 2026-03-20
- Initial public package structure for Unity Package Manager (Git URL import).
- Editor-only Unity Eli assistant with Claude Code CLI integration.
