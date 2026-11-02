using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Yy.Studio.Avalonia.Features.CommandLine;

namespace Yy.Studio.Avalonia;

public sealed partial class MainWindow
{
    private CommandLineView? _searchTarget;
    private DispatcherTimer? _searchStatusTimer;

    private void OnShowTerminalSearchButton(object? sender, RoutedEventArgs e) =>
        ShowTerminalSearch((_activeCommandLine ?? _commandLines.FirstOrDefault())
            ?.TabView.ActiveView);

    private void ShowTerminalSearch(CommandLineView? view)
    {
        if (view is null)
        {
            return;
        }

        if (!ReferenceEquals(_searchTarget, view))
        {
            HideTerminalSearch(focusTerminal: false);
        }

        _searchTarget = view;
        TerminalSearchBar.IsVisible = true;
        TerminalSearchBox.Text = string.Empty;
        TerminalSearchStatus.Text = string.Empty;
        Dispatcher.UIThread.Post(() =>
        {
            TerminalSearchBox.Focus();
            TerminalSearchBox.SelectAll();
        });
    }

    private void HideTerminalSearch(bool focusTerminal)
    {
        _searchStatusTimer?.Stop();
        _searchTarget?.CloseFind();
        _searchTarget = null;
        TerminalSearchBar.IsVisible = false;
        TerminalSearchStatus.Text = string.Empty;
        if (focusTerminal)
        {
            (_activeCommandLine?.TabView.ActiveView ?? _commandLine)?.FocusTerminal();
        }
    }

    private void OnTerminalSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchTarget?.Find(TerminalSearchBox.Text);
        UpdateSearchStatusSoon();
    }

    private void OnTerminalSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                _searchTarget?.FindPrev();
            }
            else
            {
                _searchTarget?.FindNext();
            }

            UpdateSearchStatusSoon();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            HideTerminalSearch(focusTerminal: true);
            e.Handled = true;
        }
    }

    private void OnTerminalSearchPrev(object? sender, RoutedEventArgs e)
    {
        _searchTarget?.FindPrev();
        UpdateSearchStatusSoon();
    }

    private void OnTerminalSearchNext(object? sender, RoutedEventArgs e)
    {
        _searchTarget?.FindNext();
        UpdateSearchStatusSoon();
    }

    private void OnCloseTerminalSearch(object? sender, RoutedEventArgs e) =>
        HideTerminalSearch(focusTerminal: true);

    private void UpdateSearchStatusSoon()
    {
        _searchStatusTimer ??= CreateSearchStatusTimer();
        _searchStatusTimer.Stop();
        _searchStatusTimer.Start();
    }

    private DispatcherTimer CreateSearchStatusTimer()
    {
        var timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(220)
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            UpdateSearchStatus();
        };
        return timer;
    }

    private void UpdateSearchStatus()
    {
        if (_searchTarget is null)
        {
            return;
        }

        var count = _searchTarget.MatchCount;
        var current = _searchTarget.CurrentMatch;
        TerminalSearchStatus.Text = count == 0 ? string.Empty : $"{current}/{count}";
    }
}
