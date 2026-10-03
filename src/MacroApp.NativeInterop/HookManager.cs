using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Diagnostics;

namespace MacroApp.NativeInterop;

/// <summary>
/// Represents a raw input event captured by the low-level hooks.
/// </summary>
public record RawInputEvent(
    RawInputEventType EventType,
    long TimestampTicks,
    int? VirtualKeyCode,
    uint? ScanCode,
    uint? KeyFlags,
    int? X,
    int? Y,
    int? MouseData,
    uint? MouseFlags,
    bool IsInjected = false);

/// <summary>
/// Types of raw input events from hooks.
/// </summary>
public enum RawInputEventType
{
    KeyDown,
    KeyUp,
    SysKeyDown,
    SysKeyUp,
    MouseMove,
    LeftButtonDown,
    LeftButtonUp,
    RightButtonDown,
    RightButtonUp,
    MiddleButtonDown,
    MiddleButtonUp,
    MouseWheel,
    MouseHWheel,
    XButtonDown,
    XButtonUp
}

/// <summary>
/// Manages global low-level keyboard and mouse hooks on a dedicated STA thread.
/// Hook callbacks are kept minimal (&lt;5ms) — they only stamp a timestamp and enqueue.
/// Exposes captured events as IObservable&lt;RawInputEvent&gt; via Rx.
/// </summary>
public sealed class HookManager : IDisposable
{
    private readonly Subject<RawInputEvent> _inputSubject = new();
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private IntPtr _mouseHookId = IntPtr.Zero;
    private Thread? _hookThread;
    private uint _hookThreadId;
    private volatile bool _isRunning;
    private Exception? _startError;
    private bool _disposed;

    // CRITICAL: prevent GC collection of delegates while hooks are active
    private NativeMethods.LowLevelHookProc? _keyboardProc;
    private NativeMethods.LowLevelHookProc? _mouseProc;

    /// <summary>
    /// Observable stream of raw input events captured by the hooks.
    /// </summary>
    public IObservable<RawInputEvent> InputEvents => _inputSubject.AsObservable();

    /// <summary>
    /// Gets whether the hooks are currently active.
    /// </summary>
    public bool IsRunning => _isRunning;

    /// <summary>
    /// Gets or sets whether keyboard events should be captured.
    /// </summary>
    public bool CaptureKeyboard { get; set; } = true;

    /// <summary>
    /// Gets or sets whether mouse events should be captured.
    /// </summary>
    public bool CaptureMouse { get; set; } = true;

    /// <summary>
    /// Starts the hook manager on a dedicated STA thread with a Win32 message loop.
    /// </summary>
    public void Start()
    {
        if (_isRunning) return;

        var readySignal = new ManualResetEventSlim(false);
        _startError = null;

        _hookThread = new Thread(() => HookThreadProc(readySignal))
        {
            Name = "MacroApp.HookThread",
            IsBackground = true
        };
        _hookThread.SetApartmentState(ApartmentState.STA);
        _hookThread.Start();

        // Wait for hooks to be installed before returning
        if (!readySignal.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("Hook thread failed to start within 5 seconds.");
        }

        if (_startError != null)
            throw new InvalidOperationException("Could not install the input hooks.", _startError);
    }

    /// <summary>
    /// Stops the hook manager and cleans up the hook thread.
    /// </summary>
    public void Stop()
    {
        if (!_isRunning) return;

        // Post WM_QUIT to the hook thread's message loop
        NativeMethods.PostThreadMessage(_hookThreadId, NativeConstants.WM_QUIT, IntPtr.Zero, IntPtr.Zero);

        _hookThread?.Join(TimeSpan.FromSeconds(3));
        _isRunning = false;
    }

