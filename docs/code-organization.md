# 代码模块与体积约束

易语 Studio 正在整体迁移到 Avalonia。`src/Yy.Studio.Avalonia` 是唯一继续开发的 Studio UI，平台无关的工程会话和模型位于 `src/Yy.Studio.Core`；迁移期 WPF `legacy/Yy.Studio.Wpf` 只保留为功能对照，不再新增能力，功能对齐后删除。纯业务逻辑进入 Core 服务，不进入窗口类。

原生编译器同样按层拆分：`src/Yy.Native/Semantics` 保存平台无关类型分析和值模型，`src/Yy.Native/Backends/<平台>` 保存代码生成与可执行文件写出。新增平台只增加后端目录并注册到 `NativeBackendRegistry`，不得修改共享语义或复制语言规则。

## Avalonia Studio

| 文件 | 职责 |
|---|---|
| `src/Yy.Studio.Avalonia/App.axaml` | Avalonia 应用、Fluent 主题和 AvaloniaEdit 主题 |
| `src/Yy.Studio.Avalonia/MainWindow.axaml` | 顶栏、项目树、文档区、控制台和状态栏 |
| `src/Yy.Studio.Avalonia/MainWindow.axaml.cs` | 窗口生命周期、快捷键和公共状态 |
| `src/Yy.Studio.Avalonia/MainWindow.Project.cs` | 工程打开、源码树、文档打开/保存和标签 |
| `src/Yy.Studio.Avalonia/MainWindow.Build.cs` | 检查、编译、运行、PID 和输出回传 |
| `src/Yy.Studio.Avalonia/MainWindow.Mcp.cs` | MCP 传输、状态、截图、工程/文件工具 |
| `src/Yy.Studio.Avalonia/Editor/YyCodeEditor.cs` | AvaloniaEdit 编辑器、XHZD 着色、行号和搜索 |
| `src/Yy.Studio.Avalonia/Features/...` | 设置、文档、窗体设计、项目对话框等迁移功能 |
| `src/Yy.Studio.Core/Workspace/StudioWorkspace.cs` | 工程加载、源码树、项目/单文件编译 |
| `src/Yy.Studio.Core/Configuration/...` | `YyStudio.json` 模型与校验 |
| `src/Yy.Studio.Core/Documentation/...` | 内置开发文档目录与搜索 |
| `src/Yy.Studio.Core/Mcp/StudioMcpServer.cs` | MCP JSON-RPC、HTTP/stdio 传输 |

## WPF 迁移参考（冻结）

以下文件只在迁移期用于逐项对照，不再新增功能：

| 文件 | 职责 |
|---|---|
| `MainWindow.xaml.cs` | 窗口字段、构造和公共生命周期 |
| `MainWindow.Chrome.cs` | 自定义顶栏的最小化、最大化、还原和关闭 |
| `MainWindow.EditorAssistance.cs` | 编辑器配置、补全、参数提示、F1 帮助和快捷键分派 |
| `MainWindow.Shortcuts.cs` | 从 Studio 配置创建输入绑定并刷新菜单快捷键 |
| `MainWindow.DocumentCommands.cs` | 新建、打开、保存入口和关闭标签命令 |
| `NewProjectDialog.cs` | 项目名、包名、项目位置及源码/资源目录预览 |
| `MainWindow.DocumentPersistence.cs` | 文档序列化、三文件保存和关闭确认 |
| `MainWindow.Workspace.cs` | 工作区、程序数据树、文档激活和视图切换 |
| `MainWindow.DesignerInteraction.cs` | 窗体渲染、组件箱、拖放、移动和缩放 |
| `MainWindow.DesignerSelection.cs` | 选择、剪贴板、层级、撤销与重做 |
| `MainWindow.DesignerProperties.cs` | 属性夹、事件选择与属性提交 |
| `MainWindow.FormIntegration.cs` | 预览、事件入口、组件关联和关系文件协调 |
| `MainWindow.RunBuild.cs` | 工程/源码运行、原生进程生命周期、调试、监视、搜寻和编译 |
| `MainWindow.RuntimeResults.cs` | 控制台、执行结果、诊断、状态与变量显示 |
| `MainWindow.ShellCommands.cs` | 外壳菜单、面板、资源、模块与窗口菜单 |
| `MainWindow.WindowLayout.cs` | IDEA 式工具窗停靠、显示/最小化/隐藏、恢复栏和布局持久化 |
| `DocumentationCatalog.cs` | 语言、SDK 和命令参考的内置文档模型与搜索 |
| `DocumentationWindow.*` | 帮助菜单中的可搜索开发文档窗口 |
| `Mcp/StudioMcpHost.cs` | MCP 工具到真实 IDE 操作路径的分派 |

