using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Nodes;
using Yy.Compiler;
using Yy.Compiler.Convert;
using Yy.Language;
using Yy.Native;
using Yy.Studio.Core.Mcp;
using Yy.Studio.Core.Tests;
using Yy.Studio.Core.Workspace;

var failures = 0;

Run("工程加载与源码树", TestLoadProject);
Run("独立源码编译与运行", TestCompileFile);
Run("老程序(.e)转换", TestLegacyConvert);
Run("窗体工程原生编译", TestCompileFormProject);
Run("MCP 协议与工具调用", TestMcpServer);
Run("解释器调试暂停与继续", TestDebugger);
Run("配置默认值", ConfigurationRegression.VerifyDefaults);
Run("配置解析", ConfigurationRegression.VerifyParse);
Run("配置保存与往返", ConfigurationRegression.VerifySaveRoundTrip);
Run("配置校验诊断", ConfigurationRegression.VerifyValidation);
Run("窗口布局配置", ConfigurationRegression.VerifyWindowLayout);
Run("最近文件状态", ConfigurationRegression.VerifyRecentFiles);
Run("快捷键解析", ShortcutRegression.VerifyParsing);
Run("快捷键冲突与停用", ShortcutRegression.VerifyConflictsAndDisable);
Run("开发文档目录与搜索", DocumentationRegression.VerifyCatalog);
Run("开发文档命令参考", DocumentationRegression.VerifyCommands);

Console.WriteLine();
Console.WriteLine(failures == 0 ? "全部 Studio 核心测试通过。" : $"{failures} 项测试失败。");
return failures == 0 ? 0 : 1;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"通过  {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"失败  {name}: {exception.Message}");
    }
}

static void TestLoadProject()
{
    var workspace = new StudioWorkspace();
    workspace.Load(Path.Combine(Directory.GetCurrentDirectory(), "examples", "原生窗体"));
    var project = workspace.Project ?? throw new Exception("工程没有加载。");
    Equal("原生窗体", project.Name);
    True(project.Forms.Count > 0, "工程应包含窗体。");
    ObservableCollection<ProjectNode> tree = workspace.BuildTree();
    var projectNode = tree.Single();
    Equal(project.Name, projectNode.Name);
    True(
        projectNode.Children.Any(node => node.Name == "Sources"),
        "源码树缺少 Sources。");
    True(
        projectNode.Children.Any(node => node.Name == "Resources"),
        "源码树缺少 Resources。");
}

static void TestCompileFile()
{
    var target = HostTargetOrWindows();
    var workspace = new StudioWorkspace();
    var output = Path.Combine(
        Path.GetTempPath(),
        "yy-studio-core-" + Guid.NewGuid().ToString("N"),
        "原生入门" + target.ExecutableExtension);
    try
    {
        var result = workspace.BuildFile(
            Path.Combine(Directory.GetCurrentDirectory(), "examples", "Sources", "原生入门.yyc"),
            output,
            target.Id);
        True(File.Exists(result.OutputPath), "独立源码没有生成可执行文件。");
        Equal(target.Id, result.Target.Id);
        if (!YyBuildTargets.CanRunOnCurrentHost(target))
        {
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = result.OutputPath,
            RedirectStandardOutput = true,
            UseShellExecute = false
        }) ?? throw new Exception("无法启动编译结果。");
        var text = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        True(!string.IsNullOrWhiteSpace(text), "独立源码没有输出。");
    }
    finally
    {
        var directory = Path.GetDirectoryName(output);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void TestCompileFormProject()
{
    var workspace = new StudioWorkspace();
    workspace.Load(Path.Combine(Directory.GetCurrentDirectory(), "examples", "原生窗体"));
    var project = workspace.Project ?? throw new Exception("工程没有加载。");
    var target = YyBuildTargets.Resolve(project.TargetPlatform);
    var output = Path.Combine(
        Path.GetTempPath(),
        "yy-studio-core-" + Guid.NewGuid().ToString("N"),
        "原生窗体" + target.ExecutableExtension);
    try
    {
        var result = workspace.Build(output, target.Id);
        True(File.Exists(result.OutputPath), "窗体工程没有生成可执行文件。");
        True(result.ImageSize > 0, "窗体工程产物为空。");
    }
    finally
    {
        var directory = Path.GetDirectoryName(output);
        if (directory is not null && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void TestDebugger()
{
    var paused = new TaskCompletionSource<Yy.Language.YyDebugPause>();
    var probe = new Yy.Language.YyDebugProbe(Yy.Language.YyDebugMode.StepInto);
    probe.Paused += pause =>
    {
        paused.TrySetResult(pause);
        probe.Continue();
    };
    var output = new List<string>();
    Yy.Language.YyRunner.RunSource(
        ".程序集 调试测试\n变量 数: 数字 = 1\n输出(数)",
        "debug.yyc",
        output.Add,
        debugger: probe);
    True(paused.Task.Wait(TimeSpan.FromSeconds(5)), "调试探针没有暂停。");
    True(paused.Task.Result.Line > 0, "调试暂停没有行号。");
    True(output.Any(line => line.Contains('1')), "调试运行没有继续执行。");
}

static void TestMcpServer()
{
    var server = new StudioMcpServer(
        (tool, _) => new { tool },
        action => action());
    True(
        server.HandleMessage(
            """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26"}}""")
            ?.Contains("\"serverInfo\"", StringComparison.Ordinal) ?? false,
        "MCP 初始化失败。");
    True(
        server.HandleMessage(
            """{"jsonrpc":"2.0","id":2,"method":"tools/list"}""")
            ?.Contains("run_preview", StringComparison.Ordinal) ?? false,
        "MCP 工具目录不完整。");
    True(
        server.HandleMessage(
            """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"state","arguments":{}}}""")
            ?.Contains("state", StringComparison.Ordinal) ?? false,
        "MCP 工具调用失败。");
}

static YyBuildTarget HostTargetOrWindows()
{
    try
    {
        return YyBuildTargets.Current;
    }
    catch (PlatformNotSupportedException)
    {
        return YyBuildTargets.Resolve(YyBuildTargets.WindowsX64);
    }
}

static void TestLegacyConvert()
{
    var fixture = Path.Combine("tests", "Yy.Studio.Core.Tests", "fixtures", "day1.e");
    if (!File.Exists(fixture))
    {
        throw new Exception("缺少转换测试样本 day1.e");
    }

    var output = Path.Combine(
        Path.GetTempPath(),
        "yy-convert-test-" + Guid.NewGuid().ToString("N"));
    try
    {
        var result = EProgramConverter.Convert(fixture, output, "day1导入", "imported.day1");
        True(File.Exists(result.ProjectFile), "应生成工程文件");
        True(File.Exists(result.SourceFile), "应生成入口源码");
        True(File.Exists(result.ReportFile), "应生成导入报告");
        True(result.RecoveredProcedures.Count > 0, "应按帧类型码还原子程序名");
        // 转换产物必须能被解析器直接接受(老语法)。
        var program = YyRunner.CompileFile(result.SourceFile);
        True(program.Functions.Count > 0, "转换产物应包含子程序");

        var bad = Path.Combine(output, "bad.e");
        File.WriteAllBytes(bad, "NOT-E"u8.ToArray());
        try
        {
            EProgramConverter.Convert(bad, Path.Combine(output, "bad"), "bad", "imported.bad");
            throw new Exception("非 .e 文件应被拒绝。");
        }
        catch (InvalidDataException)
        {
        }
    }
    finally
    {
        if (Directory.Exists(output))
        {
            Directory.Delete(output, recursive: true);
        }
    }
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
