using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace Yy.Studio;

public partial class ProjectPropertiesWindow : Window
{
    private readonly YyProject _project;

    public ProjectPropertiesWindow(YyProject project)
    {
        InitializeComponent();
        _project = project;
        ProjectNameBox.Text = project.Name;
        ProjectPathBox.Text = project.ProjectPath;
        OutputNameBox.Text = project.OutputName;
        EntrySourceBox.ItemsSource = project.SourcePaths
            .Select(path => Path.GetRelativePath(project.DirectoryPath, path).Replace('\\', '/'))
            .ToArray();
        EntrySourceBox.Text = Path.GetRelativePath(project.DirectoryPath, project.EntryPath)
            .Replace('\\', '/');
        StartupFormBox.ItemsSource = project.Forms
            .Select(form => form.Model.Name)
            .ToArray();
        StartupFormBox.Text = project.StartupForm;
        ProjectSummaryText.Text =
            $"包含 {project.SourcePaths.Count} 个源码文件、{project.Forms.Count} 个窗体和 {project.Resources.Count} 个资源。\n" +
            "修改保存后会更新项目清单并重新加载项目。";
    }

    public YyProject? UpdatedProject { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var enabled = 1;
        DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        try
        {
            var entry = EntrySourceBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(entry))
            {
                throw new InvalidOperationException("请选择项目入口源码。");
            }

            _project.UpdateSettings(
                ProjectNameBox.Text,
                entry,
                OutputNameBox.Text,
                StartupFormBox.Text);
            UpdatedProject = YyProject.LoadForDirectory(_project.DirectoryPath);
            DialogResult = true;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException
                or InvalidDataException or InvalidOperationException)
        {
            ValidationText.Text = exception.Message;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}
