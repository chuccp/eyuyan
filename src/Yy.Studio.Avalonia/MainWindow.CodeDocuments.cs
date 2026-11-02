using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Yy.Compiler.Forms;
using Yy.Language;
using Yy.Language.Structured;
using Yy.Studio.Avalonia.Editor;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private void OpenCodeDocument(string fullPath, string text)
    {
        if (_tabs.Find(fullPath) is { } existing)
        {
            _tabs.Select(existing);
            return;
        }

        YyCodeDocument? model;
        string plainText;
        try
        {
            model = YyCodeDocumentConverter.FromSource(text, fullPath);
            plainText = YyCodeDocumentConverter.ToSource(model);
        }
        catch (Exception exception) when (exception is YyDiagnostic or YyCodeFormatException)
        {
            SetStatus("经典视图载入失败，已退回纯代码编辑：" + exception.Message);
            plainText = text;
            model = null;
        }

        var plain = YyCodeEditor.Create(plainText, fullPath, readOnly: false);
        ConfigureEditor(plain);
        AttachEditorContextMenu(plain);
        plain.TextArea.TextView.LineTransformers.Add(
            new Editor.SemanticColorizer(
                () => model?.Functions.Select(f => f.Name).ToList() ?? [],
                () => model?.MainStatements
                    .Where(s => s.Kind == Yy.Language.Structured.YyCodeStatementKinds.Constant)
                    .Select(s => s.Name)
                    .ToList() ?? []));
        var navForm = TryLoadAssociatedForm(fullPath);
        var classic = model is null ? null : new StructuredCodeEditor();
        if (classic is not null)
        {
            classic.ContextMenuFactory = (bodyEditor, searchPanel) =>
                Editor.CodeEditorContextMenu.Create(
                    bodyEditor,
                    searchPanel,
                    () => FormatCurrentCode(),
                    completionEditor => EditorAssistance.GetSession(completionEditor)?.ShowCompletion(),
                    () => ShowHelpText("上下文帮助:把光标停在命令上按 F1 查看详情。"));
            if (model is not null)
            {
                classic.LoadDocument(model);
            }
        }

        var classicHost = new Border { Child = classic };
        var plainHost = new Border
        {
            Child = plain,
            // 解析失败时没有经典视图,直接落回纯代码,不留空白页。
            IsVisible = classic is null
        };
        var content = new Grid();
        content.Children.Add(classicHost);
        content.Children.Add(plainHost);
        // 易语言式代码窗口导航:对象 + 过程双下拉(对齐官方经典视图)。
        var objectCombo = new ComboBox
        {
            Width = 140,
            FontSize = 12,
            IsEnabled = classic is not null
        };
        var procedureCombo = new ComboBox
        {
            Width = 190,
            FontSize = 12,
            IsEnabled = classic is not null
        };
        var codeContext = new TextBlock
        {
            FontSize = 12,
            Foreground = Brush.Parse("#6C707E"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 4, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var updatingNavigation = false;

        void RefreshCodeNavigation(string? preferredFunction = null)
        {
            if (classic is null)
            {
                return;
            }

            updatingNavigation = true;
            try
            {
                var functions = classic.Functions
                    .Select(function => function.Name)
                    .ToArray();
                var choices = BuildCodeObjectChoices(navForm, functions);
                var previous = objectCombo.SelectedItem as CodeObjectChoice;
                var target = preferredFunction is null
                    ? null
                    : choices.FirstOrDefault(choice =>
                        choice.Handlers.Contains(preferredFunction, StringComparer.Ordinal));
                target ??= choices.FirstOrDefault(choice =>
                        string.Equals(choice.Display, previous?.Display, StringComparison.Ordinal))
                    ?? choices.Skip(1).FirstOrDefault()
                    ?? choices.FirstOrDefault()
                    ?? new CodeObjectChoice("（通用）", []);
                objectCombo.ItemsSource = choices;
                objectCombo.SelectedItem = target;

                var allowed = target.Handlers
                    .Where(functions.Contains)
                    .ToArray();
                if (allowed.Length == 0)
                {
                    allowed = functions;
                }

                var current = procedureCombo.SelectedItem as string;
                procedureCombo.ItemsSource = allowed;
                procedureCombo.SelectedItem =
                    preferredFunction is not null && allowed.Contains(preferredFunction)
                        ? preferredFunction
                        : current is not null && allowed.Contains(current)
                            ? current
                            : allowed.FirstOrDefault();
            }
            finally
            {
                updatingNavigation = false;
            }

            codeContext.Text = navForm is null
                ? $"程序集：{classic.Document?.Name} · {classic.Functions.Count} 个子程序"
                : $"窗口：{navForm.Name} · {classic.Functions.Count} 个子程序";
        }

        if (classic is not null)
        {
            RefreshCodeNavigation();
            objectCombo.SelectionChanged += (_, _) =>
            {
                if (!updatingNavigation)
                {
                    RefreshCodeNavigation();
                }
            };
            procedureCombo.SelectionChanged += (_, _) =>
            {
                if (!updatingNavigation &&
                    procedureCombo.SelectedItem is string name)
                {
                    classic.SelectFunction(name);
                }
            };
            classic.ModelChanged += (_, _) => RefreshCodeNavigation();
        }

        // 官方经典视图的紧凑分段切换:仅当前视图使用蓝底白字。
        var classicButton = new Button
        {
            Content = "经典视图",
            Classes = { "viewSwitch" }
        };
        var plainButton = new Button
        {
            Content = "纯代码",
            Classes = { "viewSwitch" }
        };
        var viewSwitch = new Border
        {
            Margin = new Thickness(10, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.White,
            BorderBrush = Brush.Parse("#C9CED6"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            ClipToBounds = true,
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Children = { classicButton, plainButton }
            }
        };
        var toolbar = new Grid
        {
            Margin = new Thickness(6, 3),
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*")
        };
        AddToolbarCell(toolbar, new TextBlock
        {
            Text = "对象",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 5, 0)
        }, 0);
        objectCombo.MinHeight = 24;
        objectCombo.Padding = new Thickness(6, 1);
        objectCombo.VerticalAlignment = VerticalAlignment.Center;
        AddToolbarCell(toolbar, objectCombo, 1);
        AddToolbarCell(toolbar, new TextBlock
        {
            Text = "过程",
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 5, 0)
        }, 2);
        procedureCombo.MinHeight = 24;
        procedureCombo.Padding = new Thickness(6, 1);
        procedureCombo.VerticalAlignment = VerticalAlignment.Center;
        AddToolbarCell(toolbar, procedureCombo, 3);
        // 尾部始终吃满剩余宽度，视图切换固定在右侧；上下文只在空间足够时显示。
        var toolbarTail = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(viewSwitch, Dock.Right);
        toolbarTail.Children.Add(viewSwitch);
        toolbarTail.Children.Add(codeContext);
        AddToolbarCell(toolbar, toolbarTail, 4);
        toolbar.SizeChanged += (_, e) =>
        {
            // 空间不足时收起上下文摘要；两个选择框和右侧视图按钮保持原位。
            codeContext.IsVisible = e.NewSize.Width >= 660;
        };
        var hintBar = new TextBlock
        {
            IsVisible = false,
            FontFamily = new FontFamily("Cascadia Code, Consolas, Microsoft YaHei UI"),
            FontSize = 10.5,
            Foreground = Brush.Parse("#3574F0"),
            Background = Brush.Parse("#F7F8FA"),
            Padding = new Thickness(10, 4),
            TextWrapping = TextWrapping.Wrap
        };
        // 标签条 → 对象/过程条 → 代码内容。
        var host = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toolbar, Dock.Top);
        DockPanel.SetDock(hintBar, Dock.Bottom);
        host.Children.Add(toolbar);
        var toolbarDivider = new Border
        {
            Classes = { "toolbarDivider" }
        };
        DockPanel.SetDock(toolbarDivider, Dock.Top);
        host.Children.Add(toolbarDivider);
        host.Children.Add(hintBar);
        host.Children.Add(content);

        var isForm = string.Equals(
            Path.GetExtension(fullPath), ".yyx", StringComparison.OrdinalIgnoreCase);
        var tabHandle = _tabs.CreateTab(fullPath, host, isForm ? "▣" : "‹›");
        var document = new EditorDocument
        {
            Path = fullPath,
            FileName = Path.GetFileName(fullPath),
            Tab = tabHandle.Tab,
            HeaderText = tabHandle.Title,
            DirtyMarkText = tabHandle.DirtyMark,
            CloseButton = tabHandle.CloseButton,
            HintBar = hintBar,
            Editor = plain,
            StructuredView = classic,
            PlainCodeHost = plainHost,
            StructuredCodeHost = classicHost,
            ClassicViewButton = classicButton,
            PlainViewButton = plainButton,
            UseClassicView = classic is not null,
            SavedText = plainText
        };
        UpdateCodeViewButtons(document);

        if (classic is not null)
        {
            classicButton.Click += (_, _) => SetCodeView(document, classic: true);
            classic.ModelChanged += (_, _) =>
            {
                document.IsDirty = true;
                UpdateDocumentHeader(document);
                RefreshWorkPanel();
            };
            classic.HintRequested += ShowHelpText;
            classic.ParameterHintChanged += (_, _, hint) => ShowParameterHint(hint);
        }
        else
        {
            classicButton.IsEnabled = false;
        }

        plainButton.Click += (_, _) => SetCodeView(document, classic: false);
        plain.TextChanged += (_, _) =>
        {
            if (document.SuppressDirtyTracking)
            {
                return;
            }

            var dirty = !string.Equals(
                plain.Text ?? string.Empty,
                document.SavedText,
                StringComparison.Ordinal);
            if (document.IsDirty != dirty)
            {
                document.IsDirty = dirty;
                UpdateDocumentHeader(document);
            }
        };
        plain.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (ReferenceEquals(CurrentDocument, document))
            {
                UpdateCaret(document);
            }
        };

        if (navForm is not null)
        {
            classic?.SetForm(navForm);
            var session = EditorAssistance.GetSession(plain);
            session?.SetEnvironment(new CompletionEnvironment
            {
                Form = navForm,
                FilePath = fullPath,
                DocumentFunctions = model?.Functions
            });
        }

        HideStartPage();
        _tabs.Attach(document);
        _recentFiles.AddRecent(fullPath);
        SetStatus($"已打开 {document.FileName}");
        UpdateActionState();
    }

    private bool SetCodeView(EditorDocument document, bool classic)
    {
        if (document.StructuredView is not { } structured ||
            document.PlainCodeHost is null ||
            document.StructuredCodeHost is null)
        {
            return false;
        }

        // 已在目标视图:不重建模型,避免用上一次同步的源码覆盖当前编辑。
        if (classic == document.UseClassicView)
        {
            return true;
        }

        if (classic)
        {
            try
            {
                var parsed = YyCodeDocumentConverter.FromSource(
                    document.Editor.Text ?? string.Empty,
                    document.Path);
                structured.LoadDocument(parsed);
                document.UseClassicView = true;
                document.StructuredCodeHost.IsVisible = true;
                document.PlainCodeHost.IsVisible = false;
                UpdateCodeViewButtons(document);
                SetStatus("经典视图：方法表下方连续编辑执行代码。");
                return true;
            }
            catch (Exception exception) when (
                exception is YyDiagnostic or YyCodeFormatException)
            {
                SetStatus("代码无法解析，已停留在纯代码视图：" + exception.Message);
                return false;
            }
        }

        // 有无法解析的代码区时保持经典视图,绝不用旧模型覆盖用户输入。
        if (!structured.FlushPendingEdits())
        {
            return false;
        }

        var source = structured.Document is { } model
            ? YyCodeDocumentConverter.ToSource(model)
            : document.Editor.Text ?? string.Empty;
        document.SuppressDirtyTracking = true;
        try
        {
            document.Editor.Text = source;
        }
        finally
        {
            document.SuppressDirtyTracking = false;
        }

        document.UseClassicView = false;
        document.StructuredCodeHost.IsVisible = false;
        document.PlainCodeHost.IsVisible = true;
        UpdateCodeViewButtons(document);
        SetStatus("纯代码视图：编辑完整 .yyc 源码。");
        return true;
    }

    /// <summary>经典视图/纯代码为同一分段控件,仅当前视图显示蓝底。</summary>
    private static void UpdateCodeViewButtons(EditorDocument document)
    {
        ApplyViewButtonState(document.ClassicViewButton, document.UseClassicView);
        ApplyViewButtonState(document.PlainViewButton, !document.UseClassicView);
    }

    private static void ApplyViewButtonState(Button? button, bool active)
    {
        if (button is null)
        {
            return;
        }

        button.Classes.Set("active", active);
    }

    private bool SaveCodeDocument(EditorDocument document)
    {
        if (document.StructuredView is not { } structured)
        {
            return false;
        }

        try
        {
            YyCodeDocument model;
            if (document.UseClassicView)
            {
                if (!structured.FlushPendingEdits())
                {
                    SetStatus("保存已取消：请先修正代码区的语法错误。");
                    return false;
                }

                model = structured.Document
                    ?? throw new InvalidOperationException("经典视图尚未载入代码。");
            }
            else
            {
                model = YyCodeDocumentConverter.FromSource(
                    document.Editor.Text ?? string.Empty,
                    document.Path);
                structured.LoadDocument(model);
            }

            var source = YyCodeDocumentConverter.ToSource(model);
            File.WriteAllText(
                document.Path,
                YyCodeSerializer.Serialize(model),
                new UTF8Encoding(false));
            document.SuppressDirtyTracking = true;
            try
            {
                document.Editor.Text = source;
            }
            finally
            {
                document.SuppressDirtyTracking = false;
            }

            document.SavedText = source;
            document.IsDirty = false;
            UpdateDocumentHeader(document);
            SetStatus($"已保存 {document.FileName}");
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException
                or YyDiagnostic or YyCodeFormatException or InvalidOperationException)
        {
            SetStatus("保存代码失败：" + exception.Message);
            return false;
        }
    }

    private bool FormatCurrentCode()
    {
        if (CurrentDocument is not { StructuredView: { } structured } document)
        {
            SetStatus("当前文档没有可格式化的易语代码。");
            return false;
        }

        try
        {
            YyCodeDocument model;
            if (document.UseClassicView)
            {
                if (!structured.FlushPendingEdits())
                {
                    return false;
                }

                model = structured.Document
                    ?? throw new InvalidOperationException("经典视图尚未载入代码。");
            }
            else
            {
                model = YyCodeDocumentConverter.FromSource(
                    document.Editor.Text ?? string.Empty,
                    document.Path);
            }

            var source = YyCodeDocumentConverter.ToSource(model);
            document.SuppressDirtyTracking = true;
            try
            {
                document.Editor.Text = source;
            }
            finally
            {
                document.SuppressDirtyTracking = false;
            }

            if (!document.UseClassicView)
            {
                structured.LoadDocument(model);
            }

            document.IsDirty = true;
            UpdateDocumentHeader(document);
            SetStatus("已格式化代码。");
            return true;
        }
        catch (Exception exception) when (
            exception is YyDiagnostic or YyCodeFormatException or InvalidOperationException)
        {
            SetStatus("格式化失败：" + exception.Message);
            return false;
        }
    }

    private void AttachEditorContextMenu(AvaloniaEdit.TextEditor editor)
    {
        var session = EditorAssistance.GetSession(editor);
        if (session is not null)
        {
            session.HintRequested += ShowHelpText;
        }

        editor.ContextMenu = Editor.CodeEditorContextMenu.Create(
            editor,
            session?.SearchPanel,
            () => FormatCurrentCode(),
            completionEditor => EditorAssistance.GetSession(completionEditor)?.ShowCompletion(),
            () => ShowHelpText("上下文帮助:把光标停在命令上按 F1 查看详情。"));
        editor.TextArea.PointerPressed += (_, e) =>
            Editor.CodeEditorContextMenu.MoveCaretToPointer(editor, e);
    }

    /// <summary>F1/提示信息写入状态栏摘要。</summary>
    private void ShowHelpText(string text)
    {
        var firstLine = text.Split('\n', 2)[0];
        SetStatus(firstLine.Length > 60 ? firstLine[..60] + "…" : firstLine);
    }

    private void ShowParameterHint(YyParameterHint? hint)
    {
        var bar = CurrentDocument?.HintBar;
        if (bar is not null)
        {
            bar.IsVisible = hint is not null;
            bar.Text = hint is null
                ? string.Empty
                : $"{hint.Signature}   参数 {hint.ActiveParameter + 1}：{hint.Name}";
        }

        SetStatus(hint is null
            ? "就绪"
            : $"{hint.Signature} · 参数 {hint.ActiveParameter + 1}：{hint.Name}");
    }

    private sealed record CodeObjectChoice(string Display, string[] Handlers)
    {
        public override string ToString() => Display;
    }

    private static List<CodeObjectChoice> BuildCodeObjectChoices(
        Yy.Compiler.Forms.YyFormModel? form,
        IReadOnlyList<string> functions)
    {
        var choices = new List<CodeObjectChoice>();
        var bound = new HashSet<string>(StringComparer.Ordinal);
        if (form is not null)
        {
            var formHandlers = new[] { form.LoadEvent, form.OpenEvent, form.CloseEvent }
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (formHandlers.Length > 0)
            {
                choices.Add(new CodeObjectChoice(form.Name + "（窗口）", formHandlers));
                bound.UnionWith(formHandlers);
            }

            foreach (var control in form.Controls)
            {
                var handlers = new[]
                    {
                        control.ClickEvent,
                        control.TextChangedEvent,
                        control.SelectionChangedEvent,
                        control.CheckedChangedEvent
                    }
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                if (handlers.Length == 0)
                {
                    continue;
                }

                choices.Add(new CodeObjectChoice(
                    $"{control.Name}（{Yy.Compiler.Forms.YyControlTypes.GetDisplayName(control.Type)}）",
                    handlers));
                bound.UnionWith(handlers);
            }
        }

        var general = functions
            .Where(name => !bound.Contains(name))
            .ToArray();
        choices.Insert(0, new CodeObjectChoice("（通用）", general));
        return choices;
    }

    private string BuildBreadcrumb(string fullPath)
    {
        var segments = new List<string>();
        if (_workspace.Project is { } project)
        {
            segments.Add(project.Name);
            var relative = Path.GetRelativePath(project.DirectoryPath, fullPath);
            if (!relative.StartsWith("..", StringComparison.Ordinal))
            {
                segments.AddRange(
                    relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                        .Where(part => part.Length > 0));
            }
            else
            {
                segments.Add(Path.GetFileName(fullPath));
            }
        }
        else
        {
            segments.Add(Path.GetFileName(fullPath));
        }

        return string.Join(" › ", segments);
    }

    private static YyFormModel? TryLoadAssociatedForm(string codePath)
    {
        try
        {
            var formPath = YyFormCompanionFiles.GetAssociatedFormPath(codePath);
            if (!File.Exists(formPath))
            {
                return null;
            }

            var form = YyFormSerializer.Deserialize(
                File.ReadAllText(formPath, new UTF8Encoding(false, true)));
            var bindingPath = YyFormCompanionFiles.BindingPath(formPath);
            if (File.Exists(bindingPath))
            {
                var binding = YyFormBindingSerializer.Deserialize(
                    File.ReadAllText(bindingPath, new UTF8Encoding(false, true)));
                YyFormBindingSerializer.ApplyCompatibleTo(form, binding);
            }

            return form;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException
                or YyFormFormatException)
        {
            return null;
        }
    }

    private static void AddToolbarCell(Grid toolbar, Control control, int column)
    {
        Grid.SetColumn(control, column);
        toolbar.Children.Add(control);
    }
}
