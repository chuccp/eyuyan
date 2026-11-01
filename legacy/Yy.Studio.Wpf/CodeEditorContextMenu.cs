using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Search;
using Yy.Studio.Structured;

namespace Yy.Studio;

/// <summary>纯代码与经典视图共用的 IDEA 式编辑器右键菜单。</summary>
internal static class CodeEditorContextMenu
{
    public static ContextMenu Create(
        TextEditor editor,
        SearchPanel searchPanel,
        Action? formatCode,
        string formatGesture,
        Action<TextEditor> completion,
        Action help)
    {
        var menu = new ContextMenu();
        var target = editor.TextArea;
        menu.Items.Add(CommandItem("撤销", "Ctrl+Z", ApplicationCommands.Undo, target));
        menu.Items.Add(CommandItem("重做", "Ctrl+Y", ApplicationCommands.Redo, target));
        menu.Items.Add(new Separator());
        menu.Items.Add(CommandItem("剪切", "Ctrl+X", ApplicationCommands.Cut, target));
        menu.Items.Add(CommandItem("复制", "Ctrl+C", ApplicationCommands.Copy, target));
        menu.Items.Add(CommandItem("粘贴", "Ctrl+V", ApplicationCommands.Paste, target));
        menu.Items.Add(CommandItem("全选", "Ctrl+A", ApplicationCommands.SelectAll, target));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("查找…", "Ctrl+F", searchPanel.Open));
        menu.Items.Add(ActionItem("查找下一个", "F3", searchPanel.FindNext));
        menu.Items.Add(ActionItem("查找上一个", "Shift+F3", searchPanel.FindPrevious));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem(
            "代码补全",
            "Ctrl+Space",
            () => completion(editor),
            enabled: !editor.IsReadOnly));
        menu.Items.Add(ActionItem(
            "切换注释",
            "Ctrl+/",
            () => YyTextEditing.ToggleComment(editor),
            enabled: !editor.IsReadOnly));
        menu.Items.Add(ActionItem(
            "复制当前行",
            "Ctrl+D",
            () => YyTextEditing.DuplicateLine(editor),
            enabled: !editor.IsReadOnly));
        menu.Items.Add(ActionItem(
            "删除当前行",
            "Ctrl+Shift+K",
            () => YyTextEditing.DeleteLine(editor),
            enabled: !editor.IsReadOnly));
        menu.Items.Add(new Separator());
        menu.Items.Add(ActionItem("上下文帮助", "F1", help));
        if (formatCode is not null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(ActionItem(
                "格式化代码",
                formatGesture,
                formatCode,
                enabled: !editor.IsReadOnly));
        }

        return menu;
    }

    public static void MoveCaretToPointer(TextEditor editor, MouseButtonEventArgs e)
    {
        var position = editor.GetPositionFromPoint(e.GetPosition(editor));
        if (position is not null)
        {
            editor.CaretOffset = editor.Document.GetOffset(position.Value.Location);
        }

        editor.Focus();
    }

    private static MenuItem CommandItem(
        string header,
        string gesture,
        ICommand command,
        IInputElement target) =>
        new()
        {
            Header = header,
            InputGestureText = gesture,
            Command = command,
            CommandTarget = target
        };

    private static MenuItem ActionItem(
        string header,
        string? gesture,
        Action action,
        bool enabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            InputGestureText = gesture ?? string.Empty,
            IsEnabled = enabled
        };
        item.Click += (_, _) => action();
        return item;
    }
}
