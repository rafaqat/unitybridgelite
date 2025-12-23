#!/usr/bin/env python3
"""
Unity Bridge Lite Client - TCP socket communication with Unity Editor.
Uses persistent connections for minimal latency.
"""

import json
import glob
import os
import sys
import socket
import argparse

STATUS_DIRS = [
    os.environ.get('UNITY_MCP_STATUS_DIR', os.path.expanduser('~/.unity-bridge')),
]

# Connection pool for reusing sockets
_socket_pool = {}

def find_unity_port():
    """Discover Unity's TCP port from status files."""
    port_files = []

    for status_dir in STATUS_DIRS:
        patterns = [
            os.path.join(status_dir, 'bridge-*.json'),
        ]
        for pattern in patterns:
            port_files.extend(glob.glob(pattern))

    if not port_files:
        return None, "No Unity port files found. Is Unity running with Bridge Lite?"

    port_files.sort(key=os.path.getmtime, reverse=True)

    for pf in port_files:
        try:
            with open(pf) as f:
                data = json.load(f)
                port = data.get('port')
                if port:
                    return port, None
        except (json.JSONDecodeError, IOError):
            continue

    return None, "Could not read port from Unity status files"

def get_socket(port: int, timeout: int = 30):
    """Get or create a socket connection to Unity."""
    global _socket_pool

    key = f"127.0.0.1:{port}"

    # Try to reuse existing connection
    if key in _socket_pool:
        sock = _socket_pool[key]
        try:
            # Test if socket is still alive
            sock.setblocking(False)
            try:
                data = sock.recv(1, socket.MSG_PEEK)
                if not data:
                    raise ConnectionError("Socket closed")
            except BlockingIOError:
                pass  # No data available, socket still good
            sock.setblocking(True)
            sock.settimeout(timeout)
            return sock
        except:
            try:
                sock.close()
            except:
                pass
            del _socket_pool[key]

    # Create new connection
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    sock.setsockopt(socket.IPPROTO_TCP, socket.TCP_NODELAY, 1)  # Disable Nagle
    sock.settimeout(timeout)
    sock.connect(('127.0.0.1', port))
    _socket_pool[key] = sock
    return sock

def close_connection(port: int = None):
    """Close socket connection(s)."""
    global _socket_pool

    if port:
        key = f"127.0.0.1:{port}"
        if key in _socket_pool:
            try:
                _socket_pool[key].close()
            except:
                pass
            del _socket_pool[key]
    else:
        for sock in _socket_pool.values():
            try:
                sock.close()
            except:
                pass
        _socket_pool.clear()

def send_command(command: str, params: dict = None, timeout: int = 30):
    """Send a command to Unity via TCP socket and return the response."""
    port, error = find_unity_port()
    if error:
        return {"status": "error", "error": error}

    payload = json.dumps({
        "type": command,
        "params": params or {}
    }) + "\n"

    try:
        sock = get_socket(port, timeout)
        sock.sendall(payload.encode('utf-8'))

        # Read response (newline-delimited)
        response = b""
        while True:
            chunk = sock.recv(4096)
            if not chunk:
                raise ConnectionError("Connection closed by server")
            response += chunk
            if b"\n" in response:
                break

        return json.loads(response.decode('utf-8').strip())

    except socket.timeout:
        close_connection(port)
        return {"status": "error", "error": "Connection timeout"}
    except ConnectionRefusedError:
        return {"status": "error", "error": "Cannot connect to Unity. Is Unity running?"}
    except Exception as e:
        close_connection(port)
        return {"status": "error", "error": str(e)}

