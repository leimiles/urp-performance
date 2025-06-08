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

/// <summary>
/// Unity App 内嵌 TCP 服务，仅在启用宏 INCLUDE_LOCAL_NETWORK_DEBUG 时启动
/// 局域网中的命令行工具（Mac/Linux/Windows）通过 IP+端口连接手机
/// 工具输入命令触发 Unity 调试逻辑（如 spawn、setvar、log 等）
/// 易于扩展命令集，支持本地测试调试
/// </summary>

public class DebugServer : MonoBehaviour
{
#if INCLUDE_LOCAL_NETWORK_DEBUG
    /// <summary>
    /// 简单的对象池实现
    /// </summary>
    private class SimpleObjectPool<T> where T : new()
    {
        private readonly ConcurrentQueue<T> pool = new ConcurrentQueue<T>();
        private readonly Action<T> onGet;
        private readonly Action<T> onRelease;
        private readonly int maxSize;

        public SimpleObjectPool(Action<T> onGet = null, Action<T> onRelease = null, int maxSize = 100)
        {
            this.onGet = onGet;
            this.onRelease = onRelease;
            this.maxSize = maxSize;
        }

        public T Get()
        {
            if (pool.TryDequeue(out T item))
            {
                onGet?.Invoke(item);
                return item;
            }
            return new T();
        }

        public void Release(T item)
        {
            if (pool.Count < maxSize)
            {
                onRelease?.Invoke(item);
                pool.Enqueue(item);
            }
        }
    }

    [Header("Server Settings")]
    [SerializeField] private int port = 9527;
    [SerializeField] private bool autoStart = true;
    [SerializeField] private int maxConnections = 5;
    [SerializeField] private int commandTimeout = 30; // 命令超时时间（秒）
    [SerializeField] private int maxCommandLength = 1024; // 最大命令长度
    [SerializeField] private string[] allowedIPs; // 允许连接的IP列表，为空则允许所有IP
    [SerializeField] private bool logCommands = true; // 是否在控制台输出命令

    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI debugText;

    // 服务器状态
    private TcpListener server;
    private Thread serverThread;
    private bool isRunning = false;
    private readonly ConcurrentDictionary<string, DateTime> connectedClients = new ConcurrentDictionary<string, DateTime>();
    private readonly ConcurrentQueue<DebugCommand> commandQueue = new ConcurrentQueue<DebugCommand>();
    private string currentCommand = "";

    // 命令对象池
    private readonly SimpleObjectPool<DebugCommand> commandPool = new SimpleObjectPool<DebugCommand>(
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

    // 事件系统
    public static event Action<string> OnCommandReceived;
    public static event Action<string> OnClientConnected;
    public static event Action<string> OnClientDisconnected;
    public static event Action<string> OnServerError;

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
        string clientEndPoint = client.Client.RemoteEndPoint.ToString();

        if (!connectedClients.TryAdd(clientEndPoint, DateTime.Now))
        {
            client.Close();
            return;
        }

        OnClientConnected?.Invoke(clientEndPoint);

        try
        {
            using (NetworkStream stream = client.GetStream())
            {
                stream.ReadTimeout = commandTimeout * 1000; // 设置读取超时
                byte[] buffer = new byte[maxCommandLength];
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    string command = Encoding.UTF8.GetString(buffer, 0, bytesRead).Trim();

                    // 验证命令
                    if (string.IsNullOrEmpty(command) || command.Length > maxCommandLength)
                    {
                        continue;
                    }

                    var debugCommand = commandPool.Get();
                    debugCommand.Command = command;
                    debugCommand.ClientInfo = clientEndPoint;
                    commandQueue.Enqueue(debugCommand);
                    OnCommandReceived?.Invoke(command);
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
            connectedClients.TryRemove(clientEndPoint, out _);
            OnClientDisconnected?.Invoke(clientEndPoint);
        }
    }

    private IEnumerator CleanupRoutine()
    {
        while (isRunning)
        {
            // 清理超时的连接
            var timeoutClients = connectedClients
                .Where(kvp => (DateTime.Now - kvp.Value).TotalSeconds > commandTimeout)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var client in timeoutClients)
            {
                connectedClients.TryRemove(client, out _);
                Debug.Log($"Removed timeout client: {client}");
            }

            yield return new WaitForSeconds(1f); // 每秒检查一次
        }
    }

    private void Update()
    {
        // 在主线程中处理命令队列
        while (commandQueue.TryDequeue(out DebugCommand command))
        {
            currentCommand = command.Command;
            UpdateUI();
            ProcessCommand(command);
            commandPool.Release(command); // 处理完后释放回对象池
        }
    }

    private void UpdateUI()
    {
        if (debugText != null)
        {
            debugText.text = $"input: {currentCommand}";
        }
    }

    private void ProcessCommand(DebugCommand command)
    {
        if (logCommands)
        {
            Debug.Log($"[DebugServer] Command from {command.ClientInfo}: {command.Command}");
        }

        // 这里可以添加更多命令处理逻辑
        switch (command.Command.ToLower())
        {
            case "help":
                Debug.Log("[DebugServer] Available commands:\n" +
                         "help - Show this help message\n" +
                         "clear - Clear the console\n" +
                         "clients - Show connected clients");
                break;

            case "clear":
                Debug.ClearDeveloperConsole();
                break;

            case "clients":
                var clients = string.Join("\n", connectedClients.Keys);
                Debug.Log($"[DebugServer] Connected clients:\n{clients}");
                break;
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