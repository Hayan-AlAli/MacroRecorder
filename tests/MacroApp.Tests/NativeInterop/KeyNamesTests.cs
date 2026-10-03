using FluentAssertions;
using MacroApp.NativeInterop;
using Xunit;

namespace MacroApp.Tests.NativeInterop;

public class KeyNamesTests
{
    [Theory]
    [InlineData(0x41, "A")]
    [InlineData(0x31, "D1")]
    [InlineData(0x70, "F1")]
    [InlineData(0xA2, "LeftCtrl")]
    [InlineData(0x1B, "Escape")]
    public void Formats_virtual_keys_by_name(int vk, string expected)
    {
        KeyNames.Format(vk).Should().Be(expected);
    }

    [Theory]
    [InlineData("A", 0x41)]
    [InlineData("a", 0x41)]
    [InlineData("7", 0x37)]
    [InlineData("D7", 0x37)]
    [InlineData("Enter", 0x0D)]
    [InlineData("Return", 0x0D)]
    [InlineData("Ctrl", 0x11)]
    [InlineData("LeftCtrl", 0xA2)]
    [InlineData("LeftShift", 0xA0)]
    [InlineData("esc", 0x1B)]
    [InlineData("F12", 0x7B)]
    [InlineData("0x41", 0x41)]
    public void Parses_key_names(string name, int expected)
    {
        KeyNames.TryParse(name, out int vk).Should().BeTrue();
        vk.Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("NotAKey")]
    [InlineData("12")]
    [InlineData("A, B")]
    [InlineData("0xZZ")]
    public void Rejects_things_that_are_not_keys(string name)
    {
        KeyNames.TryParse(name, out _).Should().BeFalse();
    }

    [Fact]
    public void Every_formatted_name_parses_back_to_the_same_key()
    {
        for (int vk = 1; vk < 0xFF; vk++)
        {
            KeyNames.TryParse(KeyNames.Format(vk), out int parsed).Should().BeTrue($"0x{vk:X2} should round-trip");
            KeyNames.Format(parsed).Should().Be(KeyNames.Format(vk));
        }
    }

    [Fact]
    public void Arrow_keys_are_extended_and_letters_are_not()
    {
        KeyNames.IsExtendedKey(0x25).Should().BeTrue();
        KeyNames.IsExtendedKey(0x41).Should().BeFalse();
    }
}
