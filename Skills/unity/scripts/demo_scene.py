#!/usr/bin/env python3
"""
Unity Bridge Lite Demo Scene Creator
Creates an animated solar system demo to showcase all skills.
"""

import sys
import os
import time
import math

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_client import send_command

def create_solar_system():
    print("=" * 60)
    print("Unity Bridge Lite - Solar System Demo")
    print("=" * 60)

    # Check connection
    result = send_command("ping")
    if result.get("status") != "success":
        print("ERROR: Cannot connect to Unity")
        return False

    print("\n[1/6] Creating the Sun...")
    send_command("execute_menu", {"path": "GameObject/3D Object/Sphere"})
    time.sleep(0.3)
    send_command("rename_gameobject", {"name": "Sphere", "newName": "Sun"})
    send_command("set_transform", {"name": "Sun", "scale": 2})
    send_command("set_material_color", {"name": "Sun", "color": "FFCC00"})
    time.sleep(0.2)

    print("\n[2/6] Creating planets...")
    planets = [
        {"name": "Mercury", "color": "888888", "distance": 3, "scale": 0.3, "orbit_speed": 80},
        {"name": "Venus", "color": "FFAA55", "distance": 4.5, "scale": 0.5, "orbit_speed": 60},
        {"name": "Earth", "color": "3366FF", "distance": 6, "scale": 0.5, "orbit_speed": 45},
        {"name": "Mars", "color": "FF4400", "distance": 8, "scale": 0.4, "orbit_speed": 35},
        {"name": "Jupiter", "color": "FFCC88", "distance": 11, "scale": 1.0, "orbit_speed": 20},
    ]

    for i, planet in enumerate(planets):
        print(f"  Creating {planet['name']}...")
        send_command("execute_menu", {"path": "GameObject/3D Object/Sphere"})
        time.sleep(0.2)
        send_command("rename_gameobject", {"name": "Sphere", "newName": planet["name"]})
        send_command("set_transform", {
            "name": planet["name"],
            "x": planet["distance"],
            "y": 0,
            "z": 0,
            "scale": planet["scale"]
        })
        send_command("set_material_color", {"name": planet["name"], "color": planet["color"]})
        time.sleep(0.1)

    print("\n[3/6] Adding a moon to Earth...")
    send_command("execute_menu", {"path": "GameObject/3D Object/Sphere"})
    time.sleep(0.2)
    send_command("rename_gameobject", {"name": "Sphere", "newName": "Moon"})
    send_command("set_transform", {"name": "Moon", "x": 7, "y": 0, "z": 0, "scale": 0.15})
    send_command("set_material_color", {"name": "Moon", "color": "CCCCCC"})

    print("\n[4/6] Creating orbital ring markers...")
    send_command("create_gameobject", {"name": "OrbitMarkers"})
    for planet in planets:
        send_command("execute_menu", {"path": "GameObject/3D Object/Cylinder"})
        time.sleep(0.1)
        send_command("rename_gameobject", {"name": "Cylinder", "newName": f"{planet['name']}Orbit"})
        send_command("set_transform", {
            "name": f"{planet['name']}Orbit",
            "x": 0, "y": -0.1, "z": 0,
            "sx": planet["distance"] * 2,
            "sy": 0.01,
            "sz": planet["distance"] * 2
        })
        send_command("set_material_color", {"name": f"{planet['name']}Orbit", "color": "333333"})
        send_command("set_parent", {"name": f"{planet['name']}Orbit", "parent": "OrbitMarkers"})

    print("\n[5/6] Adding lights...")
    send_command("execute_menu", {"path": "GameObject/Light/Point Light"})
    time.sleep(0.2)
    send_command("rename_gameobject", {"name": "Point Light", "newName": "SunLight"})
    send_command("set_transform", {"name": "SunLight", "x": 0, "y": 0, "z": 0})

    print("\n[6/6] Starting animations...")
    # Rotate sun
    send_command("start_rotation", {"name": "Sun", "y": 10})

    # Orbit planets
    for planet in planets:
        send_command("start_orbit", {
            "name": planet["name"],
            "radius": planet["distance"],
            "speed": planet["orbit_speed"],
            "center_x": 0,
            "center_y": 0,
            "center_z": 0
        })

    # Moon orbits Earth
    send_command("start_orbit", {
        "name": "Moon",
        "radius": 1,
        "speed": 120,
        "center_x": 6,
        "center_y": 0,
        "center_z": 0
    })

    # Rotate planets
    for planet in planets:
        send_command("start_rotation", {"name": planet["name"], "y": 30})

    print("\n" + "=" * 60)
    print("Solar System Demo Created!")
    print("=" * 60)
    print("\nObjects created:")
    print("  - Sun (rotating)")
    print("  - 5 planets (orbiting and rotating)")
    print("  - Moon (orbiting Earth)")
    print("  - Orbital ring markers")
    print("  - Point light at Sun position")
    print("\nPress Ctrl+C to stop, or run:")
    print("  python3 demo_scene.py --stop")

    return True

def stop_animations():
    print("Stopping all animations...")
    send_command("stop_rotation", {"all": True})
    send_command("stop_orbit", {"all": True})
    print("Done!")

def cleanup_scene():
    print("Cleaning up demo objects...")
    objects = ["Sun", "Mercury", "Venus", "Earth", "Mars", "Jupiter",
               "Moon", "SunLight", "OrbitMarkers"]
    for obj in objects:
        try:
            send_command("delete_gameobject", {"name": obj})
        except:
            pass

    # Also clean up orbit markers
    for planet in ["Mercury", "Venus", "Earth", "Mars", "Jupiter"]:
        try:
            send_command("delete_gameobject", {"name": f"{planet}Orbit"})
        except:
            pass
    print("Done!")

if __name__ == "__main__":
    if len(sys.argv) > 1:
        if sys.argv[1] == "--stop":
            stop_animations()
        elif sys.argv[1] == "--cleanup":
            cleanup_scene()
        else:
            print(f"Unknown option: {sys.argv[1]}")
            print("Usage: demo_scene.py [--stop|--cleanup]")
    else:
        success = create_solar_system()
        sys.exit(0 if success else 1)
