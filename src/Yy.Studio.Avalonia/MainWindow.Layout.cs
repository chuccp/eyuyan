using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Yy.Studio.Avalonia.Layout;
using Yy.Studio.Core.Configuration;
using Yy.Studio.Core.Workspace;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private ToolWindowLayoutController? _toolWindowLayout;

    private void InitializeToolWindowLayout()
    {
        _toolWindowLayout = new ToolWindowLayoutController(
            ToolWindowHost,
            ProjectPanel,
            WorkPanel,
            ConsolePanel,
            StatusBarRow,
            ProjectSplitter,
            WorkSplitter,
            ConsoleSplitter,
            WorkTabs);
        _toolWindowLayout.LayoutChanged += (_, args) =>
        {
            Configuration.WindowLayout = args.Configuration;
            Configuration.Save();
        };
        _toolWindowLayout.Apply(Configuration.WindowLayout);
        RefreshWorkPanel();
    }

    private void OnMoveToolWindow(object? sender, RoutedEventArgs e)
    {
        if (_toolWindowLayout is null ||
            (sender as MenuItem)?.Tag is not string tag)
        {
            return;
        }

        var parts = tag.Split('|');
        if (parts.Length != 2 ||
            !Enum.TryParse<ToolWindowKind>(parts[0], out var kind))
        {
            return;
        }

        var dock = parts[1] switch
        {
            "Left" => ToolWindowDock.Left,
            "Right" => ToolWindowDock.Right,
            _ => ToolWindowDock.Bottom
        };
        _toolWindowLayout.MoveDock(kind, dock);
    }

    private void OnLayoutPreset(object? sender, RoutedEventArgs e)
    {
        if (_toolWindowLayout is not null &&
            (sender as MenuItem)?.Tag is string preset)
        {
            _toolWindowLayout.SetPreset(preset);
        }
    }

    private void OnWorkTreeDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ProgramOutline.SelectedItem is ProjectNode { FullPath: { } path } &&
            File.Exists(path))
        {
            OpenProjectPath(path);
        }
    }

    private void OnToggleProjectWindow(object? sender, RoutedEventArgs e) =>
        CycleToolWindow(ToolWindowKind.Project);

    private void OnToggleWorkWindow(object? sender, RoutedEventArgs e) =>
        CycleToolWindow(ToolWindowKind.Work);

    private void OnToggleConsoleWindow(object? sender, RoutedEventArgs e) =>
        CycleToolWindow(ToolWindowKind.Console);

    private void OnToggleStatusBar(object? sender, RoutedEventArgs e)
    {
        if (_toolWindowLayout is null)
        {
            return;
        }

        _toolWindowLayout.SetStatusBarVisible(!_toolWindowLayout.StatusBarVisible);
    }

    private void CycleToolWindow(ToolWindowKind kind)
    {
        if (_toolWindowLayout is null)
        {
            return;
        }

        switch (_toolWindowLayout.GetState(kind))
        {
            case ToolWindowState.Visible:
                _toolWindowLayout.Minimize(kind);
                break;
            case ToolWindowState.Minimized:
                _toolWindowLayout.Hide(kind);
                break;
            default:
                _toolWindowLayout.Show(kind);
                break;
        }
    }

    private void RefreshWorkPanel()
    {
        if (ProgramOutline is null)
        {
            return;
        }

        var root = new ProjectNode("程序", null, isDirectory: true);
        if (_workspace.Project is { } project)
        {
            var forms = new ProjectNode("窗体", null, isDirectory: true);
            foreach (var form in project.Forms)
            {
                var formNode = new ProjectNode(
                    form.Model.Title,
                    form.FormPath,
                    isDirectory: false)
                {
                    IsExpanded = true
                };
                foreach (var control in form.Model.Controls)
                {
                    formNode.Children.Add(new ProjectNode(
                        $"{control.Name}（{control.Type}）",
                        null,
                        isDirectory: false));
                }

                forms.Children.Add(formNode);
            }

            root.Children.Add(forms);
            var resources = new ProjectNode("资源", null, isDirectory: true);
            foreach (var resource in project.ResourceTable)
            {
                resources.Children.Add(new ProjectNode(
                    $"{resource.Name}（{resource.TypeName}）",
                    null,
                    isDirectory: false));
            }

            root.Children.Add(resources);
        }
        else
        {
            root.Children.Add(new ProjectNode("尚未打开工程", null, isDirectory: false));
        }

        if (CurrentDocument is { } document)
        {
            root.Children.Add(new ProjectNode(
                "文档：" + document.FileName,
                document.Path,
                isDirectory: false));
            if (document.StructuredView?.Document is { } code)
            {
                var functions = new ProjectNode("子程序", null, isDirectory: true);
                foreach (var function in code.Functions)
                {
                    functions.Children.Add(new ProjectNode(
                        function.Name,
                        null,
                        isDirectory: false));
                }

                root.Children.Add(functions);
            }
        }

        ProgramOutline.ItemsSource = new[] { root };
        PropertySummary.Text = CurrentDocument switch
        {
            null => "选择代码、窗体或控件后显示摘要。",
            { FormView.CurrentForm: { } form } =>
                $"{form.Name}\n{form.Title}\n{form.Width} × {form.Height}",
            { FormView.SelectedControl: { } control } =>
                $"{control.Name}\n{control.Type}\n" +
                $"位置 {control.X:0.#}, {control.Y:0.#}\n" +
                $"尺寸 {control.Width:0.#} × {control.Height:0.#}",
            { StructuredView.Document: { } code } =>
                $"{CurrentDocument?.FileName}\n{code.Functions.Count} 个子程序",
            { } other => other.FileName
        };
    }
}
