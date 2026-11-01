# 项目编译与原生 EXE

## 编辑器与编译器

项目提供两个独立入口：

| 程序 | 职责 |
|---|---|
| `YyStudio` | Avalonia 跨平台可视化 IDE；Windows/macOS 已可运行，Linux 预留；设计器、MCP、设置和工具窗布局已迁移 |
| `yy.exe` / `yy` | 命令行编译器，负责检查、按目标构建和运行本机原生程序 |

命令行工具不引用 Studio、Avalonia 或 WPF。Avalonia Studio 的编译按钮和命令行构建使用同一个 `NativeCompiler`；当前 Windows 目标经过自研 Win64 后端，macOS 目标使用独立的 Mach-O/AppKit 后端。迁移期 WPF 参考宿主仍可在 Windows 运行。

## 命令行

```powershell
yy check <工程.yyproj|目录>
yy build <源码.yyc|工程.yyproj|目录> [--target 目标] [-o 输出]
yy run <工程.yyproj|目录>
yy version
```

- `check` 校验工程清单、三文件结构、资源和语言代码，不生成 EXE。
- `build` 是唯一应用构建入口，直接接收源码、工程清单或目录；工程默认输出到 `.yyproj` 指定的文件名。
- `build` 没有目标框架、运行时标识或独立/框架依赖选项。
- `run` 在临时目录构建并启动原生程序，程序退出后清理临时产物。
- 开发命令行编译器需要安装 .NET 10 SDK；生成的应用不需要 .NET、CLR 或 C Runtime。

## 统一编译流程

```text
.yyproj 项目
  ├─ Sources/
  │  ├─ 入口 .yyc
  │  ├─ 模块 .yyc
  │  ├─ 窗体 .yyx
  │  ├─ 自动关系 .Designer.yyc（纯代码）
  │  └─ 用户代码 .yyc
  └─ Resources/
        ↓ 解析、模块合并与语义检查
      易语 AST
        ↓ 目标无关语义检查
   原生后端选择
     ├─ Windows：Win64 → PE32+
     └─ macOS：ARM64/x86-64 → Mach-O
```

后端不调用 LLVM、C#、C++ 或 Rust 编译器，也不生成这些语言的中间源码。生成的用户程序不加载 IDE UI，也不依赖 `dotnet`；Windows 产物没有 CLR Header，macOS 产物目标为原生 Mach-O。

`ProjectCompiler` 只负责项目验证和源码汇总，不包含第二套应用发布链。顶层语句组成隐式主程序；存在启动窗体时，原生入口创建 Win32 窗口和菜单消息循环。

## 窗体与事件

原生编译器从 `.yyx` 读取窗体、控件、位置和视觉属性，从 `.Designer.yyc` 读取窗体结构体、控件事件和用户子程序映射。用户代码使用 `_启动窗口.结果标签.文本`、`_启动窗口.标题` 等生成上下文，不需要手写 `@id/...`。生成结果包括：

- Win32 窗口类和窗口过程。
- `STATIC`、`BUTTON`、`EDIT`、`COMBOBOX`、`LISTBOX` 等原生控件。
- 稳定 `@id/...` 标识到窗口句柄的映射。
- `GetMessageW`、`TranslateMessageW` 和 `DispatchMessageW` 消息循环。
- `WM_COMMAND` 按钮事件分发。
- `SetWindowTextW` 原生文本更新。
- 项目内多窗体的隐藏创建、VS 式拥有者/模态循环、`参数`/`结果` 和确认/取消语义。

`.yyx` 只保存界面；`.Designer.yyc` 用 `文件`、`界面`、`事件` 三个结构体保存 `@id/...` 映射和事件关系；用户逻辑始终位于 `.yyc`。IDE 中 `F5` 编译并运行当前工程，底部控制台显示编译、PID、输出和退出码；`Ctrl+Enter` 才使用 Studio 内的独立设计预览。两者都不会改变编译后端。

## 原生构建目标

语言前端、工程模型和 SDK 只维护一份源码。目标由命令参数、工程默认值和宿主环境共同确定：

| 目标 | 输出 | 系统界面 | 状态 |
|---|---|---|---|
| `windows-x64` | PE32+ | Win32、GDI+/DirectWrite | 已实现 |
| `macos-arm64` | Mach-O arm64 | 常用 AppKit 窗体控件已实现 | 控制台与窗体可用 |
| `macos-x64` | Mach-O x86-64 | AppKit、CoreGraphics/CoreText | 待实现 |
| `auto` | 当前宿主目标 | 当前原生宿主 | 已接入 |

CLI 使用 `--target` 覆盖工程默认目标。目标后端未实现时必须直接报错，不能把 Windows PE 当作 macOS 程序输出。后端选择由 `NativeBackendRegistry` 完成，新增平台不需要修改 CLI 或 IDE。

## 资源

`Resources/ids.xml` 是窗体和控件稳定标识的索引。资源文件只能位于 `Resources/`，并通过 `@资源/...` 被工程引用。

当前原生后端已经解析窗体和控件标识，但字符串表、图片嵌入、图标、声音和其他二进制资源仍在补齐。未进入原生后端的资源能力必须明确报错，不能回退到托管应用后端。

## 编译产物

- Windows 输出为单个 x64 PE32+ `.exe`，直接导入 `kernel32.dll`、`user32.dll` 等系统 DLL，不包含 CLR Header 或 .NET 运行时依赖。
- macOS 输出为单个 arm64 Mach-O，通过 dyld 绑定 `libSystem`；窗体程序另加载 `libobjc`、Foundation 和 AppKit。产物内嵌 ad-hoc 签名，可直接在当前 Mac 上运行。
- 示例控制台程序通常为 2–3 KB（Windows PE）或约 33 KB（含 Mach-O 段、绑定表和签名）。
- macOS 产物不包含 CLR，也不需要外部链接器或签名工具。
- 窗体工程已支持常用 AppKit 控件、多行输入框、事件和主题色；最大长度和窗口图标当前未实现。

## 平台脚本

- Windows：`build.ps1` 运行全部测试、编译 Windows 示例、交叉编译 macOS 控制台和窗体示例并发布 `win-x64`。
- macOS/Linux：`build.sh` 构建解决方案、运行跨平台测试、编译 Windows/macOS 控制台与窗体示例并发布宿主的 `yy`。
- 不需要工程目录中的 `.yyc`、`.yyx` 或 SDK 才能启动。

## 当前边界

Windows 原生后端正在持续补齐语言能力。目前覆盖整数、文本常量、运行时文本拼接和比较、基础数组与对象、UTF-8 文件读写和目录列举、子进程、变量、算术、控制流、基础子程序、Win32 窗口、动态控件文本读写、PNG/JPEG 图片框和部分控件事件；JSON、异常、编译期图片资源嵌入等尚未进入原生后端。

遇到未实现语法时，编译器应停止并报告明确诊断。当前不存在 C#/Avalonia/WPF 应用兼容后端，也不会把不支持的程序静默转换到其他语言或运行时。

完整后端范围和测试矩阵见 [原生后端](native-backend.md)，平台拆分见 [跨平台原生架构](cross-platform.md)。
