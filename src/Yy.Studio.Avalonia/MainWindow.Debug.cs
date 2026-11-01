using System.IO;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Yy.Language;
using Yy.Studio.Avalonia.Editor;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private YyDebugProbe? _debugProbe;
    private YyDebugPause? _debugPause;
    private CancellationTokenSource? _debugCancellation;
    private readonly HashSet<int> _breakpoints = [];

    private void OnDebugStep(object? sender, RoutedEventArgs e) =>
        _ = DebugAsync(YyDebugMode.StepInto);

    private void OnDebugStepOver(object? sender, RoutedEventArgs e) =>
        _ = DebugAsync(YyDebugMode.StepOver);

    private void OnDebugStepOut(object? sender, RoutedEventArgs e) =>
        _ = DebugAsync(YyDebugMode.StepOut);

    private void OnDebugRunToLine(object? sender, RoutedEventArgs e) =>
        _ = DebugAsync(YyDebugMode.RunToLine);

    private void OnDebugToggleBreakpoint(object? sender, RoutedEventArgs e)
    {
        if (CurrentDocument is not { StructuredView: not null } document)
        {
            SetStatus("请先在 .yyc 代码中定位断点。");
            return;
        }

        var line = document.Editor.Document.GetLineByOffset(
            Math.Clamp(document.Editor.CaretOffset, 0, document.Editor.Document.TextLength))
            .LineNumber;
        if (!_breakpoints.Add(line))
        {
            _breakpoints.Remove(line);
            SetStatus($"已清除第 {line} 行断点");
        }
        else
        {
            SetStatus($"已在第 {line} 行设置断点");
        }
    }

    private async Task DebugAsync(YyDebugMode mode)
    {
        var line = mode == YyDebugMode.RunToLine ? CursorLine() : 0;
        if (mode == YyDebugMode.RunToLine && line == 0)
        {
            SetStatus("请先在纯代码视图定位光标。");
            return;
        }

        if (_debugProbe is { } probe && _debugPause is { } pause)
        {
            _debugPause = null;
            switch (mode)
            {
                case YyDebugMode.StepInto:
                    probe.StepInto();
                    break;
                case YyDebugMode.StepOver:
                    probe.StepOver(pause.Depth);
                    break;
                case YyDebugMode.StepOut:
                    probe.StepOut(pause.Depth);
                    break;
                case YyDebugMode.RunToLine:
                    probe.RunToLine(line, pause.Depth);
                    break;
                default:
                    probe.Continue();
                    break;
            }

            SetStatus("调试继续…");
            return;
        }

        if (_debugCancellation is not null)
        {
            SetStatus("程序正在运行；请先停止或等待暂停。");
            return;
        }

        if (CurrentDocument is not { StructuredView: not null } document)
        {
            SetStatus("请先打开 .yyc 代码再调试。");
            return;
        }

        var source = CurrentCodeSource(document);
        var cancellation = new CancellationTokenSource();
        _debugCancellation = cancellation;
        var debugger = new YyDebugProbe(mode, line);
        debugger.IsBreakpoint = number => _breakpoints.Contains(number);
        debugger.Paused += pause => Dispatcher.UIThread.Post(() => OnDebugPaused(pause));
        _debugProbe = debugger;
        BottomTabs.SelectedIndex = 0;
        AppendConsole("调试开始…");
        SetStatus("调试运行中…");
        try
        {
            var result = await Task.Run(() => YyRunner.RunSource(
                source,
                document.Path,
                text => AppendConsoleFromAnyThread(text),
                cancellation.Token,
                debugger));
            ShowDebugVariables(result.Variables);
            CallStackList.ItemsSource = Array.Empty<string>();
            BottomTabs.SelectedIndex = 0;
            AppendConsole($"调试完成 · {result.Elapsed.TotalMilliseconds:F1} ms");
            SetStatus("调试完成");
        }
        catch (Exception exception) when (
            exception is YyDiagnostic or YyRuntimeError or OperationCanceledException
                or InvalidOperationException)
        {
            if (exception is OperationCanceledException)
            {
                AppendConsole("调试已停止。");
                SetStatus("调试已停止");
            }
            else
            {
                AppendConsole("调试错误：" + exception.Message);
                SetStatus("调试失败");
            }
        }
        finally
        {
            if (ReferenceEquals(_debugProbe, debugger))
            {
                _debugProbe = null;
                _debugPause = null;
            }

            if (ReferenceEquals(_debugCancellation, cancellation))
            {
                _debugCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void OnDebugPaused(YyDebugPause pause)
    {
        _debugPause = pause;
        var line = pause.Line > 0
            ? CurrentDocument?.Editor.Document.GetText(
                CurrentDocument.Editor.Document.GetLineByNumber(
                    Math.Min(pause.Line, CurrentDocument.Editor.Document.LineCount)))
            : string.Empty;
        AppendConsole($"暂停 · 第 {pause.Line} 行：{line?.Trim()}");
        foreach (var (name, value) in pause.Variables.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            AppendConsole($"  {name} = {DisplayValue(value)}");
        }

        SetStatus($"调试暂停 · 第 {pause.Line} 行");
        ShowDebugVariables(pause.Variables);
        CallStackList.ItemsSource = new[]
        {
            $"第 {pause.Line} 行 · 调用深度 {pause.Depth}"
        };
        BottomTabs.SelectedIndex = 1;
        PropertySummary.Text = $"调试暂停 · 第 {pause.Line} 行" +
            Environment.NewLine +
            string.Join(
                Environment.NewLine,
                pause.Variables.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => $"{pair.Key} = {DisplayValue(pair.Value)}"));
    }

    private int CursorLine() =>
        CurrentDocument is { StructuredView: not null } document
            ? document.Editor.Document.GetLineByOffset(
                Math.Clamp(document.Editor.CaretOffset, 0, document.Editor.Document.TextLength))
                .LineNumber
            : 0;

    private static string DisplayValue(object? value) => value switch
    {
        null => "空",
        string text => "\"" + text + "\"",
        System.Collections.IEnumerable sequence => "[" +
            string.Join(", ", sequence.Cast<object?>().Take(12).Select(DisplayValue)) + "]",
        _ => Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "空"
    };

    private bool ContinueOrStartDebug()
    {
        if (_debugProbe is null)
        {
            _ = DebugAsync(YyDebugMode.StepInto);
            return true;
        }

        if (_debugPause is not null)
        {
            _debugPause = null;
            _debugProbe.Continue();
            SetStatus("调试继续…");
        }

        return true;
    }

    private void StopDebug()
    {
        _debugProbe?.Stop();
        _debugCancellation?.Cancel();
        _debugPause = null;
        SetStatus("调试已停止");
    }
}

public sealed partial class MainWindow
{
    private readonly List<string> _watchNames = [];

    private void OnAddWatch(object? sender, RoutedEventArgs e)
    {
        var name = WatchInputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name) ||
            _watchNames.Contains(name, StringComparer.Ordinal))
        {
            return;
        }

        _watchNames.Add(name);
        WatchInputBox.Text = string.Empty;
        RefreshWatches();
    }

    private void OnRemoveWatch(object? sender, RoutedEventArgs e)
    {
        if (WatchList.SelectedItem is string name)
        {
            _watchNames.Remove(name);
            RefreshWatches();
        }
    }

    private void ShowDebugVariables(IReadOnlyDictionary<string, object?>? variables)
    {
        VariableList.ItemsSource = variables is null
            ? Array.Empty<string>()
            : variables.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => $"{pair.Key} = {DisplayValue(pair.Value)}")
                .ToArray();
        RefreshWatches(variables);
    }

    private void RefreshWatches(IReadOnlyDictionary<string, object?>? variables = null)
    {
        WatchList.ItemsSource = _watchNames
            .Select(name => variables is not null && variables.TryGetValue(name, out var value)
                ? $"{name} = {DisplayValue(value)}"
                : $"{name} = （运行暂停时显示）")
            .ToArray();
    }
}
