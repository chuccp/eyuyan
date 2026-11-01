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
        var existing = _documents.FirstOrDefault(document =>
            string.Equals(document.Path, fullPath, StringComparison.Ordinal));
        if (existing is not null)
        {
            DocumentTabs.SelectedItem = existing.Tab;
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
        var classic = model is null ? null : new StructuredCodeEditor();
        if (classic is not null && model is not null)
        {
            classic.LoadDocument(model);
        }

        var classicHost = new Border { Child = classic };
        var plainHost = new Border { Child = plain, IsVisible = false };
        var content = new Grid();
        content.Children.Add(classicHost);
        content.Children.Add(plainHost);
        var functionCombo = new ComboBox
        {
            Width = 190,
            PlaceholderText = "子程序",
            IsEnabled = classic is not null
        };
        if (classic is not null)
        {
            void RefreshFunctions()
            {
                var selected = functionCombo.SelectedItem as string;
                functionCombo.ItemsSource = classic.Functions
                    .Select(function => function.Name)
                    .ToArray();
                if (selected is not null)
                {
                    functionCombo.SelectedItem = selected;
                }
            }

            RefreshFunctions();
            functionCombo.SelectionChanged += (_, _) =>
            {
                if (functionCombo.SelectedItem is string name)
                {
                    classic.SelectFunction(name);
                }
            };
            classic.ModelChanged += (_, _) => RefreshFunctions();
        }

        var classicButton = new Button
        {
            Content = "经典视图",
            Padding = new Thickness(12, 4)
        };
        var plainButton = new Button
        {
            Content = "纯代码",
            Padding = new Thickness(12, 4)
        };
        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            Margin = new Thickness(8, 5)
        };
        toolbar.Children.Add(functionCombo);
        toolbar.Children.Add(classicButton);
        toolbar.Children.Add(plainButton);
        var host = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(toolbar, Dock.Top);
        host.Children.Add(toolbar);
        host.Children.Add(content);

        var headerText = new TextBlock
        {
            Text = Path.GetFileName(fullPath),
            VerticalAlignment = VerticalAlignment.Center
        };
        var closeButton = new Button
        {
            Content = "×",
            Padding = new Thickness(4, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 15
        };
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6
        };
        header.Children.Add(headerText);
        header.Children.Add(closeButton);
        var tab = new TabItem
        {
            Header = header,
            Content = host,
            Tag = fullPath
        };
        var document = new EditorDocument
        {
            Path = fullPath,
            FileName = Path.GetFileName(fullPath),
            Tab = tab,
            HeaderText = headerText,
            Editor = plain,
            StructuredView = classic,
            PlainCodeHost = plainHost,
            StructuredCodeHost = classicHost,
            UseClassicView = classic is not null,
            SavedText = plainText
        };

        if (classic is not null)
        {
            classicButton.Click += (_, _) => SetCodeView(document, classic: true);
            classic.ModelChanged += (_, _) =>
            {
                document.IsDirty = true;
                UpdateDocumentHeader(document);
                RefreshWorkPanel();
            };
            classic.HintRequested += SetStatus;
            classic.ParameterHintChanged += (_, _, hint) => ShowParameterHint(hint);
        }
        else
        {
            classicButton.IsEnabled = false;
        }

        plainButton.Click += (_, _) => SetCodeView(document, classic: false);
        closeButton.Click += (_, _) => CloseDocument(document);
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

        var form = TryLoadAssociatedForm(fullPath);
        if (form is not null)
        {
            classic?.SetForm(form);
            var session = EditorAssistance.GetSession(plain);
            session?.SetEnvironment(new CompletionEnvironment
            {
                Form = form,
                FilePath = fullPath,
                DocumentFunctions = model?.Functions
            });
        }

        HideStartPage();
        _documents.Add(document);
        DocumentTabs.Items.Add(tab);
        DocumentTabs.SelectedItem = tab;
        _recentFiles.AddRecent(fullPath);
        SetStatus($"已打开 {document.FileName}");
        UpdateActionState();
    }

    private void SetCodeView(EditorDocument document, bool classic)
    {
        if (document.StructuredView is not { } structured ||
            document.PlainCodeHost is null ||
            document.StructuredCodeHost is null)
        {
            return;
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
                SetStatus("经典视图：方法表下方连续编辑执行代码。");
            }
            catch (Exception exception) when (
                exception is YyDiagnostic or YyCodeFormatException)
            {
                SetStatus("代码无法解析，已停留在纯代码视图：" + exception.Message);
            }

            return;
        }

        structured.FlushPendingEdits();
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
        SetStatus("纯代码视图：编辑完整 .yyc 源码。");
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
                structured.FlushPendingEdits();
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
            structured.FlushPendingEdits();
            var model = structured.Document ??
                YyCodeDocumentConverter.FromSource(
                    document.Editor.Text ?? string.Empty,
                    document.Path);
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

    private void ShowParameterHint(YyParameterHint? hint) =>
        SetStatus(hint is null
            ? "就绪"
            : $"{hint.Signature} · 参数 {hint.ActiveParameter + 1}：{hint.Name}");

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
}
