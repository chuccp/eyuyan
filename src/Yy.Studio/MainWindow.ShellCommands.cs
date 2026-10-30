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
/// 外壳菜单、面板开关、工具、支持库、资源、模块和窗口菜单。
/// </summary>
public partial class MainWindow : Window
{
    private void OnClearOutput(object sender, RoutedEventArgs e)
    {
        OutputBox.Clear();
        RunInfoText.Text = string.Empty;
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => Close();

    private void OnSyntaxClick(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(
            """
            易语 3 快速语法

            变量 名称 = "值"
            变量 年龄: 数字 = 20
            常量 周长 = 2 * 3.14 * 半径

            如果 条件 则
                ...
            否则
                ...
            结束如果

            计次循环 序号 从 1 到 10
                ...
            结束循环

            遍历 元素 于 集合
                ...
            结束遍历

            子程序 问候(姓名: 文本 = "世界"): 文本
                返回 "你好，" + 姓名
            结束子程序

            集合：[1, 2, 3]
            对象：{姓名: "林川", 年龄: 20}
            模块：引入 "工具.yyc"
            """,
            "语法速查",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
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
        if (ProjectExplorerPanel.Visibility == Visibility.Visible)
        {
            _explorerWidth = ExplorerColumn.Width;
            ProjectExplorerPanel.Visibility = Visibility.Collapsed;
            ExplorerSplitter.Visibility = Visibility.Collapsed;
            ExplorerColumn.MinWidth = 0;
            ExplorerColumn.Width = new GridLength(0);
        }
        else
        {
            ProjectExplorerPanel.Visibility = Visibility.Visible;
            ExplorerSplitter.Visibility = Visibility.Visible;
            ExplorerColumn.MinWidth = 180;
            ExplorerColumn.Width = _explorerWidth.Value > 0 ? _explorerWidth : new GridLength(252);
        }
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
            RightToolsPanel.Margin = new Thickness(0);
        }
        else
        {
            InspectorPanel.Visibility = Visibility.Visible;
            RightToolsPanel.Margin = new Thickness(0, 0, 132, 0);
        }
    }

    private void OnToggleStandardToolbar(object sender, RoutedEventArgs e)
    {
        StandardToolbarRow.Visibility =
            ToggleStandardToolbarMenuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToggleAlignToolbar(object sender, RoutedEventArgs e)
    {
        AlignToolbarRow.Visibility =
            ToggleAlignToolbarMenuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnToggleStatusBar(object sender, RoutedEventArgs e)
    {
        StatusBarRow.Visibility =
            ToggleStatusBarMenuItem.IsChecked ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnShowWorkTab(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string tag })
        {
            return;
        }

        ProjectExplorerPanel.Visibility = Visibility.Visible;
        if (tag == "支持库")
        {
            WorkTabs.SelectedIndex = 0;
        }
        else if (tag == "程序")
        {
            WorkTabs.SelectedIndex = 1;
        }
        else if (tag == "属性")
        {
            WorkTabs.SelectedIndex = 2;
        }
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
        Editor.FontSize = _settings.EditorFontSize;
        Editor.Options.IndentationSize = _settings.TabWidth;
        SnapCheckBox.IsChecked = _settings.SnapGrid;
    }

    private void OnSystemConfigClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SystemConfigDialog(_settings) { Owner = this };
        if (dialog.ShowDialog() == true)
        {
            _settings.Save();
            ApplySettings();
            SetStatus("系统配置已保存", ReadyBrush);
        }
    }

    private void OnSupportConfigClick(object sender, RoutedEventArgs e)
    {
        var dialog = new SupportConfigDialog(
            StudioCatalog.Categories.Select(category => category.Name),
            _settings.DisabledLibraries.ToHashSet(StringComparer.Ordinal))
        {
            Owner = this
        };
        if (dialog.ShowDialog() == true)
        {
            _settings.DisabledLibraries = dialog.CollectDisabled().ToList();
            _settings.Save();
            BuildSupportTree();
            SetStatus("支持库配置已保存", ReadyBrush);
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

    /// <summary>命令夹分类行:分类小图标 + 名称(对齐官方支持库夹)。</summary>
    private static FrameworkElement CategoryHeader(string name)
    {
        var (glyph, color) = name switch
        {
            "流程控制" => ("◆", "#000080"),
            "算术运算" => ("＋", "#008000"),
            "逻辑比较" => ("?", "#800080"),
            "位运算" => ("01", "#008080"),
            "变量操作" => ("x", "#800000"),
            "数组操作" => ("≣", "#000080"),
            "环境存取" => ("⌂", "#808000"),
            "拼音处理" => ("♪", "#800080"),
            "文本操作" => ("A", "#008000"),
            "字节集操作" => ("▤", "#008080"),
            "数值转换" => ("#", "#800000"),
            "时间操作" => ("◷", "#000080"),
            "磁盘操作" => ("▣", "#808000"),
            "文件读写" => ("≣", "#008000"),
            "系统处理" => ("⚙", "#800080"),
            "媒体播放" => ("▶", "#008080"),
            "程序调试" => ("❚❚", "#800000"),
            "其他" => ("…", "#808080"),
            "数据库" => ("▦", "#000080"),
            "网络通信" => ("◎", "#008000"),
            "控制台操作" => (">_", "#800080"),
            "数据类型" => ("T", "#008080"),
            "常量" => ("c", "#800000"),
            _ => ("●", "#808080")
        };
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock
        {
            Text = glyph,
            Width = 18,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)!),
            VerticalAlignment = VerticalAlignment.Center
        });
        panel.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        return panel;
    }

