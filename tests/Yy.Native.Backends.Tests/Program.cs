using System.Buffers.Binary;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Yy.Compiler;
using Yy.Native;

var failures = 0;
Run("后端注册表与目标选择", TestRegistry);
Run("Mach-O 结构与内嵌签名", TestMachOStructure);
Run("Windows 目标交叉编译", TestWindowsCrossBuild);
Run("macOS 窗体示例交叉编译", TestFormProjects);

if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
    RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
{
    Run("macOS 语言核心与文件运行时", TestMacRuntime);
    Run("AppKit 窗体运行时", TestAppKitRuntime);
}
else
{
    Console.WriteLine("跳过  macOS 运行时测试：当前主机不是 Apple Silicon macOS。");
}

if (failures > 0)
{
    throw new Exception($"{failures} 项后端测试失败。");
}

Console.WriteLine("全部原生后端测试通过。");
return;

void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine("通过  " + name);
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"失败  {name}: {exception.Message}");
    }
}

static void TestRegistry()
{
    var windows = YyBuildTargets.Resolve("windows-x64");
    Equal("windows-x64", windows.Id);
    Equal(".exe", windows.ExecutableExtension);

    var mac = YyBuildTargets.Resolve("macos-arm64");
    Equal("macos-arm64", mac.Id);
    Equal(string.Empty, mac.ExecutableExtension);

    var directory = CreateDirectory();
    try
    {
        try
        {
            NativeCompiler.CompileSource(
                ".程序集 目标测试\n输出(\"x\")",
                "目标测试.yyc",
                Path.Combine(directory, "app"),
                YyBuildTargets.MacOSX64);
            throw new Exception("macos-x64 尚未实现时不应生成产物。");
        }
        catch (PlatformNotSupportedException exception)
        {
            True(
                exception.Message.Contains("macos-x64", StringComparison.Ordinal),
                "未注册的目标必须给出明确诊断。");
        }
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestMachOStructure()
{
    var directory = CreateDirectory();
    try
    {
        var result = NativeCompiler.CompileSource(
            """
            .程序集 MachO结构
            导入包 "系统.基础"
            输出("hello")
            """,
            "MachO结构.yyc",
            Path.Combine(directory, "mach-o"),
            YyBuildTargets.MacOSArm64);
        var image = File.ReadAllBytes(result.OutputPath);
        True(image.Length > 0x2000, $"Mach-O 体积异常：{image.Length}");
        Equal(0xFEEDFACFu, BinaryPrimitives.ReadUInt32LittleEndian(image));
        Equal(0x0100000Cu, BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(4)));
        Equal(2u, BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(12)));

        var commands = ReadLoadCommands(image);
        True(
            commands.Any(item => item.Segment == "__TEXT") &&
            commands.Any(item => item.Segment == "__DATA") &&
            commands.Any(item => item.Segment == "__LINKEDIT"),
            "Mach-O 必须包含 __TEXT、__DATA 和 __LINKEDIT 段。");
        True(
            commands.Any(item => item.Command == 0x80000028 && item.EntryOffset == 0x1000),
            "Mach-O 缺少 LC_MAIN 入口。");
        var signature = commands.FirstOrDefault(item => item.Command == 0x1D);
        True(signature is not null && signature.DataSize > 0, "Mach-O 缺少内嵌代码签名。");
        True(
            commands.Any(item =>
                item.Command == 0xC &&
                item.Name is not null &&
                item.Name.Contains("libSystem", StringComparison.Ordinal)),
            "Mach-O 必须链接 libSystem。");
        True(
            Encoding.ASCII.GetString(image).Contains("_write", StringComparison.Ordinal),
            "Mach-O 绑定表应包含 libSystem 符号。");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestWindowsCrossBuild()
{
    if (!OperatingSystem.IsMacOS())
    {
        return;
    }

    var directory = CreateDirectory();
    try
    {
        var result = NativeCompiler.CompileSource(
            ".程序集 交叉编译\n输出(\"x\")",
            "交叉编译.yyc",
            Path.Combine(directory, "app.exe"),
            YyBuildTargets.WindowsX64);
        var image = File.ReadAllBytes(result.OutputPath);
        Equal((byte)'M', image[0]);
        Equal((byte)'Z', image[1]);
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestFormProjects()
{
    var root = FindRepositoryRoot();
    var examples = Path.Combine(root, "examples");
    var temporary = CreateDirectory();
    try
    {
        var projects = Directory.GetDirectories(examples)
            .Select(directory =>
            {
                var copy = Path.Combine(
                    temporary,
                    Path.GetFileName(directory) + "-" + Guid.NewGuid().ToString("N"));
                CopyDirectory(directory, copy);
                return YyProject.LoadForDirectory(copy);
            })
            .Where(project => project is not null && project.Forms.Count > 0)
            .ToArray();
        True(projects.Length >= 2, "未找到窗体示例工程。");
        foreach (var project in projects)
        {
            var output = Path.Combine(temporary, project!.Name);
            var result = NativeCompiler.CompileProject(
                project,
                output,
                YyBuildTargets.MacOSArm64);
            var image = File.ReadAllBytes(result.OutputPath);
            var commands = ReadLoadCommands(image);
            True(
                commands.Any(item => item.Segment == "__TEXT") &&
                commands.Any(item => item.Segment == "__LINKEDIT"),
                $"{project.Name} 的 Mach-O 段不完整。");
            True(
                commands.Any(item =>
                    item.Command == 0xC &&
                    item.Name is not null &&
                    item.Name.Contains("AppKit", StringComparison.Ordinal)),
                $"{project.Name} 未链接 AppKit。");
            True(
                commands.Any(item => item.Command == 0x1D && item.DataSize > 0),
                $"{project.Name} 缺少内嵌签名。");
        }
    }
    finally
    {
        Directory.Delete(temporary, recursive: true);
    }
}

static void TestAppKitRuntime()
{
    var directory = CreateDirectory();
    try
    {
        var projectDirectory = Path.Combine(directory, "AppKit运行时");
        CreateAppKitProject(
            projectDirectory,
            """
            <yy:文本 标识="@id/_启动窗口/结果标签" 名称="结果标签" 文本="等待" 左="40" 上="40" 宽="260" 高="32" 字号="16" 前景="#17212B" 背景="Transparent" 启用="true" 可见="true" 选中="false" />
            <yy:输入框 标识="@id/_启动窗口/输入框" 名称="输入框" 文本="控件内容" 左="40" 上="88" 宽="260" 高="36" 字号="14" 前景="#17212B" 背景="#FFFFFF" 启用="true" 可见="true" 选中="false" />
            <yy:按钮 标识="@id/_启动窗口/执行按钮" 名称="执行按钮" 文本="执行" 左="40" 上="140" 宽="120" 高="40" 字号="14" 前景="#FFFFFF" 背景="#0E639C" 启用="true" 可见="true" 选中="false" />
            <yy:列表框 标识="@id/_启动窗口/列表" 名称="列表" 文本="甲,乙,丙" 左="40" 上="200" 宽="180" 高="80" 字号="14" 前景="#17212B" 背景="#FFFFFF" 启用="true" 可见="true" 选中="false" />
            <yy:分组框 标识="@id/_启动窗口/分组" 名称="分组" 文本="分组标题" 左="250" 上="200" 宽="140" 高="80" 字号="14" 前景="#17212B" 背景="#F8FAFC" 启用="true" 可见="true" 选中="false" />
            <yy:输入框 标识="@id/_启动窗口/多行框" 名称="多行框" 文本="多行初始" 左="40" 上="300" 宽="220" 高="60" 字号="14" 前景="#17212B" 背景="#EEEEEE" 启用="true" 可见="true" 选中="false" 只读="false" 多行="true" 密码="" 最大长度="500" />
            """,
            """
            结果标签: 任意 = {标识: "@id/_启动窗口/结果标签", 类型: "文本"}
            输入框: 任意 = {标识: "@id/_启动窗口/输入框", 类型: "输入框"}
            执行按钮: 任意 = {标识: "@id/_启动窗口/执行按钮", 类型: "按钮"}
            列表: 任意 = {标识: "@id/_启动窗口/列表", 类型: "列表框"}
            分组: 任意 = {标识: "@id/_启动窗口/分组", 类型: "分组框"}
            多行框: 任意 = {标识: "@id/_启动窗口/多行框", 类型: "输入框"}
            """,
            """
            子程序 _启动窗口_打开()
                _启动窗口.结果标签.文本 = _启动窗口.输入框.文本
                输出("标签：" + _启动窗口.结果标签.文本)
                _启动窗口.标题 = "控件标题"
                输出("标题：" + _启动窗口.标题)
                输出("分组：" + _启动窗口.分组.文本)
                _启动窗口.多行框.文本 = "多行写入"
                输出("多行：" + _启动窗口.多行框.文本)
                运行程序("/bin/sh", "-c \"kill -TERM $PPID\"")
            结束子程序
            """);
        var project = YyProject.LoadForDirectory(projectDirectory)
            ?? throw new Exception("无法加载 AppKit 测试工程。");
        var output = Path.Combine(directory, "app");
        NativeCompiler.CompileProject(project, output, YyBuildTargets.MacOSArm64);
        var (exitCode, stdout, _) = RunProgramWithTimeout(output, projectDirectory, 8000);
        True(exitCode != 0, "AppKit 测试程序应被主动终止。");
        True(
            stdout.Contains("标签：控件内容", StringComparison.Ordinal),
            $"AppKit 标签读写失败：{stdout}");
        True(
            stdout.Contains("标题：控件标题", StringComparison.Ordinal),
            $"AppKit 标题读写失败：{stdout}");
        True(
            stdout.Contains("分组：分组标题", StringComparison.Ordinal),
            $"AppKit 分组框读取失败：{stdout}");
        True(
            stdout.Contains("多行：多行写入", StringComparison.Ordinal),
            $"AppKit 多行输入失败：{stdout}");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void CreateAppKitProject(
    string directory,
    string controls,
    string designerControls,
    string userCode)
{
    var forms = Path.Combine(directory, "Sources", "Forms");
    Directory.CreateDirectory(forms);
    Directory.CreateDirectory(Path.Combine(directory, "Resources", "images"));
    Directory.CreateDirectory(Path.Combine(directory, "Resources", "sounds"));
    Directory.CreateDirectory(Path.Combine(directory, "Resources", "files"));
    File.WriteAllText(
        Path.Combine(directory, "Test.yyproj"),
        """
        {
          "格式版本": 1,
          "名称": "AppKit测试",
          "包名": "demo.appkittest",
          "目标平台": "auto",
          "源码目录": "Sources",
          "资源目录": "Resources",
          "入口": "Sources/Program.yyc",
          "输出": "AppKit测试",
          "启动窗体": "_启动窗口",
          "源码": ["Sources/Program.yyc", "Sources/Forms/MainForm.yyc"],
          "窗体": ["Sources/Forms/MainForm.yyx"],
          "资源": [],
          "资源标识": "Resources/ids.xml",
          "引用": []
        }
        """);
    File.WriteAllText(
        Path.Combine(directory, "Sources", "Program.yyc"),
        ".程序集 AppKit测试\n");
    File.WriteAllText(
        Path.Combine(directory, "Resources", "ids.xml"),
        """
        <?xml version="1.0" encoding="utf-8"?>
        <yy:资源标识 xmlns:yy="urn:yy-studio:resource-ids:1" 格式版本="1" />
        """);
    File.WriteAllText(
        Path.Combine(forms, "MainForm.yyx"),
        $$"""
        <yy:窗体 xmlns:yy="urn:yy-studio:form:1" 标识="@id/_启动窗口" 名称="_启动窗口" 标题="AppKit测试" 宽="420" 高="240" 背景="#F4F7FB" 边框="可调边框">
        {{controls}}
        </yy:窗体>
        """);
    File.WriteAllText(
        Path.Combine(forms, "MainForm.Designer.yyc"),
        $$"""
        .程序集 _启动窗口.Designer
        导入包 "系统.界面"

        结构体 _启动窗口文件
            界面: 文本 = "MainForm.yyx"
            代码: 文本 = "MainForm.yyc"
        结束结构体

        结构体 _启动窗口界面
        {{designerControls}}
        结束结构体

        结构体 _启动窗口事件
            打开: 文本 = "_启动窗口_打开"
        结束结构体
        """);
    File.WriteAllText(
        Path.Combine(forms, "MainForm.yyc"),
        $$"""
        .程序集 _启动窗口代码
        导入包 "系统.基础"
        导入包 "系统.界面"
        导入包 "系统.进程"

        {{userCode}}
        """);
}

static void TestMacRuntime()
{
    var directory = CreateDirectory();
    try
    {
        var textPath = Path.Combine(directory, "后端测试.txt");
        var source = $$"""
            .程序集 后端运行时
            导入包 "系统.基础"
            导入包 "系统.集合"
            导入包 "系统.文件"
            导入包 "系统.进程"

            子程序 平方(值: 数字): 数字
                返回 值 * 值
            结束子程序

            变量 总和: 数字 = 0
            计次循环 序号 从 1 到 5 步长 1
                总和 = 总和 + 平方(序号)
            结束循环
            输出("总和：" + 文本(总和))

            变量 列表: 数组 = ["甲", "乙"]
            追加(列表, "丙")
            变量 文本结果: 文本 = ""
            遍历 项 于 列表
                文本结果 = 文本结果 + 项
            结束遍历
            输出(文本结果)

            变量 记录: 对象 = {名称: "易语", 数量: 3}
            输出(记录.名称 + "：" + 文本(记录.数量))

            变量 路径: 文本 = "{{textPath}}"
            写文件(路径, "文件内容")
            输出(读文件(路径))
            输出(文本(文件存在(路径)))
            输出(文本(删除文件(路径)))
            输出(文本(运行程序("/bin/sh", "-c \"exit 7\"")))
            """;
        var output = Path.Combine(directory, "app");
        NativeCompiler.CompileSource(
            source,
            "后端运行时.yyc",
            output,
            YyBuildTargets.MacOSArm64);
        var (exitCode, stdout, stderr) = RunProgram(output, directory);
        Equal(0, exitCode);
        Equal(
            string.Join(
                '\n',
                "总和：55",
                "甲乙丙",
                "易语：3",
                "文件内容",
                "1",
                "1",
                "7",
                string.Empty),
            Normalize(stdout));
        Equal(string.Empty, Normalize(stderr));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static List<LoadCommand> ReadLoadCommands(byte[] image)
{
    var count = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(16));
    var offset = 32;
    var commands = new List<LoadCommand>();
    for (var index = 0; index < count; index++)
    {
        var command = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset));
        var size = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset + 4));
        string? segment = null;
        string? name = null;
        ulong entry = 0;
        var dataOffset = 0u;
        var dataSize = 0u;
        if (command == 0x19)
        {
            segment = Encoding.ASCII
                .GetString(image, offset + 8, 16)
                .TrimEnd('\0');
        }
        else if (command is 0xC or 0xE)
        {
            var nameOffset = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset + 8));
            var start = offset + (int)nameOffset;
            var end = start;
            while (end < image.Length && image[end] != 0)
            {
                end++;
            }

            name = Encoding.ASCII.GetString(image, start, end - start);
        }
        else if (command == 0x80000028)
        {
            entry = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(offset + 8));
        }
        else if (command == 0x1D)
        {
            dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset + 8));
            dataSize = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(offset + 12));
        }

        commands.Add(new LoadCommand(command, size, segment, name, entry, dataOffset, dataSize));
        offset += (int)size;
    }

    return commands;
}

