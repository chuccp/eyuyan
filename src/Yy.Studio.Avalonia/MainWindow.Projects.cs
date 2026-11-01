using Avalonia.Controls;
using Avalonia.Interactivity;
using Yy.Compiler;
using Yy.Studio.Avalonia.Features.Projects;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private async void OnNewProject(object? sender, RoutedEventArgs e) =>
        await CreateProjectAsync(YyProjectKind.Window);

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
        foreach (var document in _documents)
        {
            var item = new MenuItem
            {
                Header = document.FileName + (document.IsDirty ? " *" : string.Empty),
                Tag = document
            };
            item.Click += (_, _) => DocumentTabs.SelectedItem = document.Tab;
            WindowMenu.Items.Add(item);
        }

        if (_documents.Count == 0)
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
