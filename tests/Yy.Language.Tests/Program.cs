using Yy.Language;
using Yy.Language.Structured;
var tests = new List<(string Name, Action Test)>
{
    ("基础输出", TestOutput),
    ("表达式优先级", TestPrecedence),
    ("嵌套块注释", TestNestedComments),
    ("条件分支", TestIfElseIf),
    ("计次循环与跳出", TestCountLoop),
    ("遍历集合", TestForEach),
    ("递归函数", TestRecursion),
    ("对象与数组成员", TestMembers),
    ("异常捕获", TestTryCatch),
    ("JSON 与标准库", TestJson),
    ("入门示例工程", TestBeginnerExample),
    ("现代特性示例工程", TestModernExample),
    ("跨文件模块导入", TestModuleImport),
    ("源码目录包导入", TestProjectPackageImport),
    ("系统包导入", TestPackageImport),
    ("包控制函数可见性", TestPackageVisibility),
    ("类型标注与赋值检查", TestTypedVariables),
    ("参数返回值类型检查", TestTypedFunctions),
    ("未知类型诊断", TestUnknownType),
    ("重复定义诊断", TestDuplicateDefinition),
    ("参数数量静态诊断", TestArgumentCount),
    ("非法控制流静态诊断", TestInvalidControlFlow),
    ("模块诊断保留源码位置", TestModuleDiagnosticSource),
    ("拒绝旧版 .ey 文件", TestRejectLegacyFile),
    ("语法错误行号", TestParseError),
    ("静态未定义变量诊断", TestUndefinedVariable),
    ("运行时错误行号", TestRuntimeError),
    ("源码模型往返", TestStructuredModelExtensions),
    ("拒绝源码版本头", TestRejectSourceVersionHeader),
    ("语句行解析与全角输入", TestParseStatementText),
    ("官方文本命令", TestOfficialTextCommands),
    ("文本处理组合", TestTextProcessing),
    ("文件读写与磁盘操作", TestFileCommands),
    ("目录文件列举", TestListDirectory),
    ("环境与时间命令", TestEnvironmentCommands),
    ("交换变量场景", TestSwapVariables),
    ("调试断点暂停", TestDebugBreakpoint),
    ("控件 ID 上下文", TestControlIdContext)
};

var failures = 0;
foreach (var (name, test) in tests)
{
    try
    {
        test();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"通过  {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"失败  {name}: {exception.Message}");
    }
    finally
    {
        Console.ResetColor();
    }
}

Console.WriteLine();
Console.WriteLine(failures == 0
    ? $"全部 {tests.Count} 项测试通过。"
    : $"{failures} 项测试失败。");
return failures == 0 ? 0 : 1;

static void TestOutput()
{
    var output = Run("""
        .程序集 示例
        变量 名字 = "易语"
        输出("你好，" + 名字)
        输出 "第二行"
        """);
    Equal("你好，易语\n第二行", output.Output);
}

static void TestPrecedence()
{
    var output = Run("""
        输出(1 + 2 * 3)
        输出((1 + 2) * 3)
        输出(真 且 非 假)
        输出(空 ?? "默认")
        """);
    Equal("7\n9\n真\n默认", output.Output);
}

static void TestNestedComments()
{
    var output = Run("""
        /* 外层注释
            /* 内层注释 */
           外层仍在注释
        */
        输出("注释之后")
        """);
    Equal("注释之后", output.Output);
}

static void TestIfElseIf()
{
    var output = Run("""
        变量 分数 = 86
        如果 分数 >= 90 则
            输出("优秀")
        否则如果 分数 >= 60 则
            输出("及格")
        否则
            输出("不及格")
        结束如果
        """);
    Equal("及格", output.Output);
}

static void TestCountLoop()
{
    var output = Run("""
        变量 总数 = 0
        计次循环 数字 从 1 到 10
            如果 数字 == 6 则
                跳出
            结束如果
            总数 = 总数 + 数字
        结束循环
        输出(总数)
        """);
    Equal("15", output.Output);
}

static void TestForEach()
{
    var output = Run("""
        变量 总数 = 0
        遍历 项目 于 [2, 4, 6, 8]
            总数 += 项目
        结束遍历
        输出(总数)
        """);
    Equal("20", output.Output);
}

static void TestRecursion()
{
    var output = Run("""
        子程序 斐波那契(序号)
            如果 序号 <= 1 则
                返回 序号
            结束如果
            返回 斐波那契(序号 - 1) + 斐波那契(序号 - 2)
        结束子程序
        输出(斐波那契(10))
        """);
    Equal("55", output.Output);
}

