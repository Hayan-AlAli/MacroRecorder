using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using MacroApp.Core.Models;
using MacroApp.Core.Vision;
using MacroApp.NativeInterop;
using MacroApp.Scripting.Interpreter;

namespace MacroApp.Core.Playback;

/// <summary>
/// Carries out script commands with real input and Win32 window calls.
/// Created per run; must be used from an STA thread for the clipboard commands.
/// </summary>
internal sealed class ScriptActionExecutor : IActionExecutor
{
    private const int WindowPollIntervalMs = 100;
    private const int ImagePollIntervalMs = 250;
    private const double DefaultImageThreshold = 0.9;

    private readonly InputSimulator _input;
    private readonly MacroPlaybackSettings _settings;
    private readonly CancellationToken _ct;
    private readonly IScreenVision? _vision;
    private readonly string? _baseDirectory;

    public ScriptActionExecutor(InputSimulator input, MacroPlaybackSettings settings, CancellationToken ct,
        IScreenVision? vision = null, string? baseDirectory = null)
    {
        _input = input;
        _settings = settings;
        _ct = ct;
        _vision = vision;
        _baseDirectory = baseDirectory;
    }

    /// <summary>
    /// Relative paths in a script are relative to the macro's own folder, so a macro and
    /// its images folder can be copied around together.
    /// </summary>
    private string ResolvePath(string path) =>
        Path.IsPathRooted(path) || string.IsNullOrEmpty(_baseDirectory)
            ? path
            : Path.GetFullPath(Path.Combine(_baseDirectory, path));

    private CoordinateMode Mode => _settings.CoordinateMode;

    // ── Keyboard ────────────────────────────────────────────────────

    public void KeyDown(string keyName) => _input.SendKeyDown(Key(keyName));
    public void KeyUp(string keyName) => _input.SendKeyUp(Key(keyName));
    public void KeyPress(string keyName) => _input.SendKeyPress(Key(keyName));
    public void TypeText(string text) => _input.TypeText(text);

    public void KeyCombo(string[] keys)
    {
        if (keys.Length == 0)
            throw new ArgumentException("KeyCombo needs at least one key, e.g. KeyCombo Ctrl S");

        var codes = keys.Select(Key).ToArray();
        _input.SendKeyCombo(codes[..^1], codes[^1]);
    }

    private static ushort Key(string name) =>
        KeyNames.TryParse(name, out int vk)
            ? (ushort)vk
            : throw new ArgumentException($"Unknown key '{name}'");

    // ── Mouse ───────────────────────────────────────────────────────

    public void MouseMove(int x, int y) => _input.SendMouseMove(x, y, Mode);
    public void MouseDown(string button, int x, int y) => _input.SendMouseDown(Button(button), x, y, Mode);
    public void MouseUp(string button, int x, int y) => _input.SendMouseUp(Button(button), x, y, Mode);
    public void MouseClick(string button, int x, int y) => _input.SendMouseClick(Button(button), x, y, Mode);
    public void MouseDoubleClick(string button, int x, int y) => _input.SendMouseDoubleClick(Button(button), x, y, Mode);
    public void MouseScroll(int delta, int x, int y) => _input.SendMouseScroll(delta, x, y, Mode);

    public void MouseDrag(string button, int startX, int startY, int endX, int endY) =>
        _input.SendMouseDrag(Button(button), startX, startY, endX, endY, Mode);

    private static NativeInterop.MouseButton Button(string name) =>
        Enum.TryParse<NativeInterop.MouseButton>(name, ignoreCase: true, out var button) && Enum.IsDefined(button)
            ? button
            : throw new ArgumentException($"Unknown mouse button '{name}' (use Left, Right, Middle, X1 or X2)");

    // ── Timing ──────────────────────────────────────────────────────

    public void Delay(int milliseconds) => PreciseDelay.Wait(milliseconds / _settings.SpeedMultiplier, _ct);

    public void RandomDelay(int minMs, int maxMs)
    {
        if (maxMs < minMs) (minMs, maxMs) = (maxMs, minMs);
        Delay(Random.Shared.Next(minMs, maxMs + 1));
    }

    // ── Windows ─────────────────────────────────────────────────────

    public void ActivateWindow(string title)
    {
        var hwnd = RequireWindow(title);
        if (NativeMethods.IsIconic(hwnd))
            NativeMethods.ShowWindow(hwnd, NativeConstants.SW_RESTORE);
        NativeMethods.SetForegroundWindow(hwnd);
    }

    public bool WaitForWindow(string title, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (FindWindow(title) == IntPtr.Zero)
        {
            if (sw.ElapsedMilliseconds >= timeoutMs) return false;
            if (_ct.WaitHandle.WaitOne(WindowPollIntervalMs))
                _ct.ThrowIfCancellationRequested();
        }
        return true;
    }

