# Changelog

All notable changes to this package will be documented in this file.

The format is based on Keep a Changelog and this project adheres to Semantic Versioning.

## [1.1.0] - 2026-03-23

### Added
- **Button support** in `create_ui_element` tool — creates Image + Button component + child Text in one step.
- **RectTransform channels** in `set_transform` tool — new `anchors`, `pivot`, `anchored_position`, and `size_delta` channels with `w` parameter for anchor max Y.
- **Empty scene setup** in `manage_scene` tool — new `setup` parameter (`empty`/`default`) for the `new` action.
- `JsonHelper.ExtractFloat` utility for extracting float values from raw JSON with correct defaults.

### Fixed
- **Claude CLI not found** — new `ResolveClaudePath()` probes common npm global install locations and falls back to `where`/`which`, fixing Unity not inheriting the full user PATH.
- **Tool definitions not loading** when installed as a Git URL package — `ToolRegistry` now uses `PackageInfo.FindForAssembly` to locate `.tool.json` files regardless of install method.
- **MCP server registration** — uses `--scope local` for removal, gracefully handles "already exists", and falls back to `.mcp.json` for the current port.
- **UI anchor/pivot/size defaults** — `CreateUIElementTool` now uses `JsonHelper.ExtractFloat` instead of `JsonUtility` zero-default detection, fixing incorrect anchor values when fields are omitted.
- `set_component_property` error message now lists available RectTransform channels when redirecting to `set_transform`.

### Changed
- Chat input now uses **Ctrl+Enter to send** (was Enter). Plain Enter inserts a newline, matching typical multi-line editor behavior.
- Screenshot tool description clarifies `game` source is preferred for UI work.

## [1.0.1] - 2026-03-23

### Fixed
- Setup wizard crash when saving `.eli-instructions` if `Assets/UnityEli/` directory doesn't exist.
- Send and copy button icons not loading when installed as a package via Git URL.

## [1.0.0] - 2026-03-20
- Initial public package structure for Unity Package Manager (Git URL import).
- Editor-only Unity Eli assistant with Claude Code CLI integration.
