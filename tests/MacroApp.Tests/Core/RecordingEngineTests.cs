using FluentAssertions;
using MacroApp.Core.Models;
using MacroApp.Core.Recording;
using Xunit;

namespace MacroApp.Tests.Core;

public class RecordingEngineTests
{
    [Fact]
    public void Trailing_modifier_presses_from_a_stop_hotkey_are_dropped()
    {
        var events = new List<InputEvent>
        {
            new() { Type = InputEventType.KeyDown, VirtualKeyCode = 0x41 },
            new() { Type = InputEventType.KeyUp, VirtualKeyCode = 0x41 },
            new() { Type = InputEventType.KeyDown, VirtualKeyCode = 0xA2 }, // LeftCtrl
            new() { Type = InputEventType.KeyDown, VirtualKeyCode = 0xA0 }, // LeftShift
        };

        RecordingEngine.TrimTrailingModifierPresses(events);

        events.Should().HaveCount(2);
    }

    [Fact]
    public void Modifiers_in_the_middle_are_kept()
    {
        var events = new List<InputEvent>
        {
            new() { Type = InputEventType.KeyDown, VirtualKeyCode = 0xA2 },
            new() { Type = InputEventType.KeyDown, VirtualKeyCode = 0x43 },
        };

        RecordingEngine.TrimTrailingModifierPresses(events);

        events.Should().HaveCount(2);
    }
}
