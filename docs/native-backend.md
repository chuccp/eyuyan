# 自研原生后端

`Yy.Native` 是不依赖 LLVM、Rust、C++ 或 .NET 运行时的原生编译后端。Windows 已实现 Win64 机器码和 PE32+ 输出；macOS Apple Silicon 已实现 ARM64 Mach-O 控制台与常用 AppKit 窗体控件，AppKit Studio 宿主仍在开发。两种后端共享 `Yy.Native/Semantics` 中的类型规则、栈槽模型和值布局。

## 编译流程

```text
yy build 程序.yyc -o 程序.exe
        ↓
易语解析器与语义分析
        ↓
Win64 指令生成
        ↓
PE32+ 节、导入表和机器码写出
        ↓
原生 Windows EXE
```

生成的可执行文件不包含 CLR Header，不依赖 `dotnet`，也不生成其他高级语言源码。当前示例程序体量约为 2–3 KB。

## 隐式主程序

顶层语句直接组成主程序，与 Python 类似：

```text
.程序集 原生示例
导入包 "系统.基础"

变量 总数: 数字 = 0
计次循环 序号 从 1 到 5 步长 1
    总数 = 总数 + 序号
结束循环
输出(总数)
```

不需要 `main` 函数。当前版本也不接受 `.版本` 头，格式版本由工程清单管理。

## 当前能力

- UTF-8 文本和整数常量。
- 数字、文本、逻辑变量与赋值。
- 运行时文本拼接、文本相等比较和堆分配字符串。
- 同类型数组字面量、索引读取、`遍历` 和变量 `追加`。
- 固定形状对象字面量、属性读取和属性赋值。
- UTF-8 文件存在、读取、写入、删除和通配符目录列举。
- `数字(文本)`、`文本(整数)` 原生转换。
- GDI+ 加载 PNG/JPEG 并通过 `STM_SETIMAGE` 显示到 Win32 图片框。
- `运行程序` 通过 `CreateProcessW` 启动子进程、等待退出并返回退出码。
- `+ - * /` 和比较运算。
- `且/&&`、`或/||`、`非/!`。
- `如果`、`当`、`计次循环`、`跳出`、`继续`。
- 最多四个数字/文本参数的基础子程序。
- `输出`、`打印`、`提示` 和文本 `长度`。
- `RegisterClassExW`、`CreateWindowExW`、`ShowWindow` 和原生消息循环。
- Win32 `STATIC`、`BUTTON`、`EDIT`、`COMBOBOX`、`LISTBOX` 控件。
- 按钮单击事件到原生易语子程序的分发。
- `_启动窗口.结果标签.文本`、`_启动窗口.标题` 等窗体结构体的读取与写入；`取控件("@id/...")` 保留为底层动态入口。

## PE 与系统接口

当前 PE 写出器实现：

- DOS/NT 头、PE32+ 可选头和三段节表。
- `.text`、`.rdata`、`.idata`。
- RIP 相对数据和内部代码重定位。
- `kernel32.dll`、`user32.dll` 多 DLL 导入地址表。
- 自定义入口，不链接 C Runtime。

控制台输出直接调用：

```text
GetStdHandle
WriteFile
ExitProcess
```

## macOS Mach-O 后端

`--target macos-arm64` 使用独立的 AArch64 生成器和 Mach-O 写出器：

- `Backends/MacOS/Arm64CodeBuffer.cs`：AArch64 指令、标签、分支和 ADRP 重定位。
- `Backends/MacOS/MacNativeEmitter*.cs`：表达式、控制流、数组/对象、文本、文件、进程和 AppKit 窗体运行时。
- `Backends/MacOS/MachOImageWriter.cs`：`__TEXT/__DATA/__LINKEDIT`、`LC_MAIN`、`LC_DYLD_INFO_ONLY` 绑定和内嵌 ad-hoc 签名。
- `Backends/MacOS/MacNativeBackend.cs`：通过 `INativeBackend` 注册 `macos-arm64`。

系统调用通过 dyld 绑定 `libSystem` 符号实现，不直接依赖私有 syscall ABI。窗体程序另通过 dyld 加载 `libobjc`、Foundation 和 AppKit，直接使用系统 Objective-C 运行时和 AppKit 类，不包含 CLR，也不经过外部链接器。`File.SetUnixFileMode` 只用于设置可执行权限。

`open` 等可变参数系统函数按 AAPCS64 规则把可变参数放在栈上，不占用 `x2`；这是 macOS 后端必须遵守的 ABI 细节。

当前 macOS 后端支持：