static void TestMembers()
{
    var result = Run("""
        变量 用户 = {姓名: "林川", 年龄: 20, 标签: ["中文", "现代"]}
        用户.年龄 += 1
        用户.标签[1] = "原生"
        输出(用户.姓名 + "，" + 用户.年龄)
        用户
        """);
    Equal("林川，21", result.Output);
    True(result.Result.Variables.ContainsKey("用户"), "运行结果应保留全局变量。");
}

static void TestTryCatch()
{
    var output = Run("""
        尝试
            变量 结果 = 10 / 0
        捕获 错误
            输出("已捕获：" + 错误)
        结束尝试
        """);
    True(output.Output.Contains("不能除以零", StringComparison.Ordinal), "捕获到的错误内容不正确。");
}

static void TestJson()
{
    var output = Run("""
        变量 数据 = JSON解析("{\"name\":\"易语\",\"version\":3}")
        输出(数据.name)
        输出(JSON生成({版本: 数据.version}))
        """);
    Equal("易语\n{\"版本\":3}", output.Output);
}

static void TestBeginnerExample()
{
    var result = RunFile(Path.Combine(
        Directory.GetCurrentDirectory(),
        "examples",
        "Sources",
        "入门.yyc"));
    True(result.Contains("平均分：91.5", StringComparison.Ordinal), "入门示例的平均分不正确。");
}

static void TestModernExample()
{
    var result = RunFile(Path.Combine(
        Directory.GetCurrentDirectory(),
        "examples",
        "Sources",
        "现代特性.yyc"));
    True(result.Contains("异常保护捕获", StringComparison.Ordinal), "现代特性示例未捕获异常。");
    True(result.Contains("[1, 4, 9, 16, 25, 36, 49, 64]", StringComparison.Ordinal), "平方数结果不正确。");
}

static void TestModuleImport()
{
    var path = Path.Combine(
        Directory.GetCurrentDirectory(),
        "examples",
        "Sources",
        "入口",
        "主程序.yyc");
    var result = RunFile(path);
    True(result.Contains("欢迎，易语开发者！", StringComparison.Ordinal), "模块函数结果不正确。");
    True(result.Contains("折后价格：115.2", StringComparison.Ordinal), "模块默认参数结果不正确。");
}

static void TestProjectPackageImport()
{
    var root = Path.Combine(
        Path.GetTempPath(),
        "yy-package-folder-" + Guid.NewGuid().ToString("N"));
    var toolsDirectory = Path.Combine(root, "Sources", "tools");
    Directory.CreateDirectory(toolsDirectory);
    try
    {
        File.WriteAllText(
            Path.Combine(root, "PackageFolder.yyproj"),
            """
            {
              "格式版本": 1,
              "名称": "PackageFolder",
              "包名": "demo.app",
              "源码目录": "Sources",
              "资源目录": "Resources",
              "入口": "Sources/Program.yyc"
            }
            """);
        File.WriteAllText(
            Path.Combine(toolsDirectory, "问候.yyc"),
            YyCodeSerializer.Serialize(YyCodeDocumentConverter.FromSource(
                """
                .程序集 tools.问候
                子程序 取问候(): 文本
                    返回 "来自目录包"
                结束子程序
                """)));
        File.WriteAllText(
            Path.Combine(toolsDirectory, "计算.yyc"),
            YyCodeSerializer.Serialize(YyCodeDocumentConverter.FromSource(
                """
                .程序集 tools.计算
                子程序 取结果(): 数字
                    返回 42
                结束子程序
                """)));
        var entryPath = Path.Combine(root, "Sources", "Program.yyc");
        File.WriteAllText(
            entryPath,
            YyCodeSerializer.Serialize(YyCodeDocumentConverter.FromSource(
                """
                .程序集 PackageFolder
                导入包 "系统.基础"
                引入 "demo.app.tools"
                输出(取问候() + "：" + 取结果())
                """,
                entryPath)));

        var result = RunFile(entryPath);
        True(
            result.Contains("来自目录包：42", StringComparison.Ordinal),
            "源码目录包没有加载目录中的全部模块。");
    }
    finally
    {
        Directory.Delete(root, recursive: true);
    }
}

