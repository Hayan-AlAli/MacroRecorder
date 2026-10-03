using MacroApp.Core.Models;
using MacroApp.NativeInterop;

namespace MacroApp.Core.Playback;

/// <summary>
/// Current state of the playback engine.
/// </summary>
public enum PlaybackState
{
    Idle,
    Playing,
    Paused,
    StepThrough,
    Stopping
}

/// <summary>
/// Event data for playback progress updates.
/// </summary>
public record PlaybackProgressEventArgs(
    int CurrentEventIndex,
    int TotalEvents,
    int CurrentRepeat,
    int TotalRepeats,
    PlaybackState State,
    int Line = 0);

/// <summary>
/// Common surface of the two playback engines so the UI can drive either one.
/// </summary>
public interface IPlaybackEngine
{
    PlaybackState State { get; }
    event Action<PlaybackState>? StateChanged;
    event Action<PlaybackProgressEventArgs>? Progress;
    event Action? Completed;
    event Action<Exception>? Error;
    void Pause();
    void Resume();
    void EnterStepMode();
    void StepNext();
    void Stop();
}

/// <summary>
/// Replays macro events via InputSimulator on a dedicated background thread.
/// Supports speed multipliers, repeat, step-through, breakpoints, and emergency stop.
/// </summary>
public sealed class PlaybackEngine : IPlaybackEngine, IDisposable
{
    private readonly InputSimulator _inputSimulator;
    private Thread? _playbackThread;
    private CancellationTokenSource? _cts;
    private readonly ManualResetEventSlim _stepSignal = new(false);
    private volatile PlaybackState _state = PlaybackState.Idle;
    private bool _disposed;

    /// <summary>
    /// Fired when playback state changes.
    /// </summary>
    public event Action<PlaybackState>? StateChanged;

    /// <summary>
    /// Fired on each event during playback (for UI line highlighting).
    /// </summary>
    public event Action<PlaybackProgressEventArgs>? Progress;

    /// <summary>
    /// Fired when playback completes all repeats.
    /// </summary>
    public event Action? Completed;

    /// <summary>
    /// Fired when playback encounters an error.
    /// </summary>
    public event Action<Exception>? Error;

    /// <summary>
    /// Gets the current playback state.
    /// </summary>
    public PlaybackState State => _state;

    public PlaybackEngine(InputSimulator inputSimulator)
    {
        _inputSimulator = inputSimulator ?? throw new ArgumentNullException(nameof(inputSimulator));
    }

    /// <summary>
    /// Starts playing back a list of events with the given settings.
    /// </summary>
    public void Start(List<InputEvent> events, MacroPlaybackSettings settings)
    {
        if (_state != PlaybackState.Idle)
            throw new InvalidOperationException($"Cannot start playback in state {_state}");

        if (events.Count == 0) return;

        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        _inputSimulator.TargetWindowHandle = IntPtr.Zero;

        if (settings.CoordinateMode == CoordinateMode.WindowRelative)
        {
            _inputSimulator.TargetWindowHandle = NativeMethods.GetForegroundWindow();
        }

        _playbackThread = new Thread(() => PlaybackLoop(events, settings, _cts.Token))
        {
            Name = "MacroApp.PlaybackThread",
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };

        // Set before the thread starts so a second Start() call can't slip in.
        SetState(PlaybackState.Playing);
        _playbackThread.Start();
    }

    /// <summary>
    /// Pauses playback.
    /// </summary>
    public void Pause()
    {
        if (_state == PlaybackState.Playing)
            SetState(PlaybackState.Paused);
    }

    /// <summary>
    /// Resumes playback from pause.
    /// </summary>
    public void Resume()
    {
        if (_state == PlaybackState.Paused)
            SetState(PlaybackState.Playing);
    }

    /// <summary>
    /// Enters step-through mode — pauses after each event until StepNext() is called.
    /// </summary>
    public void EnterStepMode()
    {
        if (_state is PlaybackState.Playing or PlaybackState.Paused)
            SetState(PlaybackState.StepThrough);
    }

    /// <summary>
    /// In step-through mode, advances to the next event.
    /// </summary>
    public void StepNext()
    {
        if (_state == PlaybackState.StepThrough)
            _stepSignal.Set();
    }

