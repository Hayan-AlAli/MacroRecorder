using FluentAssertions;
using MacroApp.Core.Models;
using MacroApp.NativeInterop;
using MacroApp.Scripting.Parser;
using Xunit;

namespace MacroApp.Tests.Core;

public class InputEventTests
{
    [Fact]
    public void Key_events_use_the_real_key_name()
    {
        // 0x41 is VK_A. Casting it straight to WPF's Key enum used to produce "V".
        new InputEvent { Type = InputEventType.KeyDown, VirtualKeyCode = 0x41 }.ToScriptLine()
            .Should().Be("KeyDown A");
    }

    [Fact]
    public void Delays_go_on_their_own_line()
    {
        new InputEvent { Type = InputEventType.MouseMove, X = 5, Y = 6, DelayFromPreviousMs = 120 }.ToScriptLine()
            .Should().Be("Delay 120\nMouseMove 5 6");
    }

    [Fact]
    public void Sub_millisecond_delays_are_dropped()
    {
        new InputEvent { Type = InputEventType.MouseMove, X = 5, Y = 6, DelayFromPreviousMs = 0.4 }.ToScriptLine()
            .Should().Be("MouseMove 5 6");
    }

    [Fact]
    public void Generated_script_for_a_recording_parses_cleanly()
    {
        var events = new[]
        {
            new InputEvent { Type = InputEventType.MouseMove, X = 100, Y = 200 },
            new InputEvent { Type = InputEventType.MouseDown, Button = MouseButton.Left, X = 100, Y = 200, DelayFromPreviousMs = 30 },
            new InputEvent { Type = InputEventType.MouseUp, Button = MouseButton.Left, X = 100, Y = 200, DelayFromPreviousMs = 80 },
            new InputEvent { Type = InputEventType.MouseScroll, ScrollDelta = -120, X = 100, Y = 200 },
            new InputEvent { Type = InputEventType.KeyDown, VirtualKeyCode = 0xA0 },
            new InputEvent { Type = InputEventType.KeyDown, VirtualKeyCode = 0x0D, DelayFromPreviousMs = 5 },
            new InputEvent { Type = InputEventType.KeyUp, VirtualKeyCode = 0x0D },
            new InputEvent { Type = InputEventType.KeyUp, VirtualKeyCode = 0xA0 },
        };
        string script = string.Join("\n", events.Select(e => e.ToScriptLine()));

        var parser = new ScriptParser();
        parser.Parse(script);

        parser.Errors.Should().BeEmpty();
    }
}
