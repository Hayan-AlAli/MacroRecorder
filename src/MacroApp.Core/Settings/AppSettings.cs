using System.IO;
using System.Text.Json;
using MacroApp.NativeInterop;

namespace MacroApp.Core.Settings;

/// <summary>
/// User preferences, saved as settings.json next to the Macros folder.
/// </summary>
public sealed class AppSettings
{
    public string RecordHotkey { get; set; } = "F9";
    public string PlayHotkey { get; set; } = "F10";
    public string StopHotkey { get; set; } = "F11";

    /// <summary>A single key (no modifiers) that stops playback from anywhere.</summary>
    public string EmergencyStopKey { get; set; } = "Escape";

    public int CountdownSeconds { get; set; } = Models.AppConstants.DefaultCountdownSeconds;

    /// <summary>Mouse moves shorter than this many pixels aren't recorded.</summary>
    public int MouseMoveThreshold { get; set; } = Models.AppConstants.MouseMoveThresholdPixels;

    public bool StartWithWindows { get; set; }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    /// <summary>
    /// Returns what's wrong with these settings, or an empty list if they can be applied.
    /// </summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        var bindings = new List<(string Name, HotKeyBinding Binding)>();

        foreach (var (name, text) in new[] { ("Record", RecordHotkey), ("Play", PlayHotkey), ("Stop", StopHotkey) })
        {
            if (HotKeyBinding.TryParse(text, out var binding))
                bindings.Add((name, binding!));
            else
                errors.Add($"{name} hotkey '{text}' isn't a valid key combination.");
        }

        foreach (var group in bindings.GroupBy(b => b.Binding).Where(g => g.Count() > 1))
            errors.Add($"{string.Join(" and ", group.Select(g => g.Name))} use the same hotkey ({group.Key}).");

        if (!KeyNames.TryParse(EmergencyStopKey, out int stopVk))
            errors.Add($"Emergency stop key '{EmergencyStopKey}' isn't a key name.");
        else if (bindings.Any(b => b.Binding.Modifiers == 0 && b.Binding.VirtualKey == stopVk))
            errors.Add("The emergency stop key can't also be one of the hotkeys.");

        if (CountdownSeconds is < 0 or > 60)
            errors.Add("Countdown must be between 0 and 60 seconds.");

        if (MouseMoveThreshold is < 0 or > 500)
            errors.Add("Mouse move threshold must be between 0 and 500 pixels.");

        return errors;
    }

    public HotKeyBinding RecordBinding => Parse(RecordHotkey);
    public HotKeyBinding PlayBinding => Parse(PlayHotkey);
    public HotKeyBinding StopBinding => Parse(StopHotkey);
    public int EmergencyStopVirtualKey => KeyNames.TryParse(EmergencyStopKey, out int vk) ? vk : Models.AppConstants.DefaultEmergencyStopKey;

    private static HotKeyBinding Parse(string text) =>
        HotKeyBinding.TryParse(text, out var binding)
            ? binding!
            : throw new InvalidOperationException($"'{text}' isn't a valid hotkey; call Validate() first.");
}

/// <summary>
/// Reads and writes <see cref="AppSettings"/> as JSON.
/// </summary>
public static class AppSettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    /// <summary>
    /// Loads settings from <paramref name="path"/>. A missing, unreadable or invalid file gives the defaults,
    /// so a hand-edited typo never stops the app from starting.
    /// </summary>
    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Options);
                if (settings != null && settings.Validate().Count == 0)
                    return settings;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine($"Ignoring unreadable settings file {path}: {ex.Message}");
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        File.Move(temp, path, overwrite: true);
    }
}
