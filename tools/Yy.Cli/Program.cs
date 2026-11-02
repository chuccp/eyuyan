using System.Diagnostics;
using System.IO;
using Yy.Compiler;
using Yy.Native;

return await CliApplication.RunAsync(args);

internal static class CliApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || IsHelp(args[0]))
        {
            PrintHelp();
            return args.Length == 0 ? 2 : 0;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "build" => BuildNative(args[1..]),
                "check" => Check(args[1..]),
                "run" => await RunProjectAsync(args[1..]),
                "convert" => ConvertLegacy(args[1..]),
                "version" or "--version" or "-v" => PrintVersion(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException or NativeCompileException
                or PlatformNotSupportedException)
        {
            Console.Error.WriteLine("yy: " + exception.Message);
            return 1;
        }
    }

    /// <summary>yy convert:老易语言程序(.e)转换为易语工程。</summary>
    private static int ConvertLegacy(string[] args)
    {
        string? input = null;
        string? output = null;
        string? name = null;
        string? package = null;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--out" or "-o":
                    output = args[++index];
                    break;
                case "--name":
                    name = args[++index];
                    break;
                case "--package":
                    package = args[++index];
                    break;
                default:
                    input ??= args[index];
                    break;
            }
        }

        if (input is null)
        {
            throw new InvalidDataException("yy convert 需要一个 .e 老程序文件。");
        }

        output ??= Path.Combine(
            Path.GetDirectoryName(Path.GetFullPath(input)) ?? ".",
            Path.GetFileNameWithoutExtension(input) + "_易语");
        var result = Yy.Compiler.Convert.EProgramConverter.Convert(input, output, name, package);
        Console.WriteLine("已转换：" + result.ProjectFile);
        Console.WriteLine("入口源码：" + result.SourceFile);
        Console.WriteLine(
            $"还原子程序 {result.RecoveredProcedures.Count} 个、语句 {result.RecoveredStatements} 行;" +
            $"未归类 {result.UnmappedStrings} 条。");
        Console.WriteLine("导入报告：" + result.ReportFile);
        return 0;
    }

    private static int BuildNative(string[] args)
    {
        var options = NativeOptions.Parse(args);
        var input = Path.GetFullPath(options.Input);
        NativeBuildResult result;
        if (Directory.Exists(input) ||
            string.Equals(Path.GetExtension(input), ".yyproj", StringComparison.OrdinalIgnoreCase))
        {
            var project = LoadProject(input);
            var target = YyBuildTargets.Resolve(options.Target ?? project.TargetPlatform);
            var output = Path.GetFullPath(
                options.Output ?? Path.Combine(
                    project.DirectoryPath,
                    Path.GetFileNameWithoutExtension(project.OutputName) +
                    target.ExecutableExtension));
            output = YyBuildTargets.NormalizeOutputPath(output, target);
            result = NativeCompiler.CompileProject(project, output, target.Id);
        }
        else
        {
            if (!File.Exists(input) ||
                !string.Equals(Path.GetExtension(input), ".yyc", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("yy build 需要一个 .yyc 源文件、.yyproj 或项目目录。");
            }

            var target = YyBuildTargets.Resolve(options.Target);
            var output = Path.GetFullPath(
                options.Output ??
                Path.Combine(
                    Path.GetDirectoryName(input) ?? Environment.CurrentDirectory,
                    Path.GetFileNameWithoutExtension(input) + target.ExecutableExtension));
            output = YyBuildTargets.NormalizeOutputPath(output, target);
            result = NativeCompiler.CompileFile(input, output, target.Id);
        }

        Console.WriteLine(
            $"已生成原生程序：{result.OutputPath} · {result.ImageSize} 字节 · {result.Target.Id}");
        return 0;
    }

    private static int Check(string[] args)
    {
        var options = NativeOptions.Parse(args);
        var project = LoadProject(options.Input);
        new ProjectCompiler().Validate(project);
        Console.WriteLine(
            $"检查通过：{project.Name} · {project.SourcePaths.Count} 个源码 · " +
            $"{project.Forms.Count} 个窗体 · {project.ResourceTable.Count} 个资源");
        return 0;
    }

    private static async Task<int> RunProjectAsync(string[] args)
    {
        var options = NativeOptions.Parse(args);
        var project = LoadProject(options.Input);
        var target = YyBuildTargets.Resolve(options.Target ?? project.TargetPlatform);
        if (!YyBuildTargets.CanRunOnCurrentHost(target))
        {
            throw new PlatformNotSupportedException(
                $"不能在当前系统运行目标“{target.Id}”。");
        }

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "yy-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var output = Path.Combine(
                temporaryDirectory,
                Path.GetFileNameWithoutExtension(project.OutputName) +
                target.ExecutableExtension);
            NativeCompiler.CompileProject(project, output, target.Id);
            Console.WriteLine($"运行：{output}");
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = output,
                WorkingDirectory = project.DirectoryPath,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("无法启动编译后的程序。");
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (IOException)
            {
                // A just-exited Windows process can briefly keep the file locked.
            }
        }
    }

    private static YyProject LoadProject(string input)
    {
        var path = Path.GetFullPath(input);
        var directory = Directory.Exists(path)
            ? path
            : File.Exists(path) &&
              string.Equals(Path.GetExtension(path), ".yyproj", StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(path)!
                : throw new InvalidDataException($"找不到工程或目录：{input}");
        return YyProject.LoadForDirectory(directory)
            ?? throw new InvalidDataException($"目录中没有 .yyproj：{directory}");
    }

    private static bool IsHelp(string value) =>
        value is "help" or "--help" or "-h";

    private static int PrintVersion()
    {
        Console.WriteLine("yy 1.0 · 易语命令编译器 · 工程格式 1");
        return 0;
    }

    private static int UnknownCommand(string command)
    {
        Console.Error.WriteLine($"yy: 未知命令“{command}”");
        PrintHelp();
        return 2;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            易语命令行编译器

            用法：
              yy build <源码.yyc|工程.yyproj|目录> [--target 目标] [-o 输出]
              yy check <工程.yyproj|目录>
              yy run <工程.yyproj|目录>
              yy convert <老程序.e> [--out 目录] [--name 项目名] [--package 包名]
              yy version

            示例：
              yy build examples/原生窗体 -o Dist/原生窗体.exe
              yy build examples/原生窗体 --target windows-x64 -o Dist/原生窗体.exe
              yy build examples/原生窗体 --target macos-arm64 -o Dist/原生窗体
              yy build examples/Sources/原生入门.yyc --target macos-arm64 -o Dist/原生入门
              yy build examples/Sources/原生入门.yyc -o Dist/原生入门.exe
              yy check examples/原生窗体
              yy run examples/原生窗体

            目标：
              auto        当前系统
              windows-x64 Win32 / PE32+
              macos-arm64 AppKit / Mach-O (Apple Silicon，控制台与窗体)
              macos-x64   AppKit / Mach-O (Intel，开发中)
            """);
    }

    private sealed record NativeOptions(string Input, string? Output, string? Target)
    {
        public static NativeOptions Parse(string[] args)
        {
            string? input = null;
            string? output = null;
            string? target = null;
            for (var index = 0; index < args.Length; index++)
            {
                if (args[index] is "-o" or "--output")
                {
                    if (++index >= args.Length)
                    {
                        throw new ArgumentException("-o 缺少输出路径。");
                    }

                    output = args[index];
                    continue;
                }

                if (args[index] is "--target" or "-t")
                {
                    if (++index >= args.Length)
                    {
                        throw new ArgumentException("--target 缺少目标平台。");
                    }

                    target = args[index];
                    continue;
                }

                if (args[index].StartsWith("--target=", StringComparison.OrdinalIgnoreCase))
                {
                    target = args[index]["--target=".Length..];
                    continue;
                }

                if (args[index].StartsWith('-'))
                {
                    throw new ArgumentException("未知选项：" + args[index]);
                }

                if (input is not null)
                {
                    throw new ArgumentException("只能指定一个 .yyc 源文件。");
                }

                input = args[index];
            }

            return string.IsNullOrWhiteSpace(input)
                ? throw new ArgumentException("缺少源码、工程或目录。")
                : new NativeOptions(input, output, target);
        }
    }

}
