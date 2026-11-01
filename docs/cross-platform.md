# 单代码库与原生平台目标

易语 Studio 采用“一套公共代码、多套原生平台实现”。Windows 与 macOS 不共享窗口控件，也不通过模拟层把 Win32 API 映射到 Cocoa；语言、工程、编辑器模型和命令保持同一份源码。

## 目录边界

```text
src/
├─ Yy.Language/          词法、解析、AST、诊断：平台无关
├─ Yy.Compiler/          工程、窗体、资源、目标选择：平台无关
├─ Yy.Sdk/              系统包和界面契约：平台无关
├─ Yy.Native/            原生后端选择
│  ├─ Windows/           PE32+、Win32、GDI+（已实现）
│  └─ MacOS/             Mach-O、AppKit、CoreGraphics/CoreText（开发中）
├─ Yy.Studio/
│  ├─ Shared/            文档、命令、编辑器模型和 MCP
│  ├─ Platforms/Windows/ Win32 原生宿主
│  └─ Platforms/MacOS/   AppKit 原生宿主
└─ tools/Yy.Cli/         跨平台命令行入口
```

平台目录通过 MSBuild 条件和 `#if` 选择，共享模型不得复制出 Windows/macOS 两套业务逻辑。

## 目标解析

| 值 | 平台 | 产物 |
|---|---|---|
| `auto` | 当前宿主 | 当前宿主原生产物 |
| `windows-x64` | Windows x64 | PE32+ |
| `macos-arm64` | macOS Apple Silicon | Mach-O arm64 |
| `macos-x64` | macOS Intel | Mach-O x86-64 |

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
| 顶层窗口 | `HWND` | `NSWindow` |
| 项目树 | Win32 `TreeView` | `NSOutlineView` |
| 属性表 | Win32 `ListView` | `NSTableView` |
| 代码编辑 | 原生控件 + DirectWrite | `NSTextView` + CoreText |
| 文件选择 | Win32 对话框 | `NSOpenPanel`、`NSSavePanel` |
| 窗口命令 | `WM_*` | AppKit 通知与 delegate |
| 可执行格式 | PE32+ | Mach-O |

`系统.界面` 的语言 API 保持一致，窗体和控件仍通过 `@id/...`、`参数`、`结果` 和窗口事件关联。适配由平台运行时完成，用户 `.yyc` 不包含平台分支。

## 当前状态

- 目标模型、工程字段、CLI 参数和目标选择已经实现。
- Windows Win32/PE 后端保持可用。
- macOS 目标当前会明确提示尚未接入 AppKit/Mach-O，不会误生成 Windows PE。
- macOS Studio 宿主与原生程序后端在同一代码树中继续实现。
