# 项目编译与原生 EXE

## 编辑器与编译器

项目提供两个独立入口：

| 程序 | 职责 |
|---|---|
| `YyStudio.exe` | WPF 可视化 IDE，负责项目、窗体、代码、设计预览和交互 |
| `yy.exe` | 命令行编译器，负责检查、构建和运行 Windows 原生程序 |

`yy.exe` 不引用 Studio 或 WPF。IDE 编译按钮和命令行构建使用同一个 `NativeCompiler`，应用代码只经过自研 Win64 后端。

## 命令行

```powershell
yy check <工程.yyproj|目录>
yy build <工程.yyproj|目录> [-o 输出.exe]
yy native <源码.yyc|工程.yyproj|目录> [-o 输出.exe]
yy run <工程.yyproj|目录>
yy version
```

- `check` 校验工程清单、三文件结构、资源和语言代码，不生成 EXE。
- `build` 和 `native` 是同一原生构建入口；工程默认输出到 `.yyproj` 指定的文件名。
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
        ↓ 自研代码生成
   Win64 机器码
        ↓ 自研 PE 写出器
   PE32+ 原生 Windows EXE
```

后端不调用 LLVM、C#、C++ 或 Rust 编译器，也不生成这些语言的中间源码。可执行文件没有 CLR Header，不加载 WPF，也不依赖 `dotnet`。

`ProjectCompiler` 只负责项目验证和源码汇总，不包含第二套应用发布链。顶层语句组成隐式主程序；存在启动窗体时，原生入口创建 Win32 窗口和菜单消息循环。

## 窗体与事件

原生编译器从 `.yyx` 读取窗体、控件、位置和视觉属性，从 `.Designer.yyc` 读取控件事件到用户子程序的映射。生成结果包括：

- Win32 窗口类和窗口过程。
- `STATIC`、`BUTTON`、`EDIT`、`COMBOBOX`、`LISTBOX` 等原生控件。
- 稳定 `@id/...` 标识到窗口句柄的映射。
- `GetMessageW`、`TranslateMessageW` 和 `DispatchMessageW` 消息循环。
- `WM_COMMAND` 按钮事件分发。
- `SetWindowTextW` 原生文本更新。

`.yyx` 只保存界面；`.Designer.yyc` 只保存自动关系；用户逻辑始终位于 `.yyc`。设计器预览可以使用 Studio 内的 WPF 渲染，但预览不会进入编译产物，也不是编译后端。

## 资源

`Resources/ids.xml` 是窗体和控件稳定标识的索引。资源文件只能位于 `Resources/`，并通过 `@资源/...` 被工程引用。

当前原生后端已经解析窗体和控件标识，但字符串表、图片嵌入、图标、声音和其他二进制资源仍在补齐。未进入原生后端的资源能力必须明确报错，不能回退到托管应用后端。

## 编译产物

- 输出为单个 Windows x64 PE32+ `.exe`。
- 示例控制台程序通常为 2–3 KB，Win32 窗体示例通常约 3.5 KB。
- 产物直接导入 `kernel32.dll` 和 `user32.dll`。
- 产物不包含 C#、WPF、CLR Header 或 .NET 运行时依赖。
- 不需要工程目录中的 `.yyc`、`.yyx` 或 SDK 才能启动。

## 当前边界

原生后端正在持续补齐语言能力。目前覆盖整数、文本常量、运行时文本拼接和比较、基础数组与对象、UTF-8 文件读写和目录列举、变量、算术、控制流、基础子程序、Win32 窗口、动态控件文本读写和部分控件事件；JSON、异常、图片资源嵌入等尚未进入原生后端。

遇到未实现语法时，编译器应停止并报告明确诊断。当前不存在 C#/WPF 应用兼容后端，也不会把不支持的程序静默转换到其他语言或运行时。

完整后端范围和测试矩阵见 [自研 Win64 原生后端](native-backend.md)。