## 代码编辑窗口

| 文件 | 职责 |
|---|---|
| `MainWindow.CodeWindow.cs` | 对象/过程导航、函数定位和格式化 |
| `Structured/EasyLanguageCodeEditor.cs` | 紧凑方法声明表与连续执行代码块 |
| `Structured/YyTextEditing.cs` | 自动配对、注释切换、复制行和删除行 |
| `Structured/YyIndentationStrategy.cs` | 易语代码块缩进 |
| `Structured/EasyLanguageFoldingStrategy.cs` | 子程序、条件和循环折叠 |
| `EditorDocument.cs` | 结构化存储与连续源码视图之间的状态 |
| `StudioConfiguration.cs` | `YyStudio.json` 模型、校验、快捷键目录和配置保存 |
| `SystemConfigDialog.xaml` | JetBrains 风格设置窗口 |

用户代码可以在“经典视图”和“纯代码”两种呈现间切换。方法声明可以出现在小表格中，但执行语句始终处于连续代码区；两种视图共享补全、参数提示、F1 帮助和自动配对。

## 项目模型

| 文件 | 职责 |
|---|---|
| `src/Yy.Compiler/YyProject.cs` | 项目装载、设置保存、路径校验 |
| `src/Yy.Compiler/YyProjectModels.cs` | 项目窗体、全局变量、常量、资源、DLL 和数据库模型 |
| `src/Yy.Compiler/YyProject.Creation.cs` | 新项目和窗体三件套创建、默认模板 |
| `src/Yy.Compiler/YyProject.Resources.cs` | 资源导入、分类复制、资源表校验和索引同步 |
| `src/Yy.Compiler/Build/YyBuildTarget.cs` | `auto`、Windows、macOS 目标和宿主环境解析 |
| `src/Yy.Compiler/Resources/YyResourceIndex.cs` | `ids.xml` 生成、标识解析和资源路径定位 |
| `src/Yy.Sdk/YySdk.cs` | SDK 版本、包成熟度与依赖目录、原生句柄和无装箱界面契约 |
| `src/Yy.Language/ProjectPackageResolver.cs` | 把 `项目包名.目录` 解析为 `Sources/` 下的目录包 |
| `src/Yy.Native/Backends/NativeBackend.cs` | `INativeBackend`、编译请求和后端注册表 |
| `src/Yy.Native/Backends/Windows/WindowsNativeBackend.cs` | Windows x64/PE 后端注册 |
| `src/Yy.Native/Backends/Windows/NativeEmitter.cs` | 易语 AST 到 Win64 的代码生成 |
| `src/Yy.Native/Backends/Windows/NativeEmitter.Collections.cs` | 原生数组、索引、遍历和追加 |
| `src/Yy.Native/Backends/Windows/NativeEmitter.FileRuntime.cs` | Win32 文件、目录和路径转换 |
| `src/Yy.Native/Backends/Windows/NativeEmitter.ProcessRuntime.cs` | Win32 子进程、等待和退出码 |
| `src/Yy.Native/Backends/Windows/NativeEmitter.ConversionRuntime.cs` | 整数与文本运行期转换 |
| `src/Yy.Native/Backends/Windows/NativeEmitter.Loops.cs` | 原生当循环和计次循环 |
| `src/Yy.Native/Backends/Windows/NativeEmitter.TextRuntime.cs` | 堆分配文本、拼接、复制和比较运行时 |
| `src/Yy.Native/Backends/Windows/NativeEmitter.Types.cs` | Windows 后端类型推断委托 |
| `src/Yy.Native/Backends/Windows/NativeEmitter.Win32Text.cs` | Win32 控件文本编解码、读取和写入 |
| `src/Yy.Native/Backends/Windows/NativeEmitter.Win32Image.cs` | GDI+ 图片加载和 Win32 图片框显示 |
| `src/Yy.Native/Backends/Windows/NativeAssembly.cs` | Win64 机器码缓冲和重定位 |
| `src/Yy.Native/Backends/Windows/NativePeWriter.cs` | PE32+、节表、导入表和原生 EXE 写出 |
| `src/Yy.Native/Backends/MacOS/MacNativeBackend.cs` | macOS arm64/Mach-O 后端注册 |
| `src/Yy.Native/Backends/MacOS/Arm64CodeBuffer.cs` | AArch64 指令、标签和重定位 |
| `src/Yy.Native/Backends/MacOS/MacNativeEmitter*.cs` | ARM64 表达式、控制流、运行时、文件和进程代码生成 |
| `src/Yy.Native/Backends/MacOS/MacNativeEmitter.AppKit*.cs` | AppKit 窗体、控件、事件、模态会话和图片代码生成 |
| `src/Yy.Native/Backends/MacOS/MacImport.cs` | Mach-O 库路径和导入符号模型 |
| `src/Yy.Native/Backends/MacOS/MachOImageWriter.cs` | Mach-O 段、dyld 绑定和内嵌 ad-hoc 签名 |
| `src/Yy.Native/Semantics/NativeTypeAnalyzer.cs` | 平台无关类型推断 |
| `src/Yy.Native/Semantics/NativeContext.cs` | 局部变量槽、循环标签和类型状态 |
| `src/Yy.Native/Semantics/NativeRuntimeLayout.cs` | 文本、数组、对象值布局 |
| `src/Yy.Native/Semantics/NativeDataBuffer.cs` | 常量文本、零填充缓冲和标签重定位 |
| `src/Yy.Native/Semantics/NativeBuiltinCatalog.cs` | 语言核心内置命令目录 |

