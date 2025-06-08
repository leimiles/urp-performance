using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Text;
using TMPro;
using System.Collections.Concurrent;
using System.IO;
using System;
using System.Linq;
using Stopwatch = System.Diagnostics.Stopwatch;
using DebugServer.Models;
using DebugServer.Pooling;

namespace DebugServer
{
    /// <summary>
    /// Unity App 内嵌 TCP 服务，仅在启用宏 INCLUDE_LOCAL_NETWORK_DEBUG 时启动
    /// 局域网中的命令行工具（Mac/Linux/Windows）通过 IP+端口连接手机
    /// 工具输入命令触发 Unity 调试逻辑（如 spawn、setvar、log 等）
    /// 易于扩展命令集，支持本地测试调试
    /// </summary>
    public class DebugServer : MonoBehaviour
    {
#if INCLUDE_LOCAL_NETWORK_DEBUG
        [Header("Server Settings")]
        [SerializeField] private int port = 9527;
        [SerializeField] private bool autoStart = true;
        [SerializeField] private int maxConnections = 3;
        [SerializeField] private int commandTimeout = 60;
        [SerializeField] private int maxCommandLength = 4096;
        [SerializeField] private string[] allowedIPs;
        [SerializeField] private bool logCommands = true;
        [SerializeField] private int maxCommandsPerFrame = 5;
        [SerializeField] private int maxProcessingTimeMs = 200;
        [SerializeField] private int networkBufferSize = 4096;
        [SerializeField] private int cleanupInterval = 5;
        [SerializeField] private int maxCommandHistory = 20;

        [Header("Performance Settings")]
        [SerializeField] private int minReadIntervalMs = 100;
        [SerializeField] private int minCommandIntervalMs = 200;
        [SerializeField] private int uiUpdateIntervalMs = 1000;
        [SerializeField] private bool enablePerformanceMonitoring = true;

        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI debugText;

        // 网络管理器
        private DebugNetworkManager networkManager;

        // 命令处理
        private string currentCommand = "";
        private int pendingCommands = 0;
        private readonly Stopwatch commandStopwatch = new Stopwatch();
        private readonly Queue<DebugCommand> commandHistory = new Queue<DebugCommand>();

        // 对象池
        private SimpleObjectPool<DebugCommand> commandPool;
        private SimpleObjectPool<StringBuilder> stringBuilderPool;

        // 性能统计
        private readonly PerformanceStats stats = new PerformanceStats();
        private DateTime lastUIUpdateTime = DateTime.MinValue;
        private readonly Stopwatch frameStopwatch = new Stopwatch();

        private void Awake()
        {
            // 初始化对象池
            commandPool = ObjectPoolManager.GetPool<DebugCommand>(
                createFunc: () => new DebugCommand(),
                onGet: (cmd) => {
                    cmd.Command = null;
                    cmd.ClientInfo = null;
                    cmd.Timestamp = DateTime.Now;
                },
                onRelease: (cmd) => {
                    cmd.Command = null;
                    cmd.ClientInfo = null;
                },
                maxSize: 100
            );

            stringBuilderPool = ObjectPoolManager.GetPool<StringBuilder>(
                createFunc: () => new StringBuilder(),
                onGet: (sb) => sb.Clear(),
                onRelease: (sb) => sb.Clear(),
                maxSize: 50
            );

            // 初始化网络管理器
            networkManager = new DebugNetworkManager(
                port,
                maxConnections,
                commandTimeout,
                maxCommandLength,
                allowedIPs,
                networkBufferSize,
                minReadIntervalMs,
                minCommandIntervalMs
            );

            // 注册网络事件
            networkManager.OnClientConnected += HandleClientConnected;
            networkManager.OnClientDisconnected += HandleClientDisconnected;
            networkManager.OnServerError += HandleServerError;
            networkManager.OnCommandReceived += HandleCommandReceived;
        }

        private void Start()
        {
            if (autoStart)
            {
                StartServer();
            }
        }

        /// <summary>
        /// 启动调试服务器
        /// </summary>
        public void StartServer()
        {
            networkManager.StartServer();
            StartCoroutine(CleanupRoutine());
        }

        /// <summary>
        /// 停止调试服务器
        /// </summary>
        public void StopServer()
        {
            networkManager.StopServer();
        }

        private void Update()
        {
            frameStopwatch.Restart();

            // 更新性能统计
            if (enablePerformanceMonitoring)
            {
                var now = DateTime.Now;
                if ((now - stats.LastResetTime).TotalSeconds >= 1)
                {
                    stats.PeakCommandsPerSecond = Math.Max(stats.PeakCommandsPerSecond, stats.CommandsThisSecond);
                    stats.Reset();
                }
            }

            // 批量处理命令
            int processedCount = 0;
            while (networkManager.TryDequeueCommand(out DebugCommand command) && processedCount < maxCommandsPerFrame)
            {
                currentCommand = command.Command;
                if (ProcessCommandWithTimeout(command))
                {
                    processedCount++;
                }
                pendingCommands--;
            }

            // 限制UI更新频率
            if (processedCount > 0 && (DateTime.Now - lastUIUpdateTime).TotalMilliseconds >= uiUpdateIntervalMs)
            {
                UpdateUI();
                lastUIUpdateTime = DateTime.Now;
            }

            frameStopwatch.Stop();
            if (enablePerformanceMonitoring)
            {
                stats.AddProcessingTime(frameStopwatch.ElapsedMilliseconds);
            }
        }

