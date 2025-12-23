#!/usr/bin/env python3
"""
MultiSet SDK Status Visualization
Creates a 3D dashboard in Unity showing MultiSet SDK status.
"""

import sys
import os
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_client import send_command

# Colors
GREEN = "00FF00"   # Pass/Ready
RED = "FF0000"     # Fail/Error
YELLOW = "FFFF00"  # Warning
BLUE = "3366FF"    # Info
GRAY = "666666"    # Neutral
WHITE = "FFFFFF"   # Text/Labels
ORANGE = "FF8800"  # Pending

def create_status_indicator(name, x, y, z, color, scale=0.5):
    """Create a sphere indicator at position with color."""
    send_command("execute_menu", {"path": "GameObject/3D Object/Sphere"})
    time.sleep(0.15)
    send_command("rename_gameobject", {"name": "Sphere", "newName": name})
    send_command("set_transform", {"name": name, "x": x, "y": y, "z": z, "scale": scale})
    send_command("set_material_color", {"name": name, "color": color})
    return name

def create_label_cube(name, x, y, z, color, sx=2, sy=0.3, sz=0.1):
    """Create a flat cube as a label background."""
    send_command("execute_menu", {"path": "GameObject/3D Object/Cube"})
    time.sleep(0.15)
    send_command("rename_gameobject", {"name": "Cube", "newName": name})
    send_command("set_transform", {"name": name, "x": x, "y": y, "z": z, "sx": sx, "sy": sy, "sz": sz})
    send_command("set_material_color", {"name": name, "color": color})
    return name

def create_pedestal(name, x, y, z, height=1):
    """Create a cylinder pedestal."""
    send_command("execute_menu", {"path": "GameObject/3D Object/Cylinder"})
    time.sleep(0.15)
    send_command("rename_gameobject", {"name": "Cylinder", "newName": name})
    send_command("set_transform", {"name": name, "x": x, "y": y, "z": z, "sx": 0.3, "sy": height/2, "sz": 0.3})
    send_command("set_material_color", {"name": name, "color": GRAY})
    return name

def normalize_result(data):
    """Convert Key/Value array format to dict if needed."""
    if isinstance(data, list) and len(data) > 0 and isinstance(data[0], dict) and "Key" in data[0]:
        return {item["Key"]: item["Value"] for item in data}
    return data

