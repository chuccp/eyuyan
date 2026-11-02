using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Yy.Studio.Avalonia.Features.CommandLine;

/// <summary>
/// 一个命令行标签的内容：单个终端，或向右/向下拆分出的多个终端窗格。
/// 标签条仍以一个标签为单位；拆分只在当前标签内部生效。
/// </summary>
public sealed class TerminalTabView : UserControl
{
    private readonly Func<string, CommandLineView> _createView;
    private PaneNode _root;
    private CommandLineView _activeView;

    public TerminalTabView(
        CommandLineView initialView,
        Func<string, CommandLineView> createView)
    {
        _createView = createView;
        _root = CreateLeaf(initialView);
        _activeView = initialView;
        Content = BuildContent();
    }

    public CommandLineView ActiveView => _activeView;

    public IReadOnlyList<CommandLineView> Views =>
        Leaves(_root).Select(leaf => leaf.View).ToArray();

    public event EventHandler? ActiveViewChanged;
    public event EventHandler? TitleChanged;
    public event EventHandler? StructureChanged;

    /// <summary>向右或向下拆分当前终端，新终端从当前目录启动。</summary>
    public CommandLineView Split(Orientation orientation)
    {
        var directory = _activeView.WorkingDirectory;
        var view = _createView(directory);
        var leaf = FindLeaf(_root, _activeView);
        if (leaf is null)
        {
            return view;
        }

        var replacement = new SplitNode(orientation, leaf, CreateLeaf(view))
        {
            Parent = leaf.Parent
        };
        ReplaceNode(leaf, replacement);
        _activeView = view;
        Rebuild();
        ActiveViewChanged?.Invoke(this, EventArgs.Empty);
        return view;
    }

