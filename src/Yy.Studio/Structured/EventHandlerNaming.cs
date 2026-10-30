namespace Yy.Studio.Structured;

/// <summary>
/// 官方事件处理子程序命名规则(对齐易语言教程 day2):
/// "_" + 产生事件的窗口单元名称 + "_" + 事件名称,例如 _按钮1_被单击。
/// 以“_”开头的名称具有特定意义(如 _启动窗口 在程序启动后自动载入)。
/// </summary>
public static class EventHandlerNaming
{
    public const string Click = "被单击";
    public const string TextChanged = "内容被改变";
    public const string SelectionChanged = "列表项被选择";
    public const string CheckedChanged = "选中状态被改变";
    public const string FormLoad = "创建完毕";
    public const string FormClose = "将被销毁";

    public static string ForControl(string controlName, string eventName) =>
        "_" + controlName + "_" + eventName;

    public static string ForForm(string formName, string eventName) =>
        "_" + formName + "_" + eventName;

    public static string Unique(string baseName, Func<string, bool> nameExists)
    {
        var candidate = baseName;
        var index = 2;
        while (nameExists(candidate))
        {
            candidate = baseName + index++;
        }

        return candidate;
    }
}
