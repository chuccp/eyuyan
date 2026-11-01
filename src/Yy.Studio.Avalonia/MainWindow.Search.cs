using Avalonia.Input;
using Avalonia.Interactivity;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private sealed record SearchResult(int Line, string Text)
    {
        public override string ToString() => $"第 {Line} 行  {Text}";
    }

    private void OnSearch(object? sender, RoutedEventArgs e)
    {
        if (CurrentDocument is not { } document)
        {
            SetStatus("当前没有打开的文档。");
            return;
        }

        var keyword = SearchInputBox.Text?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            return;
        }

        var source = CurrentCodeSource(document);
        var results = source
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select((text, index) => new SearchResult(index + 1, text.Trim()))
            .Where(item => item.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        SearchResultsList.ItemsSource = results;
        BottomTabs.SelectedIndex = 4;
        SetStatus($"搜寻“{keyword}”：找到 {results.Length} 处");
    }

    private void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (CurrentDocument is not { } document ||
            SearchResultsList.SelectedItem is not SearchResult result)
        {
            return;
        }

        if (document.UseClassicView && document.StructuredView is not null)
        {
            SetCodeView(document, classic: false);
        }

        if (result.Line <= document.Editor.Document.LineCount)
        {
            var line = document.Editor.Document.GetLineByNumber(result.Line);
            document.Editor.Select(line.Offset, line.Length);
            document.Editor.ScrollToLine(result.Line);
            document.Editor.Focus();
        }
    }
}
