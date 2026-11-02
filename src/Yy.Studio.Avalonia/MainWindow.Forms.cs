using System.IO;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Yy.Compiler.Forms;
using Yy.Language.Structured;
using Yy.Studio.Avalonia.Editor;
using Yy.Studio.Avalonia.Features.Forms;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private void OpenFormDocument(string fullPath, string text)
    {
        if (_tabs.Find(fullPath) is { } existing)
        {
            _tabs.Select(existing);
            return;
        }

        try
        {
            var designer = new FormDesignerView
            {
                SnapToGrid = Configuration.Designer.SnapGrid
            };
            designer.LoadFormFile(fullPath, _workspace.Project?.DirectoryPath);
            var propertyPanel = designer.TakePropertyPanel();
            var sourceEditor = YyCodeEditor.Create(text, fullPath, readOnly: false);
            ConfigureEditor(sourceEditor);
            var visualHost = new Border { Child = designer };
            var sourceHost = new Border { Child = sourceEditor, IsVisible = false };
            var content = new Grid();
            content.Children.Add(visualHost);
            content.Children.Add(sourceHost);

            var visualButton = new Button
            {
                Content = "可视化界面",
                Padding = new Thickness(12, 4)
            };
            var sourceButton = new Button
            {
                Content = "源码",
                Padding = new Thickness(12, 4)
            };
            var toolbar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Margin = new Thickness(8, 5)
            };
            toolbar.Children.Add(visualButton);
            toolbar.Children.Add(sourceButton);
            var breadcrumb = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 4,
                Margin = new Thickness(10, 3, 10, 3),
                Background = Brushes.White
            };
            breadcrumb.Children.Add(new TextBlock
            {
                Text = "›",
                Foreground = Brush.Parse("#6F737A"),
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center
            });
            breadcrumb.Children.Add(new TextBlock
            {
                Text = BuildBreadcrumb(fullPath),
                Foreground = Brush.Parse("#6F737A"),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            // 对齐主分支:标签条 → 面包屑 → 界面/源码条 → 内容。
            var host = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(breadcrumb, Dock.Top);
            DockPanel.SetDock(toolbar, Dock.Top);
            host.Children.Add(breadcrumb);
            host.Children.Add(toolbar);
            host.Children.Add(content);

            var tabHandle = _tabs.CreateTab(fullPath, host, "▣");
            var document = new EditorDocument
            {
                Path = fullPath,
                FileName = Path.GetFileName(fullPath),
                Tab = tabHandle.Tab,
                HeaderText = tabHandle.Title,
                DirtyMarkText = tabHandle.DirtyMark,
                CloseButton = tabHandle.CloseButton,
                Editor = sourceEditor,
                FormView = designer,
                FormPropertyPanel = propertyPanel,
                FormVisualHost = visualHost,
                FormSourceHost = sourceHost,
                SavedText = text
            };

            visualButton.Click += (_, _) => SetFormSourceMode(document, source: false);
            sourceButton.Click += (_, _) => SetFormSourceMode(document, source: true);
            designer.FormChanged += (_, _) =>
            {
                document.IsDirty = true;
                UpdateDocumentHeader(document);
                RefreshWorkPanel();
            };
            designer.SelectionChanged += (_, _) => RefreshWorkPanel();
            designer.StatusChanged += SetStatus;
            designer.EventRequested += (_, args) => HandleDesignerEvent(document, args);
            designer.PreviewRequested += (_, _) => OnPreviewForm(null, new RoutedEventArgs());
            designer.ViewCodeRequested += (_, _) => OpenFormCodeBehind(document);
            sourceEditor.TextChanged += (_, _) =>
            {
                var dirty = !string.Equals(
                    sourceEditor.Text ?? string.Empty,
                    document.SavedText,
                    StringComparison.Ordinal);
                if (document.IsDirty != dirty)
                {
                    document.IsDirty = dirty;
                    UpdateDocumentHeader(document);
                }
            };
            sourceEditor.TextArea.Caret.PositionChanged += (_, _) =>
            {
                if (ReferenceEquals(CurrentDocument, document))
                {
                    UpdateCaret(document);
                }
            };

            HideStartPage();
            _tabs.Attach(document);
            _recentFiles.AddRecent(fullPath);
            SetStatus($"已打开窗体 {document.FileName}");
            UpdateActionState();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException
                or YyFormFormatException or InvalidOperationException)
        {
            SetStatus("打开窗体失败。");
            AppendConsole("错误：" + exception.Message);
        }
    }

    private void OpenFormCodeBehind(EditorDocument document)
    {
        var userCodePath = YyFormCompanionFiles.UserCodePath(document.Path);
        if (!File.Exists(userCodePath))
        {
            SetStatus("该窗体还没有用户代码文件。");
            return;
        }

        OpenDocument(userCodePath);
    }

    private void OnPreviewForm(object? sender, RoutedEventArgs e)
    {
        if (CurrentDocument is not { FormView: { } designer } document)
        {
            SetStatus("请先打开窗体设计器。");
            return;
        }

        if (document.FormSourceMode && !SyncFormSource(document))
        {
            return;
        }

        try
        {
            var codePath = YyFormCompanionFiles.UserCodePath(document.Path);
            var code = File.Exists(codePath)
                ? File.ReadAllText(codePath, new UTF8Encoding(false, true))
                : null;
            _lastPreview?.Close();
            _lastPreview = new FormPreviewWindow(designer.BuildForm(), document.Path, code);
            _lastPreview.Closed += (_, _) => _lastPreview = null;
            _lastPreview.Show(this);
            SetStatus("设计预览已打开。");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or InvalidOperationException)
        {
            SetStatus("预览失败：" + exception.Message);
        }
    }

    private bool SyncFormSource(EditorDocument document)
    {
        if (!document.FormSourceMode || document.FormView is not { } designer)
        {
            return true;
        }

        try
        {
            var form = YyFormSerializer.Deserialize(document.Editor.Text ?? string.Empty);
            var bindingPath = YyFormCompanionFiles.BindingPath(document.Path);
            if (File.Exists(bindingPath))
            {
                var binding = YyFormBindingSerializer.Deserialize(
                    File.ReadAllText(bindingPath, new UTF8Encoding(false, true)));
                YyFormBindingSerializer.ApplyCompatibleTo(form, binding);
            }

            designer.LoadForm(form, _workspace.Project?.DirectoryPath, document.Path);
            document.FormSourceMode = false;
            if (document.FormVisualHost is not null)
            {
                document.FormVisualHost.IsVisible = true;
            }

            if (document.FormSourceHost is not null)
            {
                document.FormSourceHost.IsVisible = false;
            }

            return true;
        }
        catch (Exception exception) when (
            exception is YyFormFormatException or InvalidDataException or InvalidOperationException)
        {
            SetStatus("窗体源码无效，无法切换到设计预览：" + exception.Message);
            return false;
        }
    }

    private void SetFormSourceMode(EditorDocument document, bool source)
    {
        if (document.FormView is not { } designer ||
            document.FormSourceHost is null ||
            document.FormVisualHost is null)
        {
            return;
        }

        if (source)
        {
            var xml = designer.SerializeForm() ?? string.Empty;
            document.Editor.Text = xml;
            document.FormSourceMode = true;
            document.FormVisualHost.IsVisible = false;
            document.FormSourceHost.IsVisible = true;
            SetStatus("窗体源码模式：直接编辑唯一的 .yyx XML。");
            return;
        }

        try
        {
            var form = YyFormSerializer.Deserialize(document.Editor.Text ?? string.Empty);
            var bindingPath = YyFormCompanionFiles.BindingPath(document.Path);
            if (File.Exists(bindingPath))
            {
                var binding = YyFormBindingSerializer.Deserialize(
                    File.ReadAllText(bindingPath, new UTF8Encoding(false, true)));
                YyFormBindingSerializer.ApplyCompatibleTo(form, binding);
            }

            designer.LoadForm(form, _workspace.Project?.DirectoryPath, document.Path);
            document.FormSourceMode = false;
            document.FormVisualHost.IsVisible = true;
            document.FormSourceHost.IsVisible = false;
            SetStatus("已从源码恢复可视化界面。");
        }
        catch (Exception exception) when (
            exception is YyFormFormatException or InvalidDataException or InvalidOperationException)
        {
            document.FormSourceMode = true;
            document.FormVisualHost.IsVisible = false;
            document.FormSourceHost.IsVisible = true;
            SetStatus("窗体源码无效，已停留在源码模式：" + exception.Message);
        }
    }

    private bool SaveFormDocument(EditorDocument document)
    {
        if (document.FormView is not { } designer)
        {
            return false;
        }

        try
        {
            if (document.FormSourceMode)
            {
                var form = YyFormSerializer.Deserialize(document.Editor.Text ?? string.Empty);
                var bindingPath = YyFormCompanionFiles.BindingPath(document.Path);
                if (File.Exists(bindingPath))
                {
                    var binding = YyFormBindingSerializer.Deserialize(
                        File.ReadAllText(bindingPath, new UTF8Encoding(false, true)));
                    YyFormBindingSerializer.ApplyCompatibleTo(form, binding);
                }

                designer.LoadForm(form, _workspace.Project?.DirectoryPath, document.Path);
            }

            if (!designer.SaveForm(document.Path))
            {
                SetStatus("保存窗体失败。");
                return false;
            }

            var userCodePath = YyFormCompanionFiles.UserCodePath(document.Path);
            if (!File.Exists(userCodePath))
            {
                var binding = YyFormBindingSerializer.Create(
                    designer.BuildForm(),
                    document.Path,
                    userCodePath);
                var code = YyFormCompanionFiles.CreateUserCodeDocument(
                    designer.BuildForm(),
                    binding);
                File.WriteAllText(
                    userCodePath,
                    YyCodeSerializer.Serialize(code),
                    new UTF8Encoding(false));
            }

            if (!designer.WriteBinding(document.Path, userCodePath))
            {
                SetStatus("生成关系文件失败。");
                return false;
            }

            document.SavedText = designer.SerializeForm() ?? string.Empty;
            document.Editor.Text = document.SavedText;
            document.IsDirty = false;
            UpdateDocumentHeader(document);
            SetStatus($"已保存 {document.FileName}");
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException
                or YyFormFormatException or InvalidOperationException)
        {
            SetStatus("保存窗体失败。");
            AppendConsole("错误：" + exception.Message);
            return false;
        }
    }

    private void HandleDesignerEvent(
        EditorDocument document,
        FormDesignerEventRequestedEventArgs args)
    {
        if (document.FormView is not { } designer)
        {
            return;
        }

        try
        {
            var handler = !string.IsNullOrWhiteSpace(args.ExistingHandlerName)
                ? args.ExistingHandlerName
                : args.SuggestedHandlerName;
            designer.ApplyEventHandler(args.Control, args.Kind, handler);
            var userCodePath = YyFormCompanionFiles.UserCodePath(document.Path);
            var form = designer.BuildForm();
            var binding = YyFormBindingSerializer.Create(form, document.Path, userCodePath);
            var code = File.Exists(userCodePath)
                ? YyCodeSerializer.Deserialize(
                    File.ReadAllText(userCodePath, new UTF8Encoding(false, true)))
                : YyFormCompanionFiles.CreateUserCodeDocument(form, binding);
            YyFormCompanionFiles.EnsureEventHandlers(code, binding.Events);
            File.WriteAllText(
                userCodePath,
                YyCodeSerializer.Serialize(code),
                new UTF8Encoding(false));
            designer.WriteBinding(document.Path, userCodePath);
            document.IsDirty = true;
            UpdateDocumentHeader(document);
            OpenDocument(userCodePath);
            SetStatus($"已创建事件入口 {handler}()");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException
                or InvalidOperationException)
        {
            SetStatus("创建事件失败：" + exception.Message);
        }
    }
}
