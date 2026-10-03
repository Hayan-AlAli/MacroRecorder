using FluentAssertions;
using MacroApp.Scripting.AST;
using MacroApp.Scripting.Parser;
using Xunit;

namespace MacroApp.Tests.Scripting;

public class ScriptParserTests
{
    private static (List<AstNode> Nodes, IReadOnlyList<ScriptError> Errors) Parse(string script)
    {
        var parser = new ScriptParser();
        var nodes = parser.Parse(script);
        return (nodes, parser.Errors);
    }

    [Fact]
    public void Variable_arguments_keep_their_braces()
    {
        var (nodes, errors) = Parse("MouseMove {x} {y}");

        errors.Should().BeEmpty();
        nodes.Should().ContainSingle().Which.Should().Be(new MouseMoveNode(1, "{x}", "{y}"));
    }

    [Fact]
    public void Conditions_keep_variable_references()
    {
        var (nodes, _) = Parse("If {count} >= 3\nStop\nEndIf");

        nodes.Should().ContainSingle().Which.Should().BeOfType<IfNode>()
            .Which.Condition.Should().Be("{count} >= 3");
    }

    [Fact]
    public void Both_label_forms_are_accepted()
    {
        var (nodes, errors) = Parse("Label first\nsecond:\nGoto first\nGoto second");

        errors.Should().BeEmpty();
        nodes.OfType<LabelNode>().Select(l => l.Name).Should().Equal("first", "second");
    }

    [Fact]
    public void Goto_to_an_undefined_label_is_an_error()
    {
        var (_, errors) = Parse("Goto nowhere");

        errors.Should().ContainSingle().Which.Message.Should().Contain("nowhere");
    }

    [Fact]
    public void Duplicate_labels_are_an_error()
    {
        var (_, errors) = Parse("top:\nDelay 1\ntop:");

        errors.Should().ContainSingle().Which.Line.Should().Be(3);
    }

    [Fact]
    public void Unclosed_block_reports_the_line_that_opened_it()
    {
        var (_, errors) = Parse("Delay 10\nRepeat 3\n  KeyPress A\n");

        errors.Should().ContainSingle().Which.Line.Should().Be(2);
    }

    [Fact]
    public void Stray_block_terminator_is_an_error()
    {
        var (_, errors) = Parse("KeyPress A\nEndIf");

        errors.Should().ContainSingle().Which.Line.Should().Be(2);
    }

    [Fact]
    public void Trailing_comments_are_ignored()
    {
        var (nodes, errors) = Parse("MouseClick Left 10 20 // the OK button");

        errors.Should().BeEmpty();
        nodes.Should().ContainSingle().Which.Should().Be(new MouseClickNode(1, "Left", "10", "20"));
    }

    [Fact]
    public void Unquoted_TypeText_takes_the_rest_of_the_line()
    {
        var (nodes, _) = Parse("TypeText hello there");

        nodes.Should().ContainSingle().Which.Should().Be(new TypeTextNode(1, "hello there"));
    }

    [Fact]
    public void Missing_arguments_are_reported()
    {
        var (_, errors) = Parse("MouseMove 10");

        errors.Should().ContainSingle().Which.Message.Should().Contain("argument 2");
    }

    [Fact]
    public void If_else_bodies_are_split_correctly()
    {
        var (nodes, errors) = Parse("If true\nKeyPress A\nElse\nKeyPress B\nKeyPress C\nEndIf");

        errors.Should().BeEmpty();
        var node = nodes.Should().ContainSingle().Which.Should().BeOfType<IfNode>().Subject;
        node.ThenBody.Should().HaveCount(1);
        node.ElseBody.Should().HaveCount(2);
    }

    [Fact]
    public void Columns_account_for_indentation()
    {
        var (_, errors) = Parse("    Bogus 1 2");

        errors.Should().ContainSingle().Which.Column.Should().Be(5);
    }
}
