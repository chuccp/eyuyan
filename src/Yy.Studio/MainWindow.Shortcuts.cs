using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Yy.Language.Structured;

namespace Yy.Studio;

/// <summary>从 Studio 配置创建窗口和编辑器快捷键，并刷新菜单提示。</summary>
public partial class MainWindow
{
    private void ApplyConfiguredShortcuts()
    {
        InputBindings.Clear();
        Bind(NewFileCommand, StudioShortcutActions.NewFile);
        Bind(NewProjectCommand, StudioShortcutActions.NewProject);
        Bind(NewFormCommand, StudioShortcutActions.NewForm);
        Bind(OpenFileCommand, StudioShortcutActions.OpenFile);
        Bind(OpenFolderCommand, StudioShortcutActions.OpenFolder);
        Bind(SaveFileCommand, StudioShortcutActions.SaveFile);
        Bind(SaveAsCommand, StudioShortcutActions.SaveAs);
        Bind(CloseTabCommand, StudioShortcutActions.CloseTab);
        Bind(CompileProjectCommand, StudioShortcutActions.Compile);
        Bind(RunCommand, StudioShortcutActions.Run);
        Bind(RunProjectCommand, StudioShortcutActions.RunProject);
        Bind(StopCommand, StudioShortcutActions.Stop);
        Bind(DebugStepCommand, StudioShortcutActions.DebugStep);
        Bind(DebugStepOverCommand, StudioShortcutActions.DebugStepOver);
        Bind(DebugStepOutCommand, StudioShortcutActions.DebugStepOut);
        Bind(DebugRunToCursorCommand, StudioShortcutActions.DebugRunToCursor);
        Bind(DebugToggleBreakpointCommand, StudioShortcutActions.DebugToggleBreakpoint);
        EasyLanguageEditor.ShortcutHandler = HandleEasyLanguageShortcut;
        RefreshConfiguredGestureLabels();
    }

    private void Bind(RoutedUICommand command, string action)
    {
        foreach (var gesture in Configuration.GetGestures(action))
        {
            InputBindings.Add(new KeyBinding(command, gesture));
        }
    }

    private bool HandleEasyLanguageShortcut(
        ICSharpCode.AvalonEdit.TextEditor editor,
        YyCodeFunction function,
        KeyEventArgs e)
    {
        if (MatchesConfiguredShortcut(e, StudioShortcutActions.Help))
        {
            ShowEasyLanguageHelp(editor, function);
            e.Handled = true;
            return true;
        }

        if (MatchesConfiguredShortcut(e, StudioShortcutActions.Completion))
        {
            ShowCompletionFor(editor, function);
            e.Handled = true;
            return true;
        }

        if (MatchesConfiguredShortcut(e, StudioShortcutActions.ToggleComment))
        {
            Structured.YyTextEditing.ToggleComment(editor);
            e.Handled = true;
            return true;
        }

        if (MatchesConfiguredShortcut(e, StudioShortcutActions.DuplicateLine))
        {
            Structured.YyTextEditing.DuplicateLine(editor);
            e.Handled = true;
            return true;
        }

        if (MatchesConfiguredShortcut(e, StudioShortcutActions.DeleteLine))
        {
            Structured.YyTextEditing.DeleteLine(editor);
            e.Handled = true;
            return true;
        }

        return Structured.YyTextEditing.HandleBackspace(editor, e);
    }

    private bool MatchesConfiguredShortcut(KeyEventArgs e, string action)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        return Configuration.GetGestures(action).Any(gesture =>
            gesture.Key == key &&
            gesture.Modifiers == Keyboard.Modifiers);
    }

    private void RefreshConfiguredGestureLabels()
    {
        var actions = new Dictionary<RoutedUICommand, string>
        {
            [NewFileCommand] = StudioShortcutActions.NewFile,
            [NewProjectCommand] = StudioShortcutActions.NewProject,
            [NewFormCommand] = StudioShortcutActions.NewForm,
            [OpenFileCommand] = StudioShortcutActions.OpenFile,
            [OpenFolderCommand] = StudioShortcutActions.OpenFolder,
            [SaveFileCommand] = StudioShortcutActions.SaveFile,
            [SaveAsCommand] = StudioShortcutActions.SaveAs,
            [CloseTabCommand] = StudioShortcutActions.CloseTab,
            [CompileProjectCommand] = StudioShortcutActions.Compile,
            [RunCommand] = StudioShortcutActions.Run,
            [RunProjectCommand] = StudioShortcutActions.RunProject,
            [StopCommand] = StudioShortcutActions.Stop,
            [DebugStepCommand] = StudioShortcutActions.DebugStep,
            [DebugStepOverCommand] = StudioShortcutActions.DebugStepOver,
            [DebugStepOutCommand] = StudioShortcutActions.DebugStepOut,
            [DebugRunToCursorCommand] = StudioShortcutActions.DebugRunToCursor,
            [DebugToggleBreakpointCommand] = StudioShortcutActions.DebugToggleBreakpoint
        };
        foreach (var item in EnumerateMenuItems(MainMenu.Items))
        {
            if (item.Command is RoutedUICommand command &&
                actions.TryGetValue(command, out var action))
            {
                item.InputGestureText = DisplayGestures(action);
            }
        }

        NewProjectButton.ToolTip = "新建项目 (" + DisplayGestures(StudioShortcutActions.NewProject) + ")";
        OpenFileButton.ToolTip = "打开文件 (" + DisplayGestures(StudioShortcutActions.OpenFile) + ")";
        SaveFileButton.ToolTip = "保存 (" + DisplayGestures(StudioShortcutActions.SaveFile) + ")";
        RunButton.ToolTip = "运行 (" + DisplayGestures(StudioShortcutActions.Run) + ")";
        StopButton.ToolTip = "终止运行 (" + DisplayGestures(StudioShortcutActions.Stop) + ")";
        CompileProjectButton.ToolTip = "编译 (" + DisplayGestures(StudioShortcutActions.Compile) + ")";
    }

    private static IEnumerable<MenuItem> EnumerateMenuItems(ItemCollection items)
    {
        foreach (var item in items.OfType<MenuItem>())
        {
            yield return item;
            foreach (var child in EnumerateMenuItems(item.Items))
            {
                yield return child;
            }
        }
    }

    private string DisplayGestures(string action) =>
        string.Join(
            " / ",
            Configuration.GetGestures(action).Select(gesture =>
                gesture.GetDisplayStringForCulture(
                    System.Globalization.CultureInfo.InvariantCulture)));
}
