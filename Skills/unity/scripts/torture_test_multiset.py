#!/usr/bin/env python3
"""
MultiSet SDK Torture Test
Tests MultiSet SDK installation, configuration, and scene setup.
"""

import sys
import os
import time
import json

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_client import send_command

# Test results tracking
results = {"passed": 0, "failed": 0, "warnings": 0, "errors": []}

def normalize_result(data):
    """Convert Key/Value array format to dict if needed."""
    if isinstance(data, list) and len(data) > 0 and isinstance(data[0], dict) and "Key" in data[0]:
        return {item["Key"]: item["Value"] for item in data}
    return data

def test(name, command, params=None, expect_success=True):
    """Run a test and track results."""
    print(f"  Testing {name}...", end=" ")
    try:
        result = send_command(command, params or {})
        success = result.get("status") == "success"
        if success == expect_success:
            print("PASS")
            results["passed"] += 1
            raw_result = result.get("result", result)
            return normalize_result(raw_result)
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

def check(name, condition, warning_msg=None):
    """Check a condition."""
    print(f"  Checking {name}...", end=" ")
    if condition:
        print("OK")
        results["passed"] += 1
        return True
    else:
        if warning_msg:
            print(f"WARNING - {warning_msg}")
            results["warnings"] += 1
        else:
            print("FAIL")
            results["failed"] += 1
        return False

def run_torture_test():
    print("=" * 60)
    print("MultiSet SDK Torture Test")
    print("=" * 60)

    # === CONNECTIVITY ===
    print("\n[1/6] Unity Bridge Connectivity")
    ping_result = test("ping", "ping")
    if not ping_result:
        print("\nFATAL: Cannot connect to Unity. Aborting.")
        return False

    # === SDK VERIFICATION ===
    print("\n[2/6] MultiSet SDK Installation")
    verify_result = test("verify_multiset_sdk", "verify_multiset_sdk")

    if verify_result:
        check("package_installed", verify_result.get("packageInstalled", False),
              "MultiSet package not installed")
        check("config_exists", verify_result.get("configExists", False),
              "MultiSetConfig.asset not found")
        check("has_client_id", verify_result.get("hasClientId", False),
              "Client ID not configured")
        check("has_client_secret", verify_result.get("hasClientSecret", False),
              "Client Secret not configured")
        check("config_valid", verify_result.get("configValid", False),
              "Config incomplete")

        found_types = verify_result.get("multisetTypes", [])
        print(f"  Found MultiSet types: {found_types}")
        check("sdk_types_available", len(found_types) >= 2,
              f"Only found {len(found_types)} types")
        check("sdk_ready", verify_result.get("sdkReady", False),
              "SDK not fully ready")

    # === CONFIG READ/WRITE ===
    print("\n[3/6] MultiSet Config Operations")
    config_result = test("get_multiset_config", "get_multiset_config")

    if config_result:
        exists = config_result.get("exists", False)
        check("config_asset_exists", exists, "Config asset missing")

        if exists:
            has_id = len(config_result.get("clientId", "")) > 0
            has_secret = config_result.get("hasSecret", False)
            check("client_id_set", has_id, "Client ID empty")
            check("client_secret_set", has_secret, "Client Secret empty")

    # === SCENE CHECK ===
    print("\n[4/6] Scene MultiSet Components")
    scene_result = test("check_multiset_scene", "check_multiset_scene")

    if scene_result:
        scene_name = scene_result.get("sceneName", "Unknown")
        print(f"  Current scene: {scene_name}")

        components = scene_result.get("components", {})
        if isinstance(components, list):
            components = normalize_result(components)
        for comp, found in components.items():
            status = "found" if found else "not in scene"
            print(f"    {comp}: {status}")

        has_any = scene_result.get("hasMultiSetComponents", False)
        if not has_any:
            print("  NOTE: No MultiSet components in current scene (expected for empty scene)")

    # === SAMPLE SCENES ===
    print("\n[5/6] Available Sample Scenes")
    samples_result = test("list_samples", "import_multiset_samples")

    if samples_result:
        samples = samples_result.get("availableSamples", [])
        print(f"  Available samples: {len(samples)}")
        for sample in samples:
            print(f"    - {sample}")
        check("samples_available", len(samples) > 0, "No samples found")

    # === STRESS TEST ===
    print("\n[6/6] Stress Test (20 rapid verifications)")
    stress_start = time.time()
    stress_pass = 0
    for i in range(20):
        result = send_command("verify_multiset_sdk")
        if result.get("status") == "success":
            stress_pass += 1
    stress_time = time.time() - stress_start
    print(f"  {stress_pass}/20 verifications in {stress_time:.2f}s ({20/stress_time:.1f} ops/sec)")
    results["passed"] += stress_pass
    results["failed"] += (20 - stress_pass)

    # === SUMMARY ===
    print("\n" + "=" * 60)
    print("MULTISET TORTURE TEST COMPLETE")
    print("=" * 60)
    total = results["passed"] + results["failed"]
    print(f"Passed: {results['passed']}/{total}")
    print(f"Failed: {results['failed']}/{total}")
    print(f"Warnings: {results['warnings']}")

    if results["errors"]:
        print("\nErrors:")
        for err in results["errors"][:5]:  # Show first 5
            print(f"  - {err}")

    # Overall status
    if verify_result and verify_result.get("sdkReady"):
        print("\nMultiSet SDK Status: READY")
    elif verify_result and verify_result.get("configValid"):
        print("\nMultiSet SDK Status: CONFIGURED (may need scene setup)")
    else:
        print("\nMultiSet SDK Status: NEEDS CONFIGURATION")

    return results["failed"] == 0

if __name__ == "__main__":
    success = run_torture_test()
    sys.exit(0 if success else 1)
