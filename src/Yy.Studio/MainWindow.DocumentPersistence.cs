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
/// 文档保存、另存、三文件写入、关闭与未保存确认。
/// </summary>
public partial class MainWindow : Window
{
    private bool SaveDocument(EditorDocument document)
    {
        if (document.IsGeneratedBinding)
        {
            MessageBox.Show(
                "关系文件由窗体设计器自动生成，不能手工保存。请修改界面或事件后保存窗体。",
                "自动生成文件",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return false;
        }

        if (string.IsNullOrWhiteSpace(document.FilePath))
        {
            return SaveDocumentAs(document);
        }

        return WriteDocument(document, document.FilePath);
    }

    private bool SaveDocumentAs(EditorDocument document)
    {
        if (document.IsGeneratedBinding)
        {
            return SaveDocument(document);
        }

        var isForm = document.IsForm;
        var dialog = new SaveFileDialog
        {
            Title = isForm ? "保存窗体设计" : "保存结构化代码",
            Filter = isForm
                ? "窗体设计 (*.yyx)|*.yyx"
                : "结构化代码 (*.yyc)|*.yyc",
            DefaultExt = isForm ? ".yyx" : ".yyc",
            AddExtension = true,
            FileName = string.IsNullOrWhiteSpace(document.FilePath)
                ? document.DisplayName
                : Path.GetFileName(document.FilePath),
            InitialDirectory = !string.IsNullOrWhiteSpace(_workspacePath)
                ? _workspacePath
                : !string.IsNullOrWhiteSpace(document.FilePath)
                    ? Path.GetDirectoryName(document.FilePath)
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        if ((isForm && !string.Equals(Path.GetExtension(dialog.FileName), ".yyx", StringComparison.OrdinalIgnoreCase)) ||
            (!isForm && !string.Equals(Path.GetExtension(dialog.FileName), ".yyc", StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(
                isForm ? "窗体文件必须使用 .yyx 扩展名。" : "代码文件必须使用 .yyc 扩展名。",
                "文件格式",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrWhiteSpace(_workspacePath))
        {
            _workspacePath = Path.GetDirectoryName(dialog.FileName);
            PopulateProjectTree();
        }

        return WriteDocument(document, dialog.FileName);
    }

    private bool WriteDocument(EditorDocument document, string path)
    {
        try
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            if (ReferenceEquals(document, _currentDocument) &&
                document.UseEasyLanguagePresentation &&
                EasyLanguageEditor.Visibility == Visibility.Visible)
            {
                EasyLanguageEditor.FlushPendingEdits();
            }

            if (document.IsStructuredCode && document.UsePlainCodeEditor)
            {
                document.Text = YyCodeSerializer.Serialize(
                    YyCodeDocumentConverter.FromSource(
                        document.PlainCodeText,
                        path));
            }

            if (document.IsForm)
            {
                var form = ReferenceEquals(document, _designerDocument) && _designForm is not null
                    ? _designForm
                    : LoadFormDocument(document.Text, document.FilePath);
                var previousCodeFile = form.CodeFile;
                var userCodePath = YyFormCompanionFiles.UserCodePath(path);
                form.CodeFile = Path.GetFileName(userCodePath);
                document.Text = YyFormSerializer.Serialize(form);
                File.WriteAllText(path, document.Text, encoding);
                var userCodeDocument = _documents.FirstOrDefault(item =>
                        string.Equals(item.FilePath, userCodePath, StringComparison.OrdinalIgnoreCase))
                    ?? _documents.FirstOrDefault(item =>
                        item.FilePath is null &&
                        (string.Equals(
                             item.DisplayName,
                             form.CodeFile,
                             StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(
                             item.DisplayName,
                             previousCodeFile,
                             StringComparison.OrdinalIgnoreCase)));
                if (userCodeDocument is not null &&
                    (!File.Exists(userCodePath) || userCodeDocument.IsDirty))
                {
                    File.WriteAllText(userCodePath, userCodeDocument.Text, encoding);
                    userCodeDocument.MarkSaved(userCodePath);
                }
                else if (!File.Exists(userCodePath))
                {
                    var binding = YyFormBindingSerializer.Create(form, path, userCodePath);
                    var userCode = YyFormCompanionFiles.CreateUserCodeDocument(form, binding);
                    File.WriteAllText(
                        userCodePath,
                        YyCodeSerializer.Serialize(userCode),
                        encoding);
                }

                YyFormCompanionFiles.WriteGeneratedBinding(path, form, userCodePath);
                RefreshGeneratedBindingDocument(path);
                document.MarkSaved(path);
                if (ReferenceEquals(document, _currentDocument))
                {
                    UpdateDocumentIdentity();
                }

                if (!string.IsNullOrWhiteSpace(_workspacePath))
                {
                    try
                    {
                        _project = YyProject.LoadForDirectory(_workspacePath);
                    }
                    catch (Exception exception) when (
                        exception is InvalidDataException or IOException or UnauthorizedAccessException)
                    {
                        ShowHint("窗体已保存，但项目清单暂时无法重新载入：" + exception.Message);
                    }

                    PopulateProjectTree();
                }

                _settings.AddRecent(path);
                SetStatus(
                    $"已保存界面、用户代码与自动关系：{Path.GetFileName(path)}",
                    ReadyBrush);
                return true;
            }

            File.WriteAllText(path, document.Text, encoding);
            document.MarkSaved(path);
            if (ReferenceEquals(document, _currentDocument))
            {
                UpdateDocumentIdentity();
            }

            if (!string.IsNullOrWhiteSpace(_workspacePath))
            {
                PopulateProjectTree();
            }

            _settings.AddRecent(path);
            SetStatus($"已保存 {Path.GetFileName(path)}", ReadyBrush);
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or YyCodeFormatException or YyDiagnostic)
        {
            MessageBox.Show(
                $"保存失败：\n{exception.Message}",
                "保存失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return false;
        }
    }

    private bool EnsureCanCloseDocument(EditorDocument document)
    {
        if (!document.IsDirty)
        {
            return true;
        }

        var result = MessageBox.Show(
            $"“{document.DisplayName}”还没有保存。是否现在保存？",
            "关闭未保存的标签",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => SaveDocument(document),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private bool CloseDocument(EditorDocument document)
    {
        if (!EnsureCanCloseDocument(document))
        {
            return false;
        }

        var index = _documents.IndexOf(document);
        _documents.Remove(document);
        if (_documents.Count == 0)
        {
            // 官方先后逻辑:关闭全部文档后回到起始页。
            AddStartPage();
        }
        else
        {
            DocumentTabs.SelectedIndex = Math.Clamp(index - 1, 0, _documents.Count - 1);
        }

        SetStatus($"已关闭 {document.DisplayName}", ReadyBrush);
        return true;
    }

}
