using MacroApp.NativeInterop;

namespace MacroApp.Core.Playback;

/// <summary>
/// Cancellable delay that's accurate to well under a millisecond.
/// </summary>
internal static class PreciseDelay
{
    /// <summary>
    /// Sleeps for most of the duration, then spin-waits the last couple of milliseconds,
    /// since Thread.Sleep alone is only accurate to the ~15 ms system timer tick.
    /// </summary>
    public static void Wait(double milliseconds, CancellationToken ct)
    {
        if (milliseconds <= 0) return;

        long targetTicks = HighResolutionTimer.MillisecondsToTicks(milliseconds);
        long startTicks = HighResolutionTimer.GetTimestamp();

        // Waiting on the token's handle (instead of Thread.Sleep) lets Stop interrupt long delays.
        int sleepMs = (int)(milliseconds - 2);
        if (sleepMs > 0 && ct.WaitHandle.WaitOne(sleepMs))
            ct.ThrowIfCancellationRequested();

        while (HighResolutionTimer.GetTimestamp() - startTicks < targetTicks)
        {
            ct.ThrowIfCancellationRequested();
            Thread.SpinWait(10);
        }
    }
}
