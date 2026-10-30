using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Folding;
using Yy.Language;
using Yy.Language.Structured;
using Yy.Studio.Structured;

namespace Yy.Studio;

/// <summary>
/// VB/易语言式代码窗口顶部导航：对象、过程和格式化。
/// </summary>
public partial class MainWindow : Window
{
    private bool _updatingCodeWindowNavigation;
    private FoldingManager? _codeFoldingManager;
    private DispatcherTimer? _codeFoldingTimer;

    private sealed record CodeObjectChoice(
        string DisplayName,
        IReadOnlyList<string> Handlers)
    {
        public override string ToString() => DisplayName;
    }

    private void OnShowEasyLanguageCode(object sender, RoutedEventArgs e) =>
        SetCodePresentation(useEasyLanguage: true);

    private void OnShowPlainCode(object sender, RoutedEventArgs e) =>
        SetCodePresentation(useEasyLanguage: false);

    private void SetCodePresentation(bool useEasyLanguage)
    {
        if (_currentDocument is not
            {
                IsStructuredCode: true,
                IsGeneratedBinding: false
            })
        {
            return;
        }

        if (_currentDocument.UseEasyLanguagePresentation)
        {
            EasyLanguageEditor.FlushPendingEdits();
        }

        _currentDocument.UseEasyLanguagePresentation = useEasyLanguage;
        ApplyCodePresentation(_currentDocument);
        SetStatus(
            useEasyLanguage
                ? "易语言代码窗口"
                : "纯代码编辑",
            ReadyBrush);
    }

    private void ApplyCodePresentation(EditorDocument document)
    {
        var easyLanguage = document.UseEasyLanguagePresentation;
        CodeNavigationPanel.Visibility =
            easyLanguage ? Visibility.Visible : Visibility.Collapsed;
        Editor.ShowLineNumbers = !easyLanguage;
        Editor.FontFamily = easyLanguage
            ? new FontFamily("SimSun, Microsoft YaHei UI")
            : new FontFamily("Cascadia Code, Consolas, Microsoft YaHei UI");
        Editor.Background = easyLanguage
            ? new SolidColorBrush(Color.FromRgb(246, 250, 255))
            : Brushes.White;

        SetModeButtonState(EasyLanguageCodeButton, easyLanguage);
        SetModeButtonState(PlainCodeButton, !easyLanguage);
        if (easyLanguage)
        {
            try
            {
                var model = YyCodeDocumentConverter.FromSource(
                    document.PlainCodeText,
                    document.FilePath ?? document.DisplayName);
                EasyLanguageEditor.SetEventAssociations(BuildEventAssociations());
                EasyLanguageEditor.LoadDocument(model);
                EasyLanguageEditor.Visibility = Visibility.Visible;
                Editor.Visibility = Visibility.Collapsed;
                ParameterHintBar.Visibility = Visibility.Collapsed;
            }
            catch (Exception exception)
            {
                StudioTrace.Write("EasyLanguageEditor load failed: " + exception);
                CodeWindowContext.Text = "易语言代码窗口载入失败：" + exception.Message;
                ShowHint("易语言代码窗口载入失败：" + exception.Message);
                EasyLanguageEditor.Visibility = Visibility.Collapsed;
                Editor.Visibility = Visibility.Visible;
                ParameterHintBar.Visibility = Visibility.Visible;
            }
        }
        else
        {
            EasyLanguageEditor.Visibility = Visibility.Collapsed;
            Editor.Visibility = Visibility.Visible;
            ParameterHintBar.Visibility = Visibility.Visible;
            UpdateCodeFoldings();
        }

        RefreshCodeWindowNavigation();
    }

    private void InitializeCodeWindowFolding()
    {
        _codeFoldingManager ??= FoldingManager.Install(Editor.TextArea);
        _codeFoldingTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _codeFoldingTimer.Tick += (_, _) =>
        {
            _codeFoldingTimer.Stop();
            UpdateCodeFoldings();
        };
    }

    private void ScheduleCodeFoldingUpdate()
    {
        if (_codeFoldingManager is null)
        {
            return;
        }

        _codeFoldingTimer ??= new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _codeFoldingTimer.Stop();
        _codeFoldingTimer.Start();
    }

    private void UpdateCodeFoldings()
    {
        try
        {
            _codeFoldingManager?.UpdateFoldings(
                EasyLanguageFoldingStrategy.CreateFoldings(Editor.Document),
                firstErrorOffset: -1);
        }
        catch (InvalidOperationException)
        {
            // 视图尚未完成一次布局时跳过本次更新，后续文本变化会重试。
        }
    }

