using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using Yy.Studio.Properties;
using Yy.Language;
using Yy.Language.Syntax;
using Yy.Studio.Forms;
using Yy.Language.Structured;
using Yy.Studio.Structured;


namespace Yy.Studio;

/// <summary>
/// 工作区、程序数据树、文档激活和界面/代码视图切换。
/// </summary>
public partial class MainWindow : Window
{
    private void OpenWorkspace(string path)
    {
        _workspacePath = path;
        try
        {
            _project = YyProject.LoadForDirectory(path);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            _project = null;
            MessageBox.Show(
                $"项目清单读取失败：\n{exception.Message}",
                "项目加载失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        PopulateProjectTree();
        _settings.AddRecent(path);
        SetStatus(
            _project is null
                ? $"已打开目录：{Path.GetFileName(path)}"
                : $"已打开项目：{_project.Name}",
            ReadyBrush);

        var startupForm = _project?.Forms.FirstOrDefault(form =>
            string.Equals(form.Model.Name, _project.StartupForm, StringComparison.Ordinal));
        var openPath = startupForm?.FormPath
            ?? _project?.EntryPath
            ?? Directory.EnumerateFiles(path, "*.yy*", SearchOption.AllDirectories)
            .Where(file => string.Equals(Path.GetExtension(file), ".yyc", StringComparison.OrdinalIgnoreCase))
            .Where(file => !IgnoredDirectories.Any(part =>
                file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Contains(part, StringComparer.OrdinalIgnoreCase)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (openPath is not null)
        {
            OpenDocument(openPath);
        }
    }

    private sealed record TableLink(string Kind);

    private DataTableView? _dataTableView;

    private void PopulateProjectTree()
    {
        PopulateDirectoryTree();
        ProjectTree.Items.Clear();
        if (string.IsNullOrWhiteSpace(_workspacePath) || !Directory.Exists(_workspacePath))
        {
            return;
        }

        // 官方“程序数据”树(对齐 img041):全局变量/自定义数据类型/Dll命令/窗口/程序集/常量表/资源表
        var root = new TreeViewItem
        {
            Header = MakeTreeHeader("程序数据"),
            IsExpanded = true
        };
        root.Items.Add(new TreeViewItem { Header = MakeTreeHeader("全局变量"), Tag = new TableLink("全局变量") });
        root.Items.Add(new TreeViewItem { Header = MakeTreeHeader("自定义数据类型"), Tag = new TableLink("自定义数据类型") });
        root.Items.Add(new TreeViewItem { Header = MakeTreeHeader("Dll命令"), Tag = new TableLink("Dll命令") });

        var windowsItem = new TreeViewItem
        {
            Header = MakeTreeHeader("窗口"),
            IsExpanded = true
        };
        var assembliesItem = new TreeViewItem
        {
            Header = MakeTreeHeader("程序集"),
            IsExpanded = true
        };

        var formFiles = new List<string>();
        var linkedCodeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_project is not null)
        {
            foreach (var form in _project.Forms)
            {
                formFiles.Add(form.FormPath);
                var formNode = new TreeViewItem
                {
                    Header = MakeTreeHeader(form.Model.Name),
                    Tag = form.FormPath,
                    IsExpanded = true
                };
                formNode.Items.Add(new TreeViewItem
                {
                    Header = MakeTreeHeader("界面  " + Path.GetFileName(form.FormPath)),
                    Tag = form.FormPath
                });
                formNode.Items.Add(new TreeViewItem
                {
                    Header = MakeTreeHeader("关系  " + Path.GetFileName(form.BindingPath)),
                    Tag = form.BindingPath
                });
                linkedCodeFiles.Add(form.BindingPath);
                if (form.CodePath is not null)
                {
                    linkedCodeFiles.Add(form.CodePath);
                    formNode.Items.Add(new TreeViewItem
                    {
                        Header = MakeTreeHeader("代码  " + Path.GetFileName(form.CodePath)),
                        Tag = form.CodePath
                    });
                }

                windowsItem.Items.Add(formNode);
            }
        }
        else
        {
            foreach (var formFile in Directory.EnumerateFiles(_workspacePath, "*.yyx", SearchOption.AllDirectories)
                         .Where(file => !IsIgnoredPath(file))
                         .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
            {
                formFiles.Add(formFile);
                var formName = Path.GetFileNameWithoutExtension(formFile);
                try
                {
                    formName = YyFormSerializer.Deserialize(File.ReadAllText(formFile)).Name;
                }
                catch (Exception exception) when (exception is IOException or YyFormFormatException)
                {
                    // 名称回退为文件名,仍允许打开修复。
                }

                var formNode = new TreeViewItem
                {
                    Header = MakeTreeHeader(formName),
                    Tag = formFile,
                    IsExpanded = true
                };
                formNode.Items.Add(new TreeViewItem
                {
                    Header = MakeTreeHeader("界面  " + Path.GetFileName(formFile)),
                    Tag = formFile
                });
                var bindingPath = YyFormCompanionFiles.BindingPath(formFile);
                if (File.Exists(bindingPath))
                {
                    linkedCodeFiles.Add(bindingPath);
                    formNode.Items.Add(new TreeViewItem
                    {
                        Header = MakeTreeHeader("关系  " + Path.GetFileName(bindingPath)),
                        Tag = bindingPath
                    });
                }

                var userCodePath = YyFormCompanionFiles.UserCodePath(formFile);
                if (File.Exists(userCodePath))
                {
                    linkedCodeFiles.Add(userCodePath);
                    formNode.Items.Add(new TreeViewItem
                    {
                        Header = MakeTreeHeader("代码  " + Path.GetFileName(userCodePath)),
                        Tag = userCodePath
                    });
                }

                windowsItem.Items.Add(formNode);
            }
        }

        var moduleCandidates = _project is not null
            ? _project.SourcePaths
            : Directory.EnumerateFiles(_workspacePath, "*.yyc", SearchOption.AllDirectories);
        var moduleFiles = moduleCandidates
            .Where(file => !IsIgnoredPath(file))
            .Where(file => !YyFormCompanionFiles.IsGeneratedBindingPath(file))
            .Where(file => !linkedCodeFiles.Contains(Path.GetFullPath(file)))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase);
        foreach (var module in moduleFiles)
        {
            assembliesItem.Items.Add(new TreeViewItem
            {
                Header = MakeTreeHeader(Path.GetFileNameWithoutExtension(module)),
                Tag = module
            });
        }

        root.Items.Add(windowsItem);
        root.Items.Add(assembliesItem);
        root.Items.Add(new TreeViewItem { Header = MakeTreeHeader("常量表..."), Tag = new TableLink("常量表") });

        var resourceItem = new TreeViewItem { Header = MakeTreeHeader("资源表") };
        resourceItem.Items.Add(new TreeViewItem { Header = MakeTreeHeader("图片或图片组..."), Tag = new TableLink("资源表") });
        resourceItem.Items.Add(new TreeViewItem { Header = MakeTreeHeader("声音..."), Tag = new TableLink("资源表") });
        root.Items.Add(resourceItem);

        ProjectTree.Items.Add(root);
    }

    private void PopulateDirectoryTree()
    {
        ProjectDirectoryTree.Items.Clear();
        if (string.IsNullOrWhiteSpace(_workspacePath) ||
            !Directory.Exists(_workspacePath))
        {
            return;
        }

        var root = CreateDirectoryNode(_workspacePath, expanded: true);
        ProjectDirectoryTree.Items.Add(root);
    }

    private TreeViewItem CreateDirectoryNode(string path, bool expanded)
    {
        var isDirectory = Directory.Exists(path);
        var name = Path.GetFileName(path);
        var item = new TreeViewItem
        {
            Header = MakeTreeHeader((isDirectory ? "▸  " : "·  ") + name),
            Tag = path,
            IsExpanded = expanded
        };
        if (!isDirectory)
        {
            return item;
        }

        try
        {
            foreach (var directory in Directory.EnumerateDirectories(path)
                         .Where(directory => !IgnoredDirectories.Contains(Path.GetFileName(directory)))
                         .Where(directory => !new[]
                         {
                             "artifacts", "tmp-shots", "screenshots"
                         }.Contains(Path.GetFileName(directory), StringComparer.OrdinalIgnoreCase))
                         .OrderBy(directory => directory, StringComparer.OrdinalIgnoreCase))
            {
                item.Items.Add(CreateDirectoryNode(directory, expanded: false));
            }

            foreach (var file in Directory.EnumerateFiles(path)
                         .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
            {
                item.Items.Add(CreateDirectoryNode(file, expanded: false));
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            item.Items.Add(new TreeViewItem
            {
                Header = MakeTreeHeader("（无法读取目录）"),
                IsEnabled = false
            });
        }

        return item;
    }

    private void OnDirectoryTreeSelectionChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem { Tag: string path })
        {
            return;
        }

        var relative = string.IsNullOrWhiteSpace(_workspacePath)
            ? path
            : Path.GetRelativePath(_workspacePath, path);
        SetStatus(relative + (Directory.Exists(path) ? "（目录）" : "（双击打开）"), ReadyBrush);
    }

    private void OnDirectoryTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectDirectoryTree.SelectedItem is not TreeViewItem { Tag: string path })
        {
            return;
        }

        if (Directory.Exists(path))
        {
            if (ProjectDirectoryTree.SelectedItem is TreeViewItem item)
            {
                item.IsExpanded = !item.IsExpanded;
            }

            return;
        }

        if (File.Exists(path))
        {
            OpenDocument(path);
        }
    }

    private static bool IsIgnoredPath(string file) =>
        file.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(part => part is "bin" or "obj" or ".git" or ".vs");

    private static TextBlock MakeTreeHeader(string text) =>
        new()
        {
            Text = text,
            FontSize = 12
        };

    private void OnProjectTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        switch (e.NewValue)
        {
            case TreeViewItem { Tag: TableLink { Kind: "自定义数据类型" } }:
                ShowHint("自定义数据类型将在后续版本提供。");
                break;
            case TreeViewItem { Tag: TableLink link }:
                SetStatus($"数据表：{link.Kind}(双击打开)", ReadyBrush);
                break;
            case TreeViewItem { Tag: string path }:
                SetStatus($"{Path.GetFileName(path)}(双击打开)", ReadyBrush);
                break;
        }
    }

