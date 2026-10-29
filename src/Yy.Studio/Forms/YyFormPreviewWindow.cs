using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yy.Language;

namespace Yy.Studio.Forms;

public sealed class YyFormPreviewWindow : Window
{
    private readonly YyFormModel _form;
    private readonly string? _formPath;
    private readonly Dictionary<YyControlModel, FrameworkElement> _elements = [];
    private readonly TextBlock _eventOutput;
    private CancellationTokenSource? _eventCancellation;
    private bool _closeEventHandled;
    private bool _allowClose;
    private bool _updatingControls;

    public YyFormPreviewWindow(YyFormModel form, string? formPath = null)
    {
        _form = form;
        _formPath = formPath;
        Title = form.Title;
        Width = form.Width;
        Height = form.Height;
        MinWidth = Math.Min(form.Width, 320);
        MinHeight = Math.Min(form.Height, 240);
        Background = YyFormRenderer.Brush(form.Background);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Closed += (_, _) => _eventCancellation?.Cancel();
        Loaded += async (_, _) => await RunFormEventAsync(form.LoadEvent, "窗体载入");
        Closing += OnWindowClosing;

        var canvas = new Canvas
        {
            Width = form.Width,
            Height = form.Height,
            Background = YyFormRenderer.Brush(form.Background),
            ClipToBounds = true
        };

        foreach (var control in form.Controls)
        {
            var element = YyFormRenderer.Create(control);
            Canvas.SetLeft(element, control.X);
            Canvas.SetTop(element, control.Y);
            canvas.Children.Add(element);
            _elements[control] = element;

            if (element is Button button && !string.IsNullOrWhiteSpace(control.ClickEvent))
            {
                button.Click += async (_, _) => await RunFormEventAsync(
                    control.ClickEvent,
                    $"控件“{control.Name}”单击");
            }

            if (element is TextBox textBox && !string.IsNullOrWhiteSpace(control.TextChangedEvent))
            {
                textBox.TextChanged += async (_, _) => await RunFormEventAsync(
                    control.TextChangedEvent,
                    $"控件“{control.Name}”文本改变");
            }

            if (element is CheckBox checkBox && !string.IsNullOrWhiteSpace(control.CheckedChangedEvent))
            {
                checkBox.Checked += async (_, _) => await RunFormEventAsync(
                    control.CheckedChangedEvent,
                    $"控件“{control.Name}”选中改变");
                checkBox.Unchecked += async (_, _) => await RunFormEventAsync(
                    control.CheckedChangedEvent,
                    $"控件“{control.Name}”选中改变");
            }

            if (element is ComboBox comboBox && !string.IsNullOrWhiteSpace(control.SelectionChangedEvent))
            {
                comboBox.SelectionChanged += async (_, _) => await RunFormEventAsync(
                    control.SelectionChangedEvent,
                    $"控件“{control.Name}”选择改变");
            }

            if (element is ListBox listBox && !string.IsNullOrWhiteSpace(control.SelectionChangedEvent))
            {
                listBox.SelectionChanged += async (_, _) => await RunFormEventAsync(
                    control.SelectionChangedEvent,
                    $"控件“{control.Name}”选择改变");
            }
        }

        _eventOutput = new TextBlock
        {
            Text = "事件输出会显示在这里。",
            Foreground = new SolidColorBrush(Color.FromRgb(102, 119, 139)),
            FontSize = 11,
            Margin = new Thickness(8, 4, 8, 4),
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var contentGrid = new Grid();
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(219, 226, 234)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = _eventOutput
        };
        var scrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = canvas
        };
        Grid.SetRow(scrollViewer, 0);
        contentGrid.Children.Add(scrollViewer);
        Grid.SetRow(statusBar, 1);
        contentGrid.Children.Add(statusBar);
        Content = contentGrid;
    }

    private async void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose ||
            _closeEventHandled ||
            string.IsNullOrWhiteSpace(_form.CloseEvent))
        {
            _eventCancellation?.Cancel();
            return;
        }

        e.Cancel = true;
        _closeEventHandled = true;
        await RunFormEventAsync(_form.CloseEvent, "窗体关闭");
        _allowClose = true;
        Close();
    }

    private async Task RunFormEventAsync(string? eventName, string context)
    {
        if (_updatingControls || string.IsNullOrWhiteSpace(eventName))
        {
            return;
        }

        if (_eventCancellation is not null)
        {
            _eventOutput.Text = "已有事件正在执行。";
            return;
        }

        string? codePath;
        string code;
        try
        {
            codePath = ResolveCodePath();
            if (codePath is null)
            {
                _eventOutput.Text = "窗体尚未保存，无法定位代码后置文件。";
                return;
            }

            if (!File.Exists(codePath))
            {
                _eventOutput.Text = $"找不到代码后置文件：{Path.GetFileName(codePath)}";
                return;
            }

            code = File.ReadAllText(codePath, new UTF8Encoding(false, true));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _eventOutput.Text = $"读取代码后置文件失败：{exception.Message}";
            return;
        }

        var source = BuildEventSource(code, eventName);
        var output = new ConcurrentQueue<string>();
        _eventCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _eventOutput.Text = $"正在执行 {context}：{eventName}…";
        try
        {
            var result = await Task.Run(() => YyRunner.RunSource(
                source,
                codePath,
                output.Enqueue,
                _eventCancellation.Token));
            ApplyControlValues(result.Variables);
            var lines = output.ToArray();
            _eventOutput.Text = lines.Length == 0
                ? $"✓ {context}：{eventName} 执行完成"
                : string.Join("  |  ", lines);
        }
        catch (OperationCanceledException)
        {
            _eventOutput.Text = $"✕ 事件执行超过 10 秒，已停止：{eventName}";
        }
        catch (YyDiagnostic diagnostic)
        {
            _eventOutput.Text = $"✕ {Path.GetFileName(diagnostic.SourceName)} 第 {diagnostic.Line} 行：{diagnostic.Message}";
            _eventOutput.ToolTip = diagnostic.Hint;
        }
        catch (YyRuntimeError error)
        {
            _eventOutput.Text = $"✕ 运行时错误，第 {error.Line} 行：{error.Message}";
            _eventOutput.ToolTip = error.Detail;
        }
        catch (Exception exception)
        {
            _eventOutput.Text = $"✕ 事件执行失败：{exception.Message}";
        }
        finally
        {
            StudioTrace.Write($"Form event {eventName}: {_eventOutput.Text}");
            _eventCancellation.Dispose();
            _eventCancellation = null;
        }
    }

    private string BuildEventSource(string code, string eventName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("' 预览器生成的窗体上下文");
        foreach (var control in _form.Controls)
        {
            var element = _elements[control];
            var selected = element switch
            {
                CheckBox checkBox => checkBox.IsChecked == true,
                _ => false
            };
            builder.AppendLine(
                $"变量 {control.Name}: 对象 = {{名称: {Quote(control.Name)}, 文本: {Quote(GetElementText(element))}, 启用: {BooleanText(element.IsEnabled)}, 可见: {BooleanText(element.Visibility == Visibility.Visible)}, 选中: {BooleanText(selected)}}}");
        }

        builder.AppendLine(
            $"变量 窗口: 对象 = {{名称: {Quote(_form.Name)}, 标题: {Quote(Title)}, 宽: {_form.Width.ToString(System.Globalization.CultureInfo.InvariantCulture)}, 高: {_form.Height.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}");
        builder.AppendLine(code);
        builder.AppendLine(eventName + "()");
        return builder.ToString();
    }

    private void ApplyControlValues(IReadOnlyDictionary<string, object?> variables)
    {
        _updatingControls = true;
        try
        {
            foreach (var control in _form.Controls)
            {
                if (!variables.TryGetValue(control.Name, out var value) ||
                    value is not IReadOnlyDictionary<string, object?> values)
                {
                    continue;
                }

                if (values.TryGetValue("文本", out var textValue))
                {
                    var text = textValue?.ToString() ?? string.Empty;
                    control.Text = text;
                    SetElementText(_elements[control], text);
                }

                if (values.TryGetValue("启用", out var enabledValue) && enabledValue is bool enabled)
                {
                    control.IsEnabled = enabled;
                    _elements[control].IsEnabled = enabled;
                }

                if (values.TryGetValue("可见", out var visibleValue) && visibleValue is bool visible)
                {
                    control.IsVisible = visible;
                    _elements[control].Visibility = visible ? Visibility.Visible : Visibility.Hidden;
                }

                if (values.TryGetValue("选中", out var selectedValue) &&
                    selectedValue is bool selected &&
                    _elements[control] is CheckBox checkBox)
                {
                    checkBox.IsChecked = selected;
                }
            }
        }
        finally
        {
            _updatingControls = false;
        }
    }

    private string? ResolveCodePath()
    {
        if (string.IsNullOrWhiteSpace(_form.CodeFile) ||
            string.IsNullOrWhiteSpace(_formPath))
        {
            return null;
        }

        var formDirectory = Path.GetDirectoryName(Path.GetFullPath(_formPath));
        return formDirectory is null
            ? null
            : Path.GetFullPath(Path.Combine(formDirectory, _form.CodeFile));
    }

    private static string GetElementText(FrameworkElement element)
    {
        return element switch
        {
            TextBox textBox => textBox.Text,
            Button button => button.Content?.ToString() ?? string.Empty,
            TextBlock textBlock => textBlock.Text,
            CheckBox checkBox => checkBox.Content?.ToString() ?? string.Empty,
            GroupBox groupBox => groupBox.Header?.ToString() ?? string.Empty,
            _ => string.Empty
        };
    }

    private static void SetElementText(FrameworkElement element, string text)
    {
        switch (element)
        {
            case TextBox textBox:
                textBox.Text = text;
                break;
            case Button button:
                button.Content = text;
                break;
            case TextBlock textBlock:
                textBlock.Text = text;
                break;
            case CheckBox checkBox:
                checkBox.Content = text;
                break;
            case GroupBox groupBox:
                groupBox.Header = text;
                break;
        }
    }

    private static string Quote(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) + "\"";
    }

    private static string BooleanText(bool value) => value ? "真" : "假";
}
