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
using DebugServer.UI;
using DebugServer.Config;

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
        [SerializeField] private DebugConfig config;

        // 网络管理器
        private DebugNetworkManager networkManager;
        
        // 命令处理器
        private DebugCommandHandler commandHandler;

        // UI管理器
        private DebugUI uiManager;

        // 命令处理
        private string currentCommand = "";
        private int pendingCommands = 0;
        private readonly Stopwatch commandStopwatch = new Stopwatch();

        // 对象池
        private SimpleObjectPool<DebugCommand> commandPool;

        // 性能统计
        private readonly PerformanceStats stats = new PerformanceStats();
        private readonly Stopwatch frameStopwatch = new Stopwatch();

        private void Awake()
        {
            // 加载配置
            if (config == null)
            {
                config = DebugConfig.Instance;
            }
            config.Validate();

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

            // 初始化网络管理器
            networkManager = new DebugNetworkManager(
                config.port,
                config.maxConnections,
                config.commandTimeout,
                config.maxCommandLength,
                config.allowedIPs,
                config.networkBufferSize,
                config.minReadIntervalMs,
                config.minCommandIntervalMs
            );

            // 初始化命令处理器
            commandHandler = new DebugCommandHandler(
                config.maxCommandHistory,
                config.maxProcessingTimeMs,
                stats
            );

            // 获取UI管理器
            uiManager = GetComponent<DebugUI>();
            if (uiManager == null)
            {
                Debug.LogWarning("[DebugServer] DebugUI component not found!");
            }

            // 注册命令处理器事件
            commandHandler.OnCommandProcessed += HandleCommandProcessed;
            commandHandler.OnCommandError += HandleCommandError;

            // 注册网络事件
            networkManager.OnClientConnected += HandleClientConnected;
            networkManager.OnClientDisconnected += HandleClientDisconnected;
            networkManager.OnServerError += HandleServerError;
            networkManager.OnCommandReceived += HandleCommandReceived;
        }

        private void Start()
        {
            if (config.autoStart)
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
            if (config.enablePerformanceMonitoring)
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
            while (networkManager.TryDequeueCommand(out DebugCommand command) && processedCount < config.maxCommandsPerFrame)
            {
                currentCommand = command.Command;
                if (ProcessCommandWithTimeout(command))
                {
                    processedCount++;
                }
                pendingCommands--;
            }

            // 更新UI
            if (uiManager != null)
            {
                uiManager.UpdateUI(
                    currentCommand,
                    pendingCommands,
                    commandStopwatch.ElapsedMilliseconds,
                    networkManager.GetConnectedClientCount(),
                    commandHandler.GetCommandHistory().Length,
                    config.maxCommandHistory,
                    stats,
                    networkManager.GetConnectedClients()
                );
            }

            frameStopwatch.Stop();
            if (config.enablePerformanceMonitoring)
            {
                stats.AddProcessingTime(frameStopwatch.ElapsedMilliseconds);
            }
        }

        private bool ProcessCommandWithTimeout(DebugCommand command)
        {
            commandStopwatch.Restart();
            try
            {
                if (config.logCommands)
                {
                    Debug.Log($"[DebugServer] Command from {command.ClientInfo}: {command.Command}");
                }
                return commandHandler.ProcessCommand(command);
            }
            catch (Exception e)
            {
                HandleServerError($"Error processing command: {e.Message}");
                return false;
            }
            finally
            {
                commandStopwatch.Stop();
                commandPool.Release(command);
            }
        }

        private void HandleClientConnected(string clientEndPoint)
        {
            Debug.Log($"Client connected: {clientEndPoint}");
            uiManager?.ShowSuccess($"Client connected: {clientEndPoint}");
        }

        private void HandleClientDisconnected(string clientEndPoint)
        {
            Debug.Log($"Client disconnected: {clientEndPoint}");
            uiManager?.ShowSuccess($"Client disconnected: {clientEndPoint}");
        }

        private void HandleServerError(string errorMessage)
        {
            Debug.LogError(errorMessage);
            uiManager?.ShowError(errorMessage);
        }

        private void HandleCommandReceived(DebugCommand command)
        {
            pendingCommands++;
            stats.CommandsThisSecond++;
            stats.TotalCommands++;
        }

        private void HandleCommandProcessed(string message)
        {
            Debug.Log($"[DebugServer] {message}");
            uiManager?.ShowSuccess(message);
        }

        private void HandleCommandError(string error)
        {
            Debug.LogError($"[DebugServer] {error}");
            uiManager?.ShowError(error);
        }

        private IEnumerator CleanupRoutine()
        {
            while (true)
            {
                networkManager.CleanupTimeoutConnections();
                yield return new WaitForSeconds(config.cleanupInterval);
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