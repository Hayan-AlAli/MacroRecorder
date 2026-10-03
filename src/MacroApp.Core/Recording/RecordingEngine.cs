using System.IO;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using MacroApp.Core.Models;
using MacroApp.NativeInterop;

namespace MacroApp.Core.Recording;

/// <summary>
/// Current state of the recording engine.
/// </summary>
public enum RecordingState
{
    Idle,
    Countdown,
    Recording,
    Paused
}

/// <summary>
/// Subscribes to HookManager's raw input event stream, applies filters,
/// converts to InputEvent models with relative timestamps,
/// and supports pause/resume and crash recovery buffering.
/// </summary>
public sealed class RecordingEngine : IDisposable
{
    private readonly HookManager _hookManager;
    private readonly List<InputEvent> _recordedEvents = new();
    private readonly object _lock = new();

    private IDisposable? _subscription;
    private long _lastTimestamp;
    private int _lastMouseX;
    private int _lastMouseY;
    private volatile RecordingState _state = RecordingState.Idle;
    private System.Timers.Timer? _crashRecoveryTimer;
    private string? _crashRecoveryPath;

    /// <summary>
    /// Fired when the recording state changes.
    /// </summary>
    public event Action<RecordingState>? StateChanged;

    /// <summary>
    /// Fired when a new event is recorded (for live UI updates).
    /// </summary>
    public event Action<InputEvent>? EventRecorded;

    /// <summary>
    /// Fired during countdown (emits remaining seconds).
    /// </summary>
    public event Action<int>? CountdownTick;

    /// <summary>
    /// Gets the current recording state.
    /// </summary>
    public RecordingState State => _state;

    /// <summary>
    /// Gets the current recording filter settings.
    /// </summary>
    public RecordingSettings Settings { get; set; } = new();

    /// <summary>
    /// A top-level window whose input should never be recorded — normally the app's own
    /// main window, so clicking "Stop" doesn't end up as the last step of every macro.
    /// </summary>
    public IntPtr IgnoredWindow { get; set; }

    /// <summary>
    /// Gets the count of recorded events.
    /// </summary>
    public int EventCount
    {
        get { lock (_lock) return _recordedEvents.Count; }
    }

    public RecordingEngine(HookManager hookManager)
    {
        _hookManager = hookManager ?? throw new ArgumentNullException(nameof(hookManager));
    }

    /// <summary>
    /// Starts recording with an optional countdown.
    /// </summary>
    public async Task StartAsync(CancellationToken ct = default)
    {
        if (_state != RecordingState.Idle) return;

        // Countdown phase
        if (Settings.CountdownSeconds > 0)
        {
            SetState(RecordingState.Countdown);
            try
            {
                for (int i = Settings.CountdownSeconds; i > 0; i--)
                {
                    ct.ThrowIfCancellationRequested();
                    CountdownTick?.Invoke(i);
                    await Task.Delay(1000, ct);
                }
            }
            catch (OperationCanceledException)
            {
                SetState(RecordingState.Idle);
                throw;
            }
        }

        lock (_lock) _recordedEvents.Clear();

        // The hooks may already be running for the emergency-stop key, so they always
        // capture both devices; KeyboardOnly/MouseOnly are applied in FilterEvent instead.
        if (!_hookManager.IsRunning)
            _hookManager.Start();

        _lastTimestamp = HighResolutionTimer.GetTimestamp();
        NativeMethods.GetCursorPos(out POINT pos);
        _lastMouseX = pos.X;
        _lastMouseY = pos.Y;

        // Subscribe to input events
        _subscription = _hookManager.InputEvents
            .Where(FilterEvent)
            .Subscribe(OnRawInputEvent);

        // Set up crash recovery buffer
        SetupCrashRecovery();

        SetState(RecordingState.Recording);
    }

    /// <summary>
    /// Pauses recording.
    /// </summary>
    public void Pause()
    {
        if (_state != RecordingState.Recording) return;
        SetState(RecordingState.Paused);
    }

    /// <summary>
    /// Resumes recording from pause.
    /// </summary>
    public void Resume()
    {
        if (_state != RecordingState.Paused) return;
        _lastTimestamp = HighResolutionTimer.GetTimestamp();
        SetState(RecordingState.Recording);
    }