    /// <summary>关闭指定窗格；只剩一个窗格时返回 false，由标签层关闭整个标签。</summary>
    public bool ClosePane(CommandLineView view)
    {
        if (Views.Count <= 1)
        {
            return false;
        }

        var leaf = FindLeaf(_root, view);
        if (leaf?.Parent is not { } parent)
        {
            return false;
        }

        var sibling = ReferenceEquals(parent.First, leaf)
            ? parent.Second
            : parent.First;
        var grandParent = parent.Parent;
        sibling.Parent = grandParent;
        if (grandParent is null)
        {
            _root = sibling;
        }
        else
        {
            grandParent.Replace(parent, sibling);
        }

        view.Stop();
        if (ReferenceEquals(_activeView, view))
        {
            _activeView = FirstView(sibling);
        }

        Rebuild();
        ActiveViewChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Stop()
    {
        foreach (var view in Views)
        {
            view.Stop();
        }
    }

    private void Rebuild()
    {
        foreach (var leaf in Leaves(_root))
        {
            if (leaf.Host.Parent is Panel panel)
            {
                panel.Children.Remove(leaf.Host);
            }
        }

        Content = null;
        var multiple = Views.Count > 1;
        foreach (var leaf in Leaves(_root))
        {
            leaf.Header.IsVisible = multiple;
        }

        Content = BuildContent();
        UpdateActiveVisual();
        Dispatcher.UIThread.Post(
            () => _activeView.FocusTerminal(),
            DispatcherPriority.Loaded);
        StructureChanged?.Invoke(this, EventArgs.Empty);
    }

    private Control BuildContent() => BuildNode(_root);

    private Control BuildNode(PaneNode node)
    {
        if (node is Leaf leaf)
        {
            return leaf.Host;
        }

        var split = (SplitNode)node;
        var grid = new Grid();
        var first = BuildNode(split.First);
        var second = BuildNode(split.Second);
        var splitter = new GridSplitter
        {
            Background = Brushes.Transparent,
            ResizeDirection = split.Orientation == Orientation.Horizontal
                ? GridResizeDirection.Columns
                : GridResizeDirection.Rows
        };

        if (split.Orientation == Orientation.Horizontal)
        {
            grid.ColumnDefinitions = new ColumnDefinitions("*,4,*");
            Grid.SetColumn(first, 0);
            Grid.SetColumn(splitter, 1);
            Grid.SetColumn(second, 2);
        }
        else
        {
            grid.RowDefinitions = new RowDefinitions("*,4,*");
            Grid.SetRow(first, 0);
            Grid.SetRow(splitter, 1);
            Grid.SetRow(second, 2);
        }

        grid.Children.Add(first);
        grid.Children.Add(splitter);
        grid.Children.Add(second);
        return grid;
    }

    private Leaf CreateLeaf(CommandLineView view)
    {
        var title = new TextBlock
        {
            Text = DisplayTitle(view),
            FontSize = 11,
            Foreground = Brush.Parse("#4B5058"),
            MaxWidth = 240,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var close = new Button
        {
            Content = "×",
            Classes = { "terminalPaneClose" },
            VerticalAlignment = VerticalAlignment.Center
        };
        var headerContent = new DockPanel
        {
            LastChildFill = true
        };
        DockPanel.SetDock(close, Dock.Right);
        headerContent.Children.Add(close);
        headerContent.Children.Add(title);

        var header = new Border
        {
            Classes = { "terminalPaneHeader" },
            IsVisible = false,
            Child = headerContent
        };
        var panel = new DockPanel
        {
            LastChildFill = true
        };
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(view);

        var host = new Border
        {
            Classes = { "terminalPane" },
            Child = panel
        };

        var leaf = new Leaf(view)
        {
            Host = host,
            Header = header,
            TitleText = title
        };
        close.Click += (_, _) => ClosePane(view);
        host.PointerPressed += (_, _) => Activate(view);
        view.Activated += (_, _) => Activate(view);
        view.TitleChanged += (_, _) =>
        {
            title.Text = DisplayTitle(view);
            TitleChanged?.Invoke(this, EventArgs.Empty);
        };
        return leaf;
    }

    private static string DisplayTitle(CommandLineView view) =>
        string.IsNullOrWhiteSpace(view.ShellTitle) ? "终端" : view.ShellTitle;

    private void Activate(CommandLineView view)
    {
        if (ReferenceEquals(_activeView, view) || FindLeaf(_root, view) is null)
        {
            return;
        }

        _activeView = view;
        UpdateActiveVisual();
        ActiveViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateActiveVisual()
    {
        foreach (var leaf in Leaves(_root))
        {
            var active = ReferenceEquals(leaf.View, _activeView);
            if (active)
            {
                if (!leaf.Host.Classes.Contains("active"))
                {
                    leaf.Host.Classes.Add("active");
                }
            }
            else
            {
                leaf.Host.Classes.Remove("active");
            }
        }
    }

    private void ReplaceNode(PaneNode child, PaneNode replacement)
    {
        if (child.Parent is { } parent)
        {
            parent.Replace(child, replacement);
            replacement.Parent = parent;
        }
        else
        {
            _root = replacement;
            replacement.Parent = null;
        }
    }

    private static Leaf? FindLeaf(PaneNode node, CommandLineView view) =>
        node switch
        {
            Leaf leaf when ReferenceEquals(leaf.View, view) => leaf,
            SplitNode split => FindLeaf(split.First, view) ?? FindLeaf(split.Second, view),
            _ => null
        };

    private static CommandLineView FirstView(PaneNode node) =>
        node switch
        {
            Leaf leaf => leaf.View,
            SplitNode split => FirstView(split.First),
            _ => throw new InvalidOperationException("空的终端窗格。")
        };

    private static IEnumerable<Leaf> Leaves(PaneNode node)
    {
        if (node is Leaf leaf)
        {
            yield return leaf;
            yield break;
        }

        if (node is not SplitNode split)
        {
            yield break;
        }

        foreach (var item in Leaves(split.First))
        {
            yield return item;
        }

        foreach (var item in Leaves(split.Second))
        {
            yield return item;
        }
    }

    private abstract class PaneNode
    {
        public SplitNode? Parent { get; set; }
    }

    private sealed class Leaf : PaneNode
    {
        public Leaf(CommandLineView view) => View = view;

        public CommandLineView View { get; }
        public Border Host { get; init; } = null!;
        public Border Header { get; init; } = null!;
        public TextBlock TitleText { get; init; } = null!;
    }

    private sealed class SplitNode : PaneNode
    {
        public SplitNode(Orientation orientation, PaneNode first, PaneNode second)
        {
            Orientation = orientation;
            First = first;
            Second = second;
            first.Parent = this;
            second.Parent = this;
        }

        public Orientation Orientation { get; }
        public PaneNode First { get; private set; }
        public PaneNode Second { get; private set; }

        public void Replace(PaneNode child, PaneNode replacement)
        {
            if (ReferenceEquals(First, child))
            {
                First = replacement;
            }
            else if (ReferenceEquals(Second, child))
            {
                Second = replacement;
            }

            replacement.Parent = this;
        }
    }
}
