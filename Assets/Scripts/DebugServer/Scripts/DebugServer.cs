using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
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
        [SerializeField] private int maxConnections = 3; // 降低最大连接数，因为这是调试工具
        [SerializeField] private int commandTimeout = 60; // 增加超时时间，给调试更多时间
        [SerializeField] private int maxCommandLength = 4096; // 增加命令长度限制，支持更长的调试命令
        [SerializeField] private string[] allowedIPs; // 允许连接的IP列表，为空则允许所有IP
        [SerializeField] private bool logCommands = true; // 是否在控制台输出命令
        [SerializeField] private int maxCommandsPerFrame = 5; // 降低每帧处理命令数，减少性能影响
        [SerializeField] private int maxProcessingTimeMs = 200; // 增加处理时间限制，给复杂命令更多时间
        [SerializeField] private int networkBufferSize = 4096; // 减小缓冲区大小，因为调试命令通常较小
        [SerializeField] private int cleanupInterval = 5; // 增加清理间隔，减少清理频率
        [SerializeField] private int maxCommandHistory = 20; // 减少历史记录数量，因为调试时通常不需要太多历史

        [Header("Performance Settings")]
        [SerializeField] private int minReadIntervalMs = 100; // 增加读取间隔，减少网络负载
        [SerializeField] private int minCommandIntervalMs = 200; // 增加命令间隔，给系统更多处理时间
        [SerializeField] private int uiUpdateIntervalMs = 1000; // 增加UI更新间隔，减少UI更新开销
        [SerializeField] private bool enablePerformanceMonitoring = true; // 保持性能监控开启

        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI debugText;

        // 服务器状态
        private TcpListener server;
        private Thread serverThread;
        private bool isRunning = false;

        // 字符串缓存
        private static readonly Dictionary<string, string> stringCache = new Dictionary<string, string>();
        private static readonly object stringCacheLock = new object();

        /// <summary>
        /// 获取缓存的字符串，避免重复创建
        /// </summary>
        private static string GetCachedString(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;

            lock (stringCacheLock)
            {
                if (stringCache.TryGetValue(key, out string cached))
                {
                    return cached;
                }

                stringCache[key] = key;
                return key;
            }
        }

        /// <summary>
        /// 清理字符串缓存
        /// </summary>
        private static void ClearStringCache()
        {
            lock (stringCacheLock)
            {
                stringCache.Clear();
            }
        }

        private readonly ConcurrentDictionary<string, ClientConnection> connectedClients = 
            new ConcurrentDictionary<string, ClientConnection>();
        private readonly ConcurrentQueue<DebugCommand> commandQueue = new ConcurrentQueue<DebugCommand>();
        private string currentCommand = "";
        private int pendingCommands = 0; // 待处理命令数
        private readonly Stopwatch commandStopwatch = new Stopwatch();

        // 对象池
        private SimpleObjectPool<DebugCommand> commandPool;
        private SimpleObjectPool<byte[]> bufferPool;
        private SimpleObjectPool<StringBuilder> stringBuilderPool;

        // 事件系统
        public static event Action<string> OnCommandReceived;
        public static event Action<string> OnClientConnected;
        public static event Action<string> OnClientDisconnected;
        public static event Action<string> OnServerError;

        private readonly PerformanceStats stats = new PerformanceStats();
        private DateTime lastReadTime = DateTime.MinValue;
        private DateTime lastCommandTime = DateTime.MinValue;
        private DateTime lastUIUpdateTime = DateTime.MinValue;
        private readonly Stopwatch frameStopwatch = new Stopwatch();

        // 命令历史记录
        private readonly Queue<DebugCommand> commandHistory = new Queue<DebugCommand>();

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

            bufferPool = ObjectPoolManager.GetPool<byte[]>(
                createFunc: () => new byte[networkBufferSize],
                onGet: (buffer) => Array.Clear(buffer, 0, buffer.Length),
                onRelease: (buffer) => Array.Clear(buffer, 0, buffer.Length),
                maxSize: 50
            );

            stringBuilderPool = ObjectPoolManager.GetPool<StringBuilder>(
                createFunc: () => new StringBuilder(),
                onGet: (sb) => sb.Clear(),
                onRelease: (sb) => sb.Clear(),
                maxSize: 50
            );
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
            if (isRunning)
            {
                Debug.LogWarning("Debug server is already running.");
                return;
            }

            try
            {
                server = new TcpListener(IPAddress.Any, port);
                server.Start();
                isRunning = true;

                serverThread = new Thread(ListenForClients);
                serverThread.IsBackground = true;
                serverThread.Start();

                // 启动清理线程
                StartCoroutine(CleanupRoutine());

                Debug.Log($"Debug Server started on port {port}");
            }
            catch (Exception e)
            {
                HandleServerError($"Failed to start debug server: {e.Message}");
            }
        }

        /// <summary>
        /// 停止调试服务器
        /// </summary>
        public void StopServer()
        {
            if (!isRunning)
            {
                return;
            }

            isRunning = false;
            try
            {
                server?.Stop();
                serverThread?.Join(1000);
                connectedClients.Clear();
            }
            catch (Exception e)
            {
                HandleServerError($"Error stopping server: {e.Message}");
            }
        }

        private void ListenForClients()
        {
            while (isRunning)
            {
                try
                {
                    TcpClient client = server.AcceptTcpClient();
                    string clientEndPoint = client.Client.RemoteEndPoint.ToString();

                    // 检查IP是否允许连接
                    if (!IsIPAllowed(clientEndPoint))
                    {
                        Debug.LogWarning($"Blocked connection from unauthorized IP: {clientEndPoint}");
                        client.Close();
                        continue;
                    }

                    // 检查连接数量限制
                    if (connectedClients.Count >= maxConnections)
                    {
                        Debug.LogWarning($"Connection limit reached. Rejected connection from: {clientEndPoint}");
                        client.Close();
                        continue;
                    }

                    // 在新线程中处理客户端连接
                    Thread clientThread = new Thread(() => HandleClient(client));
                    clientThread.IsBackground = true;
                    clientThread.Start();
                }
                catch (SocketException e)
                {
                    if (isRunning)
                    {
                        if (e.SocketErrorCode == SocketError.Interrupted)
                        {
                            Debug.Log("Debug server stopped.");
                        }
                        else
                        {
                            HandleServerError($"Socket error: {e.Message}");
                        }
                    }
                }
                catch (Exception e)
                {
                    if (isRunning)
                    {
                        HandleServerError($"Error accepting client: {e.Message}");
                    }
                }
            }
        }

        private bool IsIPAllowed(string clientEndPoint)
        {
            if (allowedIPs == null || allowedIPs.Length == 0)
            {
                return true; // 如果没有设置允许的IP，则允许所有连接
            }

            string clientIP = clientEndPoint.Split(':')[0];
            return allowedIPs.Contains(clientIP);
        }

        private void HandleClient(TcpClient client)
        {
            string clientEndPoint = GetCachedString(client.Client.RemoteEndPoint.ToString());
            var connection = new ClientConnection(commandTimeout)
            {
                Client = client,
                CommandCount = 0,
                EndPoint = clientEndPoint
            };

            if (!connectedClients.TryAdd(clientEndPoint, connection))
            {
                client.Close();
                return;
            }

            OnClientConnected?.Invoke(clientEndPoint);

            try
            {
                using (NetworkStream stream = client.GetStream())
                {
                    stream.ReadTimeout = commandTimeout * 1000;
                    var buffer = bufferPool.Get();
                    var commandBuilder = stringBuilderPool.Get();

                    try
                    {
                        int bytesRead;
                        while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            // 限制读取频率，使用更平滑的延迟
                            var now = DateTime.Now;
                            var timeSinceLastRead = (now - lastReadTime).TotalMilliseconds;
                            if (timeSinceLastRead < minReadIntervalMs)
                            {
                                Thread.Sleep((int)(minReadIntervalMs - timeSinceLastRead));
                            }
                            lastReadTime = DateTime.Now;

                            connection.UpdateActivity();
                            commandBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));
                            string command = commandBuilder.ToString().Trim();

                            if (!string.IsNullOrEmpty(command))
                            {
                                if (command.Length <= maxCommandLength)
                                {
                                    // 限制命令处理频率，使用更平滑的延迟
                                    now = DateTime.Now;
                                    var timeSinceLastCommand = (now - lastCommandTime).TotalMilliseconds;
                                    if (timeSinceLastCommand < minCommandIntervalMs)
                                    {
                                        Thread.Sleep((int)(minCommandIntervalMs - timeSinceLastCommand));
                                    }
                                    lastCommandTime = DateTime.Now;

                                    var debugCommand = commandPool.Get();
                                    debugCommand.Command = GetCachedString(command);
                                    debugCommand.ClientInfo = clientEndPoint;
                                    commandQueue.Enqueue(debugCommand);
                                    pendingCommands++;
                                    connection.CommandCount++;
                                    stats.CommandsThisSecond++;
                                    stats.TotalCommands++;
                                    OnCommandReceived?.Invoke(command);
                                }
                                else
                                {
                                    Debug.LogWarning($"Command too long from {clientEndPoint}: {command.Length} bytes");
                                }
                                commandBuilder.Clear();
                            }
                        }
                    }
                    finally
                    {
                        bufferPool.Release(buffer);
                        stringBuilderPool.Release(commandBuilder);
                    }
                }
            }
            catch (IOException)
            {
                Debug.Log($"Client {clientEndPoint} disconnected.");
            }
            catch (SocketException)
            {
                Debug.Log($"Client {clientEndPoint} disconnected.");
            }
            catch (Exception e)
            {
                HandleServerError($"Error handling client {clientEndPoint}: {e.Message}");
            }
            finally
            {
                try
                {
                    client.Close();
                }
                catch
                {
                    // 忽略关闭时的错误
                }
                if (connectedClients.TryRemove(clientEndPoint, out var removedConnection))
                {
                    Debug.Log($"Client {clientEndPoint} removed. Total commands: {removedConnection.CommandCount}");
                }
                OnClientDisconnected?.Invoke(clientEndPoint);
            }
        }

        private IEnumerator CleanupRoutine()
        {
            while (isRunning)
            {
                // 清理超时的连接
                var timeoutClients = connectedClients
                    .Where(kvp => !kvp.Value.IsActive)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var client in timeoutClients)
                {
                    if (connectedClients.TryRemove(client, out var connection))
                    {
                        try
                        {
                            connection.Client.Close();
                        }
                        catch { }
                        Debug.Log($"Removed timeout client: {client} (Commands: {connection.CommandCount})");
                    }
                }

                yield return new WaitForSeconds(cleanupInterval);
            }
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
            while (commandQueue.TryDequeue(out DebugCommand command) && processedCount < maxCommandsPerFrame)
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

            // 使用缓存的字符串进行比较
            string cmd = command.Command.ToLower();
            switch (GetCachedString(cmd))
            {
                case "help":
                    Debug.Log("[DebugServer] Available commands:\n" +
                             "help - Show this help message\n" +
                             "clear - Clear the console\n" +
                             "clients - Show connected clients\n" +
                             "delay <ms> - Simulate command delay (for testing)\n" +
                             "clearcache - Clear string cache\n" +
                             "history - Show command history");
                    break;

                case "clear":
                    Debug.ClearDeveloperConsole();
                    break;

                case "clients":
                    var clients = string.Join("\n", connectedClients.Values.Select(c => 
                        $"{c.EndPoint}: {c.CommandCount} commands, " +
                        $"Last activity: {(DateTime.Now - c.LastActivity).TotalSeconds:F1}s ago"));
                    Debug.Log($"[DebugServer] Connected clients:\n{clients}");
                    break;

                case "clearcache":
                    ClearStringCache();
                    Debug.Log("[DebugServer] String cache cleared");
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
                    sb.AppendLine($"Buffer Pool Size: {bufferPool.Count}");
                    sb.AppendLine($"StringBuilder Pool Size: {stringBuilderPool.Count}");
                    sb.AppendLine($"Connected Clients: {connectedClients.Count}");
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
                    foreach (var client in connectedClients.Values)
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

        private void HandleServerError(string errorMessage)
        {
            Debug.LogError(errorMessage);
            OnServerError?.Invoke(errorMessage);
        }

        private void OnDestroy()
        {
            StopServer();
            ClearStringCache();
            ObjectPoolManager.ClearAll();
        }

        /// <summary>
        /// 调试命令数据结构
        /// </summary>
        private class DebugCommand
        {
            public string Command { get; set; }
            public DateTime Timestamp { get; set; }
            public string ClientInfo { get; set; }
        }
#endif
    }
}