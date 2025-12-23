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
            var pairs = System.Text.RegularExpressions.Regex.Matches(paramsContent, @"""([^""]+)""\s*:\s*(""[^""]*""|true|false|\d+)");

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
                else if (int.TryParse(valueStr, out var intVal))
                    value = intVal;
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