    /// <summary>
    /// Stops playback immediately (emergency stop).
    /// </summary>
    public void Stop()
    {
        if (_state == PlaybackState.Idle) return;

        SetState(PlaybackState.Stopping);
        _cts?.Cancel();
        _playbackThread?.Join(TimeSpan.FromSeconds(2));
        SetState(PlaybackState.Idle);
    }

    private void PlaybackLoop(List<InputEvent> events, MacroPlaybackSettings settings, CancellationToken ct)
    {
        try
        {
            bool infinite = settings.RepeatCount == AppConstants.InfiniteRepeat;
            int totalRepeats = Math.Max(1, settings.RepeatCount);
            int repeatIndex = 0;

            while (infinite || repeatIndex < totalRepeats)
            {
                ct.ThrowIfCancellationRequested();

                for (int i = 0; i < events.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();

                    // Wait while paused
                    while (_state == PlaybackState.Paused)
                    {
                        ct.ThrowIfCancellationRequested();
                        Thread.Sleep(50);
                    }

                    var evt = events[i];

                    // Progress notification
                    Progress?.Invoke(new PlaybackProgressEventArgs(
                        i, events.Count,
                        repeatIndex + 1, infinite ? -1 : totalRepeats,
                        _state));

                    // Apply delay with speed multiplier
                    if (evt.DelayFromPreviousMs > 0)
                    {
                        double adjustedDelay = evt.DelayFromPreviousMs / settings.SpeedMultiplier;
                        PreciseDelay.Wait(adjustedDelay, ct);
                    }

                    ct.ThrowIfCancellationRequested();

                    // Execute the event
                    ExecuteEvent(evt, settings.CoordinateMode);

                    // Breakpoint handling
                    if (evt.HasBreakpoint)
                    {
                        SetState(PlaybackState.StepThrough);
                    }

                    // Step-through mode
                    if (_state == PlaybackState.StepThrough)
                    {
                        _stepSignal.Reset();
                        _stepSignal.Wait(ct);
                    }
                }

                repeatIndex++;

                // Inter-repeat delay
                if ((infinite || repeatIndex < totalRepeats) && settings.InterRepeatDelayMs > 0)
                {
                    PreciseDelay.Wait(settings.InterRepeatDelayMs, ct);
                }
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

    private void ExecuteEvent(InputEvent evt, CoordinateMode coordinateMode)
    {
        switch (evt.Type)
        {
            case InputEventType.KeyDown when evt.VirtualKeyCode.HasValue:
                _inputSimulator.SendKeyDown((ushort)evt.VirtualKeyCode.Value);
                break;

            case InputEventType.KeyUp when evt.VirtualKeyCode.HasValue:
                _inputSimulator.SendKeyUp((ushort)evt.VirtualKeyCode.Value);
                break;

            case InputEventType.MouseMove when evt.X.HasValue && evt.Y.HasValue:
                _inputSimulator.SendMouseMove(evt.X.Value, evt.Y.Value, coordinateMode);
                break;

            case InputEventType.MouseDown when evt.X.HasValue && evt.Y.HasValue && evt.Button.HasValue:
                _inputSimulator.SendMouseDown(evt.Button.Value, evt.X.Value, evt.Y.Value, coordinateMode);
                break;

            case InputEventType.MouseUp when evt.X.HasValue && evt.Y.HasValue && evt.Button.HasValue:
                _inputSimulator.SendMouseUp(evt.Button.Value, evt.X.Value, evt.Y.Value, coordinateMode);
                break;

            case InputEventType.MouseScroll when evt.X.HasValue && evt.Y.HasValue && evt.ScrollDelta.HasValue:
                _inputSimulator.SendMouseScroll(evt.ScrollDelta.Value, evt.X.Value, evt.Y.Value, coordinateMode);
                break;

            case InputEventType.MouseDrag when evt.X.HasValue && evt.Y.HasValue
                && evt.EndX.HasValue && evt.EndY.HasValue && evt.Button.HasValue:
                _inputSimulator.SendMouseDrag(evt.Button.Value,
                    evt.X.Value, evt.Y.Value, evt.EndX.Value, evt.EndY.Value, coordinateMode);
                break;
        }
    }

    private void SetState(PlaybackState newState)
    {
        _state = newState;
        StateChanged?.Invoke(newState);
    }

    /// <summary>
    /// Disposes playback engine resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _cts?.Dispose();
        _stepSignal.Dispose();
    }
}
