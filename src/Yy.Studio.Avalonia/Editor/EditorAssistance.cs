using AvaloniaEdit;

namespace Yy.Studio.Avalonia.Editor;

/// <summary>
/// 主窗口接入入口：一次调用即可为 AvaloniaEdit 编辑器启用易语补全、参数提示、
/// 自动配对、缩进、折叠和搜索。
/// </summary>
public static class EditorAssistance
{
    public static CodeEditorSession Attach(
        TextEditor editor,
        CodeEditorSessionOptions? options = null,
        CompletionEnvironment? environment = null)
    {
        var session = CodeEditorSession.Attach(editor, options);
        if (environment is not null)
        {
            session.SetEnvironment(environment);
        }

        return session;
    }

    public static CodeEditorSession? GetSession(TextEditor editor) =>
        CodeEditorSession.TryGet(editor);

    /// <summary>切换文档时更新补全上下文，无需重新安装编辑器辅助。</summary>
    public static void SetEnvironment(TextEditor editor, CompletionEnvironment environment) =>
        CodeEditorSession.TryGet(editor)?.SetEnvironment(environment);

    public static YyParameterHint? GetParameterHint(TextEditor editor) =>
        CodeEditorSession.TryGet(editor)?.Completion.GetParameterHint();
}
