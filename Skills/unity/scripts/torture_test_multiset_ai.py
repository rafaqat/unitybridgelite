#!/usr/bin/env python3
"""
MultiSet AI SDK Comprehensive Torture Test
Tests all MultiSet SDK commands with stress testing and validation.
"""

import sys
import os
import time
import json
import random
import string

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from unity_client import send_command

# Test results tracking
results = {"passed": 0, "failed": 0, "warnings": 0, "skipped": 0, "errors": []}

def normalize_result(data):
    """Convert Key/Value array format to dict if needed."""
    if isinstance(data, list) and len(data) > 0 and isinstance(data[0], dict) and "Key" in data[0]:
        return {item["Key"]: item["Value"] for item in data}
    return data

def test(name, command, params=None, expect_success=True, silent=False):
    """Run a test and track results."""
    if not silent:
        print(f"  {name}...", end=" ")
    try:
        result = send_command(command, params or {})
        success = result.get("status") == "success"
        if success == expect_success:
            if not silent:
                print("PASS")
            results["passed"] += 1
            raw_result = result.get("result", result)
            return normalize_result(raw_result)
        else:
            if not silent:
                print(f"FAIL")
            results["failed"] += 1
            results["errors"].append(f"{name}: {result.get('error', 'unexpected result')}")
            return None
    except Exception as e:
        if not silent:
            print(f"ERROR - {e}")
        results["failed"] += 1
        results["errors"].append(f"{name}: {e}")
        return None

def check(name, condition, warning_msg=None):
    """Check a condition."""
    print(f"  {name}...", end=" ")
    if condition:
        print("OK")
        results["passed"] += 1
        return True
    else:
        if warning_msg:
            print(f"WARN - {warning_msg}")
            results["warnings"] += 1
        else:
            print("FAIL")
            results["failed"] += 1
        return False

def section(title):
    """Print section header."""
    print(f"\n{'='*60}")
    print(f" {title}")
    print(f"{'='*60}")

