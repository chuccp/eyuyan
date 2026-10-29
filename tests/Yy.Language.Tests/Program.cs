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
    ("语法错误行号", TestParseError),
    ("静态未定义变量诊断", TestUndefinedVariable),
    ("运行时错误行号", TestRuntimeError)
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
        .版本 3
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
    var modulePath = Path.Combine(directory, "工具.ey");
    var entryPath = Path.Combine(directory, "入口.ey");
    File.WriteAllText(modulePath, """
        子程序 错误函数()
            返回 模块内未定义
        结束子程序
        """);
    File.WriteAllText(entryPath, """
        引入 "工具.ey"
        输出(错误函数())
        """);

    try
    {
        YyRunner.RunFile(entryPath);
        throw new Exception("预期出现模块静态诊断。");
    }
    catch (YyDiagnostic diagnostic)
    {
        Equal(Path.GetFullPath(modulePath), diagnostic.SourceName);
        Equal(2, diagnostic.Line);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
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