# Command aliases
COMMANDS = {
    'ping': 'ping',
    'list': 'list_commands',
    'list_commands': 'list_commands',
    'scene': 'get_scene_info',
    'scene_info': 'get_scene_info',
    'get_scene_info': 'get_scene_info',
    'hierarchy': 'get_hierarchy',
    'get_hierarchy': 'get_hierarchy',
    'selection': 'get_selection',
    'get_selection': 'get_selection',
    'create': 'create_gameobject',
    'create_gameobject': 'create_gameobject',
    'menu': 'execute_menu',
    'execute_menu': 'execute_menu',
    'play': 'set_play_mode',
    'set_play_mode': 'set_play_mode',
    'console': 'get_console',
    'get_console': 'get_console',
    'color': 'set_material_color',
    'set_color': 'set_material_color',
    'set_material_color': 'set_material_color',
    'select': 'select_gameobject',
    'select_gameobject': 'select_gameobject',
    # Rotation/animation commands
    'rotate': 'start_rotation',
    'start_rotation': 'start_rotation',
    'stop_rotation': 'stop_rotation',
    'orbit': 'start_orbit',
    'start_orbit': 'start_orbit',
    'stop_orbit': 'stop_orbit',
    # Package management commands
    'install': 'install_package',
    'install_package': 'install_package',
    'packages': 'get_packages',
    'get_packages': 'get_packages',
    # Settings commands
    'settings': 'open_settings',
    'open_settings': 'open_settings',
    # Player settings commands
    'player': 'get_player_settings',
    'get_player_settings': 'get_player_settings',
    'set_player': 'set_player_settings',
    'set_player_settings': 'set_player_settings',
    # Build target commands
    'build_target': 'get_build_target',
    'get_build_target': 'get_build_target',
    'set_build_target': 'set_build_target',
    'switch_platform': 'set_build_target',
    # MultiSet SDK config commands
    'multiset': 'get_multiset_config',
    'get_multiset': 'get_multiset_config',
    'get_multiset_config': 'get_multiset_config',
    'set_multiset': 'set_multiset_config',
    'set_multiset_config': 'set_multiset_config',
    'create_so': 'create_scriptable_object',
    'create_scriptable_object': 'create_scriptable_object',
    # MultiSet SDK verification commands
    'verify_multiset': 'verify_multiset_sdk',
    'verify_multiset_sdk': 'verify_multiset_sdk',
    'import_samples': 'import_multiset_samples',
    'import_multiset_samples': 'import_multiset_samples',
    'check_scene': 'check_multiset_scene',
    'check_multiset_scene': 'check_multiset_scene',
    # GameObject manipulation commands
    'delete': 'delete_gameobject',
    'delete_gameobject': 'delete_gameobject',
    'rename': 'rename_gameobject',
    'rename_gameobject': 'rename_gameobject',
    'transform': 'set_transform',
    'set_transform': 'set_transform',
    'get_transform': 'get_transform',
    'find': 'find_gameobject',
    'find_gameobject': 'find_gameobject',
    'duplicate': 'duplicate_gameobject',
    'duplicate_gameobject': 'duplicate_gameobject',
    'set_parent': 'set_parent',
    'parent': 'set_parent',
}

def main():
    parser = argparse.ArgumentParser(description='Unity Bridge Lite Client (TCP)')
    parser.add_argument('command', nargs='?', default='ping',
                        help='Command to execute')
    parser.add_argument('--params', '-p', type=str, default='{}',
                        help='JSON parameters for the command')
    parser.add_argument('--timeout', '-t', type=int, default=30,
                        help='Timeout in seconds')
    parser.add_argument('--pretty', action='store_true',
                        help='Pretty print JSON output')
    parser.add_argument('args', nargs='*', help='Additional key=value parameters')

    args = parser.parse_args()

    command = COMMANDS.get(args.command, args.command)

    try:
        params = json.loads(args.params)
    except json.JSONDecodeError as e:
        print(json.dumps({"status": "error", "error": f"Invalid JSON params: {e}"}))
        sys.exit(1)

    for arg in args.args:
        if '=' in arg:
            key, value = arg.split('=', 1)
            try:
                params[key] = json.loads(value)
            except json.JSONDecodeError:
                params[key] = value

    result = send_command(command, params, args.timeout)

    if args.pretty:
        print(json.dumps(result, indent=2))
    else:
        print(json.dumps(result))

    sys.exit(0 if result.get('status') == 'success' else 1)

if __name__ == '__main__':
    main()
