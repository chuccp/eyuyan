using Yy.Studio;

internal static class DocumentationRegression
{
    public static void VerifyCatalog()
    {
        Require(
            DocumentationCatalog.Topics.Count > Yy.Sdk.YySdk.Packages.Count,
            "开发文档没有包含语言、SDK 和编辑器主题。");
        Require(
            DocumentationCatalog.Search("系统.界面").Any(topic =>
                topic.Title == Yy.Sdk.YySdk.UiPackage),
            "开发文档搜索没有定位到界面 SDK 包。");
        Require(
            DocumentationCatalog.Search("取控件").Any(topic =>
                topic.Blocks.Any(block => block.Text.Contains(
                    "取控件",
                    StringComparison.Ordinal))),
            "开发文档搜索没有覆盖内置命令说明。");
        Require(
            DocumentationCatalog.Topics.Any(topic => topic.Section == "语言语法"),
            "开发文档缺少语言语法主题。");
    }

    public static void VerifyWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new DocumentationWindow();
                Require(window.Title == "易语开发文档", "开发文档窗口标题不正确。");
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