- 隐式主程序、子程序、递归和最多 8 个参数。
- 整数、文本、逻辑、数组、对象和 `+=`/`-=`/`*=`/`/=`/`%=`。
- `如果`、`当`、`计次循环`、`遍历`、`跳出`、`继续`。
- 文本拼接、比较、`数字`、`文本`、`长度`、`追加`。
- `文件存在`、`读文件`、`写文件`、`删除文件`、`取目录文件`。
- `运行程序`、退出码和标准输出。
- 窗体工程：`NSApplication`、`NSWindow`、标签、输入框（含多行）、按钮、复选框、单选框、下拉框、列表框、分组框和图片框。
- 窗体载入/打开/关闭、按钮单击、输入改变、选中改变和选择改变事件。
- `打开窗口`/`显示窗口`/`关闭窗口`、模态 `runModalForWindow:`、窗体 `参数`/`结果`、系统关闭按取消处理。
- 内嵌 ad-hoc 签名，生成后可直接在当前 Mac 上运行。

尚未实现：最大长度、窗口图标、AppKit Studio 宿主、`.app` 打包、JSON、异常保护和 `macos-x64`。

## AppKit 窗体

macOS 窗体由同一个 `MacNativeEmitter` 生成，使用 Objective-C 运行时而不是 Win32→Cocoa 映射：

- `objc_getClass`/`sel_registerName`/`objc_msgSend` 连接 AppKit 类和方法。
- `NSApplication` 启动和事件循环；`NSWindow` 创建全部项目窗体并设置标题、尺寸、样式和代理通知。
- `NSTextField`、`NSButton`、`NSPopUpButton`、`NSImageView`、`NSFont`、`NSImage` 对应标签、输入框、按钮、下拉框和图片框。
- 列表框使用 `NSScrollView` + `NSTableView`，初始项目按中英文逗号拆分；项目数组通过关联对象挂在表格上，`.文本` 读取当前选中项，选择改变由表格选择通知分派。
- 分组框使用 `NSBox`，`.文本` 对应分组标题；控件仍采用工程的绝对坐标布局。
- 多行输入框使用 `NSScrollView` + `NSTextView`，文本改变由 `NSTextDidChangeNotification` 分派。
- `NSColor` 应用窗体背景、控件前景/背景、按钮 bevel 色和分组框填充色；颜色在构建期解析为 RGB 分量。
- 控件事件通过 target-action 分派；输入改变、列表选择改变和窗口关闭通过 `NSNotificationCenter` 观察者分派。
- 模态窗口使用 `runModalForWindow:` 和 `stopModalWithCode:`，不创建第二个 UI 线程。
- 图片路径在构建期解析 `@资源/...`，运行时使用 `initWithContentsOfFile:` 加载。

未支持的控件在构建阶段报告明确诊断，不会生成空控件或静默忽略事件。

## 当前边界

完整集合算法、动态键字典、JSON、异常、模块初始化、输入框改变事件和编译期图片资源嵌入尚未进入原生后端。这些程序会停止编译并报告未实现能力，不会回退到 C#/WPF 后端。后续扩展顺序为完整 Win32 控件事件、JSON、异常和编译期资源打包。

后端注册表、共享语义和新增平台流程见 [原生后端架构](backend-architecture.md)。

## 原生窗体

对 `.yyproj` 或项目目录执行：

```powershell
yy build .\examples\原生窗体 -o .\artifacts\原生窗体.exe
```

编译器从项目内全部窗体生成：

- 窗口类和窗口过程。
- 控件句柄表。
- 全部项目窗体及其控件先隐藏创建，再只显示 `启动窗体`。
- `GetMessageW / TranslateMessage / DispatchMessageW` 消息循环。
- `WM_COMMAND` 按钮 ID 到 `.yyc` 事件的映射。
- `SetWindowTextW` 原生文本更新。
- `打开窗口(目标, 拥有者)` 的嵌套消息循环、拥有者禁用/恢复和确认结果。
- `显示窗口(目标, 拥有者)` 非模态显示，以及 `关闭窗口(目标, 确认)`。
- 窗体 `参数`、`结果` 和每次显示前执行的 `打开` 事件。

`examples/窗口交互/` 用于验证完整链路：A 以模态方式打开 B，期间 A 被禁用；B 从参数初始化输入框，确认后 A 根据结果更新文字，取消则丢弃草稿。测试通过 Win32 自动化实际点击按钮、检查启用状态并读取控件文本，不只进行源码检查。

## 平台目标

原生编译入口先解析 `windows-x64`、`macos-arm64`、`macos-x64` 或 `auto`。各目标使用独立后端，不通过模拟 API 互相兼容：

- Windows：Win64 指令、PE32+、Win32 窗口和 GDI+。
- macOS：ARM64 指令、Mach-O、libSystem、libobjc/Foundation/AppKit；常用窗体控件已接入。
- `macos-x64`：尚未注册后端，构建时明确报错。

`NativeBackendRegistry` 只选择已注册的目标。新增平台时不得把其他平台的产物伪装成目标文件，也不得回退到托管运行时。

## 自举方向

引导阶段由 C# 实现解析器以及 Windows/macOS 原生后端。接口边界保持独立，后续可用易语重写同等模块并由现有编译器编译，逐步实现自举；各平台 ABI 和输出格式不依赖引导语言。
