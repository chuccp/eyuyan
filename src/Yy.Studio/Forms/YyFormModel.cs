namespace Yy.Studio.Forms;

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
    public List<YyControlModel> Controls { get; } = [];
}

public sealed class YyControlModel
{
    public required string Type { get; init; }
    public string Name { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
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
}

public static class YyControlTypes
{
    public const string Button = "按钮";
    public const string Label = "文本";
    public const string TextBox = "输入框";
    public const string CheckBox = "复选框";
    public const string ComboBox = "下拉框";
    public const string ListBox = "列表框";
    public const string GroupBox = "分组框";

    public static IReadOnlyList<string> All { get; } =
    [
        Button,
        Label,
        TextBox,
        CheckBox,
        ComboBox,
        ListBox,
        GroupBox
    ];

    public static YyControlModel Create(string type, int sequence)
    {
        if (!All.Contains(type, StringComparer.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "不支持的窗体控件类型。");
        }

        var model = new YyControlModel
        {
            Type = type,
            Name = type.Replace("文本", "标签", StringComparison.Ordinal) + sequence
        };

        switch (type)
        {
            case Button:
                model.Text = "按钮 " + sequence;
                model.Width = 116;
                model.Height = 38;
                model.Background = "#2F7F6E";
                model.Foreground = "#FFFFFF";
                break;
            case Label:
                model.Text = "标签 " + sequence;
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
                model.Text = "选项 " + sequence;
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
                model.Text = "分组 " + sequence;
                model.Width = 280;
                model.Height = 180;
                model.Background = "#F8FAFC";
                break;
        }

        return model;
    }
}

