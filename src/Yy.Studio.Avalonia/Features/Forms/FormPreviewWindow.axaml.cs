using System.Collections.Concurrent;
using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using Yy.Compiler;
using Yy.Compiler.Forms;
using Yy.Language;
using Yy.Language.Structured;

namespace Yy.Studio.Avalonia.Features.Forms;

/// <summary>
/// 设计预览窗口:按 .yyx 模型渲染窗体，不带设计器装饰；按钮/编辑框/选项/下拉框/
/// 列表框事件通过 YyRunner 执行同名 .yyc 中的用户代码，并把控件值写回界面。
/// </summary>
public partial class FormPreviewWindow : Window
{
    private readonly YyFormModel _form;
    private readonly string? _formPath;
    private readonly string? _projectDirectory;
    private readonly string? _inlineCode;
    private readonly Dictionary<YyControlModel, Control> _elements = [];
    private readonly Dictionary<string, string> _controlTextBaseline =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _controlVariables =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _controlVariablesById =
        new(StringComparer.Ordinal);

    private CancellationTokenSource? _eventCancellation;
    private string _formTitleBaseline = string.Empty;
    private bool _closeEventHandled;
    private bool _allowClose;
    private bool _updatingControls;

    /// <summary>Avalonia XAML 运行时加载器要求公开无参构造。</summary>
    public FormPreviewWindow()
        : this(new YyFormModel())
    {
    }

    public FormPreviewWindow(
        YyFormModel sourceForm,
        string? formPath = null,
        string? inlineCode = null)
    {
        InitializeComponent();
        var form = sourceForm.Clone();
        _form = form;
        _formPath = formPath;
        _projectDirectory = YyResourceIndex.FindProjectDirectory(formPath);
        _inlineCode = inlineCode;
        ConfigureWindow(form);
        BuildFormContent(form);

        Opened += async (_, _) =>
        {
            ApplyStartWindowState(form);
            ApplyDataBindings();
            await RunFormEventAsync(form.LoadEvent, "窗体载入");
            await RunFormEventAsync(form.OpenEvent, "窗体打开");
        };
        Closing += OnWindowClosing;
        Closed += (_, _) => _eventCancellation?.Cancel();
    }

    /// <summary>事件输出区当前文本。</summary>
    public string EventStatus => EventOutput.Text ?? string.Empty;

    /// <summary>是否有事件正在执行。</summary>
    public bool EventRunning => _eventCancellation is not null;

    /// <summary>按控件名查找预览元素。</summary>
    public Control? FindControlElement(string controlName) =>
        _form.Controls.FirstOrDefault(control =>
            string.Equals(control.Name, controlName, StringComparison.Ordinal)) is { } model &&
        _elements.TryGetValue(model, out var element)
            ? element
            : null;

    /// <summary>模拟用户单击按钮(走真实 Click 事件链)。</summary>
    public bool InvokeControlEvent(string controlName)
    {
        if (FindControlElement(controlName) is Button button)
        {
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            return true;
        }

        return false;
    }

    /// <summary>读取控件当前文本。</summary>
    public string GetControlText(string controlName) =>
        FindControlElement(controlName) is { } element
            ? YyFormRenderer.GetElementText(element)
            : string.Empty;

    /// <summary>以代码方式改写控件文本，并同步预览模型。</summary>
    public void SetControlText(string controlName, string text)
    {
        var model = _form.Controls.FirstOrDefault(control =>
            string.Equals(control.Name, controlName, StringComparison.Ordinal));
        if (model is null || !_elements.TryGetValue(model, out var element))
        {
            return;
        }

        model.Text = text;
        YyFormRenderer.SetElementText(element, text);
    }

    /// <summary>让主窗口按事件名直接执行预览事件。</summary>
    public Task RunEventAsync(string eventName, string context) =>
        RunFormEventAsync(eventName, context);

    /// <summary>按拥有者显示预览；未提供拥有者时独立显示。</summary>
    public void ShowPreview(Window? owner = null)
    {
        if (owner is null)
        {
            Show();
        }
        else
        {
            Show(owner);
        }
    }

