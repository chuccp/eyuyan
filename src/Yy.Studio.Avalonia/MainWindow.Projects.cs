using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Yy.Compiler;
using Yy.Studio.Avalonia.Features.Projects;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private async void OnNewProject(object? sender, RoutedEventArgs e) =>
        await CreateProjectAsync(YyProjectKind.Window);

    /// <summary>导入老程序:选 .e → 弹窗填新目录(仿新建) → 用 yy convert 转换。</summary>
    private async void OnImportLegacy(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择老易语言程序(.e)",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("易语言程序 (*.e)") { Patterns = ["*.e"] }
            ]
        });
        var sourcePath = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return;
        }

        var project = await NewProjectDialog.ShowDialog(this, YyProjectKind.Console);
        if (project is null)
        {
            return;
        }

        try
        {
            RunYyConvert(sourcePath, project.DirectoryPath, project.Name, project.PackageName);
            OpenProjectPath(project.DirectoryPath);
            SetStatus($"已导入老程序:{project.Name} · 来源 {Path.GetFileName(sourcePath)}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException
                or FileNotFoundException)
        {
            SetStatus("导入失败。");
            AppendConsole("错误：" + exception.Message);
        }
    }

    /// <summary>调用 yy 命令行完成转换(转换逻辑统一由 yy 提供)。</summary>
    private void RunYyConvert(string sourcePath, string targetDirectory, string name, string package)
    {
        var arguments =
            $"convert \"{sourcePath}\" --out \"{targetDirectory}\" " +
            $"--name \"{name}\" --package \"{package}\"";
        var startInfo = new ProcessStartInfo
        {
            FileName = FindYyExecutable(),
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        AppendConsole("> yy " + arguments);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("无法启动 yy 转换进程。");
        var output = process.StandardOutput.ReadToEnd();
        var errors = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (!string.IsNullOrWhiteSpace(output))
        {
            AppendConsole(output.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(errors))
        {
            AppendConsole(errors.TrimEnd());
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"yy convert 失败(退出码 {process.ExitCode})。");
        }
    }

    private static string FindYyExecutable()
    {
        var names = OperatingSystem.IsWindows() ? "yy.exe" : "yy";
        var local = Path.Combine(AppContext.BaseDirectory, names);
        if (File.Exists(local))
        {
            return local;
        }

        var pathVariable = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in pathVariable.Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory.Trim(), names);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException("找不到 yy 命令行工具。请将 yy.exe 放到 Studio 同目录或 PATH 中。");
    }

    private async Task CreateProjectAsync(YyProjectKind kind)
    {
        var project = await NewProjectDialog.ShowDialog(this, kind);
        if (project is not null)
        {
            OpenProjectPath(project.DirectoryPath);
            SetStatus($"已创建项目：{project.Name} · 包 {project.PackageName}");
        }
    }

    private async void OnNewProgram(object? sender, RoutedEventArgs e)
    {
        var result = await NewProgramDialog.ShowAndCreateAsync(this);
        if (result is null)
        {
            return;
        }

        switch (result.Kind)
        {
            case NewProgramDialog.ProgramKind.OpenFile:
                await OpenFileAsync();
                break;
            case NewProgramDialog.ProgramKind.Sample when result.SamplePath is not null:
                OpenProjectPath(result.SamplePath);
                break;
            case NewProgramDialog.ProgramKind.Project when result.Project is not null:
                OpenProjectPath(result.Project.DirectoryPath);
                break;
            case NewProgramDialog.ProgramKind.Window when result.Project is not null:
            case NewProgramDialog.ProgramKind.Console when result.Project is not null:
            case NewProgramDialog.ProgramKind.Module when result.Project is not null:
                OpenProjectPath(result.Project.DirectoryPath);
                break;
        }
    }

    private void RefreshWindowMenu()
    {
        if (WindowMenu is null)
        {
            return;
        }

        WindowMenu.Items.Clear();

        // 布局预设与工具窗口(对齐主分支窗口菜单结构)
        var presets = new MenuItem { Header = "布局预设" };
        foreach (var (header, tag) in new[]
                 {
                     ("默认布局", "默认"),
                     ("专注代码", "专注代码"),
                     ("窗体设计", "窗体设计")
                 })
        {
            var preset = new MenuItem { Header = header, Tag = tag };
            preset.Click += OnLayoutPreset;
            presets.Items.Add(preset);
        }

        WindowMenu.Items.Add(presets);

        var tools = new MenuItem { Header = "工具窗口" };
        foreach (var (header, handler) in new (string, EventHandler<global::Avalonia.Interactivity.RoutedEventArgs>)[]
                 {
                     ("项目", OnToggleProjectWindow),
                     ("程序与属性", OnToggleWorkWindow),
                     ("控制台", OnToggleConsoleWindow)
                 })
        {
            var tool = new MenuItem { Header = header };
            tool.Click += handler;
            tools.Items.Add(tool);
        }

        WindowMenu.Items.Add(tools);
        WindowMenu.Items.Add(new Separator());

        foreach (var document in _tabs.Documents)
        {
            var item = new MenuItem
            {
                Header = document.FileName + (document.IsDirty ? " ●" : string.Empty),
                Tag = document,
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = ReferenceEquals(CurrentDocument, document)
            };
            item.Click += (_, _) => _tabs.Select(document);
            WindowMenu.Items.Add(item);
        }

        if (_tabs.Documents.Count == 0)
        {
            WindowMenu.Items.Add(new MenuItem
            {
                Header = "（无打开的文档）",
                IsEnabled = false
            });
        }
    }

    private async void OnProjectProperties(object? sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null)
        {
            SetStatus("请先打开工程。");
            return;
        }

        var updated = await ProjectPropertiesWindow.ShowDialog(this, _workspace.Project);
        if (updated is not null)
        {
            OpenProjectPath(updated.DirectoryPath);
            SetStatus($"项目属性已保存：{updated.Name}");
        }
    }

    private async void OnModuleManager(object? sender, RoutedEventArgs e)
    {
        await ModuleManagerDialog.ShowDialog(
            this,
            _workspace.Project?.DirectoryPath,
            _workspace.Project);
        if (_workspace.Project is not null)
        {
            _workspace.Reload();
            RefreshProjectTree();
        }
    }
}
