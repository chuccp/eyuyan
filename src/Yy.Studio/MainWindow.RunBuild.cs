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
using Yy.Native;
using Yy.Studio.Structured;


namespace Yy.Studio;

/// <summary>
/// 运行、调试、监视、搜寻、剪辑历史、预览和项目编译命令。
/// </summary>
public partial class MainWindow : Window
{
    private async void OnRun(object sender, ExecutedRoutedEventArgs e)
    {
        if (_currentDocument?.IsStartPage == true)
        {
            ShowHint("请先在起始页“新建”或“打开…”一个程序。");
            return;
        }

        if (_currentDocument?.IsForm == true)
        {
            PreviewCurrentForm();
            return;
        }

        if (_debugPaused is not null && _debugProbe is not null)
        {
            _debugPaused = null;
            _debugProbe.Continue();
            SetStatus("调试继续（全速）…", WarningBrush);
            RunInfoText.Text = "调试运行中…";
            return;
        }

        await RunDocumentAsync();
    }

    private void OnDebugStep(object sender, ExecutedRoutedEventArgs e) =>
        DebugResumeOrStart(YyDebugMode.StepInto);

    private void OnDebugStepOver(object sender, ExecutedRoutedEventArgs e) =>
        DebugResumeOrStart(YyDebugMode.StepOver);

    private void OnDebugStepOut(object sender, ExecutedRoutedEventArgs e) =>
        DebugResumeOrStart(YyDebugMode.StepOut);

    private void OnDebugRunToCursor(object sender, ExecutedRoutedEventArgs e) =>
        DebugResumeOrStart(YyDebugMode.RunToLine);

    private void DebugResumeOrStart(YyDebugMode mode)
    {
        if (_runCancellation is not null)
        {
            if (_debugProbe is null || _debugPaused is null)
            {
                SetStatus("程序正在运行（未暂停）…", WarningBrush);
                return;
            }

            var probe = _debugProbe;
            var pause = _debugPaused;
            switch (mode)
            {
                case YyDebugMode.StepInto:
                    _debugPaused = null;
                    probe.StepInto();
                    break;
                case YyDebugMode.StepOver:
                    _debugPaused = null;
                    probe.StepOver(pause.Depth);
                    break;
                case YyDebugMode.StepOut:
                    _debugPaused = null;
                    probe.StepOut(pause.Depth);
                    break;
                case YyDebugMode.RunToLine:
                    if (!TryGetCursorLine(out var target))
                    {
                        SetStatus("请先单击代码行/语句行定位光标。", WarningBrush);
                        return;
                    }

                    _debugPaused = null;
                    probe.RunToLine(target, pause.Depth);
                    break;
            }

            SetStatus("调试继续…", WarningBrush);
            RunInfoText.Text = "调试运行中…";
            return;
        }

        var probe2 = new YyDebugProbe(mode);
        if (mode == YyDebugMode.RunToLine)
        {
            if (!TryGetCursorLine(out var line))
            {
                SetStatus("请先单击代码行/语句行定位光标。", WarningBrush);
                return;
            }

            probe2 = new YyDebugProbe(YyDebugMode.RunToLine, line);
        }

        _ = RunDocumentAsync(probe2);
    }

    private bool TryGetCursorLine(out int line)
    {
        line = 0;
        if (DesignerPanel.Visibility == Visibility.Visible)
        {
            return false;
        }

        if (Editor.Visibility == Visibility.Visible)
        {
            var offset = Math.Clamp(Editor.CaretOffset, 0, Editor.Document.TextLength);
            line = Editor.Document.GetLineByOffset(offset).LineNumber;
            return true;
        }

        return false;
    }

    private void OnDebugToggleBreakpoint(object sender, ExecutedRoutedEventArgs e)
    {
        if (!TryGetCursorLine(out var line))
        {
            SetStatus("请先单击代码行/语句行定位光标。", WarningBrush);
            return;
        }

        string message;
        if (_breakpoints.TryRemove(line, out _))
        {
            message = $"已清除第 {line} 行断点";
        }
        else
        {
            _breakpoints[line] = 0;
            message = $"已在第 {line} 行设置断点";
        }

        UpdateBreakpointMarkers();
        SetStatus(message, ReadyBrush);
    }

