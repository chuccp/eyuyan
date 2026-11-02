using Avalonia.Controls;
using AvaloniaEdit;
using Yy.Studio.Avalonia.Editor;
using Yy.Studio.Avalonia.Features.Forms;

namespace Yy.Studio.Avalonia;

public sealed class EditorDocument
{
    public required string Path { get; set; }
    public required string FileName { get; set; }
    public required TabItem Tab { get; init; }
    public required TextBlock HeaderText { get; init; }

    public TextBlock? DirtyMarkText { get; init; }

    /// <summary>标签头上的关闭按钮;由 <see cref="DocumentTabController.CreateTab"/> 创建。</summary>
    public Button? CloseButton { get; init; }

    public TextBlock? HintBar { get; init; }
    public required TextEditor Editor { get; init; }
    public FormDesignerView? FormView { get; init; }
    public Control? FormVisualHost { get; init; }
    public Control? FormSourceHost { get; init; }
    public Control? FormPropertyPanel { get; init; }
    public bool FormSourceMode { get; set; }
    public StructuredCodeEditor? StructuredView { get; init; }
    public Control? PlainCodeHost { get; init; }
    public Control? StructuredCodeHost { get; init; }
    public bool UseClassicView { get; set; }
    public bool SuppressDirtyTracking { get; set; }
    public bool IsReadOnly { get; init; }
    public required string SavedText { get; set; }
    public bool IsDirty { get; set; }
}
