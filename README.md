# 易语 Studio

易语 Studio 是一个面向 Windows 的中文桌面开发环境，以现代 IDE 技术重制易语言的开发体验，把界面设计、中文代码编辑、项目管理和应用编译整合在同一个工作区。

项目正在持续开发中，语言能力、文件格式和 IDE 交互都会继续调整。

## 界面

工作区采用现代浅色 IDE 布局：

- 菜单、当前文档标题和窗口按钮共用无系统标题栏的顶部操作栏，窗口仍支持拖动、缩放、双击最大化和贴边布局。
- 左侧显示真实项目目录，可直接展开文件夹、打开窗体、代码和项目文件。
- 右侧提供支持库、程序数据和属性工具窗口。
- 组件箱只在窗体设计器中显示，不会占用代码编辑空间。
- 文档标签位于编辑器顶部，并显示项目路径和当前过程面包屑。
- 状态栏显示运行状态、光标位置和文件编码。
- 代码编辑支持自动补全、参数签名提示、F1 上下文帮助、括号配对、块缩进、折叠和撤销。

### 易语言式代码窗口

每个子程序使用一张紧凑的方法声明表，代码正文在表格下方连续编辑。支持对象/过程导航、语法着色、参数提示、补全和块缩进。

![易语言式代码窗口](docs/images/studio-code-window.webp)

### 窗体设计器

窗体设计器支持组件拖放、属性编辑、画布对齐、事件入口和原生窗口预览。

![窗体设计器](docs/images/studio-designer.webp)

### MCP 资源示例

下面的界面通过 MCP 接口从空白工程创建：导入窗口图标和图片资源、布局控件、写入按钮事件并运行预览。

![MCP 创建的资源图标界面](docs/images/mcp-resource-example.webp)

## 当前能力

- C# / WPF 原生 Windows IDE，无 Electron、无 WebView。
- 编辑器与命令行编译器分离：`YyStudio.exe` 负责 IDE，`yy.exe` 负责构建、检查和运行。
- 中文标识符、函数、递归、默认参数、类型标注、数组、对象和异常处理。
- AST 解析器、解释执行器、C# 代码生成器和跨文件诊断。
- `.yyproj` 项目管理、模块引入、资源表、常量表和数据表。
- 可视化窗体设计、组件箱、属性夹、事件绑定和窗口预览。
- 三文件界面模型：`.yyx` 界面、`.Designer.yyc` 自动关系、`.yyc` 用户代码。
- 易语言式代码窗口和纯代码模式共用同一份用户代码。
- 项目可编译为 Windows 单文件 EXE。
- 条件、`当`循环、`计次循环`、`遍历`、`跳出`和`继续`。
- JSON、文本、集合、文件、时间等内置命令。
- UTF-8 文件、行列诊断、运行输出和可取消执行。

功能规划和边界见 [路线图](docs/roadmap.md) 与 [能力核对](docs/feature-analysis.md)。

## 运行

需要 .NET 10 SDK 与 Windows 10/11。

```powershell
dotnet run --project .\src\Yy.Studio\Yy.Studio.csproj
```

打开 `examples` 文件夹即可加载示例工程。

常用快捷键：

- `F5`：运行当前源码或预览当前窗体。
- `Ctrl+F5`：运行项目。
- `Shift+F5`：停止运行。
- `Ctrl+B`：编译项目。
- `Ctrl+S`：保存。
- `Ctrl+W`：关闭当前标签。
- `Ctrl+Space`：代码补全。
- `F1`：当前位置的函数、命令、参数或变量帮助。
- `Ctrl+/`：切换当前行或所选行的注释。
- `Ctrl+D`：复制当前行或所选内容。
- `Ctrl+Shift+K`：删除当前行或所选内容。

代码窗口右上角可切换“易语言代码”和“纯代码”。

新建项目请按 `Ctrl+Alt+N`。项目清单和界面三文件关系分别见 [项目结构](docs/projects.md) 和 [界面架构](docs/architecture.md)。

## 命令行工具

`yy` 是不加载 WPF 和 IDE 的命令行编译器。开发时可以直接运行：

```powershell
dotnet run --project .\tools\Yy.Cli -- check .\examples\示例.yyproj
dotnet run --project .\tools\Yy.Cli -- build .\examples\示例.yyproj -o .\artifacts\示例.exe
dotnet run --project .\tools\Yy.Cli -- run .\examples\MCP资源演示
```

发布后使用 `yy.exe`：

