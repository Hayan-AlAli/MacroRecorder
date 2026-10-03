using FluentAssertions;
using MacroApp.Core.Models;
using MacroApp.Core.Playback;
using MacroApp.NativeInterop;
using Xunit;

namespace MacroApp.Tests.Core;

public class ScriptPlaybackEngineTests
{
    // Scripts here only use variables, so nothing reaches SendInput and the tests run anywhere.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public void Breakpoint_pauses_before_the_line_and_step_continues()
    {
        using var engine = new ScriptPlaybackEngine(new InputSimulator());
        var lines = new List<int>();
        var paused = new ManualResetEventSlim();
        var done = new ManualResetEventSlim();

        engine.Progress += p =>
        {
            lock (lines) lines.Add(p.Line);
            if (p.State == PlaybackState.StepThrough) paused.Set();
        };
        engine.Completed += done.Set;

        engine.Start("SetVar a 1\nSetVar b 2\nSetVar c 3", new MacroPlaybackSettings { InterRepeatDelayMs = 0 },
            breakpoints: new[] { 2 });

        paused.Wait(Timeout).Should().BeTrue();
        engine.State.Should().Be(PlaybackState.StepThrough);
        lock (lines) lines.Should().Equal(1, 2);

        engine.Resume();
        done.Wait(Timeout).Should().BeTrue();
        lock (lines) lines.Should().Equal(1, 2, 3);
    }

    [Fact]
    public void Syntax_errors_stop_it_from_starting()
    {
        using var engine = new ScriptPlaybackEngine(new InputSimulator());

        var act = () => engine.Start("Repeat 3\nSetVar a 1", new MacroPlaybackSettings());

        act.Should().Throw<InvalidOperationException>().WithMessage("Line 1:*");
        engine.State.Should().Be(PlaybackState.Idle);
    }

    [Fact]
    public void Runtime_errors_are_reported_through_the_Error_event()
    {
        using var engine = new ScriptPlaybackEngine(new InputSimulator());
        Exception? error = null;
        var failed = new ManualResetEventSlim();
        engine.Error += ex => { error = ex; failed.Set(); };

        engine.Start("SetVar a 1\nWaitForImage \"x.png\" 10", new MacroPlaybackSettings());

        failed.Wait(Timeout).Should().BeTrue();
        error!.Message.Should().StartWith("Line 2:");
    }
}
