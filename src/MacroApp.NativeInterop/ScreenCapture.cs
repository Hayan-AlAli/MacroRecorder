using System.Runtime.InteropServices;

namespace MacroApp.NativeInterop;

/// <summary>
/// A screenshot as 32-bit BGRA pixels, top row first. <see cref="Left"/>/<see cref="Top"/> are the
/// screen coordinates of the top-left pixel (negative for monitors left of or above the primary one).
/// </summary>
public sealed record ScreenImage(byte[] Pixels, int Width, int Height, int Left, int Top)
{
    public int Stride => Width * 4;

    /// <summary>Copies out part of the image. Coordinates are in screen pixels, clipped to the image.</summary>
    public ScreenImage Crop(int x, int y, int width, int height)
    {
        int x0 = Math.Clamp(x - Left, 0, Width);
        int y0 = Math.Clamp(y - Top, 0, Height);
        int x1 = Math.Clamp(x - Left + width, 0, Width);
        int y1 = Math.Clamp(y - Top + height, 0, Height);
        int w = x1 - x0, h = y1 - y0;

        var pixels = new byte[w * h * 4];
        for (int row = 0; row < h; row++)
            Buffer.BlockCopy(Pixels, (y0 + row) * Stride + x0 * 4, pixels, row * w * 4, w * 4);

        return new ScreenImage(pixels, w, h, Left + x0, Top + y0);
    }
}

/// <summary>
/// Grabs screen pixels with GDI. Coordinates are physical pixels, which is what the app
/// sees everywhere since it runs per-monitor DPI aware.
/// </summary>
public static class ScreenCapture
{
    /// <summary>Captures every monitor at once.</summary>
    public static ScreenImage CaptureVirtualScreen() => Capture(
        NativeMethods.GetSystemMetrics(NativeConstants.SM_XVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeConstants.SM_YVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeConstants.SM_CXVIRTUALSCREEN),
        NativeMethods.GetSystemMetrics(NativeConstants.SM_CYVIRTUALSCREEN));

    /// <summary>Captures a rectangle of the screen.</summary>
    public static ScreenImage Capture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Capture area must not be empty.");

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
        IntPtr bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
        IntPtr previous = NativeMethods.SelectObject(memDc, bitmap);

        try
        {
            // CAPTUREBLT includes layered (semi-transparent) windows
            if (!NativeMethods.BitBlt(memDc, 0, 0, width, height, screenDc, x, y,
                    NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT))
                throw new InvalidOperationException($"BitBlt failed (Win32 error {Marshal.GetLastWin32Error()}).");

            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // negative = top-down rows
                biPlanes = 1,
                biBitCount = 32,
            };

            var pixels = new byte[width * height * 4];
            NativeMethods.SelectObject(memDc, previous); // the bitmap can't be selected during GetDIBits
            if (NativeMethods.GetDIBits(memDc, bitmap, 0, (uint)height, pixels, ref header, NativeMethods.DIB_RGB_COLORS) == 0)
                throw new InvalidOperationException("GetDIBits failed.");

            // GDI leaves the alpha byte at 0; make the image opaque so saved PNGs aren't invisible
            for (int i = 3; i < pixels.Length; i += 4)
                pixels[i] = 255;

            return new ScreenImage(pixels, width, height, x, y);
        }
        finally
        {
            NativeMethods.SelectObject(memDc, previous);
            NativeMethods.DeleteObject(bitmap);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
