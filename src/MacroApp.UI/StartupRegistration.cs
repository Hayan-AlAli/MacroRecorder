using Microsoft.Win32;
using MacroApp.Core.Models;

namespace MacroApp.UI;

/// <summary>
/// Adds or removes the app from HKCU\...\Run so it starts when the user signs in.
/// </summary>
internal static class StartupRegistration
{
    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(AppConstants.StartupRegistryKey, writable: true)
                        ?? Registry.CurrentUser.CreateSubKey(AppConstants.StartupRegistryKey);

        if (enabled && Environment.ProcessPath is { } exe)
            key.SetValue(AppConstants.StartupRegistryValueName, $"\"{exe}\"");
        else if (key.GetValue(AppConstants.StartupRegistryValueName) != null)
            key.DeleteValue(AppConstants.StartupRegistryValueName);
    }
}
