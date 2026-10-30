using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Automation;
using Yy.Compiler;
using Yy.Compiler.Forms;
using Yy.Native;

var failures = 0;
Run("原生 PE 与文本输出", TestHello);
Run("原生整数、循环和条件", TestCoreLanguage);
Run("原生数组、索引、遍历与追加", TestArrays);
Run("原生对象与属性", TestObjects);
Run("原生文件读写与目录列举", TestFiles);
Run("原生子程序与隐式主程序", TestFunctions);
Run("隐式入口与无 CLR 依赖", TestImplicitEntry);
Run("原生 Win32 窗口与按钮事件", TestWin32Window);

if (failures > 0)
{
    throw new Exception($"{failures} 项原生测试失败。");
}

Console.WriteLine("全部原生编译测试通过。");
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

static void TestHello()
{
    var directory = CreateDirectory();
    try
    {
        var source = WriteSource(
            directory,
            """
            .程序集 原生问候
            导入包 "系统.基础"
            输出("你好，原生易语")
            """);
        var output = Path.Combine(directory, "hello.exe");
        var result = NativeCompiler.CompileFile(source, output);
        True(result.ImageSize < 100_000, $"原生程序体积异常：{result.ImageSize}");
        True(File.ReadAllBytes(output).Take(2).SequenceEqual("MZ"u8.ToArray()), "缺少 MZ 头。");
        Equal("你好，原生易语\r\n", RunExe(output));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestCoreLanguage()
{
    var directory = CreateDirectory();
    try
    {
        var source = WriteSource(
            directory,
            """
            .程序集 原生语法测试
            导入包 "系统.基础"

            变量 总和: 数字 = 0
            计次循环 序号 从 1 到 5 步长 1
                总和 = 总和 + 序号
            结束循环
            输出(总和)
            如果 总和 == 15 则
                输出("循环正确")
            否则
                输出("循环错误")
            结束如果
            如果 2 < 3 且 5 > 4 则
                输出("比较正确")
            结束如果
            输出("原生" + "文本")
            """);
        var output = Path.Combine(directory, "core.exe");
        NativeCompiler.CompileFile(source, output);
        Equal("15\r\n循环正确\r\n比较正确\r\n原生文本\r\n", RunExe(output));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestImplicitEntry()
{
    var directory = CreateDirectory();
    try
    {
        var source = WriteSource(
            directory,
            """
            .程序集 隐式入口
            导入包 "系统.基础"
            变量 数值: 数字 = 40
            数值 = 数值 + 2
            输出(数值)
            """);
        var output = Path.Combine(directory, "entry.exe");
        NativeCompiler.CompileFile(source, output);
        var bytes = File.ReadAllBytes(output);
        var peOffset = BitConverter.ToInt32(bytes, 0x3C);
        var optional = peOffset + 24;
        var dataDirectories = optional + 112;
        var clrRva = BitConverter.ToUInt32(bytes, dataDirectories + 14 * 8);
        Equal(0u, clrRva);
        Equal("42\r\n", RunExe(output));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestArrays()
{
    var directory = CreateDirectory();
    try
    {
        var source = WriteSource(
            directory,
            """
            .程序集 原生数组
            导入包 "系统.基础"
            导入包 "系统.集合"

            变量 数列: 数组 = [1, 2, 3]
            追加(数列, 4)
            变量 总和: 数字 = 0
            遍历 项目 于 数列
                总和 = 总和 + 项目
            结束遍历
            输出(数列[2])
            输出(总和)
            """);
        var output = Path.Combine(directory, "arrays.exe");
        NativeCompiler.CompileFile(source, output);
        Equal("3\r\n10\r\n", RunExe(output));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestFunctions()
{
    var directory = CreateDirectory();
    try
    {
        var source = WriteSource(
            directory,
            """
            .程序集 原生函数
            导入包 "系统.基础"

            子程序 加倍(数值: 数字): 数字
                返回 数值 * 2
            结束子程序

            变量 结果: 数字 = 加倍(21)
            输出(结果)
            """);
        var output = Path.Combine(directory, "functions.exe");
        NativeCompiler.CompileFile(source, output);
        Equal("42\r\n", RunExe(output));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestObjects()
{
    var directory = CreateDirectory();
    try
    {
        var source = WriteSource(
            directory,
            """
            .程序集 原生对象
            导入包 "系统.基础"
            导入包 "系统.集合"

            变量 学生: 对象 = {姓名: "林川", 成绩: [90, 95]}
            学生.姓名 = "小明"
            输出(学生.姓名)
            输出(学生.成绩[1])
            """);
        var output = Path.Combine(directory, "objects.exe");
        NativeCompiler.CompileFile(source, output);
        Equal("小明\r\n95\r\n", RunExe(output));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestFiles()
{
    var directory = CreateDirectory();
    try
    {
        var file = Path.Combine(directory, "测试.txt").Replace("\\", "\\\\");
        var folder = directory.Replace("\\", "\\\\");
        var source = WriteSource(
            directory,
            $$"""
            .程序集 原生文件
            导入包 "系统.基础"
            导入包 "系统.集合"
            导入包 "系统.文件"

            写文件("{{file}}", "你好，文件")
            输出(文件存在("{{file}}"))
            输出(读文件("{{file}}"))
            变量 文件列表: 数组 = 取目录文件("{{folder}}", "*.txt")
            输出(长度(文件列表))
            删除文件("{{file}}")
            输出(文件存在("{{file}}"))
            """);
        var output = Path.Combine(directory, "files.exe");
        NativeCompiler.CompileFile(source, output);
        Equal("真\r\n你好，文件\r\n1\r\n假\r\n", RunExe(output));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}

static void TestWin32Window()
{
    var directory = CreateDirectory();
    Process? process = null;
    try
    {
        var project = YyProject.CreateProject(
            Path.Combine(directory, "NativeWindow.yyproj"),
            "NativeWindow",
            "demo.nativewindow");
        var formPath = project.Forms[0].FormPath;
        var codePath = project.Forms[0].CodePath!;
        var form = new YyFormModel
        {
            Name = "_启动窗口",
            CodeFile = Path.GetFileName(codePath),
            Title = "原生窗口",
            Width = 520,
            Height = 280
        };
        form.Controls.Add(new YyControlModel
        {
            Type = YyControlTypes.Label,
            Name = "结果标签",
            Text = "等待点击",
            X = 50,
            Y = 55,
            Width = 260,
            Height = 36,
            FontSize = 20
        });
        form.Controls.Add(new YyControlModel
        {
            Type = YyControlTypes.Button,
            Name = "测试按钮",
            Text = "点击我",
            X = 50,
            Y = 120,
            Width = 140,
            Height = 42,
            ClickEvent = "_测试按钮_被单击"
        });
        form.Controls.Add(new YyControlModel
        {
            Type = YyControlTypes.TextBox,
            Name = "姓名输入框",
            Text = "原生",
            X = 210,
            Y = 120,
            Width = 180,
            Height = 42
        });
        File.WriteAllText(formPath, YyFormSerializer.Serialize(form), new UTF8Encoding(false));
        File.WriteAllText(
            codePath,
            """
            .程序集 _启动窗口代码
            导入包 "系统.界面"

            子程序 _测试按钮_被单击()
                变量 姓名 = 取控件("@id/_启动窗口/姓名输入框").文本
                取控件("@id/_启动窗口/结果标签").文本 = "你好，" + 姓名
                取控件("@id/_启动窗口").标题 = "事件已执行"
            结束子程序
            """,
            new UTF8Encoding(false));
        YyFormCompanionFiles.WriteGeneratedBinding(formPath, form, codePath);
        project = YyProject.LoadForDirectory(directory)
            ?? throw new Exception("原生 UI 项目无法重新加载。");
        var output = Path.Combine(directory, "window.exe");
        NativeCompiler.CompileProject(project, output);

        process = Process.Start(new ProcessStartInfo
        {
            FileName = output,
            UseShellExecute = false
        }) ?? throw new Exception("无法启动原生窗口。");
        for (var attempt = 0; attempt < 40 && process.MainWindowHandle == IntPtr.Zero; attempt++)
        {
            Thread.Sleep(100);
            process.Refresh();
        }

        True(process.MainWindowHandle != IntPtr.Zero, "没有找到原生窗口。");
        Equal("原生窗口", process.MainWindowTitle);
        var window = AutomationElement.FromHandle(process.MainWindowHandle);
        var button = window.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(AutomationElement.NameProperty, "点击我"));
        True(button is not null, "没有找到 Win32 按钮。");
        var input = window.FindFirst(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Edit));
        True(input is not null, "没有找到 Win32 输入框。");
        ((ValuePattern)input!.GetCurrentPattern(ValuePattern.Pattern)).SetValue("易语");
        NativeUiProbe.ClickButton((IntPtr)button!.Current.NativeWindowHandle);
        Thread.Sleep(1000);
        process.Refresh();
        Equal("事件已执行", process.MainWindowTitle);
        var label = NativeUiProbe.FindWindowEx(
            process.MainWindowHandle,
            IntPtr.Zero,
            "Static",
            null);
        True(label != IntPtr.Zero, "没有找到原生标签窗口。");
        Equal("你好，易语", NativeUiProbe.GetWindowText(label));
    }
    finally
    {
        if (process is { HasExited: false })
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(3000);
        }

        Directory.Delete(directory, recursive: true);
    }
}

static string WriteSource(string directory, string source)
{
    var path = Path.Combine(directory, "程序.yyc");
    File.WriteAllText(path, source, new UTF8Encoding(false));
    return path;
}

static string RunExe(string path)
{
    var start = new ProcessStartInfo
    {
        FileName = path,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };
    using var process = Process.Start(start)
        ?? throw new Exception("无法启动原生程序。");
    var output = process.StandardOutput.ReadToEnd();
    var error = process.StandardError.ReadToEnd();
    process.WaitForExit();
    Equal(0, process.ExitCode);
    True(string.IsNullOrWhiteSpace(error), error);
    return output;
}

static string CreateDirectory()
{
    var path = Path.Combine(
        Path.GetTempPath(),
        "yy-native-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
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

internal static partial class NativeUiProbe
{
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "FindWindowExW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr FindWindowExNative(
        IntPtr parent,
        IntPtr after,
        string? className,
        string? windowName);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr window, StringBuilder text, int count);

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern IntPtr SendMessageNative(
        IntPtr window,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    internal static IntPtr FindWindowEx(
        IntPtr parent,
        IntPtr after,
        string? className,
        string? windowName) =>
        FindWindowExNative(parent, after, className, windowName);

    internal static string GetWindowText(IntPtr window)
    {
        var builder = new StringBuilder(256);
        _ = GetWindowTextW(window, builder, builder.Capacity);
        return builder.ToString();
    }

    internal static void ClickButton(IntPtr button) =>
        _ = SendMessageNative(button, 0x00F5, IntPtr.Zero, IntPtr.Zero);

}
