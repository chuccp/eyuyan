using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Yy.Studio;

/// <summary>JetBrains 风格的 Studio 设置窗口。</summary>
public partial class SystemConfigDialog : Window
{
    private readonly StudioConfiguration _configuration;
    private readonly List<ShortcutRow> _shortcutRows;

    public SystemConfigDialog(StudioConfiguration configuration)
    {
        InitializeComponent();
        _configuration = configuration.Clone();
        _shortcutRows = _configuration.Shortcuts
            .Select(pair => new ShortcutRow(
                pair.Key,
                string.Join(", ", pair.Value)))
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .ToList();
        ShortcutGrid.ItemsSource = _shortcutRows;
        ConfigPathBox.Text =
            _configuration.ConfigurationPath ?? StudioConfiguration.UserFilePath;
        LoadConfiguration();
        SettingsCategories.SelectedIndex = 0;
    }

    public event Action<StudioConfiguration>? Applied;

    private void LoadConfiguration()
    {
        FontSizeBox.Text = _configuration.Editor.FontSize.ToString("0.#");
        TabWidthBox.Text = _configuration.Editor.TabWidth.ToString();
        SnapGridCheck.IsChecked = _configuration.Designer.SnapGrid;
        TargetRidCombo.SelectedIndex =
            string.Equals(_configuration.Build.TargetRid, "win-arm64", StringComparison.Ordinal)
                ? 1
                : 0;

        McpEnabledCheck.IsChecked = _configuration.Mcp.Enabled;
        McpPortBox.Text = _configuration.Mcp.Port.ToString();
        McpAutoPortCheck.IsChecked = _configuration.Mcp.AutoFindAvailablePort;
        McpAttemptsBox.Text = _configuration.Mcp.PortAttempts.ToString();
        ShowNewProgramDialogCheck.IsChecked =
            _configuration.Startup.ShowNewProgramDialog;
    }

    private void OnCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SettingsCategories.SelectedItem is not ListBoxItem { Tag: string category })
        {
            return;
        }

        EditorPanel.Visibility = category == "Editor" ? Visibility.Visible : Visibility.Collapsed;
        DesignerPanel.Visibility = category == "Designer" ? Visibility.Visible : Visibility.Collapsed;
        BuildPanel.Visibility = category == "Build" ? Visibility.Visible : Visibility.Collapsed;
        McpPanel.Visibility = category == "Mcp" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutsPanel.Visibility = category == "Shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility = category == "Files" ? Visibility.Visible : Visibility.Collapsed;
        SectionTitleText.Text = category switch
        {
            "Editor" => "编辑器",
            "Designer" => "外观与窗体",
            "Build" => "编译",
            "Mcp" => "MCP 服务",
            "Shortcuts" => "快捷键",
            _ => "配置文件"
        };
        ValidationText.Text = string.Empty;
    }

    private bool ApplyChanges()
    {
        try
        {
            ShortcutGrid.CommitEdit(DataGridEditingUnit.Cell, true);
            ShortcutGrid.CommitEdit(DataGridEditingUnit.Row, true);
            if (!double.TryParse(FontSizeBox.Text, out var fontSize) ||
                !int.TryParse(TabWidthBox.Text, out var tabWidth) ||
                !int.TryParse(McpPortBox.Text, out var mcpPort) ||
                !int.TryParse(McpAttemptsBox.Text, out var attempts))
            {
                throw new InvalidDataException("请输入有效的数字配置。");
            }

            _configuration.Editor.FontSize = fontSize;
            _configuration.Editor.TabWidth = tabWidth;
            _configuration.Designer.SnapGrid = SnapGridCheck.IsChecked == true;
            _configuration.Build.TargetRid =
                (TargetRidCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "win-x64";
            _configuration.Mcp.Enabled = McpEnabledCheck.IsChecked == true;
            _configuration.Mcp.Port = mcpPort;
            _configuration.Mcp.AutoFindAvailablePort = McpAutoPortCheck.IsChecked == true;
            _configuration.Mcp.PortAttempts = attempts;
            _configuration.Startup.ShowNewProgramDialog =
                ShowNewProgramDialogCheck.IsChecked == true;
            _configuration.Shortcuts = _shortcutRows.ToDictionary(
                row => row.Name,
                row => row.Gestures
                    .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(value => value.Trim())
                    .Where(value => value.Length > 0)
                    .ToList(),
                StringComparer.Ordinal);
            _configuration.Validate();
            _configuration.Save();
            ValidationText.Foreground = new SolidColorBrush(Color.FromRgb(26, 127, 55));
            ValidationText.Text = "设置已保存。";
            Applied?.Invoke(_configuration);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ValidationText.Foreground = new SolidColorBrush(Color.FromRgb(196, 43, 28));
            ValidationText.Text = exception.Message;
            return false;
        }
    }

    private void OnApplyClick(object sender, RoutedEventArgs e) => ApplyChanges();

    private void OnOkClick(object sender, RoutedEventArgs e)
    {
        if (ApplyChanges())
        {
            DialogResult = true;
        }
    }

    private void OnOpenConfigClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = ConfigPathBox.Text;
            var directory = Path.GetDirectoryName(path)
                ?? throw new IOException("无法确定 Studio 配置文件目录。");
            Directory.CreateDirectory(directory);
            if (!File.Exists(path))
            {
                _configuration.Save();
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ValidationText.Text = "无法打开配置目录：" + exception.Message;
        }
    }

    private sealed class ShortcutRow(string name, string gestures)
    {
        public string Name { get; } = name;
        public string Gestures { get; set; } = gestures;
    }
}