static void TestPackageImport()
{
    Equal(1, Yy.Sdk.YySdk.Version);
    Equal(21, Yy.Sdk.YySdk.Packages.Count);
    Equal(7, Yy.Sdk.YySdk.Packages.Count(package => package.IsImportable));
    True(Yy.Sdk.YySdk.IsPackageName("demo.app"), "点分隔包名应有效。");
    True(!Yy.Sdk.YySdk.IsPackageName("demo..app"), "空包路径段应被拒绝。");
    True(!Yy.Sdk.YySdk.IsPackageName("1demo"), "数字开头的包名应被拒绝。");
    True(
        Yy.Sdk.YySdk.Packages.All(package => package.Members.Count > 0),
        "每个系统 SDK 包都应声明成员。");
    Equal(
        8,
        Yy.Sdk.YySdk.Packages.Count(package =>
            package.Kind == Yy.Sdk.YyPackageKind.LanguageCore));
    Equal(
        11,
        Yy.Sdk.YySdk.Packages.Count(package =>
            package.Kind == Yy.Sdk.YyPackageKind.Platform));
    Equal(
        2,
        Yy.Sdk.YySdk.Packages.Count(package =>
            package.Kind == Yy.Sdk.YyPackageKind.Tooling));

    var program = YyRunner.Compile("""
        .程序集 包测试
        导入包 "系统.界面"
        输出("包已导入")
        """);
    Equal("系统.界面", program.PackageImports.Single().Name);

    var document = YyCodeDocumentConverter.FromSource(
        ".程序集 包测试\n导入包 \"系统.界面\"\n");
    Equal("系统.界面", document.Packages.Single());
    var source = YyCodeDocumentConverter.ToSource(document);
    True(source.Contains("导入包 \"系统.界面\"", StringComparison.Ordinal), "包导入没有写回源码。");
    var restored = YyCodeSerializer.Deserialize(YyCodeSerializer.Serialize(document));
    Equal("系统.界面", restored.Packages.Single());

    var temporaryDirectory = Path.Combine(
        Path.GetTempPath(),
        "yy-package-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temporaryDirectory);
    try
    {
        var module = new YyCodeDocument { Name = "数据模块" };
        module.Packages.Add(Yy.Sdk.YySdk.FilePackage);
        File.WriteAllText(
            Path.Combine(temporaryDirectory, "数据模块.yyc"),
            YyCodeSerializer.Serialize(module));

        var root = new YyCodeDocument { Name = "包入口" };
        root.Packages.Add(Yy.Sdk.YySdk.UiPackage);
        root.Imports.Add("数据模块.yyc");
        var rootPath = Path.Combine(temporaryDirectory, "程序.yyc");
        File.WriteAllText(rootPath, YyCodeSerializer.Serialize(root));

        var merged = YyRunner.CompileFile(rootPath);
        Equal(
            "系统.界面,系统.文件",
            string.Join(",", merged.PackageImports.Select(package => package.Name)));
    }
    finally
    {
        Directory.Delete(temporaryDirectory, recursive: true);
    }

    try
    {
        YyRunner.Compile("""
            导入包 "不存在.包"
            """);
        throw new Exception("未知系统包应被拒绝。");
    }
    catch (YyDiagnostic diagnostic)
    {
        True(diagnostic.Message.Contains("找不到系统包", StringComparison.Ordinal), diagnostic.Message);
    }

    var corePackages = YyRunner.Compile("""
        导入包 "系统.基础"
        导入包 "系统.集合"
        导入包 "系统.文本"
        导入包 "系统.数据"
        """);
    Equal(4, corePackages.PackageImports.Count);
}
static void TestPackageVisibility()
{
    var defaultOutput = new List<string>();
    YyRunner.RunSource(
        """
        .程序集 默认引用
        输出("核心包默认可见")
        """,
        null,
        defaultOutput.Add);
    Equal("核心包默认可见", string.Join("\n", defaultOutput));

    try
    {
        YyRunner.RunSource(
            """
            .程序集 平台包未导入
            文件存在("notepad.exe")
            """,
            null);
        throw new Exception("未导入的平台包函数不应可用。");
    }
    catch (YyDiagnostic diagnostic)
    {
        True(diagnostic.Message.Contains("没有导入", StringComparison.Ordinal), diagnostic.Message);
        True(
            diagnostic.Hint?.Contains("系统.文件", StringComparison.Ordinal) == true,
            diagnostic.Hint ?? "缺少包名提示。");
    }

    var output = new List<string>();
    YyRunner.RunSource(
        """
        .程序集 平台包显式导入
        导入包 "系统.文件"
        输出("平台包可见")
        """,
        null,
        output.Add);
    Equal("平台包可见", string.Join("\n", output));
}

static void TestTypedVariables()
{
    var output = Run("""
        变量 年龄: 数字 = 20
        年龄 = 21
        变量 名字: 文本 = "易语"
        变量 分数: 数字 = 95.5
        输出(名字 + "，" + 年龄 + "，" + 分数)
        """);
    Equal("易语，21，95.5", output.Output);

    try
    {
        Run("""
            变量 年龄: 数字 = 20
            年龄 = "二十一"
            """);
        throw new Exception("预期类型赋值错误。");
    }
    catch (YyDiagnostic error)
    {
        Equal(2, error.Line);
        True(error.Message.Contains("需要“数字”", StringComparison.Ordinal), error.Message);
    }
}

static void TestTypedFunctions()
{
    var output = Run("""
        子程序 描述年龄(年龄: 数字): 文本
            返回 "年龄：" + 文本(年龄)
        结束子程序
        输出(描述年龄(20))
        """);
    Equal("年龄：20", output.Output);

    try
    {
        Run("""
            子程序 错误返回(): 数字
                返回 "不是数字"
            结束子程序
            输出(错误返回())
            """);
        throw new Exception("预期返回类型错误。");
    }
    catch (YyDiagnostic error)
    {
        Equal(2, error.Line);
    }
}

static void TestUnknownType()
{
    try
    {
        YyRunner.Compile("变量 值: 不存在的类型 = 1");
        throw new Exception("预期出现未知类型错误。");
    }
    catch (YyDiagnostic diagnostic)
    {
        Equal(1, diagnostic.Line);
        True(diagnostic.Message.Contains("未知类型", StringComparison.Ordinal), diagnostic.Message);
    }
}

static void TestDuplicateDefinition()
{
    try
    {
        Run("""
            变量 数值 = 1
            变量 数值 = 2
            """);
        throw new Exception("预期出现重复定义诊断。");
    }
    catch (YyDiagnostic diagnostic)
    {
        Equal(2, diagnostic.Line);
        True(diagnostic.Message.Contains("重复定义", StringComparison.Ordinal), diagnostic.Message);
    }
}

static void TestArgumentCount()
{
    try
    {
        Run("""
            子程序 相加(左: 数字, 右: 数字): 数字
                返回 左 + 右
            结束子程序
            输出(相加(1))
            """);
        throw new Exception("预期出现参数数量诊断。");
    }
    catch (YyDiagnostic diagnostic)
    {
        Equal(4, diagnostic.Line);
    }
}

static void TestInvalidControlFlow()
{
    try
    {
        Run("跳出");
        throw new Exception("预期出现跳出位置诊断。");
    }
    catch (YyDiagnostic diagnostic)
    {
        Equal(1, diagnostic.Line);
        True(diagnostic.Message.Contains("循环", StringComparison.Ordinal), diagnostic.Message);
    }

    try
    {
        Run("""
            常量 名称 = "易语"
            名称 = "其他"
            """);
        throw new Exception("预期出现常量改写诊断。");
    }
    catch (YyDiagnostic diagnostic)
    {
        Equal(2, diagnostic.Line);
    }
}

static void TestModuleDiagnosticSource()
{
    var directory = Path.Combine(Path.GetTempPath(), "yy-module-diagnostic-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    var modulePath = Path.Combine(directory, "工具.yyc");
    var entryPath = Path.Combine(directory, "入口.yyc");
    var moduleSource = """
        子程序 错误函数()
            返回 模块内未定义
        结束子程序
        """;
    var entrySource = """
        导入包 "系统.基础"
        引入 "工具.yyc"
        输出(错误函数())
        """;
    File.WriteAllText(
        modulePath,
        Yy.Language.Structured.YyCodeSerializer.Serialize(
            Yy.Language.Structured.YyCodeDocumentConverter.FromSource(moduleSource, modulePath)));
    File.WriteAllText(
        entryPath,
        Yy.Language.Structured.YyCodeSerializer.Serialize(
            Yy.Language.Structured.YyCodeDocumentConverter.FromSource(entrySource, entryPath)));

    try
    {
        YyRunner.RunFile(entryPath);
        throw new Exception("预期出现模块静态诊断。");
    }
    catch (YyDiagnostic diagnostic)
    {
        Equal(Path.GetFullPath(modulePath), diagnostic.SourceName);
        True(diagnostic.Line >= 1, "模块诊断缺少行号。");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestRejectLegacyFile()
{
    var path = Path.Combine(
        Path.GetTempPath(),
        "yy-legacy-reject-" + Guid.NewGuid().ToString("N") + ".ey");
    File.WriteAllText(path, "输出(\"不应执行\")");
    try
    {
        try
        {
            YyRunner.RunFile(path);
            throw new Exception("旧版 .ey 文件不应被编译器接受。");
        }
        catch (InvalidDataException exception)
        {
            True(exception.Message.Contains(".yyc", StringComparison.Ordinal), exception.Message);
        }
    }
    finally
    {
        File.Delete(path);
    }
}

static void TestParseError()
{
    try
    {
        YyRunner.Compile("如果 真 则\n输出(\"缺少结束\")\n");
        throw new Exception("预期出现语法错误。");
    }
    catch (YyDiagnostic diagnostic)
    {
        True(diagnostic.Line >= 1, "语法错误缺少行号。");
    }
}

static void TestUndefinedVariable()
{
    try
    {
        Run("\n\n输出(未知变量)");
        throw new Exception("预期出现静态诊断。");
    }
    catch (YyDiagnostic error)
    {
        Equal(3, error.Line);
    }
}

static void TestRuntimeError()
{
    try
    {
        Run("\n\n输出(1 / 0)");
        throw new Exception("预期出现运行时错误。");
    }
    catch (YyRuntimeError error)
    {
        Equal(3, error.Line);
    }
}

static void TestStructuredModelExtensions()
{
    const string source = """
        .程序集 示例程序
        子程序 问候(名称: 文本): 文本
            变量 前缀: 文本 = "你好"
            返回 前缀
        结束子程序
        """;
    var document = Yy.Language.Structured.YyCodeSerializer.Deserialize(source);
    Equal("示例程序", document.Name);
    var parameter = document.Functions[0].Parameters[0];
    Equal("名称", parameter.Name);
    Equal("文本", parameter.TypeName);

    var serialized = Yy.Language.Structured.YyCodeSerializer.Serialize(document);
    True(serialized.Contains("子程序 问候", StringComparison.Ordinal), "源码往返丢失子程序。");
    True(!serialized.Contains("<yy:", StringComparison.Ordinal), ".yyc 不应序列化为 XML。");
    var restored = Yy.Language.Structured.YyCodeSerializer.Deserialize(serialized);
    Equal("示例程序", restored.Name);
    Equal("问候", restored.Functions[0].Name);
}

static void TestRejectSourceVersionHeader()
{
    try
    {
        YyRunner.Compile(".版本 1\n.程序集 示例\n");
        throw new Exception(".yyc 不应接受版本头。");
    }
    catch (YyDiagnostic diagnostic)
    {
        True(
            diagnostic.Message.Contains("格式版本由工程清单管理", StringComparison.Ordinal),
            diagnostic.Message);
    }
}

static void TestParseStatementText()
{
    var assign = Yy.Language.Structured.YyCodeDocumentConverter.ParseStatementText(
        "标签1.标题 ＝ \"我爱易语言！\"");
    Equal(Yy.Language.Structured.YyCodeStatementKinds.Assign, assign.Kind);
    Equal("标签1.标题", assign.Target);
    Equal("=", assign.Operator);

    var call = Yy.Language.Structured.YyCodeDocumentConverter.ParseStatementText("输出(\"你好\")");
    Equal(Yy.Language.Structured.YyCodeStatementKinds.Call, call.Kind);

    var conditional = Yy.Language.Structured.YyCodeDocumentConverter.ParseStatementText("如果 (真) 则");
    Equal(Yy.Language.Structured.YyCodeStatementKinds.If, conditional.Kind);

    try
    {
        Yy.Language.Structured.YyCodeDocumentConverter.ParseStatementText("如果 真 则 多行\n内容");
        throw new Exception("预期出现语句行解析诊断。");
    }
    catch (Yy.Language.Structured.YyCodeFormatException)
    {
    }
}

static void TestOfficialTextCommands()
{
    var output = Run("""
        输出(取文本左边("易语言真好用", 3))
        输出(取文本右边("易语言真好用", 2))
        输出(取文本中间("易语言真好用", 2, 2))
        输出(取文本中间("易语言真好用", 4))
        输出(取文本左边("短", 99))
        """);
    Equal("易语言\n好用\n语言\n真好用\n短", output.Output);
}

static void TestTextProcessing()
{
    var output = Run("""
        输出(替换("一,二,三", ",", "、"))
        输出(修剪("  空格  "))
        输出(转大写("abc") + "/" + 转小写("ABC"))
        输出(取子文本("易语言真好用", 1, 2))
        输出(长度("易语") + "/" + 包含("易语言", "语言"))
        """);
    Equal("一、二、三\n空格\nABC/abc\n语言\n2/真", output.Output);
}

static void TestFileCommands()
{
    var directory = Path.Combine(Path.GetTempPath(), "yy-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        var path = Path.Combine(directory, "数据.txt");
        var output = Run($"""
            写文件("{path.Replace("\\", "\\\\")}", "内容123")
            输出(文件存在("{path.Replace("\\", "\\\\")}"))
            输出(长度(读文件("{path.Replace("\\", "\\\\")}")))
            删除文件("{path.Replace("\\", "\\\\")}")
            输出(文件存在("{path.Replace("\\", "\\\\")}"))
            """);
        Equal("真\n5\n假", output.Output);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestListDirectory()
{
    var directory = Path.Combine(Path.GetTempPath(), "yy-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    try
    {
        File.WriteAllText(Path.Combine(directory, "b.png"), "x");
        File.WriteAllText(Path.Combine(directory, "a.png"), "x");
        File.WriteAllText(Path.Combine(directory, "c.txt"), "x");
        var output = Run($"""
            变量 文件列表 = 取目录文件("{directory.Replace("\\", "\\\\")}", "*.png")
            输出(长度(文件列表))
            输出(包含(连接(文件列表, ";"), "a.png"))
            输出(长度(取目录文件("{directory.Replace("\\", "\\\\")}")))
            输出(长度(取目录文件("Z:\\不存在的目录")))
            """);
        Equal("2\n真\n3\n0", output.Output);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestEnvironmentCommands()
{
    var output = Run("""
        变量 起点 = 取启动时间()
        延时(30)
        输出(取启动时间() - 起点 >= 20)
        输出(长度(取运行目录()) > 0)
        输出(类型(取环境变量("PATH")))
        """);
    Equal("真\n真\n文本", output.Output);
}

static void TestSwapVariables()
{
    // 对应官方示例“交换变量.e”的经典场景。
    var output = Run("""
        变量 甲 = 1
        变量 乙 = 2
        变量 临时 = 甲
        甲 = 乙
        乙 = 临时
        输出(甲, 乙)
        """);
    Equal("2 1", output.Output);
}

static void TestDebugBreakpoint()
{
    var pausedLines = new List<int>();
    var probe = new YyDebugProbe(YyDebugMode.Run);
    probe.IsBreakpoint = line => line == 5;
    probe.Paused += pause =>
    {
        pausedLines.Add(pause.Line);
        probe.Continue();
    };
    var lines = new List<string>();
    YyRunner.Run(TestProgram("""
        .程序集 断点示例
        输出("一")
        输出("二")
        输出("三")
        输出("四")
        """), lines.Add, CancellationToken.None, probe);
    Equal("一\n二\n三\n四", string.Join("\n", lines));
    Equal(1, pausedLines.Count);
    Equal(5, pausedLines[0]);
}

static void TestControlIdContext()
{
    var controls = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["@id/主窗口/标题标签"] = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["文本"] = "控件上下文"
        }
    };
    var output = new List<string>();
    YyRunner.RunSource(
        TestProgram("""输出(取控件("@id/主窗口/标题标签").文本)"""),
        null,
        output.Add,
        controlResolver: id => controls.TryGetValue(id, out var value) ? value : null);
    Equal("控件上下文", string.Join("\n", output));
}

static (string Output, YyRunResult Result) Run(string source)
{
    var lines = new List<string>();
    var result = YyRunner.Run(TestProgram(source), lines.Add);
    return (string.Join("\n", lines), result);
}

static string TestProgram(string source) =>
    source.TrimEnd() +
    Environment.NewLine +
    Environment.NewLine +
    """
    导入包 "系统.基础"
    导入包 "系统.集合"
    导入包 "系统.文本"
    导入包 "系统.数据"
    导入包 "系统.界面"
    导入包 "系统.文件"
    """;

static string RunFile(string path)
{
    if (!File.Exists(path))
    {
        throw new FileNotFoundException("找不到示例文件。", path);
    }

    var lines = new List<string>();
    YyRunner.RunFile(path, lines.Add);
    return string.Join("\n", lines);
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"期望“{expected}”，实际“{actual}”。");
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}
