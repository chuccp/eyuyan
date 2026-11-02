using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Exclr8.Terminal;
using Exclr8.Terminal.Render;
using Terminal.Pty;
using Yy.Studio.Avalonia.Platforms;

namespace Yy.Studio.Avalonia.Features.CommandLine;

/// <summary>
/// 底部命令行：通过系统 PTY 直接托管操作系统自带的交互式 shell,
/// 终端画面、提示符、输入和全屏程序都由真实 shell 驱动。
/// </summary>
public sealed class CommandLineView : UserControl
{
    private const int DefaultColumns = 120;
    private const int DefaultRows = 30;

    private readonly TerminalControl _terminal;
    private IPtyConnection? _pty;
    private CancellationTokenSource? _pumpCancellation;
    private int _session;
    private int _ptyColumns;
    private int _ptyRows;
    private string _workingDirectory;

    public CommandLineView()
        : this(Environment.CurrentDirectory)
    {
    }

    public CommandLineView(string workingDirectory)
    {
        _terminal = new TerminalControl
        {
            FontFamily = "JetBrains Mono,Cascadia Code,Consolas,Menlo,monospace",
            FontSize = 13,
            ScrollbackLimit = 5000,
            CursorBlinkIntervalMs = 530,
            ShowHyperlinkUnderline = true,
            Focusable = true,
            ColorScheme = CreateLightTheme()
        };
        _terminal.Input += (_, payload) => WriteToPty(payload.Span);
        _terminal.Output += (_, payload) => WriteToPty(payload.Span);
        _terminal.TitleChanged += (_, title) =>
        {
            ShellTitle = title;
            TitleChanged?.Invoke(this, title);
        };
        _terminal.WorkingDirectoryChanged += (_, directory) =>
            WorkingDirectoryChanged?.Invoke(this, directory);
        _terminal.GotFocus += (_, _) => Activated?.Invoke(this, EventArgs.Empty);
        _terminal.AddHandler(
            KeyDownEvent,
            OnTerminalKeyDown,
            RoutingStrategies.Tunnel);
        _terminal.Resized += (_, size) =>
        {
            _ptyColumns = size.Cols;
            _ptyRows = size.Rows;
            if (_pty is not null)
            {
                PtyResize.Apply(_pty, size.Cols, size.Rows);
            }
        };
        Content = new Border
        {
            Background = Brushes.White,
            Padding = new Thickness(10, 6, 10, 6),
            Child = _terminal
        };

        _workingDirectory = workingDirectory;
        StartShell(workingDirectory);
    }

    /// <summary>shell 通过 OSC 0/2 设置的标题；未设置时为 null。</summary>
    public string? ShellTitle { get; private set; }

    /// <summary>当前工作目录：优先使用 shell 通过 OSC 7 上报的目录。</summary>
    public string WorkingDirectory =>
        _terminal.WorkingDirectory ?? _workingDirectory;

    public event EventHandler? Activated;
    public event EventHandler<string>? TitleChanged;
    public event EventHandler<string>? WorkingDirectoryChanged;
    public event EventHandler? SearchRequested;

    public int MatchCount => _terminal.MatchCount;
    public int CurrentMatch => _terminal.CurrentMatch;

    public void Find(string? needle) => _terminal.Find(needle);
    public void FindNext() => _terminal.FindNext();
    public void FindPrev() => _terminal.FindPrev();
    public void CloseFind() => _terminal.CloseFind();

    /// <summary>切换工程时在工程根目录重新启动系统 shell。</summary>
    public void EnterProject(string projectDirectory)
    {
        StartShell(projectDirectory);
        _terminal.Focus();
    }

    /// <summary>切换到命令行页时把键盘焦点交给 shell。</summary>
    public void FocusTerminal() => _terminal.Focus();

