using System.IO;
using FluentAssertions;
using MacroApp.Core.Models;
using MacroApp.Core.Serialization;
using MacroApp.NativeInterop;
using Xunit;

namespace MacroApp.Tests.Core;

public class MacroSerializerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "macroapp-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Round_trips_everything_that_gets_saved()
    {
        var original = new Macro
        {
            Name = "Login",
            Description = "Types the username",
            Category = "Work",
            HotKey = new HotKeyBinding(NativeConstants.MOD_CONTROL, 0x4C),
            ScriptText = "TypeText \"hi\" // <not xml>",
            Breakpoints = { 7, 2 },
            IsEnabled = false,
            Events =
            {
                new InputEvent { Type = InputEventType.KeyDown, VirtualKeyCode = 0x41, ScanCode = 30, DelayFromPreviousMs = 12.5 },
                new InputEvent { Type = InputEventType.MouseDown, Button = MouseButton.Right, X = -1200, Y = 40, HasBreakpoint = true },
                new InputEvent { Type = InputEventType.MouseScroll, X = 1, Y = 2, ScrollDelta = -120, Annotation = "scroll down" },
            }
        };
        original.PlaybackSettings.RepeatCount = 5;
        original.PlaybackSettings.SpeedMultiplier = 2.0;
        original.PlaybackSettings.CoordinateMode = CoordinateMode.WindowRelative;

        string path = Path.Combine(_dir, "login.mcr");
        await MacroSerializer.SaveAsync(original, path);
        var loaded = await MacroSerializer.LoadAsync(path);

        loaded.Id.Should().Be(original.Id);
        loaded.CreatedAt.Should().Be(original.CreatedAt);
        loaded.ModifiedAt.Should().Be(original.ModifiedAt);
        loaded.Name.Should().Be("Login");
        loaded.Description.Should().Be("Types the username");
        loaded.Category.Should().Be("Work");
        loaded.HotKey.Should().Be(original.HotKey);
        loaded.ScriptText.Should().Be(original.ScriptText);
        loaded.Breakpoints.Should().Equal(2, 7);
        loaded.IsEnabled.Should().BeFalse();
        loaded.Events.Should().Equal(original.Events);
        loaded.PlaybackSettings.RepeatCount.Should().Be(5);
        loaded.PlaybackSettings.SpeedMultiplier.Should().Be(2.0);
        loaded.PlaybackSettings.CoordinateMode.Should().Be(CoordinateMode.WindowRelative);
        loaded.IsDirty.Should().BeFalse();
        loaded.FilePath.Should().Be(path);
    }

    [Fact]
    public async Task Save_leaves_no_temp_files_behind()
    {
        await MacroSerializer.SaveAsync(new Macro(), Path.Combine(_dir, "a.mcr"));

        Directory.GetFiles(_dir).Should().ContainSingle();
    }

    [Fact]
    public async Task Files_without_an_id_still_load()
    {
        Directory.CreateDirectory(_dir);
        string path = Path.Combine(_dir, "old.mcr");
        await File.WriteAllTextAsync(path, """
            <Macro xmlns="http://macroapp.local/schema/v1"><Name>Old</Name></Macro>
            """);

        var loaded = await MacroSerializer.LoadAsync(path);

        loaded.Name.Should().Be("Old");
        loaded.Id.Should().NotBeEmpty();
    }
}