def run_visual_test():
    print("=" * 60)
    print("MultiSet SDK Status Visualization")
    print("=" * 60)

    # Check connection
    result = send_command("ping")
    if result.get("status") != "success":
        print("ERROR: Cannot connect to Unity")
        return False

    # Clean up any previous visualization
    print("\n[1/5] Cleaning up previous visualization...")
    cleanup_objects = [
        "StatusDashboard", "SDKStatusIndicator", "ConfigIndicator",
        "PackageIndicator", "SceneIndicator", "SamplesIndicator",
        "StatusPedestal1", "StatusPedestal2", "StatusPedestal3",
        "StatusPedestal4", "StatusPedestal5", "StatusTitle",
        "StatusBase", "TypeIndicator1", "TypeIndicator2", "TypeIndicator3",
        "StressOrb", "PerformanceRing"
    ]
    for obj in cleanup_objects:
        send_command("delete_gameobject", {"name": obj})
    time.sleep(0.3)

    # Create parent container
    print("\n[2/5] Creating dashboard structure...")
    send_command("create_gameobject", {"name": "StatusDashboard"})

    # Create base platform
    send_command("execute_menu", {"path": "GameObject/3D Object/Cube"})
    time.sleep(0.15)
    send_command("rename_gameobject", {"name": "Cube", "newName": "StatusBase"})
    send_command("set_transform", {"name": "StatusBase", "x": 0, "y": -0.1, "z": 0, "sx": 12, "sy": 0.2, "sz": 8})
    send_command("set_material_color", {"name": "StatusBase", "color": "222222"})
    send_command("set_parent", {"name": "StatusBase", "parent": "StatusDashboard"})

    # Create title bar
    create_label_cube("StatusTitle", 0, 3, -3, "333333", sx=8, sy=0.5, sz=0.1)
    send_command("set_parent", {"name": "StatusTitle", "parent": "StatusDashboard"})

    # Run SDK verification
    print("\n[3/5] Checking MultiSet SDK status...")
    verify_result = send_command("verify_multiset_sdk")
    verify = verify_result.get("result", {}) if verify_result.get("status") == "success" else {}
    verify = normalize_result(verify)

    config_result = send_command("get_multiset_config")
    config = config_result.get("result", {}) if config_result.get("status") == "success" else {}
    config = normalize_result(config)

    scene_result = send_command("check_multiset_scene")
    scene = scene_result.get("result", {}) if scene_result.get("status") == "success" else {}
    scene = normalize_result(scene)

    samples_result = send_command("import_multiset_samples")
    samples = samples_result.get("result", {}) if samples_result.get("status") == "success" else {}
    samples = normalize_result(samples)

    # Determine statuses
    package_ok = verify.get("packageInstalled", False)
    config_ok = verify.get("configValid", False)
    has_id = verify.get("hasClientId", False)
    has_secret = verify.get("hasClientSecret", False)
    sdk_ready = verify.get("sdkReady", False)
    sdk_types = verify.get("multisetTypes", [])
    sample_list = samples.get("availableSamples", [])

    print(f"  Package installed: {package_ok}")
    print(f"  Config valid: {config_ok}")
    print(f"  SDK ready: {sdk_ready}")
    print(f"  Types found: {len(sdk_types)}")
    print(f"  Samples available: {len(sample_list)}")

    # Create status indicators
    print("\n[4/5] Creating status indicators...")

    # Main SDK Status (center, large)
    main_color = GREEN if sdk_ready else (YELLOW if config_ok else RED)
    create_pedestal("StatusPedestal1", 0, 0.5, 0, height=2)
    create_status_indicator("SDKStatusIndicator", 0, 2, 0, main_color, scale=1.0)
    send_command("set_parent", {"name": "StatusPedestal1", "parent": "StatusDashboard"})
    send_command("set_parent", {"name": "SDKStatusIndicator", "parent": "StatusDashboard"})

    # Make main indicator pulse/rotate
    send_command("start_rotation", {"name": "SDKStatusIndicator", "y": 20})

    # Package Status (left)
    pkg_color = GREEN if package_ok else RED
    create_pedestal("StatusPedestal2", -4, 0.25, 0, height=1)
    create_status_indicator("PackageIndicator", -4, 1, 0, pkg_color, scale=0.6)
    send_command("set_parent", {"name": "StatusPedestal2", "parent": "StatusDashboard"})
    send_command("set_parent", {"name": "PackageIndicator", "parent": "StatusDashboard"})

    # Config Status (left-center)
    cfg_color = GREEN if config_ok else (YELLOW if has_id else RED)
    create_pedestal("StatusPedestal3", -2, 0.25, 0, height=1)
    create_status_indicator("ConfigIndicator", -2, 1, 0, cfg_color, scale=0.6)
    send_command("set_parent", {"name": "StatusPedestal3", "parent": "StatusDashboard"})
    send_command("set_parent", {"name": "ConfigIndicator", "parent": "StatusDashboard"})

    # Samples Status (right-center)
    samples_color = GREEN if len(sample_list) > 5 else (YELLOW if len(sample_list) > 0 else RED)
    create_pedestal("StatusPedestal4", 2, 0.25, 0, height=1)
    create_status_indicator("SamplesIndicator", 2, 1, 0, samples_color, scale=0.6)
    send_command("set_parent", {"name": "StatusPedestal4", "parent": "StatusDashboard"})
    send_command("set_parent", {"name": "SamplesIndicator", "parent": "StatusDashboard"})

    # Scene Status (right)
    has_components = scene.get("hasMultiSetComponents", False)
    scene_color = GREEN if has_components else GRAY
    create_pedestal("StatusPedestal5", 4, 0.25, 0, height=1)
    create_status_indicator("SceneIndicator", 4, 1, 0, scene_color, scale=0.6)
    send_command("set_parent", {"name": "StatusPedestal5", "parent": "StatusDashboard"})
    send_command("set_parent", {"name": "SceneIndicator", "parent": "StatusDashboard"})

    # Create type indicators (small spheres in a row)
    print("\n[5/5] Creating SDK type indicators...")
    type_colors = [BLUE, "9966FF", "66CCFF"]  # Different blues/purples
    for i, sdk_type in enumerate(sdk_types[:3]):
        x = -1 + i * 1
        create_status_indicator(f"TypeIndicator{i+1}", x, 0.3, 2, type_colors[i % len(type_colors)], scale=0.3)
        send_command("set_parent", {"name": f"TypeIndicator{i+1}", "parent": "StatusDashboard"})
        send_command("start_rotation", {"name": f"TypeIndicator{i+1}", "y": 30 + i * 15})

    # Create performance ring (orbiting indicator)
    create_status_indicator("StressOrb", 3, 1.5, 0, ORANGE, scale=0.2)
    send_command("start_orbit", {"name": "StressOrb", "radius": 3, "speed": 60, "center_y": 1.5})
    send_command("set_parent", {"name": "StressOrb", "parent": "StatusDashboard"})

    # Position camera
    send_command("set_transform", {
        "name": "Main Camera",
        "x": 0, "y": 6, "z": -10,
        "rx": 25, "ry": 0, "rz": 0
    })

    # Enable always refresh
    send_command("set_always_refresh", {"enable": True})

    # Print summary
    print("\n" + "=" * 60)
    print("STATUS DASHBOARD CREATED")
    print("=" * 60)
    print("\nIndicators (left to right):")
    print(f"  Package:  {'GREEN (installed)' if package_ok else 'RED (not found)'}")
    print(f"  Config:   {'GREEN (valid)' if config_ok else ('YELLOW (partial)' if has_id else 'RED (missing)')}")
    print(f"  Samples:  {'GREEN' if len(sample_list) > 5 else 'YELLOW'} ({len(sample_list)} available)")
    print(f"  Scene:    {'GREEN (has components)' if has_components else 'GRAY (no components)'}")
    print(f"\nCenter:    {'GREEN (SDK READY)' if sdk_ready else ('YELLOW (configured)' if config_ok else 'RED (needs setup)')}")
    print(f"\nSDK Types: {len(sdk_types)} (shown as blue orbs)")
    print(f"Orange orb: Performance indicator (orbiting)")

    return True

def cleanup():
    """Remove all dashboard objects."""
    print("Cleaning up dashboard...")
    send_command("delete_gameobject", {"name": "StatusDashboard"})
    print("Done!")

if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "--cleanup":
        cleanup()
    else:
        success = run_visual_test()
        sys.exit(0 if success else 1)
