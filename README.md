# Unity Eli

An AI assistant embedded directly in the Unity Editor. Unity Eli uses **Claude Code CLI** as its backend. It can take real actions in your project: creating scripts, modifying scenes, adjusting components, reading console logs, and more — all through natural language.

## Prerequisites

- [Claude Code CLI](https://docs.anthropic.com/en/docs/claude-code) installed: `npm install -g @anthropic-ai/claude-code`
- Authenticated: `claude auth login`
- `claude` available in your system PATH

## Setup

Install with Unity Package Manager from your Git URL:

1. In Unity, open **Window > Package Manager**
2. Click **+** and choose **Add package from git URL...**
3. Enter your repository URL (for example: `https://github.com/frenchtoastfella/unity-eli`)
4. Open **Window > Unity Eli**
5. Unity Eli will auto-initialize — if there's no `README.md` in your Assets folder it will ask you a few questions to create one

To verify Claude Code is installed, open **Preferences > Unity Eli** and click **Check Installation**.

## Usage

Type natural language requests in the chat window and press **Ctrl+Enter** (or the Send button). Examples:

- *"Create a player controller script with WASD movement and jumping"*
- *"Add a Rigidbody to the Player and disable gravity"*
- *"There are compilation errors in the console — can you fix them?"*
- *"Set the camera at 0,10,0 and make it look at the Player"*
- *"Set the camera background to dark grey"*
- *"Show me the scene hierarchy with all components"*
- *"Create a ScriptableObject for enemy stats with health and speed fields"*

Claude will use whatever tools are needed, execute them, and respond with a summary.

### First Run

When you open the window for the first time (no existing session):

- **No `Assets/README.md`**: Eli introduces itself and asks about your game — type, 2D/3D, platforms, core mechanics, planned features. Once you answer, it creates `README.md` for you.
- **`Assets/README.md` exists**: Eli reads it, introduces itself, and asks what to work on first.

## Available Tools (47)

### Inspection & Query
| Tool | Description |
|---|---|
| `get_hierarchy` | Get the scene hierarchy tree with optional component listing |
| `get_component_property` | Read serialized property values from a component on a scene GameObject |
| `get_console_logs` | Read errors, warnings, and logs from the Unity console |
| `get_selection` | Get info about the currently selected object(s) in the Editor |
| `get_asset_property` | Read serialized properties from a project asset |
| `find_file` | Search for files in the project by name or partial name |
| `capture_screenshot` | Capture a screenshot from the Scene View camera or a game camera |

### File & Script Management
| Tool | Description |
|---|---|
| `read_file` | Read the contents of a project file |
| `create_script` | Create a new C# script |
| `edit_script` | Overwrite an existing C# script with new content |
| `refresh_assets` | Trigger AssetDatabase.Refresh() to reimport assets and compile scripts |

### Asset & Folder Management
| Tool | Description |
|---|---|
| `create_folder` | Create a folder (and any missing parent folders) in the project |
| `move_asset` | Move or rename any asset or folder |
| `delete_asset` | Delete a project asset (moved to OS trash, recoverable) |
| `create_prefab` | Create a prefab asset from a scene GameObject |
| `manage_prefab` | Instantiate, open/exit Prefab Mode, apply or revert prefab overrides |
| `create_scriptable_object` | Create a ScriptableObject asset from a compiled type |

### Scene Management
| Tool | Description |
|---|---|
| `manage_scene` | Open, create, save, close, or additively load scenes |

### Tags & Layers
| Tool | Description |
|---|---|
| `add_tag_or_layer` | Add a tag or layer and optionally assign to a GameObject |
| `remove_tag_or_layer` | Remove a custom tag or layer |

### GameObject Hierarchy
| Tool | Description |
|---|---|
| `create_gameobject` | Create a GameObject (empty or primitive) with transform and optional parent |
| `delete_game_object` | Delete a GameObject and its children |
| `reparent_game_object` | Change the parent of a GameObject (or move to scene root) |
| `reorder_game_object` | Change a GameObject's position in the hierarchy (sibling index) |
| `set_hierarchy` | Create or update a GameObject hierarchy from a text description |

### Component Management
| Tool | Description |
|---|---|
| `add_component` | Add a component to a GameObject |
| `remove_component` | Remove a component from a GameObject |
| `reorder_component` | Move a component up or down in the Inspector stack |
| `set_component_property` | Set any serialized property (bool, int, float, enum, Vector3, Color, AnimationCurve, Gradient, etc.) |
| `set_component_reference` | Set an object reference field (like dragging in the Inspector) |

### Transforms
| Tool | Description |
|---|---|
| `set_transform` | Set position, rotation, or scale on one or more GameObjects (absolute or relative) |
| `look_at_game_object` | Make a GameObject face another GameObject |

### Asset Properties
| Tool | Description |
|---|---|
| `set_asset_property` | Set a serialized property on any project asset |

### UI
| Tool | Description |
|---|---|
| `create_canvas` | Create a Canvas with CanvasScaler and EventSystem |
| `create_ui_element` | Create UI elements (TextMeshPro, Button, Image, Panel, etc.) |

### Events
| Tool | Description |
|---|---|
| `add_event_listener` | Wire a persistent UnityEvent listener (e.g. Button.onClick) to a target method |

### Rendering
| Tool | Description |
|---|---|
| `set_renderer_color` | Set a renderer's material color (creates a URP flat-color material) |
| `set_skybox` | Set the scene skybox material |

### Project Settings
| Tool | Description |
|---|---|
| `set_player_settings` | Set PlayerSettings (company name, product name, version, bundle ID, orientation) |
| `set_quality_settings` | Get quality levels or set the active quality level |
| `set_physics_settings` | Set gravity, layer collision matrix, bounce/sleep thresholds |
| `set_time_settings` | Set fixed timestep, time scale, max timestep |

### Build
| Tool | Description |
|---|---|
| `manage_build_scenes` | Manage the scene list in Build Settings (add, remove, enable/disable, reorder) |
| `manage_build_settings` | Switch platform, toggle development build, read current build config |

### Animator Controller
| Tool | Description |
|---|---|
| `create_animator_controller` | Create an AnimatorController asset with initial states and parameters |
| `configure_animator_controller` | Add/remove states, parameters, and transitions on an existing controller |

### Play Mode
| Tool | Description |
|---|---|
| `play_mode` | Enter, exit, pause, or check Play mode status |

## Architecture

```
Packages/com.frenchtoastfella.unityeli/Editor/
├── UnityEliWindow.cs       # EditorWindow chat UI + auto-initialization
├── McpServer.cs            # Local HTTP/MCP server (JSON-RPC 2.0)
├── ClaudeCodeProcess.cs    # claude subprocess management + stream-json parsing
├── SimpleSessionState.cs   # Session persistence across domain reloads
├── JsonHelper.cs           # Lightweight JSON utilities (no external deps)
├── EliToolHelpers.cs       # Shared helpers: FindGameObject, ResolveType, TrySetPropertyValue
├── UnityEliSettings.cs     # Project Settings page
└── Tools/
    ├── IEliTool.cs         # Tool interface
    ├── ToolRegistry.cs     # Reflection-based tool discovery and execution
    ├── ToolResult.cs       # Success/error result helpers
    └── <ToolName>/
        ├── <ToolName>Tool.cs      # C# implementation
        └── <tool_name>.tool.json  # JSON schema served to Claude via MCP
```

### How It Works

Unity Eli starts a local HTTP server (MCP protocol) that exposes 47 editor tools. When you send a message, it spawns `claude -p` as a subprocess with `--mcp-config` pointing at the local server. Claude Code connects to the server, discovers available tools via `tools/list`, calls them as needed, and streams its response back. The `session_id` is persisted across messages and domain reloads so conversations continue naturally.

### Adding Custom Tools

1. Create a folder: `Packages/com.frenchtoastfella.unityeli/Editor/Tools/YourToolName/`
2. Add `your_tool_name.tool.json`:
   ```json
   {
     "name": "your_tool_name",
     "description": "What this tool does.",
     "input_schema": {
       "type": "object",
       "properties": {
         "param_name": { "type": "string", "description": "..." }
       },
       "required": ["param_name"]
     }
   }
   ```
3. Add `YourToolNameTool.cs`:
   ```csharp
   using System;
   using UnityEngine;

   namespace UnityEli.Editor.Tools
   {
       public class YourToolNameTool : IEliTool
       {
           public string Name => "your_tool_name";
           public bool NeedsAssetRefresh => false;

           public string Execute(string inputJson)
           {
               var input = JsonUtility.FromJson<Input>(inputJson);
               // Do work here
               return ToolResult.Success("Done.");
           }

           [Serializable]
           private class Input { public string param_name; }
       }
   }
   ```
4. Tools are auto-discovered on the next domain reload — no registration required.
5. Set `NeedsAssetRefresh => true` if the tool writes files that need `AssetDatabase.Refresh()`.

## Settings

Open **Preferences > Unity Eli**:

- **Check Installation** — verifies `claude` is available in PATH
- **Open Claude Login** — opens a terminal and runs `claude auth login`
- **Base Port** — the starting port for the local MCP server (default: 47880, tries up to 47889)
- **Model ID** — optional override (e.g. `claude-opus-4-6`); leave blank to use Claude Code's default

## Known Limitations

- **Script editing is full-file replacement** — `edit_script` replaces the entire file. Claude must rewrite the full script even for small changes.
- **GameObject lookup is by name** — tools find GameObjects by name; if multiple share a name, the first match is used.
- **No visual preview** — Claude cannot see the Game or Scene view; it relies on hierarchy data, component info, and console logs.
- **`manage_scene` operates on the active scene** — scene tools work on currently open/active scenes. Multi-scene workflows are supported via `load_additive`.
- **`configure_animator_controller` is Layer 0 only** — multi-layer Animator setups require manual editing.
- **No ShaderGraph/VFX Graph/Timeline tools** — no public C# API for ShaderGraph/VFX; Timeline tools are deferred. Claude can still inspect these files via `read_file`.
- **Domain reload interruptions** — the in-flight request at the time of a domain reload is lost and will show a retry prompt. The Claude Code session itself is preserved.
- **Editor-only** — Unity Eli runs only in the Unity Editor and is excluded from builds.

## Requirements

- Unity 2021.3+
- Claude Code CLI (`npm install -g @anthropic-ai/claude-code`)
- Internet connection (for Claude Code to reach Anthropic's API)
