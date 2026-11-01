# 单代码库与原生平台目标

易语 Studio 采用“一份 Avalonia Studio UI + 多套原生平台后端”。项目树、菜单、编辑器和工作区由 Avalonia 在 Windows/macOS 共享，并为 Linux 预留；用户程序仍使用各平台原生窗口控件，不通过模拟层把 Win32 API 映射到 Cocoa。语言、工程、SDK 和编译语义保持同一份源码。

## 目录边界

```text
src/
├─ Yy.Language/          词法、解析、AST、诊断：平台无关
├─ Yy.Compiler/          工程、窗体、资源、目标选择：平台无关
├─ Yy.Sdk/               系统包和界面契约：平台无关
├─ Yy.Native/
│  ├─ Semantics/         类型推断、栈槽、值布局和内置命令目录：平台无关
│  └─ Backends/
│     ├─ NativeBackend.cs  后端接口与注册表
│     ├─ Windows/          x64、PE32+、Win32、GDI+（已实现）
│     └─ MacOS/            ARM64、Mach-O、libSystem、AppKit（控制台与常用窗体控件已实现）
├─ Yy.Studio.Avalonia/   跨平台 Avalonia Studio（Windows/macOS，Linux 预留）
├─ legacy/Yy.Studio.Wpf/ 迁移期 WPF 参考宿主（Windows）
└─ tools/Yy.Cli/         跨平台命令行入口
```

共享语义位于 `Yy.Native/Semantics`，平台差异只出现在 `Yy.Native/Backends/<平台>`。新增平台通过 `INativeBackend` 注册，不在编译器或 CLI 中增加平台分支，详见 [原生后端架构](backend-architecture.md)。

## 目标解析

| 值 | 平台 | 产物 | 状态 |
|---|---|---|---|
| `auto` | 当前宿主 | 当前宿主原生产物 | 已接入 |
| `windows-x64` | Windows x64 | PE32+ | 已实现（含 Win32 窗体） |
| `macos-arm64` | macOS Apple Silicon | Mach-O arm64 | 控制台与常用 AppKit 窗体控件已实现 |
| `macos-x64` | macOS Intel | Mach-O x86-64 | 待实现，构建时明确报错 |

解析优先级：

1. CLI `--target`；
2. `.yyproj` 的 `目标平台`；
3. 当前操作系统和进程架构；
4. 不支持的环境明确报错。

```powershell
yy build .\examples\窗口交互 --target windows-x64 -o .\artifacts\窗口交互.exe
yy build .\examples\窗口交互 --target macos-arm64 -o .\artifacts\窗口交互
```

## 平台职责

| 能力 | Windows | macOS |
|---|---|---|
| Studio 工作区 | Avalonia | Avalonia |
| 项目树 | Avalonia `TreeView` | Avalonia `TreeView` |
| 代码编辑 | Avalonia `TextBox`（迁移起点） | Avalonia `TextBox`（迁移起点） |
| 文件选择 | Avalonia StorageProvider | Avalonia StorageProvider |
| 用户程序窗口 | Win32 `HWND` | `NSWindow` |
| 用户程序控件 | Win32 控件 | AppKit 控件 |
| 用户程序事件 | `WM_*` | AppKit 通知与 target-action |
| 用户程序格式 | PE32+ | Mach-O |

`系统.界面` 的语言 API 保持一致，窗体和控件仍通过 `@id/...`、`参数`、`结果` 和窗口事件关联。适配由平台运行时完成，用户 `.yyc` 不包含平台分支。

## 当前状态

- 目标模型、工程字段、CLI 参数和目标选择已经实现。
- Windows Win32/PE 后端保持可用。
- `macos-arm64` 已生成可直接运行的 ARM64 Mach-O 控制台和 AppKit 窗体程序，使用 libSystem/libobjc/Foundation/AppKit 导入和内嵌 ad-hoc 签名；`macos-x64` 仍会明确提示后端未实现。
- AppKit 已支持标签、输入框（含多行）、按钮、复选框、单选框、下拉框、列表框、分组框、图片框、常用事件和主题色；最大长度和窗口图标仍在开发中。
- Avalonia Studio 已在 macOS 真实启动，可打开工程、使用经典/纯代码编辑、窗体设计器、预览、MCP、设置和工具窗布局，并检查、编译和运行原生程序；自定义顶栏已接入，完整调用栈界面仍在完善。
- 共享语义、后端注册表和新增平台流程见 [原生后端架构](backend-architecture.md)。
- Linux 原生编译后端尚未实现；Avalonia Studio 可以在 Linux 启动，但构建目标会明确报错。新增 `linux-*` 目标时必须完成注册表、CLI、文档和真实可执行文件测试。
- 剩余 macOS 工作按 [macOS 原生支持路线](macos.md) 分阶段实施。
