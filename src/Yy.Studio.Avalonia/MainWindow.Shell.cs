using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Yy.Studio.Avalonia.Features.Data;
using Yy.Studio.Avalonia.Features.Documentation;
using Yy.Studio.Avalonia.Features.Settings;
using Yy.Studio.Core.Configuration;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private async void OnSystemConfig(object? sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(Configuration);
        window.Applied += configuration =>
        {
            Configuration = configuration;
            RefreshShortcuts();
            ApplyEditorConfiguration();
            AppendConsole("设置已保存；MCP 监听参数变更需要重启 Studio。");
        };
        await window.ShowDialog(this);
        if (window.SavedConfiguration is not null)
        {
            Configuration = window.SavedConfiguration;
            RefreshShortcuts();
            ApplyEditorConfiguration();
        }
    }

    private void OnEditUndo(object? sender, RoutedEventArgs e) =>
        CurrentDocument?.Editor.Undo();

    private void OnEditRedo(object? sender, RoutedEventArgs e) =>
        CurrentDocument?.Editor.Redo();

    private void OnEditCut(object? sender, RoutedEventArgs e) =>
        CurrentDocument?.Editor.Cut();

    private void OnEditCopy(object? sender, RoutedEventArgs e) =>
        CurrentDocument?.Editor.Copy();

    private void OnEditPaste(object? sender, RoutedEventArgs e) =>
        CurrentDocument?.Editor.Paste();

    private void OnEditSelectAll(object? sender, RoutedEventArgs e) =>
        CurrentDocument?.Editor.SelectAll();

    private void OnFormatCode(object? sender, RoutedEventArgs e) =>
        FormatCurrentCode();

    private void OnOpenStudioConfig(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = Configuration.ConfigurationPath ?? StudioConfiguration.UserFilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path))
            {
                Configuration.Save();
            }

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("无法打开配置文件：" + exception.Message);
        }
    }

    private void OnOpenDataTable(object? sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null)
        {
            SetStatus("请先打开工程。");
            return;
        }

        var kind = (sender as MenuItem)?.Tag as string;
        if (string.IsNullOrWhiteSpace(kind))
        {
            return;
        }

        DataTableView.Show(
            this,
            _workspace.Project,
            kind,
            () =>
            {
                _workspace.Reload();
                RefreshProjectTree();
            });
    }

    private async void OnDatabaseDesigner(object? sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null)
        {
            SetStatus("请先打开工程。");
            return;
        }

        await DatabaseDesignerDialog.ShowDialog(this, _workspace.Project);
        _workspace.Reload();
        RefreshProjectTree();
    }

    private async void OnDataBind(object? sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null)
        {
            SetStatus("请先打开工程。");
            return;
        }

        if (CurrentDocument is not { FormView: { } view } document)
        {
            SetStatus("请先打开窗体设计器。");
            return;
        }

        if (document.FormSourceMode && !SyncFormSource(document))
        {
            return;
        }

        if (view.SelectedControl is not { } control)
        {
            SetStatus("请先在窗体设计器中选择控件。");
            return;
        }

        if (!await DataBindDialog.ShowDialog(this, control, _workspace.Project.DatabaseTables))
        {
            return;
        }

        view.LoadForm(
            view.BuildForm(),
            _workspace.Project.DirectoryPath,
            document.Path);
        document.IsDirty = true;
        UpdateDocumentHeader(document);

        SetStatus("数据绑定已更新。");
    }

    private void OnDocumentation(object? sender, RoutedEventArgs e)
    {
        var window = new DocumentationWindow();
        window.Show(this);
    }

    private void ApplyEditorConfiguration()
    {
        foreach (var document in _tabs.Documents)
        {
            ConfigureEditor(document.Editor);
        }
    }

    private void ConfigureEditor(AvaloniaEdit.TextEditor editor)
    {
        editor.FontSize = Configuration.Editor.FontSize;
        editor.Options.IndentationSize = Configuration.Editor.TabWidth;
    }

    /// <summary>按标题切换底部工具窗页签(提示/控制台/调用表/监视表/变量表/搜寻1/搜寻2/剪辑历史)。</summary>
    private void SelectBottomTab(string header)
    {
        foreach (var item in BottomTabs.Items)
        {
            if (item is TabItem { Header: string text } tab &&
                string.Equals(text, header, StringComparison.Ordinal))
            {
                tab.IsVisible = true;
                BottomTabs.SelectedItem = tab;
                return;
            }
        }
    }

    private string CurrentTargetPlatform =>
        TargetBox.SelectedItem as string ?? Yy.Compiler.YyBuildTargets.Auto;

    private static string PlatformDisplayName(string target) => target switch
    {
        Yy.Compiler.YyBuildTargets.WindowsX64 => "Windows",
        Yy.Compiler.YyBuildTargets.MacOSArm64 => "macOS (ARM64)",
        Yy.Compiler.YyBuildTargets.MacOSX64 => "macOS (x64)",
        _ => "auto"
    };

    private void SyncTargetPlatformMenu()
    {
        var current = CurrentTargetPlatform;
        foreach (var item in TargetPlatformMenu.Items)
        {
            if (item is MenuItem { Tag: string tag } menu)
            {
                menu.IsChecked = string.Equals(tag, current, StringComparison.Ordinal);
            }
        }

        if (PlatformStatusText is not null)
        {
            PlatformStatusText.Text = "UTF-8 · " + PlatformDisplayName(current) + " 原生";
        }

        UpdateDocumentIdentity();
    }

    private void OnTargetPlatformClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not string tag)
        {
            return;
        }

        TargetBox.SelectedItem = Yy.Compiler.YyBuildTargets.All.Contains(tag)
            ? tag
            : Yy.Compiler.YyBuildTargets.Auto;
        SyncTargetPlatformMenu();
    }

    /// <summary>按主分支口径组合窗口标题:目的平台与当前文档身份。</summary>
    private void UpdateDocumentIdentity()
    {
        if (TitleDocumentText is null)
        {
            return;
        }

        var platform = PlatformDisplayName(CurrentTargetPlatform);
        var dirty = CurrentDocument?.IsDirty == true ? "● " : string.Empty;
        string title;
        if (CurrentDocument is null)
        {
            title = $"{dirty}易语 Studio - 目的平台: {platform}";
        }
        else if (ReferenceEquals(CurrentDocument.Tab, _startPageTab))
        {
            title = $"易语 Studio - 目的平台: {platform} - [起始页]";
        }
        else
        {
            var document = CurrentDocument;
            var formName = document.FormView?.CurrentForm?.Name ??
                           TryGetAssociatedFormName(document.Path);
            title = formName is null
                ? $"{dirty}易语 Studio - 目的平台: {platform} - [程序集: {document.FileName}]"
                : document.FormView is not null
                    ? $"{dirty}易语 Studio - 目的平台: {platform} - [窗口: {formName}]"
                    : $"{dirty}易语 Studio - 目的平台: {platform} - [程序集: {document.FileName} / {formName}]";
        }

        Title = title;
        TitleDocumentText.Text = title;
    }

    private static string? TryGetAssociatedFormName(string codePath)
    {
        try
        {
            var formPath = Yy.Compiler.Forms.YyFormCompanionFiles.GetAssociatedFormPath(codePath);
            if (!File.Exists(formPath))
            {
                return null;
            }

            var form = Yy.Compiler.Forms.YyFormSerializer.Deserialize(
                File.ReadAllText(formPath));
            return string.IsNullOrWhiteSpace(form.Name) ? form.Title : form.Name;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException
                or InvalidDataException or Yy.Compiler.Forms.YyFormFormatException)
        {
            return null;
        }
    }
}
