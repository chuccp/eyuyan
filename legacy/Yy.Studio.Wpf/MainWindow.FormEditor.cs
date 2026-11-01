using System.IO;
using System.Windows;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Indentation;
using Yy.Compiler.Forms;
using Yy.Language;
using Yy.Studio.Structured;

namespace Yy.Studio;

/// <summary>同一 .yyx 文档的可视化设计器和 XML 源码双模式。</summary>
public partial class MainWindow
{
    private IHighlightingDefinition? _yyHighlighting;
    private IHighlightingDefinition? _xmlHighlighting;

    private void OnShowVisualFormEditor(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is not { IsForm: true } document)
        {
            return;
        }

        try
        {
            var loaded = LoadFormFromSource(document);
            document.Text = YyFormSerializer.Serialize(loaded.Form);
            document.UseFormSourceEditor = false;
            document.LastDesignerSnapshot = document.Text;
            ShowFormDesigner(document, loaded.Form);
            var discarded = loaded.TotalEvents - loaded.PreservedEvents;
            SetStatus(
                discarded > 0
                    ? $"可视化界面已更新；{discarded} 个已删除控件的事件关系将在保存时清理。"
                    : "可视化界面编辑",
                discarded > 0 ? WarningBrush : ReadyBrush);
        }
        catch (YyFormFormatException exception)
        {
            ShowDiagnostic(
                "界面源码错误",
                exception.Message,
                exception.Line,
                exception.Column,
                "修正 XML 后再切换到可视化界面。",
                document.FilePath);
        }
    }

    private void OnShowFormSourceEditor(object sender, RoutedEventArgs e)
    {
        if (_currentDocument is not { IsForm: true } document)
        {
            return;
        }

        if (!document.UseFormSourceEditor && _designForm is not null)
        {
            var source = YyFormSerializer.Serialize(_designForm);
            document.Text = source;
            document.LastDesignerSnapshot = source;
        }

        document.UseFormSourceEditor = true;
        ShowFormSourceEditor(document);
        SetStatus("界面源码编辑", ReadyBrush);
    }

    private void ShowFormSourceEditor(EditorDocument document)
    {
        DataTableViewHost.Visibility = Visibility.Collapsed;
        DesignerPanel.Visibility = Visibility.Collapsed;
        _startPageView.Visibility = Visibility.Collapsed;
        EasyLanguageEditor.Visibility = Visibility.Collapsed;
        CodeWindowBar.Visibility = Visibility.Collapsed;
        ParameterHintBar.Visibility = Visibility.Collapsed;
        FormEditorBar.Visibility = Visibility.Visible;
        Editor.Visibility = Visibility.Visible;
        Editor.IsReadOnly = false;
        SetEditorSyntax(xml: true);
        _isLoadingDocument = true;
        try
        {
            Editor.Text = document.Text;
            Editor.CaretOffset = Math.Clamp(
                document.CaretOffset,
                0,
                Editor.Document.TextLength);
        }
        finally
        {
            _isLoadingDocument = false;
        }

        InspectorPanel.Visibility = Visibility.Collapsed;
        RightToolsPanel.Margin = ToolWindowMargin(
            StudioToolWindowConfiguration.RightDock,
            toolboxVisible: false);
        UpdateRightToolsColumn();
        PropertyRows.ItemsSource = null;
        PropertyRows.Visibility = Visibility.Collapsed;
        FormEditorContext.Text =
            $"窗体源码 · {document.DisplayName} · 保存时会重建自动关系文件";
        SetFormModeButtonState(useSource: true);
        _designForm = null;
        _designerDocument = document;
        _selectedControl = null;
        _selectedElement = null;
        _selectedControls.Clear();
        _designerDragBounds.Clear();
        ClearSelectionVisuals();
        UpdateBreadcrumb(document.DisplayName);
        UpdateDocumentIdentity();
    }

    private FormSourceLoadResult LoadFormFromSource(EditorDocument document)
    {
        var form = YyFormSerializer.Deserialize(document.Text);
        var bindingPath = string.IsNullOrWhiteSpace(document.FilePath)
            ? null
            : YyFormCompanionFiles.BindingPath(document.FilePath);
        if (bindingPath is null || !File.Exists(bindingPath))
        {
            return new FormSourceLoadResult(form, 0, 0);
        }

        var binding = YyFormBindingSerializer.Deserialize(File.ReadAllText(bindingPath));
        form.CodeFile = binding.CodeFile;
        var preserved = YyFormBindingSerializer.ApplyCompatibleTo(form, binding);
        return new FormSourceLoadResult(form, preserved, binding.Events.Count);
    }

    private void SetEditorSyntax(bool xml)
    {
        Editor.SyntaxHighlighting = xml
            ? _xmlHighlighting ?? _yyHighlighting
            : _yyHighlighting;
        Editor.TextArea.IndentationStrategy = xml
            ? new DefaultIndentationStrategy()
            : new YyIndentationStrategy();
    }

    private void SetFormModeButtonState(bool useSource)
    {
        SetModeButtonState(VisualFormEditorButton, !useSource);
        SetModeButtonState(FormSourceEditorButton, useSource);
    }

    private sealed record FormSourceLoadResult(
        YyFormModel Form,
        int PreservedEvents,
        int TotalEvents);
}