    public void RunProgram(string path, string? arguments) =>
        Process.Start(new ProcessStartInfo(path, arguments ?? string.Empty) { UseShellExecute = true });

    public void CloseWindow(string title) =>
        NativeMethods.PostMessage(RequireWindow(title), NativeConstants.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    public void MinimizeWindow(string title) =>
        NativeMethods.ShowWindow(RequireWindow(title), NativeConstants.SW_SHOWMINIMIZED);

    public void MaximizeWindow(string title) =>
        NativeMethods.ShowWindow(RequireWindow(title), NativeConstants.SW_SHOWMAXIMIZED);

    public void RestoreWindow(string title) =>
        NativeMethods.ShowWindow(RequireWindow(title), NativeConstants.SW_RESTORE);

    private static IntPtr RequireWindow(string title)
    {
        var hwnd = FindWindow(title);
        return hwnd != IntPtr.Zero ? hwnd : throw new InvalidOperationException($"No window found matching '{title}'");
    }

    /// <summary>
    /// Finds a visible top-level window by title: an exact match wins, otherwise the first
    /// window whose title contains <paramref name="title"/> (case-insensitive), so
    /// "Notepad" matches "notes.txt - Notepad".
    /// </summary>
    private static IntPtr FindWindow(string title)
    {
        IntPtr exact = IntPtr.Zero, partial = IntPtr.Zero;

        NativeMethods.EnumWindows((hwnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return true;

            int length = NativeMethods.GetWindowTextLength(hwnd);
            if (length == 0) return true;

            var sb = new StringBuilder(length + 1);
            NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
            string text = sb.ToString();

            if (text.Equals(title, StringComparison.OrdinalIgnoreCase))
            {
                exact = hwnd;
                return false;
            }

            if (partial == IntPtr.Zero && text.Contains(title, StringComparison.OrdinalIgnoreCase))
                partial = hwnd;

            return true;
        }, IntPtr.Zero);

        return exact != IntPtr.Zero ? exact : partial;
    }

    // ── Clipboard ───────────────────────────────────────────────────

    public void SetClipboard(string text) => Clipboard.SetText(text);
    public string GetClipboard() => Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;

    // ── Image matching / OCR ────────────────────────────────────────

    public bool WaitForImage(string imagePath, int timeoutMs, double threshold)
    {
        string path = RequireImage(imagePath);
        var sw = Stopwatch.StartNew();

        while (Vision.FindImage(path, threshold) == null)
        {
            if (sw.ElapsedMilliseconds >= timeoutMs) return false;
            if (_ct.WaitHandle.WaitOne(ImagePollIntervalMs))
                _ct.ThrowIfCancellationRequested();
        }
        return true;
    }

    public void ClickImage(string imagePath, string? button)
    {
        var match = Vision.FindImage(RequireImage(imagePath), DefaultImageThreshold)
                    ?? throw new InvalidOperationException($"'{imagePath}' isn't on screen");

        // Match positions are screen coordinates whatever the macro's coordinate mode is
        _input.SendMouseClick(Button(button ?? "Left"), match.X, match.Y, CoordinateMode.Absolute);
    }

    public bool ImageExists(string imagePath) =>
        Vision.FindImage(RequireImage(imagePath), DefaultImageThreshold) != null;

    public string? OCRGetText(string? region)
    {
        ScreenRegion? area = null;
        if (!string.IsNullOrWhiteSpace(region))
        {
            area = ScreenRegion.TryParse(region, out var parsed)
                ? parsed
                : throw new ArgumentException($"Region '{region}' should be \"x y width height\"");
        }

        return Vision.ReadText(area);
    }

    public bool TextOnScreen(string text) =>
        Normalize(Vision.ReadText(null)).Contains(Normalize(text), StringComparison.OrdinalIgnoreCase);

    // OCR splits lines and spaces unpredictably, so compare with all whitespace collapsed
    private static string Normalize(string text) =>
        string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private IScreenVision Vision =>
        _vision ?? throw new NotSupportedException("Image matching and OCR aren't available in this build.");

    private string RequireImage(string imagePath)
    {
        string path = ResolvePath(imagePath);
        return File.Exists(path) ? path : throw new FileNotFoundException($"Image not found: {path}");
    }

    // ── Misc ────────────────────────────────────────────────────────

    public void PlaySound(string filePath)
    {
        filePath = ResolvePath(filePath);
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"Sound file not found: {filePath}");

        NativeMethods.PlaySound(filePath, IntPtr.Zero, NativeMethods.SND_FILENAME | NativeMethods.SND_SYNC);
    }

    public void ShowMessage(string message) => MessageBox.Show(message, AppConstants.AppName);

    public void MsgBox(string message, string? title) => MessageBox.Show(message, title ?? AppConstants.AppName);
}
