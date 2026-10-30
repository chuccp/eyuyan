namespace Yy.Language;

public enum YyDebugMode
{
    Run,
    StepInto,
    StepOver,
    StepOut,
    RunToLine
}

public sealed record YyDebugPause(
    int Line,
    int Column,
    int Depth,
    IReadOnlyDictionary<string, object?> Variables);

/// <summary>
/// 调试探针:解释器在每条语句执行前回调 <see cref="OnStatement"/>,
/// 按当前调试模式(单步/单步跳过/跳出/到光标处)决定是否阻塞并上报暂停位置。
/// 暂停线程是解释器线程;UI 通过 Continue/Step* 系列方法恢复执行。
/// </summary>
public sealed class YyDebugProbe
{
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _resume = new(false);
    private YyDebugMode _mode;
    private int _baseDepth;
    private int _runToLine;
    private bool _stopped;
    private bool _forceBreak;

    public YyDebugProbe(YyDebugMode initialMode = YyDebugMode.StepInto, int runToLine = 0)
    {
        _mode = initialMode;
        _runToLine = runToLine;
    }

    /// <summary>断点判定(行号 => 是否断点)。由调试器 UI 提供,普通运行也挂接以便 F9 断点生效。</summary>
    public Func<int, bool>? IsBreakpoint { get; set; }

    public event Action<YyDebugPause>? Paused;

    public int CurrentDepth
    {
        get
        {
            lock (_gate)
            {
                return _baseDepth;
            }
        }
    }

    /// <summary>“断点()”命令:请求在下一条语句处暂停(无论当前调试模式)。</summary>
    public void RequestBreak()
    {
        lock (_gate)
        {
            _forceBreak = true;
        }
    }

    internal void OnStatement(
        int line,
        int column,
        int depth,
        Func<IReadOnlyDictionary<string, object?>> snapshot)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            var shouldPause = _mode switch
            {
                YyDebugMode.StepInto => true,
                YyDebugMode.StepOver => depth <= _baseDepth,
                YyDebugMode.StepOut => depth < _baseDepth,
                YyDebugMode.RunToLine => line == _runToLine,
                _ => false
            } || _forceBreak || (IsBreakpoint?.Invoke(line) ?? false);
            _forceBreak = false;
            if (!shouldPause)
            {
                return;
            }

            _mode = YyDebugMode.Run;
            _baseDepth = depth;
        }

        Paused?.Invoke(new YyDebugPause(line, column, depth, snapshot()));
        _resume.Wait();
        _resume.Reset();
    }

    public void Continue() => Resume(YyDebugMode.Run, 0);

    public void StepInto() => Resume(YyDebugMode.StepInto, 0);

    public void StepOver(int currentDepth) => Resume(YyDebugMode.StepOver, currentDepth);

    public void StepOut(int currentDepth) => Resume(YyDebugMode.StepOut, currentDepth);

    public void RunToLine(int line, int currentDepth) => Resume(YyDebugMode.RunToLine, currentDepth, line);

    public void Stop()
    {
        lock (_gate)
        {
            _stopped = true;
        }

        _resume.Set();
    }

    private void Resume(YyDebugMode mode, int baseDepth, int runToLine = 0)
    {
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _mode = mode;
            _baseDepth = baseDepth;
            _runToLine = runToLine;
        }

        _resume.Set();
    }
}
