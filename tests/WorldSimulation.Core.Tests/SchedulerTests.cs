using WorldSimulation.Core.Scheduling;
using WorldSimulation.Core.Time;
using Xunit;

namespace WorldSimulation.Core.Tests;

public class SchedulerTests
{
    private static TimeSpan S(double seconds) => TimeSpan.FromSeconds(seconds);

    private static (WorldClock Clock, Scheduler Scheduler) Create()
    {
        var clock = new WorldClock();
        return (clock, new Scheduler(clock));
    }

    [Fact]
    public void PeekNext_EmptyIsNull_AndDoesNotChangeTimeOrQueue()
    {
        var (clock, scheduler) = Create();
        Assert.Null(scheduler.PeekNext());

        scheduler.Schedule(S(5), "a");
        Assert.Equal("a", scheduler.PeekNext()!.Value.WorkKey);
        Assert.Equal("a", scheduler.PeekNext()!.Value.WorkKey);
        Assert.Equal(TimeSpan.Zero, clock.Now);
        Assert.Equal(1, scheduler.Count);
    }

    [Fact]
    public void TakeNextDueBatch_ReturnsEarliestFirst_AndAdvancesClock()
    {
        var (clock, scheduler) = Create();
        scheduler.Schedule(S(30), "late");
        scheduler.Schedule(S(10), "early");

        var first = scheduler.TakeNextDueBatch();
        Assert.Equal(new[] { "early" }, first.Select(w => w.WorkKey));
        Assert.Equal(S(10), clock.Now);

        var second = scheduler.TakeNextDueBatch();
        Assert.Equal(new[] { "late" }, second.Select(w => w.WorkKey));
        Assert.Equal(S(30), clock.Now);
    }

    [Fact]
    public void TakeNextDueBatch_TakesAllEntriesDueAtSameTime()
    {
        var (clock, scheduler) = Create();
        for (var i = 0; i < 50; i++)
        {
            scheduler.Schedule(S(7), "same");
        }
        scheduler.Schedule(S(8), "later");

        var batch = scheduler.TakeNextDueBatch();
        Assert.Equal(50, batch.Count);
        Assert.All(batch, w => Assert.Equal(S(7), w.DueTime));
        Assert.Equal(S(7), clock.Now);
        Assert.Equal(1, scheduler.Count);
    }

    [Fact]
    public void Cancel_RemovesEarliestAndMiddleEntries()
    {
        var (_, scheduler) = Create();
        var earliest = scheduler.Schedule(S(1), "a");
        var middle = scheduler.Schedule(S(2), "b");
        scheduler.Schedule(S(3), "c");

        Assert.True(scheduler.Cancel(earliest));
        Assert.True(scheduler.Cancel(middle));
        Assert.False(scheduler.Cancel(middle));
        Assert.Equal("c", scheduler.PeekNext()!.Value.WorkKey);
        Assert.Equal(new[] { "c" }, scheduler.TakeNextDueBatch().Select(w => w.WorkKey));
    }

    [Fact]
    public void Cancel_DoesNotMoveClock_AndTakenEntryCannotBeCancelled()
    {
        var (clock, scheduler) = Create();
        var handle = scheduler.Schedule(S(4), "a");
        var other = scheduler.Schedule(S(9), "b");

        scheduler.Cancel(other);
        Assert.Equal(TimeSpan.Zero, clock.Now);

        scheduler.TakeNextDueBatch();
        Assert.False(scheduler.Cancel(handle));
    }

    [Fact]
    public void Schedule_InvalidInputs_ThrowAndDoNotPolluteQueue()
    {
        var (clock, scheduler) = Create();
        clock.AdvanceTo(S(10));

        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Schedule(S(9), "past"));
        Assert.Throws<ArgumentOutOfRangeException>(() => scheduler.Schedule(S(-1), "negative"));
        Assert.Throws<ArgumentException>(() => scheduler.Schedule(S(11), ""));
        Assert.Throws<ArgumentException>(() => scheduler.Schedule(S(11), "  "));
        Assert.Throws<ArgumentException>(() => scheduler.Schedule(S(11), null!));

        Assert.Equal(0, scheduler.Count);
        Assert.Null(scheduler.PeekNext());
        Assert.Empty(scheduler.TakeNextDueBatch());
    }

    [Fact]
    public void TakeNextDueBatch_EmptyQueue_DoesNotAdvanceClock()
    {
        var (clock, scheduler) = Create();
        clock.AdvanceTo(S(3));
        Assert.Empty(scheduler.TakeNextDueBatch());
        Assert.Equal(S(3), clock.Now);
    }

    [Fact]
    public void Schedule_AtCurrentNow_IsTakenInNextBatchWithoutMovingClock()
    {
        var (clock, scheduler) = Create();
        scheduler.Schedule(S(5), "a");
        scheduler.TakeNextDueBatch();

        scheduler.Schedule(S(5), "again");
        var batch = scheduler.TakeNextDueBatch();
        Assert.Equal(new[] { "again" }, batch.Select(w => w.WorkKey));
        Assert.Equal(S(5), clock.Now);
    }

    [Fact]
    public void SameInputs_ProduceSameOutputs()
    {
        static List<string> Run()
        {
            var clock = new WorldClock();
            var scheduler = new Scheduler(clock);
            foreach (var (t, key) in new[] { (3.0, "c"), (1.0, "a"), (3.0, "d"), (1.0, "b"), (2.0, "x") })
            {
                scheduler.Schedule(TimeSpan.FromSeconds(t), key);
            }

            var output = new List<string>();
            while (scheduler.PeekNext() is not null)
            {
                output.AddRange(scheduler.TakeNextDueBatch().Select(w => $"{w.DueTime.TotalSeconds}:{w.WorkKey}"));
            }
            return output;
        }

        var first = Run();
        Assert.Equal(first, Run());
        Assert.Equal(new[] { "1:a", "1:b", "2:x", "3:c", "3:d" }, first);
    }
}
