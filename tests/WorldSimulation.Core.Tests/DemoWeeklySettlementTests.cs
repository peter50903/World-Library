using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// V0-04A：每週資源再生 → 勞動與生產 → 消耗（TEMP），資源帳與人口帳守恆，
/// 以及單一原子提交（中途失敗時 Entity、RNG、Scheduler、History、Ledger 全部不變）。
/// </summary>
public sealed class DemoWeeklySettlementTests
{
    private sealed class InjectedFault : Exception
    {
        public InjectedFault(string message) : base(message)
        {
        }
    }

    private static DemoObserverWorld AdvancedWorld(ulong seed, int days)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed));
        for (var i = 0; i < days; i++) world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void NoWeeklyLedgerBeforeDaySevenAndExactlyOneOnDaySeven()
    {
        var world = AdvancedWorld(42, 6);
        Assert.Null(world.LatestWeek);
        Assert.Empty(world.WeekReports());

        world.AdvanceOneDay();

        var week = Assert.IsType<DemoWeekReport>(world.LatestWeek);
        Assert.Equal(1, week.Week);
        Assert.Equal(TimeSpan.FromDays(7), week.WorldTime);
        Assert.Equal(world.Sites.Settlements.Count, week.Settlements.Count);
        Assert.Single(world.WeekReports());
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(7UL)]
    [InlineData(20261009UL)]
    public void FiftyTwoWeeksEverySettlementLedgerBalancesAndChainsWeekToWeek(ulong seed)
    {
        var world = AdvancedWorld(seed, 52 * 7);
        var weeks = world.WeekReports();
        Assert.Equal(52, weeks.Count);

        var cfg = DemoWeeklyTempConfig.Sample;
        DemoWeekReport? previous = null;
        foreach (var week in weeks)
        {
            Assert.Equal(TimeSpan.FromDays(7 * week.Week), week.WorldTime);
            foreach (var l in week.Settlements)
            {
                // 資源帳、糧食帳、人口帳：期初＋流入－流出＝期末。
                Assert.Equal(l.ReserveClosing, l.ReserveOpening + l.Regenerated - l.Harvested);
                Assert.Equal(l.FoodClosing, l.FoodOpening + l.FoodProduced + l.FoodCarriedIn - l.FoodConsumed - l.FoodCarriedOut);
                Assert.Equal(l.PopulationClosing, l.PopulationOpening + l.Births - l.Deaths + l.Immigrants - l.Emigrants);
                // M1_NO_MAGIC_FOOD：生產只來自採收，採收不超過再生後的存量。
                Assert.Equal(l.Harvested, l.FoodProduced);
                Assert.True(l.Harvested <= l.ReserveOpening + l.Regenerated);
                // M1_RES：總資源只因再生增加、只因消耗減少。
                Assert.Equal(l.ReserveClosing + l.FoodClosing,
                    l.ReserveOpening + l.FoodOpening + l.Regenerated + l.FoodCarriedIn - l.FoodConsumed - l.FoodCarriedOut);
                // 消耗與缺糧合計等於需求；存量不超過上限。
                Assert.Equal(l.PopulationOpening * cfg.FoodPerPersonPerWeek, l.FoodConsumed + l.FoodShortage);
                Assert.True(l.ReserveClosing <= cfg.ReserveCapPerPoint * world.Sites.ResourcePointsOf(l.SettlementId).Count);
                Assert.True(l.IsBalanced);

                if (previous is not null)
                {
                    var before = previous.Settlements.Single(p => p.SettlementId == l.SettlementId);
                    Assert.Equal(before.ReserveClosing, l.ReserveOpening);
                    Assert.Equal(before.FoodClosing, l.FoodOpening);
                    Assert.Equal(before.PopulationClosing, l.PopulationOpening);
                }
            }

            previous = week;
        }

        // 最新一週期末等於世界目前狀態。
        foreach (var l in weeks[weeks.Count - 1].Settlements)
        {
            Assert.Equal(l.FoodClosing, world.SettlementFood(l.SettlementId));
            Assert.Equal(l.PopulationClosing, world.SettlementPopulation(l.SettlementId));
            Assert.Equal(l.ReserveClosing,
                world.Sites.ResourcePointsOf(l.SettlementId).Sum(p => world.ResourceReserve(p.Id)));
        }
    }

    [Fact]
    public void FirstWeekMatchesHandComputedTempArithmetic()
    {
        var world = AdvancedWorld(42, 7);
        var cfg = DemoWeeklyTempConfig.Sample;

        foreach (var l in world.LatestWeek!.Settlements)
        {
            var people = world.Sites.PopGroupsOf(l.SettlementId).Sum(p => (long)p.Size);
            var kind = world.Sites.ResourcePointsOf(l.SettlementId).Single().Kind;
            // 第 08 章 a 節：未開墾草原沒有存量、不再生、不產糧。
            var uncultivated = kind == DemoResourceKind.Uncultivated;
            var regenRate = kind switch
            {
                DemoResourceKind.Woodland => cfg.WoodlandRegenPerWeek,
                DemoResourceKind.Fishery => cfg.FisheryRegenPerWeek,
                _ => 0
            };
            var reserveOpening = uncultivated ? 0 : cfg.InitialReservePerPoint;
            var regen = Math.Min(regenRate, cfg.ReserveCapPerPoint - reserveOpening);
            var harvest = Math.Min(people * cfg.YieldPerWorkerPerWeek, reserveOpening + regen);
            var foodOpening = people * cfg.InitialFoodPerPerson;
            var consumed = Math.Min(foodOpening + harvest, people * cfg.FoodPerPersonPerWeek);

            Assert.Equal(reserveOpening, l.ReserveOpening);
            Assert.Equal(regen, l.Regenerated);
            Assert.Equal(harvest, l.Harvested);
            Assert.Equal(foodOpening, l.FoodOpening);
            Assert.Equal(consumed, l.FoodConsumed);
            Assert.Equal(people, l.PopulationOpening);
            Assert.Equal(0, l.Births);
            Assert.Equal(0, l.Deaths);
        }
    }

    [Fact]
    public void SameSeedGivesIdenticalLedgersAndWholeWorldState()
    {
        var a = AdvancedWorld(123456, 70);
        var b = AdvancedWorld(123456, 70);

        Assert.Equal(a.StateFingerprint(), b.StateFingerprint());
        Assert.Equal(a.WeekReports().Count, b.WeekReports().Count);
        for (var i = 0; i < a.WeekReports().Count; i++)
        {
            Assert.Equal(a.WeekReports()[i].Week, b.WeekReports()[i].Week);
            Assert.Equal(a.WeekReports()[i].Settlements.ToArray(), b.WeekReports()[i].Settlements.ToArray());
        }
    }

    [Fact]
    public void WeeklySettlementUsesNoRngAndEveryWeekRecordsUnchangedCounters()
    {
        // C05：舊每日 RESOURCE 抽樣已退役；每週提交記下十條流計數器，提交前＝提交後。
        var world = AdvancedWorld(888, 21);
        Assert.Equal(0UL, world.RngState().Single(s => s.StreamId == RngStreams.Resource).DrawCounter);
        Assert.Equal(3, world.WeekReports().Count);
        Assert.All(world.WeekReports(), w =>
        {
            Assert.Equal(RngStreams.All, w.RngCounters.Select(c => c.StreamId));
            Assert.All(w.RngCounters, c => Assert.Equal(c.Before, c.After));
        });
    }

    [Theory]
    [InlineData("AfterScheduleStaged")]
    [InlineData("AfterRngStaged")]
    [InlineData("AfterLedgerStaged")]
    [InlineData("BeforePublish")]
    public void FaultMidCommitLeavesEntityRngSchedulerHistoryAndLedgerUnchanged(string stageName)
    {
        // DemoCommitStage is internal, so the theory data is passed by name.
        var stage = Enum.Parse<DemoCommitStage>(stageName);

        // Day 7 is the commit that carries the weekly ledger and the RNG counters (C05).
        var world = AdvancedWorld(42, 6);
        var before = world.StateFingerprint();
        var snapshotBefore = world.Snapshot();
        var rngBefore = world.RngState().ToArray();

        world.FaultInjector = s =>
        {
            if (s == stage) throw new InjectedFault($"注入故障：{s}");
        };

        Assert.Throws<InjectedFault>(() => world.AdvanceOneDay());

        Assert.Equal(before, world.StateFingerprint());
        Assert.Equal(snapshotBefore, world.Snapshot());
        Assert.Equal(rngBefore, world.RngState().ToArray());
        Assert.Empty(world.WeekReports());
        Assert.Null(world.LatestWeek);

        // Once the fault is removed the SAME commit succeeds exactly like a world that never failed.
        world.FaultInjector = null;
        world.AdvanceOneDay();
        var control = AdvancedWorld(42, 7);
        Assert.Equal(control.StateFingerprint(), world.StateFingerprint());
        Assert.Single(world.WeekReports());
    }

    [Fact]
    public void FaultOnAnOrdinaryDayAlsoRollsBackCompletely()
    {
        var world = AdvancedWorld(5, 10);
        var before = world.StateFingerprint();
        world.FaultInjector = s =>
        {
            if (s == DemoCommitStage.BeforePublish) throw new InjectedFault("注入故障");
        };

        Assert.Throws<InjectedFault>(() => world.AdvanceOneDay());
        Assert.Equal(before, world.StateFingerprint());
    }

    [Fact]
    public void UnbalancedLedgerIsRejected()
    {
        var bad = new DemoSettlementWeekLedger(
            Week: 1, SettlementId: 1, SettlementName: "示範聚落甲",
            ReserveOpening: 60, Regenerated: 30, Harvested: 30, ReserveClosing: 60,
            FoodOpening: 10, FoodProduced: 31, FoodConsumed: 12, FoodClosing: 29, FoodShortage: 0,
            PopulationOpening: 3, Births: 0, Deaths: 0, PopulationClosing: 3);

        Assert.True(bad.ReserveBalanced);
        Assert.True(bad.FoodBalanced);
        Assert.False(bad.NoMagicFood); // 生產 31 > 採收 30：魔法糧食。
        Assert.False(bad.IsBalanced);

        var report = new DemoWeekReport(1, TimeSpan.FromDays(7), new[] { bad });
        var ex = Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(report));
        Assert.Contains("帳目不平衡", ex.Message);

        var popBad = bad with { FoodProduced = 30, FoodFromFarming = 30, FoodClosing = 28, PopulationClosing = 4 };
        Assert.True(popBad.NoMagicFood);
        Assert.True(popBad.FoodBalanced);
        Assert.False(popBad.PopulationBalanced);
        Assert.Throws<InvalidOperationException>(() =>
            DemoWeeklySettlement.Verify(new DemoWeekReport(1, TimeSpan.FromDays(7), new[] { popBad })));
    }

    [Fact]
    public void ZeroYieldMeansNoFoodIsProducedFromNothing()
    {
        var cfg = DemoWeeklyTempConfig.Sample with { YieldPerWorkerPerWeek = 0, InitialFoodPerPerson = 0 };
        var world = new DemoObserverWorld(DemoScenario.Sample(42), cfg);
        for (var i = 0; i < 28; i++) world.AdvanceOneDay();

        foreach (var week in world.WeekReports())
        foreach (var l in week.Settlements)
        {
            Assert.Equal(0, l.Harvested);
            Assert.Equal(0, l.FoodProduced);
            Assert.Equal(0, l.FoodClosing);
            Assert.Equal(l.PopulationOpening * cfg.FoodPerPersonPerWeek, l.FoodShortage);
        }
    }

    [Fact]
    public void InvalidWeeklyConfigIsRejectedBeforeAnyWorldExists()
    {
        Assert.Throws<ArgumentNullException>(() => new DemoObserverWorld(DemoScenario.Sample(1), null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoObserverWorld(
            DemoScenario.Sample(1), DemoWeeklyTempConfig.Sample with { FoodPerPersonPerWeek = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoObserverWorld(
            DemoScenario.Sample(1), DemoWeeklyTempConfig.Sample with { ReserveCapPerPoint = 10 }));
    }
}