    private void OnProjectTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ProjectTree.SelectedItem is not TreeViewItem item)
        {
            return;
        }

        switch (item.Tag)
        {
            case TableLink { Kind: "自定义数据类型" }:
                ShowHint("自定义数据类型将在后续版本提供。");
                break;
            case TableLink link:
                ShowDataTableView(link.Kind);
                break;
            case string path when File.Exists(path):
                OpenDocument(path);
                break;
            default:
                item.IsExpanded = !item.IsExpanded;
                break;
        }
    }

    private void ShowDataTableView(string kind)
    {
        if (_project is null)
        {
            ShowHint("请先打开项目后再编辑数据表。");
            return;
        }

        _dataTableView ??= new DataTableView();
        if (!ReferenceEquals(DataTableViewHost.Content, _dataTableView))
        {
            DataTableViewHost.Content = _dataTableView;
        }

        Editor.Visibility = Visibility.Collapsed;
        EasyLanguageEditor.Visibility = Visibility.Collapsed;
        CodeWindowBar.Visibility = Visibility.Collapsed;
        DesignerPanel.Visibility = Visibility.Collapsed;
        ParameterHintBar.Visibility = Visibility.Collapsed;
        DataTableViewHost.Visibility = Visibility.Visible;
        InspectorPanel.Visibility = Visibility.Collapsed;
        RightToolsPanel.Margin = new Thickness(0);

        _dataTableView.Load(_project, kind, () => _project.SaveManifest());
        SetStatus($"数据表：{kind}", ReadyBrush);
    }

    private void OpenDocument(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var extension = Path.GetExtension(fullPath);
        if (!string.Equals(extension, ".yyc", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".yyx", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(extension, ".yyproj", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "旧版源码格式不再受支持。\n\n请使用 .yyc 结构化代码、.yyx 窗体或 .yyproj 项目。",
                "不支持的旧格式",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var existing = _documents.FirstOrDefault(document =>
            string.Equals(document.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            DocumentTabs.SelectedItem = existing;
            return;
        }

        try
        {
            var source = File.ReadAllText(
                fullPath,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true));
            var document = new EditorDocument(fullPath, Path.GetFileName(fullPath), source);
            _documents.Add(document);
            DocumentTabs.SelectedItem = document;
            _settings.AddRecent(fullPath);
            SetStatus(
                document.IsGeneratedBinding
                    ? $"自动生成的界面关系（只读）：{Path.GetFileName(fullPath)}"
                    : $"已打开 {Path.GetFileName(fullPath)}",
                ReadyBrush);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DecoderFallbackException)
        {
            MessageBox.Show(
                $"无法打开文件：\n{exception.Message}",
                "打开失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void AddNewDocument(string source)
    {
        var displayName = NextUntitledName();
        var content = string.Equals(Path.GetExtension(displayName), ".yyc", StringComparison.OrdinalIgnoreCase)
            ? YyCodeSerializer.Serialize(YyCodeDocumentConverter.FromSource(source))
            : source;
        var document = new EditorDocument(null, displayName, content);
        _documents.Add(document);
        DocumentTabs.SelectedItem = document;
    }

    /// <summary>添加起始页标签(对齐官方:启动/关闭全部文档后先到起始页)。</summary>
    private void AddStartPage()
    {
        if (_documents.Any(document => document.IsStartPage))
        {
            DocumentTabs.SelectedItem = _documents.First(document => document.IsStartPage);
            return;
        }

        var document = new EditorDocument(null, "起始页", string.Empty) { IsStartPage = true };
        _documents.Add(document);
        DocumentTabs.SelectedItem = document;
    }

    private void ShowStartPage()
    {
        DataTableViewHost.Visibility = Visibility.Collapsed;
        DesignerPanel.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Collapsed;
        EasyLanguageEditor.Visibility = Visibility.Collapsed;
        CodeWindowBar.Visibility = Visibility.Collapsed;
        ParameterHintBar.Visibility = Visibility.Collapsed;
        InspectorPanel.Visibility = Visibility.Collapsed;
        RightToolsPanel.Margin = new Thickness(0);
        _startPageView.Visibility = Visibility.Visible;
        RestoreInspectorColumn();
        PropertyRows.Visibility = Visibility.Collapsed;
        _designForm = null;
        _selectedControl = null;
        _selectedElement = null;
        _selectedControls.Clear();
        ClearSelectionVisuals();
        RefreshStartPageRecent();
    }

    private void RefreshStartPageRecent() =>
        _startPageView.SetRecent(_settings.RecentFiles);

    private string NextUntitledName()
    {
        if (_documents.All(document => document.DisplayName != "未命名.yyc"))
        {
            return "未命名.yyc";
        }

        var index = 2;
        while (_documents.Any(document => document.DisplayName == $"未命名 {index}.yyc"))
        {
            index++;
        }

        return $"未命名 {index}.yyc";
    }

    private string NextUntitledFormName()
    {
        if (_documents.All(document => document.DisplayName != "未命名窗体.yyx"))
        {
            return "未命名窗体.yyx";
        }

        var index = 2;
        while (_documents.Any(document => document.DisplayName == $"未命名窗体 {index}.yyx"))
        {
            index++;
        }

        return $"未命名窗体 {index}.yyx";
    }

    private void ActivateDocument(EditorDocument document)
    {
        if (_currentDocument is not null && !_currentDocument.IsForm)
        {
            _currentDocument.CaretOffset = Editor.CaretOffset;
            _currentDocument.VerticalOffset = Editor.VerticalOffset;
        }

        _currentDocument = document;
        _helpFunctionsSource = null;
        _helpFunctions.Clear();
        if (document.IsStartPage)
        {
            ShowStartPage();
            UpdateDocumentIdentity();
            return;
        }

        if (document.IsForm)
        {
            ShowFormDesigner(document);
            UpdateDocumentIdentity();
            return;
        }

        if (document.IsGeneratedBinding)
        {
            ShowCodeEditor(document);
            _isLoadingDocument = true;
            try
            {
                Editor.Text = document.Text;
                Editor.IsReadOnly = true;
            }
            finally
            {
                _isLoadingDocument = false;
            }

            UpdateDocumentIdentity();
            SetStatus("自动生成的界面关系文件（只读）", ReadyBrush);
            return;
        }

        if (document.IsStructuredCode)
        {
            ShowPlainStructuredCode(document);
            UpdateDocumentIdentity();
            return;
        }

        ShowCodeEditor(document);
        _isLoadingDocument = true;
        try
        {
            Editor.Text = document.Text;
            Editor.CaretOffset = Math.Clamp(document.CaretOffset, 0, Editor.Document.TextLength);
            Editor.ScrollToVerticalOffset(document.VerticalOffset);
        }
        finally
        {
            _isLoadingDocument = false;
        }

        UpdateDocumentIdentity();
        CaretText.Text = $"行 {Editor.TextArea.Caret.Line}，列 {Editor.TextArea.Caret.Column}";
    }

    private void ShowCodeEditor(EditorDocument document)
    {
        DataTableViewHost.Visibility = Visibility.Collapsed;
        DesignerPanel.Visibility = Visibility.Collapsed;
        _startPageView.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        EasyLanguageEditor.Visibility = Visibility.Collapsed;
        Editor.IsReadOnly = document.IsGeneratedBinding;
        InspectorPanel.Visibility = Visibility.Collapsed;
        RightToolsPanel.Margin = new Thickness(0);
        CodeWindowBar.Visibility =
            document is { IsStructuredCode: true, IsGeneratedBinding: false }
                ? Visibility.Visible
                : Visibility.Collapsed;
        ParameterHintBar.Visibility = Visibility.Visible;
        RestoreInspectorColumn();
        PropertyRows.Visibility = Visibility.Collapsed;
        _designForm = null;
        _designerDocument = null;
        _selectedControl = null;
        _selectedElement = null;
        _selectedControls.Clear();
        _designerDragBounds.Clear();
        ClearSelectionVisuals();
    }

    private void ShowFormDesigner(EditorDocument document)
    {
        DataTableViewHost.Visibility = Visibility.Collapsed;
        try
        {
            _designForm = LoadFormDocument(document.Text, document.FilePath);
            _designerDocument = document;
            var currentSnapshot = YyFormSerializer.Serialize(_designForm);
            if (document.LastDesignerSnapshot is null ||
                !string.Equals(document.LastDesignerSnapshot, currentSnapshot, StringComparison.Ordinal))
            {
                document.DesignerUndo.Clear();
                document.DesignerRedo.Clear();
                document.LastDesignerSnapshot = currentSnapshot;
            }

            Editor.Visibility = Visibility.Collapsed;
            EasyLanguageEditor.Visibility = Visibility.Collapsed;
            CodeWindowBar.Visibility = Visibility.Collapsed;
            _startPageView.Visibility = Visibility.Collapsed;
            DesignerPanel.Visibility = Visibility.Visible;
            InspectorPanel.Visibility = Visibility.Visible;
            RightToolsPanel.Margin = new Thickness(0, 0, 132, 0);
            ParameterHintBar.Visibility = Visibility.Collapsed;
            RestoreInspectorColumn();
            PropertyRows.Visibility = Visibility.Visible;
            RenderDesignerForm();
            UpdateDesignerHistoryButtons();
            UpdateDesignerTitleChrome();
            SetStatus($"窗体设计：{_designForm.Title}", ReadyBrush);
        }
        catch (YyFormFormatException exception)
        {
            ShowCodeEditor(document);
            _isLoadingDocument = true;
            try
            {
                Editor.Text = document.Text;
                NavigateTo(exception.Line, exception.Column);
            }
            finally
            {
                _isLoadingDocument = false;
            }

            AppendOutput(string.Empty);
            AppendOutput($"✕ 窗体 XML 错误 · 第 {exception.Line} 行，第 {exception.Column} 列");
            AppendOutput($"  {exception.Message}");
            SetStatus("窗体 XML 无法解析，已切换到文本编辑模式", ErrorBrush);
        }
    }

    private void ShowPlainStructuredCode(EditorDocument document)
    {
        ShowCodeEditor(document);
        _isLoadingDocument = true;
        try
        {
            Editor.Text = document.PlainCodeText;
            Editor.CaretOffset = Math.Clamp(
                document.CaretOffset,
                0,
                Editor.Document.TextLength);
        }
        finally
        {
            _isLoadingDocument = false;
        }

        ApplyCodePresentation(document);
        SetStatus($"代码窗口：{document.DisplayName}", ReadyBrush);
    }

    private void HideInspectorColumn()
    {
        // 官方布局:窗口组件箱常驻右侧,不随文档视图隐藏。
    }

    private void RestoreInspectorColumn()
    {
        // 官方布局:窗口组件箱常驻右侧,不随文档视图隐藏。
    }

}
