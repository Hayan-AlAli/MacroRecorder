namespace MacroApp.Core.Models;

/// <summary>
/// Types of input events that can be recorded and played back.
/// </summary>
public enum InputEventType
{
    KeyDown,
    KeyUp,
    MouseMove,
    MouseDown,
    MouseUp,
    MouseScroll,
    MouseDrag
}

/// <summary>
/// Represents a single recorded or scripted input event with timing information.
/// Immutable record type for thread safety in the recording/playback pipeline.
/// </summary>
public record InputEvent
{
    /// <summary>Type of input event.</summary>
    public InputEventType Type { get; init; }

    /// <summary>
    /// Delay in milliseconds from the previous event.
    /// For the first event, this is 0.
    /// </summary>
    public double DelayFromPreviousMs { get; init; }

    /// <summary>Virtual key code for keyboard events.</summary>
    public int? VirtualKeyCode { get; init; }

    /// <summary>Scan code for keyboard events.</summary>
    public uint? ScanCode { get; init; }

    /// <summary>X coordinate for mouse events.</summary>
    public int? X { get; init; }

    /// <summary>Y coordinate for mouse events.</summary>
    public int? Y { get; init; }

    /// <summary>Mouse button for mouse click/drag events.</summary>
    public NativeInterop.MouseButton? Button { get; init; }

    /// <summary>Scroll delta for mouse scroll events.</summary>
    public int? ScrollDelta { get; init; }

    /// <summary>End X coordinate for drag events.</summary>
    public int? EndX { get; init; }

    /// <summary>End Y coordinate for drag events.</summary>
    public int? EndY { get; init; }

    /// <summary>Optional text annotation or metadata.</summary>
    public string? Annotation { get; init; }

    /// <summary>Whether a breakpoint is set on this event.</summary>
    public bool HasBreakpoint { get; init; }

    /// <summary>The line number in the script editor (for highlighting).</summary>
    public int LineNumber { get; init; }

    /// <summary>
    /// Creates a readable description for display in the event list.
    /// </summary>
    public string ToDisplayString()
    {
        return Type switch
        {
            InputEventType.KeyDown => $"Key Down: {FormatKeyName()} ({DelayFromPreviousMs:F0}ms)",
            InputEventType.KeyUp => $"Key Up: {FormatKeyName()} ({DelayFromPreviousMs:F0}ms)",
            InputEventType.MouseMove => $"Mouse Move: ({X}, {Y}) ({DelayFromPreviousMs:F0}ms)",
            InputEventType.MouseDown => $"Mouse Down: {Button} at ({X}, {Y}) ({DelayFromPreviousMs:F0}ms)",
            InputEventType.MouseUp => $"Mouse Up: {Button} at ({X}, {Y}) ({DelayFromPreviousMs:F0}ms)",
            InputEventType.MouseScroll => $"Mouse Scroll: {ScrollDelta} at ({X}, {Y}) ({DelayFromPreviousMs:F0}ms)",
            InputEventType.MouseDrag => $"Mouse Drag: ({X}, {Y}) → ({EndX}, {EndY}) ({DelayFromPreviousMs:F0}ms)",
            _ => $"Unknown ({DelayFromPreviousMs:F0}ms)"
        };
    }

    /// <summary>
    /// Converts the event to its script text representation.
    /// </summary>
    public string ToScriptLine()
    {
        var delay = DelayFromPreviousMs > 0 ? $"Delay {DelayFromPreviousMs:F0}\n" : "";
        return Type switch
        {
            InputEventType.KeyDown => $"{delay}KeyDown {FormatKeyName()}",
            InputEventType.KeyUp => $"{delay}KeyUp {FormatKeyName()}",
            InputEventType.MouseMove => $"{delay}MouseMove {X} {Y}",
            InputEventType.MouseDown => $"{delay}MouseDown {Button} {X} {Y}",
            InputEventType.MouseUp => $"{delay}MouseUp {Button} {X} {Y}",
            InputEventType.MouseScroll => $"{delay}MouseScroll {ScrollDelta} {X} {Y}",
            InputEventType.MouseDrag => $"{delay}MouseDrag {Button} {X} {Y} {EndX} {EndY}",
            _ => $"// Unknown event"
        };
    }

    private string FormatKeyName()
    {
        if (VirtualKeyCode == null) return "Unknown";
        try
        {
            return ((System.Windows.Input.Key)VirtualKeyCode.Value).ToString();
        }
        catch
        {
            return $"0x{VirtualKeyCode:X2}";
        }
    }
}