    private void ConfigureWindow(YyFormModel form)
    {
        Title = form.Title;
        Width = form.Width;
        Height = form.Height;
        MinWidth = Math.Min(form.Width, 320);
        MinHeight = Math.Min(form.Height, 240);
        Background = YyFormRenderer.Brush(form.Background);
        FontFamily = new FontFamily("Microsoft YaHei UI");
        WindowStartupLocation = form.StartPosition == "居中显示"
            ? WindowStartupLocation.CenterOwner
            : WindowStartupLocation.Manual;

        var icon = YyFormRenderer.LoadImageSource(form.Icon, _projectDirectory);
        if (icon is not null)
        {
            Icon = new WindowIcon(icon);
        }

        if (form.BorderStyle == "无边框")
        {
            SystemDecorations = SystemDecorations.None;
            CanResize = false;
            CanMinimize = false;
            CanMaximize = false;
            return;
        }

        var resizable = form.BorderStyle == "可调边框";
        CanResize = resizable && form.MaximizeBox;
        CanMinimize = resizable && form.MinimizeBox;
        CanMaximize = resizable && form.MaximizeBox;
    }

    private void ApplyStartWindowState(YyFormModel form)
    {
        if (form.StartPosition == "最小化")
        {
            WindowState = WindowState.Minimized;
        }
        else if (form.StartPosition == "最大化")
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void BuildFormContent(YyFormModel form)
    {
        FormCanvas.Width = form.Width;
        FormCanvas.Height = form.Height;
        FormCanvas.Background = YyFormRenderer.Brush(form.Background);
        for (var index = 0; index < form.Controls.Count; index++)
        {
            var control = form.Controls[index];
            var element = YyFormRenderer.Create(control, _projectDirectory);
            Canvas.SetLeft(element, control.X);
            Canvas.SetTop(element, control.Y);
            element.ZIndex = index;
            FormCanvas.Children.Add(element);
            _elements[control] = element;
            WireControlEvents(control, element);
        }
    }

    private void WireControlEvents(YyControlModel control, Control element)
    {
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

        if (element is ToggleButton toggle &&
            !string.IsNullOrWhiteSpace(control.CheckedChangedEvent))
        {
            toggle.IsCheckedChanged += async (_, _) => await RunFormEventAsync(
                control.CheckedChangedEvent,
                $"控件“{control.Name}”选中改变");
        }

        if (element is ComboBox comboBox &&
            !string.IsNullOrWhiteSpace(control.SelectionChangedEvent))
        {
            comboBox.SelectionChanged += async (_, _) => await RunFormEventAsync(
                control.SelectionChangedEvent,
                $"控件“{control.Name}”选择改变");
        }

        if (element is ListBox listBox &&
            !string.IsNullOrWhiteSpace(control.SelectionChangedEvent))
        {
            listBox.SelectionChanged += async (_, _) => await RunFormEventAsync(
                control.SelectionChangedEvent,
                $"控件“{control.Name}”选择改变");
        }
    }

    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
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
            EventOutput.Text = "已有事件正在执行。";
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
                    EventOutput.Text = "窗体尚未保存，无法定位用户代码文件。";
                    return;
                }

                if (!File.Exists(codePath))
                {
                    EventOutput.Text = $"找不到用户代码文件：{Path.GetFileName(codePath)}";
                    return;
                }

                if (!string.Equals(
                        Path.GetExtension(codePath),
                        ".yyc",
                        StringComparison.OrdinalIgnoreCase))
                {
                    EventOutput.Text = "窗体用户代码必须使用 .yyc 格式。";
                    return;
                }

