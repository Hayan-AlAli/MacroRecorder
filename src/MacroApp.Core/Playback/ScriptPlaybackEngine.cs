using MacroApp.Core.Models;
using MacroApp.Core.Vision;
using MacroApp.NativeInterop;
using MacroApp.Scripting.Interpreter;
using MacroApp.Scripting.Parser;

namespace MacroApp.Core.Playback;

/// <summary>
/// Runs a macro's script text through the interpreter on a background thread.
/// This is what plays a macro once it has a script, so edits made in the editor
/// (loops, variables, window commands, hand-tuned delays) are what actually run.
/// </summary>
public sealed class ScriptPlaybackEngine : IPlaybackEngine, IDisposable
{
    private readonly InputSimulator _inputSimulator;
    private readonly IScreenVision? _vision;
    private readonly ManualResetEventSlim _stepSignal = new(false);
    private Thread? _thread;
    private CancellationTokenSource? _cts;
    private volatile PlaybackState _state = PlaybackState.Idle;
    private bool _disposed;

    public event Action<PlaybackState>? StateChanged;
    public event Action<PlaybackProgressEventArgs>? Progress;
    public event Action? Completed;
    public event Action<Exception>? Error;

    public PlaybackState State => _state;

    public ScriptPlaybackEngine(InputSimulator inputSimulator, IScreenVision? vision = null)
    {
        _inputSimulator = inputSimulator ?? throw new ArgumentNullException(nameof(inputSimulator));
        _vision = vision;
    }

    /// <summary>
    /// Parses and starts running <paramref name="script"/>.
    /// Throws <see cref="InvalidOperationException"/> if the script has syntax errors.
    /// </summary>
    /// <param name="breakpoints">Script lines to stop at (switching to step mode) before they run.</param>
    /// <param name="baseDirectory">Folder that relative file paths in the script (images, sounds) are resolved against.</param>
    public void Start(string script, MacroPlaybackSettings settings,
        IReadOnlyCollection<int>? breakpoints = null, string? baseDirectory = null)
    {
        if (_state != PlaybackState.Idle)
            throw new InvalidOperationException($"Cannot start playback in state {_state}");

        var parser = new ScriptParser();
        var nodes = parser.Parse(script);
        var firstError = parser.Errors.FirstOrDefault(e => e.Severity == ScriptErrorSeverity.Error);
        if (firstError != null)
            throw new InvalidOperationException($"Line {firstError.Line}: {firstError.Message}");

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _inputSimulator.TargetWindowHandle = settings.CoordinateMode == CoordinateMode.WindowRelative
            ? NativeMethods.GetForegroundWindow()
            : IntPtr.Zero;

        var breakpointLines = new HashSet<int>(breakpoints ?? Array.Empty<int>());
        _thread = new Thread(() => Run(nodes, settings, breakpointLines, baseDirectory, ct))
        {
            Name = "MacroApp.ScriptThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };

        // STA so clipboard commands and message boxes work from the script thread.
        if (OperatingSystem.IsWindows())
            _thread.SetApartmentState(ApartmentState.STA);

        SetState(PlaybackState.Playing);
        _thread.Start();
    }

    public void Pause()
    {
        if (_state == PlaybackState.Playing)
            SetState(PlaybackState.Paused);
    }

    public void Resume()
    {
        if (_state is PlaybackState.Paused or PlaybackState.StepThrough)
        {
            SetState(PlaybackState.Playing);
            _stepSignal.Set();
        }
    }

    public void EnterStepMode()
    {
        if (_state is PlaybackState.Playing or PlaybackState.Paused)
            SetState(PlaybackState.StepThrough);
    }

    public void StepNext()
    {
        if (_state == PlaybackState.StepThrough)
            _stepSignal.Set();
    }

    public void Stop()
    {
        if (_state == PlaybackState.Idle) return;

        SetState(PlaybackState.Stopping);
        _cts?.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        SetState(PlaybackState.Idle);
    }

    private void Run(List<Scripting.AST.AstNode> nodes, MacroPlaybackSettings settings,
        HashSet<int> breakpoints, string? baseDirectory, CancellationToken ct)
    {
        var executor = new ScriptActionExecutor(_inputSimulator, settings, ct, _vision, baseDirectory);
        var interpreter = new ScriptInterpreter(executor);

        bool infinite = settings.RepeatCount == AppConstants.InfiniteRepeat;
        int totalRepeats = Math.Max(1, settings.RepeatCount);
        int repeat = 0;

        interpreter.LineExecuting += line =>
        {
            if (breakpoints.Contains(line) && (_state is PlaybackState.Playing or PlaybackState.Paused))
                SetState(PlaybackState.StepThrough);

            Progress?.Invoke(new PlaybackProgressEventArgs(
                line - 1, 0, repeat + 1, infinite ? -1 : totalRepeats, _state, line));
            WaitWhilePausedOrStepping(ct);
        };

        try
        {
            while (infinite || repeat < totalRepeats)
            {
                interpreter.Execute(nodes, ct);
                if (interpreter.WasStopped) break; // the script ran a Stop command

                repeat++;
                if ((infinite || repeat < totalRepeats) && settings.InterRepeatDelayMs > 0)
                    PreciseDelay.Wait(settings.InterRepeatDelayMs, ct);
            }

            SetState(PlaybackState.Idle);
            Completed?.Invoke();
        }
        catch (OperationCanceledException)
        {
            SetState(PlaybackState.Idle);
        }
        catch (Exception ex)
        {
            SetState(PlaybackState.Idle);
            Error?.Invoke(ex);
        }
        finally
        {
            _inputSimulator.ReleaseAll();
        }
    }

    private void WaitWhilePausedOrStepping(CancellationToken ct)
    {
        while (_state == PlaybackState.Paused)
        {
            ct.ThrowIfCancellationRequested();
            Thread.Sleep(50);
        }

        if (_state == PlaybackState.StepThrough)
        {
            _stepSignal.Reset();
            _stepSignal.Wait(ct);
        }
    }

    private void SetState(PlaybackState newState)
    {
        _state = newState;
        StateChanged?.Invoke(newState);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _cts?.Dispose();
        _stepSignal.Dispose();
    }
}