    /// <summary>
    /// Stops recording and returns the captured events.
    /// </summary>
    public List<InputEvent> Stop()
    {
        _subscription?.Dispose();
        _subscription = null;
        _crashRecoveryTimer?.Stop();
        _crashRecoveryTimer?.Dispose();
        _crashRecoveryTimer = null;

        // Clean up crash recovery file
        if (_crashRecoveryPath != null && File.Exists(_crashRecoveryPath))
        {
            try { File.Delete(_crashRecoveryPath); } catch { }
            _crashRecoveryPath = null;
        }

        List<InputEvent> result;
        lock (_lock)
        {
            result = new List<InputEvent>(_recordedEvents);
            _recordedEvents.Clear();
        }

        SetState(RecordingState.Idle);
        return result;
    }

    private bool FilterEvent(RawInputEvent raw)
    {
        if (_state != RecordingState.Recording) return false;

        // Input synthesized by SendInput (our own playback, or other automation tools)
        if (raw.IsInjected) return false;

        if (IsTargetingIgnoredWindow(raw)) return false;

        // Keyboard-only mode
        if (Settings.KeyboardOnly && IsMouseEvent(raw.EventType)) return false;

        // Mouse-only mode
        if (Settings.MouseOnly && IsKeyboardEvent(raw.EventType)) return false;

        // Key exclusion filter
        if (raw.VirtualKeyCode.HasValue && Settings.ExcludedKeys.Contains(raw.VirtualKeyCode.Value))
            return false;

        // Mouse movement threshold
        if (raw.EventType == RawInputEventType.MouseMove && raw.X.HasValue && raw.Y.HasValue)
        {
            int dx = Math.Abs(raw.X.Value - _lastMouseX);
            int dy = Math.Abs(raw.Y.Value - _lastMouseY);
            if (dx < Settings.MouseMovementThreshold && dy < Settings.MouseMovementThreshold)
                return false;
        }

        return true;
    }

    private bool IsTargetingIgnoredWindow(RawInputEvent raw)
    {
        if (IgnoredWindow == IntPtr.Zero) return false;

        if (IsKeyboardEvent(raw.EventType))
            return NativeMethods.GetForegroundWindow() == IgnoredWindow;

        if (raw.X.HasValue && raw.Y.HasValue && raw.EventType != RawInputEventType.MouseMove)
        {
            var hwnd = NativeMethods.WindowFromPoint(new POINT { X = raw.X.Value, Y = raw.Y.Value });
            return hwnd != IntPtr.Zero && NativeMethods.GetAncestor(hwnd, NativeConstants.GA_ROOT) == IgnoredWindow;
        }

        return false;
    }

    private void OnRawInputEvent(RawInputEvent raw)
    {
        long currentTimestamp = raw.TimestampTicks;
        double delayMs = HighResolutionTimer.TicksToMilliseconds(currentTimestamp - _lastTimestamp);
        _lastTimestamp = currentTimestamp;

        if (raw.X.HasValue) _lastMouseX = raw.X.Value;
        if (raw.Y.HasValue) _lastMouseY = raw.Y.Value;

        var inputEvent = ConvertToInputEvent(raw, delayMs);
        if (inputEvent == null) return;

        lock (_lock)
        {
            _recordedEvents.Add(inputEvent);
        }

        EventRecorded?.Invoke(inputEvent);
    }

