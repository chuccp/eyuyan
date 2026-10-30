# 代码模块与体积约束

易语 Studio 已从单文件窗口类拆分为按职责组织的 partial class。partial 只用于共享同一个 WPF 窗口状态，纯业务逻辑仍应进入独立服务或模型类。

## 窗口模块

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
| `MainWindow.RunBuild.cs` | 运行、调试、监视、搜寻和编译 |
| `MainWindow.RuntimeResults.cs` | 执行结果、诊断、状态与变量显示 |
| `MainWindow.ShellCommands.cs` | 外壳菜单、支持库、资源、模块与窗口菜单 |
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

用户代码可以在“易语言代码”和“纯代码”两种呈现间切换。方法声明可以出现在小表格中，但执行语句始终处于连续代码区；两种视图共享补全、参数提示、F1 帮助和自动配对。

## 项目模型

| 文件 | 职责 |
|---|---|
| `src/Yy.Compiler/YyProject.cs` | 项目装载、设置保存、路径校验 |
| `src/Yy.Compiler/YyProjectModels.cs` | 项目窗体、全局变量、常量、资源、DLL 和数据库模型 |
| `src/Yy.Compiler/YyProject.Creation.cs` | 新项目和窗体三件套创建、默认模板 |
| `src/Yy.Compiler/YyProject.Resources.cs` | 资源导入、分类复制、资源表校验和索引同步 |
| `src/Yy.Compiler/Resources/YyResourceIndex.cs` | `ids.xml` 生成、标识解析和资源路径定位 |
| `src/Yy.Sdk/YySdk.cs` | SDK 版本、包成熟度与依赖目录、原生句柄和无装箱界面契约 |
| `src/Yy.Language/ProjectPackageResolver.cs` | 把 `项目包名.目录` 解析为 `Sources/` 下的目录包 |
| `src/Yy.Native/NativeEmitter.cs` | 易语 AST 到 Win64 指令的原生代码生成 |
| `src/Yy.Native/NativeEmitter.Collections.cs` | 原生数组、索引、遍历和追加 |
| `src/Yy.Native/NativeEmitter.FileRuntime.cs` | UTF-8/Win32 文件、目录和路径转换 |
| `src/Yy.Native/NativeEmitter.ConversionRuntime.cs` | 整数与文本运行期转换 |
| `src/Yy.Native/NativeEmitter.Loops.cs` | 原生当循环和计次循环 |
| `src/Yy.Native/NativeEmitter.TextRuntime.cs` | 堆分配文本、拼接、复制和比较运行时 |
| `src/Yy.Native/NativeEmitter.Types.cs` | 原生表达式、函数和声明类型推断 |
| `src/Yy.Native/NativeEmitter.Win32Text.cs` | Win32 控件文本编解码、读取和写入 |
| `src/Yy.Native/NativeEmitter.Win32Image.cs` | GDI+ 图片加载和 Win32 图片框显示 |
| `src/Yy.Native/NativeAssembly.cs` | 机器码、数据段和重定位缓冲 |
| `src/Yy.Native/NativePeWriter.cs` | PE32+、节表、导入表和原生 EXE 写出 |

## 编译器与命令行

| 项目或文件 | 职责 |
|---|---|
| `src/Yy.Compiler` | 无 WPF 依赖的工程协议、窗体模型、资源索引和 AST 汇总 |
| `src/Yy.Compiler/ProjectCompiler.cs` | 校验项目并汇总入口、窗体和模块源码 |
| `tools/Yy.Cli` | `yy.exe` 命令行入口 |
| `tools/Yy.Cli/Program.cs` | `build`、`native`、`check`、`run`、`version` 子命令 |

`YyStudio` 和 `Yy.Cli` 都通过 `Yy.Native` 编译应用；`Yy.Cli` 不得引用 `Yy.Studio` 或 WPF。Studio 的 WPF 代码只实现 IDE 与设计预览，不会进入编译产物。

## 体积门槛

- 单个手写 C# 源文件目标不超过 800 行。
- 硬性回归门槛为 900 行；`Yy.Studio.Tests` 会扫描仓库并阻止继续增长。
- 生成目录 `obj/`、`bin/` 不参与统计。
- 超过目标临近值时，优先按可独立理解的功能拆分，而不是按任意行数切断方法。
- 可复用算法、文件协议、解析和生成逻辑应放入独立服务，不应通过 partial 继续堆进窗口类。
- 新增窗口 partial 时，文件名必须表达职责，例如 `MainWindow.RuntimeResults.cs`，禁止 `Part1`、`Misc`、`Helpers` 等模糊命名。
