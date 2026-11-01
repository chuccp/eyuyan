using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace Yy.Studio;

public partial class DocumentationWindow : Window
{
    private bool _updatingTopics;

    public DocumentationWindow()
    {
        InitializeComponent();
        LoadTopics(DocumentationCatalog.Topics);
    }

    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = string.IsNullOrEmpty(SearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        LoadTopics(DocumentationCatalog.Search(SearchBox.Text));
    }

    private void LoadTopics(IReadOnlyList<DocumentationTopic> topics)
    {
        _updatingTopics = true;
        try
        {
            var selectedTitle =
                (TopicList.SelectedItem as DocumentationTopic)?.Title;
            var view = new ListCollectionView(topics.ToList());
            view.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(DocumentationTopic.Section)));
            TopicList.ItemsSource = view;
            NoResultsText.Visibility =
                topics.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            TopicList.Visibility =
                topics.Count == 0 ? Visibility.Collapsed : Visibility.Visible;

            if (topics.Count == 0)
            {
                ShowEmptySearch();
                return;
            }

            TopicList.SelectedItem =
                topics.FirstOrDefault(topic =>
                    string.Equals(topic.Title, selectedTitle, StringComparison.Ordinal))
                ?? topics[0];
        }
        finally
        {
            _updatingTopics = false;
        }

        if (TopicList.SelectedItem is DocumentationTopic topic)
        {
            ShowTopic(topic);
        }
    }

    private void OnTopicSelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!_updatingTopics && TopicList.SelectedItem is DocumentationTopic topic)
        {
            ShowTopic(topic);
        }
    }

    private void ShowTopic(DocumentationTopic topic)
    {
        DocSectionText.Text = topic.Section;
        DocTitleText.Text = topic.Title;
        DocSummaryText.Text = topic.Summary;

        var document = new FlowDocument
        {
            PagePadding = new Thickness(32, 20, 46, 52),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
            FontSize = 13,
            LineHeight = 22,
            Foreground = ResourceBrush("TextBrush", Brushes.Black)
        };
        foreach (var block in topic.Blocks)
        {
            document.Blocks.Add(CreateBlock(block));
        }

        DocumentViewer.Document = document;
    }

    private void ShowEmptySearch()
    {
        DocSectionText.Text = "搜索结果";
        DocTitleText.Text = "没有匹配的文档";
        DocSummaryText.Text = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? "开发文档暂时不可用。"
            : $"没有找到包含“{SearchBox.Text.Trim()}”的标题、语法、包名、命令或说明。";
        DocumentViewer.Document = new FlowDocument();
    }

    private Paragraph CreateBlock(DocumentationBlock block)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 14)
        };
        switch (block.Kind)
        {
            case DocumentationBlockKind.Code:
                paragraph.FontFamily = new FontFamily("Cascadia Mono, Consolas");
                paragraph.FontSize = 12;
                paragraph.LineHeight = 19;
                paragraph.Background = ResourceBrush("FaceBrush", Brushes.WhiteSmoke);
                paragraph.BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray);
                paragraph.BorderThickness = new Thickness(1);
                paragraph.Padding = new Thickness(14, 11, 14, 11);
                AddLines(paragraph, block.Text);
                break;

            case DocumentationBlockKind.Api:
                var pieces = block.Text.Split('\n', 2);
                paragraph.Background = ResourceBrush("PanelBrush", Brushes.WhiteSmoke);
                paragraph.BorderBrush = ResourceBrush("BorderBrush", Brushes.LightGray);
                paragraph.BorderThickness = new Thickness(3, 0, 0, 0);
                paragraph.Padding = new Thickness(12, 9, 12, 9);
                paragraph.Inlines.Add(new Run(pieces[0])
                {
                    FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ResourceBrush("AccentBrush", Brushes.SteelBlue)
                });
                if (pieces.Length > 1)
                {
                    paragraph.Inlines.Add(new LineBreak());
                    AddLines(paragraph, pieces[1]);
                }
                break;

            case DocumentationBlockKind.Note:
                paragraph.Background = ResourceBrush("PanelBrush", Brushes.WhiteSmoke);
                paragraph.Padding = new Thickness(12, 8, 12, 8);
                paragraph.Inlines.Add(new Run("说明  ")
                {
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ResourceBrush("AccentBrush", Brushes.SteelBlue)
                });
                AddLines(paragraph, block.Text);
                break;

            default:
                AddLines(paragraph, block.Text);
                break;
        }

        return paragraph;
    }

    private static void AddLines(
        Paragraph paragraph,
        string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                paragraph.Inlines.Add(new LineBreak());
            }

            paragraph.Inlines.Add(new Run(lines[index]));
        }
    }

    private Brush ResourceBrush(string key, Brush fallback) =>
        TryFindResource(key) as Brush ?? fallback;

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
