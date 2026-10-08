namespace WorldSimulation.Core.Time;

/// <summary>
/// The single world timeline (MOD_CLOCK). Holds the only WorldTime, expressed as
/// elapsed time since the world origin, and only ever moves forward (INV_TIME_ORDER).
/// Advanced by the future Scheduler via <see cref="AdvanceTo"/>; never by wall clock or frame delta.
/// </summary>
public sealed class WorldClock
{
    /// <summary>Current world time since the world origin. Starts at <see cref="TimeSpan.Zero"/>.</summary>
    public TimeSpan Now { get; private set; } = TimeSpan.Zero;

    /// <summary>
    /// Moves world time to <paramref name="nextDue"/>. Equal time is a no-op (several events may share a DueTime).
    /// A time before <see cref="Now"/> (including any negative time) is rejected and <see cref="Now"/> is left unchanged.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="nextDue"/> is earlier than <see cref="Now"/>.</exception>
    internal void AdvanceTo(TimeSpan nextDue)
    {
        if (nextDue < Now)
        {
            throw new ArgumentOutOfRangeException(
                nameof(nextDue), nextDue, $"World time cannot move backwards (Now = {Now}).");
        }

        Now = nextDue;
    }
}
