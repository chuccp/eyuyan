using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace Yy.Studio;

public sealed class EditorDocument : INotifyPropertyChanged
{
    private string? _filePath;
    private string _displayName;
    private string _text;
    private string _savedText;
    private bool _isDirty;

    public EditorDocument(string? filePath, string displayName, string text)
    {
        _filePath = filePath;
        _displayName = displayName;
        _text = text;
        _savedText = text;
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

    public bool IsStructuredCode =>
        string.Equals(Path.GetExtension(FilePath ?? DisplayName), ".yyc", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Path.GetExtension(FilePath ?? DisplayName), ".ey", StringComparison.OrdinalIgnoreCase);

    public bool IsLegacyCode =>
        string.Equals(Path.GetExtension(FilePath ?? DisplayName), ".ey", StringComparison.OrdinalIgnoreCase);

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
