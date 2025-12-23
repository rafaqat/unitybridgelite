using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace UnityBridgeLite
{
    /// <summary>
    /// Editor window showing bridge status, connections, and command log.
    /// </summary>
    public class BridgeWindow : EditorWindow
    {
        private Vector2 _logScrollPos;
        private int _port = 6400;
        private List<LogEntry> _displayedLogs = new();
        private GUIStyle _logStyle;
        private GUIStyle _headerStyle;
        private bool _autoScroll = true;

        [MenuItem("Window/Unity Bridge Lite")]
        public static void ShowWindow()
        {
            var window = GetWindow<BridgeWindow>("Bridge Lite");
            window.minSize = new Vector2(400, 300);
        }

        private void OnEnable()
        {
            BridgeServer.OnLogUpdated += RefreshLogs;
            BridgeServer.OnClientCountChanged += Repaint;
            RefreshLogs();
        }

        private void OnDisable()
        {
            BridgeServer.OnLogUpdated -= RefreshLogs;
            BridgeServer.OnClientCountChanged -= Repaint;
        }

        private void RefreshLogs()
        {
            _displayedLogs = BridgeServer.EventLog.ToList();
            Repaint();
        }

        private void InitStyles()
        {
            if (_logStyle == null)
            {
                _logStyle = new GUIStyle(EditorStyles.label)
                {
                    fontSize = 11,
                    wordWrap = true,
                    richText = true
                };
            }

            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14
                };
            }
        }

        private void OnGUI()
        {
            InitStyles();

            // Header
            EditorGUILayout.Space(5);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Unity Bridge Lite", _headerStyle);
            GUILayout.FlexibleSpace();

            // Status indicator
            var statusColor = BridgeServer.IsRunning ? Color.green : Color.red;
            var statusText = BridgeServer.IsRunning ? "Running" : "Stopped";

            var oldColor = GUI.color;
            GUI.color = statusColor;
            GUILayout.Label("\u25CF", GUILayout.Width(15));
            GUI.color = oldColor;
            GUILayout.Label(statusText, GUILayout.Width(60));

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Control panel
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            EditorGUILayout.LabelField("Port:", GUILayout.Width(35));
            _port = EditorGUILayout.IntField(_port, GUILayout.Width(60));

            GUILayout.Space(10);

            if (BridgeServer.IsRunning)
            {
                if (GUILayout.Button("Stop", GUILayout.Width(60)))
                {
                    BridgeServer.Stop();
                }
                if (GUILayout.Button("Restart", GUILayout.Width(60)))
                {
                    BridgeServer.Restart(_port);
                }
            }
            else
            {
                if (GUILayout.Button("Start", GUILayout.Width(60)))
                {
                    BridgeServer.Start(_port);
                }
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField($"Clients: {BridgeServer.ClientCount}", GUILayout.Width(70));

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Tabs
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(true, "Event Log", EditorStyles.toolbarButton))
            {
            }
            GUILayout.FlexibleSpace();
            _autoScroll = GUILayout.Toggle(_autoScroll, "Auto-scroll", EditorStyles.toolbarButton);
            if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                CopyLogToClipboard();
            }
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                while (BridgeServer.EventLog.TryDequeue(out _)) { }
                RefreshLogs();
            }
            EditorGUILayout.EndHorizontal();

            // Log view
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            _logScrollPos = EditorGUILayout.BeginScrollView(_logScrollPos);

            foreach (var entry in _displayedLogs)
            {
                DrawLogEntry(entry);
            }

            EditorGUILayout.EndScrollView();

            if (_autoScroll && _displayedLogs.Count > 0)
            {
                _logScrollPos.y = float.MaxValue;
            }

            EditorGUILayout.EndVertical();

            // Footer with connection info
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            if (BridgeServer.IsRunning)
            {
                EditorGUILayout.LabelField($"Listening on localhost:{BridgeServer.Port}");
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy Address", GUILayout.Width(100)))
                {
                    EditorGUIUtility.systemCopyBuffer = $"localhost:{BridgeServer.Port}";
                }
            }
            else
            {
                EditorGUILayout.LabelField("Bridge not running");
            }
            EditorGUILayout.EndHorizontal();
        }

        private void CopyLogToClipboard()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== Unity Bridge Lite Event Log ===");
            sb.AppendLine($"Port: {BridgeServer.Port} | Status: {(BridgeServer.IsRunning ? "Running" : "Stopped")} | Clients: {BridgeServer.ClientCount}");
            sb.AppendLine();

            foreach (var entry in _displayedLogs)
            {
                var typeLabel = entry.Type switch
                {
                    LogType.Connection => "[CONN]",
                    LogType.Command => "[CMD]",
                    LogType.Response => "[RESP]",
                    LogType.Error => "[ERR]",
                    _ => "[INFO]"
                };
                sb.AppendLine($"{entry.Timestamp:HH:mm:ss} {typeLabel} {entry.Message}");
            }

            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log("[BridgeLite] Event log copied to clipboard");
        }

        private void DrawLogEntry(LogEntry entry)
        {
            EditorGUILayout.BeginHorizontal();

            // Timestamp
            var timeStr = entry.Timestamp.ToString("HH:mm:ss");
            EditorGUILayout.LabelField(timeStr, GUILayout.Width(60));

            // Type indicator with color
            string typeLabel;
            Color typeColor;

            switch (entry.Type)
            {
                case LogType.Connection:
                    typeLabel = "[CONN]";
                    typeColor = new Color(0.4f, 0.8f, 1f);
                    break;
                case LogType.Command:
                    typeLabel = "[CMD]";
                    typeColor = new Color(1f, 0.8f, 0.4f);
                    break;
                case LogType.Response:
                    typeLabel = "[RESP]";
                    typeColor = new Color(0.6f, 1f, 0.6f);
                    break;
                case LogType.Error:
                    typeLabel = "[ERR]";
                    typeColor = new Color(1f, 0.4f, 0.4f);
                    break;
                default:
                    typeLabel = "[INFO]";
                    typeColor = Color.gray;
                    break;
            }

            var oldColor = GUI.color;
            GUI.color = typeColor;
            EditorGUILayout.LabelField(typeLabel, GUILayout.Width(50));
            GUI.color = oldColor;

            // Message
            EditorGUILayout.LabelField(entry.Message, _logStyle);

            EditorGUILayout.EndHorizontal();
        }
    }
}
