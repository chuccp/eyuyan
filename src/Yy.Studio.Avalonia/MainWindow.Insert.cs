using System.IO;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Yy.Language;
using Yy.Language.Structured;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private void OnInsertSubprogram(object? sender, RoutedEventArgs e)
    {
        if (CurrentDocument is not { StructuredView: { } structured } document)
        {
            SetStatus("请先打开 .yyc 代码。");
            return;
        }

        try
        {
            structured.FlushPendingEdits();
            var model = structured.Document ??
                YyCodeDocumentConverter.FromSource(
                    document.Editor.Text ?? string.Empty,
                    document.Path);
            var sequence = 1;
            string name;
            do
            {
                name = "新子程序" + sequence++;
            }
            while (model.Functions.Any(function =>
                string.Equals(function.Name, name, StringComparison.Ordinal)));
            model.Functions.Add(new YyCodeFunction { Name = name });
            SetInsertedCodeModel(document, model);
            SetStatus("已插入子程序 " + name);
        }
        catch (Exception exception) when (
            exception is YyDiagnostic or YyCodeFormatException or InvalidOperationException)
        {
            SetStatus("插入子程序失败：" + exception.Message);
        }
    }

    private void SetInsertedCodeModel(
        EditorDocument document,
        YyCodeDocument model)
    {
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

        document.StructuredView?.LoadDocument(model);
        document.IsDirty = true;
        UpdateDocumentHeader(document);
    }

    private async void OnAddImageResource(object? sender, RoutedEventArgs e) =>
        await AddResourceAsync("图片");

    private async void OnAddSoundResource(object? sender, RoutedEventArgs e) =>
        await AddResourceAsync("声音");

    private async Task AddResourceAsync(string type)
    {
        if (_workspace.Project is null)
        {
            SetStatus("请先打开工程。");
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "导入" + type,
            AllowMultiple = true,
            FileTypeFilter =
            [
                new FilePickerFileType(type)
                {
                    Patterns = type == "图片"
                        ? ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.ico"]
                        : ["*.wav", "*.mp3", "*.ogg", "*.m4a"]
                }
            ]
        });
        if (files.Count == 0)
        {
            return;
        }

        try
        {
            foreach (var file in files)
            {
                var path = file.TryGetLocalPath();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    _workspace.Project.AddResource(path, type, null);
                }
            }

            _workspace.Reload();
            RefreshProjectTree();
            SetStatus($"已导入 {files.Count} 个{type}资源。");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus($"导入{type}失败：" + exception.Message);
        }
    }
}
