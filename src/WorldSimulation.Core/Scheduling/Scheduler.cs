using WorldSimulation.Core.Time;

namespace WorldSimulation.Core.Scheduling;

/// <summary>
/// Minimal future-work queue (MOD_SCHED). Shares the single injected <see cref="WorldClock"/>;
/// never keeps its own time. Does not execute work or create world events.
/// </summary>
public sealed class Scheduler
{
    private readonly WorldClock _clock;
    private readonly PriorityQueue<ScheduledWork, (TimeSpan Due, long Seq)> _queue = new();
    private readonly Dictionary<long, ScheduledWork> _pending = new();
    private long _nextSeq;

    public Scheduler(WorldClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Number of valid (not taken, not cancelled) entries.</summary>
    public int Count => _pending.Count;

    /// <summary>Queues work due at <paramref name="dueTime"/> (must not be earlier than the clock's Now).</summary>
    public ScheduleHandle Schedule(TimeSpan dueTime, string workKey)
    {
        if (string.IsNullOrWhiteSpace(workKey))
        {
            throw new ArgumentException("Work key must not be empty.", nameof(workKey));
        }

        if (dueTime < _clock.Now)
        {
            throw new ArgumentOutOfRangeException(
                nameof(dueTime), dueTime, $"Cannot schedule before world time (Now = {_clock.Now}).");
        }

        var handle = new ScheduleHandle(_nextSeq++);
        var work = new ScheduledWork(handle, dueTime, workKey);
        _pending.Add(handle.Value, work);
        _queue.Enqueue(work, (dueTime, handle.Value));
        return handle;
    }

    /// <summary>Earliest valid entry without removing it or touching the clock; null when empty.</summary>
    public ScheduledWork? PeekNext()
    {
        DiscardCancelledHead();
        return _queue.TryPeek(out var work, out _) ? work : null;
    }

    /// <summary>Cancels a still-queued entry. Returns false if unknown, already taken or already cancelled.</summary>
    public bool Cancel(ScheduleHandle handle) => _pending.Remove(handle.Value);

    /// <summary>
    /// Advances the shared clock to the earliest DueTime and returns every valid entry due then.
    /// Order inside the batch is a technical tie-break only (insertion order), not a priority.
    /// Empty queue: returns an empty batch and leaves the clock untouched.
    /// </summary>
    public IReadOnlyList<ScheduledWork> TakeNextDueBatch()
    {
        DiscardCancelledHead();
        if (!_queue.TryPeek(out var first, out _))
        {
            return Array.Empty<ScheduledWork>();
        }

        // Advance first: if the clock rejects the time, nothing has been removed from the queue.
        _clock.AdvanceTo(first.DueTime);

        var batch = new List<ScheduledWork>();
        while (_queue.TryPeek(out var next, out _) && next.DueTime == first.DueTime)
        {
            _queue.Dequeue();
            if (_pending.Remove(next.Handle.Value))
            {
                batch.Add(next);
            }
        }

        return batch;
    }

    /// <summary>
    /// V0-04A atomic commit support: an independent copy bound to <paramref name="clock"/>
    /// (normally a <see cref="WorldClock.Clone"/>), with the same pending entries and the
    /// same next handle sequence. Mutating the copy never touches this scheduler.
    /// </summary>
    internal Scheduler CloneOnto(WorldClock clock)
    {
        var copy = new Scheduler(clock);
        copy._nextSeq = _nextSeq;
        foreach (var work in _pending.Values)
        {
            copy._pending.Add(work.Handle.Value, work);
            copy._queue.Enqueue(work, (work.DueTime, work.Handle.Value));
        }

        return copy;
    }

    /// <summary>Pending entries in due order (DueTime, then handle). Read-only view for state checks.</summary>
    internal IReadOnlyList<ScheduledWork> PendingInOrder()
        => _pending.Values.OrderBy(w => w.DueTime).ThenBy(w => w.Handle.Value).ToArray();

    /// <summary>The handle value the next <see cref="Schedule"/> call would use.</summary>
    internal long NextSequence => _nextSeq;

    private void DiscardCancelledHead()
    {
        while (_queue.TryPeek(out var head, out _) && !_pending.ContainsKey(head.Handle.Value))
        {
            _queue.Dequeue();
        }
    }
}
