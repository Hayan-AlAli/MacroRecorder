using System.Runtime.InteropServices;

namespace MacroApp.NativeInterop;

/// <summary>
/// Coordinate mode for mouse input simulation.
/// </summary>
public enum CoordinateMode
{
    /// <summary>Absolute screen coordinates.</summary>
    Absolute,
    /// <summary>Relative to current cursor position.</summary>
    Relative,
    /// <summary>Relative to the target window's client area.</summary>
    WindowRelative
}

/// <summary>
/// Mouse button identifiers.
/// </summary>
public enum MouseButton
{
    Left,
    Right,
    Middle,
    X1,
    X2
}

/// <summary>
/// Wraps SendInput() for synthesizing keyboard and mouse events.
/// All coordinate conversions and INPUT struct construction are handled internally.
/// </summary>
public sealed class InputSimulator
{
    // VIRTUALDESK makes the 0-65535 range span every monitor instead of just the primary one.
    private const uint AbsoluteMove =
        NativeConstants.MOUSEEVENTF_MOVE | NativeConstants.MOUSEEVENTF_ABSOLUTE | NativeConstants.MOUSEEVENTF_VIRTUALDESK;

    private readonly int _inputSize = Marshal.SizeOf<INPUT>();

    // Keys and buttons we've pressed but not yet released, so a stopped macro
    // doesn't leave Shift or the left mouse button stuck down.
    private readonly HashSet<ushort> _heldKeys = new();
    private readonly HashSet<MouseButton> _heldButtons = new();
    private readonly object _heldLock = new();

    /// <summary>
    /// Gets or sets the target window handle for WindowRelative coordinate mode.
    /// </summary>
    public IntPtr TargetWindowHandle { get; set; } = IntPtr.Zero;

    // ── Keyboard ────────────────────────────────────────────────────

    /// <summary>
    /// Simulates a key down event.
    /// </summary>
    public void SendKeyDown(ushort virtualKeyCode)
    {
        var input = CreateKeyInput(virtualKeyCode, 0);
        SendSingleInput(input);
        lock (_heldLock) _heldKeys.Add(virtualKeyCode);
    }

    /// <summary>
    /// Simulates a key up event.
    /// </summary>
    public void SendKeyUp(ushort virtualKeyCode)
    {
        var input = CreateKeyInput(virtualKeyCode, NativeConstants.KEYEVENTF_KEYUP);
        SendSingleInput(input);
        lock (_heldLock) _heldKeys.Remove(virtualKeyCode);
    }

