using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace UnityBridgeLite
{
    /// <summary>
    /// TCP socket server that auto-starts when Unity Editor loads.
    /// Uses simple newline-delimited JSON for minimal overhead.
    /// </summary>
    [InitializeOnLoad]
    public static class BridgeServer
    {
        private static TcpListener _listener;
        private static CancellationTokenSource _cts;
        private static int _port = 6400;
        private static bool _isRunning;
        private static int _clientCount;

        // Event log for UI
        public static readonly ConcurrentQueue<LogEntry> EventLog = new();
        public static event Action OnLogUpdated;
        public static event Action OnClientCountChanged;

        public static int Port => _port;
        public static bool IsRunning => _isRunning;
        public static int ClientCount => _clientCount;

        static BridgeServer()
        {
            EditorApplication.delayCall += () =>
            {
                if (!_isRunning)
                {
                    Start();
                }
            };

            EditorApplication.quitting += Stop;
            AssemblyReloadEvents.beforeAssemblyReload += Stop;
        }

        public static void Start(int port = 6400)
        {
            if (_isRunning) return;

            _port = port;
            _cts = new CancellationTokenSource();

            try
            {
                _listener = new TcpListener(IPAddress.Loopback, _port);
                _listener.Start();
                _isRunning = true;

                Log($"TCP Bridge started on port {_port}", LogType.Info);
                WriteStatusFile();

                Task.Run(() => AcceptClientsAsync(_cts.Token));
            }
            catch (Exception ex)
            {
                Log($"Failed to start: {ex.Message}", LogType.Error);
                _isRunning = false;
            }
        }

        public static void Stop()
        {
            if (!_isRunning) return;

            _cts?.Cancel();

            try
            {
                _listener?.Stop();
            }
            catch { }

            _isRunning = false;
            DeleteStatusFile();
            Log("Bridge stopped", LogType.Info);
            EditorApplication.delayCall += () => OnClientCountChanged?.Invoke();
        }

        public static void Restart(int port = 6400)
        {
            Stop();
            Start(port);
        }

        private static async Task AcceptClientsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning)
            {
                try
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client, token);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        Log($"Accept error: {ex.Message}", LogType.Error);
                    }
                }
            }
        }

        private static async Task HandleClientAsync(TcpClient client, CancellationToken token)
        {
            Interlocked.Increment(ref _clientCount);
            EditorApplication.delayCall += () => OnClientCountChanged?.Invoke();
            Log("Client connected", LogType.Connection);

            try
            {
                client.NoDelay = true; // Disable Nagle for lower latency
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

                while (!token.IsCancellationRequested && client.Connected)
                {
                    var line = await reader.ReadLineAsync();
                    if (line == null) break; // Client disconnected

                    var commandType = GetCommandPreview(line);
                    Log($"Command: {commandType}", LogType.Command);

                    // Execute on main thread
                    var response = await ExecuteOnMainThreadAsync(line, token);

                    await writer.WriteLineAsync(response);
                    Log("Response sent", LogType.Response);
                }
            }
            catch (IOException)
            {
                // Client disconnected
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Log($"Client error: {ex.Message}", LogType.Error);
                }
            }
            finally
            {
                try { client.Close(); } catch { }
                Interlocked.Decrement(ref _clientCount);
                EditorApplication.delayCall += () => OnClientCountChanged?.Invoke();
                Log("Client disconnected", LogType.Connection);
            }
        }

        // Pending commands queue for main thread execution
        private static readonly ConcurrentQueue<(string json, TaskCompletionSource<string> tcs, CancellationToken token)> _pendingCommands = new();
        private static bool _updateHooked;

        private static Task<string> ExecuteOnMainThreadAsync(string commandJson, CancellationToken token)
        {
            var tcs = new TaskCompletionSource<string>();
            _pendingCommands.Enqueue((commandJson, tcs, token));

            if (!_updateHooked)
            {
                _updateHooked = true;
                EditorApplication.update += ProcessPendingCommands;
            }

            return tcs.Task;
        }

        private static void ProcessPendingCommands()
        {
            int processed = 0;
            while (processed < 10 && _pendingCommands.TryDequeue(out var item))
            {
                var (commandJson, tcs, token) = item;

                if (token.IsCancellationRequested)
                {
                    tcs.TrySetCanceled();
                    continue;
                }

                try
                {
                    var result = CommandExecutor.Execute(commandJson);
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    var error = $"{{\"status\":\"error\",\"error\":\"{EscapeJson(ex.Message)}\"}}";
                    tcs.TrySetResult(error);
                }

                processed++;
            }
        }

        private static string GetCommandPreview(string json)
        {
            try
            {
                var typeStart = json.IndexOf("\"type\"");
                if (typeStart < 0) return json.Length > 50 ? json.Substring(0, 50) + "..." : json;

                var colonPos = json.IndexOf(':', typeStart);
                var quoteStart = json.IndexOf('"', colonPos);
                var quoteEnd = json.IndexOf('"', quoteStart + 1);

                if (quoteStart > 0 && quoteEnd > quoteStart)
                {
                    return json.Substring(quoteStart + 1, quoteEnd - quoteStart - 1);
                }
            }
            catch { }

            return json.Length > 50 ? json.Substring(0, 50) + "..." : json;
        }

        private static string EscapeJson(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private static void WriteStatusFile()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unity-bridge");
                Directory.CreateDirectory(dir);

                var projectHash = Application.dataPath.GetHashCode().ToString("x8");
                var statusFile = Path.Combine(dir, $"bridge-{projectHash}.json");

                var status = $@"{{
  ""port"": {_port},
  ""protocol"": ""tcp"",
  ""project_name"": ""{EscapeJson(Application.productName)}"",
  ""project_path"": ""{EscapeJson(Application.dataPath)}"",
  ""unity_version"": ""{Application.unityVersion}"",
  ""last_heartbeat"": ""{DateTime.UtcNow:O}""
}}";
                File.WriteAllText(statusFile, status);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[BridgeLite] Failed to write status file: {ex.Message}");
            }
        }

        private static void DeleteStatusFile()
        {
            try
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".unity-bridge");
                var projectHash = Application.dataPath.GetHashCode().ToString("x8");
                var statusFile = Path.Combine(dir, $"bridge-{projectHash}.json");

                if (File.Exists(statusFile))
                {
                    File.Delete(statusFile);
                }
            }
            catch { }
        }

        public static void Log(string message, LogType type)
        {
            var entry = new LogEntry(DateTime.Now, message, type);
            EventLog.Enqueue(entry);

            while (EventLog.Count > 100)
            {
                EventLog.TryDequeue(out _);
            }

            EditorApplication.delayCall += () => OnLogUpdated?.Invoke();
        }
    }

    public enum LogType
    {
        Info,
        Connection,
        Command,
        Response,
        Error
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; }
        public string Message { get; }
        public LogType Type { get; }

        public LogEntry(DateTime timestamp, string message, LogType type)
        {
            Timestamp = timestamp;
            Message = message;
            Type = type;
        }
    }
}
