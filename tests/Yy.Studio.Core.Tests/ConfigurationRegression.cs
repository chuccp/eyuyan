using System.IO;
using Yy.Studio.Core.Configuration;

namespace Yy.Studio.Core.Tests;

internal static class ConfigurationRegression
{
    public static void VerifyDefaults()
    {
        var configuration = new StudioConfiguration();
        configuration.Validate();

        TestAssert.Equal(1, configuration.FormatVersion);
        TestAssert.Equal(StudioConfiguration.DefaultMcpPort, configuration.Mcp.Port);
        TestAssert.True(configuration.Mcp.Enabled, "MCP 默认应启用。");
        TestAssert.True(configuration.Mcp.AutoFindAvailablePort, "MCP 默认应自动寻找端口。");
        TestAssert.Equal(10, configuration.Mcp.PortAttempts);
        TestAssert.Equal(13d, configuration.Editor.FontSize);
        TestAssert.Equal(4, configuration.Editor.TabWidth);
        TestAssert.True(configuration.Designer.SnapGrid, "窗体设计器默认应吸附网格。");
        TestAssert.True(
            configuration.Startup.ShowNewProgramDialog,
            "默认应显示新建程序对话框。");
        TestAssert.Equal(
            StudioWindowLayoutConfiguration.DefaultPreset,
            configuration.WindowLayout.Preset);
        TestAssert.True(
            configuration.WindowLayout.ShowStatusBar,
            "默认应显示状态栏。");
        TestAssert.Equal(0, configuration.WindowLayout.RightToolsTab);
        TestAssert.Equal(
            StudioToolWindowConfiguration.LeftDock,
            configuration.WindowLayout.Project.Dock);
        TestAssert.Equal(260d, configuration.WindowLayout.Project.Size);
        TestAssert.Equal(
            StudioToolWindowConfiguration.RightDock,
            configuration.WindowLayout.WorkTools.Dock);
        TestAssert.Equal(360d, configuration.WindowLayout.WorkTools.Size);
        TestAssert.Equal(
            StudioToolWindowConfiguration.BottomDock,
            configuration.WindowLayout.Console.Dock);
        TestAssert.Equal(172d, configuration.WindowLayout.Console.Size);

        TestAssert.Equal(
            StudioShortcutActions.All.Count,
            configuration.Shortcuts.Count);
        var compile = configuration.GetGestures(StudioShortcutActions.Compile);
        TestAssert.Equal(2, compile.Count);
        TestAssert.Equal(StudioShortcutModifiers.Control, compile[0].Modifiers);
        TestAssert.Equal("B", compile[0].Key);
        TestAssert.Equal(StudioShortcutModifiers.None, compile[1].Modifiers);
        TestAssert.Equal("F7", compile[1].Key);
        TestAssert.Equal("Ctrl+/", configuration
            .GetGestures(StudioShortcutActions.ToggleComment)
            .Single()
            .Display);
    }

    public static void VerifyParse()
    {
        var configuration = StudioConfiguration.Parse(
            """
            {
              "格式版本": 1,
              "MCP": {
                "启用": false,
                "端口": 38123,
                "自动寻找可用端口": false,
                "端口尝试次数": 3
              },
              "编辑器": { "字号": 15.5, "制表宽度": 2 },
              "窗体设计器": { "吸附网格": false },
              "启动": { "显示新建程序对话框": false },
              "快捷键": {
                "运行": ["Ctrl+R"],
                "编译": []
              }
            }
            """,
            "YyStudio.json");

        TestAssert.Equal(1, configuration.FormatVersion);
        TestAssert.Equal(38123, configuration.Mcp.Port);
        TestAssert.True(
            !configuration.Mcp.Enabled,
            "MCP 启用状态没有读取。");
        TestAssert.True(
            !configuration.Mcp.AutoFindAvailablePort,
            "MCP 自动端口配置没有读取。");
        TestAssert.Equal(3, configuration.Mcp.PortAttempts);
        TestAssert.Equal(15.5d, configuration.Editor.FontSize);
        TestAssert.Equal(2, configuration.Editor.TabWidth);
        TestAssert.True(!configuration.Designer.SnapGrid, "吸附网格配置没有读取。");
        TestAssert.True(
            !configuration.Startup.ShowNewProgramDialog,
            "启动对话框配置没有读取。");
        TestAssert.Equal("YyStudio.json", configuration.ConfigurationPath);

        var run = configuration.GetGestures(StudioShortcutActions.Run).Single();
        TestAssert.Equal(StudioShortcutModifiers.Control, run.Modifiers);
        TestAssert.Equal("R", run.Key);
        TestAssert.Equal(0, configuration.GetGestures(StudioShortcutActions.Compile).Count);
        TestAssert.Equal(
            "S",
            configuration.GetGestures(StudioShortcutActions.SaveFile).Single().Key);

        var clone = configuration.Clone();
        clone.Mcp.Port = 40000;
        clone.Shortcuts[StudioShortcutActions.Run].Add("Ctrl+F9");
        TestAssert.Equal(38123, configuration.Mcp.Port);
        TestAssert.Equal(
            1,
            configuration.GetGestures(StudioShortcutActions.Run).Count);
        TestAssert.Equal("YyStudio.json", clone.ConfigurationPath);
    }

