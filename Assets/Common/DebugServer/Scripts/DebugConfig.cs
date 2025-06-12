using UnityEngine;
using System;
using System.Collections.Generic;

namespace DebugServer.Config
{
    /// <summary>
    /// 调试服务器配置
    /// 集中管理所有配置项
    /// </summary>
    [CreateAssetMenu(fileName = "DebugServerConfig", menuName = "DebugServer/Config")]
    public class DebugConfig : ScriptableObject
    {
        [Header("Server Settings")]
        [Tooltip("服务器端口")]
        public int port = 9527;
        [Tooltip("是否自动启动服务器")]
        public bool autoStart = true;
        [Tooltip("最大连接数")]
        public int maxConnections = 3;
        [Tooltip("命令超时时间(秒)")]
        public int commandTimeout = 60;
        [Tooltip("最大命令长度")]
        public int maxCommandLength = 4096;
        [Tooltip("允许连接的IP地址列表")]
        public string[] allowedIPs;
        [Tooltip("是否记录命令日志")]
        public bool logCommands = true;
        [Tooltip("每帧最大处理命令数")]
        public int maxCommandsPerFrame = 5;
        [Tooltip("命令最大处理时间(毫秒)")]
        public int maxProcessingTimeMs = 200;
        [Tooltip("网络缓冲区大小")]
        public int networkBufferSize = 4096;
        [Tooltip("清理间隔(秒)")]
        public int cleanupInterval = 5;
        [Tooltip("最大命令历史记录数")]
        public int maxCommandHistory = 20;

        [Header("Performance Settings")]
        [Tooltip("最小读取间隔(毫秒)")]
        public int minReadIntervalMs = 100;
        [Tooltip("最小命令间隔(毫秒)")]
        public int minCommandIntervalMs = 200;
        [Tooltip("是否启用性能监控")]
        public bool enablePerformanceMonitoring = true;

        [Header("UI Settings")]
        [Tooltip("UI更新间隔(毫秒)")]
        public int uiUpdateIntervalMs = 1000;
        [Tooltip("最大历史记录显示行数")]
        public int maxHistoryLines = 10;
        [Tooltip("是否显示性能统计")]
        public bool showPerformanceStats = true;
        [Tooltip("是否显示客户端列表")]
        public bool showClientList = true;
        [Tooltip("是否显示命令历史")]
        public bool showCommandHistory = true;

        // 单例实例
        private static DebugConfig instance;
        public static DebugConfig Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = Resources.Load<DebugConfig>("DebugServerConfig");
                    if (instance == null)
                    {
                        Debug.LogWarning("[DebugConfig] Config file not found in Resources folder, using default settings.");
                        instance = CreateInstance<DebugConfig>();
                    }
                }
                return instance;
            }
        }

        // 验证配置
        public void Validate()
        {
            port = Mathf.Clamp(port, 1024, 65535);
            maxConnections = Mathf.Max(1, maxConnections);
            commandTimeout = Mathf.Max(1, commandTimeout);
            maxCommandLength = Mathf.Max(1, maxCommandLength);
            maxCommandsPerFrame = Mathf.Max(1, maxCommandsPerFrame);
            maxProcessingTimeMs = Mathf.Max(1, maxProcessingTimeMs);
            networkBufferSize = Mathf.Max(1024, networkBufferSize);
            cleanupInterval = Mathf.Max(1, cleanupInterval);
            maxCommandHistory = Mathf.Max(1, maxCommandHistory);
            minReadIntervalMs = Mathf.Max(0, minReadIntervalMs);
            minCommandIntervalMs = Mathf.Max(0, minCommandIntervalMs);
            uiUpdateIntervalMs = Mathf.Max(100, uiUpdateIntervalMs);
            maxHistoryLines = Mathf.Max(1, maxHistoryLines);
        }

        // 重置为默认值
        public void ResetToDefaults()
        {
            port = 9527;
            autoStart = true;
            maxConnections = 3;
            commandTimeout = 60;
            maxCommandLength = 4096;
            allowedIPs = new string[0];
            logCommands = true;
            maxCommandsPerFrame = 5;
            maxProcessingTimeMs = 200;
            networkBufferSize = 4096;
            cleanupInterval = 5;
            maxCommandHistory = 20;
            minReadIntervalMs = 100;
            minCommandIntervalMs = 200;
            enablePerformanceMonitoring = true;
            uiUpdateIntervalMs = 1000;
            maxHistoryLines = 10;
            showPerformanceStats = true;
            showClientList = true;
            showCommandHistory = true;
        }
    }
} 