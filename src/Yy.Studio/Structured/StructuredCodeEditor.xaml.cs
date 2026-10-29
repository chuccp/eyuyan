using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Yy.Language.Structured;
using Yy.Studio.Forms;

namespace Yy.Studio.Structured;

public partial class StructuredCodeEditor : UserControl
{
    private readonly ObservableCollection<FunctionItem> _functionItems = [];
    private YyCodeDocument? _document;
    private FunctionItem? _selectedFunction;
    private TreeEntry? _selectedEntry;
    private IReadOnlyList<YyControlModel> _controls = [];
    private YyFormModel? _associatedForm;
    private bool _updating;

    public StructuredCodeEditor()
    {
        InitializeComponent();
        StatementKindBox.ItemsSource = YyCodeStatementKinds.All;
        StatementKindBox.SelectedItem = YyCodeStatementKinds.Call;
        FunctionList.ItemsSource = _functionItems;
        PropertyTargetBox.ItemsSource = new[] { "文本", "可见", "启用", "选中", "左", "上", "宽", "高" };
        PropertyTargetBox.SelectedIndex = 0;
    }

    public event EventHandler? ModelChanged;
    public event EventHandler<DesignerActivationEventArgs>? DesignerActivationRequested;

    public YyCodeDocument? Document => _document;

    public void SetControls(IReadOnlyList<YyControlModel> controls)
    {
        _controls = controls;
        ControlTargetBox.ItemsSource = controls.Select(control => control.Name).ToArray();
        ControlTargetBox.SelectedIndex = controls.Count > 0 ? 0 : -1;
    }

    public void SetDesignContext(YyFormModel? form)
    {
        _associatedForm = form;
        SetControls(form?.Controls ?? []);
        AssociatedFormText.Text = form is null ? "未关联窗体" : $"{form.Name} · {form.Title}";
        OpenDesignerButton.IsEnabled = form is not null;
        FormatTitle.Text = "结构化代码";
        SourceFormatText.Text = "YYC";
        SourceFormatBadge.Background = YyFormRenderer.Brush("#264F78");
        SourceFormatBadge.ToolTip = ".yyc 是可直接编辑的结构化工程源码";
        WorkflowHint.Text = "选择容器步骤后添加子步骤；F5 运行，Ctrl+B 编译";
        RefreshAssociation();
    }

    public void LoadDocument(YyCodeDocument document)
    {
        _document = document;
        _functionItems.Clear();
        _functionItems.Add(new FunctionItem("程序入口", null));
        foreach (var function in document.Functions)
        {
            _functionItems.Add(new FunctionItem(function.Name, function));
        }

        FunctionList.SelectedIndex = 0;
        RefreshAssociation();
    }

    public void SelectFunction(string functionName)
    {
        var item = _functionItems.FirstOrDefault(candidate =>
            string.Equals(candidate.Function?.Name, functionName, StringComparison.Ordinal));
        if (item is not null)
        {
            FunctionList.SelectedItem = item;
            FunctionList.ScrollIntoView(item);
            if (StatementTree.Items.Count > 0 &&
                StatementTree.Items[0] is TreeViewItem root &&
                root.Items.Count > 0 &&
                root.Items[0] is TreeViewItem firstStatement)
            {
                firstStatement.IsSelected = true;
                firstStatement.BringIntoView();
            }
        }
    }