    private void OnTerminalKeyDown(object? sender, KeyEventArgs e)
    {
        var modifier = OperatingSystem.IsMacOS()
            ? KeyModifiers.Meta
            : KeyModifiers.Control;
        if (e.Key == Key.F &&
            e.KeyModifiers.HasFlag(modifier) &&
            !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            SearchRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    /// <summary>向当前 shell 会话写入一条命令(MCP 自动化使用)。</summary>
    public bool RunCommand(string command)
    {
        if (string.IsNullOrWhiteSpace(command) || _pty is null)
        {
            return false;
        }

        var text = command.Replace("\r\n", "\n").TrimEnd('\n') + "\r";
        WriteToPty(Encoding.UTF8.GetBytes(text));
        _terminal.Focus();
        return true;
    }

    /// <summary>读取终端网格和光标状态,用于 MCP 诊断。</summary>
    public object CaptureState() => new
    {
        pty = _pty is not null,
        ptyColumns = _ptyColumns,
        ptyRows = _ptyRows,
        columns = _terminal.Buffer.Cols,
        rows = _terminal.Buffer.Rows,
        cursorRow = _terminal.Buffer.CursorRow,
        cursorColumn = _terminal.Buffer.CursorCol,
        scrollOffset = _terminal.Buffer.ScrollOffset,
        scrollback = _terminal.Buffer.ScrollbackCount,
        screen = CaptureScreen()
    };

    /// <summary>导出终端可回放内容,用于 MCP 诊断。</summary>
    public string Dump() => _terminal.Buffer.Serialize();

    private string[] CaptureScreen()
    {
        var lines = new string[_terminal.Buffer.Rows];
        for (var row = 0; row < lines.Length; row++)
        {
            lines[row] = Exclr8.Terminal.Buffer.RowText
                .Build(_terminal.Buffer.GetRowForRender(row), out _)
                .TrimEnd();
        }

        return lines;
    }

    /// <summary>结束 shell 和所有子进程。</summary>
    public void Stop()
    {
        StopSession();
        _terminal.Dispose();
    }

    private void StartShell(string workingDirectory)
    {
        var session = ++_session;
        var columns = _terminal.Buffer.Cols > 0 ? _terminal.Buffer.Cols : DefaultColumns;
        var rows = _terminal.Buffer.Rows > 0 ? _terminal.Buffer.Rows : DefaultRows;
        StopSession();
        _terminal.PrepareForNewSession();
        _workingDirectory = workingDirectory;
        ShellTitle = null;
        try
        {
            _ptyColumns = columns;
            _ptyRows = rows;
            _pty = PtyConnection.Start(CreateOptions(workingDirectory, columns, rows));
            _terminal.RootProcessId = _pty.ProcessId;
            _pty.Exited += exitCode =>
            {
                if (session != _session)
                {
                    return;
                }

                Dispatcher.UIThread.Post(() => _terminal.Write(
                    Encoding.UTF8.GetBytes(
                        $"\r\n[进程已退出，退出码 {exitCode}]\r\n")));
            };
            _pumpCancellation = new CancellationTokenSource();
            _ = PumpAsync(_pty.Output, _pumpCancellation.Token);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException
                or PlatformNotSupportedException or System.ComponentModel.Win32Exception)
        {
            _terminal.Write(Encoding.UTF8.GetBytes(
                "无法启动系统终端：" + exception.Message + "\r\n"));
        }
    }

    private static PtyOptions CreateOptions(string workingDirectory, int columns, int rows)
    {
        var directory = Directory.Exists(workingDirectory)
            ? workingDirectory
            : Environment.CurrentDirectory;
        if (OperatingSystem.IsWindows())
        {
            return new PtyOptions
            {
                Command = "powershell.exe",
                Arguments = ["-NoLogo"],
                WorkingDirectory = directory,
                Columns = columns,
                Rows = rows
            };
        }

        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(shell) || !File.Exists(shell))
        {
            shell = File.Exists("/bin/zsh")
                ? "/bin/zsh"
                : File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
        }

        return new PtyOptions
        {
            Command = shell,
            Arguments = ["-l"],
            WorkingDirectory = directory,
            Columns = columns,
            Rows = rows,
            Environment = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["TERM"] = "xterm-256color",
                ["COLORTERM"] = "truecolor"
            }
        };
    }

    private async Task PumpAsync(Stream output, CancellationToken cancellationToken)
    {
        var buffer = new byte[8192];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await output.ReadAsync(buffer, cancellationToken);
                if (read <= 0)
                {
                    break;
                }

                _terminal.Write(buffer.AsSpan(0, read));
            }
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or IOException
                or ObjectDisposedException)
        {
            // 会话结束或切换工程时正常退出读取循环。
        }
    }

    private void WriteToPty(ReadOnlySpan<byte> payload)
    {
        try
        {
            _pty?.Write(payload);
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException
                or InvalidOperationException)
        {
            // shell 已退出,忽略迟到的按键。
        }
    }

    private void StopSession()
    {
        _pumpCancellation?.Cancel();
        _pumpCancellation?.Dispose();
        _pumpCancellation = null;

        var pty = _pty;
        _pty = null;
        if (pty is null)
        {
            return;
        }

        try
        {
            pty.Output.Dispose();
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException)
        {
            // 输出流可能已随进程关闭。
        }

        try
        {
            using var process = Process.GetProcessById(pty.ProcessId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException
                or System.ComponentModel.Win32Exception)
        {
            // 进程可能已经退出。
        }
    }

    private static TerminalTheme CreateLightTheme() => new()
    {
        Foreground = Color.Parse("#1F2328"),
        Background = Color.Parse("#FFFFFF"),
        Cursor = Color.Parse("#3574F0"),
        AnsiColors =
        [
            Color.Parse("#000000"),
            Color.Parse("#CD3131"),
            Color.Parse("#00BC00"),
            Color.Parse("#949800"),
            Color.Parse("#0451A5"),
            Color.Parse("#BC05BC"),
            Color.Parse("#0598BC"),
            Color.Parse("#555555"),
            Color.Parse("#666666"),
            Color.Parse("#CD3131"),
            Color.Parse("#14CE14"),
            Color.Parse("#B5BA00"),
            Color.Parse("#0451A5"),
            Color.Parse("#BC05BC"),
            Color.Parse("#0598BC"),
            Color.Parse("#A5A5A5")
        ]
    };
}