    private void HookThreadProc(ManualResetEventSlim readySignal)
    {
        try
        {
            _hookThreadId = NativeMethods.GetCurrentThreadId();

            // Store delegates as fields to prevent GC collection
            _keyboardProc = KeyboardHookCallback;
            _mouseProc = MouseHookCallback;

            IntPtr moduleHandle = NativeMethods.GetModuleHandle(null);

            if (CaptureKeyboard)
            {
                _keyboardHookId = NativeMethods.SetWindowsHookEx(
                    NativeConstants.WH_KEYBOARD_LL, _keyboardProc, moduleHandle, 0);

                if (_keyboardHookId == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException(
                        $"Failed to install keyboard hook. Win32 error: {error}");
                }
            }

            if (CaptureMouse)
            {
                _mouseHookId = NativeMethods.SetWindowsHookEx(
                    NativeConstants.WH_MOUSE_LL, _mouseProc, moduleHandle, 0);

                if (_mouseHookId == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    // Clean up keyboard hook if mouse hook fails
                    if (_keyboardHookId != IntPtr.Zero)
                    {
                        NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
                        _keyboardHookId = IntPtr.Zero;
                    }
                    throw new InvalidOperationException(
                        $"Failed to install mouse hook. Win32 error: {error}");
                }
            }

            _isRunning = true;
            readySignal.Set();

            // Run message loop — required for low-level hooks to receive callbacks
            while (NativeMethods.GetMessage(out MSG msg, IntPtr.Zero, 0, 0))
            {
                NativeMethods.TranslateMessage(ref msg);
                NativeMethods.DispatchMessage(ref msg);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HookManager thread error: {ex}");
            _startError = ex;
            if (!_isRunning) readySignal.Set(); // Unblock caller even on failure
        }
        finally
        {
            UninstallHooks();
            _isRunning = false;
        }
    }

    private void UninstallHooks()
    {
        if (_keyboardHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
            _keyboardHookId = IntPtr.Zero;
        }

        if (_mouseHookId != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var hookStruct = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                long timestamp = HighResolutionTimer.GetTimestamp();

                var eventType = (int)wParam switch
                {
                    NativeConstants.WM_KEYDOWN => RawInputEventType.KeyDown,
                    NativeConstants.WM_KEYUP => RawInputEventType.KeyUp,
                    NativeConstants.WM_SYSKEYDOWN => RawInputEventType.SysKeyDown,
                    NativeConstants.WM_SYSKEYUP => RawInputEventType.SysKeyUp,
                    _ => (RawInputEventType?)null
                };

                if (eventType.HasValue)
                {
                    var evt = new RawInputEvent(
                        EventType: eventType.Value,
                        TimestampTicks: timestamp,
                        VirtualKeyCode: (int)hookStruct.vkCode,
                        ScanCode: hookStruct.scanCode,
                        KeyFlags: hookStruct.flags,
                        X: null,
                        Y: null,
                        MouseData: null,
                        MouseFlags: null,
                        IsInjected: (hookStruct.flags & NativeConstants.LLKHF_INJECTED) != 0);

                    _inputSubject.OnNext(evt);
                }
            }
            catch
            {
                // Swallow exceptions in hook callback — must return quickly
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                long timestamp = HighResolutionTimer.GetTimestamp();

                var eventType = (int)wParam switch
                {
                    NativeConstants.WM_MOUSEMOVE => RawInputEventType.MouseMove,
                    NativeConstants.WM_LBUTTONDOWN => RawInputEventType.LeftButtonDown,
                    NativeConstants.WM_LBUTTONUP => RawInputEventType.LeftButtonUp,
                    NativeConstants.WM_RBUTTONDOWN => RawInputEventType.RightButtonDown,
                    NativeConstants.WM_RBUTTONUP => RawInputEventType.RightButtonUp,
                    NativeConstants.WM_MBUTTONDOWN => RawInputEventType.MiddleButtonDown,
                    NativeConstants.WM_MBUTTONUP => RawInputEventType.MiddleButtonUp,
                    NativeConstants.WM_MOUSEWHEEL => RawInputEventType.MouseWheel,
                    NativeConstants.WM_MOUSEHWHEEL => RawInputEventType.MouseHWheel,
                    NativeConstants.WM_XBUTTONDOWN => RawInputEventType.XButtonDown,
                    NativeConstants.WM_XBUTTONUP => RawInputEventType.XButtonUp,
                    _ => (RawInputEventType?)null
                };

                if (eventType.HasValue)
                {
                    var evt = new RawInputEvent(
                        EventType: eventType.Value,
                        TimestampTicks: timestamp,
                        VirtualKeyCode: null,
                        ScanCode: null,
                        KeyFlags: null,
                        X: hookStruct.pt.X,
                        Y: hookStruct.pt.Y,
                        MouseData: hookStruct.mouseData,
                        MouseFlags: hookStruct.flags,
                        IsInjected: (hookStruct.flags & NativeConstants.LLMHF_INJECTED) != 0);

                    _inputSubject.OnNext(evt);
                }
            }
            catch
            {
                // Swallow exceptions in hook callback — must return quickly
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// Disposes the hook manager, ensuring all hooks are removed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _inputSubject.OnCompleted();
        _inputSubject.Dispose();
    }
}
