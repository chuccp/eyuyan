using System.Diagnostics;
using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaEdit;
using Yy.Studio.Avalonia.Editor;
using Yy.Studio.Avalonia.Features.Forms;
using Yy.Language.Structured;
using Yy.Compiler.Forms;
using Yy.Studio.Core.Workspace;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private async void OnOpenProject(object? sender, RoutedEventArgs e) =>
        await OpenProjectAsync();

    private async void OnOpenFile(object? sender, RoutedEventArgs e) =>
        await OpenFileAsync();

    private async void OnOpenFolder(object? sender, RoutedEventArgs e) =>
        await OpenFolderAsync();

    private async Task OpenFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开易语文件",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("易语源码")
                {
                    Patterns = ["*.yyc", "*.yyx", "*.yyproj"]
                },
                FilePickerFileTypes.All
            ]
        });
        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("无法读取所选文件的本地路径。");
            return;
        }

        if (string.Equals(Path.GetExtension(path), ".yyproj", StringComparison.OrdinalIgnoreCase))
        {
            OpenProjectPath(path);
        }
        else
        {
            OpenDocument(path);
        }
    }

    private async Task OpenFolderAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "打开易语工程文件夹",
            AllowMultiple = false
        });
        if (folders.Count == 0)
        {
            return;
        }

        var path = folders[0].TryGetLocalPath();
        if (!string.IsNullOrWhiteSpace(path))
        {
            OpenProjectPath(path);
        }
    }

    private async Task OpenProjectAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开易语工程",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("易语工程")
                {
                    Patterns = ["*.yyproj"],
                    MimeTypes = ["application/json"]
                }
            ]
        });
        if (files.Count == 0)
        {
            return;
        }

        var path = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("无法读取所选工程的本地路径。");
            return;
        }

        OpenProjectPath(path);
    }

    private void OpenProjectPath(string path)
    {
        if (!SaveAll())
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath) &&
            Path.GetExtension(fullPath).ToLowerInvariant() is ".yyc" or ".yyx")
        {
            var projectDirectory = FindProjectDirectory(fullPath);
            if (projectDirectory is not null)
            {
                try
                {
                    _workspace.Load(projectDirectory);
                    RefreshProjectTree();
                    TargetBox.SelectedItem = Yy.Compiler.YyBuildTargets.All.Contains(
                        _workspace.Project!.TargetPlatform)
                        ? _workspace.Project.TargetPlatform
                        : Yy.Compiler.YyBuildTargets.Auto;
                    ProjectStatusText.Text =
                        $"{_workspace.Project.Name} · {_workspace.Project.PackageName}";
                }
                catch (Exception exception) when (
                    exception is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    AppendConsole("无法加载所属工程：" + exception.Message);
                }
            }

            OpenDocument(fullPath);
            return;
        }

        try
        {
            _workspace.Load(path);
            RefreshProjectTree();
            var project = _workspace.Project!;
            Title = $"易语 Studio · {project.Name}";
            TitleDocumentText.Text = project.Name;
            ProjectStatusText.Text = $"{project.Name} · {project.PackageName}";
            TargetBox.SelectedItem = Yy.Compiler.YyBuildTargets.All.Contains(project.TargetPlatform)
                ? project.TargetPlatform
                : Yy.Compiler.YyBuildTargets.Auto;
            SetStatus($"已打开 {project.Name}（{project.TargetPlatform}）");
            AppendConsole($"工程：{project.ProjectPath}");
            _recentFiles.AddRecent(project.DirectoryPath);
            if (File.Exists(project.EntryPath))
            {
                OpenDocument(project.EntryPath);
            }
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException
                or ArgumentException)
        {
            SetStatus("打开工程失败。");
            AppendConsole("错误：" + exception.Message);
        }
        finally
        {
            UpdateActionState();
        }
    }

    private void OnNewForm(object? sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null)
        {
            SetStatus("请先打开工程。");
            return;
        }

        try
        {
            var created = _workspace.Project.CreateForm("新窗体");
            _workspace.Reload();
            RefreshProjectTree();
            OpenDocument(created.FormPath);
            SetStatus($"已创建窗体 {Path.GetFileName(created.FormPath)}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            SetStatus("创建窗体失败。");
            AppendConsole("错误：" + exception.Message);
        }
    }

    private void OnNewSource(object? sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null)
        {
            SetStatus("请先打开工程。");
            return;
        }

        try
        {
            var path = _workspace.Project.CreateSourceFile();
            _workspace.Reload();
            RefreshProjectTree();
            OpenDocument(path);
            SetStatus($"已创建源码 {Path.GetFileName(path)}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("创建源码失败。");
            AppendConsole("错误：" + exception.Message);
        }
    }

    private static string? FindProjectDirectory(string path)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        while (!string.IsNullOrWhiteSpace(directory))
        {
            if (Directory.EnumerateFiles(directory, "*.yyproj", SearchOption.TopDirectoryOnly).Any())
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return null;
    }

    private void OnReloadProject(object? sender, RoutedEventArgs e)
    {
        if (_workspace.Project is null)
        {
            SetStatus("尚未打开工程。");
            return;
        }

        if (!SaveAll())
        {
            return;
        }

        try
        {
            _workspace.Reload();
            RefreshProjectTree();
            SetStatus("工程已重新加载。");
        }
        catch (Exception exception)
        {
            SetStatus("重新加载失败。");
            AppendConsole("错误：" + exception.Message);
        }
    }

    private void RefreshProjectTree()
    {
        ProjectTree.ItemsSource = _workspace.BuildTree();
        RefreshWorkPanel();
    }

    private void OnProjectTreeDoubleTapped(object? sender, TappedEventArgs e) =>
        OpenSelectedNode();

    private void OnOpenSelectedNode(object? sender, RoutedEventArgs e) =>
        OpenSelectedNode();

    private void OpenSelectedNode()
    {
        if (ProjectTree.SelectedItem is ProjectNode { IsDirectory: false, FullPath: not null } node)
        {
            OpenDocument(node.FullPath);
        }
    }

    private void OnRevealSelectedNode(object? sender, RoutedEventArgs e)
    {
        if (ProjectTree.SelectedItem is not ProjectNode { FullPath: not null } node ||
            !File.Exists(node.FullPath) && !Directory.Exists(node.FullPath))
        {
            return;
        }

        try
        {
            var path = node.FullPath;
            if (OperatingSystem.IsMacOS())
            {
                Start(path, File.Exists(path) ? "-R" : string.Empty);
            }
            else if (OperatingSystem.IsWindows())
            {
                Start("explorer.exe", File.Exists(path) ? $"/select,\"{path}\"" : $"\"{path}\"");
            }
            else
            {
                Start("xdg-open", File.Exists(path) ? Path.GetDirectoryName(path)! : path);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SetStatus("无法打开文件管理器：" + exception.Message);
        }
    }

    private static void Start(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    private void OnDocumentTabsSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (CurrentDocument is { } document && CaretText is not null)
        {
            UpdateCaret(document);
            Title = $"{document.FileName} · 易语 Studio";
            if (TitleDocumentText is not null)
            {
                TitleDocumentText.Text = _workspace.Project is { } owner
                    ? $"{owner.Name} · {document.FileName}"
                    : document.FileName;
            }
        }
        else if (DocumentTabs is not null)
        {
            Title = _workspace.Project is { } project
                ? $"{project.Name} · 易语 Studio"
                : "易语 Studio";
            if (TitleDocumentText is not null)
            {
                TitleDocumentText.Text = _workspace.Project?.Name ?? "易语 Studio";
            }
        }

        if (DocumentTabs is not null)
        {
            if (WorkPropertyHost is not null)
            {
                WorkPropertyHost.Content = CurrentDocument?.FormPropertyPanel
                    ?? _defaultPropertyContent;
                if (CurrentDocument?.FormPropertyPanel is not null &&
                    WorkTabs is not null)
                {
                    WorkTabs.SelectedIndex = 1;
                }
            }
            UpdateActionState();
            RefreshWorkPanel();
            RefreshWindowMenu();
        }
    }

    private void UpdateCaret(EditorDocument document) =>
        CaretText.Text = $"行 {document.Editor.TextArea.Caret.Line}，列 {document.Editor.TextArea.Caret.Column}";

    private void OpenDocument(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var existing = _documents.FirstOrDefault(document =>
            string.Equals(document.Path, fullPath, StringComparison.Ordinal));
        if (existing is not null)
        {
            DocumentTabs.SelectedItem = existing.Tab;
            return;
        }

        if (!IsTextDocument(fullPath))
        {
            SetStatus($"暂不支持在 IDE 中打开：{Path.GetFileName(fullPath)}");
            return;
        }

        try
        {
            var text = File.ReadAllText(fullPath, new UTF8Encoding(false, true));
            var readOnly = IsGeneratedFile(fullPath);
            if (!readOnly &&
                string.Equals(Path.GetExtension(fullPath), ".yyx", StringComparison.OrdinalIgnoreCase))
            {
                OpenFormDocument(fullPath, text);
                return;
            }

            if (!readOnly &&
                string.Equals(Path.GetExtension(fullPath), ".yyc", StringComparison.OrdinalIgnoreCase))
            {
                OpenCodeDocument(fullPath, text);
                return;
            }

            var editor = YyCodeEditor.Create(text, fullPath, readOnly);
            ConfigureEditor(editor);
            var headerText = new TextBlock
            {
                Text = Path.GetFileName(fullPath),
                VerticalAlignment = VerticalAlignment.Center
            };
            var closeButton = new Button
            {
                Content = "×",
                Padding = new Thickness(4, 0),
                Background = global::Avalonia.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 15
            };
            var header = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6
            };
            header.Children.Add(headerText);
            header.Children.Add(closeButton);
            var tab = new TabItem
            {
                Header = header,
                Content = editor,
                Tag = fullPath
            };
            var document = new EditorDocument
            {
                Path = fullPath,
                FileName = Path.GetFileName(fullPath),
                Tab = tab,
                HeaderText = headerText,
                Editor = editor,
                IsReadOnly = readOnly,
                SavedText = text
            };
            closeButton.Click += (_, _) => CloseDocument(document);
            if (!readOnly)
            {
                editor.TextChanged += (_, _) =>
                {
                    var dirty = !string.Equals(
                        document.Editor.Text ?? string.Empty,
                        document.SavedText,
                        StringComparison.Ordinal);
                    if (document.IsDirty != dirty)
                    {
                        document.IsDirty = dirty;
                        UpdateDocumentHeader(document);
                    }
                };
            }

            editor.TextArea.Caret.PositionChanged += (_, _) =>
            {
                if (ReferenceEquals(CurrentDocument, document))
                {
                    UpdateCaret(document);
                }
            };
            HideStartPage();
            _documents.Add(document);
            DocumentTabs.Items.Add(tab);
            DocumentTabs.SelectedItem = tab;
            _recentFiles.AddRecent(fullPath);
            UpdateActionState();
            if (readOnly)
            {
                SetStatus($"{document.FileName} 由生成器维护，只读。");
            }
            else
            {
                SetStatus($"已打开 {document.FileName}");
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            SetStatus("读取文件失败。");
            AppendConsole("错误：" + exception.Message);
        }
    }

    private EditorDocument? CurrentDocument =>
        DocumentTabs?.SelectedItem is TabItem tab
            ? _documents.FirstOrDefault(document => ReferenceEquals(document.Tab, tab))
            : null;

    private void OnSaveCurrent(object? sender, RoutedEventArgs e) =>
        SaveCurrent();

    private void OnSaveAll(object? sender, RoutedEventArgs e) =>
        SaveAll();

    private async void OnSaveAs(object? sender, RoutedEventArgs e) =>
        await SaveAsAsync();

    private async Task SaveAsAsync()
    {
        var document = CurrentDocument;
        if (document is null)
        {
            SetStatus("当前没有打开的文档。");
            return;
        }

        if (document.IsReadOnly)
        {
            SetStatus($"{document.FileName} 由生成器维护，不能另存为。");
            return;
        }

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "另存为",
            SuggestedFileName = document.FileName,
            DefaultExtension = Path.GetExtension(document.Path).TrimStart('.'),
            ShowOverwritePrompt = true
        });
        if (file is null)
        {
            return;
        }

        var path = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(path))
        {
            SetStatus("无法读取保存路径。");
            return;
        }

        try
        {
            var oldPath = document.Path;
            var oldCodePath = document.FormView is null
                ? null
                : YyFormCompanionFiles.UserCodePath(oldPath);
            document.Path = path;
            document.FileName = Path.GetFileName(path);
            document.Tab.Tag = path;
            document.HeaderText.Text = document.FileName;
            if (document.FormView is not null)
            {
                var newCodePath = YyFormCompanionFiles.UserCodePath(path);
                if (oldCodePath is not null &&
                    File.Exists(oldCodePath) &&
                    !string.Equals(oldCodePath, newCodePath, StringComparison.Ordinal))
                {
                    File.Copy(oldCodePath, newCodePath, overwrite: true);
                }

                if (!SaveFormDocument(document))
                {
                    return;
                }
            }
            else if (document.StructuredView is { } structured)
            {
                structured.FlushPendingEdits();
                var model = YyCodeDocumentConverter.FromSource(
                    document.Editor.Text ?? string.Empty,
                    path);
                File.WriteAllText(path, YyCodeSerializer.Serialize(model), new UTF8Encoding(false));
                document.SavedText = YyCodeDocumentConverter.ToSource(model);
                document.IsDirty = false;
            }
            else
            {
                var text = document.Editor.Text ?? string.Empty;
                File.WriteAllText(path, text, new UTF8Encoding(false));
                document.SavedText = text;
                document.IsDirty = false;
            }

            SetStatus($"已另存为 {document.FileName}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("另存为失败。");
            AppendConsole("错误：" + exception.Message);
        }
    }

    private bool SaveCurrent()
    {
        var document = CurrentDocument;
        if (document is null)
        {
            SetStatus("当前没有打开的文档。");
            return false;
        }

        return SaveDocument(document);
    }

    private bool SaveAll()
    {
        foreach (var document in _documents.Where(item => item.IsDirty))
        {
            if (!SaveDocument(document))
            {
                return false;
            }
        }

        return true;
    }

    private bool SaveDocument(EditorDocument document)
    {
        if (document.IsReadOnly)
        {
            SetStatus($"{document.FileName} 由生成器维护，不能直接保存。");
            return false;
        }

        if (document.FormView is not null)
        {
            return SaveFormDocument(document);
        }

        if (document.StructuredView is not null)
        {
            return SaveCodeDocument(document);
        }

        try
        {
            var text = document.Editor.Text ?? string.Empty;
            File.WriteAllText(document.Path, text, new UTF8Encoding(false));
            document.SavedText = text;
            document.IsDirty = false;
            UpdateDocumentHeader(document);
            SetStatus($"已保存 {document.FileName}");
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("保存失败。");
            AppendConsole("错误：" + exception.Message);
            return false;
        }
    }

    private static void UpdateDocumentHeader(EditorDocument document) =>
        document.HeaderText.Text = document.FileName + (document.IsDirty ? " *" : string.Empty);

    private void OnCloseCurrent(object? sender, RoutedEventArgs e) =>
        CloseCurrent();

    private void CloseCurrent()
    {
        if (CurrentDocument is { } document)
        {
            CloseDocument(document);
        }
    }

    private void CloseDocument(EditorDocument document)
    {
        if (document.IsDirty)
        {
            SetStatus($"{document.FileName} 有未保存修改，请先保存。");
            return;
        }

        _documents.Remove(document);
        DocumentTabs.Items.Remove(document.Tab);
        if (_documents.Count == 0)
        {
            ShowStartPage();
        }

        RefreshWindowMenu();
        SetStatus($"已关闭 {document.FileName}");
        UpdateActionState();
    }

    private static bool IsGeneratedFile(string path) =>
        path.EndsWith(".Designer.yyc", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetFileName(path), "ids.xml", StringComparison.OrdinalIgnoreCase);

    private static bool IsTextDocument(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is
            ".yyc" or ".yyx" or ".yyproj" or ".xml" or ".json" or
            ".md" or ".txt" or ".ps1" or ".sh" or ".props" or ".csproj";
}
