using CommunityToolkit.Mvvm.ComponentModel;
using MacroApp.NativeInterop;

namespace MacroApp.Core.Models;

/// <summary>
/// Represents a single macro with its events, script, settings, and metadata.
/// </summary>
public partial class Macro : ObservableObject
{
    /// <summary>Unique identifier for this macro.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>User-assigned name.</summary>
    [ObservableProperty]
    private string _name = "Untitled Macro";

    /// <summary>Optional description.</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>Category/folder for organization.</summary>
    [ObservableProperty]
    private string _category = "Default";

    /// <summary>Assigned global hotkey binding.</summary>
    [ObservableProperty]
    private HotKeyBinding? _hotKey;

    /// <summary>List of recorded input events.</summary>
    [ObservableProperty]
    private List<InputEvent> _events = new();

    /// <summary>Raw script text (synced bidirectionally with Events).</summary>
    [ObservableProperty]
    private string _scriptText = string.Empty;

    /// <summary>Script lines (1-based) where playback pauses in step mode.</summary>
    [ObservableProperty]
    private List<int> _breakpoints = new();

    /// <summary>Playback configuration.</summary>
    [ObservableProperty]
    private MacroPlaybackSettings _playbackSettings = new();

    /// <summary>When this macro was created.</summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>When this macro was last modified.</summary>
    [ObservableProperty]
    private DateTime _modifiedAt = DateTime.UtcNow;

    /// <summary>Whether this macro is enabled (can be triggered by hotkey).</summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <summary>File path where this macro is saved.</summary>
    [ObservableProperty]
    private string? _filePath;

    /// <summary>Whether this macro has unsaved changes.</summary>
    [ObservableProperty]
    private bool _isDirty;

    /// <summary>
    /// Marks the macro as modified and updates the timestamp.
    /// </summary>
    public void MarkModified()
    {
        ModifiedAt = DateTime.UtcNow;
        IsDirty = true;
    }

    /// <summary>
    /// Creates a deep copy of this macro with a new <see cref="Id"/>.
    /// </summary>
    public Macro Clone(bool keepName = false)
    {
        return new Macro
        {
            Name = keepName ? Name : Name + " (Copy)",
            Description = Description,
            Category = Category,
            HotKey = HotKey,
            Events = new List<InputEvent>(Events),
            ScriptText = ScriptText,
            Breakpoints = new List<int>(Breakpoints),
            PlaybackSettings = PlaybackSettings.Clone(),
            IsEnabled = IsEnabled
        };
    }
}
