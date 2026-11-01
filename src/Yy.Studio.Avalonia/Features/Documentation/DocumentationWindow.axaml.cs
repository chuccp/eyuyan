using Avalonia.Controls;
using Avalonia.Input;
using Yy.Studio.Core.Documentation;

namespace Yy.Studio.Avalonia.Features.Documentation;

/// <summary>可搜索的内置开发文档浏览器，内容来自平台无关的 DocumentationCatalog。</summary>
public sealed partial class DocumentationWindow : Window
{
    private const string AllSections = "全部章节";
    private bool _updatingTopics;

    public DocumentationWindow()
        : this(null)
    {
    }

    public DocumentationWindow(string? query)
    {
        InitializeComponent();

        _updatingTopics = true;
        try
        {
            var sections = new List<string> { AllSections };
            sections.AddRange(DocumentationCatalog.Topics
                .Select(topic => topic.Section)
                .Distinct(StringComparer.Ordinal));
            SectionFilter.ItemsSource = sections;
            SectionFilter.SelectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(query))
            {
                SearchBox.Text = query;
            }
        }
        finally
        {
            _updatingTopics = false;
        }

        LoadTopics();
        Opened += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(query))
            {
                FocusSearch();
            }
        };
    }

    /// <summary>聚焦搜索框并全选现有查询，供 Ctrl+F / ⌘F 调用。</summary>
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (!_updatingTopics)
        {
            LoadTopics();
        }
    }

    private void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updatingTopics)
        {
            LoadTopics();
        }
    }

    private void OnTopicSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_updatingTopics &&
            TopicList.SelectedItem is DocumentationNavigationItem item)
        {
            ShowTopic(item.Topic);
        }
    }

    private void LoadTopics()
    {
        _updatingTopics = true;
        try
        {
            var selectedTitle =
                (TopicList.SelectedItem as DocumentationNavigationItem)?.Topic.Title;
            IEnumerable<DocumentationTopic> topics =
                DocumentationCatalog.Search(SearchBox.Text);
            var section = SectionFilter.SelectedItem as string;
            if (!string.IsNullOrEmpty(section) && section != AllSections)
            {
                topics = topics.Where(topic => topic.Section == section);
            }

            var filtered = topics.ToArray();
            var items = new List<DocumentationNavigationItem>(filtered.Length);
            string? previousSection = null;
            foreach (var topic in filtered)
            {
                items.Add(new DocumentationNavigationItem(
                    topic,
                    previousSection == topic.Section ? null : topic.Section));
                previousSection = topic.Section;
            }

            TopicList.ItemsSource = items;
            TopicList.IsVisible = items.Count > 0;
            NoResultsText.IsVisible = items.Count == 0;

            if (items.Count == 0)
            {
                ShowEmptySearch();
                return;
            }

            var target = selectedTitle is null
                ? items[0]
                : items.FirstOrDefault(item =>
                      string.Equals(
                          item.Topic.Title,
                          selectedTitle,
                          StringComparison.Ordinal)) ?? items[0];
            TopicList.SelectedItem = target;
            ShowTopic(target.Topic);
        }
        finally
        {
            _updatingTopics = false;
        }
    }

    private void ShowTopic(DocumentationTopic topic)
    {
        DocSectionText.Text = topic.Section;
        DocTitleText.Text = topic.Title;
        DocSummaryText.Text = topic.Summary;
        BlocksPanel.Children.Clear();
        foreach (var block in topic.Blocks)
        {
            BlocksPanel.Children.Add(CreateDocumentationBlock(block));
        }
    }

    private void ShowEmptySearch()
    {
        DocSectionText.Text = "搜索结果";
        DocTitleText.Text = "没有匹配的文档";
        var query = SearchBox.Text?.Trim();
        DocSummaryText.Text = string.IsNullOrEmpty(query)
            ? "开发文档暂时不可用。"
            : $"没有找到包含“{query}”的标题、语法、包名、命令或说明。";
        BlocksPanel.Children.Clear();
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.F &&
                 (e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                  e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
        {
            FocusSearch();
            e.Handled = true;
        }
    }
}

/// <summary>文档导航项；节标题内联在搜索结果第一项前显示。</summary>
public sealed class DocumentationNavigationItem
{
    public DocumentationNavigationItem(
        DocumentationTopic topic,
        string? sectionHeader)
    {
        Topic = topic;
        SectionHeader = sectionHeader;
    }

    public DocumentationTopic Topic { get; }

    public string? SectionHeader { get; }

    public bool HasSectionHeader => SectionHeader is not null;

    public string Title => Topic.Title;

    public string Summary => Topic.Summary;
}