    private void UpdateBreakpointMarkers()
    {
        // 断点状态由行号集合维护；代码窗口使用 AvalonEdit 原生行号。
    }

    // =============== 底部面板:监视表/搜寻/剪辑历史 ===============

    private void OnWatchAddClick(object sender, RoutedEventArgs e) => AddWatch();

    private void OnWatchInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddWatch();
            e.Handled = true;
        }
    }

    private void AddWatch()
    {
        var name = WatchInputBox.Text.Trim();
        if (string.IsNullOrEmpty(name) || _watchNames.Contains(name, StringComparer.Ordinal))
        {
            return;
        }

        _watchNames.Add(name);
        WatchInputBox.Text = string.Empty;
        RefreshWatches();
    }

    private void OnWatchRemoveClick(object sender, RoutedEventArgs e)
    {
        foreach (var item in WatchList.SelectedItems.OfType<VariableRow>().ToList())
        {
            _watchNames.Remove(item.Key);
        }

        RefreshWatches();
    }

    private void RefreshWatches(IReadOnlyDictionary<string, object?>? variables = null)
    {
        WatchList.ItemsSource = _watchNames
            .Select(name => new VariableRow(
                name,
                variables is not null && variables.TryGetValue(name, out var value)
                    ? DisplayValue(value)
                    : "（运行暂停时显示）"))
            .ToList();
    }

    private void OnSearchClick(object sender, RoutedEventArgs e) => RunSearch();

    private void OnSearchInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            RunSearch();
            e.Handled = true;
        }
    }

    private void RunSearch()
    {
        var keyword = SearchInputBox.Text;
        if (string.IsNullOrEmpty(keyword))
        {
            return;
        }

        var text = Editor.Text;
        var rows = new List<SearchResultRow>();
        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(new SearchResultRow(i + 1, lines[i].Trim()));
            }
        }

        SearchResultsList1.ItemsSource = rows;
        BottomTabs.SelectedIndex = 5;
        SetStatus($"搜寻“{keyword}”：找到 {rows.Count} 处", ReadyBrush);
    }

    private void OnSearchResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is not SearchResultRow row)
        {
            return;
        }

        if (Editor.Visibility == Visibility.Visible)
        {
            if (row.Line <= Editor.Document.LineCount)
            {
                var line = Editor.Document.GetLineByNumber(row.Line);
                Editor.Select(line.Offset, 0);
                Editor.ScrollToLine(row.Line);
                Editor.Focus();
            }

            return;
        }

    }

    private void OnClipHistoryInsert(object sender, MouseButtonEventArgs e)
    {
        if (ClipHistoryList.SelectedItem is ClipHistoryRow row &&
            Editor.Visibility == Visibility.Visible)
        {
            Editor.Document.Insert(Editor.CaretOffset, row.Text);
            Editor.Focus();
        }
    }

    private void RecordClipHistory()
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                return;
            }

            var text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text) || string.Equals(text, _lastClipText, StringComparison.Ordinal))
            {
                return;
            }

            _lastClipText = text;
            var display = text.Replace("\r", string.Empty).Replace("\n", " ");
            if (display.Length > 200)
            {
                display = display[..200] + "…";
            }

            _clipHistory.Insert(0, new ClipHistoryRow(text));
            if (_clipHistory.Count > 50)
            {
                _clipHistory.RemoveAt(_clipHistory.Count - 1);
            }

            ClipHistoryList.ItemsSource = _clipHistory.ToList();
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.COMException or ExternalException)
        {
            // 剪贴板被占用时忽略本次记录。
        }
    }

    private void OnDebugPaused(YyDebugPause pause)
    {
        _debugPaused = pause;
        var lineText = pause.Line > 0 && pause.Line <= _debugSourceLines.Length
            ? _debugSourceLines[pause.Line - 1].Trim()
            : string.Empty;
        SetStatus($"调试暂停 · 第 {pause.Line} 行：{lineText}", WarningBrush);
        RunInfoText.Text = $"暂停 · 第 {pause.Line} 行";

        CallStackList.Items.Clear();
        CallStackList.Items.Add(new ListBoxItem { Content = $"第 {pause.Line} 行    {lineText}" });
        for (var depth = 0; depth < pause.Depth; depth++)
        {
            CallStackList.Items.Add(new ListBoxItem { Content = $"（外层调用 {depth + 1}）" });
        }

        ShowVariables(pause.Variables);
        RefreshWatches(pause.Variables);
        BottomTabs.SelectedIndex = 4;
    }

    private async void OnRunProject(object sender, ExecutedRoutedEventArgs e)
    {
        if (_project is null)
        {
            MessageBox.Show(
                "当前目录没有可读取的 .yyproj 项目清单。",
                "运行项目",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!SaveDocumentsForBuild())
        {
            return;
        }

        var output = Path.Combine(
            Path.GetTempPath(),
            "yy-native-run-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            _project = YyProject.LoadForDirectory(_project.DirectoryPath)
                ?? throw new InvalidDataException("项目清单已经不存在。");
            SetStatus("正在编译原生项目…", WarningBrush);
            await Task.Run(() => NativeCompiler.CompileProject(_project, output));
            SetStatus("正在运行原生项目", ReadyBrush);
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = output,
                WorkingDirectory = _project.DirectoryPath,
                UseShellExecute = false
            }) ?? throw new InvalidOperationException("无法启动原生项目。");
            await process.WaitForExitAsync();
            SetStatus("原生项目已退出", ReadyBrush);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or NativeCompileException or InvalidOperationException)
        {
            SetStatus("原生项目运行失败", ErrorBrush);
            MessageBox.Show(exception.Message, "运行项目", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            try
            {
                File.Delete(output);
            }
            catch (IOException)
            {
            }
        }
    }

    private async void OnCompileProject(object sender, ExecutedRoutedEventArgs e)
    {
        await CompileProjectAsync();
    }

    private async Task CompileProjectAsync()
    {
        if (_project is null)
        {
            MessageBox.Show(
                "请先打开一个 .yyproj 项目。",
                "编译为 EXE",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!SaveDocumentsForBuild())
        {
            return;
        }

        try
        {
            _project = YyProject.LoadForDirectory(_project.DirectoryPath)
                ?? throw new InvalidDataException("项目清单已经不存在。");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException)
        {
            MessageBox.Show(exception.Message, "编译为 EXE", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "编译原生 Win32 可执行文件",
            Filter = "Windows 应用 (*.exe)|*.exe",
            DefaultExt = ".exe",
            AddExtension = true,
            FileName = _project.OutputName,
            InitialDirectory = _project.DirectoryPath
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        SetStatus("正在编译项目…", WarningBrush);
        try
        {
            var output = await Task.Run(() =>
                NativeCompiler.CompileProject(_project, dialog.FileName));
            SetStatus("原生 EXE 编译完成", ReadyBrush);
            MessageBox.Show(
                $"原生编译完成：\n{output.OutputPath}\n\n" +
                $"PE 大小：{output.ImageSize} 字节\n" +
                "直接调用 kernel32/user32，不使用 .NET、WPF 或 C Runtime。",
                "编译完成",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException or
                UnauthorizedAccessException or NativeCompileException)
        {
            SetStatus("EXE 编译失败", ErrorBrush);
            MessageBox.Show(
                exception.Message,
                "编译失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
        }
    }

    private bool SaveDocumentsForBuild()
    {
        var dirty = _documents.Where(document => document.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            return true;
        }

        var result = MessageBox.Show(
            $"有 {dirty.Count} 个文件尚未保存。编译前需要保存这些文件，是否继续？",
            "保存并编译",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return false;
        }

        foreach (var document in dirty)
        {
            if (!SaveDocument(document))
            {
                return false;
            }
        }

        return true;
    }

    private void OnStop(object sender, ExecutedRoutedEventArgs e)
    {
        if (_runCancellation is not null)
        {
            _debugProbe?.Stop();
            _debugPaused = null;
            _runCancellation.Cancel();
            SetStatus("正在停止…", WarningBrush);
            RunInfoText.Text = "已请求停止";
        }
    }

}
