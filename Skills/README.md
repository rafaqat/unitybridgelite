# Unity Bridge Lite - Claude Code Skills

This folder contains skills for integrating Unity Bridge Lite with Claude Code.

## Installation

Copy the `unity` folder to your Claude Code skills directory:

```bash
cp -r unity ~/.claude/skills/
```

## Unity Skill

The `unity` skill provides a Python client for communicating with Unity Editor via TCP sockets.

### Files

- `scripts/unity_client.py` - Main client library and CLI tool

### Usage in Claude Code

Once installed, you can use the skill in conversations:

```
/unity ping
/unity hierarchy
/unity color name=Cube color=FF0000
```

### Programmatic Usage

```python
import sys
sys.path.append(os.path.expanduser('~/.claude/skills/unity/scripts'))
from unity_client import send_command

# Send commands to Unity
result = send_command('ping')
result = send_command('set_material_color', {'name': 'Cube', 'color': '00FF00'})
result = send_command('start_rotation', {'name': 'Cube', 'y': 90})
```

### Available Commands

See the main [README](../README.md) for the full list of commands.

## Creating Your Own Skills

You can extend the Unity skill or create new ones:

1. Create a folder in `~/.claude/skills/`
2. Add a `scripts/` subfolder with your Python code
3. Reference it in your Claude Code conversations

### Example Custom Skill

```python
# ~/.claude/skills/my-unity-tools/scripts/spawn_enemies.py
from unity_client import send_command

def spawn_enemies(count=5):
    for i in range(count):
        send_command('execute_menu', {'path': 'GameObject/3D Object/Capsule'})
        # Position, color, etc.
    return {"spawned": count}
```
