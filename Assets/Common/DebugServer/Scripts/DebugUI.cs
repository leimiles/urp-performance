using UnityEngine;
using TMPro;
using System.Text;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DebugServer.Models;
using DebugServer.Pooling;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DebugServer.UI
{
    /// <summary>
    /// 调试服务器UI管理器
    /// 负责处理所有UI相关的显示和更新
    /// </summary>
    public class DebugUI : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private TextMeshProUGUI debugText;
        [SerializeField] private TextMeshProUGUI commandHistoryText;
        [SerializeField] private TextMeshProUGUI clientListText;
        [SerializeField] private TextMeshProUGUI performanceStatsText;

        [Header("UI Settings")]
        [SerializeField] private int uiUpdateIntervalMs = 1000;
        [SerializeField] private int maxHistoryLines = 10;
        [SerializeField] private bool showPerformanceStats = true;
        [SerializeField] private bool showClientList = true;
        [SerializeField] private bool showCommandHistory = true;

        // 对象池
        private SimpleObjectPool<StringBuilder> stringBuilderPool;
        
        // 状态
        private DateTime lastUIUpdateTime = DateTime.MinValue;
        private string currentCommand = "";
        private int pendingCommands = 0;
        private readonly Stopwatch commandStopwatch = new Stopwatch();

        // 消息队列
        private readonly Queue<UIMessage> messageQueue = new Queue<UIMessage>();
        private readonly object messageQueueLock = new object();

        private class UIMessage
        {
            public enum MessageType
            {
                UpdateUI,
                ShowError,
                ShowSuccess,
                ShowCommandHistory
            }

            public MessageType Type { get; set; }
            public object[] Parameters { get; set; }
        }

        private void Awake()
        {
            // 初始化对象池
            stringBuilderPool = ObjectPoolManager.GetPool<StringBuilder>(
                createFunc: () => new StringBuilder(),
                onGet: (sb) => sb.Clear(),
                onRelease: (sb) => sb.Clear(),
                maxSize: 50
            );

            // 确保所有UI组件都被正确引用
            ValidateUIComponents();
        }

        private void Update()
        {
            // 处理消息队列
            lock (messageQueueLock)
            {
                while (messageQueue.Count > 0)
                {
                    var message = messageQueue.Dequeue();
                    ProcessMessage(message);
                }
            }
        }

        private void ProcessMessage(UIMessage message)
        {
            switch (message.Type)
            {
                case UIMessage.MessageType.UpdateUI:
                    UpdateUIInternal(
                        (string)message.Parameters[0],
                        (int)message.Parameters[1],
                        (long)message.Parameters[2],
                        (int)message.Parameters[3],
                        (int)message.Parameters[4],
                        (int)message.Parameters[5],
                        (PerformanceStats)message.Parameters[6],
                        (ClientConnection[])message.Parameters[7]
                    );
                    break;
                case UIMessage.MessageType.ShowError:
                    ShowErrorInternal((string)message.Parameters[0]);
                    break;
                case UIMessage.MessageType.ShowSuccess:
                    ShowSuccessInternal((string)message.Parameters[0]);
                    break;
                case UIMessage.MessageType.ShowCommandHistory:
                    ShowCommandHistoryInternal((DebugCommand[])message.Parameters[0]);
                    break;
            }
        }

        private void ValidateUIComponents()
        {
            if (debugText == null)
                Debug.LogWarning("[DebugUI] Debug text component is not assigned!");
            if (commandHistoryText == null)
                Debug.LogWarning("[DebugUI] Command history text component is not assigned!");
            if (clientListText == null)
                Debug.LogWarning("[DebugUI] Client list text component is not assigned!");
            if (performanceStatsText == null)
                Debug.LogWarning("[DebugUI] Performance stats text component is not assigned!");
        }

        /// <summary>
        /// 更新UI显示
        /// </summary>
        public void UpdateUI(
            string currentCommand,
            int pendingCommands,
            long lastProcessTime,
            int connectedClientCount,
            int commandHistoryCount,
            int maxCommandHistory,
            PerformanceStats stats,
            ClientConnection[] connectedClients)
        {
            lock (messageQueueLock)
            {
                messageQueue.Enqueue(new UIMessage
                {
                    Type = UIMessage.MessageType.UpdateUI,
                    Parameters = new object[]
                    {
                        currentCommand,
                        pendingCommands,
                        lastProcessTime,
                        connectedClientCount,
                        commandHistoryCount,
                        maxCommandHistory,
                        stats,
                        connectedClients
                    }
                });
            }
        }

        private void UpdateUIInternal(
            string currentCommand,
            int pendingCommands,
            long lastProcessTime,
            int connectedClientCount,
            int commandHistoryCount,
            int maxCommandHistory,
            PerformanceStats stats,
            ClientConnection[] connectedClients)
        {
            if ((DateTime.Now - lastUIUpdateTime).TotalMilliseconds < uiUpdateIntervalMs)
                return;

            this.currentCommand = currentCommand;
            this.pendingCommands = pendingCommands;
            this.commandStopwatch.Reset();
            this.commandStopwatch.Start();
            this.commandStopwatch.Stop();

            var sb = stringBuilderPool.Get();
            try
            {
                // 更新主调试信息
                if (debugText != null)
                {
                    sb.Clear();
                    sb.AppendLine($"Input: {currentCommand}");
                    sb.AppendLine($"Pending Commands: {pendingCommands}");
                    sb.AppendLine($"Last Process Time: {lastProcessTime}ms");
                    sb.AppendLine($"Connected Clients: {connectedClientCount}");
                    sb.AppendLine($"Command History: {commandHistoryCount}/{maxCommandHistory}");
                    debugText.text = sb.ToString();
                }

                // 更新性能统计
                if (showPerformanceStats && performanceStatsText != null)
                {
                    sb.Clear();
                    sb.AppendLine("Performance Stats:");
                    sb.AppendLine($"Total Commands: {stats.TotalCommands}");
                    sb.AppendLine($"Commands/Second: {stats.CommandsThisSecond}");
                    sb.AppendLine($"Peak Commands/Second: {stats.PeakCommandsPerSecond}");
                    sb.AppendLine($"Avg Process Time: {stats.AverageProcessingTime:F2}ms");
                    performanceStatsText.text = sb.ToString();
                }

                // 更新客户端列表
                if (showClientList && clientListText != null && connectedClients != null)
                {
                    sb.Clear();
                    sb.AppendLine("Connected Clients:");
                    foreach (var client in connectedClients)
                    {
                        sb.AppendLine($"- {client.EndPoint}: {client.CommandCount} commands, " +
                                    $"Last activity: {(DateTime.Now - client.LastActivity).TotalSeconds:F1}s ago");
                    }
                    clientListText.text = sb.ToString();
                }

                // 更新命令历史
                if (showCommandHistory && commandHistoryText != null)
                {
                    sb.Clear();
                    sb.AppendLine("Recent Commands:");
                    // 这里需要从DebugCommandHandler获取历史记录
                    // 暂时留空，等待实现
                    commandHistoryText.text = sb.ToString();
                }
            }
            finally
            {
                stringBuilderPool.Release(sb);
            }

            lastUIUpdateTime = DateTime.Now;
        }

        /// <summary>
        /// 显示命令历史
        /// </summary>
        public void ShowCommandHistory(DebugCommand[] history)
        {
            lock (messageQueueLock)
            {
                messageQueue.Enqueue(new UIMessage
                {
                    Type = UIMessage.MessageType.ShowCommandHistory,
                    Parameters = new object[] { history }
                });
            }
        }

        private void ShowCommandHistoryInternal(DebugCommand[] history)
        {
            if (!showCommandHistory || commandHistoryText == null || history == null)
                return;

            var sb = stringBuilderPool.Get();
            try
            {
                sb.AppendLine("Recent Commands:");
                var count = Math.Min(history.Length, maxHistoryLines);
                for (int i = history.Length - count; i < history.Length; i++)
                {
                    var cmd = history[i];
                    sb.AppendLine($"[{cmd.Timestamp:HH:mm:ss}] {cmd.ClientInfo}: {cmd.Command}");
                }
                commandHistoryText.text = sb.ToString();
            }
            finally
            {
                stringBuilderPool.Release(sb);
            }
        }

        /// <summary>
        /// 显示错误消息
        /// </summary>
        public void ShowError(string message)
        {
            lock (messageQueueLock)
            {
                messageQueue.Enqueue(new UIMessage
                {
                    Type = UIMessage.MessageType.ShowError,
                    Parameters = new object[] { message }
                });
            }
        }

        private void ShowErrorInternal(string message)
        {
            if (debugText != null)
            {
                var sb = stringBuilderPool.Get();
                try
                {
                    sb.AppendLine(debugText.text);
                    sb.AppendLine($"Error: {message}");
                    debugText.text = sb.ToString();
                }
                finally
                {
                    stringBuilderPool.Release(sb);
                }
            }
        }

        /// <summary>
        /// 显示成功消息
        /// </summary>
        public void ShowSuccess(string message)
        {
            lock (messageQueueLock)
            {
                messageQueue.Enqueue(new UIMessage
                {
                    Type = UIMessage.MessageType.ShowSuccess,
                    Parameters = new object[] { message }
                });
            }
        }

        private void ShowSuccessInternal(string message)
        {
            if (debugText != null)
            {
                var sb = stringBuilderPool.Get();
                try
                {
                    sb.AppendLine(debugText.text);
                    sb.AppendLine($"Success: {message}");
                    debugText.text = sb.ToString();
                }
                finally
                {
                    stringBuilderPool.Release(sb);
                }
            }
        }

        private void OnDestroy()
        {
            // 清理对象池
            if (stringBuilderPool != null)
            {
                stringBuilderPool.Clear();
            }
        }
    }
} 