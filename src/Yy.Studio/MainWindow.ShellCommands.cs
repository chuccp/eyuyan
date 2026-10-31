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
/// 外壳菜单、面板开关、工具、资源、模块和窗口菜单。
/// </summary>
public partial class MainWindow : Window
{
    private DocumentationWindow? _documentationWindow;

    private void OnClearOutput(object sender, RoutedEventArgs e)
    {
        OutputBox.Clear();
        RunInfoText.Text = string.Empty;
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnDocumentationClick(object sender, RoutedEventArgs e)
    {
        if (_documentationWindow is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }

        var window = new DocumentationWindow { Owner = this };
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_documentationWindow, window))
            {
                _documentationWindow = null;
            }
        };
        _documentationWindow = window;
        window.Show();
    }

    private void OnAboutClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            """
            易语 Studio 3.0 MVP

            一个 Windows 原生、中文优先的现代易语言重制版。

            当前内核：解释执行、UTF-8、函数/递归、集合、对象、
            异常处理、JSON、行号诊断和可取消运行。

            窗体采用 .yyx 界面、.Designer.yyc 自动关系和 .yyc 用户代码
            三文件模型；用户代码在连续代码窗口中编辑。

            原版 .e 二进制工程格式不再兼容。
            """,
            "关于易语 Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnToggleExplorer(object sender, RoutedEventArgs e)
    {
        SetProjectToolWindowVisible(
            ProjectExplorerPanel.Visibility != Visibility.Visible);
        PersistWindowLayout(markCustom: true);
    }

    private void OnToggleToolboxPanel(object sender, RoutedEventArgs e)
    {
        if (_designForm is null)
        {
            SetStatus("窗口组件箱仅在窗体设计界面显示。", WarningBrush);
            return;
        }

        if (InspectorPanel.Visibility == Visibility.Visible)
        {
            InspectorPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            InspectorPanel.Visibility = Visibility.Visible;
        }

        UpdateRightToolsColumn();
        PersistWindowLayout(markCustom: true);
    }

    private void OnToggleStatusBar(object sender, RoutedEventArgs e)
    {
        var visible = sender is MenuItem item
            ? item.IsChecked
            : ToggleStatusBarMenuItem.IsChecked;
        SetStatusBarVisible(visible);
        PersistWindowLayout(markCustom: true);
    }

    private void OnShowWorkTab(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag })
        {
            return;
        }

        ShowRightToolsTab(tag == "程序" ? 0 : 1);
    }

    private void OnOpenDataTableView(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string kind })
        {
            ShowDataTableView(kind);
        }
    }

    private void OnInsertFunctionClick(object sender, RoutedEventArgs e)
    {
        if (Editor.Visibility == Visibility.Visible &&
                 _currentDocument is { IsStructuredCode: true, UsePlainCodeEditor: true })
        {
            InsertPlainCodeSnippet("子程序 新子程序()\n    \n结束子程序", "新子程序");
        }
        else
        {
            SetStatus("请先打开 .yyc 代码文件。", WarningBrush);
        }
    }

    private void OnInsertConstantClick(object sender, RoutedEventArgs e)
    {
        ShowDataTableView("常量表");
    }

    private void OnInsertLocalVariableClick(object sender, RoutedEventArgs e)
    {
        if (Editor.Visibility == Visibility.Visible &&
                 _currentDocument is { IsStructuredCode: true, UsePlainCodeEditor: true })
        {
            InsertPlainCodeSnippet("变量 名称 = 值", "名称");
        }
        else
        {
            SetStatus("请先打开 .yyc 代码文件。", WarningBrush);
        }
    }

    private void InsertPlainCodeSnippet(string text, string selection)
    {
        var offset = Editor.CaretOffset;
        Editor.Document.Insert(offset, text);
        var relative = text.IndexOf(selection, StringComparison.Ordinal);
        Editor.Select(
            offset + Math.Max(0, relative),
            relative >= 0 ? selection.Length : 0);
        Editor.Focus();
    }

    private void OnMenuEditorClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            this,
            "菜单编辑器将在后续版本提供。\n当前可先在窗体属性中设置菜单。",
            "菜单编辑器",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void OnPrintClick(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is null)
        {
            SetStatus("没有可打印的文档。", WarningBrush);
            return;
        }

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var flow = new FlowDocument
        {
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            PagePadding = new Thickness(48),
            PageWidth = dialog.PrintableAreaWidth
        };
        flow.Blocks.Add(new Paragraph(new Run(_currentDocument.DisplayName))
        {
            FontWeight = FontWeights.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 12)
        });
        foreach (var line in _currentDocument.Text.Split('\n'))
        {
            flow.Blocks.Add(new Paragraph(new Run(line.TrimEnd('\r')))
            {
                Margin = new Thickness(0)
            });
        }

        dialog.PrintDocument(
            ((IDocumentPaginatorSource)flow).DocumentPaginator,
            _currentDocument.DisplayName);
        SetStatus($"已发送到打印机：{_currentDocument.DisplayName}", ReadyBrush);
    }

    private void OnAddResourceClick(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            ShowHint("请先打开项目，再向资源表添加资源。");
            return;
        }

        if (sender is not MenuItem { Tag: string kind })
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = $"添加{kind}资源",
            Filter = kind == "图片"
                ? "图片文件|*.bmp;*.png;*.jpg;*.jpeg;*.gif;*.ico|所有文件|*.*"
                : "声音文件|*.wav;*.mp3;*.mid;*.wma|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var entry = _project.AddResource(dialog.FileName, kind);
            ShowDataTableView("资源表");
            SetStatus(
                $"已复制{kind}资源“{entry.Name}”到 {entry.File}",
                ReadyBrush);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or ArgumentException)
        {
            MessageBox.Show(
                this,
                $"无法添加资源：\n{exception.Message}",
                "添加资源",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ApplySettings()
    {
        Editor.FontSize = Configuration.Editor.FontSize;
        Editor.Options.IndentationSize = Configuration.Editor.TabWidth;
    }

    private void OnSystemConfigClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SystemConfigDialog(Configuration) { Owner = this };
        dialog.Applied += updated =>
        {
            Configuration = updated;
            ApplySettings();
            ApplyConfiguredShortcuts();
        };
        if (dialog.ShowDialog() == true)
        {
            SetStatus("系统配置已保存", ReadyBrush);
        }
    }

    private void OnOpenStudioConfigClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Configuration.ConfigurationPath ?? StudioConfiguration.UserFilePath;
            Configuration.ConfigurationPath = path;
            if (!File.Exists(path))
            {
                Configuration.Save();
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
            ShowHint($"已打开 Studio 配置：{path}\n修改 MCP 端口等配置后需要重启 Studio。");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                "无法打开 Studio 配置文件：\n" + exception.Message,
                "Studio 配置",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OnModuleManagerClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ModuleManagerDialog(_workspacePath) { Owner = this };
        dialog.ShowDialog();
    }

    private void OnCompileModuleClick(object sender, RoutedEventArgs e)
    {
        if (_currentDocument?.IsStructuredCode != true)
        {
            ShowHint("请先打开 .yyc 代码文件，再编译为易模块。");
            return;
        }

        YyCodeDocument document;
        try
        {
            document = YyCodeDocumentConverter.FromSource(
                _currentDocument.PlainCodeText,
                _currentDocument.FilePath ?? _currentDocument.DisplayName);
        }
        catch (YyDiagnostic diagnostic)
        {
            ShowDiagnostic(
                "易模块编译错误",
                diagnostic.Message,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.Hint,
                diagnostic.SourceName);
            return;
        }

        var exports = document.Functions.Where(function => function.IsExport || function.IsPublic).ToList();
        if (exports.Count == 0)
        {
            MessageBox.Show(
                this,
                "当前程序集没有可导出的子程序。\n请先在代码中标记要公开或导出的子程序。",
                "编译为易模块",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "编译为易模块",
            Filter = "易模块 (*.ec)|*.ec",
            DefaultExt = ".ec",
            AddExtension = true,
            FileName = document.Name + ".ec",
            InitialDirectory = !string.IsNullOrWhiteSpace(_workspacePath)
                ? _workspacePath
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var module = YyModule.Compile(document, exports);
            module.Save(dialog.FileName);
            SetStatus($"易模块已生成：{Path.GetFileName(dialog.FileName)}", ReadyBrush);
            MessageBox.Show(
                this,
                $"易模块编译完成：\n{dialog.FileName}\n\n共导出 {exports.Count} 个公开子程序。",
                "编译为易模块",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus("易模块编译失败", ErrorBrush);
            MessageBox.Show(this, exception.Message, "编译为易模块",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDatabaseDesignerClick(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            ShowHint("请先打开项目，再设计数据表。");
            return;
        }

        var dialog = new DatabaseDesignerDialog(
            _project.DatabaseTables,
            () => _project.SaveManifest())
        {
            Owner = this
        };
        dialog.ShowDialog();
        SetStatus("数据库结构已保存", ReadyBrush);
    }

    private void OnDataBindClick(object sender, RoutedEventArgs e)
    {
        if (_project is null)
        {
            ShowHint("请先打开项目，再设置数据绑定。");
            return;
        }

        if (_designForm is null || _selectedControl is null)
        {
            ShowHint("请先在窗体设计器中选择一个控件，再设置数据绑定。");
            return;
        }

        var dialog = new DataBindDialog(_selectedControl, _project.DatabaseTables) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            PopulateDesignerProperties();
            DesignerChanged();
            SetStatus($"已更新 {_selectedControl.Name} 的数据绑定", ReadyBrush);
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_runCancellation is not null)
        {
            _runCancellation.Cancel();
        }

        if (_runningNativeProcess is { HasExited: false } process)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or Win32Exception)
            {
                // The native process exited between the state check and shutdown.
            }
        }

        foreach (var document in _documents.Where(document => document.IsDirty).ToList())
        {
            if (!EnsureCanCloseDocument(document))
            {
                e.Cancel = true;
                return;
            }
        }

        PersistWindowLayout(markCustom: false);
    }

}
