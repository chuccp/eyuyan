using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Yy.Studio.Core.Configuration;

namespace Yy.Studio.Avalonia.Features.Settings;

/// <summary>JetBrains 风格的 Studio 设置窗口，编辑平台无关的 YyStudio.json 配置模型。</summary>
public sealed partial class SettingsWindow : Window
{
    private readonly StudioConfiguration _configuration;
    private readonly List<ShortcutRow> _shortcutRows;

    public SettingsWindow()
        : this(new StudioConfiguration())
    {
    }

    public SettingsWindow(StudioConfiguration configuration)
    {
        InitializeComponent();
        _configuration = configuration.Clone();
        _shortcutRows = _configuration.Shortcuts
            .Select(pair => new ShortcutRow(pair.Key, string.Join(", ", pair.Value)))
            .OrderBy(row => row.Name, StringComparer.Ordinal)
            .ToList();
        ShortcutList.ItemsSource = _shortcutRows;
        ConfigPathBox.Text =
            _configuration.ConfigurationPath ?? StudioConfiguration.UserFilePath;
        LoadConfiguration();
        SettingsCategories.SelectedIndex = 0;
    }

    public event Action<StudioConfiguration>? Applied;

    /// <summary>最近一次成功保存并经过完整校验的配置。</summary>
    public StudioConfiguration? SavedConfiguration { get; private set; }

    private void LoadConfiguration()
    {
        FontSizeBox.Text = _configuration.Editor.FontSize.ToString("0.#");
        TabWidthBox.Text = _configuration.Editor.TabWidth.ToString();
        SnapGridCheck.IsChecked = _configuration.Designer.SnapGrid;
        McpEnabledCheck.IsChecked = _configuration.Mcp.Enabled;
        McpPortBox.Text = _configuration.Mcp.Port.ToString();
        McpAutoPortCheck.IsChecked = _configuration.Mcp.AutoFindAvailablePort;
        McpAttemptsBox.Text = _configuration.Mcp.PortAttempts.ToString();
        ShowNewProgramDialogCheck.IsChecked =
            _configuration.Startup.ShowNewProgramDialog;
    }

    private void OnCategoryChanged(object? sender, SelectionChangedEventArgs e)
    {
        var category = SettingsCategories.SelectedIndex switch
        {
            1 => "Designer",
            2 => "Mcp",
            3 => "Shortcuts",
            4 => "Files",
            _ => "Editor"
        };
        EditorPanel.IsVisible = category == "Editor";
        DesignerPanel.IsVisible = category == "Designer";
        McpPanel.IsVisible = category == "Mcp";
        ShortcutsPanel.IsVisible = category == "Shortcuts";
        FilesPanel.IsVisible = category == "Files";
        SectionTitleText.Text = category switch
        {
            "Editor" => "编辑器",
            "Designer" => "外观与窗体",
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
            _configuration.Mcp.Enabled = McpEnabledCheck.IsChecked == true;
            _configuration.Mcp.Port = mcpPort;
            _configuration.Mcp.AutoFindAvailablePort =
                McpAutoPortCheck.IsChecked == true;
            _configuration.Mcp.PortAttempts = attempts;
            _configuration.Startup.ShowNewProgramDialog =
                ShowNewProgramDialogCheck.IsChecked == true;
            _configuration.Shortcuts = _shortcutRows.ToDictionary(
                row => row.Name,
                row => SplitGestures(row.Gestures),
                StringComparer.Ordinal);
            _configuration.Validate();
            _configuration.Save();
            ValidationText.Foreground = Brush.Parse("#1A7F37");
            ValidationText.Text = "设置已保存。";
            SavedConfiguration = _configuration;
            Applied?.Invoke(_configuration);
            return true;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            ValidationText.Foreground = Brush.Parse("#C42B1C");
            ValidationText.Text = exception.Message;
            return false;
        }
    }

    private static List<string> SplitGestures(string? value) =>
        (value ?? string.Empty)
            .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .ToList();

    private void OnApplyClick(object? sender, RoutedEventArgs e) => ApplyChanges();

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (ApplyChanges())
        {
            Close();
        }
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();

    private void OnOpenConfigClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var path = ConfigPathBox.Text;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = StudioConfiguration.UserFilePath;
            }

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
            ValidationText.Foreground = Brush.Parse("#C42B1C");
            ValidationText.Text = "无法打开配置目录：" + exception.Message;
        }
    }
}

/// <summary>设置窗口中的一行快捷键编辑数据。</summary>
public sealed class ShortcutRow
{
    public ShortcutRow(string name, string gestures)
    {
        Name = name;
        Gestures = gestures;
    }

    public string Name { get; }

    public string Gestures { get; set; }
}
