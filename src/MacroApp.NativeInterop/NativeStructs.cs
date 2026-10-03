using System.Runtime.InteropServices;

namespace MacroApp.NativeInterop;

/// <summary>
/// Native Win32 structures used for hooks and input simulation.
/// </summary>

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

/// <summary>
/// Contains information about a low-level keyboard input event.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct KBDLLHOOKSTRUCT
{
    public uint vkCode;
    public uint scanCode;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

/// <summary>
/// Contains information about a low-level mouse input event.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MSLLHOOKSTRUCT
{
    public POINT pt;
    public int mouseData;
    public uint flags;
    public uint time;
    public IntPtr dwExtraInfo;
}

/// <summary>
/// Used by SendInput to store information for synthesizing input events.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct INPUT
{
    public uint type;
    public INPUTUNION union;
}

[StructLayout(LayoutKind.Explicit)]
public struct INPUTUNION
{
    [FieldOffset(0)] public MOUSEINPUT mi;
    [FieldOffset(0)] public KEYBDINPUT ki;
    [FieldOffset(0)] public HARDWAREINPUT hi;
}

[StructLayout(LayoutKind.Sequential)]
public struct MOUSEINPUT
{
    public int dx;
    public int dy;
    public int mouseData;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct KEYBDINPUT
{
    public ushort wVk;
    public ushort wScan;
    public uint dwFlags;
    public uint time;
    public IntPtr dwExtraInfo;
}

[StructLayout(LayoutKind.Sequential)]
public struct HARDWAREINPUT
{
    public uint uMsg;
    public ushort wParamL;
    public ushort wParamH;
}

/// <summary>
/// Message structure for GetMessage / PeekMessage.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr hwnd;
    public uint message;
    public IntPtr wParam;
    public IntPtr lParam;
    public uint time;
    public POINT pt;
}

/// <summary>
/// Header for GetDIBits. Only the fields used for a 32-bit top-down DIB matter here.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;
}
