using System.Windows.Controls;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Search;
using Yy.Language.Structured;

namespace Yy.Studio;

/// <summary>编辑器上下文帮助和 IDEA 式右键菜单。</summary>
public partial class MainWindow
{
    private void ShowEasyLanguageHelp(TextEditor editor, YyCodeFunction function)
    {
        var word = GetWordAtOffset(editor.Text, editor.CaretOffset);
        var parameter = function.Parameters.FirstOrDefault(item =>
            string.Equals(item.Name, word, StringComparison.Ordinal));
        var local = function.Statements
            .TakeWhile(statement => statement.Kind == YyCodeStatementKinds.Variable)
            .FirstOrDefault(statement =>
                string.Equals(statement.Name, word, StringComparison.Ordinal));
        var form = GetAssociatedFormForCurrentDocument();
        var control = form?.Controls.FirstOrDefault(item =>
            string.Equals(item.Name, word, StringComparison.Ordinal));
        var isWindowAlias = form is not null &&
            (string.Equals(word, form.Name, StringComparison.Ordinal) ||
             string.Equals(word, "窗口", StringComparison.Ordinal));
        var callable = FindCallable(word);
        EditorHelpBox.Text = parameter is not null
            ? $"参数：{parameter.Name}\n类型：{parameter.TypeName ?? "任意"}\n\n{parameter.Remark}"
            : local is not null
                ? $"局部变量：{local.Name}\n类型：{local.TypeName ?? "任意"}\n\n{local.Remark}"
                : control is not null
                    ? $"界面组件：{control.Name}\n类型：{control.Type}\n资源标识由 Designer.yyc 自动维护。"
                    : isWindowAlias
                        ? $"当前窗口：{form!.Name}\n可使用“{form.Name}.标题”或“窗口.标题”访问窗口。"
                        : callable is not null
                            ? $"{callable.Value.Signature}\n\n{callable.Value.Description}\n\n" +
                              "Ctrl+Space：补全；Ctrl+/：注释；Ctrl+D：复制行；Ctrl+Shift+K：删除行。"
                            : "将光标移到命令、函数、控件、参数或变量上查看帮助。\n\n" +
                              "Ctrl+Space：补全；Ctrl+/：注释；Ctrl+D：复制行；Ctrl+Shift+K：删除行。";
        BottomTabs.SelectedIndex = 1;
    }

    private void AttachCodeEditorContextMenu(
        TextEditor editor,
        SearchPanel searchPanel,
        bool includeFormat)
    {
        editor.TextArea.PreviewMouseRightButtonDown += (_, e) =>
        {
            CodeEditorContextMenu.MoveCaretToPointer(editor, e);
            editor.TextArea.ContextMenu = CreateCodeEditorContextMenu(
                editor,
                searchPanel,
                includeFormat &&
                _currentDocument is
                {
                    IsStructuredCode: true,
                    UsePlainCodeEditor: true
                });
        };
    }

    private ContextMenu CreateCodeEditorContextMenu(
        TextEditor editor,
        SearchPanel searchPanel,
        bool includeFormat) =>
        CodeEditorContextMenu.Create(
            editor,
            searchPanel,
            includeFormat ? FormatCurrentPlainCode : null,
            DisplayGestures(StudioShortcutActions.FormatCode),
            sourceEditor => ShowCompletionFor(sourceEditor, null),
            () =>
            {
                BottomTabs.SelectedIndex = 1;
                if (_currentDocument?.IsForm == true)
                {
                    ShowHint("界面源码使用 XML；切换到“可视化界面”后可继续使用组件箱和属性夹。");
                }
                else
                {
                    UpdateEditorHelp();
                }
            });
}