    private static InputEvent? ConvertToInputEvent(RawInputEvent raw, double delayMs)
    {
        return raw.EventType switch
        {
            RawInputEventType.KeyDown or RawInputEventType.SysKeyDown => new InputEvent
            {
                Type = InputEventType.KeyDown,
                DelayFromPreviousMs = delayMs,
                VirtualKeyCode = raw.VirtualKeyCode,
                ScanCode = raw.ScanCode
            },
            RawInputEventType.KeyUp or RawInputEventType.SysKeyUp => new InputEvent
            {
                Type = InputEventType.KeyUp,
                DelayFromPreviousMs = delayMs,
                VirtualKeyCode = raw.VirtualKeyCode,
                ScanCode = raw.ScanCode
            },
            RawInputEventType.MouseMove => new InputEvent
            {
                Type = InputEventType.MouseMove,
                DelayFromPreviousMs = delayMs,
                X = raw.X,
                Y = raw.Y
            },
            RawInputEventType.LeftButtonDown => new InputEvent
            {
                Type = InputEventType.MouseDown,
                DelayFromPreviousMs = delayMs,
                X = raw.X, Y = raw.Y,
                Button = MouseButton.Left
            },
            RawInputEventType.LeftButtonUp => new InputEvent
            {
                Type = InputEventType.MouseUp,
                DelayFromPreviousMs = delayMs,
                X = raw.X, Y = raw.Y,
                Button = MouseButton.Left
            },
            RawInputEventType.RightButtonDown => new InputEvent
            {
                Type = InputEventType.MouseDown,
                DelayFromPreviousMs = delayMs,
                X = raw.X, Y = raw.Y,
                Button = MouseButton.Right
            },
            RawInputEventType.RightButtonUp => new InputEvent
            {
                Type = InputEventType.MouseUp,
                DelayFromPreviousMs = delayMs,
                X = raw.X, Y = raw.Y,
                Button = MouseButton.Right
            },
            RawInputEventType.MiddleButtonDown => new InputEvent
            {
                Type = InputEventType.MouseDown,
                DelayFromPreviousMs = delayMs,
                X = raw.X, Y = raw.Y,
                Button = MouseButton.Middle
            },
            RawInputEventType.MiddleButtonUp => new InputEvent
            {
                Type = InputEventType.MouseUp,
                DelayFromPreviousMs = delayMs,
                X = raw.X, Y = raw.Y,
                Button = MouseButton.Middle
            },
            RawInputEventType.MouseWheel => new InputEvent
            {
                Type = InputEventType.MouseScroll,
                DelayFromPreviousMs = delayMs,
                X = raw.X, Y = raw.Y,
                ScrollDelta = raw.MouseData.HasValue ? NativeConstants.HIWORD(raw.MouseData.Value) : 0
            },
            RawInputEventType.XButtonDown => new InputEvent
            {
                Type = InputEventType.MouseDown,
                DelayFromPreviousMs = delayMs,
                X = raw.X, Y = raw.Y,
                Button = raw.MouseData.HasValue && NativeConstants.HIWORD(raw.MouseData.Value) == 2
                    ? MouseButton.X2 : MouseButton.X1
            },
            RawInputEventType.XButtonUp => new InputEvent
            {
                Type = InputEventType.MouseUp,
                DelayFromPreviousMs = delayMs,
                X = raw.X, Y = raw.Y,
                Button = raw.MouseData.HasValue && NativeConstants.HIWORD(raw.MouseData.Value) == 2
                    ? MouseButton.X2 : MouseButton.X1
            },
            _ => null
        };
    }

    private static bool IsMouseEvent(RawInputEventType type) =>
        type is RawInputEventType.MouseMove or RawInputEventType.LeftButtonDown or
        RawInputEventType.LeftButtonUp or RawInputEventType.RightButtonDown or
        RawInputEventType.RightButtonUp or RawInputEventType.MiddleButtonDown or
        RawInputEventType.MiddleButtonUp or RawInputEventType.MouseWheel or
        RawInputEventType.MouseHWheel or RawInputEventType.XButtonDown or
        RawInputEventType.XButtonUp;

    private static bool IsKeyboardEvent(RawInputEventType type) =>
        type is RawInputEventType.KeyDown or RawInputEventType.KeyUp or
        RawInputEventType.SysKeyDown or RawInputEventType.SysKeyUp;

    private void SetupCrashRecovery()
    {
        _crashRecoveryPath = Path.Combine(
            Path.GetTempPath(),
            $"macroapp_recovery_{DateTime.Now:yyyyMMdd_HHmmss}{AppConstants.CrashRecoveryExtension}");

        _crashRecoveryTimer = new System.Timers.Timer(AppConstants.CrashRecoveryFlushIntervalMs);
        _crashRecoveryTimer.Elapsed += (_, _) => FlushCrashRecoveryBuffer();
        _crashRecoveryTimer.Start();
    }

    private void FlushCrashRecoveryBuffer()
    {
        if (_crashRecoveryPath == null) return;

        try
        {
            List<InputEvent> snapshot;
            lock (_lock) snapshot = new List<InputEvent>(_recordedEvents);

            var tempMacro = new Macro { Events = snapshot, Name = "CrashRecovery" };
            Serialization.MacroSerializer.SaveSync(tempMacro, _crashRecoveryPath);
        }
        catch
        {
            // Best effort crash recovery
        }
    }

    private void SetState(RecordingState newState)
    {
        _state = newState;
        StateChanged?.Invoke(newState);
    }

    /// <summary>
    /// Disposes the recording engine and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        Stop();
    }
}
