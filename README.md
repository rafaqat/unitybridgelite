# Unity Bridge Lite

A lightweight TCP socket bridge for controlling Unity Editor from external tools like Claude Code, Python scripts, or any TCP client.

## Features

- **TCP Socket Communication** - Fast, low-latency protocol (no HTTP overhead)
- **Auto-start** - Server starts automatically when Unity Editor loads
- **Main Thread Execution** - Commands safely execute on Unity's main thread
- **Extensible** - Add custom commands via `[BridgeCommand]` attribute
- **Connection Pooling** - Python client reuses connections for speed

## Installation

### Unity Package

1. Open your Unity project
2. Go to **Window > Package Manager**
3. Click **+** > **Add package from git URL**
4. Enter:
   ```
   https://github.com/rafaqat/unitybridgelite.git?path=UnityPackage
   ```

Or add to your `Packages/manifest.json`:
```json
{
  "dependencies": {
    "com.local.unity-bridge-lite": "https://github.com/rafaqat/unitybridgelite.git?path=UnityPackage"
  }
}
```

### Python Client (Claude Code Skill)

Copy the `Skills/unity` folder to your Claude Code skills directory:
```bash
cp -r Skills/unity ~/.claude/skills/
```

## Usage

### Python Client

```python
from unity_client import send_command

# Ping the server
result = send_command('ping')

# Create a cube
send_command('execute_menu', {'path': 'GameObject/3D Object/Cube'})

# Change material color
send_command('set_material_color', {'name': 'Cube', 'color': '0000FF'})

# Start rotation (45 deg/sec on Y axis)
send_command('start_rotation', {'name': 'Cube', 'y': 45})

# Start orbiting around origin
send_command('start_orbit', {'name': 'Cube', 'radius': 3, 'speed': 60})
```

### CLI Usage

```bash
python unity_client.py ping
python unity_client.py hierarchy --pretty
python unity_client.py color name=Cube color=FF0000
python unity_client.py menu path="GameObject/3D Object/Sphere"
```

### Raw TCP (any language)

Connect to `127.0.0.1:6400` and send newline-delimited JSON:

```
{"type":"ping"}\n
```

Response:
```
{"status":"success","result":{"message":"pong"}}\n
```

## Available Commands

| Command | Parameters | Description |
|---------|------------|-------------|
| `ping` | - | Health check |
| `list_commands` | - | List all available commands |
| `get_scene_info` | - | Get active scene info |
| `get_hierarchy` | - | Get scene hierarchy |
| `get_selection` | - | Get selected GameObjects |
| `create_gameobject` | `name`, `parent` | Create empty GameObject |
| `execute_menu` | `path` | Execute Unity menu item |
| `set_play_mode` | `play` (bool) | Enter/exit play mode |
| `set_material_color` | `name`, `color` (hex) | Set object color |
| `select_gameobject` | `name` | Select a GameObject |
| `start_rotation` | `name`, `x`, `y`, `z` | Start continuous rotation (deg/sec) |
| `stop_rotation` | `name` or `all` | Stop rotation |
| `start_orbit` | `name`, `radius`, `speed`, `center_x/y/z` | Orbit around point |
| `stop_orbit` | `name` or `all` | Stop orbiting |

## Adding Custom Commands

Create a new class with the `[BridgeCommand]` attribute:

```csharp
using UnityBridgeLite;
using System.Collections.Generic;

[BridgeCommand("my_command")]
public class MyCommand
{
    public static object HandleCommand(Dictionary<string, object> p)
    {
        var param = p.TryGetValue("param", out var v) ? v.ToString() : "default";

        // Do something in Unity...

        return new { success = true, message = "Done!" };
    }
}
```

## Protocol

- **Transport**: TCP socket on port 6400 (configurable)
- **Format**: Newline-delimited JSON (`\n` terminated)
- **Request**: `{"type": "command_name", "params": {...}}\n`
- **Response**: `{"status": "success|error", "result": {...}}\n`

## Status File

The server writes a status file to `~/.unity-bridge/bridge-{hash}.json`:

```json
{
  "port": 6400,
  "protocol": "tcp",
  "project_name": "MyProject",
  "project_path": "/path/to/Assets",
  "unity_version": "2022.3.x",
  "last_heartbeat": "2025-12-23T12:00:00Z"
}
```

The Python client uses this to auto-discover the port.

## Requirements

- Unity 2021.3+ (tested on 2022.3)
- Python 3.7+ (for client)

## License

MIT License - feel free to use and modify.
