namespace Yy.Compiler.Forms;

public sealed class YyFormModel
{
    public string Name { get; set; } = "主窗口";
    public string CodeFile { get; set; } = string.Empty;
    public string? LoadEvent { get; set; }
    public string? CloseEvent { get; set; }
    public string Title { get; set; } = "易语窗体";
    public double Width { get; set; } = 800;
    public double Height { get; set; } = 520;
    public string Background { get; set; } = "#F4F7FB";
    public string Icon { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;

    /// <summary>边框:可调边框/对话框边框/无边框(官方属性)。</summary>
    public string BorderStyle { get; set; } = "可调边框";

    /// <summary>可否移动。</summary>
    public bool Movable { get; set; } = true;

    /// <summary>初始位置:默认/居中显示/最小化/最大化。</summary>
    public string StartPosition { get; set; } = "默认";

    /// <summary>最小化按钮。</summary>
    public bool MinimizeBox { get; set; } = true;

    /// <summary>最大化按钮。</summary>
    public bool MaximizeBox { get; set; } = true;

    public List<YyControlModel> Controls { get; } = [];

    public YyFormModel Clone()
    {
        var clone = new YyFormModel
        {
            Name = Name,
            CodeFile = CodeFile,
            LoadEvent = LoadEvent,
            CloseEvent = CloseEvent,
            Title = Title,
            Width = Width,
            Height = Height,
            Background = Background,
            Icon = Icon,
            Remark = Remark,
            BorderStyle = BorderStyle,
            Movable = Movable,
            StartPosition = StartPosition,
            MinimizeBox = MinimizeBox,
            MaximizeBox = MaximizeBox
        };
        foreach (var control in Controls)
        {
            clone.Controls.Add(control.Clone());
        }

        return clone;
    }
}

public sealed class YyControlModel
{
    public required string Type { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string? ClickEvent { get; set; }
    public string? TextChangedEvent { get; set; }
    public string? SelectionChangedEvent { get; set; }
    public string? CheckedChangedEvent { get; set; }
    public double X { get; set; } = 40;
    public double Y { get; set; } = 40;
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 36;
    public double FontSize { get; set; } = 14;
    public string Foreground { get; set; } = "#17212B";
    public string Background { get; set; } = "#FFFFFF";
    public bool IsEnabled { get; set; } = true;
    public bool IsVisible { get; set; } = true;
    public bool IsChecked { get; set; }
    public bool ReadOnly { get; set; }
    public bool MultiLine { get; set; }
    public string PasswordChar { get; set; } = string.Empty;
    public int MaxLength { get; set; }
    public string Picture { get; set; } = string.Empty;
    public string? DataBindingTable { get; set; }
    public string? DataBindingColumn { get; set; }

    /// <summary>深复制(设计器剪贴板用)。</summary>
    public YyControlModel Clone() => new()
    {
        Type = Type,
        Name = Name,
        Text = Text,
        Remark = Remark,
        ClickEvent = ClickEvent,
        TextChangedEvent = TextChangedEvent,
        SelectionChangedEvent = SelectionChangedEvent,
        CheckedChangedEvent = CheckedChangedEvent,
        X = X,
        Y = Y,
        Width = Width,
        Height = Height,
        FontSize = FontSize,
        Foreground = Foreground,
        Background = Background,
        IsEnabled = IsEnabled,
        IsVisible = IsVisible,
        IsChecked = IsChecked,
        ReadOnly = ReadOnly,
        MultiLine = MultiLine,
        PasswordChar = PasswordChar,
        MaxLength = MaxLength,
        Picture = Picture,
        DataBindingTable = DataBindingTable,
        DataBindingColumn = DataBindingColumn
    };
}

public static class YyControlTypes
{
    public const string Button = "按钮";
    public const string Label = "文本";
    public const string TextBox = "输入框";
    public const string CheckBox = "复选框";
    public const string RadioButton = "单选框";
    public const string ComboBox = "下拉框";
    public const string ListBox = "列表框";
    public const string GroupBox = "分组框";
    public const string PictureBox = "图片框";

    public static IReadOnlyList<string> All { get; } =
    [
        Button,
        Label,
        TextBox,
        RadioButton,
        CheckBox,
        ComboBox,
        ListBox,
        GroupBox,
        PictureBox
    ];

    /// <summary>官方组件显示名(组件箱/属性夹用),格式内部类型名不变。</summary>
    public static string GetDisplayName(string type) => type switch
    {
        Label => "标签",
        TextBox => "编辑框",
        ComboBox => "组合框",
        _ => type
    };

    /// <summary>官方生成名前缀:标签N/按钮N/编辑框N/组合框N…</summary>
    public static string GetBaseName(string type) => type switch
    {
        Label => "标签",
        TextBox => "编辑框",
        ComboBox => "组合框",
        _ => type
    };

    public static YyControlModel Create(string type, int sequence)
    {
        if (!All.Contains(type, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的窗体控件类型。");
        }

        var model = new YyControlModel
        {
            Type = type,
            Name = GetBaseName(type) + sequence
        };

        switch (type)
        {
            case Button:
                model.Text = "按钮" + sequence;
                model.Width = 116;
                model.Height = 38;
                model.Background = "#ECE9D8";
                model.Foreground = "#000000";
                break;
            case Label:
                model.Text = "标签" + sequence;
                model.Width = 130;
                model.Height = 28;
                model.Background = "Transparent";
                break;
            case TextBox:
                model.Text = string.Empty;
                model.Width = 220;
                model.Height = 36;
                break;
            case CheckBox:
                model.Text = "选项" + sequence;
                model.Width = 150;
                model.Height = 28;
                model.Background = "Transparent";
                break;
            case RadioButton:
                model.Text = "单选" + sequence;
                model.Width = 150;
                model.Height = 28;
                model.Background = "Transparent";
                break;
            case ComboBox:
                model.Text = "选项一,选项二,选项三";
                model.Width = 180;
                model.Height = 34;
                break;
            case ListBox:
                model.Text = "列表项目一,列表项目二,列表项目三";
                model.Width = 200;
                model.Height = 130;
                break;
            case GroupBox:
                model.Text = "分组" + sequence;
                model.Width = 280;
                model.Height = 180;
                model.Background = "#F8FAFC";
                break;
            case PictureBox:
                model.Width = 220;
                model.Height = 150;
                model.Background = "#FFFFFF";
                break;
        }

        return model;
    }
}

