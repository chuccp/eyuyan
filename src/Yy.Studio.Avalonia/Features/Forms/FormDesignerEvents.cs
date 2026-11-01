using Yy.Compiler.Forms;

namespace Yy.Studio.Avalonia.Features.Forms;

/// <summary>设计器可创建的事件入口类型。</summary>
public enum FormDesignerEventKind
{
    Click,
    TextChanged,
    SelectionChanged,
    CheckedChanged,
    FormLoad,
    FormOpen,
    FormClose
}

/// <summary>
/// 事件选择与命名规则。事件处理程序名只保存在内存模型和 .Designer.yyc 关系文件中，
/// 绝不写入 .yyx 界面文件。
/// </summary>
public static class FormDesignerEvents
{
    public const string ClickName = "被单击";
    public const string TextChangedName = "内容被改变";
    public const string SelectionChangedName = "列表项被选择";
    public const string CheckedChangedName = "选中状态被改变";
    public const string FormLoadName = "创建完毕";
    public const string FormOpenName = "窗口打开";
    public const string FormCloseName = "将被销毁";

    public static string DisplayName(FormDesignerEventKind kind) => kind switch
    {
        FormDesignerEventKind.Click => ClickName,
        FormDesignerEventKind.TextChanged => TextChangedName,
        FormDesignerEventKind.SelectionChanged => SelectionChangedName,
        FormDesignerEventKind.CheckedChanged => CheckedChangedName,
        FormDesignerEventKind.FormLoad => FormLoadName,
        FormDesignerEventKind.FormOpen => FormOpenName,
        FormDesignerEventKind.FormClose => FormCloseName,
        _ => kind.ToString()
    };

    public static bool IsFormEvent(FormDesignerEventKind kind) =>
        kind is FormDesignerEventKind.FormLoad
            or FormDesignerEventKind.FormOpen
            or FormDesignerEventKind.FormClose;

    /// <summary>控件支持的事件;标签、分组框和图片框没有可生成的事件入口。</summary>
    public static IReadOnlyList<FormDesignerEventKind> GetKinds(string controlType) =>
        controlType switch
        {
            YyControlTypes.Button => [FormDesignerEventKind.Click],
            YyControlTypes.TextBox => [FormDesignerEventKind.TextChanged],
            YyControlTypes.ComboBox or YyControlTypes.ListBox =>
                [FormDesignerEventKind.SelectionChanged],
            YyControlTypes.CheckBox or YyControlTypes.RadioButton =>
                [FormDesignerEventKind.CheckedChanged],
            _ => []
        };

    /// <summary>双击控件时默认创建的事件。</summary>
    public static bool TryGetDefaultKind(
        string controlType,
        out FormDesignerEventKind kind)
    {
        var kinds = GetKinds(controlType);
        if (kinds.Count == 0)
        {
            kind = default;
            return false;
        }

        kind = kinds[0];
        return true;
    }

    public static string? GetHandler(
        YyControlModel control,
        FormDesignerEventKind kind) => kind switch
    {
        FormDesignerEventKind.Click => control.ClickEvent,
        FormDesignerEventKind.TextChanged => control.TextChangedEvent,
        FormDesignerEventKind.SelectionChanged => control.SelectionChangedEvent,
        FormDesignerEventKind.CheckedChanged => control.CheckedChangedEvent,
        _ => null
    };

    public static void SetHandler(
        YyControlModel control,
        FormDesignerEventKind kind,
        string? handlerName)
    {
        switch (kind)
        {
            case FormDesignerEventKind.Click:
                control.ClickEvent = handlerName;
                break;
            case FormDesignerEventKind.TextChanged:
                control.TextChangedEvent = handlerName;
                break;
            case FormDesignerEventKind.SelectionChanged:
                control.SelectionChangedEvent = handlerName;
                break;
            case FormDesignerEventKind.CheckedChanged:
                control.CheckedChangedEvent = handlerName;
                break;
        }
    }

    public static string? GetHandler(
        YyFormModel form,
        FormDesignerEventKind kind) => kind switch
    {
        FormDesignerEventKind.FormLoad => form.LoadEvent,
        FormDesignerEventKind.FormOpen => form.OpenEvent,
        FormDesignerEventKind.FormClose => form.CloseEvent,
        _ => null
    };

    public static void SetHandler(
        YyFormModel form,
        FormDesignerEventKind kind,
        string? handlerName)
    {
        switch (kind)
        {
            case FormDesignerEventKind.FormLoad:
                form.LoadEvent = handlerName;
                break;
            case FormDesignerEventKind.FormOpen:
                form.OpenEvent = handlerName;
                break;
            case FormDesignerEventKind.FormClose:
                form.CloseEvent = handlerName;
                break;
        }
    }

    /// <summary>
    /// 建议的事件处理程序基名(官方规则 "_控件_事件"/"_窗体_事件")。
    /// 实际保存时由主窗口用现有子程序名做唯一化。
    /// </summary>
    public static string SuggestedHandlerName(
        YyFormModel form,
        YyControlModel? control,
        FormDesignerEventKind kind)
    {
        var owner = control?.Name ?? form.Name;
        return "_" + owner + "_" + DisplayName(kind);
    }
}
