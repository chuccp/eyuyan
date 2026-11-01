using System.Diagnostics;
using Yy.Studio.Core.Workspace;

namespace Yy.Studio.Avalonia;

/// <summary>无界面冒烟入口：验证工程加载、源码树和原生编译，不启动 Avalonia。</summary>
internal static class SmokeRunner
{
    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--smoke");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.Error.WriteLine("用法：YyStudio --smoke <工程.yyproj|目录> [--run]");
            return 2;
        }

        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            "yy-studio-smoke-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = new StudioWorkspace();
            workspace.Load(args[index + 1]);
            Console.WriteLine($"工程：{workspace.Project!.Name} · {workspace.Project.PackageName}");
            PrintNodes(workspace.BuildTree(), 0);

            Directory.CreateDirectory(temporaryDirectory);
            var output = Path.Combine(
                temporaryDirectory,
                Path.GetFileNameWithoutExtension(workspace.Project.OutputName));
            var result = workspace.Build(output, ReadOption(args, "--target"));
            Console.WriteLine(
                $"编译：{result.OutputPath} · {result.ImageSize} 字节 · {result.Target.Id}");

            if (args.Contains("--run", StringComparer.Ordinal))
            {
                return RunProcess(result.OutputPath, workspace.Project.DirectoryPath);
            }

            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("冒烟失败：" + exception.Message);
            return 1;
        }
        finally
        {
            try
            {
                Directory.Delete(temporaryDirectory, recursive: true);
            }
            catch (IOException)
            {
                // 刚退出的原生进程可能短暂占用输出文件。
            }
        }
    }

    private static string? ReadOption(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int RunProcess(string output, string workingDirectory)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = output,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        }) ?? throw new InvalidOperationException("无法启动编译后的程序。");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        if (!process.WaitForExit(TimeSpan.FromSeconds(10)))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("冒烟程序在 10 秒内没有退出。");
        }

        Console.Write(stdout);
        Console.Error.Write(stderr);
        Console.WriteLine($"退出码：{process.ExitCode}");
        return process.ExitCode;
    }

    private static void PrintNodes(IEnumerable<ProjectNode> nodes, int depth)
    {
        foreach (var node in nodes)
        {
            Console.WriteLine(new string(' ', depth * 2) + node.Name);
            PrintNodes(node.Children, depth + 1);
        }
    }
}