                codeText = File.ReadAllText(codePath, new UTF8Encoding(false, true));
            }

            code = codeText;
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or YyCodeFormatException)
        {
            EventOutput.Text = $"读取用户代码文件失败：{exception.Message}";
            return;
        }

        var source = BuildEventSource(code, eventName);
        var output = new ConcurrentQueue<string>();
        _eventCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        EventOutput.Text = $"正在执行 {context}：{eventName}…";
        try
        {
            var result = await Task.Run(() => YyRunner.RunSource(
                source,
                codePath,
                output.Enqueue,
                _eventCancellation.Token,
                controlResolver: ResolveControlById));
            ApplyControlValues(result.Variables);
            var lines = output.ToArray();
            EventOutput.Text = lines.Length == 0
                ? $"✓ {context}：{eventName} 执行完成"
                : string.Join("  |  ", lines);
        }
        catch (OperationCanceledException)
        {
            EventOutput.Text = $"✕ 事件执行超过 10 秒，已停止：{eventName}";
        }
        catch (YyDiagnostic diagnostic)
        {
            EventOutput.Text =
                $"✕ {Path.GetFileName(diagnostic.SourceName)} 第 {diagnostic.Line} 行：" +
                diagnostic.Message;
            ToolTip.SetTip(EventOutput, diagnostic.Hint);
        }
        catch (YyRuntimeError error)
        {
            EventOutput.Text = $"✕ 运行时错误，第 {error.Line} 行：{error.Message}";
            ToolTip.SetTip(EventOutput, error.Detail);
        }
        catch (Exception exception)
        {
            EventOutput.Text = $"✕ 事件执行失败：{exception.Message}";
        }
        finally
        {
            _eventCancellation.Dispose();
            _eventCancellation = null;
        }
    }

    private string BuildEventSource(string code, string eventName)
    {
        var builder = new StringBuilder();
        builder.AppendLine("' 预览器生成的窗体上下文");
        builder.AppendLine($"结构体 {_form.Name}界面");
        builder.AppendLine("    名称: 文本");
        builder.AppendLine("    标题: 文本");
        builder.AppendLine("    宽: 数字");
        builder.AppendLine("    高: 数字");
        builder.AppendLine("    参数: 文本");
        builder.AppendLine("    结果: 文本");
        foreach (var control in _form.Controls)
        {
            builder.AppendLine($"    {control.Name}: 对象");
        }

        builder.AppendLine("结束结构体");
        builder.AppendLine();
        _controlVariables.Clear();
        _controlVariablesById.Clear();
        foreach (var control in _form.Controls)
        {
            var element = _elements[control];
            var selected = element is ToggleButton toggle && toggle.IsChecked == true;
            var baseline = YyFormRenderer.GetElementText(element);
            _controlTextBaseline[control.Name] = baseline;
            var value = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["名称"] = control.Name,
                ["标题"] = baseline,
                ["文本"] = baseline,
                ["图片"] = control.Picture,
                ["启用"] = element.IsEnabled,
                ["可见"] = element.IsVisible,
                ["选中"] = selected
            };
            _controlVariables[control.Name] = value;
            _controlVariablesById[control.Name] = value;
            var controlId = YyResourceIndex.ControlId(_form.Name, control.Name);
            _controlVariablesById[controlId] = value;
            builder.AppendLine(
                $"变量 {control.Name}: 对象 = 取控件({Quote(controlId)})");
        }

        var window = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["名称"] = _form.Name,
            ["标题"] = Title,
            ["宽"] = _form.Width,
            ["高"] = _form.Height,
            ["参数"] = string.Empty,
            ["结果"] = string.Empty
        };
        _formTitleBaseline = Title ?? string.Empty;
        foreach (var control in _form.Controls)
        {
            window[control.Name] = _controlVariables[control.Name];
        }

        _controlVariables["窗口"] = window;
        _controlVariables[_form.Name] = window;
        _controlVariablesById[_form.Name] = window;
        var formId = YyResourceIndex.FormId(_form.Name);
        _controlVariablesById[formId] = window;
        builder.AppendLine($"变量 窗口: 对象 = 取控件({Quote(formId)})");
        builder.AppendLine(
            $"变量 {_form.Name}: {_form.Name}界面 = 取控件({Quote(formId)})");
        builder.AppendLine(code);
        builder.AppendLine(eventName + "()");
        return builder.ToString();
    }

    private object? ResolveControlById(string id) =>
        _controlVariablesById.TryGetValue(id, out var value) ? value : null;

    private void ApplyControlValues(IReadOnlyDictionary<string, object?> variables)
    {
        _updatingControls = true;
        try
        {
            if (variables.TryGetValue(_form.Name, out var formValue) &&
                formValue is IReadOnlyDictionary<string, object?> formValues &&
                formValues.TryGetValue("标题", out var formTitleValue))
            {
                var title = formTitleValue?.ToString() ?? string.Empty;
                if (!string.Equals(title, _formTitleBaseline, StringComparison.Ordinal))
                {
                    Title = title;
                    _form.Title = title;
                    _formTitleBaseline = title;
                }
            }

            foreach (var control in _form.Controls)
            {
                if (!variables.TryGetValue(control.Name, out var value) ||
                    value is not IReadOnlyDictionary<string, object?> values)
                {
                    continue;
                }

                // 标题/文本 是同一属性的两个别名;偏离初始值的键才代表代码写入了新文本。
                _controlTextBaseline.TryGetValue(control.Name, out var baselineText);
                baselineText ??= string.Empty;
                string? newText = null;
                if (values.TryGetValue("标题", out var titleValue) &&
                    !string.Equals(
                        titleValue?.ToString() ?? string.Empty,
                        baselineText,
                        StringComparison.Ordinal))
                {
                    newText = titleValue?.ToString() ?? string.Empty;
                }
                else if (values.TryGetValue("文本", out var textValue) &&
                         !string.Equals(
                             textValue?.ToString() ?? string.Empty,
                             baselineText,
                             StringComparison.Ordinal))
                {
                    newText = textValue?.ToString() ?? string.Empty;
                }

                if (newText is not null)
                {
                    control.Text = newText;
                    YyFormRenderer.SetElementText(_elements[control], newText);
                }

                if (values.TryGetValue("图片", out var pictureValue))
                {
                    var picture = pictureValue?.ToString() ?? string.Empty;
                    if (!string.Equals(picture, control.Picture, StringComparison.Ordinal))
                    {
                        control.Picture = picture;
                        YyFormRenderer.SetElementPicture(
                            _elements[control],
                            picture,
                            _projectDirectory);
                    }
                }

                if (values.TryGetValue("启用", out var enabledValue) &&
                    enabledValue is bool enabled)
                {
                    control.IsEnabled = enabled;
                    _elements[control].IsEnabled = enabled;
                }

                if (values.TryGetValue("可见", out var visibleValue) &&
                    visibleValue is bool visible)
                {
                    control.IsVisible = visible;
                    _elements[control].IsVisible = visible;
                }

                if (values.TryGetValue("选中", out var selectedValue) &&
                    selectedValue is bool selected &&
                    _elements[control] is ToggleButton toggle)
                {
                    toggle.IsChecked = selected;
                }
            }
        }
        finally
        {
            _updatingControls = false;
        }
    }

    /// <summary>预览载入时从工作区“数据.json”读取绑定表的首行，填充绑定了数据列的控件。</summary>
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
            using var document = System.Text.Json.JsonDocument.Parse(
                File.ReadAllText(dataPath));
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
                    System.Text.Json.JsonValueKind.Array
                        when tableValue.GetArrayLength() > 0 => tableValue[0],
                    System.Text.Json.JsonValueKind.Object => tableValue,
                    _ => default
                };
                if (row.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    row.TryGetProperty(control.DataBindingColumn, out var cell))
                {
                    var text = cell.ToString();
                    control.Text = text;
                    YyFormRenderer.SetElementText(_elements[control], text);
                }
            }
        }
        catch (Exception exception) when (
            exception is IOException
                or UnauthorizedAccessException
                or System.Text.Json.JsonException)
        {
            EventOutput.Text = $"数据.json 读取失败：{exception.Message}";
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

    private static string Quote(string value)
    {
        return "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) + "\"";
    }
}