    private void OnFunctionSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        _selectedFunction = FunctionList.SelectedItem as FunctionItem;
        BuildStatementTree();
        PopulateFunctionProperties();
    }

    private void BuildStatementTree()
    {
        StatementTree.Items.Clear();
        var statements = _selectedFunction?.Function?.Statements ?? _document?.MainStatements;
        if (statements is null)
        {
            return;
        }

        var root = new TreeViewItem
        {
            Header = CreateStatementHeader("开始", _selectedFunction?.DisplayName ?? "程序入口", "#007ACC"),
            Tag = new TreeEntry(null, statements),
            IsExpanded = true
        };
        AddStatements(root, statements);
        StatementTree.Items.Add(root);
        root.IsSelected = true;
        _selectedEntry = root.Tag as TreeEntry;
        PopulateStatementProperties();
    }

    private void AddStatements(TreeViewItem parent, List<YyCodeStatement> statements)
    {
        foreach (var statement in statements)
        {
            var item = new TreeViewItem
            {
                Header = CreateStatementHeader(statement.Kind, Describe(statement), "#569CD6"),
                Tag = new TreeEntry(statement, statements),
                IsExpanded = true
            };
            if (statement.Children.Count > 0)
            {
                AddStatements(item, statement.Children);
            }

            if (statement.Kind is YyCodeStatementKinds.If or YyCodeStatementKinds.Try)
            {
                var branch = new TreeViewItem
                {
                    Header = CreateStatementHeader(
                        statement.Kind == YyCodeStatementKinds.If ? "否则" : "捕获",
                        statement.Kind == YyCodeStatementKinds.If
                            ? "条件不成立时执行"
                            : $"捕获到 {statement.CatchName} 时执行",
                        "#8B8B8B"),
                    Tag = new TreeEntry(null, statement.ElseChildren, IsBranch: true)
                };
                AddStatements(branch, statement.ElseChildren);
                item.Items.Add(branch);
            }

            parent.Items.Add(item);
        }
    }

    private static Border CreateStatementHeader(string kind, string text, string color)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var badge = new Border
        {
            Background = YyFormRenderer.Brush(color),
            CornerRadius = new CornerRadius(2),
            Padding = new Thickness(7, 3, 7, 3),
            Child = new TextBlock
            {
                Text = kind,
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 10
            }
        };
        var description = new TextBlock
        {
            Text = text,
            Margin = new Thickness(9, 2, 0, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(description, 1);
        grid.Children.Add(badge);
        grid.Children.Add(description);
        return new Border
        {
            Background = YyFormRenderer.Brush("#2D2D30"),
            BorderBrush = YyFormRenderer.Brush("#3F3F46"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8, 4, 10, 4),
            Child = grid
        };
    }

    private static string Describe(YyCodeStatement statement)
    {
        return statement.Kind switch
        {
            YyCodeStatementKinds.Variable or YyCodeStatementKinds.Constant =>
                $"{statement.Name} = {Short(statement.Expression)}",
            YyCodeStatementKinds.Assign =>
                $"{statement.Target} {statement.Operator} {Short(statement.Expression)}",
            YyCodeStatementKinds.Call => Short(statement.Expression),
            YyCodeStatementKinds.If => $"如果 {Short(statement.Condition)}",
            YyCodeStatementKinds.While => $"当 {Short(statement.Condition)} 循环",
            YyCodeStatementKinds.CountLoop =>
                $"{statement.Variable} 从 {statement.From} 到 {statement.To}",
            YyCodeStatementKinds.ForEach =>
                $"{statement.Variable} 于 {Short(statement.Expression)}",
            YyCodeStatementKinds.Try => "执行并捕获错误",
            YyCodeStatementKinds.Return => "返回 " + Short(statement.Expression),
            _ => statement.Kind
        };
    }

    private static string Short(string value) =>
        value.Length <= 44 ? value : value[..41] + "...";

    private void OnStatementSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _selectedEntry = (e.NewValue as TreeViewItem)?.Tag as TreeEntry;
        PopulateStatementProperties();
    }

    private void RefreshAssociation()
    {
        ComponentTree.Items.Clear();
        if (_associatedForm is null || _document is null)
        {
            AssociationSummaryText.Text = "选择同名 .yyx 或项目内代码后置后显示组件事件";
            return;
        }

        var association = YyCodeAssociation.Build(_associatedForm, _document);
        AssociationSummaryText.Text = association.MissingEventCount == 0
            ? $"{association.Controls.Count} 个控件 · {association.EventCount} 个事件已关联"
            : $"{association.Controls.Count} 个控件 · {association.EventCount} 个事件 · " +
              $"{association.MissingEventCount} 个待实现";

        var formItem = new TreeViewItem
        {
            Header = CreateAssociationHeader(
                "▣",
                $"{association.FormName} · 窗体",
                association.FormEvents.Any(item => !item.IsImplemented) ? "有待实现事件" : "已关联",
                "#007ACC"),
            IsExpanded = true,
            ToolTip = "双击在窗体设计器中打开"
        };
        formItem.MouseDoubleClick += OnAssociationDoubleClick;
        AddEventItems(formItem, association.FormEvents);

        foreach (var control in association.Controls)
        {
            var controlItem = new TreeViewItem
            {
                Header = CreateAssociationHeader(
                    "◇",
                    $"{control.ControlName} · {control.ControlType}",
                    control.Events.Count == 0 ? "无事件绑定" : $"{control.Events.Count} 个事件",
                    control.Events.Count == 0 ? "#6F7782" : "#4EC9B0"),
                Tag = new ControlAssociationEntry(control.ControlName),
                ToolTip = "双击在设计器中定位此控件"
            };
            controlItem.MouseDoubleClick += OnAssociationDoubleClick;
            AddEventItems(controlItem, control.Events);
            formItem.Items.Add(controlItem);
        }

        ComponentTree.Items.Add(formItem);
    }

    private void AddEventItems(
        TreeViewItem parent,
        IReadOnlyList<YyCodeEventAssociation> events)
    {
        foreach (var eventLink in events)
        {
            var eventItem = new TreeViewItem
            {
                Header = CreateAssociationHeader(
                    eventLink.IsImplemented ? "ƒ" : "!",
                    $"{eventLink.EventDisplayName} · {eventLink.EventName}",
                    eventLink.IsImplemented ? "代码已实现" : "代码未实现",
                    eventLink.IsImplemented ? "#DCDCAA" : "#D7BA7D"),
                Tag = new EventAssociationEntry(eventLink),
                ToolTip = eventLink.IsImplemented
                    ? "选择以定位事件子程序"
                    : "窗体声明了此事件，但当前代码文件中没有对应子程序"
            };
            parent.Items.Add(eventItem);
        }
    }

    private static Border CreateAssociationHeader(
        string icon,
        string title,
        string status,
        string color)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var iconText = new TextBlock
        {
            Text = icon,
            Foreground = YyFormRenderer.Brush(color),
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleText = new TextBlock
        {
            Text = title,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var statusText = new TextBlock
        {
            Text = status,
            Foreground = YyFormRenderer.Brush(color),
            FontSize = 9,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(titleText, 1);
        Grid.SetColumn(statusText, 2);
        grid.Children.Add(iconText);
        grid.Children.Add(titleText);
        grid.Children.Add(statusText);
        return new Border
        {
            Background = YyFormRenderer.Brush("#2D2D30"),
            Padding = new Thickness(4, 3, 4, 3),
            Child = grid
        };
    }

    private void OnComponentTreeSelectionChanged(
        object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if ((e.NewValue as TreeViewItem)?.Tag is EventAssociationEntry entry &&
            entry.EventLink.IsImplemented)
        {
            SelectFunction(entry.EventLink.EventName);
        }
    }

    private void OnAssociationDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is TreeViewItem item)
        {
            var controlName = item.Tag switch
            {
                ControlAssociationEntry control => control.ControlName,
                EventAssociationEntry @event => @event.EventLink.ControlName,
                _ => null
            };
            DesignerActivationRequested?.Invoke(
                this,
                new DesignerActivationEventArgs(controlName));
            e.Handled = true;
        }
    }

    private void OnOpenDesigner(object sender, RoutedEventArgs e)
    {
        if (_associatedForm is not null)
        {
            DesignerActivationRequested?.Invoke(this, new DesignerActivationEventArgs(null));
        }
    }

    private void PopulateFunctionProperties()
    {
        _updating = true;
        try
        {
            var function = _selectedFunction?.Function;
            FunctionProperties.Visibility = function is null ? Visibility.Collapsed : Visibility.Visible;
            StatementProperties.Visibility = function is null ? Visibility.Collapsed : Visibility.Visible;
            InspectorTitle.Text = function?.Name ?? "程序入口";
            InspectorSubtitle.Text = function is null
                ? "项目启动时执行"
                : $"{function.Parameters.Count} 个参数";
            if (function is not null)
            {
                FunctionNameBox.Text = function.Name;
                ReturnTypeBox.Text = function.ReturnType;
                ParametersBox.Text = string.Join(
                    ", ",
                    function.Parameters.Select(parameter =>
                        parameter.Name +
                        (string.IsNullOrWhiteSpace(parameter.TypeName) ? string.Empty : ": " + parameter.TypeName) +
                        (string.IsNullOrWhiteSpace(parameter.DefaultExpression)
                            ? string.Empty
                            : " = " + parameter.DefaultExpression)));
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void PopulateStatementProperties()
    {
        _updating = true;
        try
        {
            var statement = _selectedEntry?.Step;
            if (statement is null)
            {
                FunctionProperties.Visibility =
                    _selectedFunction?.Function is null ? Visibility.Collapsed : Visibility.Visible;
                StatementProperties.Visibility = Visibility.Collapsed;
                InspectorSubtitle.Text = _selectedEntry?.IsBranch == true
                    ? "选择此“否则/捕获”节点后添加分支步骤"
                    : "选择左侧子程序后编辑参数";
                return;
            }

            FunctionProperties.Visibility = Visibility.Collapsed;
            StatementProperties.Visibility = Visibility.Visible;
            InspectorTitle.Text = statement.Kind;
            InspectorSubtitle.Text = Describe(statement);
            NameBox.Text = statement.Name;
            TypeBox.Text = statement.TypeName;
            TargetBox.Text = statement.Target;
            ExpressionBox.Text = statement.Expression;
            ConditionBox.Text = statement.Condition;
            FromBox.Text = statement.From;
            ToBox.Text = statement.To;
            StepBox.Text = statement.Step;
            CatchBox.Text = statement.CatchName;

            NameBox.IsEnabled = statement.Kind is YyCodeStatementKinds.Variable
                or YyCodeStatementKinds.Constant or YyCodeStatementKinds.CountLoop
                or YyCodeStatementKinds.ForEach;
            TypeBox.IsEnabled = statement.Kind is YyCodeStatementKinds.Variable
                or YyCodeStatementKinds.Constant;
            TargetBox.IsEnabled = statement.Kind == YyCodeStatementKinds.Assign;
            ExpressionBox.IsEnabled = statement.Kind is not (YyCodeStatementKinds.Break
                or YyCodeStatementKinds.Continue or YyCodeStatementKinds.If
                or YyCodeStatementKinds.While);
            ConditionBox.IsEnabled = statement.Kind is YyCodeStatementKinds.If or YyCodeStatementKinds.While;
            FromBox.IsEnabled = statement.Kind == YyCodeStatementKinds.CountLoop;
            ToBox.IsEnabled = statement.Kind == YyCodeStatementKinds.CountLoop;
            StepBox.IsEnabled = statement.Kind == YyCodeStatementKinds.CountLoop;
            CatchBox.IsEnabled = statement.Kind == YyCodeStatementKinds.Try;
            ComponentTargetPanel.Visibility = statement.Kind == YyCodeStatementKinds.Assign
                ? Visibility.Visible
                : Visibility.Collapsed;
            if (statement.Kind == YyCodeStatementKinds.Assign)
            {
                var targetParts = statement.Target.Split('.', 2, StringSplitOptions.TrimEntries);
                if (targetParts.Length == 2)
                {
                    ControlTargetBox.SelectedItem = targetParts[0];
                    PropertyTargetBox.SelectedItem = targetParts[1];
                }
            }
        }
        finally
        {
            _updating = false;
        }
    }

    private void OnAddFunction(object sender, RoutedEventArgs e)
    {
        if (_document is null)
        {
            return;
        }

        var index = _document.Functions.Count + 1;
        string name;
        do
        {
            name = "子程序" + index++;
        }
        while (_document.Functions.Any(function =>
            string.Equals(function.Name, name, StringComparison.Ordinal)));

        var function = new YyCodeFunction { Name = name };
        _document.Functions.Add(function);
        var item = new FunctionItem(name, function);
        _functionItems.Add(item);
        FunctionList.SelectedItem = item;
        NotifyChanged();
    }

    private void OnAddStatement(object sender, RoutedEventArgs e)
    {
        if (_selectedFunction is null || StatementKindBox.SelectedItem is not string kind)
        {
            return;
        }

        var statement = new YyCodeStatement { Kind = kind };
        var collection = _selectedEntry?.Step is { } selected &&
                         YyCodeStatementKinds.IsContainer(selected.Kind)
            ? selected.Children
            : _selectedEntry?.Siblings ?? _selectedFunction.Function?.Statements;
        if (collection is null)
        {
            return;
        }

        collection.Add(statement);
        BuildStatementTree();
        NotifyChanged();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry?.Step is not { } statement)
        {
            return;
        }

        _selectedEntry.Siblings.Remove(statement);
        BuildStatementTree();
        NotifyChanged();
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int direction)
    {
        if (_selectedEntry?.Step is not { } statement)
        {
            return;
        }

        var index = _selectedEntry.Siblings.IndexOf(statement);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _selectedEntry.Siblings.Count)
        {
            return;
        }

        (_selectedEntry.Siblings[index], _selectedEntry.Siblings[target]) =
            (_selectedEntry.Siblings[target], _selectedEntry.Siblings[index]);
        BuildStatementTree();
        NotifyChanged();
    }

    private void OnFunctionPropertyChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || _selectedFunction?.Function is not { } function)
        {
            return;
        }

        function.Name = FunctionNameBox.Text.Trim();
        function.ReturnType = ReturnTypeBox.Text.Trim();
        function.Parameters.Clear();
        foreach (var part in ParametersBox.Text.Split(
                     [',', '，'],
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var defaultParts = part.Split('=', 2, StringSplitOptions.TrimEntries);
            var typeParts = defaultParts[0].Split(':', 2, StringSplitOptions.TrimEntries);
            function.Parameters.Add(new YyCodeParameter
            {
                Name = typeParts[0],
                TypeName = typeParts.Length > 1 ? typeParts[1] : string.Empty,
                DefaultExpression = defaultParts.Length > 1 ? defaultParts[1] : string.Empty
            });
        }

        _selectedFunction.DisplayName = function.Name;
        FunctionList.Items.Refresh();
        NotifyChanged();
    }

    private void OnStatementPropertyChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || _selectedEntry?.Step is not { } statement)
        {
            return;
        }

        statement.Name = NameBox.Text.Trim();
        statement.TypeName = TypeBox.Text.Trim();
        statement.Target = TargetBox.Text.Trim();
        statement.Expression = ExpressionBox.Text.Trim();
        statement.Condition = ConditionBox.Text.Trim();
        statement.From = FromBox.Text.Trim();
        statement.To = ToBox.Text.Trim();
        statement.Step = StepBox.Text.Trim();
        statement.CatchName = CatchBox.Text.Trim();
        NotifyChanged();
    }

    private void OnComponentTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _selectedEntry?.Step is not { Kind: YyCodeStatementKinds.Assign } statement ||
            ControlTargetBox.SelectedItem is not string control)
        {
            return;
        }

        var property = PropertyTargetBox.SelectedItem as string ?? "文本";
        statement.Target = control + "." + property;
        _updating = true;
        try
        {
            TargetBox.Text = statement.Target;
        }
        finally
        {
            _updating = false;
        }

        NotifyChanged();
    }

    private void NotifyChanged() => ModelChanged?.Invoke(this, EventArgs.Empty);

    private sealed class FunctionItem(string displayName, YyCodeFunction? function)
    {
        public string DisplayName { get; set; } = displayName;
        public YyCodeFunction? Function { get; } = function;
        public override string ToString() => DisplayName;
    }

    private sealed record TreeEntry(
        YyCodeStatement? Step,
        List<YyCodeStatement> Siblings,
        bool IsBranch = false);

    private sealed record ControlAssociationEntry(string ControlName);
    private sealed record EventAssociationEntry(YyCodeEventAssociation EventLink);
}

public sealed class DesignerActivationEventArgs(string? controlName) : EventArgs
{
    public string? ControlName { get; } = controlName;
}
