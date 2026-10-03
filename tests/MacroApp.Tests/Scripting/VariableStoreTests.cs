using FluentAssertions;
using MacroApp.Scripting.Interpreter;
using Xunit;

namespace MacroApp.Tests.Scripting;

public class VariableStoreTests
{
    [Fact]
    public void Undefined_variables_are_left_as_written()
    {
        new VariableStore().Interpolate("hi {name}").Should().Be("hi {name}");
    }

    [Fact]
    public void Names_are_case_insensitive()
    {
        var vars = new VariableStore();
        vars.Set("Name", "Ada");

        vars.Interpolate("{NAME}").Should().Be("Ada");
    }

    [Theory]
    [InlineData("42", 42)]
    [InlineData("-7", -7)]
    [InlineData("2.6", 3)]
    [InlineData("abc", 0)]
    public void ResolveInt_parses_or_falls_back_to_zero(string value, int expected)
    {
        new VariableStore().ResolveInt(value).Should().Be(expected);
    }

    [Fact]
    public void Increment_defaults_to_one()
    {
        var vars = new VariableStore();
        vars.Increment("i", "");
        vars.Increment("i", "");

        vars.Get("i").Should().Be("2");
    }
}
