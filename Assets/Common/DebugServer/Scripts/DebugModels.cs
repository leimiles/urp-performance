using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;

namespace DebugServer.Models
{
    /// <summary>
    /// 调试命令数据结构
    /// </summary>
    public class DebugCommand
    {
        public string Command { get; set; }
        public DateTime Timestamp { get; set; }
        public string ClientInfo { get; set; }
    }

    /// <summary>
    /// 客户端连接信息
    /// </summary>
    public class ClientConnection
    {
        public TcpClient Client { get; set; }
        public DateTime LastActivity { get; set; }
        public int CommandCount { get; set; }
        public string EndPoint { get; set; }
        private readonly int timeout;

        public ClientConnection(int timeout)
        {
            this.timeout = timeout;
            LastActivity = DateTime.Now;
        }

        public bool IsActive => (DateTime.Now - LastActivity).TotalSeconds < timeout;
        public void UpdateActivity() => LastActivity = DateTime.Now;
    }

    /// <summary>
    /// 性能统计数据
    /// </summary>
    public class PerformanceStats
    {
        public int TotalCommands { get; set; }
        public int CommandsThisSecond { get; set; }
        public float AverageProcessingTime { get; set; }
        public int PeakCommandsPerSecond { get; set; }
        public DateTime LastResetTime { get; set; }
        public readonly Queue<float> ProcessingTimes = new Queue<float>();
        public readonly int MaxProcessingTimeSamples = 50;

        public void AddProcessingTime(float time)
        {
            ProcessingTimes.Enqueue(time);
            if (ProcessingTimes.Count > MaxProcessingTimeSamples)
            {
                ProcessingTimes.Dequeue();
            }
            AverageProcessingTime = ProcessingTimes.Average();
        }

        public void Reset()
        {
            CommandsThisSecond = 0;
            LastResetTime = DateTime.Now;
        }
    }
} 