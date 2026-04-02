using System.Runtime.InteropServices;

namespace MacroApp.NativeInterop;

/// <summary>
/// High-resolution timer using QueryPerformanceCounter for microsecond-accurate timestamps.
/// </summary>
public sealed class HighResolutionTimer
{
    private static readonly long Frequency;
    private static readonly double TicksPerMicrosecond;

    static HighResolutionTimer()
    {
        NativeMethods.QueryPerformanceFrequency(out Frequency);
        TicksPerMicrosecond = Frequency / 1_000_000.0;
    }

    /// <summary>
    /// Gets the current high-resolution timestamp in ticks.
    /// </summary>
    public static long GetTimestamp()
    {
        NativeMethods.QueryPerformanceCounter(out long count);
        return count;
    }

    /// <summary>
    /// Gets the timer frequency (ticks per second).
    /// </summary>
    public static long GetFrequency() => Frequency;

    /// <summary>
    /// Converts a tick count to microseconds.
    /// </summary>
    public static double TicksToMicroseconds(long ticks) => ticks / TicksPerMicrosecond;

    /// <summary>
    /// Converts a tick count to milliseconds.
    /// </summary>
    public static double TicksToMilliseconds(long ticks) => ticks / (TicksPerMicrosecond * 1000.0);

    /// <summary>
    /// Calculates elapsed milliseconds between two timestamps.
    /// </summary>
    public static double ElapsedMilliseconds(long startTicks, long endTicks)
        => TicksToMilliseconds(endTicks - startTicks);

    /// <summary>
    /// Converts milliseconds to ticks.
    /// </summary>
    public static long MillisecondsToTicks(double ms) => (long)(ms * TicksPerMicrosecond * 1000.0);
}
