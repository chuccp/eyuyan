using Yy.Language;

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
    ("结构化模型扩展字段往返", TestStructuredModelExtensions),
    ("语句行解析与全角输入", TestParseStatementText),
    ("官方文本命令", TestOfficialTextCommands),
    ("文本处理组合", TestTextProcessing),
    ("文件读写与磁盘操作", TestFileCommands),
    ("目录文件列举", TestListDirectory),
    ("环境与时间命令", TestEnvironmentCommands),
    ("交换变量场景", TestSwapVariables),
    ("调试断点暂停", TestDebugBreakpoint)
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
        .版本 1
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
    var result = RunFile(Path.Combine(Directory.GetCurrentDirectory(), "examples", "入门.yyc"));
    True(result.Contains("平均分：91.5", StringComparison.Ordinal), "入门示例的平均分不正确。");
}

static void TestModernExample()
{
    var result = RunFile(Path.Combine(Directory.GetCurrentDirectory(), "examples", "现代特性.yyc"));
    True(result.Contains("异常保护捕获", StringComparison.Ordinal), "现代特性示例未捕获异常。");
    True(result.Contains("[1, 4, 9, 16, 25, 36, 49, 64]", StringComparison.Ordinal), "平方数结果不正确。");
}

static void TestModuleImport()
{
    var path = Path.Combine(Directory.GetCurrentDirectory(), "examples", "模块", "主程序.yyc");
    var result = RunFile(path);
    True(result.Contains("欢迎，易语开发者！", StringComparison.Ordinal), "模块函数结果不正确。");
    True(result.Contains("折后价格：115.2", StringComparison.Ordinal), "模块默认参数结果不正确。");
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
    const string legacy = """
        <yy:代码 xmlns:yy="urn:yy-studio:code:1" 格式版本="1" 名称="旧程序集">
          <yy:子程序 名称="问候" 返回类型="文本">
            <yy:参数 名称="名称" 类型="文本" />
            <yy:变量 名称="前缀" 类型="文本" 值="&quot;你好&quot;" />
            <yy:返回 值="前缀" />
          </yy:子程序>
        </yy:代码>
        """;
    var document = Yy.Language.Structured.YyCodeSerializer.Deserialize(legacy);
    Equal(string.Empty, document.Remark);
    True(!document.Functions[0].IsPublic, "子程序默认不公开。");
    Equal(string.Empty, document.Functions[0].Remark);
    var parameter = document.Functions[0].Parameters[0];
    True(!parameter.ByRef && !parameter.Nullable && !parameter.IsArray, "参数标志默认关闭。");
    True(!document.Functions[0].Statements[0].IsStatic, "变量默认非静态。");

    document.Remark = "程序集备注";
    document.Functions[0].IsPublic = true;
    document.Functions[0].Remark = "子程序备注";
    parameter.ByRef = true;
    parameter.Nullable = true;
    parameter.IsArray = true;
    parameter.Remark = "参数备注";
    document.Functions[0].Statements[0].IsStatic = true;

    var xml = Yy.Language.Structured.YyCodeSerializer.Serialize(document);
    True(xml.Contains("公开", StringComparison.Ordinal) &&
         xml.Contains("静态", StringComparison.Ordinal) &&
         xml.Contains("参考", StringComparison.Ordinal),
        "新字段应写入 XML。");

    var restored = Yy.Language.Structured.YyCodeSerializer.Deserialize(xml);
    Equal("程序集备注", restored.Remark);
    True(restored.Functions[0].IsPublic, "公开标志应往返。");
    Equal("子程序备注", restored.Functions[0].Remark);
    var restoredParameter = restored.Functions[0].Parameters[0];
    True(restoredParameter.ByRef && restoredParameter.Nullable && restoredParameter.IsArray,
        "参数标志应往返。");
    Equal("参数备注", restoredParameter.Remark);
    True(restored.Functions[0].Statements[0].IsStatic, "静态标志应往返。");
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
    YyRunner.Run("""
        .版本 1
        .程序集 断点示例
        输出("一")
        输出("二")
        输出("三")
        输出("四")
        """, lines.Add, CancellationToken.None, probe);
    Equal("一\n二\n三\n四", string.Join("\n", lines));
    Equal(1, pausedLines.Count);
    Equal(5, pausedLines[0]);
}

static (string Output, YyRunResult Result) Run(string source)
{
    var lines = new List<string>();
    var result = YyRunner.Run(source, lines.Add);
    return (string.Join("\n", lines), result);
}

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
