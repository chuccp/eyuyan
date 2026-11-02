using System.Diagnostics;
using System.IO;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Yy.Native;
using Yy.Studio.Core.Workspace;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private async void OnCheckProject(object? sender, RoutedEventArgs e) =>
        await CheckProjectAsync();

    private async void OnBuildProject(object? sender, RoutedEventArgs e) =>
        await BuildProjectAsync();

    private async void OnRunProject(object? sender, RoutedEventArgs e) =>
        await RunProjectAsync();

    private void OnStopProgram(object? sender, RoutedEventArgs e)
    {
        if (_debugProbe is not null || _debugCancellation is not null)
        {
            StopDebug();
            return;
        }

        StopProcess(silent: false);
    }

    private async Task CheckProjectAsync()
    {
        if (_workspace.Project is null)
        {
            SetStatus("请先打开工程。");
            return;
        }

        if (!SaveAll())
        {
            return;
        }

        _building = true;
        UpdateActionState();
        SetStatus("正在检查工程…");
        try
        {
            await Task.Run(_workspace.Check);
            var project = _workspace.Project!;
            SetStatus(
                $"检查通过：{project.Name} · {project.SourcePaths.Count} 个源码 · " +
                $"{project.Forms.Count} 个窗体 · {project.ResourceTable.Count} 个资源");
        }
        catch (Exception exception)
        {
            SetStatus("检查失败。");
            AppendConsole("错误：" + exception.Message);
        }
        finally
        {
            _building = false;
            UpdateActionState();
        }
    }

    private async Task<NativeBuildResult?> BuildProjectAsync()
    {
        if (_building)
        {
            return null;
        }

        if (_workspace.Project is null)
        {
            return await BuildCurrentFileAsync();
        }

        if (!SaveAll())
        {
            return null;
        }

        _building = true;
        UpdateActionState();
        SetStatus("正在编译…");
        AppendConsole($"编译：{_workspace.Project.Name}");
        try
        {
            var targetId = TargetBox.SelectedItem as string;
            var result = await Task.Run(() => _workspace.Build(targetId: targetId));
            SetStatus(
                $"编译成功 · {result.Target.Id} · {result.ImageSize} 字节");
            AppendConsole($"已生成：{result.OutputPath}");
            return result;
        }
        catch (Exception exception)
        {
            SetStatus("编译失败。");
            AppendConsole("错误：" + exception.Message);
            return null;
        }
        finally
        {
            _building = false;
            UpdateActionState();
        }
    }

    private async Task<NativeBuildResult?> BuildCurrentFileAsync()
    {
        var document = CurrentDocument;
        if (document is null ||
            !string.Equals(Path.GetExtension(document.Path), ".yyc", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("请先打开工程或 .yyc 源码。");
            return null;
        }

        if (!SaveCurrent())
        {
            return null;
        }

        _building = true;
        UpdateActionState();
        SetStatus("正在编译当前源码…");
        try
        {
            var targetId = TargetBox.SelectedItem as string;
            var result = await Task.Run(() => _workspace.BuildFile(document.Path, targetId: targetId));
            SetStatus($"编译成功 · {result.Target.Id} · {result.ImageSize} 字节");
            AppendConsole($"已生成：{result.OutputPath}");
            return result;
        }
        catch (Exception exception)
        {
            SetStatus("编译失败。");
            AppendConsole("错误：" + exception.Message);
            return null;
        }
        finally
        {
            _building = false;
            UpdateActionState();
        }
    }

    private async Task RunProjectAsync()
    {
        if (_process is not null)
        {
            SetStatus("程序已在运行。");
            return;
        }

        var result = await BuildProjectAsync();
        if (result is null)
        {
            return;
        }

        if (!StudioWorkspace.CanRun(result))
        {
            SetStatus($"当前系统不能运行目标“{result.Target.Id}”。");
            return;
        }

        var projectDirectory = _workspace.Project?.DirectoryPath ??
            Path.GetDirectoryName(result.OutputPath) ??
            Environment.CurrentDirectory;
        var startInfo = new ProcessStartInfo
        {
            FileName = result.OutputPath,
            WorkingDirectory = projectDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };
        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                AppendConsoleFromAnyThread(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is not null)
            {
                AppendConsoleFromAnyThread("错误：" + args.Data);
            }
        };
        process.Exited += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            if (ReferenceEquals(_process, process))
            {
                _process = null;
                SetConsoleVisible(false);
            }

            AppendConsole($"程序已退出，退出码 {process.ExitCode}。");
            UpdateActionState();
            SetStatus($"程序已退出，退出码 {process.ExitCode}");
            process.Dispose();
        });

        _process = process;
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            SelectBottomTab("控制台");
            SetStatus($"运行中 · PID {process.Id}");
            AppendConsole($"运行：{result.OutputPath} · PID {process.Id}");
            UpdateActionState();
        }
        catch (Exception exception)
        {
            _process = null;
            process.Dispose();
            SetStatus("启动失败。");
            AppendConsole("错误：" + exception.Message);
            UpdateActionState();
        }
    }

    private void StopProcess(bool silent)
    {
        var process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
            if (!silent)
            {
                AppendConsole("已请求停止程序。");
                SetStatus("已请求停止程序。");
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _process = null;
            UpdateActionState();
            if (!silent)
            {
                AppendConsole("停止失败：" + exception.Message);
            }
        }
    }
}
