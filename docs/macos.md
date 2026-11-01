# macOS 原生支持路线

macOS 支持按“共享语义 + 独立平台后端”推进，不回退到托管运行时，也不把 Win32 API 映射成 Cocoa API。当前已完成控制台和 AppKit 窗体原生程序链路；Studio 不再做 AppKit 专用宿主，改用 Avalonia 单一 UI 代码库。

## 状态

| 阶段 | 内容 | 状态 |
|---|---|---|
| 1 | 构建与工具链：macOS 宿主构建解决方案、运行跨平台测试、`build.sh` | 已实现 |
| 2 | 共享语义与后端注册表：`Semantics`、`INativeBackend`、`NativeBackendRegistry` | 已实现 |
| 3 | `macos-arm64` 控制台：ARM64 指令、Mach-O、libSystem、内嵌 ad-hoc 签名 | 已实现 |
| 4 | `macos-x64`：Intel Mac 的 Mach-O 与 SysV x64 后端 | 待实现 |
| 5 | AppKit 窗体/控件：窗口、常用控件、事件、模态会话、图片、多行输入和主题色 | 已实现（常用控件） |
| 6 | Avalonia Studio：同一份跨平台 UI、项目树、编辑器、设计器、MCP、工具窗布局、原生编译/运行控制台 | 部分实现（完整调用栈界面和发布打包待补） |
| 7 | `.app` 打包、通用二进制、签名与公证 | 待实现 |

## 已完成链路

```text
yy / Studio
   ↓  选择 macos-arm64
NativeBackendRegistry
   ↓
MacNativeEmitter (AArch64)
   ↓
MachOImageWriter
   ├─ __TEXT / __DATA / __LINKEDIT
   ├─ LC_DYLD_INFO_ONLY 绑定 libSystem
   └─ 内嵌 ad-hoc CodeDirectory
   ↓
可执行 Mach-O
```

- 文本、数组、对象、控制流、子程序、文件、目录和子进程与 Windows 后端共享同一套语言语义和值布局。
- AppKit 窗体使用 `NSApplication`/`NSWindow`，通过 Objective-C 运行时连接控件、target-action 事件和 `NSNotificationCenter` 通知；模态窗口使用 `runModalForWindow:`。
- 已支持标签、输入框（含多行）、按钮、复选框、单选框、下拉框、列表框、分组框、图片框和主题色，以及窗体载入/打开/关闭、按钮单击、输入改变、选中改变和选择改变事件。
- `macos-x64` 未注册或未实现时构建阶段直接报错；Studio 使用 Avalonia，不依赖 AppKit 专用宿主。
- 测试位于 `tests/Yy.Native.Backends.Tests`：结构检查跨平台运行，真实执行只在 Apple Silicon macOS 上进行。

## 后续阶段

### 4. `macos-x64`

- 新增 SysV x64 指令生成器和 Mach-O x86-64 写出器，复用 `Semantics` 与 `MacNativeEmitter` 的语义分派。
- 不通过 Rosetta 兼容层伪装成 arm64；两个架构分别生成对应 CPU 类型的 Mach-O。
- 在 Intel Mac 或 CI 的 x64 macOS 上执行真实回归。

### 5. AppKit 窗体与控件

- 已通过 Objective-C 运行时（`objc_msgSend`）连接 AppKit，不建立 Win32→Cocoa 模拟层。
- 已实现 `NSApplication`、`NSWindow`、`NSButton`、`NSTextField`、`NSSecureTextField`、`NSTextView`、`NSPopUpButton`、`NSScrollView`/`NSTableView`、`NSBox`、`NSImageView`、`NSFont`、`NSImage` 和 `NSColor` 映射。
- 事件通过 target-action 和通知观察者分派到易语事件入口；`打开窗口` 使用 `runModalForWindow:`，`显示窗口` 使用普通窗口，系统关闭按钮按取消处理。
- 保持 `@id/...`、`参数`、`结果` 和窗体结构体契约不变。
- 已支持多行输入框、控件/窗口前景与背景色、字体和分组框填充色。
- 待补齐：最大长度、窗口图标和 `.app` 资源目录。

### 6. Avalonia Studio 跨平台宿主

- Studio 使用 `src/Yy.Studio.Avalonia` 的一份 Avalonia UI，Windows/macOS 共享，Linux 预留；不再新增 AppKit 或 WPF 专用窗口实现。
- 已接通项目树、文本编辑、保存、检查、编译、运行、PID、标准输出/错误和退出码；可通过 `--smoke` 在无界面模式验证工程编译。
- 设计器、MCP、补全、配置、启动页和工具窗停靠已经迁移到 Avalonia；自定义顶栏已接入，调试器界面与发布打包仍在继续。
- 迁移期 WPF 宿主只作为功能参考，不再新增能力；功能对齐后删除。
- Avalonia Studio 与 `yy` 共用 `ProjectCompiler` 和原生后端。
- Linux 原生后端尚未实现；Studio 可在 Linux 启动，但构建目标必须明确报错。

### 7. 打包与分发

- 生成 `.app`、`Info.plist`、图标和资源目录。
- 支持 arm64/x64 通用二进制。
- 开发者签名、公证和首次运行提示纳入发布脚本。

## 验收原则

- 每个阶段都必须有真实可执行文件或真实 AppKit 交互测试，不只检查源码。
- 未完成阶段必须明确报错，不能生成其他平台产物或回退到 .NET 运行时。
- 新增平台步骤见 [原生后端架构](backend-architecture.md)。
