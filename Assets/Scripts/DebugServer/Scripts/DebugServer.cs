using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;

/// <summary>
/// Unity App 内嵌 TCP 服务，仅在启用宏 INCLUDE_LOCAL_NETWORK_DEBUG 时启动
/// 局域网中的命令行工具（Mac/Linux/Windows）通过 IP+端口连接手机
/// 工具输入命令触发 Unity 调试逻辑（如 spawn、setvar、log 等）
/// 易于扩展命令集，支持本地测试调试
/// </summary>

public class DebugServer : MonoBehaviour
{
#if INCLUDE_LOCAL_NETWORK_DEBUG
    [SerializeField] private int port = 9527;
    [SerializeField] private TextMeshProUGUI debugText; // 用于显示调试信息的UI文本组件

    private TcpListener server;
    private Thread serverThread;
    private bool isRunning = false;
    private string receivedCommand = "";

    private void Start()
    {
        StartServer();
    }

    private void StartServer()
    {
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
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to start debug server: {e.Message}");
        }
    }

    private void ListenForClients()
    {
        while (isRunning)
        {
            try
            {
                TcpClient client = server.AcceptTcpClient();
                HandleClient(client);
            }
            catch (System.Exception e)
            {
                if (isRunning)
                {
                    Debug.LogError($"Error accepting client: {e.Message}");
                }
            }
        }
    }

    private void HandleClient(TcpClient client)
    {
        try
        {
            NetworkStream stream = client.GetStream();
            byte[] buffer = new byte[1024];
            int bytesRead;

            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
            {
                string command = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                receivedCommand = command.Trim();
                Debug.Log($"Received command: {receivedCommand}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error handling client: {e.Message}");
        }
        finally
        {
            client.Close();
        }
    }

    private void Update()
    {
        if (!string.IsNullOrEmpty(receivedCommand) && debugText != null)
        {
            debugText.text = $"Last received command: {receivedCommand}";
        }
    }

    private void OnDestroy()
    {
        isRunning = false;
        if (server != null)
        {
            server.Stop();
        }
        if (serverThread != null)
        {
            serverThread.Join();
        }
    }
#endif
} 