---
name: unity
description: >
  Control Unity Editor via TCP socket connection using Unity Bridge Lite.
  Create/modify GameObjects, manage packages, control animations, and more.
  Use when user asks to interact with Unity Editor. Requires Unity Bridge Lite
  package installed (auto-starts when Unity loads).
---

# Unity Bridge Lite Control

Fast TCP socket communication with Unity Editor - minimal latency, no HTTP overhead.

## Prerequisites

- Unity Editor running with **Unity Bridge Lite** package installed
- Bridge auto-starts on port 6400 when Unity loads

Install via Package Manager:
```
https://github.com/rafaqat/unitybridgelite.git?path=UnityPackage
```

## Quick Commands

```bash
# Ping Unity
python3 <skill-path>/scripts/unity_client.py ping

# Get scene hierarchy
python3 <skill-path>/scripts/unity_client.py hierarchy --pretty

# Create a cube
python3 <skill-path>/scripts/unity_client.py menu path="GameObject/3D Object/Cube"

# Change object color
python3 <skill-path>/scripts/unity_client.py color name=Cube color=FF0000
```

---

## Available Commands

### Basic Commands

| Command | Description |
|---------|-------------|
| `ping` | Health check |
| `list_commands` | List all commands |
| `hierarchy` | Get scene hierarchy |
| `scene` | Get scene info |
| `selection` | Get selected objects |

### GameObject Commands

**Create via menu:**
```bash
python3 <skill-path>/scripts/unity_client.py menu path="GameObject/3D Object/Cube"
python3 <skill-path>/scripts/unity_client.py menu path="GameObject/3D Object/Sphere"
python3 <skill-path>/scripts/unity_client.py menu path="GameObject/Light/Point Light"
```

**Create empty GameObject:**
```bash
python3 <skill-path>/scripts/unity_client.py create name=MyObject
python3 <skill-path>/scripts/unity_client.py create name=Child parent=MyObject
```

**Select object:**
```bash
python3 <skill-path>/scripts/unity_client.py select name=Cube
```

**Set material color (hex):**
```bash
python3 <skill-path>/scripts/unity_client.py color name=Cube color=FF0000
python3 <skill-path>/scripts/unity_client.py color name=Sphere color=00FF00
```

---

### Animation Commands

**Start rotation (degrees/second):**
```bash
# Rotate on Y axis at 45 deg/sec
python3 <skill-path>/scripts/unity_client.py rotate name=Cube y=45

# Rotate on multiple axes
python3 <skill-path>/scripts/unity_client.py rotate name=Cube x=10 y=45 z=5
```

**Stop rotation:**
```bash
python3 <skill-path>/scripts/unity_client.py stop_rotation name=Cube
python3 <skill-path>/scripts/unity_client.py stop_rotation all=true
```

**Start orbit (circle around point):**
```bash
# Orbit around origin, radius 3, speed 60 deg/sec
python3 <skill-path>/scripts/unity_client.py orbit name=Cube radius=3 speed=60

# Orbit around custom center
python3 <skill-path>/scripts/unity_client.py orbit name=Cube radius=5 speed=30 center_x=0 center_y=1 center_z=0
```

**Stop orbit:**
```bash
python3 <skill-path>/scripts/unity_client.py stop_orbit name=Cube
python3 <skill-path>/scripts/unity_client.py stop_orbit all=true
```

---

### Package Management

**Install package:**
```bash
# By package name
python3 <skill-path>/scripts/unity_client.py install package=com.unity.xr.arkit

# From git URL
python3 <skill-path>/scripts/unity_client.py install package="https://github.com/user/repo.git"
```

**List installed packages:**
```bash
python3 <skill-path>/scripts/unity_client.py packages --pretty
```

---

### Settings & Editor Control

**Open Project Settings:**
```bash
# Open XR settings
python3 <skill-path>/scripts/unity_client.py settings path="Project/XR Plug-in Management"

# Open Player settings
python3 <skill-path>/scripts/unity_client.py settings path="Project/Player"

# Open Quality settings
python3 <skill-path>/scripts/unity_client.py settings path="Project/Quality"
```

**Play mode:**
```bash
python3 <skill-path>/scripts/unity_client.py play play=true   # Enter play mode
python3 <skill-path>/scripts/unity_client.py play play=false  # Exit play mode
```

---

## Python API Usage

```python
import sys, os
sys.path.append(os.path.expanduser('~/.claude/skills/unity/scripts'))
from unity_client import send_command

# Ping
result = send_command('ping')

# Create cube and color it
send_command('execute_menu', {'path': 'GameObject/3D Object/Cube'})
send_command('set_material_color', {'name': 'Cube', 'color': '0000FF'})

# Animate
send_command('start_rotation', {'name': 'Cube', 'y': 45})
send_command('start_orbit', {'name': 'Cube', 'radius': 3, 'speed': 60})

# Install package
send_command('install_package', {'package': 'com.unity.xr.arkit'})

# Open settings
send_command('open_settings', {'path': 'Project/XR Plug-in Management'})
```

---

## Response Format

All commands return JSON:

```json
{
  "status": "success",
  "result": { ... }
}
```

Or on error:

```json
{
  "status": "error",
  "error": "Error description"
}
```

---

## Troubleshooting

**"No Unity port files found"**
- Unity must be running with Bridge Lite installed
- Check `~/.unity-bridge/` for status files

**"Connection refused"**
- Unity may be recompiling - wait and retry
- Restart Unity if bridge is stuck

**"Unknown command"**
- Unity needs to recompile after code changes
- Click on Unity window to trigger recompile
