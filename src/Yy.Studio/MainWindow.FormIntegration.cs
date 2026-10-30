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
using Yy.Studio.Forms;
using Yy.Language.Structured;
using Yy.Studio.Structured;


namespace Yy.Studio;

/// <summary>
/// 窗体预览、事件入口、组件关联和自动关系文件协调。
/// </summary>
public partial class MainWindow : Window
{
    private void OnPreviewForm(object sender, RoutedEventArgs e) => PreviewCurrentForm();

    private void OnOpenFormCodeBehind(object sender, RoutedEventArgs e)
    {
        if (_designForm is null ||
            string.IsNullOrWhiteSpace(_designerDocument?.FilePath))
        {
            MessageBox.Show(
                "请先保存窗体。保存后会自动生成同名的用户代码和关系文件。",
                "用户代码",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var codePath = ResolveCodeBehindPath();
        if (codePath is null || !File.Exists(codePath))
        {
            MessageBox.Show(
                $"找不到用户代码文件：\n{codePath ?? "（未生成）"}",
                "用户代码",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        OpenDocument(codePath);
    }

    private void CreateOrOpenControlEventHandler(
        YyControlModel control,
        ControlEventKind eventKind)
    {
        if (_designForm is null)
        {
            return;
        }

        // 未保存的内存窗体也能直接在内存用户代码中创建事件入口。
        var isFileBacked = !string.IsNullOrWhiteSpace(_currentDocument?.FilePath);
        string? formDirectory = isFileBacked ? Path.GetDirectoryName(_currentDocument!.FilePath)! : null;
        var codeFileName = string.IsNullOrWhiteSpace(_designForm.CodeFile)
            ? (isFileBacked
                ? Path.GetFileNameWithoutExtension(_currentDocument!.FilePath!)
                : Path.GetFileNameWithoutExtension(_currentDocument?.DisplayName ?? "未命名窗体")) + ".yyc"
            : _designForm.CodeFile;
        string? codePath = isFileBacked ? Path.GetFullPath(Path.Combine(formDirectory!, codeFileName)) : null;
        var codeDocument = codePath is not null
            ? _documents.FirstOrDefault(document =>
                string.Equals(document.FilePath, codePath, StringComparison.OrdinalIgnoreCase))
            : _documents.FirstOrDefault(document =>
                document.FilePath is null &&
                string.Equals(document.DisplayName, codeFileName, StringComparison.OrdinalIgnoreCase));

        if (string.Equals(Path.GetExtension(codeFileName), ".yyc", StringComparison.OrdinalIgnoreCase))
        {
            CreateStructuredEventHandler(
                control,
                eventKind,
                codePath,
                codeDocument,
                formDirectory);
            return;
        }

        var source = codeDocument?.Text
            ?? (codePath is not null && File.Exists(codePath)
                ? File.ReadAllText(codePath, new UTF8Encoding(false, true))
                : $"""
                   .版本 3
                   .程序集 {_designForm.Name}代码

                   ' 在这里编写窗体事件代码。
                   """);

        var handlerName = GetControlEvent(control, eventKind);
        if (string.IsNullOrWhiteSpace(handlerName))
        {
            var baseName = EventHandlerNaming.ForControl(control.Name, GetEventDisplayName(eventKind));
            var existing = YyRunner.CompileSource(source, codePath).Functions
                .Select(function => function.Name)
                .ToHashSet(StringComparer.Ordinal);
            handlerName = EventHandlerNaming.Unique(baseName, existing.Contains);
        }

        var functionMarker = "子程序 " + handlerName;
        if (!source.Contains(functionMarker, StringComparison.Ordinal))
        {
            source = source.TrimEnd() + Environment.NewLine + Environment.NewLine +
                $"""
                 子程序 {handlerName}()
                     ' 在这里编写 {control.Name} 的 {GetEventDisplayName(eventKind)} 逻辑。
                 结束子程序
                 """ + Environment.NewLine;
        }

        if (codeDocument is null)
        {
            codeDocument = codePath is not null
                ? new EditorDocument(codePath, Path.GetFileName(codePath), source)
                : new EditorDocument(null, codeFileName, source);
            _documents.Add(codeDocument);
            if (codePath is null || !File.Exists(codePath))
            {
                codeDocument.MarkModified();
            }
        }
        else
        {
            codeDocument.Text = source;
        }

        SetControlEvent(control, eventKind, handlerName);
        if (formDirectory is not null && codePath is not null)
        {
            _designForm.CodeFile = Path.GetRelativePath(formDirectory, codePath).Replace('\\', '/');
        }

        DesignerChanged();
        RefreshCurrentFormBinding();
        DocumentTabs.SelectedItem = codeDocument;
        var offset = codeDocument.Text.IndexOf(functionMarker, StringComparison.Ordinal);
        if (offset >= 0)
        {
            Editor.CaretOffset = offset;
            var line = Editor.Document.GetLineByOffset(offset);
            Editor.Select(line.Offset, line.Length);
            Editor.ScrollToLine(line.LineNumber);
        }

        SetStatus($"已绑定 {GetEventDisplayName(eventKind)} 事件 {handlerName}()", ReadyBrush);
    }

    private void CreateStructuredEventHandler(
        YyControlModel control,
        ControlEventKind eventKind,
        string? codePath,
        EditorDocument? codeDocument,
        string? formDirectory)
    {
        try
        {
            var existingText = codeDocument?.Text
                ?? (codePath is not null && File.Exists(codePath)
                    ? File.ReadAllText(codePath, new UTF8Encoding(false, true))
                    : null);
            var model = string.IsNullOrWhiteSpace(existingText)
                ? new YyCodeDocument { Name = _designForm?.Name + "代码" ?? "窗体代码" }
                : YyCodeSerializer.Deserialize(existingText);
            var handlerName = GetControlEvent(control, eventKind);
            if (string.IsNullOrWhiteSpace(handlerName))
            {
                var baseName = EventHandlerNaming.ForControl(control.Name, GetEventDisplayName(eventKind));
                var existingNames = model.Functions
                    .Select(function => function.Name)
                    .ToHashSet(StringComparer.Ordinal);
                handlerName = EventHandlerNaming.Unique(baseName, existingNames.Contains);
            }

            if (model.Functions.All(function =>
                    !string.Equals(function.Name, handlerName, StringComparison.Ordinal)))
            {
                var function = new YyCodeFunction
                {
                    Name = handlerName,
                    Remark = $"{control.Name} · {GetEventDisplayName(eventKind)}"
                };
                function.Statements.Add(new YyCodeStatement
                {
                    Kind = YyCodeStatementKinds.Call,
                    Expression = $"' 在这里编写“{control.Name} · {GetEventDisplayName(eventKind)}”的用户逻辑。"
                });
                model.Functions.Add(function);
            }

            var serialized = YyCodeSerializer.Serialize(model);
            var changed = !string.Equals(existingText, serialized, StringComparison.Ordinal);
            if (codeDocument is null)
            {
                codeDocument = codePath is not null
                    ? new EditorDocument(codePath, Path.GetFileName(codePath), serialized)
                    : new EditorDocument(null, _designForm?.CodeFile ?? "未命名窗体.yyc", serialized);
                _documents.Add(codeDocument);
                if (changed || codePath is null || !File.Exists(codePath))
                {
                    codeDocument.MarkModified();
                }
            }
            else
            {
                codeDocument.Text = serialized;
            }

            SetControlEvent(control, eventKind, handlerName);
            if (formDirectory is not null && codePath is not null)
            {
                _designForm!.CodeFile = Path.GetRelativePath(formDirectory, codePath).Replace('\\', '/');
            }

            DesignerChanged();
            RefreshCurrentFormBinding();
            DocumentTabs.SelectedItem = codeDocument;
            SelectPlainFunction(handlerName);
            SetStatus($"已绑定 {GetEventDisplayName(eventKind)} 事件 {handlerName}()", ReadyBrush);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or YyCodeFormatException)
        {
            MessageBox.Show(
                $"无法创建事件代码：\n{exception.Message}",
                "生成事件代码",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static string? GetControlEvent(YyControlModel control, ControlEventKind eventKind)
    {
        return eventKind switch
        {
            ControlEventKind.TextChanged => control.TextChangedEvent,
            ControlEventKind.SelectionChanged => control.SelectionChangedEvent,
            ControlEventKind.CheckedChanged => control.CheckedChangedEvent,
            _ => control.ClickEvent
        };
    }

    private static void SetControlEvent(
        YyControlModel control,
        ControlEventKind eventKind,
        string? eventName)
    {
        switch (eventKind)
        {
            case ControlEventKind.TextChanged:
                control.TextChangedEvent = eventName;
                break;
            case ControlEventKind.SelectionChanged:
                control.SelectionChangedEvent = eventName;
                break;
            case ControlEventKind.CheckedChanged:
                control.CheckedChangedEvent = eventName;
                break;
            default:
                control.ClickEvent = eventName;
                break;
        }
    }

    private static string GetEventDisplayName(ControlEventKind eventKind) => eventKind switch
    {
        ControlEventKind.TextChanged => EventHandlerNaming.TextChanged,
        ControlEventKind.SelectionChanged => EventHandlerNaming.SelectionChanged,
        ControlEventKind.CheckedChanged => EventHandlerNaming.CheckedChanged,
        _ => EventHandlerNaming.Click
    };

    /// <summary>
    /// 构建 事件子程序名 → 组件关联描述 映射(如 "_按钮1_被单击" → "按钮1 · 被单击"),
    /// 用于代码编辑页显示子程序与窗体组件的对应关系。
    /// </summary>
    private Dictionary<string, string> BuildEventAssociations()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        void AddForm(YyFormModel form)
        {
            void Add(string? handler, string display)
            {
                if (!string.IsNullOrWhiteSpace(handler))
                {
                    map[handler] = display;
                }
            }

            Add(form.LoadEvent, form.Name + " · " + EventHandlerNaming.FormLoad);
            Add(form.CloseEvent, form.Name + " · " + EventHandlerNaming.FormClose);
            foreach (var control in form.Controls)
            {
                Add(control.ClickEvent, control.Name + " · " + EventHandlerNaming.Click);
                Add(control.TextChangedEvent, control.Name + " · " + EventHandlerNaming.TextChanged);
                Add(control.SelectionChangedEvent, control.Name + " · " + EventHandlerNaming.SelectionChanged);
                Add(control.CheckedChangedEvent, control.Name + " · " + EventHandlerNaming.CheckedChanged);
            }
        }

        var associated = GetAssociatedFormForCurrentDocument();
        if (associated is not null)
        {
            AddForm(associated);
        }
        else if (_project is not null)
        {
            foreach (var form in _project.Forms)
            {
                AddForm(form.Model);
            }
        }
        else if (_designForm is not null)
        {
            AddForm(_designForm);
        }

        return map;
    }

    private void PreviewCurrentForm()
    {
        StudioTrace.Write($"Preview form requested; form loaded={_designForm is not null}");
        if (_designForm is null)
        {
            MessageBox.Show(
                "请先打开一个可解析的 .yyx 窗体文件。",
                "预览窗体",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var preview = new YyFormPreviewWindow(_designForm, _currentDocument?.FilePath, ResolveCodeBehindText())
        {
            Owner = this
        };
        preview.Closed += (_, _) =>
        {
            StudioTrace.Write("Form preview closed");
            if (ReferenceEquals(_lastPreview, preview))
            {
                _lastPreview = null;
            }
        };
        _lastPreview = preview;
        preview.Show();
        StudioTrace.Write($"Form preview shown: {preview.Title}");
        RunInfoText.Text = $"窗体预览：{_designForm.Title}";
        SetStatus("窗体预览已打开", ReadyBrush);
    }

    /// <summary>
    /// 取当前窗体的用户代码文本(优先编辑器中的内存文档,其次磁盘文件),
    /// 让未保存的窗体也能在预览中运行事件代码(对齐官方)。
    /// </summary>
    private string? ResolveCodeBehindText()
    {
        if (_designForm is null)
        {
            return null;
        }

        var path = ResolveCodeBehindPath();
        var document = path is null
            ? null
            : _documents.FirstOrDefault(item =>
                string.Equals(item.FilePath, path, StringComparison.OrdinalIgnoreCase));
        var codeFileName = _designForm.CodeFile;
        document ??= codeFileName is null
            ? null
            : _documents.FirstOrDefault(item =>
                item.FilePath is null &&
                string.Equals(item.DisplayName, codeFileName, StringComparison.OrdinalIgnoreCase));
        if (document is not null)
        {
            return document.Text;
        }

        return path is not null && File.Exists(path)
            ? File.ReadAllText(path, new UTF8Encoding(false, true))
            : null;
    }

    /// <summary>解析当前窗体的用户代码磁盘路径(仅文件窗体有效)。</summary>
    private string? ResolveCodeBehindPath()
    {
        var formPath = _designerDocument?.FilePath;
        if (_designForm is null || string.IsNullOrWhiteSpace(formPath))
        {
            return null;
        }

        return YyFormCompanionFiles.UserCodePath(formPath);
    }

    private bool RefreshCurrentFormBinding()
    {
        var formPath = _designerDocument?.FilePath;
        if (_designForm is null || string.IsNullOrWhiteSpace(formPath))
        {
            return false;
        }

        try
        {
            var userCodePath = YyFormCompanionFiles.UserCodePath(formPath);
            YyFormCompanionFiles.WriteGeneratedBinding(formPath, _designForm, userCodePath);
            RefreshGeneratedBindingDocument(formPath);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or YyFormFormatException)
        {
            ShowHint("自动关系文件生成失败：" + exception.Message);
            return false;
        }
    }

    private void RefreshGeneratedBindingDocument(string formPath)
    {
        var bindingPath = YyFormCompanionFiles.BindingPath(formPath);
        var document = _documents.FirstOrDefault(item =>
            string.Equals(item.FilePath, bindingPath, StringComparison.OrdinalIgnoreCase));
        if (document is null || !File.Exists(bindingPath))
        {
            return;
        }

        document.Text = File.ReadAllText(bindingPath, new UTF8Encoding(false, true));
        document.MarkSaved(bindingPath);
        if (ReferenceEquals(document, _currentDocument))
        {
            Editor.Text = document.Text;
        }
    }

}
