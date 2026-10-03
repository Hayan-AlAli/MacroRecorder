using MacroApp.Scripting.Interpreter;

namespace MacroApp.Tests.Scripting;

/// <summary>
/// Records every action as a short string ("KeyPress A", "MouseClick Left 10 20") so tests
/// can assert on what a script would have done.
/// </summary>
internal sealed class FakeExecutor : IActionExecutor
{
    public List<string> Calls { get; } = new();
    public string Clipboard { get; set; } = string.Empty;
    public HashSet<string> Windows { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void KeyDown(string keyName) => Calls.Add($"KeyDown {keyName}");
    public void KeyUp(string keyName) => Calls.Add($"KeyUp {keyName}");
    public void KeyPress(string keyName) => Calls.Add($"KeyPress {keyName}");
    public void TypeText(string text) => Calls.Add($"TypeText {text}");
    public void KeyCombo(string[] keys) => Calls.Add($"KeyCombo {string.Join("+", keys)}");

    public void MouseMove(int x, int y) => Calls.Add($"MouseMove {x} {y}");
    public void MouseDown(string button, int x, int y) => Calls.Add($"MouseDown {button} {x} {y}");
    public void MouseUp(string button, int x, int y) => Calls.Add($"MouseUp {button} {x} {y}");
    public void MouseClick(string button, int x, int y) => Calls.Add($"MouseClick {button} {x} {y}");
    public void MouseDoubleClick(string button, int x, int y) => Calls.Add($"MouseDoubleClick {button} {x} {y}");
    public void MouseScroll(int delta, int x, int y) => Calls.Add($"MouseScroll {delta} {x} {y}");
    public void MouseDrag(string button, int startX, int startY, int endX, int endY) =>
        Calls.Add($"MouseDrag {button} {startX} {startY} {endX} {endY}");

    public void Delay(int milliseconds) => Calls.Add($"Delay {milliseconds}");
    public void RandomDelay(int minMs, int maxMs) => Calls.Add($"RandomDelay {minMs} {maxMs}");

    public void ActivateWindow(string title) => Calls.Add($"ActivateWindow {title}");
    public bool WaitForWindow(string title, int timeoutMs) => Windows.Contains(title);
    public void RunProgram(string path, string? arguments) => Calls.Add($"RunProgram {path} {arguments}".TrimEnd());
    public void CloseWindow(string title) => Calls.Add($"CloseWindow {title}");
    public void MinimizeWindow(string title) => Calls.Add($"MinWindow {title}");
    public void MaximizeWindow(string title) => Calls.Add($"MaxWindow {title}");
    public void RestoreWindow(string title) => Calls.Add($"RestoreWindow {title}");

    public void SetClipboard(string text) => Clipboard = text;
    public string GetClipboard() => Clipboard;

    /// <summary>Images that are "on screen".</summary>
    public HashSet<string> VisibleImages { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string ScreenText { get; set; } = string.Empty;

    public bool WaitForImage(string imagePath, int timeoutMs, double threshold) => VisibleImages.Contains(imagePath);

    public void ClickImage(string imagePath, string? button)
    {
        if (!VisibleImages.Contains(imagePath)) throw new InvalidOperationException($"'{imagePath}' isn't on screen");
        Calls.Add($"ClickImage {imagePath} {button ?? "Left"}");
    }

    public bool ImageExists(string imagePath) => VisibleImages.Contains(imagePath);

    public string? OCRGetText(string? region)
    {
        Calls.Add($"OCRGetText {region}".TrimEnd());
        return ScreenText;
    }

    public bool TextOnScreen(string text) => ScreenText.Contains(text, StringComparison.OrdinalIgnoreCase);

    public void PlaySound(string filePath) => Calls.Add($"PlaySound {filePath}");
    public void ShowMessage(string message) => Calls.Add($"ShowMessage {message}");
    public void MsgBox(string message, string? title) => Calls.Add($"MsgBox {message}");
}