```powershell
yy build <工程.yyproj|目录> [-o 输出.exe] [--framework-dependent] [--rid RID]
yy check <工程.yyproj|目录>
yy run <工程.yyproj|目录>
```

`YyStudio.exe` 仅负责可视化编辑和 IDE 内操作；脚本、CI 和批处理可直接调用 `yy.exe`。命令行编译目前需要开发机安装 .NET 10 SDK。

## MCP 接口

Studio 内置 MCP（Model Context Protocol）服务端，AI 客户端可以读取 IDE 状态、打开和保存工程、编辑窗体、创建事件、写入子程序、运行预览并触发界面交互。每个 MCP 工具都调用 Studio 的真实编辑流程，与用户在界面中的操作使用同一套代码路径。

IDE 启动后默认在 `http://127.0.0.1:37213/mcp` 提供 HTTP JSON-RPC 接口，项目根目录的 `.mcp.json` 已包含可直接使用的客户端配置：

```json
{
  "mcpServers": {
    "yy-studio": {
      "type": "http",
      "url": "http://127.0.0.1:37213/mcp"
    }
  }
}
```

如果默认端口被占用，服务会依次尝试后续端口，并将实际端口写入 `%TEMP%\yystudio-mcp.port`。也可以使用标准输入输出传输：

```powershell
YyStudio.exe --mcp-stdio
```

当前提供 19 个工具，覆盖工程创建、资源导入、窗体设计、控件与事件、代码读写、预览运行、控件交互和界面截图。完整参数、请求示例和返回说明见 [MCP 接口文档](docs/mcp.md)。

## 构建发布版

```powershell
.\build.ps1
```

发布结果：

```text
artifacts\publish\YyStudio\YyStudio.exe
artifacts\publish\yy\yy.exe
artifacts\samples\易语示例.exe
```

构建脚本会运行语言测试和编译链烟雾测试，然后生成 Windows x64 自包含版本。

若目标机器已安装 .NET 10 Desktop Runtime，可以生成体积更小的版本：

```powershell
.\build-lite.ps1
```

输出包含 `artifacts\publish\win-x64-framework-dependent\YyStudio.exe` 和
`artifacts\publish\yy-framework-dependent\yy.exe`。

## 工程结构

可视化界面采用三个基础文件：

```text
Forms/
├─ MainForm.yyx              界面与布局
├─ MainForm.Designer.yyc     自动生成的事件关系
└─ MainForm.yyc              用户代码

Resources/
├─ ids.xml                   自动生成的控件与资源标识
├─ images/                   图片资源
├─ sounds/                   声音资源
└─ files/                    其他文件资源
```

`Resources/ids.xml` 类似 Android 的资源关联表：窗体控件使用 `@id/窗体/控件`，资源使用 `@资源/图片/名称` 形式登记。工程清单通过 `"资源标识": "Resources/ids.xml"` 声明索引，新建项目、保存窗体和资源表时自动同步。

示例主窗体使用 `@资源/图片/应用图标` 作为窗口图标，并通过图片框显示 `@资源/图片/客户图标`。设计器、预览和编译后的 EXE 共用同一套资源标识。

```text
src/Yy.Language/          词法、语法树、解析器、解释器和中文标准库
src/Yy.Compiler/          项目模型、窗体协议、资源索引、代码生成和编译器
src/Yy.CompilerRuntime/   编译应用使用的值、控件与标准库运行时
src/Yy.Studio/            WPF 原生 IDE
tools/Yy.Cli/             yy.exe 命令行入口
tests/                    语言与桌面回归测试
examples/                 示例工程
docs/                     格式、架构、编辑器和路线图文档
```

## 代码示例

`.yyc` 以结构化 XML 保存，IDE 同时提供易语言式方法表和纯代码编辑视图。

```text
.版本 1
.程序集 示例

子程序 问候(姓名: 文本 = "世界"): 文本
    返回 "你好，" + 姓名
结束子程序

输出(问候())
```

表达式使用中文易语语法。详细说明见 [语言参考](docs/language.md)、[代码编辑器](docs/code-editor.md) 和 [结构化代码格式](docs/code-format.md)。

## 设计原则

1. **Windows 原生优先**：桌面端使用 WPF，保持原生窗口、输入和文件系统体验。
2. **中文优先**：标识符、关键字、诊断和工程文件都适合中文开发。
3. **界面与逻辑分离**：XML、自动关系代码和用户代码各自承担单一职责。
4. **可视化与纯代码并存**：同一工程可以使用窗体设计器，也可以直接编辑完整源码。
5. **可验证的工程工具链**：解析、执行、编译、保存和编辑器交互都纳入自动测试。
