using System.Globalization;

namespace MacroApp.NativeInterop;

/// <summary>
/// Converts between Win32 virtual-key codes and the key names used in scripts and the UI.
/// </summary>
/// <remarks>
/// Names follow WPF's <c>System.Windows.Input.Key</c> enum ("LeftCtrl", "Return", "OemComma")
/// so they look familiar, but the table lives here because that enum's values are not
/// virtual-key codes and casting between the two silently produces the wrong key.
/// </remarks>
public static class KeyNames
{
    private static readonly Dictionary<int, string> NamesByCode = BuildNameTable();
    private static readonly Dictionary<string, int> CodesByName = BuildLookup();

    /// <summary>
    /// Returns a readable name for a virtual-key code, e.g. 0x41 → "A", 0x0D → "Return".
    /// Unknown codes are formatted as hex ("0xE8") so they still round-trip through <see cref="TryParse"/>.
    /// </summary>
    public static string Format(int virtualKey) =>
        NamesByCode.TryGetValue(virtualKey, out var name) ? name : $"0x{virtualKey:X2}";

    /// <summary>
    /// Parses a key name ("A", "Enter", "LeftCtrl", "Ctrl", "F5", "7", "0x41") into a virtual-key code.
    /// Matching is case-insensitive.
    /// </summary>
    public static bool TryParse(string? name, out int virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(name)) return false;
        name = name.Trim();

