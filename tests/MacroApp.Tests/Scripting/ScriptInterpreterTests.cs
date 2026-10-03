using System.Globalization;
using FluentAssertions;
using MacroApp.Scripting.Interpreter;
using MacroApp.Scripting.Parser;
using Xunit;

namespace MacroApp.Tests.Scripting;

public class ScriptInterpreterTests
{
    private static FakeExecutor Run(string script, FakeExecutor? executor = null)
    {
        executor ??= new FakeExecutor();
        var parser = new ScriptParser();
        var nodes = parser.Parse(script);
        parser.Errors.Should().BeEmpty();

        new ScriptInterpreter(executor).Execute(nodes);
        return executor;
    }

    [Fact]
    public void Runs_commands_in_order()
    {
        var calls = Run("KeyPress A\nDelay 50\nMouseClick Left 10 20").Calls;

        calls.Should().Equal("KeyPress A", "Delay 50", "MouseClick Left 10 20");
    }

    [Fact]
    public void Variables_are_substituted_into_arguments()
    {
        var calls = Run("SetVar x 100\nSetVar y 250\nMouseMove {x} {y}\nTypeText \"at {x},{y}\"").Calls;

        calls.Should().Equal("MouseMove 100 250", "TypeText at 100,250");
    }

    [Fact]
    public void Repeat_runs_its_body_the_given_number_of_times()
    {
        var calls = Run("Repeat 3\nKeyPress Space\nEndRepeat").Calls;

        calls.Should().HaveCount(3).And.OnlyContain(c => c == "KeyPress Space");
    }

    [Fact]
    public void While_loop_with_counter()
    {
        var calls = Run("SetVar i 0\nWhile {i} < 3\nIncVar i\nKeyPress {i}\nEndWhile").Calls;

        calls.Should().Equal("KeyPress 1", "KeyPress 2", "KeyPress 3");
    }

    [Theory]
    [InlineData("5", "big")]
    [InlineData("1", "small")]
    public void If_else_picks_a_branch(string value, string expected)
    {
        var calls = Run($"SetVar n {value}\nIf {{n}} > 2\nTypeText big\nElse\nTypeText small\nEndIf").Calls;

        calls.Should().Equal($"TypeText {expected}");
    }

    [Fact]
    public void String_comparison_is_case_insensitive()
    {
        var executor = new FakeExecutor { Clipboard = "Done" };
        var calls = Run("GetClipboard status\nIf {status} == done\nKeyPress Enter\nEndIf", executor).Calls;

        calls.Should().Equal("KeyPress Enter");
    }

    [Fact]
    public void Goto_jumps_backwards_and_out_of_loops()
    {
        const string script = """
            SetVar n 0
            top:
            IncVar n
            Repeat 10
              If {n} == 3
                Goto done
              EndIf
              Goto top
            EndRepeat
            done:
            TypeText "n={n}"
            """;

        Run(script).Calls.Should().Equal("TypeText n=3");
    }

    [Fact]
    public void Goto_into_a_nested_block_fails_with_the_line_number()
    {
        var parser = new ScriptParser();
        var nodes = parser.Parse("Goto inner\nRepeat 1\ninner:\nEndRepeat");

        var act = () => new ScriptInterpreter(new FakeExecutor()).Execute(nodes);

        act.Should().Throw<ScriptRuntimeException>().Which.Line.Should().Be(1);
    }

    [Fact]
    public void Stop_ends_the_script()
    {
        var executor = new FakeExecutor();
        var parser = new ScriptParser();
        var interpreter = new ScriptInterpreter(executor);

        interpreter.Execute(parser.Parse("KeyPress A\nStop\nKeyPress B"));

        executor.Calls.Should().Equal("KeyPress A");
        interpreter.WasStopped.Should().BeTrue();
    }

    [Fact]
    public void Executor_failures_are_reported_with_the_script_line()
    {
        var act = () => Run("KeyPress A\n\nClickImage \"ok.png\"");

        act.Should().Throw<ScriptRuntimeException>().Which.Line.Should().Be(3);
    }

    [Fact]
    public void Image_commands_use_what_is_on_screen()
    {
        var executor = new FakeExecutor();
        executor.VisibleImages.Add("ok.png");

        const string script = """
            WaitForImage "ok.png" 1000
            ClickImage "ok.png" Right
            IfImageExists "cancel.png"
              TypeText cancel
            Else
              TypeText no-cancel
            EndIf
            """;

        Run(script, executor).Calls.Should().Equal("ClickImage ok.png Right", "TypeText no-cancel");
    }

    [Fact]
    public void WaitForImage_timeout_stops_the_script()
    {
        var act = () => Run("WaitForImage \"missing.png\" 10\nTypeText hi");

        act.Should().Throw<ScriptRuntimeException>().WithMessage("*missing.png*");
    }

    [Fact]
    public void OCR_text_goes_into_a_variable()
    {
        var executor = new FakeExecutor { ScreenText = "Total: 42" };

        var calls = Run("OCRGetText total 10 20 300 40\nTypeText {total}\nIfTextOnScreen \"total:\"\nKeyPress Enter\nEndIf", executor).Calls;

        calls.Should().Equal("OCRGetText 10 20 300 40", "TypeText Total: 42", "KeyPress Enter");
    }

    [Fact]
    public void WaitForWindow_timeout_stops_the_script()
    {
        var act = () => Run("WaitForWindow \"Notepad\" 10\nTypeText hi");

        act.Should().Throw<ScriptRuntimeException>().WithMessage("*Notepad*");
    }

    [Fact]
    public void WaitForWindow_continues_when_the_window_exists()
    {
        var executor = new FakeExecutor();
        executor.Windows.Add("Notepad");

        Run("WaitForWindow \"Notepad\"\nTypeText hi", executor).Calls.Should().Equal("TypeText hi");
    }

    [Fact]
    public void Cancellation_stops_an_endless_loop()
    {
        using var cts = new CancellationTokenSource();
        var executor = new FakeExecutor();
        var parser = new ScriptParser();
        var nodes = parser.Parse("loop:\nKeyPress A\nGoto loop");
        var interpreter = new ScriptInterpreter(executor);
        interpreter.LineExecuting += _ => { if (executor.Calls.Count >= 5) cts.Cancel(); };

        var act = () => interpreter.Execute(nodes, cts.Token);

        act.Should().Throw<OperationCanceledException>();
        executor.Calls.Should().HaveCount(5);
    }

    [Fact]
    public void Numbers_use_a_dot_regardless_of_the_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("de-DE");
        try
        {
            var calls = Run("SetVar x 1.5\nIncVar x 1.5\nTypeText {x}\nIf {x} > 2.5\nKeyPress A\nEndIf").Calls;

            calls.Should().Equal("TypeText 3", "KeyPress A");
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void LineExecuting_skips_comments_and_labels()
    {
        var lines = new List<int>();
        var parser = new ScriptParser();
        var interpreter = new ScriptInterpreter(new FakeExecutor());
        interpreter.LineExecuting += lines.Add;

        interpreter.Execute(parser.Parse("// setup\nstart:\nKeyPress A\nKeyPress B"));

        lines.Should().Equal(3, 4);
    }
}