        private bool ProcessCommandWithTimeout(DebugCommand command)
        {
            commandStopwatch.Restart();
            try
            {
                ProcessCommand(command);
                return true;
            }
            catch (Exception e)
            {
                HandleServerError($"Error processing command: {e.Message}");
                return false;
            }
            finally
            {
                commandStopwatch.Stop();
                if (commandStopwatch.ElapsedMilliseconds > maxProcessingTimeMs)
                {
                    Debug.LogWarning($"Command processing took too long: {commandStopwatch.ElapsedMilliseconds}ms\nCommand: {command.Command}");
                }
                commandPool.Release(command);
            }
        }

        private void ProcessCommand(DebugCommand command)
        {
            if (logCommands)
            {
                Debug.Log($"[DebugServer] Command from {command.ClientInfo}: {command.Command}");
            }

            // 添加到历史记录
            commandHistory.Enqueue(command);
            while (commandHistory.Count > maxCommandHistory)
            {
                commandHistory.Dequeue();
            }

            string cmd = command.Command.ToLower();
            switch (cmd)
            {
                case "help":
                    Debug.Log("[DebugServer] Available commands:\n" +
                             "help - Show this help message\n" +
                             "clear - Clear the console\n" +
                             "clients - Show connected clients\n" +
                             "delay <ms> - Simulate command delay (for testing)\n" +
                             "history - Show command history");
                    break;

                case "clear":
                    Debug.ClearDeveloperConsole();
                    break;

                case "clients":
                    var clients = string.Join("\n", networkManager.GetConnectedClients().Select(c => 
                        $"{c.EndPoint}: {c.CommandCount} commands, " +
                        $"Last activity: {(DateTime.Now - c.LastActivity).TotalSeconds:F1}s ago"));
                    Debug.Log($"[DebugServer] Connected clients:\n{clients}");
                    break;

                case "history":
                    var history = string.Join("\n", commandHistory.Select(c => 
                        $"[{c.Timestamp:HH:mm:ss}] {c.ClientInfo}: {c.Command}"));
                    Debug.Log($"[DebugServer] Command history (last {commandHistory.Count} commands):\n{history}");
                    break;

                default:
                    if (cmd.StartsWith("delay "))
                    {
                        if (int.TryParse(cmd.Substring(6), out int delayMs))
                        {
                            System.Threading.Thread.Sleep(delayMs);
                        }
                    }
                    break;
            }
        }

        private void UpdateUI()
        {
            if (debugText != null)
            {
                var sb = stringBuilderPool.Get();
                try
                {
                    sb.AppendLine($"input: {currentCommand}");
                    sb.AppendLine($"Pending Commands: {pendingCommands}");
                    sb.AppendLine($"Last Process Time: {commandStopwatch.ElapsedMilliseconds}ms");
                    sb.AppendLine($"Connected Clients: {networkManager.GetConnectedClientCount()}");
                    sb.AppendLine($"Command History: {commandHistory.Count}/{maxCommandHistory}");

                    if (enablePerformanceMonitoring)
                    {
                        sb.AppendLine($"\nPerformance Stats:");
                        sb.AppendLine($"Total Commands: {stats.TotalCommands}");
                        sb.AppendLine($"Commands/Second: {stats.CommandsThisSecond}");
                        sb.AppendLine($"Peak Commands/Second: {stats.PeakCommandsPerSecond}");
                        sb.AppendLine($"Avg Process Time: {stats.AverageProcessingTime:F2}ms");
                    }

                    // 显示客户端详细信息
                    foreach (var client in networkManager.GetConnectedClients())
                    {
                        sb.AppendLine($"- {client.EndPoint}: {client.CommandCount} commands, " +
                                    $"Last activity: {(DateTime.Now - client.LastActivity).TotalSeconds:F1}s ago");
                    }

                    debugText.text = sb.ToString();
                }
                finally
                {
                    stringBuilderPool.Release(sb);
                }
            }
        }

        private void HandleClientConnected(string clientEndPoint)
        {
            Debug.Log($"Client connected: {clientEndPoint}");
        }

        private void HandleClientDisconnected(string clientEndPoint)
        {
            Debug.Log($"Client disconnected: {clientEndPoint}");
        }

        private void HandleServerError(string errorMessage)
        {
            Debug.LogError(errorMessage);
        }

        private void HandleCommandReceived(DebugCommand command)
        {
            pendingCommands++;
            stats.CommandsThisSecond++;
            stats.TotalCommands++;
        }

        private IEnumerator CleanupRoutine()
        {
            while (true)
            {
                networkManager.CleanupTimeoutConnections();
                yield return new WaitForSeconds(cleanupInterval);
            }
        }

        private void OnDestroy()
        {
            StopServer();
            ObjectPoolManager.ClearAll();
        }
#endif
    }
}