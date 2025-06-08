using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

[Serializable]
public class DebugCommandBinding
{
    public GameObject target;
    public string commandName;      // 远端命令名
    public string methodSignature;  // 完整方法签名，如 Attack(), Attack(int, float), Attack(int[])
}

public class DebugInvoker : MonoBehaviour
{
    [SerializeField]
    public List<DebugCommandBinding> bindings = new List<DebugCommandBinding>();


    void Start()
    {

    }

    void Update()
    {

    }

    /// <summary>
    /// 运行时根据命令名和参数自动匹配并调用目标方法（支持重载）
    /// </summary>
    public bool InvokeCommand(string command, string[] args)
    {
        foreach (var binding in bindings)
        {
            if (binding == null || binding.target == null) continue;
            if (!string.Equals(binding.commandName, command, StringComparison.OrdinalIgnoreCase)) continue;

            // 查找目标方法
            var components = binding.target.GetComponents<MonoBehaviour>();
            foreach (var comp in components)
            {
                var type = comp.GetType();
                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                    .Where(m => GetMethodSignature(m) == binding.methodSignature);
                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    if (TryParseParameters(args, parameters, out object[] parsedArgs))
                    {
                        method.Invoke(comp, parsedArgs);
                        Debug.Log($"[DebugInvoker] Invoked {type.Name}.{method.Name}({string.Join(", ", parsedArgs)})");
                        return true;
                    }
                }
            }
        }
        Debug.LogWarning($"[DebugInvoker] No matching binding for command: {command} with args: {string.Join(",", args)}");
        return false;
    }

    /// <summary>
    /// 生成方法签名字符串
    /// </summary>
    public static string GetMethodSignature(MethodInfo m)
    {
        var ps = m.GetParameters();
        if (ps.Length == 0) return $"{m.Name}()";
        return $"{m.Name}({string.Join(", ", ps.Select(p => p.ParameterType.Name))})";
    }

    /// <summary>
    /// 参数解析（支持int/float/bool/string/数组）
    /// </summary>
    private static bool TryParseParameters(string[] args, ParameterInfo[] parameters, out object[] parsedArgs)
    {
        parsedArgs = new object[parameters.Length];
        if (args.Length != parameters.Length) return false;
        for (int i = 0; i < parameters.Length; i++)
        {
            var type = parameters[i].ParameterType;
            try
            {
                if (type == typeof(int))
                    parsedArgs[i] = int.Parse(args[i]);
                else if (type == typeof(float))
                    parsedArgs[i] = float.Parse(args[i]);
                else if (type == typeof(bool))
                    parsedArgs[i] = bool.Parse(args[i]);
                else if (type == typeof(string))
                    parsedArgs[i] = args[i];
                else if (type == typeof(int[]))
                    parsedArgs[i] = args[i].Split(',').Select(int.Parse).ToArray();
                else if (type == typeof(float[]))
                    parsedArgs[i] = args[i].Split(',').Select(float.Parse).ToArray();
                else
                    return false;
            }
            catch { return false; }
        }
        return true;
    }

}
