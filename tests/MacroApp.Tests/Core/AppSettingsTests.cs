using System.IO;
using FluentAssertions;
using MacroApp.Core.Settings;
using Xunit;

namespace MacroApp.Tests.Core;

public class AppSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"macroapp-settings-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void Defaults_are_valid()
    {
        new AppSettings().Validate().Should().BeEmpty();
    }

    [Fact]
    public void Duplicate_hotkeys_are_rejected()
    {
        var settings = new AppSettings { PlayHotkey = "F9" };

        settings.Validate().Should().ContainSingle().Which.Should().Contain("Record and Play");
    }

    [Fact]
    public void Emergency_key_cannot_be_a_hotkey()
    {
        new AppSettings { EmergencyStopKey = "F10" }.Validate().Should().ContainSingle();
    }

    [Fact]
    public void Bad_values_are_reported()
    {
        var settings = new AppSettings { RecordHotkey = "Ctrl+", EmergencyStopKey = "Nope", CountdownSeconds = -1 };

        settings.Validate().Should().HaveCount(3);
    }

    [Fact]
    public void Saves_and_loads()
    {
        var settings = new AppSettings
        {
            RecordHotkey = "Ctrl+Shift+R",
            EmergencyStopKey = "Pause",
            CountdownSeconds = 0,
            MouseMoveThreshold = 12,
            StartWithWindows = true,
        };

        AppSettingsStore.Save(settings, _path);
        var loaded = AppSettingsStore.Load(_path);

        loaded.Should().BeEquivalentTo(settings);
        loaded.EmergencyStopVirtualKey.Should().Be(0x13);
    }

    [Fact]
    public void Missing_or_broken_files_give_defaults()
    {
        AppSettingsStore.Load(_path).Should().BeEquivalentTo(new AppSettings());

        File.WriteAllText(_path, "{ not json");
        AppSettingsStore.Load(_path).Should().BeEquivalentTo(new AppSettings());

        File.WriteAllText(_path, """{ "PlayHotkey": "F9" }""");
        AppSettingsStore.Load(_path).Should().BeEquivalentTo(new AppSettings());
    }
}
