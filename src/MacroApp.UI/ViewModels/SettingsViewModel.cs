using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MacroApp.Core.Models;

namespace MacroApp.UI.ViewModels;

/// <summary>
/// ViewModel for the settings dialog.
/// </summary>
public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _theme = "Dark";

    [ObservableProperty]
    private string _storagePath = AppConstants.DefaultMacroStoragePath;

    [ObservableProperty]
    private bool _startWithWindows;

    [ObservableProperty]
    private bool _minimizeToTray = true;

    [ObservableProperty]
    private double _defaultSpeed = AppConstants.DefaultSpeedMultiplier;

    [ObservableProperty]
    private int _defaultRepeatCount = AppConstants.DefaultRepeatCount;

    [ObservableProperty]
    private string _recordHotkey = "F9";

    [ObservableProperty]
    private string _playHotkey = "F10";

    [ObservableProperty]
    private string _stopHotkey = "F11";

    [ObservableProperty]
    private string _pauseHotkey = "F12";

    [ObservableProperty]
    private string _emergencyStopHotkey = "Escape";

    [RelayCommand]
    private void SaveSettings()
    {
        // TODO: Persist to JSON settings file
    }

    [RelayCommand]
    private void ResetToDefaults()
    {
        Theme = "Dark";
        StoragePath = AppConstants.DefaultMacroStoragePath;
        StartWithWindows = false;
        MinimizeToTray = true;
        DefaultSpeed = AppConstants.DefaultSpeedMultiplier;
        DefaultRepeatCount = AppConstants.DefaultRepeatCount;
        RecordHotkey = "F9";
        PlayHotkey = "F10";
        StopHotkey = "F11";
        PauseHotkey = "F12";
        EmergencyStopHotkey = "Escape";
    }
}