    private void BuildSupportTree()
    {
        SupportTree.Items.Clear();
        var root = new TreeViewItem
        {
            Header = "系统核心支持库",
            IsExpanded = true
        };
        foreach (var category in StudioCatalog.Categories)
        {
            if (_settings.DisabledLibraries.Contains(category.Name))
            {
                continue;
            }

            var categoryItem = new TreeViewItem
            {
                Header = CategoryHeader(category.Name),
                Tag = category
            };
            if (category.Commands.Count == 0)
            {
                categoryItem.Items.Add(new TreeViewItem
                {
                    Header = "（暂无命令）",
                    Foreground = new SolidColorBrush(Color.FromRgb(128, 128, 128))
                });
            }
            else
            {
                foreach (var command in category.Commands)
                {
                    categoryItem.Items.Add(new TreeViewItem
                    {
                        Header = command.Name,
                        Tag = command
                    });
                }
            }

            root.Items.Add(categoryItem);
        }

        SupportTree.Items.Add(root);
    }

    private void OnSupportTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        switch (e.NewValue)
        {
            case TreeViewItem { Tag: BuiltinDoc command }:
                ShowHint(command.Signature + "\n" + command.Description);
                break;
            case TreeViewItem { Tag: CommandCategory category }:
                ShowHint(category.Commands.Count > 0
                    ? "“" + category.Name + "”分类包含 " + category.Commands.Count + " 条命令：\n" +
                      string.Join("、", category.Commands.Select(command => command.Name)) +
                      "\n\n展开分类后双击命令即可插入代码。"
                    : "“" + category.Name + "”分类暂无命令，后续版本补充。");
                break;
        }
    }

    private void OnSupportTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2)
        {
            return;
        }

        if (SupportTree.SelectedItem is not TreeViewItem { Tag: BuiltinDoc command })
        {
            return;
        }

        if (Editor.Visibility == Visibility.Visible &&
            _currentDocument is not null)
        {
            Editor.Document.Insert(Editor.CaretOffset, command.Name);
            Editor.Focus();
            SetStatus($"已插入命令 {command.Name}()", ReadyBrush);
            e.Handled = true;
        }
    }

    private void BuildWindowMenu()
    {
        WindowMenu.Items.Clear();
        foreach (var document in _documents)
        {
            var item = new MenuItem
            {
                Header = document.DisplayName,
                Tag = document,
                IsChecked = ReferenceEquals(document, _currentDocument)
            };
            item.Click += (_, _) => DocumentTabs.SelectedItem = document;
            WindowMenu.Items.Add(item);
        }

        if (WindowMenu.Items.Count == 0)
        {
            WindowMenu.Items.Add(new MenuItem { Header = "（无打开的窗口）", IsEnabled = false });
        }
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_runCancellation is not null)
        {
            _runCancellation.Cancel();
        }

        foreach (var document in _documents.Where(document => document.IsDirty).ToList())
        {
            if (!EnsureCanCloseDocument(document))
            {
                e.Cancel = true;
                return;
            }
        }
    }

}
