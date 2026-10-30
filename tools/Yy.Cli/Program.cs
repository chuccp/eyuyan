using System.Diagnostics;
using System.IO;
using Yy.Compiler;

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
                "build" => await BuildAsync(args[1..]),
                "check" => Check(args[1..]),
                "run" => await RunProjectAsync(args[1..]),
                "version" or "--version" or "-v" => PrintVersion(),
                _ => UnknownCommand(args[0])
            };
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException
                or ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine("yy: " + exception.Message);
            return 1;
        }
    }

    private static async Task<int> BuildAsync(string[] args)
    {
        var options = CommandOptions.Parse(args);
        var project = LoadProject(options.Input);
        var output = Path.GetFullPath(
            options.Output ?? Path.Combine(Environment.CurrentDirectory, project.OutputName));
        EnsureExeExtension(ref output);
        var compiler = new ProjectCompiler();
        await compiler.CompileAsync(
            project,
            output,
            options.SelfContained,
            options.Rid,
            new Progress<string>(Console.WriteLine));
        Console.WriteLine($"已生成：{output}");
        return 0;
    }

    private static int Check(string[] args)
    {
        var options = CommandOptions.Parse(args, allowBuildOptions: false);
        var project = LoadProject(options.Input);
        new ProjectCompiler().Validate(project);
        Console.WriteLine(
            $"检查通过：{project.Name} · {project.SourcePaths.Count} 个源码 · " +
            $"{project.Forms.Count} 个窗体 · {project.ResourceTable.Count} 个资源");
        return 0;
    }

    private static async Task<int> RunProjectAsync(string[] args)
    {
        var options = CommandOptions.Parse(args);
        var project = LoadProject(options.Input);
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "yy-run-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        try
        {
            var output = Path.Combine(temporaryDirectory, project.OutputName);
            var compiler = new ProjectCompiler();
            await compiler.CompileAsync(
                project,
                output,
                options.SelfContained,
                options.Rid,
                new Progress<string>(Console.WriteLine));
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
              yy build <工程.yyproj|目录> [-o 输出.exe] [--framework-dependent] [--rid RID]
              yy check <工程.yyproj|目录>
              yy run <工程.yyproj|目录> [--framework-dependent] [--rid RID]
              yy version

            示例：
              yy build examples/示例.yyproj -o Dist/易语示例.exe
              yy check examples/示例.yyproj
              yy run examples/MCP资源演示

            默认输出自包含 Windows x64 单文件。--framework-dependent 生成需要
            .NET 10 Desktop Runtime 的精简版本。
            """);
    }

    private sealed record CommandOptions(
        string Input,
        string? Output,
        bool SelfContained,
        string Rid)
    {
        public static CommandOptions Parse(
            string[] args,
            bool allowBuildOptions = true)
        {
            string? input = null;
            string? output = null;
            var selfContained = true;
            var rid = "win-x64";
            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                switch (argument)
                {
                    case "-o" or "--output" when allowBuildOptions:
                        output = RequireValue(args, ref index, argument);
                        break;
                    case "--framework-dependent" when allowBuildOptions:
                        selfContained = false;
                        break;
                    case "--self-contained" when allowBuildOptions:
                        selfContained = true;
                        break;
                    case "--rid" when allowBuildOptions:
                        rid = RequireValue(args, ref index, argument);
                        break;
                    default:
                        if (argument.StartsWith('-'))
                        {
                            throw new ArgumentException("未知选项：" + argument);
                        }

                        if (input is not null)
                        {
                            throw new ArgumentException("只能指定一个工程文件或目录。");
                        }

                        input = argument;
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(input))
            {
                throw new ArgumentException("缺少工程文件或目录。");
            }

            return new CommandOptions(input, output, selfContained, rid);
        }

        private static string RequireValue(string[] args, ref int index, string option)
        {
            if (index + 1 >= args.Length)
            {
                throw new ArgumentException($"{option} 缺少参数。");
            }

            index++;
            return args[index];
        }
    }
}
