using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using Yy.Language.Structured;

namespace Yy.Studio;

public sealed class EditorDocument : INotifyPropertyChanged
{
    private string? _filePath;
    private string _displayName;
    private string _text;
    private string _savedText;
    private bool _isDirty;
    private string _plainCodeText = string.Empty;
    private string _savedPlainCodeText = string.Empty;
    private bool _usePlainCodeEditor;

    public EditorDocument(string? filePath, string displayName, string text)
    {
        _filePath = filePath;
        _displayName = displayName;
        _text = text;
        _savedText = text;
        if (IsStructuredCode && !IsGeneratedBinding)
        {
            try
            {
                var model = YyCodeSerializer.Deserialize(text);
                BeginPlainCodeEditing(YyCodeDocumentConverter.ToSource(model));
            }
            catch (YyCodeFormatException)
            {
                // 损坏文件进入文本修复视图，不再回退到旧表格编辑器。
                BeginPlainCodeEditing(text);
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? FilePath
    {
        get => _filePath;
        private set
        {
            if (_filePath == value)
            {
                return;
            }

            _filePath = value;
            OnPropertyChanged();
        }
    }

    public string DisplayName
    {
        get => _displayName;
        private set
        {
            if (_displayName == value)
            {
                return;
            }

            _displayName = value;
            OnPropertyChanged();
        }
    }

    public string Text
    {
        get => _text;
        set
        {
            if (_text == value)
            {
                return;
            }

            _text = value;
            IsDirty = !string.Equals(_text, _savedText, StringComparison.Ordinal);
            OnPropertyChanged();
        }
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (_isDirty == value)
            {
                return;
            }

            _isDirty = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DirtyMark));
        }
    }

    public string DirtyMark => IsDirty ? "●" : string.Empty;

    public bool IsForm => string.Equals(
        Path.GetExtension(FilePath ?? DisplayName),
        ".yyx",
        StringComparison.OrdinalIgnoreCase);

    /// <summary>起始页标签(启动/关闭全部文档后显示,对齐官方第一步)。</summary>
    public bool IsStartPage { get; set; }

    public string TabIcon => IsStartPage
        ? "⌂"
        : IsForm
            ? "▣"
            : IsReadOnlyGenerated
                ? "⚙"
                : "‹›";

    public bool IsStructuredCode =>
        string.Equals(Path.GetExtension(FilePath ?? DisplayName), ".yyc", StringComparison.OrdinalIgnoreCase);

    public bool IsGeneratedBinding =>
        IsStructuredCode &&
        Yy.Compiler.Forms.YyFormCompanionFiles.IsGeneratedBindingPath(FilePath ?? DisplayName);

    public bool IsResourceIndex =>
        YyResourceIndex.IsIndexPath(FilePath ?? DisplayName);

    public bool IsReadOnlyGenerated => IsGeneratedBinding || IsResourceIndex;

    /// <summary>当前 .yyc 以纯源码视图编辑；磁盘仍保存结构化 XML。</summary>
    public bool UsePlainCodeEditor
    {
        get => _usePlainCodeEditor;
        set => _usePlainCodeEditor = value;
    }

    /// <summary>同一份代码可在易语言风格窗口和纯代码窗口中呈现。</summary>
    public bool UseEasyLanguagePresentation { get; set; } = true;

    public string PlainCodeText
    {
        get => _plainCodeText;
        set
        {
            if (_plainCodeText == value)
            {
                return;
            }

            _plainCodeText = value;
            if (UsePlainCodeEditor)
            {
                IsDirty = !string.Equals(
                    _plainCodeText,
                    _savedPlainCodeText,
                    StringComparison.Ordinal);
            }
        }
    }

    public void BeginPlainCodeEditing(string source)
    {
        UsePlainCodeEditor = true;
        _plainCodeText = source;
        _savedPlainCodeText = source;
    }

    public void EndPlainCodeEditing()
    {
        UsePlainCodeEditor = false;
        _plainCodeText = string.Empty;
        _savedPlainCodeText = string.Empty;
    }

    public int CaretOffset { get; set; }

    public double VerticalOffset { get; set; }
    public Stack<string> DesignerUndo { get; } = new();
    public Stack<string> DesignerRedo { get; } = new();
    public string? LastDesignerSnapshot { get; set; }

    public void MarkSaved(string path)
    {
        FilePath = path;
        DisplayName = Path.GetFileName(path);
        _savedText = Text;
        _savedPlainCodeText = _plainCodeText;
        IsDirty = false;
    }

    public void MarkModified()
    {
        IsDirty = true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
