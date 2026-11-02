using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Search;
using Yy.Studio.Avalonia.Highlighting;
using Yy.Language.Structured;

namespace Yy.Studio.Avalonia.Editor;

/// <summary>
/// 创建带易语语法着色、行号、搜索栏和编辑辅助的 AvaloniaEdit 编辑器。
/// 公共签名保持稳定，主窗口可以继续只调用 <see cref="Create"/>。
/// </summary>
public static class YyCodeEditor
{
    public static TextEditor Create(string text, string path, bool readOnly)
    {
        var editor = new TextEditor
        {
            Text = text,
            IsReadOnly = readOnly,
            ShowLineNumbers = true,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Microsoft YaHei UI"),
            FontSize = 13,
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Color.FromRgb(31, 41, 55)),
            Padding = new Thickness(8, 10, 8, 16)
        };
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 4;
        editor.Options.EnableHyperlinks = false;
        editor.Options.EnableEmailHyperlinks = false;
        CurrentLineRenderer.Attach(editor);

        if (string.Equals(Path.GetExtension(path), ".yyc", StringComparison.OrdinalIgnoreCase))
        {
            editor.SyntaxHighlighting = YyHighlighting.Yy;
            EditorAssistance.Attach(
                editor,
                new CodeEditorSessionOptions
                {
                    EnableCompletion = !readOnly,
                    EnableAutoPairing = !readOnly,
                    EnableFolding = true,
                    EnableParameterHint = true,
                    EnableFullWidthNormalization = !readOnly,
                    InstallSearchPanel = true,
                    ConfigureEditorOptions = true,
                    SyntaxHighlighting = YyHighlighting.Yy
                },
                EditorEnvironmentDiscovery.Create(path));
        }
        else
        {
            if (string.Equals(Path.GetExtension(path), ".yyx", StringComparison.OrdinalIgnoreCase))
            {
                editor.SyntaxHighlighting = YyHighlighting.ForPath(path);
            }

            // 其他文本文件保持原有的搜索栏能力。
            SearchPanel.Install(editor);
        }

        return editor;
    }
}
