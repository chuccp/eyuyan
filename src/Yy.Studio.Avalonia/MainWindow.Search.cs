using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private sealed record SearchResult(int Line, string Text)
    {
        public override string ToString() => $"第 {Line} 行  {Text}";
    }

    private void OnSearch(object? sender, RoutedEventArgs e) =>
        RunSearch(SearchInputBox, SearchResultsList, "搜寻1");

    private void OnSearch2(object? sender, RoutedEventArgs e) =>
        RunSearch(SearchInputBox2, SearchResultsList2, "搜寻2");

    private void RunSearch(TextBox input, ListBox results, string bottomTabHeader)
    {
        if (CurrentDocument is not { } document)
        {
            SetStatus("当前没有打开的文档。");
            return;
        }

        var keyword = input.Text?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            return;
        }

        var source = CurrentCodeSource(document);
        var matches = source
            .Replace("\r\n", "\n")
            .Split('\n')
            .Select((text, index) => new SearchResult(index + 1, text.Trim()))
            .Where(item => item.Text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        results.ItemsSource = matches;
        SelectBottomTab(bottomTabHeader);
        SetStatus($"搜寻“{keyword}”：找到 {matches.Length} 处");
    }

    private void OnSearchResultDoubleTapped(object? sender, TappedEventArgs e) =>
        JumpToSearchResult(SearchResultsList.SelectedItem as SearchResult);

    private void OnSearchResult2DoubleTapped(object? sender, TappedEventArgs e) =>
        JumpToSearchResult(SearchResultsList2.SelectedItem as SearchResult);

    private void JumpToSearchResult(SearchResult? result)
    {
        if (CurrentDocument is not { } document || result is null)
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

    // =============== 剪辑历史:记录复制/剪切文本,双击插回编辑器 ===============

    private sealed record ClipHistoryRow(string Text)
    {
        public override string ToString() =>
            Text.Replace("\r\n", " ").Replace("\n", " ");
    }

    private readonly List<ClipHistoryRow> _clipHistory = [];
    private string _lastClipText = string.Empty;

    private void InitializeClipHistory()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(800)
        };
        timer.Tick += (_, _) => _ = RecordClipHistoryAsync();
        timer.Start();
    }

    private async Task RecordClipHistoryAsync()
    {
        try
        {
            var text = await (TopLevel.GetTopLevel(this)?.Clipboard?.GetTextAsync()
                ?? Task.FromResult<string?>(null));
            if (string.IsNullOrEmpty(text) ||
                string.Equals(text, _lastClipText, StringComparison.Ordinal))
            {
                return;
            }

            _lastClipText = text;
            if (_clipHistory.Any(row => string.Equals(row.Text, text, StringComparison.Ordinal)))
            {
                return;
            }

            _clipHistory.Insert(0, new ClipHistoryRow(text));
            if (_clipHistory.Count > 50)
            {
                _clipHistory.RemoveAt(_clipHistory.Count - 1);
            }

            ClipHistoryList.ItemsSource = _clipHistory.ToList();
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or IOException)
        {
            // 剪贴板被其他进程独占时跳过本拍。
        }
    }

    private void OnClipHistoryInsert(object? sender, TappedEventArgs e)
    {
        if (ClipHistoryList.SelectedItem is not ClipHistoryRow row ||
            CurrentDocument is not { } document)
        {
            return;
        }

        if (document.UseClassicView && document.StructuredView is not null)
        {
            SetCodeView(document, classic: false);
        }

        document.Editor.Document.Insert(document.Editor.CaretOffset, row.Text);
        document.Editor.Focus();
    }
}