    public static void VerifySaveRoundTrip()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "yy-studio-core-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "nested", "YyStudio.json");
        try
        {
            var configuration = StudioConfiguration.Parse(
                """{"格式版本":1}""",
                path);
            configuration.Mcp.Port = 39001;
            configuration.Editor.FontSize = 16;
            configuration.Shortcuts[StudioShortcutActions.Run] = ["Ctrl+R"];
            configuration.Save();

            TestAssert.True(File.Exists(path), "配置文件没有写入指定路径。");
            var json = File.ReadAllText(path);
            TestAssert.True(
                json.Contains("\"格式版本\": 1", StringComparison.Ordinal),
                "配置没有保存格式版本。");
            TestAssert.True(
                json.Contains("\"端口\": 39001", StringComparison.Ordinal),
                "配置没有保存 MCP 端口。");
            TestAssert.True(
                json.Contains("\"运行\"", StringComparison.Ordinal),
                "中文配置项不应被转义。");

            var loaded = StudioConfiguration.Load(["--config", path]);
            TestAssert.True(loaded.LoadWarning is null, loaded.LoadWarning ?? "加载配置出现警告。");
            TestAssert.Equal(39001, loaded.Mcp.Port);
            TestAssert.Equal(16d, loaded.Editor.FontSize);
            TestAssert.Equal(
                "R",
                loaded.GetGestures(StudioShortcutActions.Run).Single().Key);
            TestAssert.Equal(path, loaded.ConfigurationPath);

            var optionStyle = StudioConfiguration.Load(["--config=" + path]);
            TestAssert.Equal(39001, optionStyle.Mcp.Port);

            var invalidPath = Path.Combine(root, "invalid.json");
            File.WriteAllText(invalidPath, "{ 不是 JSON");
            var fallback = StudioConfiguration.Load(["--config", invalidPath]);
            TestAssert.True(
                fallback.LoadWarning is not null,
                "损坏配置应给出加载警告。");
            TestAssert.Equal(
                StudioConfiguration.DefaultMcpPort,
                fallback.Mcp.Port);

            var missing = StudioConfiguration.Load(
                ["--config", Path.Combine(root, "missing.json")]);
            TestAssert.True(
                missing.LoadWarning is not null,
                "缺失的显式配置应回落到默认值并给出警告。");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    public static void VerifyValidation()
    {
        Expect(
            """{"格式版本":2}""",
            "格式版本");
        Expect(
            """{"格式版本":1,"MCP":{"端口":80}}""",
            "1024");
        Expect(
            """{"格式版本":1,"MCP":{"端口尝试次数":0}}""",
            "尝试次数");
        Expect(
            """{"格式版本":1,"编辑器":{"字号":7}}""",
            "字号");
        Expect(
            """{"格式版本":1,"编辑器":{"制表宽度":17}}""",
            "制表宽度");
        Expect(
            """{"格式版本":1,"快捷键":{"未知动作":["Ctrl+Q"]}}""",
            "未知快捷键动作");
        Expect(
            """{"格式版本":1,"快捷键":{"运行":["Ctrl+NotAKey"]}}""",
            "无效");
        Expect(
            """{"格式版本":1,"快捷键":{"运行":null}}""",
            "数组");
        Expect(
            """{"格式版本":1,"快捷键":{"运行":["Ctrl+S"],"保存":["Ctrl+S"]}}""",
            "同时分配");
        Expect(
            """
            {
              "格式版本": 1,
              "快捷键": {
                "运行": ["Shift+Ctrl+K"],
                "保存": ["Ctrl+Shift+K"]
              }
            }
            """,
            "同时分配");
        Expect(
            """{"格式版本":1,"快捷键":{"编译":["F7","F7"]}}""",
            "同时分配");
    }

    public static void VerifyWindowLayout()
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
        TestAssert.Equal(
            StudioWindowLayoutConfiguration.CustomPreset,
            configuration.WindowLayout.Preset);
        TestAssert.Equal(
            StudioToolWindowConfiguration.RightDock,
            configuration.WindowLayout.Project.Dock);
        TestAssert.Equal(
            StudioToolWindowConfiguration.MinimizedState,
            configuration.WindowLayout.Project.State);
        TestAssert.Equal(
            StudioToolWindowConfiguration.LeftDock,
            configuration.WindowLayout.WorkTools.Dock);
        TestAssert.Equal(
            StudioToolWindowConfiguration.HiddenState,
            configuration.WindowLayout.Console.State);
        TestAssert.Equal(1, configuration.WindowLayout.RightToolsTab);
        TestAssert.True(
            !configuration.WindowLayout.ShowStatusBar,
            "状态栏配置没有读取。");

        var blankPreset = StudioConfiguration.Parse(
            """{"格式版本":1,"窗口布局":{"预设":"   "}}""");
        TestAssert.Equal(
            StudioWindowLayoutConfiguration.DefaultPreset,
            blankPreset.WindowLayout.Preset);

        ExpectLayout(
            """
            {
              "格式版本": 1,
              "窗口布局": {
                "项目": { "停靠": "左侧", "尺寸": 260 },
                "程序与属性": { "停靠": "左侧", "尺寸": 360 },
                "控制台": { "停靠": "底部", "尺寸": 172 }
              }
            }
            """,
            "停靠区");
        ExpectLayout(
            """{"格式版本":1,"窗口布局":{"项目":{"尺寸":100}}}""",
            "侧边宽度");
        ExpectLayout(
            """{"格式版本":1,"窗口布局":{"控制台":{"停靠":"底部","尺寸":700}}}""",
            "底部高度");
        ExpectLayout(
            """{"格式版本":1,"窗口布局":{"预设":"未知布局"}}""",
            "预设");
        ExpectLayout(
            """{"格式版本":1,"窗口布局":{"右侧活动页":2}}""",
            "活动页");
        ExpectLayout(
            """{"格式版本":1,"窗口布局":{"项目":{"状态":"折叠"}}}""",
            "显示状态");
    }

