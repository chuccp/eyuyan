using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Yy.Language;
using Yy.Language.Structured;

namespace Yy.Studio.Forms;

public sealed class YyFormPreviewWindow : Window
{
    private readonly YyFormModel _form;
    private readonly string? _formPath;
    private readonly string? _inlineCode;
    private readonly Dictionary<YyControlModel, FrameworkElement> _elements = [];
    private readonly Dictionary<string, string> _controlTextBaseline = [];
    private readonly TextBlock _eventOutput;
    private CancellationTokenSource? _eventCancellation;
    private bool _closeEventHandled;
    private bool _allowClose;
    private bool _updatingControls;

    public YyFormPreviewWindow(YyFormModel form, string? formPath = null, string? inlineCode = null)
    {
        _form = form;
        _formPath = formPath;
        _inlineCode = inlineCode;
        Title = form.Title;
        Width = form.Width;
        Height = form.Height;
        MinWidth = Math.Min(form.Width, 320);
        MinHeight = Math.Min(form.Height, 240);
        Background = YyFormRenderer.Brush(form.Background);
        WindowStartupLocation = form.StartPosition == "居中显示"
            ? WindowStartupLocation.CenterOwner
            : WindowStartupLocation.Manual;
        if (form.BorderStyle == "无边框")
        {
            WindowStyle = WindowStyle.None;
        }
        else
        {
            ResizeMode = form.BorderStyle == "可调边框"
                ? ResizeMode.CanResize
                : ResizeMode.NoResize;
            if (form.BorderStyle == "可调边框" && !form.MaximizeBox && form.MinimizeBox)
            {
                ResizeMode = ResizeMode.CanMinimize;
            }
        }

        if (!form.MinimizeBox && !form.MaximizeBox && form.BorderStyle == "可调边框")
        {
            ResizeMode = ResizeMode.NoResize;
        }

        SourceInitialized += (_, _) =>
        {
            if (form.StartPosition == "最小化")
            {
                WindowState = WindowState.Minimized;
            }
            else if (form.StartPosition == "最大化")
            {
                WindowState = WindowState.Maximized;
            }
        };
        FontFamily = new FontFamily("Microsoft YaHei UI");
        Closed += (_, _) => _eventCancellation?.Cancel();
        Loaded += async (_, _) =>
        {
            ApplyDataBindings();
            await RunFormEventAsync(form.LoadEvent, "窗体载入");
        };
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

            if (element is PasswordBox passwordBox && !string.IsNullOrWhiteSpace(control.TextChangedEvent))
            {
                passwordBox.PasswordChanged += async (_, _) => await RunFormEventAsync(
                    control.TextChangedEvent,
                    $"控件“{control.Name}”文本改变");
            }

            if (element is System.Windows.Controls.Primitives.ToggleButton checkBox &&
                !string.IsNullOrWhiteSpace(control.CheckedChangedEvent))
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

        string? codePath = null;
        string code;
        try
        {
            string codeText;
            if (_inlineCode is not null)
            {
                // 内存窗体:直接使用编辑器中的用户代码。
                codeText = _inlineCode;
            }
            else
            {
                codePath = ResolveCodePath();
                if (codePath is null)
                {
                    _eventOutput.Text = "窗体尚未保存，无法定位用户代码文件。";
                    return;
                }

                if (!File.Exists(codePath))
                {
                    _eventOutput.Text = $"找不到用户代码文件：{Path.GetFileName(codePath)}";
                    return;
                }

                if (!string.Equals(Path.GetExtension(codePath), ".yyc", StringComparison.OrdinalIgnoreCase))
                {
                    _eventOutput.Text = "窗体用户代码必须使用 .yyc 格式。";
                    return;
                }

                codeText = File.ReadAllText(codePath, new UTF8Encoding(false, true));
            }

            code = YyCodeDocumentConverter.ToSource(YyCodeSerializer.Deserialize(codeText));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or YyCodeFormatException)
        {
            _eventOutput.Text = $"读取用户代码文件失败：{exception.Message}";
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
                System.Windows.Controls.Primitives.ToggleButton checkBox => checkBox.IsChecked == true,
                _ => false
            };
            var baseline = GetElementText(element);
            _controlTextBaseline[control.Name] = baseline;
            builder.AppendLine(
                $"变量 {control.Name}: 对象 = {{名称: {Quote(control.Name)}, 标题: {Quote(baseline)}, 文本: {Quote(baseline)}, 图片: {Quote(control.Picture)}, 启用: {BooleanText(element.IsEnabled)}, 可见: {BooleanText(element.Visibility == Visibility.Visible)}, 选中: {BooleanText(selected)}}}");
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

                // 标题/文本 是同一属性的两个别名;哪个键偏离初始值即为被代码改写的键,
                // 否则旧别名的残留值会覆盖新值。
                _controlTextBaseline.TryGetValue(control.Name, out var baselineText);
                baselineText ??= string.Empty;
                string? newText = null;
                if (values.TryGetValue("标题", out var titleValue) &&
                    !string.Equals(titleValue?.ToString() ?? string.Empty, baselineText, StringComparison.Ordinal))
                {
                    newText = titleValue?.ToString() ?? string.Empty;
                }
                else if (values.TryGetValue("文本", out var textValue) &&
                         !string.Equals(textValue?.ToString() ?? string.Empty, baselineText, StringComparison.Ordinal))
                {
                    newText = textValue?.ToString() ?? string.Empty;
                }

                if (newText is not null)
                {
                    control.Text = newText;
                    SetElementText(_elements[control], newText);
                }

                if (values.TryGetValue("图片", out var pictureValue))
                {
                    var picture = pictureValue?.ToString() ?? string.Empty;
                    if (!string.Equals(picture, control.Picture, StringComparison.Ordinal))
                    {
                        control.Picture = picture;
                        YyFormRenderer.SetElementPicture(_elements[control], picture);
                    }
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

    /// <summary>预览载入时从工作区“数据.json”读取绑定表的首行,填充绑定了数据列的控件。</summary>
    private void ApplyDataBindings()
    {
        if (string.IsNullOrWhiteSpace(_formPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(_formPath));
        if (directory is null)
        {
            return;
        }

        var dataPath = Path.Combine(directory, "数据.json");
        if (!File.Exists(dataPath))
        {
            return;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(dataPath));
            var root = document.RootElement;
            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
            {
                return;
            }

            foreach (var control in _form.Controls)
            {
                if (string.IsNullOrWhiteSpace(control.DataBindingTable) ||
                    string.IsNullOrWhiteSpace(control.DataBindingColumn) ||
                    !root.TryGetProperty(control.DataBindingTable, out var tableValue))
                {
                    continue;
                }

                var row = tableValue.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.Array when tableValue.GetArrayLength() > 0 => tableValue[0],
                    System.Text.Json.JsonValueKind.Object => tableValue,
                    _ => default
                };
                if (row.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    row.TryGetProperty(control.DataBindingColumn, out var cell))
                {
                    var text = cell.ToString();
                    control.Text = text;
                    SetElementText(_elements[control], text);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            _eventOutput.Text = $"数据.json 读取失败：{exception.Message}";
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
            PasswordBox passwordBox => passwordBox.Password,
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
            case PasswordBox passwordBox:
                passwordBox.Password = text;
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

    /// <summary>MCP 接口:事件输出区当前文本。</summary>
    internal string EventStatus => _eventOutput.Text;

    /// <summary>MCP 接口:是否有事件正在执行。</summary>
    internal bool EventRunning => _eventCancellation is not null;

    /// <summary>MCP 接口:按控件名查找预览元素。</summary>
    internal FrameworkElement? FindControlElement(string controlName) =>
        _form.Controls.FirstOrDefault(control =>
                string.Equals(control.Name, controlName, StringComparison.Ordinal)) is { } model &&
        _elements.TryGetValue(model, out var element)
            ? element
            : null;

    /// <summary>MCP 接口:模拟用户单击按钮(走真实 Click 事件链)。</summary>
    internal bool InvokeControlEvent(string controlName)
    {
        if (FindControlElement(controlName) is Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        }

        return false;
    }

    /// <summary>MCP 接口:读取控件当前文本。</summary>
    internal string GetControlText(string controlName) =>
        FindControlElement(controlName) switch
        {
            TextBox textBox => textBox.Text,
            PasswordBox passwordBox => passwordBox.Password,
            Button button => button.Content?.ToString() ?? string.Empty,
            TextBlock textBlock => textBlock.Text,
            CheckBox checkBox => checkBox.Content?.ToString() ?? string.Empty,
            GroupBox groupBox => groupBox.Header?.ToString() ?? string.Empty,
            _ => string.Empty
        };

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
