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

            // 方法
            if (binding.target != null)
            {
                var allMethods = new List<MethodInfo>();
                foreach (var comp in binding.target.GetComponents<MonoBehaviour>())
                    allMethods.AddRange(comp.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly));
                var methodSigs = allMethods.Select(DebugInvoker.GetMethodSignature).Distinct().ToArray();
                int selected = Mathf.Max(0, Array.IndexOf(methodSigs, binding.methodSignature));
                selected = EditorGUI.Popup(
                    new Rect(rect.x, y, rect.width, lineH), "方法", selected, methodSigs);
                binding.methodSignature = methodSigs.Length > 0 ? methodSigs[selected] : "";
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
}
#endif
