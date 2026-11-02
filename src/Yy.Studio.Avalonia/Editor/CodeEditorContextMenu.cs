using Avalonia.Controls;
using Avalonia.Input;
using AvaloniaEdit;
using AvaloniaEdit.Search;

namespace Yy.Studio.Avalonia.Editor;

/// <summary>纯代码与经典视图共用的 IDEA 式编辑器右键菜单(对齐主分支命令表)。</summary>
internal static class CodeEditorContextMenu
{
    public static ContextMenu Create(
        TextEditor editor,
        SearchPanel? searchPanel,
        Action? formatCode,
        Action<TextEditor> completion,
        Action help)
    {
        var menu = new ContextMenu();
        var readOnly = editor.IsReadOnly;

        menu.Items.Add(Item("撤销", "Ctrl+Z", () => editor.Undo()));
        menu.Items.Add(Item("重做", "Ctrl+Y", () => editor.Redo()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("剪切", "Ctrl+X", () => editor.Cut()));
        menu.Items.Add(Item("复制", "Ctrl+C", () => editor.Copy()));
        menu.Items.Add(Item("粘贴", "Ctrl+V", () => editor.Paste(), enabled: !readOnly));
        menu.Items.Add(Item("全选", "Ctrl+A", () => editor.SelectAll()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("查找…", "Ctrl+F", () => searchPanel?.Open()));
        menu.Items.Add(Item("查找下一个", "F3", () => searchPanel?.FindNext()));
        menu.Items.Add(Item("查找上一个", "Shift+F3", () => searchPanel?.FindPrevious()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item(
            "代码补全", "Ctrl+Space", () => completion(editor), enabled: !readOnly));
        // Ctrl+/ 的“/”不是可解析的按键枚举名,只能作为备注写进标题。
        menu.Items.Add(Item(
            "切换注释  Ctrl+/", string.Empty, () => YyTextEditing.ToggleComment(editor), enabled: !readOnly));
        menu.Items.Add(Item(
            "复制当前行", "Ctrl+D", () => YyTextEditing.DuplicateLine(editor), enabled: !readOnly));
        menu.Items.Add(Item(
            "删除当前行", "Ctrl+Shift+K", () => YyTextEditing.DeleteLine(editor), enabled: !readOnly));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("上下文帮助", "F1", help));
        if (formatCode is not null)
        {
            menu.Items.Add(new Separator());
            menu.Items.Add(Item("格式化代码", string.Empty, formatCode, enabled: !readOnly));
        }

        return menu;
    }

    /// <summary>右键按下时把光标移到指针处,菜单命令作用于点击位置。</summary>
    public static void MoveCaretToPointer(TextEditor editor, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(editor.TextArea).Properties.IsRightButtonPressed)
        {
            return;
        }

        var textView = editor.TextArea.TextView;
        var position = textView.GetPosition(e.GetPosition(textView));
        if (position is { } real)
        {
            editor.CaretOffset = editor.Document.GetOffset(real.Location);
        }

        editor.Focus();
    }

    private static MenuItem Item(
        string header,
        string gesture,
        Action action,
        bool enabled = true)
    {
        var item = new MenuItem
        {
            Header = header,
            IsEnabled = enabled
        };
        if (!string.IsNullOrEmpty(gesture))
        {
            item.InputGesture = KeyGesture.Parse(gesture);
        }

        item.Click += (_, _) => action();
        return item;
    }
}