    public static void VerifyRecentFiles()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "yy-studio-recent-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "settings.json");
        try
        {
            var settings = StudioRecentFiles.Load(path);
            TestAssert.Equal(0, settings.RecentFiles.Count);

            var first = Path.Combine(root, "甲.yyproj");
            var second = Path.Combine(root, "乙.yyproj");
            settings.AddRecent(first);
            settings.AddRecent(second);
            settings.AddRecent(first);
            TestAssert.Equal(2, settings.RecentFiles.Count);
            TestAssert.Equal(Path.GetFullPath(first), settings.RecentFiles[0]);
            TestAssert.Equal(Path.GetFullPath(second), settings.RecentFiles[1]);

            var reloaded = StudioRecentFiles.Load(path);
            TestAssert.Equal(2, reloaded.RecentFiles.Count);
            TestAssert.Equal(
                Path.GetFullPath(first),
                reloaded.RecentFiles[0]);

            for (var index = 0; index < 15; index++)
            {
                reloaded.AddRecent(Path.Combine(root, $"项目{index}.yyproj"));
            }

            TestAssert.Equal(
                StudioRecentFiles.MaximumCount,
                reloaded.RecentFiles.Count);
            TestAssert.Equal(
                Path.GetFullPath(Path.Combine(root, "项目14.yyproj")),
                reloaded.RecentFiles[0]);

            var corrupted = Path.Combine(root, "corrupted.json");
            File.WriteAllText(corrupted, "{ broken");
            TestAssert.Equal(
                0,
                StudioRecentFiles.Load(corrupted).RecentFiles.Count);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void Expect(string json, string expectedMessage)
    {
        var exception = TestAssert.Throws<InvalidDataException>(
            () => StudioConfiguration.Parse(json),
            $"配置应被拒绝并包含“{expectedMessage}”。");
        TestAssert.True(
            exception.Message.Contains(expectedMessage, StringComparison.Ordinal),
            exception.Message);
    }

    private static void ExpectLayout(string json, string expectedMessage) =>
        Expect(json, expectedMessage);
}
