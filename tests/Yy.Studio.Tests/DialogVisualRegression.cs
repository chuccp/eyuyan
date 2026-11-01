using System.IO;

internal static class DialogVisualRegression
{
    public static void Verify()
    {
        var root = Path.Combine(Directory.GetCurrentDirectory(), "src", "Yy.Studio");
        var startPage = File.ReadAllText(Path.Combine(root, "StartPageView.cs"));
        var newProgram = File.ReadAllText(Path.Combine(root, "NewProgramDialog.cs"));
        var newProject = File.ReadAllText(Path.Combine(root, "NewProjectDialog.cs"));
        var settings = File.ReadAllText(Path.Combine(root, "SystemConfigDialog.xaml"));
        var documentation = File.ReadAllText(Path.Combine(root, "DocumentationWindow.xaml"));

        True(startPage.Contains("最近的项目", StringComparison.Ordinal), "起始页缺少最近项目卡片。");
        True(startPage.Contains("快速开始", StringComparison.Ordinal), "起始页缺少快速开始卡片。");
        True(startPage.Contains("CornerRadius = new CornerRadius(12)", StringComparison.Ordinal), "起始页卡片没有使用统一的大面板圆角。");
        True(newProgram.Contains("WindowStyle = WindowStyle.None", StringComparison.Ordinal), "新建对话框仍使用系统标题栏。");
        True(newProgram.Contains("CornerRadius = new CornerRadius(12)", StringComparison.Ordinal), "新建对话框没有使用统一的大面板圆角。");
        True(newProgram.Contains("BorderThickness = new Thickness(1)", StringComparison.Ordinal), "新建对话框缺少独立外框。");
        True(newProgram.Contains("IdeDialogChrome.CreateCloseButton", StringComparison.Ordinal), "新建对话框关闭按钮未复用主窗口样式。");
        True(newProgram.Contains("RoundedClipBehavior.SetRadius(contentSurface, 11)", StringComparison.Ordinal), "新建对话框没有单独裁剪圆角内容。");
        True(newProgram.Contains("CornerRadius = new CornerRadius(12)", StringComparison.Ordinal), "新建对话框缺少可见圆角外框。");
        True(newProject.Contains("WindowChrome.SetWindowChrome", StringComparison.Ordinal), "新建项目对话框缺少自定义窗口框架。");
        True(newProject.Contains("RoundedClipBehavior.SetRadius(contentSurface, 11)", StringComparison.Ordinal), "新建项目对话框没有单独裁剪圆角内容。");
        True(settings.Contains("{DynamicResource WindowBrush}", StringComparison.Ordinal), "设置窗口没有使用统一主题。");
        True(documentation.Contains("CornerRadius=\"12\"", StringComparison.Ordinal), "文档窗口缺少统一的大面板圆角。");
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
