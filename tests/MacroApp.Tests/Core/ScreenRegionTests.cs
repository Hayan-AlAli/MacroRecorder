using FluentAssertions;
using MacroApp.Core.Vision;
using Xunit;

namespace MacroApp.Tests.Core;

public class ScreenRegionTests
{
    [Theory]
    [InlineData("10 20 300 40")]
    [InlineData("10,20,300,40")]
    [InlineData(" 10, 20  300 ,40 ")]
    public void Parses_space_or_comma_separated(string text)
    {
        ScreenRegion.TryParse(text, out var region).Should().BeTrue();
        region.Should().Be(new ScreenRegion(10, 20, 300, 40));
    }

    [Fact]
    public void Negative_positions_are_allowed_for_monitors_left_of_the_primary()
    {
        ScreenRegion.TryParse("-1920 0 100 50", out var region).Should().BeTrue();
        region.X.Should().Be(-1920);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1 2 3")]
    [InlineData("1 2 0 5")]
    [InlineData("a b c d")]
    public void Rejects_bad_regions(string text)
    {
        ScreenRegion.TryParse(text, out _).Should().BeFalse();
    }
}
