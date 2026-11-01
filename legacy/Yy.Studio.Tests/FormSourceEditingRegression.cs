using Yy.Compiler.Forms;

internal static class FormSourceEditingRegression
{
    public static void Verify()
    {
        var form = new YyFormModel
        {
            Name = "主窗口",
            LoadEvent = "_主窗口_创建完毕"
        };
        form.Controls.Add(new YyControlModel
        {
            Type = YyControlTypes.Button,
            Name = "保存按钮",
            ClickEvent = "_保存按钮_被单击"
        });
        var binding = YyFormBindingSerializer.Create(
            form,
            "主窗口.yyx",
            "主窗口.yyc");
        var sourceForm = YyFormSerializer.Deserialize(YyFormSerializer.Serialize(form));
        var preserved = YyFormBindingSerializer.ApplyCompatibleTo(sourceForm, binding);
        Require(preserved == 2, "界面源码切回设计器时没有保留兼容事件。");
        Require(sourceForm.LoadEvent == "_主窗口_创建完毕", "窗体载入事件丢失。");
        Require(
            sourceForm.Controls.Single().ClickEvent == "_保存按钮_被单击",
            "控件单击事件丢失。");

        sourceForm.Name = "改名窗口";
        var afterRename = YyFormBindingSerializer.ApplyCompatibleTo(sourceForm, binding);
        Require(afterRename == 0, "改名后的旧稳定标识不应继续绑定。");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
