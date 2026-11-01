using System.IO;
using Yy.Studio.Core.Configuration;

namespace Yy.Studio.Core.Tests;

internal static class ShortcutRegression
{
    public static void VerifyParsing()
    {
        var gesture = StudioShortcutGesture.Parse("Ctrl+Shift+K", "测试");
        TestAssert.Equal(
            StudioShortcutModifiers.Control | StudioShortcutModifiers.Shift,
            gesture.Modifiers);
        TestAssert.Equal("K", gesture.Key);
        TestAssert.Equal("Ctrl+Shift+K", gesture.Display);

        TestAssert.Equal(
            "Ctrl+Shift+K",
            StudioShortcutGesture.Parse("shift+ctrl+k", "测试").Display);
        TestAssert.Equal(
            "Ctrl+Alt+Delete",
            StudioShortcutGesture.Parse("Alt+Control+Del", "测试").Display);
        TestAssert.Equal("Ctrl+/", StudioShortcutGesture.Parse("Ctrl+/", "测试").Display);
        TestAssert.Equal(
            "Ctrl+/",
            StudioShortcutGesture.Parse("Ctrl+OemQuestion", "测试").Display);
        TestAssert.Equal("F12", StudioShortcutGesture.Parse("f12", "测试").Display);
        TestAssert.Equal("5", StudioShortcutGesture.Parse("D5", "测试").Key);
        TestAssert.Equal("Space", StudioShortcutGesture.Parse("Ctrl+Space", "测试").Key);
        TestAssert.Equal(
            StudioShortcutModifiers.None,
            StudioShortcutGesture.Parse("F5", "测试").Modifiers);
        TestAssert.Equal(
            "Ctrl+B",
            StudioShortcutActions.ParseGesture("Ctrl+B", StudioShortcutActions.Compile).Display);

        TestAssert.True(
            !StudioShortcutGesture.TryParse("  ", out _),
            "空白快捷键应无效。");
        TestAssert.True(
            !StudioShortcutGesture.TryParse("Ctrl+", out _),
            "缺少按键的快捷键应无效。");
        TestAssert.True(
            !StudioShortcutGesture.TryParse("Ctrl+Ctrl+S", out _),
            "重复修饰键应无效。");
        TestAssert.True(
            !StudioShortcutGesture.TryParse("Ctrl+NotAKey", out _),
            "未知按键应无效。");
        TestAssert.True(
            !StudioShortcutGesture.TryParse("Ctrl+F25", out _),
            "超出范围的 F 键应无效。");

        var exception = TestAssert.Throws<InvalidDataException>(
            () => StudioShortcutGesture.Parse("Ctrl+NotAKey", "运行"),
            "无效快捷键应抛出 InvalidDataException。");
        TestAssert.True(
            exception.Message.Contains("运行", StringComparison.Ordinal) &&
            exception.Message.Contains("无效", StringComparison.Ordinal),
            exception.Message);

        var defaults = StudioShortcutActions.CreateDefaults();
        TestAssert.Equal(StudioShortcutActions.All.Count, defaults.Count);
        defaults[StudioShortcutActions.Run].Add("Ctrl+F9");
        TestAssert.Equal(
            1,
            StudioShortcutActions.CreateDefaults()[StudioShortcutActions.Run].Count);
    }

    public static void VerifyConflictsAndDisable()
    {
        var configuration = StudioConfiguration.Parse(
            """
            {
              "格式版本": 1,
              "快捷键": {
                "运行": [],
                "编译": ["F7"]
              }
            }
            """);
        TestAssert.Equal(
            0,
            configuration.GetGestures(StudioShortcutActions.Run).Count);
        TestAssert.Equal(
            "F7",
            configuration.GetGestures(StudioShortcutActions.Compile).Single().Display);

        var normalized = StudioConfiguration.Parse(
            """
            {
              "格式版本": 1,
              "快捷键": {
                "运行": ["shift+ctrl+u"]
              }
            }
            """);
        TestAssert.Equal(
            "Ctrl+Shift+U",
            normalized.GetGestures(StudioShortcutActions.Run).Single().Display);

        var conflict = TestAssert.Throws<InvalidDataException>(
            () => StudioConfiguration.Parse(
                """
                {
                  "格式版本": 1,
                  "快捷键": {
                    "运行": ["Ctrl+S"],
                    "保存": ["ctrl+s"]
                  }
                }
                """),
            "规范化后相同的快捷键应被拒绝。");
        TestAssert.True(
            conflict.Message.Contains("同时分配", StringComparison.Ordinal),
            conflict.Message);

        var unknownAction = TestAssert.Throws<InvalidDataException>(
            () => StudioConfiguration.Parse(
                """{"格式版本":1,"快捷键":{"打开终端":["Ctrl+T"]}}"""),
            "未知快捷键动作应被拒绝。");
        TestAssert.True(
            unknownAction.Message.Contains(
                "未知快捷键动作",
                StringComparison.Ordinal),
            unknownAction.Message);
    }
}
