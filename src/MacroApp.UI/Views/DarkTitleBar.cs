using System.Windows;
using System.Windows.Interop;
using MacroApp.NativeInterop;

namespace MacroApp.UI.Views;

/// <summary>
/// Asks Windows to draw a window's title bar dark so it matches the app instead of
/// sitting there as a white strip. No effect on Windows versions that don't support it.
/// </summary>
internal static class DarkTitleBar
{
    public static void Apply(Window window)
    {
        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            Set(window);
        else
            window.SourceInitialized += (_, _) => Set(window);
    }

    private static void Set(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int on = 1;
        if (NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
    }
}