def run_torture_test():
    print("=" * 60)
    print("  MULTISET AI SDK COMPREHENSIVE TORTURE TEST")
    print("=" * 60)
    print(f"  Started: {time.strftime('%Y-%m-%d %H:%M:%S')}")

    # =========================================================================
    section("1. CONNECTIVITY & BASIC COMMANDS")
    # =========================================================================

    ping = test("Ping Unity", "ping")
    if not ping:
        print("\nFATAL: Cannot connect to Unity. Aborting.")
        return False

    commands = test("List commands", "list_commands")
    if commands:
        cmd_list = commands.get("commands", [])
        multiset_cmds = [c for c in cmd_list if "multiset" in c.lower()]
        print(f"    Total commands: {len(cmd_list)}")
        print(f"    MultiSet commands: {len(multiset_cmds)}")
        check("MultiSet commands available", len(multiset_cmds) >= 4)

    # =========================================================================
    section("2. SDK VERIFICATION")
    # =========================================================================

    verify = test("Verify MultiSet SDK", "verify_multiset_sdk")

    sdk_ready = False
    if verify:
        check("Package installed", verify.get("packageInstalled", False),
              "MultiSet package not found")
        check("Config exists", verify.get("configExists", False),
              "MultiSetConfig.asset missing")
        check("Has Client ID", verify.get("hasClientId", False),
              "Client ID not set")
        check("Has Client Secret", verify.get("hasClientSecret", False),
              "Client Secret not set")
        check("Config valid", verify.get("configValid", False),
              "Config incomplete")

        types = verify.get("multisetTypes", [])
        print(f"    SDK Types found: {len(types)}")
        for t in types[:5]:  # Show first 5
            print(f"      - {t}")
        if len(types) > 5:
            print(f"      ... and {len(types) - 5} more")

        check("Core types present", len(types) >= 2,
              f"Only {len(types)} types found")

        sdk_ready = verify.get("sdkReady", False)
        check("SDK ready", sdk_ready, "SDK not fully operational")

    # =========================================================================
    section("3. CONFIG OPERATIONS")
    # =========================================================================

    config = test("Get MultiSet config", "get_multiset_config")

    original_client_id = None
    if config:
        exists = config.get("exists", False)
        check("Config asset exists", exists)

        if exists:
            original_client_id = config.get("clientId", "")
            has_id = len(original_client_id) > 0
            has_secret = config.get("hasSecret", False)

            check("Client ID configured", has_id, "Client ID empty")
            check("Client Secret configured", has_secret, "No secret set")

            if has_id:
                print(f"    Client ID: {original_client_id[:8]}...{original_client_id[-4:]}")

    # Test config read consistency
    print("\n  Config consistency test:")
    if original_client_id:
        # Read config multiple times and verify consistency
        read1 = test("  First read", "get_multiset_config")
        read2 = test("  Second read", "get_multiset_config")
        if read1 and read2:
            id1 = read1.get("clientId", "")
            id2 = read2.get("clientId", "")
            check("  Config consistent", id1 == id2 == original_client_id,
                  "Config values inconsistent between reads")

    # =========================================================================
    section("4. SCENE ANALYSIS")
    # =========================================================================

    scene = test("Check MultiSet scene", "check_multiset_scene")

    if scene:
        scene_name = scene.get("sceneName", "Unknown")
        print(f"    Current scene: {scene_name}")

        components = scene.get("components", {})
        if isinstance(components, list):
            components = normalize_result(components)

        found_count = 0
        for comp, found in components.items():
            status = "FOUND" if found else "not present"
            print(f"      {comp}: {status}")
            if found:
                found_count += 1

        has_components = scene.get("hasMultiSetComponents", False)
        if has_components:
            check("Scene has MultiSet components", True)
        else:
            print("    Note: No MultiSet components in scene (normal for test scene)")
            results["skipped"] += 1

    # =========================================================================
    section("5. SAMPLE SCENES")
    # =========================================================================

    samples = test("Get sample scenes", "import_multiset_samples")

    if samples:
        available = samples.get("availableSamples", [])
        print(f"    Available samples: {len(available)}")
        for sample in available[:5]:
            print(f"      - {sample}")
        if len(available) > 5:
            print(f"      ... and {len(available) - 5} more")

        check("Samples available", len(available) > 0, "No sample scenes found")

    # =========================================================================
    section("6. STRESS TEST - Rapid SDK Verification")
    # =========================================================================

    iterations = 30
    print(f"  Running {iterations} rapid verifications...")

    stress_start = time.time()
    stress_pass = 0
    stress_times = []

    for i in range(iterations):
        iter_start = time.time()
        result = send_command("verify_multiset_sdk")
        iter_time = time.time() - iter_start
        stress_times.append(iter_time)

        if result.get("status") == "success":
            stress_pass += 1

        # Progress indicator
        if (i + 1) % 10 == 0:
            print(f"    Progress: {i + 1}/{iterations}")

    stress_total = time.time() - stress_start
    avg_time = sum(stress_times) / len(stress_times)
    min_time = min(stress_times)
    max_time = max(stress_times)

    print(f"    Results: {stress_pass}/{iterations} passed")
    print(f"    Total time: {stress_total:.2f}s")
    print(f"    Ops/second: {iterations/stress_total:.1f}")
    print(f"    Latency: avg={avg_time*1000:.1f}ms, min={min_time*1000:.1f}ms, max={max_time*1000:.1f}ms")

    results["passed"] += stress_pass
    results["failed"] += (iterations - stress_pass)

    check("Stress test >90% pass rate", stress_pass >= iterations * 0.9)

    # =========================================================================
    section("7. STRESS TEST - Config Read Cycle")
    # =========================================================================

    iterations = 20
    print(f"  Running {iterations} config read operations...")

    config_start = time.time()
    config_pass = 0

    for i in range(iterations):
        result = send_command("get_multiset_config")
        if result.get("status") == "success":
            config_pass += 1

    config_time = time.time() - config_start
    print(f"    Results: {config_pass}/{iterations} passed")
    print(f"    Time: {config_time:.2f}s ({iterations/config_time:.1f} ops/sec)")

    results["passed"] += config_pass
    results["failed"] += (iterations - config_pass)

    # =========================================================================
    section("8. STRESS TEST - Mixed Commands")
    # =========================================================================

    print("  Running mixed command stress test...")

    commands_to_test = [
        ("verify_multiset_sdk", {}),
        ("get_multiset_config", {}),
        ("check_multiset_scene", {}),
        ("import_multiset_samples", {}),
    ]

    mixed_start = time.time()
    mixed_pass = 0
    total_ops = 0

    for _ in range(5):  # 5 rounds
        for cmd, params in commands_to_test:
            result = send_command(cmd, params)
            total_ops += 1
            if result.get("status") == "success":
                mixed_pass += 1

    mixed_time = time.time() - mixed_start
    print(f"    Results: {mixed_pass}/{total_ops} passed")
    print(f"    Time: {mixed_time:.2f}s ({total_ops/mixed_time:.1f} ops/sec)")

    results["passed"] += mixed_pass
    results["failed"] += (total_ops - mixed_pass)

    # =========================================================================
    # SUMMARY
    # =========================================================================
    print("\n" + "=" * 60)
    print("  TORTURE TEST COMPLETE")
    print("=" * 60)

    total = results["passed"] + results["failed"]
    pass_rate = (results["passed"] / total * 100) if total > 0 else 0

    print(f"\n  Results:")
    print(f"    Passed:   {results['passed']}/{total} ({pass_rate:.1f}%)")
    print(f"    Failed:   {results['failed']}/{total}")
    print(f"    Warnings: {results['warnings']}")
    print(f"    Skipped:  {results['skipped']}")

    if results["errors"]:
        print(f"\n  Errors ({len(results['errors'])}):")
        for err in results["errors"][:10]:
            print(f"    - {err}")
        if len(results["errors"]) > 10:
            print(f"    ... and {len(results['errors']) - 10} more")

    # SDK Status Summary
    print("\n  SDK Status:")
    if sdk_ready:
        print("    [READY] MultiSet SDK is fully operational")
    elif verify and verify.get("configValid"):
        print("    [CONFIGURED] SDK configured but may need scene setup")
    else:
        print("    [NEEDS SETUP] SDK requires configuration")

    print(f"\n  Finished: {time.strftime('%Y-%m-%d %H:%M:%S')}")

    return results["failed"] == 0

if __name__ == "__main__":
    try:
        success = run_torture_test()
        sys.exit(0 if success else 1)
    except KeyboardInterrupt:
        print("\n\nTest interrupted by user.")
        sys.exit(130)
    except Exception as e:
        print(f"\n\nFatal error: {e}")
        sys.exit(1)
