using Yy.Sdk;

namespace Yy.Studio;

public enum DocumentationBlockKind
{
    Paragraph,
    Note,
    Code,
    Api
}

public sealed record DocumentationBlock(
    DocumentationBlockKind Kind,
    string Text);

public sealed class DocumentationTopic(
    string section,
    string title,
    string summary,
    IReadOnlyList<DocumentationBlock> blocks)
{
    public string Section { get; } = section;
    public string Title { get; } = title;
    public string Summary { get; } = summary;
    public IReadOnlyList<DocumentationBlock> Blocks { get; } = blocks;
    public string SearchText { get; } = string.Join(
        '\n',
        new[] { section, title, summary }.Concat(blocks.Select(block => block.Text)));
}

/// <summary>
/// Studio 内置开发文档。语言、SDK 包和内置命令都从同一份模型生成，
/// 避免帮助内容与补全、配置和编译器各自维护一套说明。
/// </summary>
public static class DocumentationCatalog
{
    public static IReadOnlyList<DocumentationTopic> Topics { get; } = CreateTopics();

    public static IReadOnlyList<DocumentationTopic> Search(string? query)
    {
        var value = query?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return Topics;
        }

        return Topics
            .Where(topic => topic.SearchText.Contains(value, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static IReadOnlyList<DocumentationTopic> CreateTopics()
    {
        var topics = new List<DocumentationTopic>();
        topics.AddRange(CreateGettingStartedTopics());
        topics.AddRange(CreateLanguageTopics());
        topics.AddRange(CreateSdkTopics());
        topics.AddRange(CreateCommandTopics());
        topics.AddRange(CreateEditorTopics());
        return topics;
    }

    private static IEnumerable<DocumentationTopic> CreateGettingStartedTopics()
    {
        yield return Topic(
            "快速开始",
            "创建并运行第一个工程",
            "从新建项目、设计窗体到填写事件代码的完整路径。",
            Block(
                DocumentationBlockKind.Paragraph,
                "选择“程序 → 新建”，选择窗口、控制台或模块工程。确认类型后填写保存位置、项目名和包名，Studio 才会创建磁盘工程并进入 IDE。"),
            Block(
                DocumentationBlockKind.Code,
                """
                项目位置：D:\Projects
                项目名：  客户管理
                包名：    demo.customer
                """),
            Block(
                DocumentationBlockKind.Paragraph,
                "右键项目打开窗体，或双击 Sources/Forms/主窗体.yyx。将按钮拖到窗体上，在属性夹中设置标识和标题，然后双击按钮生成单击事件。"),
            Block(
                DocumentationBlockKind.Code,
                """
                子程序 _保存按钮_被单击()
                    变量 姓名 = 姓名输入框.文本
                    如果 姓名 = "" 则
                        提示("请先填写姓名")
                        返回
                    结束如果
                    结果标签.文本 = "已保存：" + 姓名
                    窗口.标题 = "客户管理 · 已保存"
                结束子程序
                """),
            Block(
                DocumentationBlockKind.Note,
                "按 F5 运行当前工程；F5 会编译并启动 Win32 窗口，同时自动展开底部控制台。用户逻辑只写在 .yyc 中。"));

        yield return Topic(
            "快速开始",
            "工程目录",
            "源码、资源、窗体和工程清单的标准位置。",
            Block(
                DocumentationBlockKind.Code,
                """
                DemoProject/
                ├─ DemoProject.yyproj
                ├─ Sources/
                │  ├─ Program.yyc
                │  ├─ Forms/
                │  │  ├─ MainForm.yyx
                │  │  ├─ MainForm.Designer.yyc
                │  │  └─ MainForm.yyc
                │  └─ 工具/
                │     └─ 文本.yyc
                └─ Resources/
                   ├─ ids.xml
                   ├─ images/
                   ├─ sounds/
                   └─ files/
                """),
            Block(
                DocumentationBlockKind.Paragraph,
                "Sources 是源代码目录，Resources 是资源目录。图片、声音、文件和 ids.xml 不得放入 Sources；.yyc 和窗体文件不得放入 Resources。"),
            Block(
                DocumentationBlockKind.Code,
                """
                引入 "demo.customer.工具"
                引入 "工具/文本.yyc"
                """),
            Block(
                DocumentationBlockKind.Note,
                "目录包使用“项目包名.子目录”，相对文件导入仍使用完整文件路径。"));

        yield return Topic(
            "快速开始",
            "界面三文件模型",
            "界面、自动关系和用户代码各自只有一个所有者。",
            Block(
                DocumentationBlockKind.Paragraph,
                ".yyx 只保存界面布局和视觉属性；.Designer.yyc 由生成器维护控件标识和事件关系；用户 .yyc 保存全部事件入口和业务逻辑。"),
            Block(
                DocumentationBlockKind.Code,
                """
                主窗体.yyx
                主窗体.Designer.yyc  自动生成，只读
                主窗体.yyc           用户代码
                """),
            Block(
                DocumentationBlockKind.Paragraph,
                "设计器保存窗体时会确定性地重建自动关系文件，并补齐用户代码中缺失的事件入口，但不会覆盖已有参数、备注或语句。"),
            Block(
                DocumentationBlockKind.Note,
                "控件名和窗口名由生成层提供。用户代码直接使用 结果标签.文本、窗口.标题；@id/... 只存在于 .Designer.yyc。"));
    }

    private static IEnumerable<DocumentationTopic> CreateLanguageTopics()
    {
        yield return Topic(
            "语言语法",
            "变量、常量与表达式",
            "无分号的易语表达式和常用字面量。",
            Block(
                DocumentationBlockKind.Code,
                """
                变量 姓名 = "世界"
                变量 年龄: 数字 = 20
                常量 圆周率 = 3.14159
                变量 用户 = {姓名: "林川", 年龄: 20}
                变量 数列 = [1, 2, 3, 4, 5]
                变量 标题 = "你好，" + 姓名
                """),
            Block(
                DocumentationBlockKind.Paragraph,
                "语句不使用分号。逻辑运算可写作 且、或、非，也接受 &&、||、!。集合使用方括号，对象使用花括号。"),
            Block(
                DocumentationBlockKind.Note,
                "系统核心包默认引用；除非语法关键字所需的平台能力，其他功能通过导入包获得。"));

        yield return Topic(
            "语言语法",
            "条件与循环",
            "中文块标记、连续代码画布和可嵌套的流程结构。",
            Block(
                DocumentationBlockKind.Code,
                """
                如果 年龄 >= 18 则
                    输出("成年")
                否则如果 年龄 >= 12 则
                    输出("青少年")
                否则
                    输出("儿童")
                结束如果

                计次循环 序号 从 1 到 10
                    输出(序号)
                结束循环

                遍历 数值 于 数列
                    如果 数值 = 3 则
                        继续
                    结束如果
                    输出(数值)
                结束遍历
                """),
            Block(
                DocumentationBlockKind.Paragraph,
                "代码正文保持连续，不把执行语句拆成逐行表格。流程线仅显示块层次，不参与文本布局，也不会移动光标基线。"),
            Block(
                DocumentationBlockKind.Note,
                "循环可位于子程序或顶层代码。跳出用于退出当前循环，继续进入下一次迭代。"));

        yield return Topic(
            "语言语法",
            "子程序",
            "参数、返回类型、默认参数和表达式返回。",
            Block(
                DocumentationBlockKind.Code,
                """
                子程序 创建问候(姓名: 文本, 前缀: 文本 = "你好"): 文本
                    返回 前缀 + "，" + 姓名 + "！"
                结束子程序

                输出(创建问候("世界"))
                """),
            Block(
                DocumentationBlockKind.Paragraph,
                "子程序声明显示在紧凑方法表中，执行语句显示在表格下方。纯代码模式直接显示完整源码，两种模式保存到同一个 .yyc。"),
            Block(
                DocumentationBlockKind.Note,
                "入口程序是 .yyc 中的顶层语句，不要求声明 main。"));

        yield return Topic(
            "语言语法",
            "结构体与接口",
            "用固定字段描述数据，用接口声明公开方法契约。",
            Block(
                DocumentationBlockKind.Code,
                """
                结构体 客户
                    姓名: 文本
                    年龄: 数字
                结束结构体

                接口 可问候
                    子程序 问候(): 文本
                结束接口

                结构体 个人客户 实现 可问候
                    姓名: 文本
                结束结构体
                """),
            Block(
                DocumentationBlockKind.Paragraph,
                "结构体用于减少对象键名拼写错误；接口用于描述模块和结构体必须提供的方法。完整 SDK 源码位于 sdk/Sources。"),
            Block(
                DocumentationBlockKind.Note,
                "结构体和接口属于语言模型，不依赖界面 SDK。"));

        yield return Topic(
            "语言语法",
            "错误处理",
            "捕获运行时错误并保留可定位的诊断。",
            Block(
                DocumentationBlockKind.Code,
                """
                尝试
                    变量 内容 = 读文件("data.txt")
                    输出(内容)
                捕获 错误
                    提示("读取失败：" + 错误)
                结束尝试
                """),
            Block(
                DocumentationBlockKind.Paragraph,
                "运行时错误带有行列和源文件信息。无法解析的源码可切换到纯代码模式修复，再回到经典视图继续编辑。"));
    }

    private static IEnumerable<DocumentationTopic> CreateSdkTopics()
    {
        yield return Topic(
            "系统 SDK",
            "包模型",
            "语言核心包、平台包和规划包的范围。",
            Block(
                DocumentationBlockKind.Paragraph,
                "易语 SDK 借鉴 Java 包和 Android 平台分层。系统.基础、系统.集合、系统.文本、系统.数据是语言核心包并默认可见。"),
            Block(
                DocumentationBlockKind.Code,
                """
                导入包 "系统.界面"
                导入包 "系统.文件"
                """),
            Block(
                DocumentationBlockKind.Paragraph,
                "系统.界面、系统.文件等平台包把语言能力连接到 Win32、窗口和操作系统服务。语言核心语义由编译器和运行时自身实现。"),
            Block(
                DocumentationBlockKind.Note,
                "规划中的包会展示设计契约，但在实现完成前不能导入。包名清单和补全来自同一个 YySdk 模型。"));

        foreach (var package in YySdk.Packages)
        {
            var blocks = new List<DocumentationBlock>
            {
                Block(DocumentationBlockKind.Note, package.StatusText + " · " + KindText(package.Kind)),
                Block(DocumentationBlockKind.Paragraph, package.Description)
            };
            if (package.IsImportable)
            {
                blocks.Add(Block(
                    DocumentationBlockKind.Code,
                    $"导入包 \"{package.Name}\""));
            }
            else
            {
                blocks.Add(Block(
                    DocumentationBlockKind.Note,
                    "当前为规划包，仅提供设计契约，尚不能导入。"));
            }

            if (package.Members.Count > 0)
            {
                blocks.Add(Block(
                    DocumentationBlockKind.Paragraph,
                    "公开成员：" + string.Join("、", package.Members)));
            }

            foreach (var builtin in package.Builtins)
            {
                var detail = StudioCatalog.Builtins.FirstOrDefault(item => item.Name == builtin);
                blocks.Add(Block(
                    DocumentationBlockKind.Api,
                    detail is null ? builtin : detail.Signature + "\n" + detail.Description));
            }

            if (package.Dependencies.Count > 0)
            {
                blocks.Add(Block(
                    DocumentationBlockKind.Note,
                    "依赖包：" + string.Join("、", package.Dependencies)));
            }

            yield return new DocumentationTopic(
                "系统 SDK",
                package.Name,
                package.Description,
                blocks);
        }
    }

    private static IEnumerable<DocumentationTopic> CreateCommandTopics()
    {
        var populatedCategories = StudioCatalog.Categories
            .Where(category => category.Commands.Count > 0)
            .ToArray();
        yield return Topic(
            "命令参考",
            "内置命令总览",
            "按语言能力分类的内置命令。",
            Block(
                DocumentationBlockKind.Paragraph,
                string.Join(
                    "\n",
                    populatedCategories.Select(category =>
                        $"{category.Name}：{category.Commands.Count} 条命令"))),
            Block(
                DocumentationBlockKind.Note,
                "输入命令名时编辑器提供补全；在代码中按 F1 可查看当前位置的上下文帮助。"));

        foreach (var category in populatedCategories)
        {
            yield return new DocumentationTopic(
                "命令参考",
                category.Name,
                $"{category.Commands.Count} 条内置命令与签名。",
                category.Commands
                    .Select(command => Block(
                        DocumentationBlockKind.Api,
                        command.Signature + "\n" + command.Description))
                    .ToArray());
        }
    }

    private static IEnumerable<DocumentationTopic> CreateEditorTopics()
    {
        yield return Topic(
            "编辑器",
            "经典视图",
            "方法声明表与连续代码正文的组合方式。",
            Block(
                DocumentationBlockKind.Paragraph,
                "经典视图在每个子程序上方显示紧凑的方法声明表，参数和局部变量也使用小表。所有执行语句位于表下的连续代码区，不显示逐行表格横线。"),
            Block(
                DocumentationBlockKind.Paragraph,
                "左侧流程线表达如果、否则、循环和异常块层级。流程线绘制在编辑器背景层，不占用文本缩进，因此光标与代码左边缘保持对齐。"),
            Block(
                DocumentationBlockKind.Note,
                "通过代码窗口右上角切换“经典视图”和“纯代码”。切换不会创建第二份源码。"));

        yield return Topic(
            "编辑器",
            "补全与上下文帮助",
            "补全、参数提示、诊断和事件跳转。",
            Block(
                DocumentationBlockKind.Paragraph,
                "输入字母、中文、下划线或点号时自动出现补全，Ctrl+Space 可手动打开。函数签名和当前参数序号显示在提示栏。"),
            Block(
                DocumentationBlockKind.Paragraph,
                "候选来自当前源码、窗体关系、参数、局部变量、系统包和内置命令。导入包时只显示可导入的 SDK 包。"),
            Block(
                DocumentationBlockKind.Note,
                "F1 显示当前位置的上下文帮助；“帮助 → 易语开发文档”打开可搜索的完整语言、SDK 和命令参考。"));

        yield return Topic(
            "编辑器",
            "默认快捷键",
            "运行、编译、补全和行编辑的常用操作。",
            Block(
                DocumentationBlockKind.Api, "F5\n编译并运行当前工程；没有工程时运行当前源码。"),
            Block(
                DocumentationBlockKind.Api, "Ctrl+F5\n运行完整项目。"),
            Block(
                DocumentationBlockKind.Api, "Shift+F5\n停止当前运行。"),
            Block(
                DocumentationBlockKind.Api, "Ctrl+B / F7\n编译原生 EXE。"),
            Block(
                DocumentationBlockKind.Api, "Ctrl+S / Ctrl+W\n保存文件；关闭当前标签。"),
            Block(
                DocumentationBlockKind.Api, "Ctrl+Space\n打开代码补全。"),
            Block(
                DocumentationBlockKind.Api, "Ctrl+/\n切换当前行或所选行的注释。"),
            Block(
                DocumentationBlockKind.Api, "Ctrl+D / Ctrl+Shift+K\n复制当前行；删除当前行。"),
            Block(
                DocumentationBlockKind.Api, "底部控制台\n显示编译路径、启动 PID、标准输出、错误和退出码。"),
            Block(
                DocumentationBlockKind.Note,
                "快捷键可在“程序 → 系统配置 → 快捷键”或 YyStudio.json 中修改。"));
    }

    private static DocumentationTopic Topic(
        string section,
        string title,
        string summary,
        params DocumentationBlock[] blocks) =>
        new(section, title, summary, blocks);

    private static DocumentationBlock Block(
        DocumentationBlockKind kind,
        string text) =>
        new(kind, text);

    private static string KindText(YyPackageKind kind) => kind switch
    {
        YyPackageKind.LanguageCore => "语言核心",
        YyPackageKind.Platform => "平台 SDK",
        YyPackageKind.Tooling => "工具链",
        _ => kind.ToString()
    };
}
