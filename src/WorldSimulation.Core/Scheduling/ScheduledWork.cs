namespace WorldSimulation.Core.Scheduling;

/// <summary>Scheduler-local cancellation handle. Not an EntityID or EventID.</summary>
public readonly record struct ScheduleHandle(long Value);

/// <summary>
/// A future work entry (not a History Event). Carries only scheduling metadata:
/// the handle, the DueTime and an opaque work key.
/// </summary>
public readonly record struct ScheduledWork(ScheduleHandle Handle, TimeSpan DueTime, string WorkKey);
