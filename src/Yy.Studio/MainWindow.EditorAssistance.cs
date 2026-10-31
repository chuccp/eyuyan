using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml;
using Yy.Studio.Properties;
using Yy.Language;
using Yy.Language.Syntax;
using Yy.Sdk;
using Yy.Studio.Forms;
using Yy.Language.Structured;
using Yy.Studio.Structured;

namespace Yy.Studio;

/// <summary>
/// 编辑器配置、补全、参数提示与上下文帮助。
/// </summary>
public partial class MainWindow : Window
{
    private void ConfigureEditor()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("Yy.Studio.Highlighting.Yy.xshd");
        if (stream is not null)
        {
            using var reader = XmlReader.Create(stream);
            Editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        }

        Editor.Options.EnableHyperlinks = false;
        Editor.Options.EnableEmailHyperlinks = false;
        Editor.Options.IndentationSize = 4;
        Editor.Options.ConvertTabsToSpaces = true;
        Editor.Options.HighlightCurrentLine = false;
        Editor.TextArea.IndentationStrategy = new YyIndentationStrategy();
        Editor.TextArea.TextView.LineTransformers.Add(new CurrentLineColorizer(Editor));
        Editor.TextArea.TextView.CurrentLineBackground = Brushes.Transparent;
        var searchPanel = SearchPanel.Install(Editor.TextArea);
        AttachCodeEditorContextMenu(Editor, searchPanel, includeFormat: true);
        EasyLanguageEditor.ContextMenuFactory = (editor, bodySearchPanel) =>
            CreateCodeEditorContextMenu(editor, bodySearchPanel, includeFormat: false);
        Editor.TextArea.Caret.PositionChanged += OnCaretPositionChanged;
        Editor.TextArea.TextEntering += OnEditorTextEntering;
        Editor.TextArea.TextEntered += OnEditorTextEntered;
        Editor.PreviewKeyDown += OnEditorPreviewKeyDown;
        Editor.Document.UndoStack.SizeLimit = 500;
        EasyLanguageEditor.CompletionRequested += (editor, function) =>
            ShowCompletionFor(editor, function);
        EasyLanguageEditor.ParameterHintRequested += (editor, function) =>
            UpdateParameterHintFor(editor);
        InitializeCodeWindowFolding();
    }

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

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isLoadingDocument || _currentDocument is null)
        {
            return;
        }

        if (_currentDocument.IsStructuredCode && _currentDocument.UsePlainCodeEditor)
        {
            _currentDocument.PlainCodeText = Editor.Text;
            ScheduleCodeFoldingUpdate();
            UpdateDocumentIdentity();
            UpdateEditorHelp();
            UpdateParameterHint();
            return;
        }

        _currentDocument.Text = Editor.Text;
        UpdateDocumentIdentity();
        UpdateEditorHelp();
        UpdateParameterHint();
    }

    private void OnCaretPositionChanged(object? sender, EventArgs e)
    {
        CaretText.Text = $"行 {Editor.TextArea.Caret.Line}，列 {Editor.TextArea.Caret.Column}";
        UpdateEditorHelp();
        UpdateParameterHint();
    }

    private void OnEditorTextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (_completionWindow is not null || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var character = e.Text[0];
        if (char.IsLetter(character) || character == '_' || character == '.')
        {
            ShowCompletion();
        }
    }

    private void OnEditorTextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (!_isLoadingDocument)
        {
            YyTextEditing.HandleTextEntering(Editor, e);
        }
    }

    private void OnEditorPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (MatchesConfiguredShortcut(e, StudioShortcutActions.FormatCode))
        {
            FormatCurrentPlainCode();
            e.Handled = true;
            return;
        }

        if (MatchesConfiguredShortcut(e, StudioShortcutActions.Help))
        {
            BottomTabs.SelectedIndex = 1;
            UpdateEditorHelp();
            e.Handled = true;
            return;
        }

        if (MatchesConfiguredShortcut(e, StudioShortcutActions.Completion))
        {
            ShowCompletion();
            e.Handled = true;
            return;
        }

        if (MatchesConfiguredShortcut(e, StudioShortcutActions.ToggleComment))
        {
            YyTextEditing.ToggleComment(Editor);
            e.Handled = true;
            return;
        }

        if (MatchesConfiguredShortcut(e, StudioShortcutActions.DuplicateLine))
        {
            YyTextEditing.DuplicateLine(Editor);
            e.Handled = true;
            return;
        }

        if (MatchesConfiguredShortcut(e, StudioShortcutActions.DeleteLine))
        {
            YyTextEditing.DeleteLine(Editor);
            e.Handled = true;
            return;
        }

        if (YyTextEditing.HandleBackspace(Editor, e))
        {
            return;
        }
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
                includeFormat);
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
                UpdateEditorHelp();
            });

    private void ShowCompletion() => ShowCompletionFor(Editor, null);

    private void ShowCompletionFor(TextEditor editor, YyCodeFunction? currentFunction)
    {
        if (_completionWindow is not null)
        {
            return;
        }

        _completionWindow = new CompletionWindow(editor.TextArea)
        {
            Width = 335,
            MaxHeight = 330
        };
        var data = _completionWindow.CompletionList.CompletionData;
        var known = new HashSet<string>(StringComparer.Ordinal);
        if (IsSourceImportContext(editor.Text, editor.CaretOffset))
        {
            foreach (var (text, description) in GetProjectPackageCompletions())
            {
                if (known.Add(text))
                {
                    data.Add(new CompletionData(
                        text,
                        description,
                        CompletionKind.Package));
                }
            }

            if (data.Count > 0)
            {
                _completionWindow.Show();
                _completionWindow.Closed += (_, _) => _completionWindow = null;
                return;
            }
        }

        if (IsPackageImportContext(editor.Text, editor.CaretOffset))
        {
            foreach (var package in YySdk.Packages.Where(item => item.IsImportable))
            {
                data.Add(new CompletionData(
                    package.Name,
                    $"系统 SDK 包 · {package.StatusText} · {package.Description}",
                    CompletionKind.Package));
            }

            _completionWindow.Show();
            _completionWindow.Closed += (_, _) => _completionWindow = null;
            return;
        }

        var form = GetAssociatedFormForCurrentDocument();
        var memberOwner = TryGetMemberOwnerName(editor.Text, editor.CaretOffset);
        if (form is not null && memberOwner is not null)
        {
            var control = form.Controls.FirstOrDefault(item =>
                string.Equals(item.Name, memberOwner, StringComparison.Ordinal));
            var properties = new (string Name, string Description)[]
            {
                ("文本", "控件的文本、标题或内容"),
                ("名称", "控件名称"),
                ("可见", "是否显示控件"),
                ("启用", "是否允许用户操作"),
                ("选中", "复选框是否选中"),
                ("左", "控件的左侧坐标"),
                ("上", "控件的顶部坐标"),
                ("宽", "控件宽度"),
                ("高", "控件高度")
            };
            foreach (var (name, description) in properties)
            {
                if (known.Add(name))
                {
                    data.Add(new CompletionData(
                        name,
                        control is null ? description : $"{description} · {control.Name}",
                        CompletionKind.Property));
                }
            }

            _completionWindow.Show();
            _completionWindow.Closed += (_, _) => _completionWindow = null;
            return;
        }

        if (currentFunction is not null)
        {
            foreach (var parameter in currentFunction.Parameters)
            {
                if (known.Add(parameter.Name))
                {
                    data.Add(new CompletionData(
                        parameter.Name,
                        $"参数 · {parameter.TypeName ?? "任意"}",
                        CompletionKind.Parameter));
                }
            }

            foreach (var local in currentFunction.Statements
                         .TakeWhile(statement =>
                             statement.Kind == YyCodeStatementKinds.Variable))
            {
                if (known.Add(local.Name))
                {
                    data.Add(new CompletionData(
                        local.Name,
                        $"局部变量 · {local.TypeName ?? "任意"}",
                        CompletionKind.Variable));
                }
            }
        }

        if (form is not null)
        {
            foreach (var alias in new[] { form.Name, "窗口" })
            {
                if (known.Add(alias))
                {
                    data.Add(new CompletionData(
                        alias,
                        $"窗口上下文 · {form.Title}",
                        CompletionKind.Component));
                }
            }

            foreach (var control in form.Controls)
            {
                if (known.Add(control.Name))
                {
                    data.Add(new CompletionData(
                        control.Name,
                        $"组件 · {control.Type}",
                        CompletionKind.Component));
                }
            }
        }

        try
        {
            var source = ReferenceEquals(editor, Editor)
                ? editor.Text
                : _currentDocument?.PlainCodeText ?? editor.Text;
            var program = YyRunner.CompileSource(source, _currentDocument?.FilePath);
            foreach (var function in program.Functions)
            {
                if (known.Add(function.Name))
                {
                    var parameters = string.Join(
                        ", ",
                        function.Parameters.Select(parameter =>
                            parameter.TypeName is null
                                ? parameter.Name
                                : $"{parameter.Name}: {parameter.TypeName}"));
                    data.Add(new CompletionData(
                        function.Name,
                        $"子程序 · {function.Name}({parameters})",
                        CompletionKind.Snippet));
                }
            }
        }
        catch (YyDiagnostic)
        {
            // Completion still works while the document is temporarily incomplete.
        }

        foreach (var (text, description) in StudioCatalog.Completions)
        {
            if (!known.Add(text))
            {
                continue;
            }

            var kind = text is "输出" or "长度" or "类型" or "数字" or "文本" or "范围" or "包含"
                or "分割" or "连接" or "追加" or "移除" or "排序" or "JSON解析" or "JSON生成"
                or "当前时间" or "格式化时间" or "断言"
                ? CompletionKind.Builtin
                : CompletionKind.Keyword;
            if (CodeSnippets.TryGetValue(text, out var snippet))
            {
                data.Add(new CompletionData(
                    text,
                    description + " · 插入代码块",
                    CompletionKind.Snippet,
                    snippet.InsertText,
                    snippet.CaretOffset,
                    snippet.SelectionLength));
            }
            else
            {
                data.Add(new CompletionData(text, description, kind));
            }
        }

        _completionWindow.Show();
        _completionWindow.Closed += (_, _) => _completionWindow = null;
    }

    private static bool IsPackageImportContext(string text, int caretOffset)
        => IsQuotedDirectiveContext(text, caretOffset, "导入包");

    private static bool IsSourceImportContext(string text, int caretOffset)
        => IsQuotedDirectiveContext(text, caretOffset, "引入");

    private static bool IsQuotedDirectiveContext(
        string text,
        int caretOffset,
        string directive)
    {
        var caret = Math.Clamp(caretOffset, 0, text.Length);
        var lineStart = caret == 0 ? 0 : text.LastIndexOf('\n', caret - 1) + 1;
        var lineEnd = text.IndexOf('\n', caret);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        var prefix = text[lineStart..caret];
        var trimmed = prefix.TrimStart();
        if (trimmed.Length <= directive.Length ||
            !trimmed.StartsWith(directive, StringComparison.Ordinal) ||
            !char.IsWhiteSpace(trimmed[directive.Length]))
        {
            return false;
        }

        return prefix.Count(character => character == '"') == 1 &&
               text.AsSpan(caret, lineEnd - caret).IndexOf('"') < 0;
    }

    private IEnumerable<(string Text, string Description)> GetProjectPackageCompletions()
    {
        if (_project is null || !Directory.Exists(_project.SourceRootPath))
        {
            yield break;
        }

        foreach (var directory in Directory
                     .EnumerateDirectories(
                         _project.SourceRootPath,
                         "*",
                         SearchOption.AllDirectories)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(_project.SourceRootPath, directory)
                .Replace('\\', '/');
            if (relative.Split('/').Contains("Forms", StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var hasSource = Directory
                .EnumerateFiles(directory, "*.yyc", SearchOption.TopDirectoryOnly)
                .Any(path =>
                    !Path.GetFileName(path).EndsWith(
                        ".Designer.yyc",
                        StringComparison.OrdinalIgnoreCase));
            if (!hasSource)
            {
                continue;
            }

            var package = _project.PackageName +
                "." +
                string.Join(".", relative.Split('/'));
            yield return (
                package,
                $"源码目录包 · {YyProject.SourceDirectoryName}/{relative}");
        }
    }

    private YyFormModel? GetAssociatedFormForCurrentDocument()
    {
        var formPath = GetAssociatedFormPathForCurrentDocument();
        if (formPath is null)
        {
            return null;
        }

        if (_project is not null)
        {
            var projectForm = _project.Forms.FirstOrDefault(form =>
                string.Equals(form.FormPath, formPath, StringComparison.OrdinalIgnoreCase));
            if (projectForm is not null)
            {
                return projectForm.Model;
            }
        }

        try
        {
            return LoadFormDocument(File.ReadAllText(formPath), formPath);
        }
        catch (Exception exception) when (
            exception is YyFormFormatException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 界面 XML 只装载外观,事件映射从同名 .Designer.yyc 合并。
    /// 未保存的内存窗体暂时没有关系文件,保存时会一次生成三件套。
    /// </summary>
    private static YyFormModel LoadFormDocument(string text, string? formPath)
    {
        var form = YyFormSerializer.Deserialize(text);
        if (string.IsNullOrWhiteSpace(formPath))
        {
            return form;
        }

        var bindingPath = YyFormCompanionFiles.BindingPath(formPath);
        if (!File.Exists(bindingPath))
        {
            return form;
        }

        var binding = YyFormBindingSerializer.Deserialize(File.ReadAllText(bindingPath));
        YyFormBindingSerializer.ApplyTo(form, binding);
        form.CodeFile = binding.CodeFile;
        return form;
    }

    private string? GetAssociatedFormPathForCurrentDocument()
    {
        var path = _currentDocument?.FilePath;
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (_project is not null)
        {
            var projectForm = _project.Forms.FirstOrDefault(form =>
                string.Equals(form.CodePath, path, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(form.BindingPath, path, StringComparison.OrdinalIgnoreCase));
            if (projectForm is not null)
            {
                return projectForm.FormPath;
            }
        }

        var directory = Path.GetDirectoryName(path);
        if (directory is null)
        {
            return null;
        }

        var candidate = YyFormCompanionFiles.GetAssociatedFormPath(path);
        return File.Exists(candidate) ? candidate : null;
    }

    private string? TryGetMemberOwnerName() =>
        TryGetMemberOwnerName(Editor.Text, Editor.CaretOffset);

    private static string? TryGetMemberOwnerName(string text, int caretOffset)
    {
        var caret = Math.Clamp(caretOffset, 0, text.Length);
        if (caret <= 0)
        {
            return null;
        }

        var cursor = caret - 1;
        while (cursor >= 0 && (char.IsLetterOrDigit(text[cursor]) || text[cursor] == '_'))
        {
            cursor--;
        }

        while (cursor >= 0 && char.IsWhiteSpace(text[cursor]))
        {
            cursor--;
        }

        if (cursor < 0 || text[cursor] != '.')
        {
            return null;
        }

        var end = cursor - 1;
        while (end >= 0 && char.IsWhiteSpace(text[end]))
        {
            end--;
        }

        var start = end;
        while (start >= 0 && (char.IsLetterOrDigit(text[start]) || text[start] == '_'))
        {
            start--;
        }

        return end > start ? text[(start + 1)..(end + 1)] : null;
    }

    private void UpdateParameterHint() => UpdateParameterHintFor(Editor);

    private void UpdateParameterHintFor(TextEditor editor)
    {
        if (_currentDocument?.IsForm == true)
        {
            ParameterHintBar.Visibility = Visibility.Collapsed;
            return;
        }

        var caret = Math.Clamp(editor.CaretOffset, 0, editor.Document.TextLength);
        var line = editor.Document.GetLineByOffset(caret);
        var prefix = editor.Document.GetText(line.Offset, caret - line.Offset);
        var openIndex = FindActiveOpenParenthesis(prefix);
        if (openIndex < 0)
        {
            ParameterHintBar.Visibility = Visibility.Collapsed;
            return;
        }

        var nameEnd = openIndex - 1;
        while (nameEnd >= 0 && char.IsWhiteSpace(prefix[nameEnd]))
        {
            nameEnd--;
        }

        var nameStart = nameEnd;
        while (nameStart >= 0 &&
               (char.IsLetterOrDigit(prefix[nameStart]) || prefix[nameStart] == '_'))
        {
            nameStart--;
        }

        if (nameEnd <= nameStart)
        {
            ParameterHintBar.Visibility = Visibility.Collapsed;
            return;
        }

        var name = prefix[(nameStart + 1)..(nameEnd + 1)];
        var callable = FindCallable(name);
        if (callable is null)
        {
            ParameterHintBar.Visibility = Visibility.Collapsed;
            return;
        }

        var argumentIndex = CountTopLevelArguments(prefix[(openIndex + 1)..]) + 1;
        ParameterHintText.Text =
            $"{callable.Value.Signature}    当前参数 {argumentIndex}    {callable.Value.Description}";
        ParameterHintBar.Visibility = Visibility.Visible;
    }

    private void UpdateEditorHelp()
    {
        if (_currentDocument?.IsForm == true)
        {
            EditorHelpBox.Text =
                "窗体设计模式\n\n" +
                "单击控件编辑属性；双击控件生成或打开默认事件代码；F5 预览窗体。";
            return;
        }

        var caret = Math.Clamp(Editor.CaretOffset, 0, Editor.Document.TextLength);
        var word = GetWordAtOffset(Editor.Text, caret);
        var form = GetAssociatedFormForCurrentDocument();
        var memberOwner = TryGetMemberOwnerName();
        var ownerControl = form?.Controls.FirstOrDefault(item =>
            string.Equals(item.Name, memberOwner, StringComparison.Ordinal));
        if (ownerControl is not null && !string.IsNullOrWhiteSpace(word))
        {
            EditorHelpBox.Text =
                $"{ownerControl.Name}.{word}\n\n" +
                $"组件类型：{ownerControl.Type}\n" +
                $"当前值：{GetControlPropertySummary(ownerControl, word)}\n\n" +
                "可使用赋值语句直接修改属性，例如：\n" +
                $"{ownerControl.Name}.{word} = 新值";
            return;
        }

        var control = form?.Controls.FirstOrDefault(item =>
            string.Equals(item.Name, word, StringComparison.Ordinal));
        if (control is not null)
        {
            EditorHelpBox.Text =
                $"组件：{control.Name}\n" +
                $"类型：{control.Type}\n" +
                $"位置：左 {control.X}，上 {control.Y}；尺寸：{control.Width} × {control.Height}\n\n" +
                "可引用属性：文本、名称、可见、启用、选中、左、上、宽、高。\n" +
                $"示例：{control.Name}.文本 = \"新值\"";
            return;
        }

        var callable = FindCallable(word);
        if (callable is not null)
        {
            EditorHelpBox.Text =
                $"{callable.Value.Signature}\n\n{callable.Value.Description}\n\n" +
                "F1：上下文帮助；Ctrl+Space：命令补全。";
            return;
        }

        var keyword = StudioCatalog.Completions.FirstOrDefault(item =>
            string.Equals(item.Text, word, StringComparison.Ordinal));
        EditorHelpBox.Text = keyword.Text is null
            ? "将光标移到命令、函数或组件名上查看帮助。按 F1 打开提示面板，按 Ctrl+Space 打开补全。"
            : $"{keyword.Text}\n\n{keyword.Description}\n\nF1：上下文帮助；Ctrl+Space：命令补全。";
    }

    private static string GetControlPropertySummary(YyControlModel control, string property)
    {
        return property switch
        {
            "文本" => control.Text,
            "名称" => control.Name,
            "可见" => control.IsVisible ? "真" : "假",
            "启用" => control.IsEnabled ? "真" : "假",
            "选中" => control.IsChecked ? "真" : "假",
            "左" => control.X.ToString(),
            "上" => control.Y.ToString(),
            "宽" => control.Width.ToString(),
            "高" => control.Height.ToString(),
            _ => "可通过“组件名.属性名”访问"
        };
    }

    private (string Signature, string Description)? FindCallable(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var builtin = StudioCatalog.Builtins.FirstOrDefault(item =>
            string.Equals(item.Name, name, StringComparison.Ordinal));
        if (builtin is not null)
        {
            return (builtin.Signature, builtin.Description);
        }

        if (!string.Equals(_helpFunctionsSource, Editor.Text, StringComparison.Ordinal))
        {
            _helpFunctionsSource = Editor.Text;
            _helpFunctions = new Dictionary<string, FunctionDeclaration>(StringComparer.Ordinal);
            try
            {
                var program = YyRunner.CompileSource(Editor.Text, _currentDocument?.FilePath);
                _helpFunctions = program.Functions.ToDictionary(
                    function => function.Name,
                    StringComparer.Ordinal);
            }
            catch (YyDiagnostic)
            {
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        if (_helpFunctions.TryGetValue(name, out var userFunction))
        {
            var parameters = string.Join(
                ", ",
                userFunction.Parameters.Select(parameter =>
                    $"{parameter.Name}{(parameter.TypeName is null ? string.Empty : ": " + parameter.TypeName)}" +
                    (parameter.DefaultValue is null ? string.Empty : " = 默认值")));
            return (
                $"{userFunction.Name}({parameters}): {userFunction.ReturnType ?? "任意"}",
                "当前文件定义的子程序。");
        }

        return null;
    }

    private static string GetWordAtOffset(string text, int offset)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var position = Math.Clamp(offset, 0, text.Length);
        if (position == text.Length ||
            !(char.IsLetterOrDigit(text[position]) || text[position] == '_'))
        {
            position = Math.Max(0, position - 1);
        }

        if (position >= text.Length ||
            !(char.IsLetterOrDigit(text[position]) || text[position] == '_'))
        {
            return string.Empty;
        }

        var start = position;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_'))
        {
            start--;
        }

        var end = position + 1;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_'))
        {
            end++;
        }

        return text[start..end];
    }

    private static int FindActiveOpenParenthesis(string prefix)
    {
        var depth = 0;
        for (var index = prefix.Length - 1; index >= 0; index--)
        {
            if (prefix[index] == ')')
            {
                depth++;
            }
            else if (prefix[index] == '(')
            {
                if (depth == 0)
                {
                    return index;
                }

                depth--;
            }
        }

        return -1;
    }

    private static int CountTopLevelArguments(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var depth = 0;
        var commas = 0;
        foreach (var character in text)
        {
            switch (character)
            {
                case '(' or '[' or '{':
                    depth++;
                    break;
                case ')' or ']' or '}':
                    depth = Math.Max(0, depth - 1);
                    break;
                case ',' when depth == 0:
                    commas++;
                    break;
            }
        }

        return commas;
    }

}