        if (name.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(name.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out virtualKey)
                   && virtualKey is > 0 and < 0xFF;
        }

        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                virtualKey = c; // VK_A..VK_Z and VK_0..VK_9 match their ASCII codes
                return true;
            }
        }

        return CodesByName.TryGetValue(name, out virtualKey);
    }

    /// <summary>
    /// Keys that need KEYEVENTF_EXTENDEDKEY when sent through SendInput. Without the flag,
    /// apps that read scan codes see e.g. the numpad arrows instead of the dedicated arrow keys.
    /// </summary>
    public static bool IsExtendedKey(int virtualKey) => virtualKey switch
    {
        0x21 or 0x22 or 0x23 or 0x24 => true,           // PageUp, PageDown, End, Home
        >= 0x25 and <= 0x28 => true,                    // Arrow keys
        0x2C or 0x2D or 0x2E => true,                   // PrintScreen, Insert, Delete
        0x5B or 0x5C or 0x5D => true,                   // LWin, RWin, Apps
        0x6F => true,                                   // Numpad divide
        0x90 => true,                                   // NumLock
        0xA3 or 0xA5 => true,                           // Right Ctrl, Right Alt
        >= 0xA6 and <= 0xB7 => true,                    // Browser / media / volume keys
        _ => false
    };

    private static Dictionary<int, string> BuildNameTable()
    {
        var names = new Dictionary<int, string>
        {
            [0x03] = "Cancel",
            [0x08] = "Back",
            [0x09] = "Tab",
            [0x0C] = "Clear",
            [0x0D] = "Return",
            [0x10] = "Shift",
            [0x11] = "Ctrl",
            [0x12] = "Alt",
            [0x13] = "Pause",
            [0x14] = "CapsLock",
            [0x15] = "KanaMode",
            [0x17] = "JunjaMode",
            [0x18] = "FinalMode",
            [0x19] = "KanjiMode",
            [0x1B] = "Escape",
            [0x1C] = "ImeConvert",
            [0x1D] = "ImeNonConvert",
            [0x1E] = "ImeAccept",
            [0x1F] = "ImeModeChange",
            [0x20] = "Space",
            [0x21] = "PageUp",
            [0x22] = "PageDown",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x29] = "Select",
            [0x2A] = "Print",
            [0x2B] = "Execute",
            [0x2C] = "PrintScreen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
            [0x2F] = "Help",
            [0x5B] = "LWin",
            [0x5C] = "RWin",
            [0x5D] = "Apps",
            [0x5F] = "Sleep",
            [0x6A] = "Multiply",
            [0x6B] = "Add",
            [0x6C] = "Separator",
            [0x6D] = "Subtract",
            [0x6E] = "Decimal",
            [0x6F] = "Divide",
            [0x90] = "NumLock",
            [0x91] = "Scroll",
            [0xA0] = "LeftShift",
            [0xA1] = "RightShift",
            [0xA2] = "LeftCtrl",
            [0xA3] = "RightCtrl",
            [0xA4] = "LeftAlt",
            [0xA5] = "RightAlt",
            [0xA6] = "BrowserBack",
            [0xA7] = "BrowserForward",
            [0xA8] = "BrowserRefresh",
            [0xA9] = "BrowserStop",
            [0xAA] = "BrowserSearch",
            [0xAB] = "BrowserFavorites",
            [0xAC] = "BrowserHome",
            [0xAD] = "VolumeMute",
            [0xAE] = "VolumeDown",
            [0xAF] = "VolumeUp",
            [0xB0] = "MediaNextTrack",
            [0xB1] = "MediaPreviousTrack",
            [0xB2] = "MediaStop",
            [0xB3] = "MediaPlayPause",
            [0xB4] = "LaunchMail",
            [0xB5] = "SelectMedia",
            [0xB6] = "LaunchApplication1",
            [0xB7] = "LaunchApplication2",
            [0xBA] = "OemSemicolon",
            [0xBB] = "OemPlus",
            [0xBC] = "OemComma",
            [0xBD] = "OemMinus",
            [0xBE] = "OemPeriod",
            [0xBF] = "OemQuestion",
            [0xC0] = "OemTilde",
            [0xDB] = "OemOpenBrackets",
            [0xDC] = "OemPipe",
            [0xDD] = "OemCloseBrackets",
            [0xDE] = "OemQuotes",
            [0xDF] = "Oem8",
            [0xE2] = "OemBackslash",
            [0xE5] = "ImeProcessed",
            [0xF6] = "Attn",
            [0xF7] = "CrSel",
            [0xF8] = "ExSel",
            [0xF9] = "EraseEof",
            [0xFA] = "Play",
            [0xFB] = "Zoom",
            [0xFD] = "Pa1",
            [0xFE] = "OemClear",
        };

        for (int i = 0; i <= 9; i++)
        {
            names[0x30 + i] = $"D{i}";
            names[0x60 + i] = $"NumPad{i}";
        }

        for (char c = 'A'; c <= 'Z'; c++)
            names[c] = c.ToString();

        for (int i = 1; i <= 24; i++)
            names[0x6F + i] = $"F{i}";

        return names;
    }

    private static Dictionary<string, int> BuildLookup()
    {
        var lookup = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (code, name) in NamesByCode)
            lookup[name] = code;

        // Other spellings people reach for (several are WPF Key enum synonyms)
        var aliases = new Dictionary<string, int>
        {
            ["Enter"] = 0x0D,
            ["Backspace"] = 0x08,
            ["Esc"] = 0x1B,
            ["Control"] = 0x11,
            ["Del"] = 0x2E,
            ["Ins"] = 0x2D,
            ["PgUp"] = 0x21,
            ["Prior"] = 0x21,
            ["PgDn"] = 0x22,
            ["Next"] = 0x22,
            ["Capital"] = 0x14,
            ["Snapshot"] = 0x2C,
            ["ScrollLock"] = 0x91,
            ["Win"] = 0x5B,
            ["Windows"] = 0x5B,
            ["LeftWin"] = 0x5B,
            ["RightWin"] = 0x5C,
            ["LShift"] = 0xA0,
            ["RShift"] = 0xA1,
            ["LCtrl"] = 0xA2,
            ["RCtrl"] = 0xA3,
            ["LAlt"] = 0xA4,
            ["RAlt"] = 0xA5,
            ["HangulMode"] = 0x15,
            ["Oem1"] = 0xBA,
            ["Oem2"] = 0xBF,
            ["Oem3"] = 0xC0,
            ["Oem4"] = 0xDB,
            ["Oem5"] = 0xDC,
            ["Oem6"] = 0xDD,
            ["Oem7"] = 0xDE,
            ["Oem102"] = 0xE2,
        };
        foreach (var (name, code) in aliases)
            lookup[name] = code;

        return lookup;
    }
}
