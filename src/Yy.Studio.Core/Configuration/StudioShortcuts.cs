using System.Globalization;
using System.IO;

namespace Yy.Studio.Core.Configuration;

/// <summary>快捷键修饰键。平台层负责把该值映射到自身的按键枚举。</summary>
[Flags]
public enum StudioShortcutModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4
}

/// <summary>
/// 平台无关的快捷键组合。Key 使用标准化名称（字母、数字、F1–F24、
/// Space、Enter、OemQuestion 的显示形式等），供设置窗口、冲突检测和
/// 平台输入绑定共同使用。
/// </summary>
public readonly record struct StudioShortcutGesture(
    StudioShortcutModifiers Modifiers,
    string Key)
{
    /// <summary>规范化显示文本，例如 Ctrl+Shift+K；用于比较和冲突诊断。</summary>
    public string Display
    {
        get
        {
            var parts = new List<string>(4);
            if ((Modifiers & StudioShortcutModifiers.Control) != 0)
            {
                parts.Add("Ctrl");
            }

            if ((Modifiers & StudioShortcutModifiers.Shift) != 0)
            {
                parts.Add("Shift");
            }

            if ((Modifiers & StudioShortcutModifiers.Alt) != 0)
            {
                parts.Add("Alt");
            }

            parts.Add(Key);
            return string.Join('+', parts);
        }
    }

    public override string ToString() => Display;

    public static StudioShortcutGesture Parse(string value, string action) =>
        TryParse(value, out var gesture)
            ? gesture
            : throw new InvalidDataException(
                $"快捷键“{action}”的值“{value}”无效，请使用例如 Ctrl+S、F5、Ctrl+Shift+K。");

    public static bool TryParse(string? value, out StudioShortcutGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var tokens = value.Trim().Split('+');
        if (tokens.Length == 0)
        {
            return false;
        }

        var modifiers = StudioShortcutModifiers.None;
        for (var index = 0; index < tokens.Length - 1; index++)
        {
            var token = tokens[index].Trim();
            var modifier = token.ToLowerInvariant() switch
            {
                "ctrl" or "control" => StudioShortcutModifiers.Control,
                "shift" => StudioShortcutModifiers.Shift,
                "alt" => StudioShortcutModifiers.Alt,
                _ => StudioShortcutModifiers.None
            };
            if (modifier == StudioShortcutModifiers.None ||
                (modifiers & modifier) != 0)
            {
                return false;
            }

            modifiers |= modifier;
        }

        if (!StudioShortcutKeyNames.TryNormalize(tokens[^1].Trim(), out var key))
        {
            return false;
        }

        gesture = new StudioShortcutGesture(modifiers, key);
        return true;
    }
}

/// <summary>快捷键键名的解析与标准化。不依赖 WPF 或 Avalonia 的 Key 枚举。</summary>
internal static class StudioShortcutKeyNames
{
    private static readonly Dictionary<string, string> Aliases = CreateAliases();

    public static bool TryNormalize(string token, out string key)
    {
        key = string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        if (Aliases.TryGetValue(token, out var alias))
        {
            key = alias;
            return true;
        }

        if (token.Length == 1)
        {
            var character = char.ToUpperInvariant(token[0]);
            if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                key = character.ToString();
                return true;
            }

            return false;
        }

        if (token.Length is 2 or 3 &&
            (token[0] == 'F' || token[0] == 'f') &&
            int.TryParse(
                token[1..],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var functionKey) &&
            functionKey is >= 1 and <= 24)
        {
            key = "F" + functionKey.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        return false;
    }

