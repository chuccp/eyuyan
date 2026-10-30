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
                "native" => BuildNative(args[1..]),
                "check" => Check(args[1..]),
                "run" => await RunProjectAsync(args[1..]),
                "version" or "--version" or "-v" => PrintVersion(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException or NativeCompileException)
        {
            Console.Error.WriteLine("yy: " + exception.Message);
            return 1;
        }
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
            var output = Path.GetFullPath(
                options.Output ?? Path.Combine(project.DirectoryPath, project.OutputName));
            EnsureExeExtension(ref output);
            result = NativeCompiler.CompileProject(project, output);
        }
        else
        {
            if (!File.Exists(input) ||
                !string.Equals(Path.GetExtension(input), ".yyc", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("yy native 需要一个 .yyc 源文件、.yyproj 或项目目录。");
            }

            var output = Path.GetFullPath(
                options.Output ??
                Path.Combine(
                    Path.GetDirectoryName(input) ?? Environment.CurrentDirectory,
                    Path.GetFileNameWithoutExtension(input) + ".exe"));
            EnsureExeExtension(ref output);
            result = NativeCompiler.CompileFile(input, output);
        }

        Console.WriteLine(
            $"已生成原生程序：{result.OutputPath} · {result.ImageSize} 字节");
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
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "yy-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var output = Path.Combine(temporaryDirectory, project.OutputName);
            NativeCompiler.CompileProject(project, output);
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

    private static void EnsureExeExtension(ref string output)
    {
        if (!string.Equals(Path.GetExtension(output), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            output += ".exe";
        }
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
              yy build <工程.yyproj|目录> [-o 输出.exe]
              yy native <源码.yyc|工程.yyproj|目录> [-o 输出.exe]
              yy check <工程.yyproj|目录>
              yy run <工程.yyproj|目录>
              yy version

            示例：
              yy build examples/原生窗体 -o Dist/原生窗体.exe
              yy native examples/Sources/原生入门.yyc -o Dist/原生入门.exe
              yy check examples/原生窗体
              yy run examples/原生窗体

            build/native 使用自研 Win64/PE 后端，不生成 C#、C++、Rust 或 LLVM IR，
            编译结果不依赖 .NET 或 C Runtime。
            """);
    }

    private sealed record NativeOptions(string Input, string? Output)
    {
        public static NativeOptions Parse(string[] args)
        {
            string? input = null;
            string? output = null;
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
                : new NativeOptions(input, output);
        }
    }

}