    private void SetModeButtonState(Button button, bool active)
    {
        button.FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal;
        button.Background = active
            ? (Brush)FindResource("AccentBrush")
            : (Brush)FindResource("FaceBrush");
        button.Foreground = active
            ? (Brush)FindResource("HighlightTextBrush")
            : (Brush)FindResource("TextBrush");
    }

    private void RefreshCodeWindowNavigation(string? preferredFunction = null)
    {
        if (_currentDocument is not
            {
                IsStructuredCode: true,
                IsGeneratedBinding: false
            })
        {
            CodeWindowBar.Visibility = Visibility.Collapsed;
            return;
        }

        CodeWindowBar.Visibility = Visibility.Visible;
        YyCodeDocument sourceDocument;
        try
        {
            sourceDocument = YyCodeDocumentConverter.FromSource(
                _currentDocument.PlainCodeText,
                _currentDocument.FilePath ?? _currentDocument.DisplayName);
        }
        catch (YyDiagnostic diagnostic)
        {
            CodeObjectPicker.ItemsSource = null;
            CodeProcedurePicker.ItemsSource = null;
            CodeWindowContext.Text =
                $"代码尚不能解析 · 第 {diagnostic.Line} 行，第 {diagnostic.Column} 列";
            return;
        }

        var form = GetAssociatedFormForCurrentDocument() ?? _designForm;
        var choices = BuildCodeObjectChoices(form, sourceDocument);
        var previousObject = CodeObjectPicker.SelectedItem as CodeObjectChoice;
        var preferredObject = preferredFunction is null
            ? null
            : choices.FirstOrDefault(choice =>
                choice.Handlers.Contains(preferredFunction, StringComparer.Ordinal));
        var targetObject = preferredObject
            ?? choices.FirstOrDefault(choice =>
                string.Equals(
                    choice.DisplayName,
                    previousObject?.DisplayName,
                    StringComparison.Ordinal))
            ?? choices.Skip(1).FirstOrDefault()
            ?? choices[0];

        _updatingCodeWindowNavigation = true;
        try
        {
            CodeObjectPicker.ItemsSource = choices;
            CodeObjectPicker.SelectedItem = targetObject;
        }
        finally
        {
            _updatingCodeWindowNavigation = false;
        }

        UpdateCodeProcedurePicker(
            targetObject,
            sourceDocument,
            preferredFunction);
        CodeWindowContext.Text = form is null
            ? $"程序集：{sourceDocument.Name} · {sourceDocument.Functions.Count} 个子程序"
            : $"窗口：{form.Name} · {sourceDocument.Functions.Count} 个子程序";
        UpdateBreadcrumb(preferredFunction ?? CodeProcedurePicker.SelectedItem as string);
    }

    private List<CodeObjectChoice> BuildCodeObjectChoices(
        Yy.Compiler.Forms.YyFormModel? form,
        YyCodeDocument document)
    {
        var choices = new List<CodeObjectChoice>();
        var boundHandlers = new HashSet<string>(StringComparer.Ordinal);
        if (form is not null)
        {
            var formHandlers = new[]
                {
                    form.LoadEvent,
                    form.CloseEvent
                }
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (formHandlers.Length > 0)
            {
                choices.Add(new CodeObjectChoice(
                    form.Name + "（窗口）",
                    formHandlers));
                boundHandlers.UnionWith(formHandlers);
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
                boundHandlers.UnionWith(handlers);
            }
        }

        var generalHandlers = document.Functions
            .Select(function => function.Name)
            .Where(name => !boundHandlers.Contains(name))
            .ToArray();
        choices.Insert(0, new CodeObjectChoice("（通用）", generalHandlers));
        return choices;
    }

    private void UpdateCodeProcedurePicker(
        CodeObjectChoice selectedObject,
        YyCodeDocument sourceDocument,
        string? preferredFunction)
    {
        var allFunctions = sourceDocument.Functions
            .Select(function => function.Name)
            .ToArray();
        var allowed = selectedObject.Handlers
            .Where(handler => allFunctions.Contains(handler, StringComparer.Ordinal))
            .ToArray();
        if (allowed.Length == 0)
        {
            allowed = allFunctions;
        }

        var current = CodeProcedurePicker.SelectedItem as string;
        _updatingCodeWindowNavigation = true;
        try
        {
            CodeProcedurePicker.ItemsSource = allowed;
            CodeProcedurePicker.SelectedItem =
                preferredFunction is not null &&
                allowed.Contains(preferredFunction, StringComparer.Ordinal)
                    ? preferredFunction
                    : current is not null &&
                      allowed.Contains(current, StringComparer.Ordinal)
                        ? current
                        : allowed.FirstOrDefault();
        }
        finally
        {
            _updatingCodeWindowNavigation = false;
        }
    }