    private static Dictionary<string, string> CreateAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = "Space",
            ["Enter"] = "Enter",
            ["Return"] = "Enter",
            ["Tab"] = "Tab",
            ["Escape"] = "Escape",
            ["Esc"] = "Escape",
            ["Backspace"] = "Backspace",
            ["Back"] = "Backspace",
            ["Delete"] = "Delete",
            ["Del"] = "Delete",
            ["Insert"] = "Insert",
            ["Ins"] = "Insert",
            ["Home"] = "Home",
            ["End"] = "End",
            ["PageUp"] = "PageUp",
            ["PgUp"] = "PageUp",
            ["PageDown"] = "PageDown",
            ["PgDn"] = "PageDown",
            ["Up"] = "Up",
            ["ArrowUp"] = "Up",
            ["Down"] = "Down",
            ["ArrowDown"] = "Down",
            ["Left"] = "Left",
            ["ArrowLeft"] = "Left",
            ["Right"] = "Right",
            ["ArrowRight"] = "Right",
            ["PrintScreen"] = "PrintScreen",
            ["Pause"] = "Pause",
            ["CapsLock"] = "CapsLock",
            ["NumLock"] = "NumLock",
            ["Scroll"] = "Scroll",
            ["OemQuestion"] = "/",
            ["/"] = "/",
            ["OemPlus"] = "=",
            ["="] = "=",
            ["OemMinus"] = "-",
            ["-"] = "-",
            ["OemComma"] = ",",
            [","] = ",",
            ["OemPeriod"] = ".",
            ["."] = ".",
            ["OemSemicolon"] = ";",
            [";"] = ";",
            ["OemOpenBrackets"] = "[",
            ["["] = "[",
            ["OemCloseBrackets"] = "]",
            ["]"] = "]",
            ["OemQuotes"] = "'",
            ["'"] = "'",
            ["OemPipe"] = "\\",
            ["\\"] = "\\",
            ["OemTilde"] = "`",
            ["`"] = "`"
        };

        for (var digit = 0; digit <= 9; digit++)
        {
            var value = digit.ToString(CultureInfo.InvariantCulture);
            aliases["D" + value] = value;
            aliases["NumPad" + value] = "NumPad" + value;
        }

        return aliases;
    }
}

/// <summary>Studio 全部可配置快捷键动作及其默认值。</summary>
public static class StudioShortcutActions
{
    public const string NewFile = "新建源码";
    public const string NewProject = "新建项目";
    public const string NewForm = "新建窗体";
    public const string OpenFile = "打开文件";
    public const string OpenFolder = "打开文件夹";
    public const string SaveFile = "保存";
    public const string SaveAs = "另存为";
    public const string CloseTab = "关闭标签";
    public const string Compile = "编译";
    public const string Run = "运行";
    public const string RunProject = "运行项目";
    public const string Stop = "停止运行";
    public const string DebugStep = "调试单步";
    public const string DebugStepOver = "调试跳过";
    public const string DebugStepOut = "调试跳出";
    public const string DebugRunToCursor = "运行到光标";
    public const string DebugToggleBreakpoint = "切换断点";
    public const string Completion = "代码补全";
    public const string Help = "上下文帮助";
    public const string FormatCode = "格式化代码";
    public const string ToggleComment = "切换注释";
    public const string DuplicateLine = "复制当前行";
    public const string DeleteLine = "删除当前行";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [
            NewFile,
            NewProject,
            NewForm,
            OpenFile,
            OpenFolder,
            SaveFile,
            SaveAs,
            CloseTab,
            Compile,
            Run,
            RunProject,
            Stop,
            DebugStep,
            DebugStepOver,
            DebugStepOut,
            DebugRunToCursor,
            DebugToggleBreakpoint,
            Completion,
            Help,
            FormatCode,
            ToggleComment,
            DuplicateLine,
            DeleteLine
        ],
        StringComparer.Ordinal);

    public static Dictionary<string, List<string>> CreateDefaults() =>
        new(StringComparer.Ordinal)
        {
            [NewFile] = ["Ctrl+N"],
            [NewProject] = ["Ctrl+Alt+N"],
            [NewForm] = ["Ctrl+Shift+N"],
            [OpenFile] = ["Ctrl+O"],
            [OpenFolder] = ["Ctrl+Shift+O"],
            [SaveFile] = ["Ctrl+S"],
            [SaveAs] = ["Ctrl+Shift+S"],
            [CloseTab] = ["Ctrl+W"],
            [Compile] = ["Ctrl+B", "F7"],
            [Run] = ["F5"],
            [RunProject] = ["Ctrl+F5"],
            [Stop] = ["Shift+F5"],
            [DebugStep] = ["F8"],
            [DebugStepOver] = ["F10"],
            [DebugStepOut] = ["Shift+F8"],
            [DebugRunToCursor] = ["F4"],
            [DebugToggleBreakpoint] = ["F9"],
            [Completion] = ["Ctrl+Space"],
            [Help] = ["F1"],
            [FormatCode] = ["Ctrl+Alt+L"],
            [ToggleComment] = ["Ctrl+/"],
            [DuplicateLine] = ["Ctrl+D"],
            [DeleteLine] = ["Ctrl+Shift+K"]
        };

    public static StudioShortcutGesture ParseGesture(string value, string action) =>
        StudioShortcutGesture.Parse(value, action);
}
