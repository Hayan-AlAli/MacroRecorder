using FluentAssertions;
using MacroApp.NativeInterop;
using Xunit;

namespace MacroApp.Tests.NativeInterop;

public class HotKeyBindingTests
{
    [Theory]
    [InlineData("F9", 0u, 0x78u)]
    [InlineData("Ctrl+Shift+R", NativeConstants.MOD_CONTROL | NativeConstants.MOD_SHIFT, 0x52u)]
    [InlineData(" alt + f4 ", NativeConstants.MOD_ALT, 0x73u)]
    [InlineData("Win+D", NativeConstants.MOD_WIN, 0x44u)]
    public void Parses_key_combinations(string text, uint modifiers, uint key)
    {
        HotKeyBinding.TryParse(text, out var binding).Should().BeTrue();
        binding.Should().Be(new HotKeyBinding(modifiers, key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Shift")]
    [InlineData("Ctrl++A")]
    [InlineData("A+B")]
    [InlineData("Hyper+A")]
    public void Rejects_invalid_combinations(string text)
    {
        HotKeyBinding.TryParse(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("F9")]
    [InlineData("Ctrl+Alt+Shift+Win+F12")]
    [InlineData("Ctrl+OemComma")]
    public void ToString_round_trips(string text)
    {
        HotKeyBinding.TryParse(text, out var binding).Should().BeTrue();
        binding!.ToString().Should().Be(text);
    }
}
