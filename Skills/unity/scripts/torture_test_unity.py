#!/usr/bin/env python3
"""
Unity Bridge Lite Torture Test
Tests all available commands and stresses the Unity Editor connection.
"""

import sys
import os
import time
import json
import random

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_client import send_command

# Test results tracking
results = {"passed": 0, "failed": 0, "errors": []}

def test(name, command, params=None, expect_success=True):
    """Run a test and track results."""
    print(f"  Testing {name}...", end=" ")
    try:
        result = send_command(command, params or {})
        success = result.get("status") == "success"
        if success == expect_success:
            print("PASS")
            results["passed"] += 1
            return result
        else:
            print(f"FAIL - {result}")
            results["failed"] += 1
            results["errors"].append(f"{name}: {result}")
            return None
    except Exception as e:
        print(f"ERROR - {e}")
        results["failed"] += 1
        results["errors"].append(f"{name}: {e}")
        return None

def run_torture_test():
    print("=" * 60)
    print("Unity Bridge Lite Torture Test")
    print("=" * 60)

    # === BASIC CONNECTIVITY ===
    print("\n[1/8] Basic Connectivity")
    test("ping", "ping")
    test("list_commands", "list_commands")

    # === SCENE INFO ===
    print("\n[2/8] Scene Information")
    test("get_scene_info", "get_scene_info")
    test("get_hierarchy", "get_hierarchy")
    test("get_selection", "get_selection")
    test("get_console", "get_console")

    # === GAMEOBJECT CREATION ===
    print("\n[3/8] GameObject Creation")

    # Create test objects via menu
    test("create_cube", "execute_menu", {"path": "GameObject/3D Object/Cube"})
    time.sleep(0.3)
    test("create_sphere", "execute_menu", {"path": "GameObject/3D Object/Sphere"})
    time.sleep(0.3)
    test("create_cylinder", "execute_menu", {"path": "GameObject/3D Object/Cylinder"})
    time.sleep(0.3)
    test("create_plane", "execute_menu", {"path": "GameObject/3D Object/Plane"})
    time.sleep(0.3)
    test("create_point_light", "execute_menu", {"path": "GameObject/Light/Point Light"})
    time.sleep(0.3)

    # Create empty GameObjects
    test("create_empty_parent", "create_gameobject", {"name": "TestParent"})
    time.sleep(0.2)
    test("create_empty_child", "create_gameobject", {"name": "TestChild", "parent": "TestParent"})
    time.sleep(0.2)

    # === SELECTION ===
    print("\n[4/8] Selection")
    test("select_cube", "select_gameobject", {"name": "Cube"})
    time.sleep(0.2)
    test("verify_selection", "get_selection")

    # === MATERIALS & COLORS ===
    print("\n[5/8] Materials & Colors")
    colors = ["FF0000", "00FF00", "0000FF", "FFFF00", "FF00FF", "00FFFF"]
    objects = ["Cube", "Sphere", "Cylinder"]

    for i, obj in enumerate(objects):
        color = colors[i % len(colors)]
        test(f"color_{obj}_{color}", "set_material_color", {"name": obj, "color": color})
        time.sleep(0.2)

    # === ANIMATION ===
    print("\n[6/8] Animation (Rotation & Orbit)")

    # Start rotations
    test("rotate_cube", "start_rotation", {"name": "Cube", "y": 45})
    test("rotate_sphere", "start_rotation", {"name": "Sphere", "x": 30, "y": 60})
    test("rotate_cylinder", "start_rotation", {"name": "Cylinder", "x": 15, "y": 30, "z": 45})

    # Start orbits
    test("orbit_sphere", "start_orbit", {"name": "Sphere", "radius": 3, "speed": 60})

    print("  Letting animations run for 3 seconds...")
    time.sleep(3)

    # Stop animations
    test("stop_rotation_cube", "stop_rotation", {"name": "Cube"})
    test("stop_orbit_sphere", "stop_orbit", {"name": "Sphere"})
    test("stop_all_rotations", "stop_rotation", {"all": True})
    test("stop_all_orbits", "stop_orbit", {"all": True})

    # === SETTINGS & CONFIG ===
    print("\n[7/8] Settings & Configuration")
    test("get_build_target", "get_build_target")
    test("get_player_settings_ios", "get_player_settings", {"platform": "ios"})
    test("get_player_settings_android", "get_player_settings", {"platform": "android"})
    test("get_packages", "get_packages")

    # === MULTISET CONFIG ===
    print("\n[8/8] MultiSet Configuration")
    test("get_multiset_config", "get_multiset_config")

    # === STRESS TEST ===
    print("\n[STRESS] Rapid Command Execution (50 pings)")
    stress_start = time.time()
    stress_pass = 0
    for i in range(50):
        result = send_command("ping")
        if result.get("status") == "success":
            stress_pass += 1
    stress_time = time.time() - stress_start
    print(f"  {stress_pass}/50 pings in {stress_time:.2f}s ({50/stress_time:.1f} ops/sec)")
    results["passed"] += stress_pass
    results["failed"] += (50 - stress_pass)

    # === SUMMARY ===
    print("\n" + "=" * 60)
    print("TORTURE TEST COMPLETE")
    print("=" * 60)
    total = results["passed"] + results["failed"]
    print(f"Passed: {results['passed']}/{total}")
    print(f"Failed: {results['failed']}/{total}")

    if results["errors"]:
        print("\nErrors:")
        for err in results["errors"]:
            print(f"  - {err}")

    return results["failed"] == 0

if __name__ == "__main__":
    success = run_torture_test()
    sys.exit(0 if success else 1)
