using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using MacroApp.Core.Vision;
using MacroApp.NativeInterop;

namespace MacroApp.UI.Views;

/// <summary>
/// A dimmed overlay across every monitor where the user drags out a rectangle.
/// <see cref="Selection"/> is in physical screen pixels. Esc or right-click cancels.
/// </summary>
public sealed class RegionPickerWindow : Window
{
    private readonly Canvas _canvas = new();
    private readonly Rectangle _box = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(0x3B, 0x8E, 0xEA)),
        StrokeThickness = 2,
        Fill = new SolidColorBrush(Color.FromArgb(0x28, 0x3B, 0x8E, 0xEA)),
        Visibility = Visibility.Collapsed,
    };

    private POINT? _start;

    public ScreenRegion? Selection { get; private set; }

    public RegionPickerWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(70, 0, 0, 0));
        Topmost = true;
        ShowInTaskbar = false;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _canvas.Children.Add(_box);
        _canvas.Children.Add(new TextBlock
        {
            Text = "Drag around the part of the screen to capture.  Esc to cancel.",
            Foreground = Brushes.White,
            FontSize = 15,
            FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI"),
            Margin = new Thickness(24),
        });
        Content = _canvas;

        SourceInitialized += (_, _) => CoverAllMonitors();
        Loaded += (_, _) => { CoverAllMonitors(); Activate(); };
    }

    /// <summary>
    /// WPF positions windows in DIPs relative to one monitor's DPI, which doesn't add up across
    /// mixed-DPI setups. Placing the window in raw pixels does.
    /// </summary>
    private void CoverAllMonitors()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST,
            NativeMethods.GetSystemMetrics(NativeConstants.SM_XVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeConstants.SM_YVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeConstants.SM_CXVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeConstants.SM_CYVIRTUALSCREEN),
            NativeMethods.SWP_SHOWWINDOW);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        NativeMethods.GetCursorPos(out var p);
        _start = p;
        CaptureMouse();
        UpdateBox(p);
        _box.Visibility = Visibility.Visible;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_start == null) return;
        NativeMethods.GetCursorPos(out var p);
        UpdateBox(p);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_start is not { } start) return;
        ReleaseMouseCapture();
        NativeMethods.GetCursorPos(out var end);

        int x = Math.Min(start.X, end.X), y = Math.Min(start.Y, end.Y);
        int w = Math.Abs(end.X - start.X), h = Math.Abs(end.Y - start.Y);

        // A click without a drag is almost certainly a mistake; let them try again
        if (w < 4 || h < 4)
        {
            _start = null;
            _box.Visibility = Visibility.Collapsed;
            return;
        }

        Selection = new ScreenRegion(x, y, w, h);
        DialogResult = true;
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e) => DialogResult = false;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) DialogResult = false;
    }

    private void UpdateBox(POINT current)
    {
        if (_start is not { } start) return;

        // Cursor positions are physical pixels; PointFromScreen turns them into this window's DIPs
        var a = PointFromScreen(new Point(start.X, start.Y));
        var b = PointFromScreen(new Point(current.X, current.Y));

        Canvas.SetLeft(_box, Math.Min(a.X, b.X));
        Canvas.SetTop(_box, Math.Min(a.Y, b.Y));
        _box.Width = Math.Abs(b.X - a.X);
        _box.Height = Math.Abs(b.Y - a.Y);
    }
}
