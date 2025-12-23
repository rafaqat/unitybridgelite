using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace UnityBridgeLite
{
    /// <summary>
    /// Executes commands received from external tools.
    /// Auto-discovers command handlers marked with [BridgeCommand] attribute.
    /// </summary>
    public static class CommandExecutor
    {
        private static readonly Dictionary<string, MethodInfo> _handlers = new();
        private static bool _initialized;

        public static IReadOnlyDictionary<string, MethodInfo> Handlers => _handlers;

        public static string Execute(string json)
        {
            EnsureInitialized();

            try
            {
                // Parse command
                var command = ParseCommand(json);
                if (command == null)
                {
                    return ErrorResponse("Invalid JSON format");
                }

                var commandType = command.type?.ToLowerInvariant() ?? "";

                // Built-in commands
                if (commandType == "ping")
                {
                    return SuccessResponse(new { message = "pong" });
                }

                if (commandType == "list_commands")
                {
                    return SuccessResponse(new { commands = _handlers.Keys.ToArray() });
                }

                // Find handler
                if (!_handlers.TryGetValue(commandType, out var handler))
                {
                    return ErrorResponse($"Unknown command: {commandType}");
                }

                // Execute handler
                var result = handler.Invoke(null, new object[] { command.@params ?? new Dictionary<string, object>() });
                return SuccessResponse(result);
            }
            catch (TargetInvocationException ex)
            {
                return ErrorResponse(ex.InnerException?.Message ?? ex.Message);
            }
            catch (Exception ex)
            {
                return ErrorResponse(ex.Message);
            }
        }

        private static void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            DiscoverHandlers();
            RegisterBuiltinHandlers();

            Debug.Log($"[BridgeLite] Discovered {_handlers.Count} command handlers");
        }

        private static void DiscoverHandlers()
        {
            try
            {
                var assemblies = AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic);

                foreach (var assembly in assemblies)
                {
                    try
                    {
                        foreach (var type in assembly.GetTypes())
                        {
                            var attr = type.GetCustomAttribute<BridgeCommandAttribute>();
                            if (attr == null) continue;

                            var method = type.GetMethod("HandleCommand",
                                BindingFlags.Public | BindingFlags.Static,
                                null,
                                new[] { typeof(Dictionary<string, object>) },
                                null);

                            if (method == null)
                            {
                                Debug.LogWarning($"[BridgeLite] {type.Name} has [BridgeCommand] but no HandleCommand method");
                                continue;
                            }

                            var name = string.IsNullOrEmpty(attr.Name) ? ToSnakeCase(type.Name) : attr.Name;
                            _handlers[name] = method;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BridgeLite] Error discovering handlers: {ex.Message}");
            }
        }

        private static void RegisterBuiltinHandlers()
        {
            // Register built-in Unity control commands
            RegisterHandler("get_scene_info", GetSceneInfo);
            RegisterHandler("get_hierarchy", GetHierarchy);
            RegisterHandler("get_selection", GetSelection);
            RegisterHandler("create_gameobject", CreateGameObject);
            RegisterHandler("get_console", GetConsole);
            RegisterHandler("execute_menu", ExecuteMenu);
            RegisterHandler("set_play_mode", SetPlayMode);
            RegisterHandler("set_material_color", SetMaterialColor);
            RegisterHandler("select_gameobject", SelectGameObject);
            RegisterHandler("start_rotation", StartRotation);
            RegisterHandler("stop_rotation", StopRotation);
            RegisterHandler("start_orbit", StartOrbit);
            RegisterHandler("stop_orbit", StopOrbit);
            RegisterHandler("install_package", InstallPackage);
            RegisterHandler("get_packages", GetPackages);
            RegisterHandler("open_settings", OpenSettings);
            RegisterHandler("get_player_settings", GetPlayerSettings);
            RegisterHandler("set_player_settings", SetPlayerSettings);
            RegisterHandler("get_build_target", GetBuildTarget);
            RegisterHandler("set_build_target", SetBuildTarget);
            RegisterHandler("set_multiset_config", SetMultiSetConfig);
            RegisterHandler("get_multiset_config", GetMultiSetConfig);
            RegisterHandler("create_scriptable_object", CreateScriptableObject);
            RegisterHandler("verify_multiset_sdk", VerifyMultiSetSdk);
            RegisterHandler("import_multiset_samples", ImportMultiSetSamples);
            RegisterHandler("check_multiset_scene", CheckMultiSetScene);
            // GameObject manipulation
            RegisterHandler("delete_gameobject", DeleteGameObject);
            RegisterHandler("rename_gameobject", RenameGameObject);
            RegisterHandler("set_transform", SetTransform);
            RegisterHandler("get_transform", GetTransform);
            RegisterHandler("find_gameobject", FindGameObject);
            RegisterHandler("duplicate_gameobject", DuplicateGameObject);
            RegisterHandler("set_parent", SetParent);
        }

        // Rotation state
        private static readonly Dictionary<int, (GameObject go, Vector3 speed)> _rotatingObjects = new();
        private static bool _rotationUpdateHooked;

        // Orbit state
        private static readonly Dictionary<int, (GameObject go, Vector3 center, float radius, float speed, float angle)> _orbitingObjects = new();
        private static bool _orbitUpdateHooked;

        private static void RegisterHandler(string name, Func<Dictionary<string, object>, object> handler)
        {
            var method = handler.Method;
            _handlers[name] = method;
        }

        #region Built-in Handlers

        private static object GetSceneInfo(Dictionary<string, object> p)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            return new
            {
                name = scene.name,
                path = scene.path,
                rootCount = scene.rootCount,
                isDirty = scene.isDirty,
                isLoaded = scene.isLoaded
            };
        }

        private static object GetHierarchy(Dictionary<string, object> p)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();

            return roots.Select(go => GetGameObjectInfo(go, 0, 2)).ToArray();
        }

        private static object GetGameObjectInfo(GameObject go, int depth, int maxDepth)
        {
            var info = new Dictionary<string, object>
            {
                ["name"] = go.name,
                ["active"] = go.activeSelf,
                ["tag"] = go.tag,
                ["layer"] = LayerMask.LayerToName(go.layer),
                ["components"] = go.GetComponents<Component>()
                    .Where(c => c != null)
                    .Select(c => c.GetType().Name)
                    .ToArray()
            };

            if (depth < maxDepth && go.transform.childCount > 0)
            {
                var children = new List<object>();
                for (int i = 0; i < go.transform.childCount; i++)
                {
                    children.Add(GetGameObjectInfo(go.transform.GetChild(i).gameObject, depth + 1, maxDepth));
                }
                info["children"] = children;
            }

            return info;
        }

        private static object GetSelection(Dictionary<string, object> p)
        {
            return new
            {
                gameObjects = Selection.gameObjects.Select(go => new
                {
                    name = go.name,
                    path = GetGameObjectPath(go)
                }).ToArray(),
                activeObject = Selection.activeGameObject?.name
            };
        }

        private static string GetGameObjectPath(GameObject go)
        {
            var path = go.name;
            var parent = go.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }

        private static object CreateGameObject(Dictionary<string, object> p)
        {
            var name = p.TryGetValue("name", out var n) ? n?.ToString() ?? "New GameObject" : "New GameObject";
            var go = new GameObject(name);

            if (p.TryGetValue("parent", out var parentPath) && parentPath != null)
            {
                var parent = GameObject.Find(parentPath.ToString());
                if (parent != null)
                {
                    go.transform.SetParent(parent.transform);
                }
            }

            Selection.activeGameObject = go;
            Undo.RegisterCreatedObjectUndo(go, "Create GameObject");

            return new
            {
                name = go.name,
                instanceId = go.GetInstanceID()
            };
        }

        private static object GetConsole(Dictionary<string, object> p)
        {
            // Unity's console log is not easily accessible, return placeholder
            return new
            {
                message = "Console log access requires additional setup",
                tip = "Use Application.logMessageReceived to capture logs"
            };
        }

        private static object ExecuteMenu(Dictionary<string, object> p)
        {
            if (!p.TryGetValue("path", out var pathObj) || pathObj == null)
            {
                throw new ArgumentException("Missing 'path' parameter");
            }

            var path = pathObj.ToString();
            var success = EditorApplication.ExecuteMenuItem(path);

            return new { success, path };
        }

        private static object SetPlayMode(Dictionary<string, object> p)
        {
            if (!p.TryGetValue("play", out var playObj))
            {
                throw new ArgumentException("Missing 'play' parameter");
            }

            var play = Convert.ToBoolean(playObj);
            EditorApplication.isPlaying = play;

            return new
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused
            };
        }

        private static object SetMaterialColor(Dictionary<string, object> p)
        {
            // Get the target - either by name/path or use current selection
            GameObject target = null;

            if (p.TryGetValue("name", out var nameObj) && nameObj != null)
            {
                target = GameObject.Find(nameObj.ToString());
            }
            else if (Selection.activeGameObject != null)
            {
                target = Selection.activeGameObject;
            }

            if (target == null)
            {
                throw new ArgumentException("No target found. Provide 'name' parameter or select a GameObject.");
            }

            var renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                throw new ArgumentException($"GameObject '{target.name}' has no Renderer component");
            }

            // Parse color - support r,g,b or r,g,b,a (0-1 range) or hex
            Color color = Color.white;

            if (p.TryGetValue("color", out var colorObj) && colorObj != null)
            {
                var colorStr = colorObj.ToString();

                // Try hex format (#RRGGBB or RRGGBB)
                if (colorStr.StartsWith("#") || colorStr.Length == 6)
                {
                    var hex = colorStr.TrimStart('#');
                    if (ColorUtility.TryParseHtmlString("#" + hex, out var parsedColor))
                    {
                        color = parsedColor;
                    }
                }
            }
            else
            {
                // Try individual r, g, b, a parameters
                float r = p.TryGetValue("r", out var rObj) ? Convert.ToSingle(rObj) : 1f;
                float g = p.TryGetValue("g", out var gObj) ? Convert.ToSingle(gObj) : 1f;
                float b = p.TryGetValue("b", out var bObj) ? Convert.ToSingle(bObj) : 1f;
                float a = p.TryGetValue("a", out var aObj) ? Convert.ToSingle(aObj) : 1f;
                color = new Color(r, g, b, a);
            }

            // Create a new material instance to avoid modifying shared materials
            var mat = new Material(Shader.Find("Standard"));
            mat.color = color;

            Undo.RecordObject(renderer, "Set Material Color");
            renderer.sharedMaterial = mat;

            return new
            {
                gameObject = target.name,
                color = new { r = color.r, g = color.g, b = color.b, a = color.a },
                colorHex = ColorUtility.ToHtmlStringRGBA(color)
            };
        }

        private static object SelectGameObject(Dictionary<string, object> p)
        {
            if (!p.TryGetValue("name", out var nameObj) || nameObj == null)
            {
                throw new ArgumentException("Missing 'name' parameter");
            }

            var target = GameObject.Find(nameObj.ToString());
            if (target == null)
            {
                throw new ArgumentException($"GameObject '{nameObj}' not found");
            }

            Selection.activeGameObject = target;
            EditorGUIUtility.PingObject(target);

            return new
            {
                selected = target.name,
                path = GetGameObjectPath(target)
            };
        }

        private static object StartRotation(Dictionary<string, object> p)
        {
            GameObject target = null;

            if (p.TryGetValue("name", out var nameObj) && nameObj != null)
            {
                target = GameObject.Find(nameObj.ToString());
            }
            else if (Selection.activeGameObject != null)
            {
                target = Selection.activeGameObject;
            }

            if (target == null)
            {
                throw new ArgumentException("No target found. Provide 'name' parameter or select a GameObject.");
            }

            // Parse rotation speed (degrees per second)
            float x = p.TryGetValue("x", out var xObj) ? Convert.ToSingle(xObj) : 0f;
            float y = p.TryGetValue("y", out var yObj) ? Convert.ToSingle(yObj) : 45f; // Default Y rotation
            float z = p.TryGetValue("z", out var zObj) ? Convert.ToSingle(zObj) : 0f;

            var speed = new Vector3(x, y, z);
            _rotatingObjects[target.GetInstanceID()] = (target, speed);

            // Hook update if not already
            if (!_rotationUpdateHooked)
            {
                _rotationUpdateHooked = true;
                EditorApplication.update += UpdateRotations;
            }

            return new
            {
                gameObject = target.name,
                rotationSpeed = new { x, y, z },
                status = "rotating"
            };
        }

        private static object StopRotation(Dictionary<string, object> p)
        {
            GameObject target = null;

            if (p.TryGetValue("name", out var nameObj) && nameObj != null)
            {
                target = GameObject.Find(nameObj.ToString());
            }
            else if (Selection.activeGameObject != null)
            {
                target = Selection.activeGameObject;
            }

            if (p.TryGetValue("all", out var allObj) && Convert.ToBoolean(allObj))
            {
                var count = _rotatingObjects.Count;
                _rotatingObjects.Clear();
                return new { stopped = count, status = "all rotations stopped" };
            }

            if (target == null)
            {
                throw new ArgumentException("No target found. Provide 'name' parameter or select a GameObject.");
            }

            var id = target.GetInstanceID();
            if (_rotatingObjects.Remove(id))
            {
                return new { gameObject = target.name, status = "stopped" };
            }

            return new { gameObject = target.name, status = "was not rotating" };
        }

        private static double _lastRotationTime;

        private static void UpdateRotations()
        {
            if (_rotatingObjects.Count == 0) return;

            var currentTime = EditorApplication.timeSinceStartup;
            var deltaTime = (float)(currentTime - _lastRotationTime);
            _lastRotationTime = currentTime;

            // Clamp delta to avoid huge jumps
            if (deltaTime > 0.1f) deltaTime = 0.016f;

            var toRemove = new List<int>();

            foreach (var kvp in _rotatingObjects)
            {
                var (go, speed) = kvp.Value;
                if (go == null)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                go.transform.Rotate(speed * deltaTime, Space.World);
            }

            foreach (var id in toRemove)
            {
                _rotatingObjects.Remove(id);
            }

            // Force scene view repaint
            SceneView.RepaintAll();
        }

        private static object StartOrbit(Dictionary<string, object> p)
        {
            GameObject target = null;

            if (p.TryGetValue("name", out var nameObj) && nameObj != null)
            {
                target = GameObject.Find(nameObj.ToString());
            }
            else if (Selection.activeGameObject != null)
            {
                target = Selection.activeGameObject;
            }

            if (target == null)
            {
                throw new ArgumentException("No target found. Provide 'name' parameter or select a GameObject.");
            }

            // Parse orbit parameters
            float radius = p.TryGetValue("radius", out var rObj) ? Convert.ToSingle(rObj) : 3f;
            float speed = p.TryGetValue("speed", out var sObj) ? Convert.ToSingle(sObj) : 45f; // degrees per second

            // Center point - default to current position or origin
            Vector3 center = Vector3.zero;
            if (p.TryGetValue("center_x", out var cx)) center.x = Convert.ToSingle(cx);
            if (p.TryGetValue("center_y", out var cy)) center.y = Convert.ToSingle(cy);
            if (p.TryGetValue("center_z", out var cz)) center.z = Convert.ToSingle(cz);

            // Calculate starting angle from current position
            var offset = target.transform.position - center;
            float startAngle = Mathf.Atan2(offset.z, offset.x) * Mathf.Rad2Deg;

            _orbitingObjects[target.GetInstanceID()] = (target, center, radius, speed, startAngle);

            // Hook update if not already
            if (!_orbitUpdateHooked)
            {
                _orbitUpdateHooked = true;
                EditorApplication.update += UpdateOrbits;
            }

            return new
            {
                gameObject = target.name,
                center = new { x = center.x, y = center.y, z = center.z },
                radius,
                speed,
                status = "orbiting"
            };
        }

        private static object StopOrbit(Dictionary<string, object> p)
        {
            GameObject target = null;

            if (p.TryGetValue("name", out var nameObj) && nameObj != null)
            {
                target = GameObject.Find(nameObj.ToString());
            }
            else if (Selection.activeGameObject != null)
            {
                target = Selection.activeGameObject;
            }

            if (p.TryGetValue("all", out var allObj) && Convert.ToBoolean(allObj))
            {
                var count = _orbitingObjects.Count;
                _orbitingObjects.Clear();
                return new { stopped = count, status = "all orbits stopped" };
            }

            if (target == null)
            {
                throw new ArgumentException("No target found. Provide 'name' parameter or select a GameObject.");
            }

            var id = target.GetInstanceID();
            if (_orbitingObjects.Remove(id))
            {
                return new { gameObject = target.name, status = "stopped" };
            }

            return new { gameObject = target.name, status = "was not orbiting" };
        }

        private static double _lastOrbitTime;

        private static void UpdateOrbits()
        {
            if (_orbitingObjects.Count == 0) return;

            var currentTime = EditorApplication.timeSinceStartup;
            var deltaTime = (float)(currentTime - _lastOrbitTime);
            _lastOrbitTime = currentTime;

            if (deltaTime > 0.1f) deltaTime = 0.016f;

            var toRemove = new List<int>();
            var updates = new Dictionary<int, (GameObject go, Vector3 center, float radius, float speed, float angle)>();

            foreach (var kvp in _orbitingObjects)
            {
                var (go, center, radius, speed, angle) = kvp.Value;
                if (go == null)
                {
                    toRemove.Add(kvp.Key);
                    continue;
                }

                // Update angle
                float newAngle = angle + speed * deltaTime;

                // Calculate new position
                float rad = newAngle * Mathf.Deg2Rad;
                float x = center.x + radius * Mathf.Cos(rad);
                float z = center.z + radius * Mathf.Sin(rad);

                go.transform.position = new Vector3(x, go.transform.position.y, z);

                updates[kvp.Key] = (go, center, radius, speed, newAngle);
            }

            foreach (var kvp in updates)
            {
                _orbitingObjects[kvp.Key] = kvp.Value;
            }

            foreach (var id in toRemove)
            {
                _orbitingObjects.Remove(id);
            }

            SceneView.RepaintAll();
        }

        private static object InstallPackage(Dictionary<string, object> p)
        {
            if (!p.TryGetValue("package", out var packageObj) || packageObj == null)
            {
                throw new ArgumentException("Missing 'package' parameter (e.g., 'com.unity.xr.arkit' or git URL)");
            }

            var packageId = packageObj.ToString();

            // Start the add request
            var request = Client.Add(packageId);

            // Wait for completion (with timeout)
            var startTime = DateTime.Now;
            while (!request.IsCompleted)
            {
                if ((DateTime.Now - startTime).TotalSeconds > 60)
                {
                    return new { status = "timeout", package = packageId, message = "Package installation timed out after 60s" };
                }
                System.Threading.Thread.Sleep(100);
            }

            if (request.Status == StatusCode.Success)
            {
                var info = request.Result;
                return new
                {
                    status = "installed",
                    package = info.packageId,
                    version = info.version,
                    displayName = info.displayName
                };
            }
            else
            {
                return new { status = "error", package = packageId, error = request.Error?.message ?? "Unknown error" };
            }
        }

        private static object GetPackages(Dictionary<string, object> p)
        {
            var request = Client.List(true); // include dependencies

            var startTime = DateTime.Now;
            while (!request.IsCompleted)
            {
                if ((DateTime.Now - startTime).TotalSeconds > 30)
                {
                    throw new Exception("Package list request timed out");
                }
                System.Threading.Thread.Sleep(100);
            }

            if (request.Status != StatusCode.Success)
            {
                throw new Exception(request.Error?.message ?? "Failed to list packages");
            }

            var packages = request.Result.Select(pkg => new
            {
                name = pkg.name,
                version = pkg.version,
                displayName = pkg.displayName,
                source = pkg.source.ToString()
            }).ToArray();

            return new { count = packages.Length, packages };
        }

        private static object OpenSettings(Dictionary<string, object> p)
        {
            var path = p.TryGetValue("path", out var pathObj) ? pathObj?.ToString() : "Project/XR Plug-in Management";

            SettingsService.OpenProjectSettings(path);

            return new { opened = path };
        }

        private static object GetPlayerSettings(Dictionary<string, object> p)
        {
            var platform = p.TryGetValue("platform", out var platObj) ? platObj?.ToString()?.ToLower() : "ios";

            var settings = new Dictionary<string, object>
            {
                ["companyName"] = PlayerSettings.companyName,
                ["productName"] = PlayerSettings.productName,
                ["bundleVersion"] = PlayerSettings.bundleVersion,
            };

            if (platform == "ios")
            {
                settings["bundleIdentifier"] = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS);
                settings["targetOSVersionString"] = PlayerSettings.iOS.targetOSVersionString;
                settings["cameraUsageDescription"] = PlayerSettings.iOS.cameraUsageDescription;
                settings["locationUsageDescription"] = PlayerSettings.iOS.locationUsageDescription;
                // requiresARKitSupport removed in Unity 6 - use XR Plugin Management instead
                settings["appleEnableAutomaticSigning"] = PlayerSettings.iOS.appleEnableAutomaticSigning;
                settings["appleDeveloperTeamID"] = PlayerSettings.iOS.appleDeveloperTeamID;
            }
            else if (platform == "android")
            {
                settings["bundleIdentifier"] = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
                settings["minSdkVersion"] = PlayerSettings.Android.minSdkVersion.ToString();
                settings["targetSdkVersion"] = PlayerSettings.Android.targetSdkVersion.ToString();
            }

            return settings;
        }

        private static object SetPlayerSettings(Dictionary<string, object> p)
        {
            var platform = p.TryGetValue("platform", out var platObj) ? platObj?.ToString()?.ToLower() : "ios";
            var changes = new List<string>();

            // Common settings
            if (p.TryGetValue("companyName", out var company) && company != null)
            {
                PlayerSettings.companyName = company.ToString();
                changes.Add("companyName");
            }

            if (p.TryGetValue("productName", out var product) && product != null)
            {
                PlayerSettings.productName = product.ToString();
                changes.Add("productName");
            }

            if (p.TryGetValue("bundleVersion", out var version) && version != null)
            {
                PlayerSettings.bundleVersion = version.ToString();
                changes.Add("bundleVersion");
            }

            if (p.TryGetValue("bundleIdentifier", out var bundleId) && bundleId != null)
            {
                var namedTarget = platform == "android"
                    ? UnityEditor.Build.NamedBuildTarget.Android
                    : UnityEditor.Build.NamedBuildTarget.iOS;
                PlayerSettings.SetApplicationIdentifier(namedTarget, bundleId.ToString());
                changes.Add("bundleIdentifier");
            }

            // iOS specific
            if (platform == "ios")
            {
                if (p.TryGetValue("targetOSVersion", out var osVer) && osVer != null)
                {
                    PlayerSettings.iOS.targetOSVersionString = osVer.ToString();
                    changes.Add("targetOSVersion");
                }

                if (p.TryGetValue("cameraUsageDescription", out var camDesc) && camDesc != null)
                {
                    PlayerSettings.iOS.cameraUsageDescription = camDesc.ToString();
                    changes.Add("cameraUsageDescription");
                }

                if (p.TryGetValue("locationUsageDescription", out var locDesc) && locDesc != null)
                {
                    PlayerSettings.iOS.locationUsageDescription = locDesc.ToString();
                    changes.Add("locationUsageDescription");
                }

                // requiresARKitSupport removed in Unity 6 - use XR Plugin Management instead

                if (p.TryGetValue("appleEnableAutomaticSigning", out var autoSign))
                {
                    PlayerSettings.iOS.appleEnableAutomaticSigning = Convert.ToBoolean(autoSign);
                    changes.Add("appleEnableAutomaticSigning");
                }

                if (p.TryGetValue("appleDeveloperTeamID", out var teamId) && teamId != null)
                {
                    PlayerSettings.iOS.appleDeveloperTeamID = teamId.ToString();
                    changes.Add("appleDeveloperTeamID");
                }
            }

            // Android specific
            if (platform == "android")
            {
                if (p.TryGetValue("minSdkVersion", out var minSdk) && minSdk != null)
                {
                    if (Enum.TryParse<AndroidSdkVersions>(minSdk.ToString(), out var sdkEnum))
                    {
                        PlayerSettings.Android.minSdkVersion = sdkEnum;
                        changes.Add("minSdkVersion");
                    }
                }
            }

            return new { platform, changed = changes.ToArray(), count = changes.Count };
        }

        private static object GetBuildTarget(Dictionary<string, object> p)
        {
            return new
            {
                activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
                selectedBuildTargetGroup = EditorUserBuildSettings.selectedBuildTargetGroup.ToString(),
                development = EditorUserBuildSettings.development,
                buildAppBundle = EditorUserBuildSettings.buildAppBundle
            };
        }

        private static object SetBuildTarget(Dictionary<string, object> p)
        {
            if (!p.TryGetValue("target", out var targetObj) || targetObj == null)
            {
                throw new ArgumentException("Missing 'target' parameter (e.g., 'iOS', 'Android', 'StandaloneOSX')");
            }

            var targetStr = targetObj.ToString();
            BuildTarget target;
            BuildTargetGroup group;

            switch (targetStr.ToLower())
            {
                case "ios":
                    target = BuildTarget.iOS;
                    group = BuildTargetGroup.iOS;
                    break;
                case "android":
                    target = BuildTarget.Android;
                    group = BuildTargetGroup.Android;
                    break;
                case "standaloneosx":
                case "macos":
                case "osx":
                    target = BuildTarget.StandaloneOSX;
                    group = BuildTargetGroup.Standalone;
                    break;
                case "standalonewindows64":
                case "windows":
                    target = BuildTarget.StandaloneWindows64;
                    group = BuildTargetGroup.Standalone;
                    break;
                case "webgl":
                    target = BuildTarget.WebGL;
                    group = BuildTargetGroup.WebGL;
                    break;
                default:
                    throw new ArgumentException($"Unknown build target: {targetStr}");
            }

            var success = EditorUserBuildSettings.SwitchActiveBuildTarget(group, target);

            return new
            {
                success,
                target = target.ToString(),
                group = group.ToString()
            };
        }

        private static object SetMultiSetConfig(Dictionary<string, object> p)
        {
            if (!p.TryGetValue("clientId", out var clientIdObj) || clientIdObj == null)
            {
                throw new ArgumentException("Missing 'clientId' parameter");
            }
            if (!p.TryGetValue("clientSecret", out var clientSecretObj) || clientSecretObj == null)
            {
                throw new ArgumentException("Missing 'clientSecret' parameter");
            }

            var clientId = clientIdObj.ToString();
            var clientSecret = clientSecretObj.ToString();

            // Ensure Resources folder exists
            var resourcesPath = "Assets/Resources";
            if (!AssetDatabase.IsValidFolder(resourcesPath))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            var configPath = "Assets/Resources/MultiSetConfig.asset";

            // Try to find existing config
            var config = AssetDatabase.LoadAssetAtPath<ScriptableObject>(configPath);

            if (config == null)
            {
                // Try to find MultiSetConfig type
                var configType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => t.Name == "MultiSetConfig" && typeof(ScriptableObject).IsAssignableFrom(t));

                if (configType == null)
                {
                    throw new Exception("MultiSetConfig type not found. Is the MultiSet SDK installed?");
                }

                config = ScriptableObject.CreateInstance(configType);
                AssetDatabase.CreateAsset(config, configPath);
            }

            // Set the fields using reflection
            var clientIdField = config.GetType().GetField("clientId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var clientSecretField = config.GetType().GetField("clientSecret", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            if (clientIdField != null)
            {
                clientIdField.SetValue(config, clientId);
            }
            if (clientSecretField != null)
            {
                clientSecretField.SetValue(config, clientSecret);
            }

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new
            {
                path = configPath,
                clientIdSet = clientIdField != null,
                clientSecretSet = clientSecretField != null,
                message = "MultiSet config updated"
            };
        }

        private static object GetMultiSetConfig(Dictionary<string, object> p)
        {
            var configPath = "Assets/Resources/MultiSetConfig.asset";
            var config = AssetDatabase.LoadAssetAtPath<ScriptableObject>(configPath);

            if (config == null)
            {
                return new { exists = false, path = configPath };
            }

            var clientIdField = config.GetType().GetField("clientId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var clientSecretField = config.GetType().GetField("clientSecret", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var clientId = clientIdField?.GetValue(config)?.ToString() ?? "";
            var clientSecret = clientSecretField?.GetValue(config)?.ToString() ?? "";

            return new
            {
                exists = true,
                path = configPath,
                clientId = !string.IsNullOrEmpty(clientId) ? clientId.Substring(0, Math.Min(8, clientId.Length)) + "..." : "(empty)",
                clientSecretSet = !string.IsNullOrEmpty(clientSecret)
            };
        }

        private static object CreateScriptableObject(Dictionary<string, object> p)
        {
            if (!p.TryGetValue("type", out var typeObj) || typeObj == null)
            {
                throw new ArgumentException("Missing 'type' parameter");
            }
            if (!p.TryGetValue("path", out var pathObj) || pathObj == null)
            {
                throw new ArgumentException("Missing 'path' parameter (e.g., 'Assets/Resources/MyConfig.asset')");
            }

            var typeName = typeObj.ToString();
            var path = pathObj.ToString();

            // Ensure directory exists
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !AssetDatabase.IsValidFolder(dir))
            {
                var parts = dir.Split('/');
                var current = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    var next = current + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(current, parts[i]);
                    }
                    current = next;
                }
            }

            // Find the type
            var soType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                .FirstOrDefault(t => t.Name == typeName && typeof(ScriptableObject).IsAssignableFrom(t));

            if (soType == null)
            {
                throw new Exception($"ScriptableObject type '{typeName}' not found");
            }

            var instance = ScriptableObject.CreateInstance(soType);
            AssetDatabase.CreateAsset(instance, path);
            AssetDatabase.SaveAssets();

            return new
            {
                created = true,
                path,
                type = soType.FullName
            };
        }

        private static object VerifyMultiSetSdk(Dictionary<string, object> p)
        {
            var result = new Dictionary<string, object>();

            // Check if MultiSet package is installed
            var multisetPackage = AssetDatabase.FindAssets("t:asmdef", new[] { "Packages/com.multiset.sdk" });
            result["packageInstalled"] = multisetPackage.Length > 0;

            // Check for MultiSetConfig
            var config = Resources.Load("MultiSetConfig");
            result["configExists"] = config != null;

            if (config != null)
            {
                var clientIdField = config.GetType().GetField("clientId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var clientSecretField = config.GetType().GetField("clientSecret", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                var clientId = clientIdField?.GetValue(config)?.ToString() ?? "";
                var clientSecret = clientSecretField?.GetValue(config)?.ToString() ?? "";

                result["hasClientId"] = !string.IsNullOrEmpty(clientId);
                result["hasClientSecret"] = !string.IsNullOrEmpty(clientSecret);
                result["configValid"] = !string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret);
            }

            // Check for MultiSet types
            var multisetTypes = new[] { "MultisetSdkManager", "MapLocalizationManager", "MultiSetConfig" };
            var foundTypes = new List<string>();

            foreach (var typeName in multisetTypes)
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => t.Name == typeName);
                if (type != null)
                {
                    foundTypes.Add(typeName);
                }
            }
            result["multisetTypes"] = foundTypes;
            result["sdkReady"] = foundTypes.Count >= 2 && (bool)(result["configValid"] ?? false);

            return result;
        }

        private static object ImportMultiSetSamples(Dictionary<string, object> p)
        {
            var sampleName = p.TryGetValue("sample", out var s) ? s?.ToString() : null;

            // Find sample scenes in the MultiSet package
            var packagePath = "Packages/com.multiset.sdk/Samples~/Scenes";
            var samples = new List<string>();

            try
            {
                if (System.IO.Directory.Exists(packagePath))
                {
                    var dirs = System.IO.Directory.GetDirectories(packagePath);
                    foreach (var dir in dirs)
                    {
                        var name = System.IO.Path.GetFileName(dir);
                        if (!name.EndsWith(".meta"))
                        {
                            samples.Add(name);
                        }
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(sampleName))
            {
                return new { availableSamples = samples };
            }

            // Copy sample to Assets
            var sourcePath = $"{packagePath}/{sampleName}";
            var destPath = $"Assets/MultiSetSamples/{sampleName}";

            if (!System.IO.Directory.Exists(sourcePath))
            {
                throw new Exception($"Sample '{sampleName}' not found");
            }

            if (!System.IO.Directory.Exists("Assets/MultiSetSamples"))
            {
                AssetDatabase.CreateFolder("Assets", "MultiSetSamples");
            }

            FileUtil.CopyFileOrDirectory(sourcePath, destPath);
            AssetDatabase.Refresh();

            return new
            {
                imported = true,
                sample = sampleName,
                path = destPath
            };
        }

        private static object CheckMultiSetScene(Dictionary<string, object> p)
        {
            var components = new Dictionary<string, bool>();
            var componentTypes = new[]
            {
                "MultisetSdkManager",
                "MapLocalizationManager",
                "SingleFrameLocalizationManager",
                "OnDeviceLocalizationManager",
                "MapMeshDownloader",
                "ModelsetMeshDownloader",
                "SimulatorModeController"
            };

            foreach (var typeName in componentTypes)
            {
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => t.Name == typeName && typeof(Component).IsAssignableFrom(t));

                if (type != null)
                {
                    var found = UnityEngine.Object.FindFirstObjectByType(type) != null;
                    components[typeName] = found;
                }
                else
                {
                    components[typeName] = false;
                }
            }

            var hasAnyMultiSet = components.Values.Any(v => v);

            return new
            {
                components,
                hasMultiSetComponents = hasAnyMultiSet,
                sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
            };
        }

        private static object DeleteGameObject(Dictionary<string, object> p)
        {
            var name = p.TryGetValue("name", out var n) ? n?.ToString() : null;
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Missing 'name' parameter");

            var go = GameObject.Find(name);
            if (go == null)
                throw new Exception($"GameObject '{name}' not found");

            Undo.DestroyObjectImmediate(go);
            return new { deleted = true, name };
        }

        private static object RenameGameObject(Dictionary<string, object> p)
        {
            var name = p.TryGetValue("name", out var n) ? n?.ToString() : null;
            var newName = p.TryGetValue("newName", out var nn) ? nn?.ToString() : null;

            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Missing 'name' parameter");
            if (string.IsNullOrEmpty(newName))
                throw new ArgumentException("Missing 'newName' parameter");

            var go = GameObject.Find(name);
            if (go == null)
                throw new Exception($"GameObject '{name}' not found");

            Undo.RecordObject(go, "Rename GameObject");
            go.name = newName;
            return new { renamed = true, oldName = name, newName };
        }

        private static object SetTransform(Dictionary<string, object> p)
        {
            var name = p.TryGetValue("name", out var n) ? n?.ToString() : null;
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Missing 'name' parameter");

            var go = GameObject.Find(name);
            if (go == null)
                throw new Exception($"GameObject '{name}' not found");

            Undo.RecordObject(go.transform, "Set Transform");

            // Position
            if (p.TryGetValue("x", out var px)) go.transform.position = new Vector3(Convert.ToSingle(px), go.transform.position.y, go.transform.position.z);
            if (p.TryGetValue("y", out var py)) go.transform.position = new Vector3(go.transform.position.x, Convert.ToSingle(py), go.transform.position.z);
            if (p.TryGetValue("z", out var pz)) go.transform.position = new Vector3(go.transform.position.x, go.transform.position.y, Convert.ToSingle(pz));

            if (p.TryGetValue("position", out var posObj) && posObj is Dictionary<string, object> pos)
            {
                var x = pos.TryGetValue("x", out var posX) ? Convert.ToSingle(posX) : go.transform.position.x;
                var y = pos.TryGetValue("y", out var posY) ? Convert.ToSingle(posY) : go.transform.position.y;
                var z = pos.TryGetValue("z", out var posZ) ? Convert.ToSingle(posZ) : go.transform.position.z;
                go.transform.position = new Vector3(x, y, z);
            }

            // Rotation (euler angles)
            if (p.TryGetValue("rx", out var rx)) go.transform.eulerAngles = new Vector3(Convert.ToSingle(rx), go.transform.eulerAngles.y, go.transform.eulerAngles.z);
            if (p.TryGetValue("ry", out var ry)) go.transform.eulerAngles = new Vector3(go.transform.eulerAngles.x, Convert.ToSingle(ry), go.transform.eulerAngles.z);
            if (p.TryGetValue("rz", out var rz)) go.transform.eulerAngles = new Vector3(go.transform.eulerAngles.x, go.transform.eulerAngles.y, Convert.ToSingle(rz));

            if (p.TryGetValue("rotation", out var rotObj) && rotObj is Dictionary<string, object> rot)
            {
                var x = rot.TryGetValue("x", out var rotX) ? Convert.ToSingle(rotX) : go.transform.eulerAngles.x;
                var y = rot.TryGetValue("y", out var rotY) ? Convert.ToSingle(rotY) : go.transform.eulerAngles.y;
                var z = rot.TryGetValue("z", out var rotZ) ? Convert.ToSingle(rotZ) : go.transform.eulerAngles.z;
                go.transform.eulerAngles = new Vector3(x, y, z);
            }

            // Scale
            if (p.TryGetValue("sx", out var sx)) go.transform.localScale = new Vector3(Convert.ToSingle(sx), go.transform.localScale.y, go.transform.localScale.z);
            if (p.TryGetValue("sy", out var sy)) go.transform.localScale = new Vector3(go.transform.localScale.x, Convert.ToSingle(sy), go.transform.localScale.z);
            if (p.TryGetValue("sz", out var sz)) go.transform.localScale = new Vector3(go.transform.localScale.x, go.transform.localScale.y, Convert.ToSingle(sz));

            if (p.TryGetValue("scale", out var scaleObj))
            {
                if (scaleObj is Dictionary<string, object> scale)
                {
                    var x = scale.TryGetValue("x", out var scaleX) ? Convert.ToSingle(scaleX) : go.transform.localScale.x;
                    var y = scale.TryGetValue("y", out var scaleY) ? Convert.ToSingle(scaleY) : go.transform.localScale.y;
                    var z = scale.TryGetValue("z", out var scaleZ) ? Convert.ToSingle(scaleZ) : go.transform.localScale.z;
                    go.transform.localScale = new Vector3(x, y, z);
                }
                else
                {
                    var uniformScale = Convert.ToSingle(scaleObj);
                    go.transform.localScale = new Vector3(uniformScale, uniformScale, uniformScale);
                }
            }

            return new
            {
                name,
                position = new { x = go.transform.position.x, y = go.transform.position.y, z = go.transform.position.z },
                rotation = new { x = go.transform.eulerAngles.x, y = go.transform.eulerAngles.y, z = go.transform.eulerAngles.z },
                scale = new { x = go.transform.localScale.x, y = go.transform.localScale.y, z = go.transform.localScale.z }
            };
        }

        private static object GetTransform(Dictionary<string, object> p)
        {
            var name = p.TryGetValue("name", out var n) ? n?.ToString() : null;
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Missing 'name' parameter");

            var go = GameObject.Find(name);
            if (go == null)
                throw new Exception($"GameObject '{name}' not found");

            return new
            {
                name = go.name,
                position = new { x = go.transform.position.x, y = go.transform.position.y, z = go.transform.position.z },
                localPosition = new { x = go.transform.localPosition.x, y = go.transform.localPosition.y, z = go.transform.localPosition.z },
                rotation = new { x = go.transform.eulerAngles.x, y = go.transform.eulerAngles.y, z = go.transform.eulerAngles.z },
                localRotation = new { x = go.transform.localEulerAngles.x, y = go.transform.localEulerAngles.y, z = go.transform.localEulerAngles.z },
                scale = new { x = go.transform.localScale.x, y = go.transform.localScale.y, z = go.transform.localScale.z },
                parent = go.transform.parent?.name
            };
        }

        private static object FindGameObject(Dictionary<string, object> p)
        {
            var name = p.TryGetValue("name", out var n) ? n?.ToString() : null;
            var tag = p.TryGetValue("tag", out var t) ? t?.ToString() : null;
            var contains = p.TryGetValue("contains", out var c) ? c?.ToString() : null;

            var results = new List<object>();

            if (!string.IsNullOrEmpty(name))
            {
                var go = GameObject.Find(name);
                if (go != null)
                {
                    results.Add(new { name = go.name, path = GetGameObjectPath(go) });
                }
            }
            else if (!string.IsNullOrEmpty(tag))
            {
                var objects = GameObject.FindGameObjectsWithTag(tag);
                foreach (var go in objects)
                {
                    results.Add(new { name = go.name, path = GetGameObjectPath(go) });
                }
            }
            else if (!string.IsNullOrEmpty(contains))
            {
                var allObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                foreach (var go in allObjects)
                {
                    if (go.name.Contains(contains, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(new { name = go.name, path = GetGameObjectPath(go) });
                    }
                }
            }
            else
            {
                throw new ArgumentException("Provide 'name', 'tag', or 'contains' parameter");
            }

            return new { count = results.Count, objects = results };
        }

        private static object DuplicateGameObject(Dictionary<string, object> p)
        {
            var name = p.TryGetValue("name", out var n) ? n?.ToString() : null;
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Missing 'name' parameter");

            var go = GameObject.Find(name);
            if (go == null)
                throw new Exception($"GameObject '{name}' not found");

            var duplicate = UnityEngine.Object.Instantiate(go);
            duplicate.name = go.name + " (Copy)";
            Undo.RegisterCreatedObjectUndo(duplicate, "Duplicate GameObject");

            if (p.TryGetValue("newName", out var newName) && newName != null)
            {
                duplicate.name = newName.ToString();
            }

            return new
            {
                duplicated = true,
                originalName = go.name,
                newName = duplicate.name,
                instanceId = duplicate.GetInstanceID()
            };
        }

        private static object SetParent(Dictionary<string, object> p)
        {
            var name = p.TryGetValue("name", out var n) ? n?.ToString() : null;
            var parentName = p.TryGetValue("parent", out var pn) ? pn?.ToString() : null;

            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Missing 'name' parameter");

            var go = GameObject.Find(name);
            if (go == null)
                throw new Exception($"GameObject '{name}' not found");

            Undo.SetTransformParent(go.transform, null, "Set Parent");

            if (!string.IsNullOrEmpty(parentName))
            {
                var parent = GameObject.Find(parentName);
                if (parent == null)
                    throw new Exception($"Parent GameObject '{parentName}' not found");

                Undo.SetTransformParent(go.transform, parent.transform, "Set Parent");
            }

            return new
            {
                name,
                parent = go.transform.parent?.name ?? "(root)"
            };
        }

        #endregion

        #region Helpers

        private static CommandData ParseCommand(string json)
        {
            try
            {
                // Simple JSON parsing without external dependencies
                var data = new CommandData();

                // Extract type
                var typeMatch = System.Text.RegularExpressions.Regex.Match(json, @"""type""\s*:\s*""([^""]+)""");
                if (typeMatch.Success)
                {
                    data.type = typeMatch.Groups[1].Value;
                }

                // Extract params (simplified - works for basic cases)
                var paramsMatch = System.Text.RegularExpressions.Regex.Match(json, @"""params""\s*:\s*\{([^}]*)\}");
                if (paramsMatch.Success)
                {
                    data.@params = ParseSimpleParams(paramsMatch.Groups[1].Value);
                }

                return data;
            }
            catch
            {
                return null;
            }
        }

        private static Dictionary<string, object> ParseSimpleParams(string paramsContent)
        {
            var result = new Dictionary<string, object>();
            var pairs = System.Text.RegularExpressions.Regex.Matches(paramsContent, @"""([^""]+)""\s*:\s*(""[^""]*""|true|false|-?\d+\.?\d*)");

            foreach (System.Text.RegularExpressions.Match pair in pairs)
            {
                var key = pair.Groups[1].Value;
                var valueStr = pair.Groups[2].Value;

                object value;
                if (valueStr.StartsWith("\""))
                    value = valueStr.Trim('"');
                else if (valueStr == "true")
                    value = true;
                else if (valueStr == "false")
                    value = false;
                else if (double.TryParse(valueStr, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var numVal))
                    value = numVal;
                else
                    value = valueStr;

                result[key] = value;
            }

            return result;
        }

        private static string SuccessResponse(object result)
        {
            return $"{{\"status\":\"success\",\"result\":{SerializeObject(result)}}}";
        }

        private static string ErrorResponse(string message)
        {
            return $"{{\"status\":\"error\",\"error\":\"{EscapeJson(message)}\"}}";
        }

        private static string SerializeObject(object obj)
        {
            if (obj == null) return "null";

            // Simple serialization for common types
            if (obj is string s) return $"\"{EscapeJson(s)}\"";
            if (obj is bool b) return b ? "true" : "false";
            if (obj is int || obj is long || obj is float || obj is double) return obj.ToString();

            if (obj is System.Collections.IEnumerable enumerable && !(obj is string))
            {
                var items = new List<string>();
                foreach (var item in enumerable)
                {
                    items.Add(SerializeObject(item));
                }
                return "[" + string.Join(",", items) + "]";
            }

            // Handle anonymous types and dictionaries
            var props = new List<string>();
            foreach (var prop in obj.GetType().GetProperties())
            {
                try
                {
                    var value = prop.GetValue(obj);
                    props.Add($"\"{prop.Name}\":{SerializeObject(value)}");
                }
                catch { }
            }

            if (obj is System.Collections.IDictionary dict)
            {
                foreach (System.Collections.DictionaryEntry entry in dict)
                {
                    props.Add($"\"{entry.Key}\":{SerializeObject(entry.Value)}");
                }
            }

            return "{" + string.Join(",", props) + "}";
        }

        private static string EscapeJson(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static string ToSnakeCase(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            var result = System.Text.RegularExpressions.Regex.Replace(name, "(.)([A-Z][a-z]+)", "$1_$2");
            result = System.Text.RegularExpressions.Regex.Replace(result, "([a-z0-9])([A-Z])", "$1_$2");
            return result.ToLower();
        }

        #endregion

        private class CommandData
        {
            public string type;
            public Dictionary<string, object> @params;
        }
    }

    /// <summary>
    /// Mark a class as a bridge command handler.
    /// The class must have a static HandleCommand(Dictionary&lt;string, object&gt;) method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class BridgeCommandAttribute : Attribute
    {
        public string Name { get; }

        public BridgeCommandAttribute(string name = null)
        {
            Name = name;
        }
    }
}
