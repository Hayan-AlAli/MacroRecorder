using CommunityToolkit.Mvvm.ComponentModel;

namespace MacroApp.Core.Models;

/// <summary>
/// Playback configuration for a macro.
/// </summary>
public partial class MacroPlaybackSettings : ObservableObject
{
    /// <summary>Speed multiplier for playback (1.0 = normal speed).</summary>
    [ObservableProperty]
    private double _speedMultiplier = AppConstants.DefaultSpeedMultiplier;

    /// <summary>Number of times to repeat (-1 for infinite).</summary>
    [ObservableProperty]
    private int _repeatCount = AppConstants.DefaultRepeatCount;

    /// <summary>Delay in milliseconds between repeats.</summary>
    [ObservableProperty]
    private int _interRepeatDelayMs = AppConstants.DefaultInterRepeatDelayMs;

    /// <summary>Coordinate mode for mouse events.</summary>
    [ObservableProperty]
    private NativeInterop.CoordinateMode _coordinateMode = NativeInterop.CoordinateMode.Absolute;

    /// <summary>Virtual key code for emergency stop.</summary>
    [ObservableProperty]
    private int _emergencyStopKey = AppConstants.DefaultEmergencyStopKey;

    /// <summary>
    /// Creates a deep copy of these settings.
    /// </summary>
    public MacroPlaybackSettings Clone()
    {
        return new MacroPlaybackSettings
        {
            SpeedMultiplier = SpeedMultiplier,
            RepeatCount = RepeatCount,
            InterRepeatDelayMs = InterRepeatDelayMs,
            CoordinateMode = CoordinateMode,
            EmergencyStopKey = EmergencyStopKey
        };
    }
}

/// <summary>
/// Recording filter settings.
/// </summary>
public partial class RecordingSettings : ObservableObject
{
    /// <summary>When true, only keyboard events are captured.</summary>
    [ObservableProperty]
    private bool _keyboardOnly;

    /// <summary>When true, only mouse events are captured.</summary>
    [ObservableProperty]
    private bool _mouseOnly;

    /// <summary>Minimum pixel distance for mouse move events to be recorded.</summary>
    [ObservableProperty]
    private int _mouseMovementThreshold = AppConstants.MouseMoveThresholdPixels;

    /// <summary>Virtual key codes to exclude from recording.</summary>
    [ObservableProperty]
    private List<int> _excludedKeys = new();

    /// <summary>Countdown seconds before recording starts.</summary>
    [ObservableProperty]
    private int _countdownSeconds = AppConstants.DefaultCountdownSeconds;

    /// <summary>
    /// Creates a deep copy of these settings.
    /// </summary>
    public RecordingSettings Clone()
    {
        return new RecordingSettings
        {
            KeyboardOnly = KeyboardOnly,
            MouseOnly = MouseOnly,
            MouseMovementThreshold = MouseMovementThreshold,
            ExcludedKeys = new List<int>(ExcludedKeys),
            CountdownSeconds = CountdownSeconds
        };
    }
}
