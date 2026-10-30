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
/// 文档执行、诊断展示、运行状态和变量结果。
/// </summary>
public partial class MainWindow : Window
{
    private async Task<bool> RunDocumentAsync(YyDebugProbe? debugger = null)
    {
        if (_runCancellation is not null)
        {
            return false;
        }

        var document = _currentDocument;
        if (document?.IsGeneratedBinding == true)
        {
            SetStatus("自动关系文件不能直接运行，请运行关联窗体或用户代码。", WarningBrush);
            return false;
        }

        var source = document is { IsStructuredCode: true, UsePlainCodeEditor: true }
            ? document.PlainCodeText
            : document?.Text ?? Editor.Text;
        _debugLineMap.Clear();
        if (document?.IsStructuredCode == true &&
            !document.UsePlainCodeEditor &&
            string.Equals(
                Path.GetExtension(document.FilePath ?? document.DisplayName),
                ".yyc",
                StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                source = YyCodeDocumentConverter.ToSource(
                    YyCodeSerializer.Deserialize(source),
                    _debugLineMap);
            }
            catch (YyCodeFormatException exception)
            {
                AppendOutput($"✕ 结构化代码错误 · 第 {exception.Line} 行，第 {exception.Column} 列");
                AppendOutput("  " + exception.Message);
                SetStatus("结构化代码无法运行", ErrorBrush);
                return false;
            }
        }

        var sourcePath = _currentDocument?.FilePath;
        var cancellation = new CancellationTokenSource();
        _runCancellation = cancellation;
        OutputBox.Clear();
        var isDebugRun = debugger is not null;
        RunInfoText.Text = isDebugRun ? "调试运行…" : "正在运行…";
        SetRunningState(true);
        SetStatus(isDebugRun ? "调试运行中" : "程序运行中", WarningBrush);
        var succeeded = false;

        // 普通运行也挂探针,以便 F9 断点在运行中生效。
        debugger ??= new YyDebugProbe(YyDebugMode.Run);
        debugger.IsBreakpoint = line => _breakpoints.ContainsKey(line);
        _debugProbe = debugger;
        _debugSourceLines = source.Replace("\r\n", "\n").Split('\n');
        debugger.Paused += pause => Dispatcher.BeginInvoke(() => OnDebugPaused(pause));

        try
        {
            var result = await Task.Run(() => YyRunner.RunSource(
                source,
                sourcePath,
                line => Dispatcher.Invoke(() => AppendOutput(line)),
                cancellation.Token,
                debugger));

            ShowVariables(result.Variables);
            var elapsed = Math.Max(0, result.Elapsed.TotalMilliseconds);
            AppendOutput(string.Empty);
            AppendOutput($"✓ 执行完成 · {elapsed:F1} ms");
            RunInfoText.Text = $"完成 · {elapsed:F1} ms";
            SetStatus("运行完成", ReadyBrush);
            succeeded = true;
        }
        catch (YyDiagnostic diagnostic)
        {
            ShowDiagnostic(
                "编译错误",
                diagnostic.Message,
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.Hint,
                diagnostic.SourceName);
        }
        catch (YyRuntimeError error)
        {
            ShowDiagnostic(
                "运行时错误",
                error.Message,
                error.Line,
                error.Column,
                error.Detail,
                error.SourceName);
        }
        catch (OperationCanceledException)
        {
            AppendOutput(string.Empty);
            AppendOutput("■ 程序已停止。");
            RunInfoText.Text = "已停止";
            SetStatus("已停止", WarningBrush);
        }
        catch (Exception exception)
        {
            ShowDiagnostic("内部错误", exception.Message, 1, 1,
                "请保存源码并在问题反馈中附上最小复现示例。");
        }
        finally
        {
            if (ReferenceEquals(_runCancellation, cancellation))
            {
                _runCancellation = null;
            }

            if (ReferenceEquals(_debugProbe, debugger))
            {
                _debugProbe = null;
                _debugPaused = null;
            }

            cancellation.Dispose();
            SetRunningState(false);
        }

        return succeeded;
    }

