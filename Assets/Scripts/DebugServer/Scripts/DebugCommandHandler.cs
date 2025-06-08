using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using DebugServer.Models;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace DebugServer
{
    /// <summary>
    /// 命令参数解析结果
    /// </summary>
    public class CommandParameters
    {
        public string Command { get; set; }
        public string[] Arguments { get; set; }
        public Dictionary<string, string> NamedArguments { get; set; }
        public string RawInput { get; set; }

        public CommandParameters()
        {
            Arguments = Array.Empty<string>();
            NamedArguments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        public bool HasArgument(int index) => index >= 0 && index < Arguments.Length;
        public string GetArgument(int index) => HasArgument(index) ? Arguments[index] : null;
        public bool HasNamedArgument(string name) => NamedArguments.ContainsKey(name);
        public string GetNamedArgument(string name) => NamedArguments.TryGetValue(name, out var value) ? value : null;
    }

    /// <summary>
    /// 调试命令处理器
    /// 负责处理所有调试命令的执行和注册
    /// </summary>
    public class DebugCommandHandler
    {
        // 命令处理委托
        private delegate void CommandAction(CommandParameters parameters, DebugCommand command);
        
        // 命令注册表
        private readonly Dictionary<string, CommandAction> commandHandlers;
        
        // 命令历史记录
        private readonly Queue<DebugCommand> commandHistory;
        private readonly int maxCommandHistory;
        
        // 性能监控
        private readonly PerformanceStats stats;
        private readonly Stopwatch commandStopwatch;
        private readonly int maxProcessingTimeMs;
        
        // 事件
        public event Action<string> OnCommandProcessed;
        public event Action<string> OnCommandError;

        public DebugCommandHandler(int maxCommandHistory, int maxProcessingTimeMs, PerformanceStats stats)
        {
            this.maxCommandHistory = maxCommandHistory;
            this.maxProcessingTimeMs = maxProcessingTimeMs;
            this.stats = stats;
            
            commandHandlers = new Dictionary<string, CommandAction>(StringComparer.OrdinalIgnoreCase);
            commandHistory = new Queue<DebugCommand>();
            commandStopwatch = new Stopwatch();
            
            // 注册默认命令
            RegisterDefaultCommands();
        }

        /// <summary>
        /// 解析命令参数
        /// </summary>
        private CommandParameters ParseCommand(string input)
        {
            var parameters = new CommandParameters { RawInput = input };
            
            if (string.IsNullOrEmpty(input))
                return parameters;

            var parts = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return parameters;

            parameters.Command = parts[0].ToLower();
            parameters.Arguments = new string[parts.Length - 1];
            Array.Copy(parts, 1, parameters.Arguments, 0, parts.Length - 1);

            // 解析命名参数 (--name=value 或 -n=value 格式)
            for (int i = 0; i < parameters.Arguments.Length; i++)
            {
                var arg = parameters.Arguments[i];
                if (arg.StartsWith("--") || arg.StartsWith("-"))
                {
                    var nameValue = arg.Substring(arg.StartsWith("--") ? 2 : 1).Split('=');
                    if (nameValue.Length == 2)
                    {
                        parameters.NamedArguments[nameValue[0]] = nameValue[1];
                        parameters.Arguments[i] = null; // 标记为已处理
                    }
                }
            }

            // 移除已处理的命名参数
            parameters.Arguments = parameters.Arguments.Where(a => a != null).ToArray();
            
            return parameters;
        }

        /// <summary>
        /// 注册默认命令
        /// </summary>
        private void RegisterDefaultCommands()
        {
            RegisterCommand("help", (parameters, cmd) => {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("[DebugServer] Available commands:");
                sb.AppendLine("help [--verbose] - Show this help message");
                sb.AppendLine("clear - Clear the console");
                sb.AppendLine("clients [--timeout=seconds] - Show connected clients");
                sb.AppendLine("delay <ms> - Simulate command delay (for testing)");
                sb.AppendLine("history [--count=number] - Show command history");
                
                if (parameters.HasNamedArgument("verbose"))
                {
                    sb.AppendLine("\nDetailed command information:");
                    foreach (var command in GetRegisteredCommands())
                    {
                        sb.AppendLine($"- {command}");
                    }
                }
                
                Debug.Log(sb.ToString());
            });

            RegisterCommand("clear", (parameters, cmd) => {
                Debug.ClearDeveloperConsole();
            });

            RegisterCommand("clients", (parameters, cmd) => {
                var timeout = parameters.HasNamedArgument("timeout") 
                    ? float.Parse(parameters.GetNamedArgument("timeout"))
                    : 60f;
                
                var clients = string.Join("\n", cmd.ClientInfo.Split('\n')
                    .Select(c => $"{c}: commands, Last activity: {(DateTime.Now - cmd.Timestamp).TotalSeconds:F1}s ago"));
                Debug.Log($"[DebugServer] Connected clients:\n{clients}");
            });

            RegisterCommand("history", (parameters, cmd) => {
                var count = parameters.HasNamedArgument("count")
                    ? int.Parse(parameters.GetNamedArgument("count"))
                    : commandHistory.Count;
                
                count = Math.Min(count, commandHistory.Count);
                var history = string.Join("\n", commandHistory.TakeLast(count).Select(c => 
                    $"[{c.Timestamp:HH:mm:ss}] {c.ClientInfo}: {c.Command}"));
                Debug.Log($"[DebugServer] Command history (last {count} commands):\n{history}");
            });
        }

        /// <summary>
        /// 注册新命令
        /// </summary>
        public void RegisterCommand(string commandName, Action<CommandParameters, DebugCommand> handler)
        {
            if (string.IsNullOrEmpty(commandName))
                throw new ArgumentNullException(nameof(commandName));
            
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            commandHandlers[commandName] = (parameters, cmd) => {
                try
                {
                    handler(parameters, cmd);
                    OnCommandProcessed?.Invoke($"Command '{commandName}' executed successfully");
                }
                catch (Exception e)
                {
                    OnCommandError?.Invoke($"Error executing command '{commandName}': {e.Message}");
                }
            };
        }

        /// <summary>
        /// 处理命令
        /// </summary>
        public bool ProcessCommand(DebugCommand command)
        {
            if (command == null || string.IsNullOrEmpty(command.Command))
                return false;

            commandStopwatch.Restart();
            try
            {
                // 添加到历史记录
                commandHistory.Enqueue(command);
                while (commandHistory.Count > maxCommandHistory)
                {
                    commandHistory.Dequeue();
                }

                // 解析命令参数
                var parameters = ParseCommand(command.Command);
                
                // 处理特殊命令
                if (parameters.Command == "delay" && parameters.HasArgument(0))
                {
                    if (int.TryParse(parameters.GetArgument(0), out int delayMs))
                    {
                        System.Threading.Thread.Sleep(delayMs);
                        return true;
                    }
                    return false;
                }

                // 查找并执行命令处理器
                if (commandHandlers.TryGetValue(parameters.Command, out CommandAction handler))
                {
                    handler(parameters, command);
                    return true;
                }

                OnCommandError?.Invoke($"Unknown command: {parameters.Command}");
                return false;
            }
            catch (Exception e)
            {
                OnCommandError?.Invoke($"Error processing command: {e.Message}");
                return false;
            }
            finally
            {
                commandStopwatch.Stop();
                if (commandStopwatch.ElapsedMilliseconds > maxProcessingTimeMs)
                {
                    Debug.LogWarning($"Command processing took too long: {commandStopwatch.ElapsedMilliseconds}ms\nCommand: {command.Command}");
                }
            }
        }

        /// <summary>
        /// 获取命令历史记录
        /// </summary>
        public DebugCommand[] GetCommandHistory()
        {
            return commandHistory.ToArray();
        }

        /// <summary>
        /// 获取已注册的命令列表
        /// </summary>
        public string[] GetRegisteredCommands()
        {
            return commandHandlers.Keys.ToArray();
        }
    }
} 