    private void OnCodeObjectPickerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingCodeWindowNavigation ||
            CodeObjectPicker.SelectedItem is not CodeObjectChoice choice ||
            _currentDocument is not
            {
                IsStructuredCode: true,
                UsePlainCodeEditor: true
            })
        {
            return;
        }

        try
        {
            var document = YyCodeDocumentConverter.FromSource(
                _currentDocument.PlainCodeText,
                _currentDocument.FilePath ?? _currentDocument.DisplayName);
            UpdateCodeProcedurePicker(choice, document, null);
            if (CodeProcedurePicker.SelectedItem is string procedure)
            {
                NavigateToCodeFunction(procedure);
            }
        }
        catch (YyDiagnostic)
        {
            // 语法修复期间保留当前光标，不因导航失败打断输入。
        }
    }

    private void OnCodeProcedurePickerChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingCodeWindowNavigation ||
            CodeProcedurePicker.SelectedItem is not string procedure)
        {
            return;
        }

        NavigateToCodeFunction(procedure);
    }

    private void SelectPlainFunction(string functionName)
    {
        if (_currentDocument is not { UsePlainCodeEditor: true })
        {
            return;
        }

        RefreshCodeWindowNavigation(functionName);
        NavigateToCodeFunction(functionName);
    }

    private void NavigateToCodeFunction(string functionName)
    {
        UpdateBreadcrumb(functionName);
        if (_currentDocument?.UseEasyLanguagePresentation == true)
        {
            EasyLanguageEditor.SelectFunction(functionName);
            return;
        }

        NavigateToPlainFunction(functionName);
    }

    private void NavigateToPlainFunction(string functionName)
    {
        if (_currentDocument is not { UsePlainCodeEditor: true } ||
            Editor.Document.LineCount == 0)
        {
            return;
        }

        for (var number = 1; number <= Editor.Document.LineCount; number++)
        {
            var line = Editor.Document.GetLineByNumber(number);
            var text = Editor.Document.GetText(line).TrimStart();
            if (text.StartsWith("子程序 " + functionName + "(", StringComparison.Ordinal) ||
                text.StartsWith("函数 " + functionName + "(", StringComparison.Ordinal))
            {
                Editor.Select(line.Offset, line.Length);
                Editor.ScrollToLine(number);
                Editor.Focus();
                CaretText.Text = $"行 {number}，列 1";
                return;
            }
        }
    }

    private void OnFormatPlainCode(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is not
            {
                IsStructuredCode: true,
                UsePlainCodeEditor: true
            })
        {
            SetStatus("当前窗口不是可格式化的用户代码。", WarningBrush);
            return;
        }

        try
        {
            var source = YyCodeDocumentConverter.ToSource(
                YyCodeDocumentConverter.FromSource(
                    _currentDocument.PlainCodeText,
                    _currentDocument.FilePath ?? _currentDocument.DisplayName));
            var caretLine = Editor.Document.GetLineByOffset(
                Math.Clamp(Editor.CaretOffset, 0, Editor.Document.TextLength)).LineNumber;
            _isLoadingDocument = true;
            try
            {
                Editor.Text = source;
                _currentDocument.PlainCodeText = source;
            }
            finally
            {
                _isLoadingDocument = false;
            }

            var target = Editor.Document.GetLineByNumber(
                Math.Clamp(caretLine, 1, Editor.Document.LineCount));
            Editor.CaretOffset = target.Offset;
            Editor.Focus();
            if (_currentDocument.UseEasyLanguagePresentation)
            {
                EasyLanguageEditor.LoadDocument(
                    YyCodeDocumentConverter.FromSource(
                        _currentDocument.PlainCodeText,
                        _currentDocument.FilePath ?? _currentDocument.DisplayName));
            }

            RefreshCodeWindowNavigation();
            SetStatus("代码已按块结构缩进和格式化", ReadyBrush);
        }
        catch (Exception exception) when (
            exception is YyDiagnostic or YyCodeFormatException)
        {
            SetStatus("格式化失败：" + exception.Message, ErrorBrush);
            ShowHint("格式化失败：" + exception.Message);
        }
    }

    private void OnEasyLanguageCodeChanged(object? sender, EventArgs e)
    {
        if (_currentDocument is null ||
            EasyLanguageEditor.Document is null)
        {
            return;
        }

        _currentDocument.Text = YyCodeSerializer.Serialize(
            EasyLanguageEditor.Document);
        _currentDocument.PlainCodeText = YyCodeDocumentConverter.ToSource(
            EasyLanguageEditor.Document);
        UpdateDocumentIdentity();
    }

    private void OnEasyLanguageHint(string text) => ShowHint(text);
}
