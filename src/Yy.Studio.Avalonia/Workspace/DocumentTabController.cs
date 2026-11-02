using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Yy.Studio.Avalonia.Workspace;

/// <summary>标准文档标签外观:标题、脏标记和关闭按钮。</summary>
public sealed record DocumentTabHandle(
    TabItem Tab,
    TextBlock Title,
    TextBlock DirtyMark,
    Button CloseButton);

/// <summary>
/// 文档标签条统一封装。标签外观、选择切换、关闭路由、脏标记和右键菜单
/// 集中在此;业务窗口通过事件响应保存/关闭,不再直接操作 TabControl。
/// </summary>
public sealed class DocumentTabController
{
    private static readonly IBrush MutedBrush = Brush.Parse("#6C707E");
    private static readonly IBrush DirtyBrush = Brush.Parse("#3574F0");

    private readonly TabControl _tabs;
    private readonly List<EditorDocument> _documents = [];

    public DocumentTabController(TabControl tabs)
    {
        _tabs = tabs ?? throw new ArgumentNullException(nameof(tabs));
        _tabs.SelectionChanged += (_, _) => SelectionChanged?.Invoke(Current);
    }

    /// <summary>当前打开的全部文档,按打开顺序。</summary>
    public IReadOnlyList<EditorDocument> Documents => _documents;

    /// <summary>当前选中文档;起始页或无标签时为空。</summary>
    public EditorDocument? Current =>
        _tabs.SelectedItem is TabItem tab
            ? _documents.FirstOrDefault(document => ReferenceEquals(document.Tab, tab))
            : null;

    /// <summary>标签选择变化;参数为当前文档,起始页时为空。</summary>
    public event Action<EditorDocument?>? SelectionChanged;

    /// <summary>用户请求保存(右键菜单)。</summary>
    public event Action<EditorDocument>? SaveRequested;

    /// <summary>用户请求关闭(关闭按钮/中键/右键菜单)。</summary>
    public event Action<EditorDocument>? CloseRequested;

    /// <summary>用户请求关闭其他标签。</summary>
    public event Action<EditorDocument?>? CloseOthersRequested;

    /// <summary>最后一个文档标签被移除;宿主应显示起始页。</summary>
    public event Action? Emptied;

    public EditorDocument? Find(string fullPath) =>
        _documents.FirstOrDefault(document =>
            string.Equals(document.Path, fullPath, StringComparison.Ordinal));

    /// <summary>创建标准文档标签:图标 + 标题 + 脏标记 + 关闭按钮 + 右键菜单。</summary>
    public DocumentTabHandle CreateTab(
        string path,
        Control content,
        string? icon = null,
        bool dirtyCapable = true)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 5
        };
        panel.Children.Add(new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(2),
            Background = Brush.Parse(FileTypeColor(path)),
            VerticalAlignment = VerticalAlignment.Center
        });

        var title = new TextBlock
        {
            Text = System.IO.Path.GetFileName(path),
            FontSize = 12,
            MaxWidth = 170,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        panel.Children.Add(title);
        var dirtyMark = new TextBlock
        {
            Text = string.Empty,
            FontSize = 11,
            Foreground = DirtyBrush,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (dirtyCapable)
        {
            panel.Children.Add(dirtyMark);
        }

        var closeButton = new Button
        {
            Content = "×",
            Padding = new Thickness(4, 0),
            Margin = new Thickness(3, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = MutedBrush,
            FontSize = 14
        };
        ToolTip.SetTip(closeButton, "关闭标签页");
        panel.Children.Add(closeButton);
        var tab = new TabItem
        {
            Header = panel,
            Content = content,
            Tag = path
        };
        return new DocumentTabHandle(tab, title, dirtyMark, closeButton);
    }

    /// <summary>挂接文档标签:关闭按钮、中键、右键菜单,加入标签条并按需选中。</summary>
    public void Attach(EditorDocument document, bool select = true)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.CloseButton is not null)
        {
            document.CloseButton.Click += (_, _) => CloseRequested?.Invoke(document);
        }

        document.Tab.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(document.Tab).Properties.IsMiddleButtonPressed)
            {
                CloseRequested?.Invoke(document);
            }
        };
        document.Tab.ContextMenu = BuildDocumentMenu(document);
        _documents.Add(document);
        _tabs.Items.Add(document.Tab);
        if (select)
        {
            _tabs.SelectedItem = document.Tab;
        }
    }

    /// <summary>添加非文档标签(如起始页)并选中;提供独立关闭入口。</summary>
    public void AddExtra(TabItem tab, Action onClose, bool select = true)
    {
        ArgumentNullException.ThrowIfNull(tab);
        ArgumentNullException.ThrowIfNull(onClose);
        tab.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(tab).Properties.IsMiddleButtonPressed)
            {
                onClose();
            }
        };
        var menu = new ContextMenu();
        var close = new MenuItem { Header = "关闭" };
        close.Click += (_, _) => onClose();
        menu.Items.Add(close);
        tab.ContextMenu = menu;
        _tabs.Items.Add(tab);
        if (select)
        {
            _tabs.SelectedItem = tab;
        }
    }

    public void RemoveExtra(TabItem tab) => _tabs.Items.Remove(tab);

    /// <summary>选中指定文档的标签。</summary>
    public void Select(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _tabs.SelectedItem = document.Tab;
    }

    /// <summary>选中指定标签。</summary>
    public void Select(TabItem tab) => _tabs.SelectedItem = tab;

    /// <summary>移除文档标签;最后一个被移除后触发 <see cref="Emptied"/>。</summary>
    public void Remove(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        _documents.Remove(document);
        _tabs.Items.Remove(document.Tab);
        if (_documents.Count == 0)
        {
            Emptied?.Invoke();
        }
    }

    /// <summary>刷新标签头:文件名与脏标记。</summary>
    public void UpdateHeader(EditorDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.HeaderText.Text = document.FileName;
        if (document.DirtyMarkText is not null)
        {
            document.DirtyMarkText.Text = document.IsDirty ? "●" : string.Empty;
        }
    }

    private static string FileTypeColor(string path)
    {
        var extension = System.IO.Path.GetExtension(path);
        if (string.Equals(extension, ".yyx", StringComparison.OrdinalIgnoreCase))
        {
            return "#208A3C";
        }

        if (string.Equals(extension, ".yyc", StringComparison.OrdinalIgnoreCase))
        {
            return path.EndsWith(
                ".Designer.yyc",
                StringComparison.OrdinalIgnoreCase)
                ? "#A8ADBD"
                : "#3574F0";
        }

        if (string.Equals(extension, ".yyproj", StringComparison.OrdinalIgnoreCase))
        {
            return "#A46704";
        }

        return "#6C707E";
    }

    private ContextMenu BuildDocumentMenu(EditorDocument document)
    {
        var menu = new ContextMenu();
        var save = new MenuItem { Header = "保存", InputGesture = KeyGesture.Parse("Ctrl+S") };
        save.Click += (_, _) => SaveRequested?.Invoke(document);
        var close = new MenuItem { Header = "关闭" };
        close.Click += (_, _) => CloseRequested?.Invoke(document);
        var closeOthers = new MenuItem { Header = "关闭其他" };
        closeOthers.Click += (_, _) => CloseOthersRequested?.Invoke(document);
        menu.Items.Add(save);
        menu.Items.Add(new Separator());
        menu.Items.Add(close);
        menu.Items.Add(closeOthers);
        return menu;
    }
}