## 编译器与命令行

| 项目或文件 | 职责 |
|---|---|
| `src/Yy.Compiler` | 无 UI 依赖的工程协议、窗体模型、资源索引和 AST 汇总 |
| `src/Yy.Compiler/ProjectCompiler.cs` | 校验项目并汇总入口、窗体和模块源码 |
| `tools/Yy.Cli` | `yy` / `yy.exe` 命令行入口和目标选择 |
| `tools/Yy.Cli/Program.cs` | `build`、`check`、`run`、`version` 子命令 |
| `tests/Yy.Native.Backends.Tests` | 后端注册表、Mach-O 结构、内嵌签名、交叉编译和 macOS 运行时 |
| `docs/backend-architecture.md` | 后端契约、共享语义和新增平台流程 |

`YyStudio` 和 `Yy.Cli` 都通过 `Yy.Native` 编译应用；`Yy.Cli` 不得引用 Studio、Avalonia 或 WPF。Avalonia Studio 只实现 IDE 与设计预览，不会进入用户程序产物。

## 体积门槛

- 单个手写 C# 源文件目标不超过 800 行。
- 硬性回归门槛为 900 行；`Yy.Studio.Tests` 会扫描仓库并阻止继续增长。
- 生成目录 `obj/`、`bin/` 不参与统计。
- 超过目标临近值时，优先按可独立理解的功能拆分，而不是按任意行数切断方法。
- 可复用算法、文件协议、解析和生成逻辑应放入独立服务，不应通过 partial 继续堆进窗口类。
- 新增窗口 partial 时，文件名必须表达职责，例如 `MainWindow.RuntimeResults.cs`，禁止 `Part1`、`Misc`、`Helpers` 等模糊命名。
