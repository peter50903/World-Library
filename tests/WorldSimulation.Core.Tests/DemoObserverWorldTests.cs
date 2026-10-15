using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// C05（P1.0.04-a）：舊 V0-01 每日糧食線已退役（使用者 2026-10-14 裁決）；世界只剩多聚落每週結算。
/// 這裡驗證時鐘推進、亂數流用途（41A）與每週十條流計數器（44G SCHED_RNG_SCOPE、44J FAIL_REPLAY）。
/// </summary>
public sealed class DemoObserverWorldTests
{
    [Fact]
    public void FreshDemoStartsAtDayZeroWithoutAnyCommittedWeek()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42));
        var snapshot = world.Snapshot();

        Assert.Equal(TimeSpan.Zero, snapshot.WorldTime);
        Assert.Equal(0, snapshot.DaysElapsed);
        Assert.Empty(world.WeekReports());
        Assert.Null(world.LatestWeek);
    }

    [Fact]
    public void AdvanceProcessesActualScheduledDaysAndReportsTheSettledWeekOnDaySeven()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42));

        var day1 = world.AdvanceOneDay();
        Assert.Equal(1, day1.Day);
        Assert.Equal(TimeSpan.FromDays(1), day1.WorldTime);
        Assert.Null(day1.SettledWeek);

        DemoDayAdvance last = day1;
        for (var i = 2; i <= 7; i++) last = world.AdvanceOneDay();

        Assert.Equal(7, last.Day);
        Assert.Equal(7, world.Snapshot().DaysElapsed);
        Assert.NotNull(last.SettledWeek);
        Assert.Same(world.LatestWeek, last.SettledWeek);
        Assert.StartsWith("第 1 週結算（第 7 日）｜聚落人口 ", last.SettledWeek!.Summary());
    }

    [Fact]
    public void SameHistoryOrWorldSeedGivesTheSameStreamsButOnlyTheHistorySeedChangesHistoryStreams()
    {
        // 正例：同世界種子、兩個不同歷史種子 → 地圖、聚落、資源點、人口群體人數逐項相同（初始人口不再抽 POP）。
        var a = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.Sample, historySeed: 1);
        var b = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.Sample, historySeed: 999);

        Assert.Equal(a.Map.Cells.ToArray(), b.Map.Cells.ToArray());
        Assert.Equal(a.Sites.Settlements.ToArray(), b.Sites.Settlements.ToArray());
        Assert.Equal(a.Sites.ResourcePoints.ToArray(), b.Sites.ResourcePoints.ToArray());
        Assert.Equal(a.Sites.PopGroups.ToArray(), b.Sites.PopGroups.ToArray());

        var ra = a.RngState().ToDictionary(s => s.StreamId);
        var rb = b.RngState().ToDictionary(s => s.StreamId);
        foreach (var id in RngStreams.WorldStreams)
            Assert.Equal(ra[id], rb[id]);
        Assert.Contains(RngStreams.HistoryStreams, id => ra[id].State != rb[id].State);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(42UL)]
    [InlineData(777UL)]
    public void OpeningDrawsOnlyEcologyInitAndRuntimeDrawsNothing(ulong seed)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed));
        var opening = world.RngState();
        var counts = opening.ToDictionary(s => s.StreamId, s => s.DrawCounter);

        // ECOLOGY_INIT＝1（聚落數）＋聚落數（位置）＋聚落數（人數）；POP／RESOURCE 開局 0。
        Assert.Equal(1UL + 2UL * (ulong)world.Sites.Settlements.Count, counts[RngStreams.EcologyInit]);
        Assert.Equal(0UL, counts[RngStreams.Pop]);
        Assert.Equal(0UL, counts[RngStreams.Resource]);

        for (var i = 0; i < 365; i++) world.AdvanceOneDay();
        Assert.Equal(opening.ToArray(), world.RngState().ToArray());
    }

    [Fact]
    public void EveryWeekRecordsTenChainedRngCountersInStreamOrder()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(5), DemoWeeklyTempConfig.LivingForagingSample);
        var previous = world.RngState().ToDictionary(s => s.StreamId, s => s.DrawCounter);
        for (var i = 0; i < 7 * 20; i++) world.AdvanceOneDay();

        Assert.Equal(20, world.WeekReports().Count);
        foreach (var week in world.WeekReports())
        {
            Assert.Equal(RngStreams.All, week.RngCounters.Select(c => c.StreamId));
            foreach (var c in week.RngCounters)
            {
                Assert.Equal(previous[c.StreamId], c.Before);
                Assert.True(c.After >= c.Before);
                previous[c.StreamId] = c.After;
            }
        }

        var final = world.RngState().ToDictionary(s => s.StreamId, s => s.DrawCounter);
        Assert.Equal(final, previous);
    }

    [Fact]
    public void RepeatingSameSeedGivesIdenticalWeeklySummariesCountersAndState()
    {
        var first = new DemoObserverWorld(DemoScenario.Sample(123456));
        var second = new DemoObserverWorld(DemoScenario.Sample(123456));

        for (var i = 0; i < 70; i++)
        {
            var x = first.AdvanceOneDay();
            var y = second.AdvanceOneDay();
            Assert.Equal(x.Day, y.Day);
            Assert.Equal(x.SettledWeek?.Summary(), y.SettledWeek?.Summary());
        }

        Assert.Equal(first.Snapshot(), second.Snapshot());
        Assert.Equal(
            first.WeekReports().SelectMany(w => w.RngCounters),
            second.WeekReports().SelectMany(w => w.RngCounters));
        Assert.Equal(first.StateFingerprint(), second.StateFingerprint());
    }

    [Fact]
    public void ReadingStatusWeeksAndRngStateDoesNotAdvanceTheWorld()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(888));
        for (var i = 0; i < 9; i++) world.AdvanceOneDay();
        var before = world.StateFingerprint();

        for (var i = 0; i < 20; i++)
        {
            _ = world.Snapshot();
            _ = world.WeekReports();
            _ = world.RngState();
            _ = world.LatestWeek?.Summary();
        }

        Assert.Equal(before, world.StateFingerprint());
    }

    [Fact]
    public void WeekReportListIsReadOnlyAndDetachedFromLaterWeeks()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(8));
        for (var i = 0; i < 7; i++) world.AdvanceOneDay();
        var before = world.WeekReports();
        var list = Assert.IsAssignableFrom<IList<DemoWeekReport>>(before);
        Assert.Throws<NotSupportedException>(() => list.Clear());

        for (var i = 0; i < 7; i++) world.AdvanceOneDay();

        Assert.Single(before);
        Assert.Equal(2, world.WeekReports().Count);
    }

    [Fact]
    public void InvalidScenarioIsRejectedBeforeAnyWorldStateExists()
    {
        Assert.Throws<ArgumentNullException>(() => new DemoObserverWorld(null!));
        Assert.Throws<ArgumentNullException>(() => new DemoObserverWorld(DemoScenario.Sample(1), null!));
    }
}