static (int ExitCode, string Stdout, string Stderr) RunProgram(string path, string workingDirectory)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = path,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    using var process = Process.Start(startInfo)
        ?? throw new Exception("无法启动编译后的程序。");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    return (process.ExitCode, stdout, stderr);
}

static string Normalize(string value) => value.Replace("\r\n", "\n");

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null &&
           !File.Exists(Path.Combine(directory.FullName, "Eyuyan.slnx")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName
        ?? throw new Exception("找不到仓库根目录。");
}

static void CopyDirectory(string source, string destination)
{
    Directory.CreateDirectory(destination);
    foreach (var file in Directory.GetFiles(source))
    {
        File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    }

    foreach (var directory in Directory.GetDirectories(source))
    {
        CopyDirectory(
            directory,
            Path.Combine(destination, Path.GetFileName(directory)));
    }
}

static (int ExitCode, string Stdout, string Stderr) RunProgramWithTimeout(
    string path,
    string workingDirectory,
    int timeoutMilliseconds)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = path,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true
    };
    using var process = Process.Start(startInfo)
        ?? throw new Exception("无法启动编译后的程序。");
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(timeoutMilliseconds))
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

        throw new Exception("AppKit 测试程序未在超时前退出。");
    }

    return (
        process.ExitCode,
        stdoutTask.GetAwaiter().GetResult(),
        stderrTask.GetAwaiter().GetResult());
}

static string CreateDirectory()
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "yy-backend-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new Exception($"期望 {expected}，实际 {actual}。");
    }
}

internal sealed record LoadCommand(
    uint Command,
    uint Size,
    string? Segment,
    string? Name,
    ulong EntryOffset,
    uint DataOffset,
    uint DataSize);
