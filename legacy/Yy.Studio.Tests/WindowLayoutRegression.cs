using System.IO;
using Yy.Studio;

internal static class WindowLayoutRegression
{
    public static void Verify()
    {
        var configuration = StudioConfiguration.Parse(
            """
            {
              "格式版本": 1,
              "窗口布局": {
                "预设": "自定义",
                "显示状态栏": false,
                "项目": { "停靠": "右侧", "状态": "最小化", "尺寸": 280 },
                "程序与属性": { "停靠": "左侧", "状态": "显示", "尺寸": 420 },
                "控制台": { "停靠": "底部", "状态": "隐藏", "尺寸": 220 },
                "右侧活动页": 1
              }
            }
            """);
        Equal(StudioWindowLayoutConfiguration.CustomPreset, configuration.WindowLayout.Preset);
        Equal(StudioToolWindowConfiguration.RightDock, configuration.WindowLayout.Project.Dock);
        Equal(
            StudioToolWindowConfiguration.MinimizedState,
            configuration.WindowLayout.Project.State);
        Equal(StudioToolWindowConfiguration.LeftDock, configuration.WindowLayout.WorkTools.Dock);
        Equal(1, configuration.WindowLayout.RightToolsTab);
        True(!configuration.WindowLayout.ShowStatusBar, "状态栏配置没有读取。");
        var windowXaml = File.ReadAllText(Path.Combine(
            Directory.GetCurrentDirectory(),
            "src",
            "Yy.Studio",
            "MainWindow.xaml"));
        True(windowXaml.Contains("x:Name=\"LeftActionRail\"", StringComparison.Ordinal), "缺少左侧常驻操作轨。");
        True(windowXaml.Contains("x:Name=\"RightActionRail\"", StringComparison.Ordinal), "缺少右侧常驻操作轨。");
        True(windowXaml.Contains("x:Name=\"IdeWorkbenchFrame\"", StringComparison.Ordinal), "左右操作轨没有接入一体式工作台边框。");
        True(windowXaml.Contains("x:Name=\"BottomToolWindowHost\"", StringComparison.Ordinal), "底部工具窗口没有接入统一工作台。");
        True(windowXaml.Contains("Grid.ColumnSpan=\"5\"", StringComparison.Ordinal), "底部工具窗口没有横跨完整内容区。");
        True(windowXaml.Contains("RoundedClipBehavior.Radius=\"12\"", StringComparison.Ordinal), "IDE 面板缺少统一的内圆角裁剪。");
        True(windowXaml.Contains("OnProjectActionRailClick", StringComparison.Ordinal), "左侧操作轨没有接入工具窗口命令。");

        try
        {
            StudioConfiguration.Parse(
                """
                {
                  "格式版本": 1,
                  "窗口布局": {
                    "项目": { "停靠": "左侧", "状态": "显示", "尺寸": 260 },
                    "程序与属性": { "停靠": "左侧", "状态": "显示", "尺寸": 360 },
                    "控制台": { "停靠": "底部", "状态": "显示", "尺寸": 172 }
                  }
                }
                """);
            throw new InvalidOperationException("重复停靠区应被拒绝。");
        }
        catch (InvalidDataException exception)
        {
            True(
                exception.Message.Contains("停靠区", StringComparison.Ordinal),
                exception.Message);
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"期望“{expected}”，实际“{actual}”。");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
