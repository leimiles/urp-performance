using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using UnityEngine;
using DebugServer.Models;
using DebugServer.Pooling;

namespace DebugServer
{
    /// <summary>
    /// 调试服务器网络管理器
    /// 负责处理所有网络相关的操作，包括服务器启动、客户端连接、数据接收等
    /// </summary>
    public class DebugNetworkManager
    {
        // 服务器配置
        private readonly int port;
        private readonly int maxConnections;
        private readonly int commandTimeout;
        private readonly int maxCommandLength;
        private readonly string[] allowedIPs;
        private readonly int networkBufferSize;
        private readonly int minReadIntervalMs;
        private readonly int minCommandIntervalMs;

        // 服务器状态
        private TcpListener server;
        private Thread serverThread;
        private bool isRunning;

        // 客户端管理
        private readonly ConcurrentDictionary<string, ClientConnection> connectedClients;
        private readonly ConcurrentQueue<DebugCommand> commandQueue;

        // 对象池
        private readonly SimpleObjectPool<byte[]> bufferPool;
        private readonly SimpleObjectPool<StringBuilder> stringBuilderPool;

        // 事件
        public event Action<string> OnClientConnected;
        public event Action<string> OnClientDisconnected;
        public event Action<string> OnServerError;
        public event Action<DebugCommand> OnCommandReceived;

        // 性能监控
        private DateTime lastReadTime = DateTime.MinValue;
        private DateTime lastCommandTime = DateTime.MinValue;

        public DebugNetworkManager(
            int port,
            int maxConnections,
            int commandTimeout,
            int maxCommandLength,
            string[] allowedIPs,
            int networkBufferSize,
            int minReadIntervalMs,
            int minCommandIntervalMs)
        {
            this.port = port;
            this.maxConnections = maxConnections;
            this.commandTimeout = commandTimeout;
            this.maxCommandLength = maxCommandLength;
            this.allowedIPs = allowedIPs;
            this.networkBufferSize = networkBufferSize;
            this.minReadIntervalMs = minReadIntervalMs;
            this.minCommandIntervalMs = minCommandIntervalMs;

            connectedClients = new ConcurrentDictionary<string, ClientConnection>();
            commandQueue = new ConcurrentQueue<DebugCommand>();

            // 初始化对象池
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

        /// <summary>
        /// 获取待处理的命令
        /// </summary>
        public bool TryDequeueCommand(out DebugCommand command)
        {
            return commandQueue.TryDequeue(out command);
        }

        /// <summary>
        /// 获取当前连接的客户端数量
        /// </summary>
        public int GetConnectedClientCount()
        {
            return connectedClients.Count;
        }

        /// <summary>
        /// 获取所有连接的客户端信息
        /// </summary>
        public ClientConnection[] GetConnectedClients()
        {
            return connectedClients.Values.ToArray();
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
            string clientEndPoint = client.Client.RemoteEndPoint.ToString();
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
                            // 限制读取频率
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
                                    // 限制命令处理频率
                                    now = DateTime.Now;
                                    var timeSinceLastCommand = (now - lastCommandTime).TotalMilliseconds;
                                    if (timeSinceLastCommand < minCommandIntervalMs)
                                    {
                                        Thread.Sleep((int)(minCommandIntervalMs - timeSinceLastCommand));
                                    }
                                    lastCommandTime = DateTime.Now;

                                    var debugCommand = new DebugCommand
                                    {
                                        Command = command,
                                        ClientInfo = clientEndPoint,
                                        Timestamp = DateTime.Now
                                    };

                                    commandQueue.Enqueue(debugCommand);
                                    connection.CommandCount++;
                                    OnCommandReceived?.Invoke(debugCommand);
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

        private void HandleServerError(string errorMessage)
        {
            Debug.LogError(errorMessage);
            OnServerError?.Invoke(errorMessage);
        }

        /// <summary>
        /// 清理超时的连接
        /// </summary>
        public void CleanupTimeoutConnections()
        {
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
        }
    }
} 