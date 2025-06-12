using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
using System;
using System.Reflection;
using System.Linq;
using UnityEditorInternal;

[CustomEditor(typeof(DebugInvoker))]
public class DebugInvokerEditor : Editor
{
    private ReorderableList reorderableList;

    private void OnEnable()
    {
        var invoker = (DebugInvoker)target;
        if (invoker.bindings == null)
            invoker.bindings = new List<DebugCommandBinding>();
        reorderableList = new ReorderableList(invoker.bindings, typeof(DebugCommandBinding), true, true, true, true);
        reorderableList.drawHeaderCallback = (Rect rect) => {
            EditorGUI.LabelField(rect, "调试命令绑定 (可排序)", EditorStyles.boldLabel);
        };
        reorderableList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) => {
            var invokerT = (DebugInvoker)target;
            var binding = invokerT.bindings[index];
            float y = rect.y + 2;
            float lineH = EditorGUIUtility.singleLineHeight;
            float spacing = 2f;

            // 目标对象
            binding.target = (GameObject)EditorGUI.ObjectField(
                new Rect(rect.x, y, rect.width, lineH), "目标对象", binding.target, typeof(GameObject), true);
            y += lineH + spacing;

            // 调试命令
            binding.commandName = EditorGUI.TextField(
                new Rect(rect.x, y, rect.width, lineH), "调试命令", binding.commandName);
            y += lineH + spacing;

            // 方法分组与过滤
            if (binding.target != null)
            {
                var allMethods = new List<(string group, string display, string signature, MethodInfo method)>();
                foreach (var comp in binding.target.GetComponents<Component>())
                {
                    var compType = comp.GetType();
                    // 只显示带[DebugCallable]且参数类型合规的方法
                    var methods = compType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                        .Where(m =>
                            m.GetCustomAttribute(typeof(DebugCallableAttribute)) != null &&
                            !m.IsSpecialName &&
                            !m.IsGenericMethod &&
                            m.GetParameters().All(p => IsSupportedType(p.ParameterType))
                        )
                        .ToArray();
                    foreach (var m in methods)
                    {
                        string sig = DebugInvoker.GetMethodSignature(m);
                        string display = $"{compType.Name}/{sig}";
                        allMethods.Add((compType.Name, display, sig, m));
                    }
                }
                var methodDisplays = allMethods.Select(x => x.display).ToArray();
                int selected = Mathf.Max(0, Array.IndexOf(allMethods.Select(x => x.signature).ToArray(), binding.methodSignature));
                selected = EditorGUI.Popup(
                    new Rect(rect.x, y, rect.width, lineH), "方法", selected, methodDisplays);
                if (allMethods.Count > 0)
                {
                    binding.methodSignature = allMethods[selected].signature;
                }
            }
        };
        reorderableList.elementHeightCallback = (index) => {
            // 3行控件 + 间距
            return EditorGUIUtility.singleLineHeight * 3 + 8;
        };
        reorderableList.onAddCallback = (ReorderableList list) => {
            list.list.Add(new DebugCommandBinding());
        };
        reorderableList.onRemoveCallback = (ReorderableList list) => {
            list.list.RemoveAt(list.index);
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        reorderableList.DoLayoutList();
        serializedObject.ApplyModifiedProperties();
    }

    private static bool IsSupportedType(Type t)
    {
        return t == typeof(int) || t == typeof(float) || t == typeof(string) || t == typeof(bool)
            || t == typeof(int[]) || t == typeof(float[]) || t == typeof(string[]) || t == typeof(bool[]);
    }
}
#endif