    private void ShowDiagnostic(
        string category,
        string message,
        int line,
        int column,
        string? hint,
        string? sourceName = null)
    {
        if (!string.IsNullOrWhiteSpace(sourceName) &&
            File.Exists(sourceName) &&
            !string.Equals(sourceName, _currentDocument?.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            OpenDocument(sourceName);
        }

        AppendOutput(string.Empty);
        var location = string.IsNullOrWhiteSpace(sourceName)
            ? string.Empty
            : $"{Path.GetFileName(sourceName)} · ";
        AppendOutput($"✕ {category} · {location}第 {line} 行，第 {column} 列");
        AppendOutput($"  {message}");
        if (!string.IsNullOrWhiteSpace(hint))
        {
            AppendOutput($"  提示：{hint}");
        }

        RunInfoText.Text = $"{category} · {line}:{column}";
        NavigateTo(line, column);
        SetStatus($"{category}：{message}", ErrorBrush);
    }

    private void SetRunningState(bool running)
    {
        RunButton.IsEnabled = !running;
        StopButton.IsEnabled = running;
        Editor.IsReadOnly = running;
    }

    private void ShowVariables(IReadOnlyDictionary<string, object?> variables)
    {
        var display = variables
            .Select(pair => new VariableRow(pair.Key, DisplayValue(pair.Value)))
            .OrderBy(row => row.Key, StringComparer.Ordinal)
            .ToArray();
        VariableList.ItemsSource = display.Length > 0 ? display : null;
        if (display.Length > 0)
        {
            BottomTabs.SelectedIndex = 4;
        }
    }

    private static string DisplayValue(object? value)
    {
        return value switch
        {
            null => "空",
            bool boolean => boolean ? "真" : "假",
            double number when number == Math.Truncate(number) => ((long)number).ToString(),
            string text => $"\"{text}\"",
            DateTime dateTime => dateTime.ToString("yyyy/MM/dd HH:mm:ss"),
            IReadOnlyDictionary<string, object?> dictionary =>
                "{" + string.Join(", ", dictionary.Select(pair => $"{pair.Key}: {DisplayValue(pair.Value)}")) + "}",
            IEnumerable<object?> sequence =>
                "[" + string.Join(", ", sequence.Select(DisplayValue)) + "]",
            _ => value.ToString() ?? string.Empty
        };
    }


    private void NavigateTo(int line, int column)
    {
        if (Editor.Document.LineCount == 0)
        {
            return;
        }

        var targetLine = Math.Clamp(line, 1, Editor.Document.LineCount);
        var documentLine = Editor.Document.GetLineByNumber(targetLine);
        Editor.CaretOffset = documentLine.Offset;
        Editor.Select(documentLine.Offset, documentLine.Length);
        Editor.ScrollToLine(targetLine);
        Editor.Focus();
        CaretText.Text = $"行 {targetLine}，列 {column}";
    }

    private void AppendOutput(string text)
    {
        OutputBox.AppendText(text + Environment.NewLine);
        OutputBox.ScrollToEnd();
    }

    private void SetStatus(string text, Brush brush)
    {
        StatusText.Text = text;
        StatusDot.Fill = brush;
    }

    private void UpdateDocumentIdentity()
    {
        var dirty = _currentDocument?.IsDirty == true;
        var prefix = dirty ? "● " : string.Empty;
        if (_currentDocument is null)
        {
            Title = $"{prefix}易语 Studio - 目的平台: Windows";
            EditorBreadcrumbText.Text = "工作区";
            return;
        }

        if (_currentDocument.IsStartPage)
        {
            Title = "易语 Studio - 目的平台: Windows - [起始页]";
            EditorBreadcrumbText.Text = _project?.Name ?? "工作区";
            return;
        }

        var formName = _currentDocument.IsForm
            ? _designForm?.Name ?? _currentDocument.DisplayName
            : GetAssociatedFormForCurrentDocument()?.Name;

        Title = formName is null
            ? $"{prefix}易语 Studio - 目的平台: Windows - [程序集: {_currentDocument.DisplayName}]"
            : _currentDocument.IsForm
                ? $"{prefix}易语 Studio - 目的平台: Windows - [窗口: {formName}]"
                : $"{prefix}易语 Studio - 目的平台: Windows - [程序集: {_currentDocument.DisplayName} / {formName}]";
        UpdateBreadcrumb();
    }

    private void UpdateBreadcrumb(string? functionName = null)
    {
        if (_currentDocument is null || _currentDocument.IsStartPage)
        {
            EditorBreadcrumbText.Text = _project?.Name ?? "工作区";
            return;
        }

        var segments = new List<string>();
        var baseDirectory = _project?.DirectoryPath;
        var path = _currentDocument.FilePath;
        if (!string.IsNullOrWhiteSpace(baseDirectory) &&
            !string.IsNullOrWhiteSpace(path))
        {
            segments.Add(_project!.Name);
            var relative = Path.GetRelativePath(baseDirectory, path);
            segments.AddRange(relative.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries));
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(_workspacePath))
            {
                segments.Add(Path.GetFileName(_workspacePath));
            }

            segments.Add(_currentDocument.DisplayName);
        }

        if (!string.IsNullOrWhiteSpace(functionName))
        {
            segments.Add(functionName);
        }

        EditorBreadcrumbText.Text = string.Join("  ›  ", segments);
    }

}
