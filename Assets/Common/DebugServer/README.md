# DebugServer 模块

## 目录结构

```
DebugServer/
├── Scripts/      # 核心功能代码（主逻辑、数据结构、网络、UI等）
├── Editor/       # 编辑器扩展（自定义Inspector、属性绘制器等）
├── Prefabs/      # 预设体（调试面板、调试对象等）
├── Configs/      # 配置文件（ScriptableObject、JSON等）
├── Test_Attack.cs# 示例/测试脚本
```

## 功能简介
- 支持远端命令行调试：通过局域网 TCP 发送命令，自动映射到 Unity 内部方法。
- Inspector 可视化配置：拖拽对象、填写命令名、选择可调试方法（支持参数、重载、类型校验）。
- 支持方法筛选：仅 `[DebugCallable]` 标记且参数为基础类型的方法可被远端调用。
- 支持命令参数自动解析与类型转换。
- 支持命令历史、性能监控、UI 回显等扩展。

## 详细使用步骤

1. **新建调试 GameObject 并添加核心组件**
   - 在场景中创建一个新的空 GameObject（建议命名为 `DebugServer`）。
   - 添加 `DebugServer` 组件。
   - 在 Inspector 中配置 `DebugConfig`（可用 ScriptableObject 资源或默认配置）。

2. **添加调试 UI（可选）**
   - 在同一个 GameObject 上添加 `DebugUI` 组件。
   - 可在 Inspector 中拖入 TextMeshProUGUI 组件（如 TextMeshPro Text）作为调试信息显示面板。
   - 可根据需要自定义 UI 布局和样式。

3. **添加 DebugInvoker 并配置调试对象和指令**
   - 在同一个 GameObject 上添加 `DebugInvoker` 组件。
   - 在 Inspector 的"调试命令绑定"列表中，点击 `+` 添加一条调试命令：
     - 拖入目标对象（如你的脚本组件或 GameObject）。
     - 填写调试命令名（如 `atk`、`def`）。
     - 下拉选择带 `[DebugCallable]` 标记且参数类型合规的方法。
   - 可添加多条命令，支持参数和方法重载。

4. **在自定义脚本中添加可调试方法**
   - 在你的脚本方法上加 `[DebugCallable]` 特性，并确保参数类型为 int/float/bool/string 或其数组。
   - 例如：
     ```csharp
     [DebugCallable]
     public void Attack(int damage) { ... }
     ```

5. **远端发送命令进行调试**
   - 使用命令行工具或自定义客户端，通过 TCP 发送命令（如 `atk 33`）到调试服务器。
   - 命令会自动映射到 Inspector 配置的方法并执行。

6. **（可选）保存场景和Prefab**
   - 编辑完 Inspector 配置后，务必保存场景（Ctrl+S），如有需要可 Apply 到 Prefab。

## 推荐扩展
- 日志系统：记录所有远端命令和执行结果。
- 权限控制：为敏感命令加白名单或密码。
- 批量导入/导出命令配置（JSON/ScriptableObject）。
- UI/UX 优化：命令分组、备注、搜索。
- 自动化测试支持：模拟远端命令，持续集成。

## 适用场景
- 移动端/主机/PC 游戏的远程调试与自动化测试。
- 多人协作下的高效调试与问题定位。
- 需要低侵入、可扩展、可视化调试工具的 Unity 项目。

---
如需更多高级用法、批量配置、权限、日志等功能，请参考源码注释或联系维护者。 