    /// <summary>
    /// Releases every key and mouse button this simulator pressed and hasn't released yet.
    /// Call this whenever playback ends, especially when it's cancelled halfway through.
    /// </summary>
    public void ReleaseAll()
    {
        ushort[] keys;
        MouseButton[] buttons;
        lock (_heldLock)
        {
            keys = _heldKeys.ToArray();
            buttons = _heldButtons.ToArray();
            _heldKeys.Clear();
            _heldButtons.Clear();
        }

        var inputs = new List<INPUT>();
        foreach (var key in keys)
            inputs.Add(CreateKeyInput(key, NativeConstants.KEYEVENTF_KEYUP));

        foreach (var button in buttons)
        {
            var (_, upFlag, data) = GetMouseButtonFlags(button);
            inputs.Add(new INPUT
            {
                type = NativeConstants.INPUT_MOUSE,
                union = new INPUTUNION { mi = new MOUSEINPUT { mouseData = data, dwFlags = upFlag } }
            });
        }

        if (inputs.Count > 0)
            NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), _inputSize);
    }

    /// <summary>
    /// Simulates a key press (down + up).
    /// </summary>
    public void SendKeyPress(ushort virtualKeyCode)
    {
        var inputs = new[]
        {
            CreateKeyInput(virtualKeyCode, 0),
            CreateKeyInput(virtualKeyCode, NativeConstants.KEYEVENTF_KEYUP)
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, _inputSize);
    }

    /// <summary>
    /// Types a string by sending Unicode character events.
    /// </summary>
    public void TypeText(string text)
    {
        var inputs = new List<INPUT>();
        foreach (char c in text)
        {
            inputs.Add(new INPUT
            {
                type = NativeConstants.INPUT_KEYBOARD,
                union = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = NativeConstants.KEYEVENTF_UNICODE,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });
            inputs.Add(new INPUT
            {
                type = NativeConstants.INPUT_KEYBOARD,
                union = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = NativeConstants.KEYEVENTF_UNICODE | NativeConstants.KEYEVENTF_KEYUP,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                }
            });
        }

        if (inputs.Count > 0)
        {
            NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), _inputSize);
        }
    }

    /// <summary>
    /// Sends a key combination (e.g., Ctrl+C). Presses all modifiers, then the key, then releases.
    /// </summary>
    public void SendKeyCombo(ushort[] modifiers, ushort key)
    {
        var inputs = new List<INPUT>();

        // Press modifiers
        foreach (var mod in modifiers)
            inputs.Add(CreateKeyInput(mod, 0));

        // Press and release key
        inputs.Add(CreateKeyInput(key, 0));
        inputs.Add(CreateKeyInput(key, NativeConstants.KEYEVENTF_KEYUP));

        // Release modifiers in reverse
        for (int i = modifiers.Length - 1; i >= 0; i--)
            inputs.Add(CreateKeyInput(modifiers[i], NativeConstants.KEYEVENTF_KEYUP));

        NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), _inputSize);
    }

    // ── Mouse ───────────────────────────────────────────────────────

    /// <summary>
    /// Moves the mouse to the specified coordinates.
    /// </summary>
    public void SendMouseMove(int x, int y, CoordinateMode mode = CoordinateMode.Absolute)
    {
        var (absX, absY) = ConvertCoordinates(x, y, mode);
        var (normX, normY) = NormalizeAbsoluteCoordinates(absX, absY);

        var input = new INPUT
        {
            type = NativeConstants.INPUT_MOUSE,
            union = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = normX,
                    dy = normY,
                    mouseData = 0,
                    dwFlags = AbsoluteMove,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendSingleInput(input);
    }

    /// <summary>
    /// Simulates a mouse button press (down).
    /// </summary>
    public void SendMouseDown(MouseButton button, int x, int y, CoordinateMode mode = CoordinateMode.Absolute)
    {
        MoveAndClick(button, x, y, mode, isDown: true);
    }

    /// <summary>
    /// Simulates a mouse button release (up).
    /// </summary>
    public void SendMouseUp(MouseButton button, int x, int y, CoordinateMode mode = CoordinateMode.Absolute)
    {
        MoveAndClick(button, x, y, mode, isDown: false);
    }

    /// <summary>
    /// Simulates a mouse click (down + up) at the specified position.
    /// </summary>
    public void SendMouseClick(MouseButton button, int x, int y, CoordinateMode mode = CoordinateMode.Absolute)
    {
        var (absX, absY) = ConvertCoordinates(x, y, mode);
        var (normX, normY) = NormalizeAbsoluteCoordinates(absX, absY);
        var (downFlag, upFlag, data) = GetMouseButtonFlags(button);

        var inputs = new[]
        {
            new INPUT
            {
                type = NativeConstants.INPUT_MOUSE,
                union = new INPUTUNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = normX, dy = normY,
                        mouseData = data,
                        dwFlags = AbsoluteMove | downFlag,
                        time = 0, dwExtraInfo = IntPtr.Zero
                    }
                }
            },
            new INPUT
            {
                type = NativeConstants.INPUT_MOUSE,
                union = new INPUTUNION
                {
                    mi = new MOUSEINPUT
                    {
                        dx = normX, dy = normY,
                        mouseData = data,
                        dwFlags = AbsoluteMove | upFlag,
                        time = 0, dwExtraInfo = IntPtr.Zero
                    }
                }
            }
        };

        NativeMethods.SendInput((uint)inputs.Length, inputs, _inputSize);
    }

    /// <summary>
    /// Simulates a mouse double-click at the specified position.
    /// </summary>
    public void SendMouseDoubleClick(MouseButton button, int x, int y, CoordinateMode mode = CoordinateMode.Absolute)
    {
        SendMouseClick(button, x, y, mode);
        Thread.Sleep(50);
        SendMouseClick(button, x, y, mode);
    }

    /// <summary>
    /// Simulates a mouse scroll event.
    /// </summary>
    public void SendMouseScroll(int delta, int x, int y, CoordinateMode mode = CoordinateMode.Absolute)
    {
        var (absX, absY) = ConvertCoordinates(x, y, mode);
        var (normX, normY) = NormalizeAbsoluteCoordinates(absX, absY);

        var input = new INPUT
        {
            type = NativeConstants.INPUT_MOUSE,
            union = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = normX,
                    dy = normY,
                    mouseData = delta,
                    dwFlags = NativeConstants.MOUSEEVENTF_WHEEL | AbsoluteMove,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendSingleInput(input);
    }

    /// <summary>
    /// Simulates a mouse drag from one position to another.
    /// </summary>
    public void SendMouseDrag(MouseButton button, int startX, int startY, int endX, int endY,
        CoordinateMode mode = CoordinateMode.Absolute, int steps = 20, int stepDelayMs = 5)
    {
        SendMouseDown(button, startX, startY, mode);
        Thread.Sleep(stepDelayMs);

        for (int i = 1; i <= steps; i++)
        {
            int x = startX + (endX - startX) * i / steps;
            int y = startY + (endY - startY) * i / steps;
            SendMouseMove(x, y, mode);
            Thread.Sleep(stepDelayMs);
        }

        SendMouseUp(button, endX, endY, mode);
    }

    // ── Private Helpers ─────────────────────────────────────────────

    private void SendSingleInput(INPUT input)
    {
        NativeMethods.SendInput(1, new[] { input }, _inputSize);
    }

    private static INPUT CreateKeyInput(ushort virtualKeyCode, uint flags)
    {
        uint scanCode = NativeMethods.MapVirtualKey(virtualKeyCode, 0);
        if (KeyNames.IsExtendedKey(virtualKeyCode))
            flags |= NativeConstants.KEYEVENTF_EXTENDEDKEY;
        return new INPUT
        {
            type = NativeConstants.INPUT_KEYBOARD,
            union = new INPUTUNION
            {
                ki = new KEYBDINPUT
                {
                    wVk = virtualKeyCode,
                    wScan = (ushort)scanCode,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };
    }

    private void MoveAndClick(MouseButton button, int x, int y, CoordinateMode mode, bool isDown)
    {
        var (absX, absY) = ConvertCoordinates(x, y, mode);
        var (normX, normY) = NormalizeAbsoluteCoordinates(absX, absY);
        var (downFlag, upFlag, data) = GetMouseButtonFlags(button);
        uint flag = isDown ? downFlag : upFlag;

        var input = new INPUT
        {
            type = NativeConstants.INPUT_MOUSE,
            union = new INPUTUNION
            {
                mi = new MOUSEINPUT
                {
                    dx = normX,
                    dy = normY,
                    mouseData = data,
                    dwFlags = AbsoluteMove | flag,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            }
        };

        SendSingleInput(input);

        lock (_heldLock)
        {
            if (isDown) _heldButtons.Add(button);
            else _heldButtons.Remove(button);
        }
    }

    private (int absX, int absY) ConvertCoordinates(int x, int y, CoordinateMode mode)
    {
        switch (mode)
        {
            case CoordinateMode.Absolute:
                return (x, y);

            case CoordinateMode.Relative:
                NativeMethods.GetCursorPos(out POINT currentPos);
                return (currentPos.X + x, currentPos.Y + y);

            case CoordinateMode.WindowRelative:
                if (TargetWindowHandle == IntPtr.Zero || !NativeMethods.IsWindow(TargetWindowHandle))
                    return (x, y); // fallback to absolute

                var point = new POINT { X = x, Y = y };
                NativeMethods.ClientToScreen(TargetWindowHandle, ref point);
                return (point.X, point.Y);

            default:
                return (x, y);
        }
    }

    private static (int normX, int normY) NormalizeAbsoluteCoordinates(int absX, int absY)
    {
        // SendInput MOUSEEVENTF_ABSOLUTE uses 0-65535 normalized coordinates
        int screenWidth = NativeMethods.GetSystemMetrics(NativeConstants.SM_CXVIRTUALSCREEN);
        int screenHeight = NativeMethods.GetSystemMetrics(NativeConstants.SM_CYVIRTUALSCREEN);
        int screenLeft = NativeMethods.GetSystemMetrics(NativeConstants.SM_XVIRTUALSCREEN);
        int screenTop = NativeMethods.GetSystemMetrics(NativeConstants.SM_YVIRTUALSCREEN);

        if (screenWidth <= 1 || screenHeight <= 1)
            return (0, 0);

        // Map the last pixel to exactly 65535; dividing by the full width lands one pixel short.
        int normX = (int)Math.Round((absX - screenLeft) * 65535.0 / (screenWidth - 1));
        int normY = (int)Math.Round((absY - screenTop) * 65535.0 / (screenHeight - 1));

        return (normX, normY);
    }

    private static (uint downFlag, uint upFlag, int data) GetMouseButtonFlags(MouseButton button) => button switch
    {
        MouseButton.Left => (NativeConstants.MOUSEEVENTF_LEFTDOWN, NativeConstants.MOUSEEVENTF_LEFTUP, 0),
        MouseButton.Right => (NativeConstants.MOUSEEVENTF_RIGHTDOWN, NativeConstants.MOUSEEVENTF_RIGHTUP, 0),
        MouseButton.Middle => (NativeConstants.MOUSEEVENTF_MIDDLEDOWN, NativeConstants.MOUSEEVENTF_MIDDLEUP, 0),
        MouseButton.X1 => (NativeConstants.MOUSEEVENTF_XDOWN, NativeConstants.MOUSEEVENTF_XUP, (int)NativeConstants.XBUTTON1),
        MouseButton.X2 => (NativeConstants.MOUSEEVENTF_XDOWN, NativeConstants.MOUSEEVENTF_XUP, (int)NativeConstants.XBUTTON2),
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };
}
