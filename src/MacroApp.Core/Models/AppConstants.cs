namespace MacroApp.Core.Models;

/// <summary>
/// Application-wide constants. No magic numbers anywhere else.
/// </summary>
public static class AppConstants
{
    // ── File Extensions ─────────────────────────────────────────────
    public const string MacroFileExtension = ".mcr";
    public const string MacroLibraryExtension = ".mcrlib";
    public const string CrashRecoveryExtension = ".mcr.recovery";
    public const string TempFilePrefix = "macro_temp_";

    // ── Timing ──────────────────────────────────────────────────────
    public const int HookCallbackMaxMs = 5;
    public const int PlaybackTimingToleranceMs = 10;
    public const int DefaultCountdownSeconds = 3;
    public const int DefaultInterRepeatDelayMs = 500;
    public const int MinDelayMs = 1;
    public const int MaxDelayMs = 3_600_000; // 1 hour
    public const int CrashRecoveryFlushIntervalMs = 5000;
    public const int MouseMoveThresholdPixels = 5;

    // ── Playback Speed ──────────────────────────────────────────────
    public static readonly double[] SpeedMultipliers = { 0.25, 0.5, 1.0, 2.0, 5.0, 10.0 };
    public const double DefaultSpeedMultiplier = 1.0;
    public const int DefaultRepeatCount = 1;
    public const int InfiniteRepeat = -1;

    // ── Memory / Performance ────────────────────────────────────────
    public const int MaxEventsInMemory = 100_000;
    public const int MaxUndoLevels = 50;
    public const int MaxMemoryMB = 150;

    // ── Default Hotkeys (Virtual Key Codes) ─────────────────────────
    public const int DefaultRecordKey = 0x78;    // F9
    public const int DefaultPlayKey = 0x79;      // F10
    public const int DefaultStopKey = 0x7A;      // F11
    public const int DefaultPauseKey = 0x7B;     // F12
    public const int DefaultEmergencyStopKey = 0x1B; // Escape

    // ── UI ──────────────────────────────────────────────────────────
    public const string AppName = "MacroApp";
    public const string AppVersion = "1.0.0";
    public const double RecordingBlinkIntervalMs = 500;
    public const string DefaultMacroStoragePath = "Macros";

    // ── Registry ────────────────────────────────────────────────────
    public const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    public const string StartupRegistryValueName = "MacroApp";

    // ── Serialization ───────────────────────────────────────────────
    public const string XmlRootElement = "Macro";
    public const string XmlNamespace = "http://macroapp.local/schema/v1";
}
