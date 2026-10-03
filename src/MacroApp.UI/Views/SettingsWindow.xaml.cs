using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MacroApp.Core.Settings;
using MacroApp.NativeInterop;

namespace MacroApp.UI.Views;

/// <summary>
/// Edits a copy of <see cref="AppSettings"/>. On Save, <see cref="Result"/> holds the new settings.
/// </summary>
public partial class SettingsWindow : Window
{
    public AppSettings? Result { get; private set; }

    public SettingsWindow(AppSettings current)
    {
        InitializeComponent();
        Show(current);
    }

    private void Show(AppSettings settings)
    {
        RecordBox.Text = settings.RecordHotkey;
        PlayBox.Text = settings.PlayHotkey;
        StopBox.Text = settings.StopHotkey;
        EmergencyBox.Text = settings.EmergencyStopKey;
        CountdownBox.Text = settings.CountdownSeconds.ToString(CultureInfo.InvariantCulture);
        ThresholdBox.Text = settings.MouseMoveThreshold.ToString(CultureInfo.InvariantCulture);
        StartupBox.IsChecked = settings.StartWithWindows;
    }

    /// <summary>Turns the key press into text like "Ctrl+Shift+F9" instead of typing it.</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Plain Tab still moves between fields
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;

        e.Handled = true;
        var key = ActualKey(e);
        if (IsModifier(key)) return; // wait for the real key

        var parts = new List<string>();
        var mods = Keyboard.Modifiers;
        if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyNames.Format(KeyInterop.VirtualKeyFromKey(key)));

        ((TextBox)sender).Text = string.Join("+", parts);
    }

    private void SingleKeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Plain Tab still moves between fields
        if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.None) return;

        e.Handled = true;
        var key = ActualKey(e);
        if (IsModifier(key)) return;
        ((TextBox)sender).Text = KeyNames.Format(KeyInterop.VirtualKeyFromKey(key));
    }

    // Alt combinations arrive as Key.System, and F10 does too
    private static Key ActualKey(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    private static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private void Defaults_Click(object sender, RoutedEventArgs e) => Show(new AppSettings());

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            RecordHotkey = RecordBox.Text.Trim(),
            PlayHotkey = PlayBox.Text.Trim(),
            StopHotkey = StopBox.Text.Trim(),
            EmergencyStopKey = EmergencyBox.Text.Trim(),
            StartWithWindows = StartupBox.IsChecked == true,
        };

        var errors = new List<string>();
        if (int.TryParse(CountdownBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int countdown))
            settings.CountdownSeconds = countdown;
        else
            errors.Add("Countdown must be a whole number.");

        if (int.TryParse(ThresholdBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int threshold))
            settings.MouseMoveThreshold = threshold;
        else
            errors.Add("Mouse move threshold must be a whole number.");

        errors.AddRange(settings.Validate());

        if (errors.Count > 0)
        {
            ErrorText.Text = string.Join("\n", errors);
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        Result = settings;
        DialogResult = true;
    }
}
