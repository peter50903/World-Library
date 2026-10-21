using WorldSimulation.Core.Rng;
using System.Collections.Immutable;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// V0-04B：飢餓債 → 餓死，帶原因的聚落事件，以及 45G 的 M1_RES／M1_POP／M1_NO_MAGIC_FOOD
/// 在 TEMP 示範範圍內的自動驗收。所有門檻與速率都是 DemoWeeklyTempConfig 的 TEMP 值。
/// </summary>
public sealed class DemoStarvationTests
{
    private sealed class InjectedFault : Exception
    {
        public InjectedFault(string message) : base(message)
        {
        }
    }

    private static DemoObserverWorld Run(ulong seed, DemoWeeklyTempConfig cfg, int days)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), cfg);
        for (var i = 0; i < days; i++) world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void ZeroFoodProducesHungerThenCausedStarvationDeathsWithinWeeks()
    {
        var cfg = DemoWeeklyTempConfig.FamineSample;
        var world = Run(42, cfg, 4 * 7);

        var events = world.SettlementEvents();
        Assert.Contains(events, e => e.Kind == DemoSettlementEventKind.Hunger);
        var deaths = events.Where(e => e.Kind == DemoSettlementEventKind.StarvationDeath).ToList();
        Assert.NotEmpty(deaths);
        Assert.All(deaths, e =>
        {
            Assert.True(e.Count > 0);
            Assert.Contains("飢餓", e.Cause);
            Assert.Contains("餓死", e.Describe());
        });

        foreach (var week in world.WeekReports())
        foreach (var l in week.Settlements)
        {
            Assert.True(l.IsBalanced);
            Assert.Equal(0, l.Harvested);
            Assert.Equal(0, l.FoodProduced);
            // 每個帳上死亡都有同週、同聚落、帶原因的事件。
            Assert.Equal(l.Deaths, week.Events
                .Where(e => e.SettlementId == l.SettlementId && e.Kind == DemoSettlementEventKind.StarvationDeath)
                .Sum(e => e.Count));
        }

        // 人口帳：初始人口－累計餓死＝目前人口。
        foreach (var s in world.Sites.Settlements)
        {
            var initial = world.Sites.PopGroupsOf(s.Id).Sum(p => (long)p.Size);
            var died = events.Where(e => e.SettlementId == s.Id && e.Kind == DemoSettlementEventKind.StarvationDeath)
                .Sum(e => e.Count);
            Assert.Equal(initial - died, world.SettlementPopulation(s.Id));
        }
    }

    [Fact]
    public void FirstFamineWeekMatchesHandComputedTempArithmetic()
    {
        var cfg = DemoWeeklyTempConfig.FamineSample;
        var world = Run(42, cfg, 7);

        foreach (var l in world.LatestWeek!.Settlements)
        {
            var people = world.Sites.PopGroupsOf(l.SettlementId).Sum(p => (long)p.Size);
            var shortage = people * cfg.FoodPerPersonPerWeek;
            var deaths = Math.Min(people, shortage / cfg.HungerDebtPerStarvationDeath);
            var removed = people - deaths == 0 ? shortage : deaths * cfg.HungerDebtPerStarvationDeath;

            Assert.Equal(shortage, l.FoodShortage);
            Assert.Equal(0, l.HungerDebtOpening);
            Assert.Equal(deaths, l.Deaths);
            Assert.Equal(removed, l.HungerDebtRemovedByDeath);
            Assert.Equal(shortage - removed, l.HungerDebtClosing);
            Assert.Equal(people - deaths, l.PopulationClosing);
            Assert.Equal(l.HungerDebtClosing, world.SettlementHungerDebt(l.SettlementId));
        }
    }

    [Fact]
    public void FamineEventuallyEmptiesASettlementWithoutAnyHiddenRescue()
    {
        // SURV_NO_RESCUE：沒有任何來源時，聚落會真的滅亡，不生成糧食或人口。
        var world = Run(42, DemoWeeklyTempConfig.FamineSample, 52 * 7);
        foreach (var s in world.Sites.Settlements)
        {
            Assert.Equal(0, world.SettlementPopulation(s.Id));
            Assert.Equal(0, world.SettlementHungerDebt(s.Id));
            Assert.False(world.IsStarving(s.Id));
            Assert.Equal(0, world.SettlementFood(s.Id));
        }

        var last = world.LatestWeek!;
        Assert.All(last.Settlements, l => Assert.Equal(0, l.FoodConsumed));
        Assert.Empty(last.Events);
    }

    [Fact]
    public void HungerRecoversOnlyWhenRealFoodMeetsDemand()
    {
        // 直接呼叫純函式：聚落帶著飢餓債、資源點有存量。採收足以滿足需求的週，
        // 飢餓債才依 TEMP 速率下降，降到 0 時出現帶來源的「飢餓解除」事件。
        var cfg = DemoWeeklyTempConfig.Sample;
        var sites = new DemoObserverWorld(DemoScenario.Sample(42)).Sites;
        var reserves = sites.ResourcePoints.ToImmutableSortedDictionary(p => p.Id, _ => 100L);
        var food = sites.Settlements.ToImmutableSortedDictionary(s => s.Id, _ => 0L);
        var population = sites.Settlements.ToImmutableSortedDictionary(
            s => s.Id, s => sites.PopGroupsOf(s.Id).Sum(p => (long)p.Size));
        var debt = sites.Settlements.ToImmutableSortedDictionary(s => s.Id, _ => 10L);

        var recovered = new HashSet<int>();
        for (var week = 1; week <= 6; week++)
        {
            var outcome = DemoWeeklySettlement.Settle(week, TimeSpan.FromDays(7 * week), sites,
                reserves, food, population, debt, cfg);
            foreach (var l in outcome.Report.Settlements)
            {
                Assert.True(l.IsBalanced);
                Assert.Equal(0, l.FoodShortage);
                Assert.Equal(0, l.Deaths);
                Assert.Equal(Math.Min(l.HungerDebtOpening, l.PopulationOpening * cfg.HungerRecoveryPerPersonPerWeek),
                    l.HungerDebtRecovered);
            }

            foreach (var e in outcome.Report.Events)
            {
                Assert.Equal(DemoSettlementEventKind.HungerRecovered, e.Kind);
                Assert.Contains("來源", e.Cause);
                Assert.True(recovered.Add(e.SettlementId)); // 每個聚落只解除一次。
            }

            (reserves, food, population, debt) = (outcome.Reserves, outcome.SettlementFood, outcome.Population, outcome.HungerDebt);
        }

        Assert.True(recovered.SetEquals(sites.Settlements.Select(s => s.Id)));
        Assert.All(debt.Values, d => Assert.Equal(0, d));
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(7UL)]
    [InlineData(20261009UL)]
    public void M1ResPopAndNoMagicFoodHoldForEveryWeekAndSettlement(ulong seed)
    {
        foreach (var cfg in new[] { DemoWeeklyTempConfig.Sample, DemoWeeklyTempConfig.FamineSample })
        {
            var world = Run(seed, cfg, 52 * 7);
            var weeks = world.WeekReports();
            Assert.Equal(52, weeks.Count);

            DemoWeekReport? previous = null;
            foreach (var week in weeks)
            {
                foreach (var l in week.Settlements)
                {
                    // M1_RES：每個資源點／期初庫存＋流入－流出＝結束，差額都有原因（再生、採收、消耗）。
                    Assert.Equal(l.ReserveClosing, l.ReserveOpening + l.Regenerated - l.Harvested);
                    Assert.Equal(l.FoodClosing, l.FoodOpening + l.FoodProduced + l.FoodCarriedIn - l.FoodConsumed - l.FoodCarriedOut);
                    Assert.Equal(l.ReserveClosing + l.FoodClosing,
                        l.ReserveOpening + l.FoodOpening + l.Regenerated + l.FoodCarriedIn - l.FoodConsumed - l.FoodCarriedOut);

                    // M1_POP：期初＋出生－死亡＝期末；死亡全部由帶原因事件對帳。
                    Assert.Equal(l.PopulationClosing, l.PopulationOpening + l.Births - l.Deaths + l.Immigrants - l.Emigrants);
                    Assert.Equal(l.Deaths, week.Events
                        .Where(e => e.SettlementId == l.SettlementId && e.Kind == DemoSettlementEventKind.StarvationDeath)
                        .Sum(e => e.Count));

                    // M1_NO_MAGIC_FOOD：糧食增量只來自合法採集／農牧，且等於實際採收。
                    Assert.Equal(l.Harvested, l.FoodProduced);
                    Assert.Equal(l.FoodProduced, l.FoodFromFarming + l.FoodFromGathering);
                    var kinds = world.Sites.ResourcePointsOf(l.SettlementId).Select(p => p.Kind).ToList();
                    Assert.Equal(0, l.FoodFromFarming); // 第 08 章 a 節：沒有自動農田，農牧產量一律 0
                    if (!kinds.Any(k => k is DemoResourceKind.Woodland or DemoResourceKind.Fishery))
                        Assert.Equal(0, l.FoodFromGathering);

                    // 飢餓債帳平衡。
                    Assert.Equal(l.HungerDebtClosing,
                        l.HungerDebtOpening + l.FoodShortage - l.HungerDebtRecovered - l.HungerDebtRemovedByDeath
                        + l.HungerDebtCarriedIn - l.HungerDebtCarriedOut);
                    Assert.True(l.IsBalanced);

                    if (previous is not null)
                    {
                        var before = previous.Settlements.Single(p => p.SettlementId == l.SettlementId);
                        Assert.Equal(before.PopulationClosing, l.PopulationOpening);
                        Assert.Equal(before.HungerDebtClosing, l.HungerDebtOpening);
                        Assert.Equal(before.FoodClosing, l.FoodOpening);
                        Assert.Equal(before.ReserveClosing, l.ReserveOpening);
                    }
                }

                previous = week;
            }

            // 週結算不抽亂數；C05 起執行期完全不抽 RESOURCE。
            Assert.Equal(0UL, world.RngState().Single(s => s.StreamId == RngStreams.Resource).DrawCounter);
        }
    }

    [Fact]
    public void SameSeedGivesIdenticalFamineHistory()
    {
        var a = Run(99, DemoWeeklyTempConfig.FamineSample, 8 * 7);
        var b = Run(99, DemoWeeklyTempConfig.FamineSample, 8 * 7);

        Assert.Equal(a.StateFingerprint(), b.StateFingerprint());
        Assert.Equal(a.SettlementEvents().ToArray(), b.SettlementEvents().ToArray());
    }

    [Theory]
    [InlineData("AfterLedgerStaged")]
    [InlineData("BeforePublish")]
    public void FaultDuringAStarvationCommitChangesNothing(string stageName)
    {
        var stage = Enum.Parse<DemoCommitStage>(stageName);
        var world = Run(42, DemoWeeklyTempConfig.FamineSample, 6);
        var before = world.StateFingerprint();
        var popBefore = world.Sites.Settlements.Select(s => world.SettlementPopulation(s.Id)).ToArray();

        world.FaultInjector = s =>
        {
            if (s == stage) throw new InjectedFault($"注入故障：{s}");
        };
        Assert.Throws<InjectedFault>(() => world.AdvanceOneDay());

        Assert.Equal(before, world.StateFingerprint());
        Assert.Empty(world.SettlementEvents());
        Assert.Equal(popBefore, world.Sites.Settlements.Select(s => world.SettlementPopulation(s.Id)).ToArray());

        world.FaultInjector = null;
        world.AdvanceOneDay();
        Assert.Equal(Run(42, DemoWeeklyTempConfig.FamineSample, 7).StateFingerprint(), world.StateFingerprint());
        Assert.NotEmpty(world.SettlementEvents());
    }

    [Fact]
    public void DeathWithoutACausedEventIsRejected()
    {
        var ledger = new DemoSettlementWeekLedger(
            Week: 1, SettlementId: 1, SettlementName: "示範聚落甲",
            ReserveOpening: 0, Regenerated: 0, Harvested: 0, ReserveClosing: 0,
            FoodOpening: 0, FoodProduced: 0, FoodConsumed: 0, FoodClosing: 0, FoodShortage: 12,
            PopulationOpening: 3, Births: 0, Deaths: 1, PopulationClosing: 2,
            HungerDebtOpening: 0, HungerDebtRecovered: 0, HungerDebtRemovedByDeath: 12, HungerDebtClosing: 0);
        Assert.True(ledger.IsBalanced);

        // 帳上 1 人死亡，但沒有任何餓死事件 → 拒絕。
        var noEvent = new DemoWeekReport(1, TimeSpan.FromDays(7), new[] { ledger });
        var ex = Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(noEvent));
        Assert.Contains("死亡沒有對應的帶原因事件", ex.Message);

        // 有事件但原因空白 → 拒絕。
        var blank = new DemoSettlementEvent(1, TimeSpan.FromDays(7), 1, "示範聚落甲",
            DemoSettlementEventKind.StarvationDeath, 1, " ");
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(
            new DemoWeekReport(1, TimeSpan.FromDays(7), new[] { ledger }, new[] { blank })));

        // 有原因的事件 → 通過。
        var caused = blank with { Cause = "飢餓" };
        DemoWeeklySettlement.Verify(new DemoWeekReport(1, TimeSpan.FromDays(7), new[] { ledger }, new[] { caused }));

        // 有死亡卻沒有移除任何飢餓債（死亡無因）→ 帳目不平衡。
        var causeless = ledger with { HungerDebtRemovedByDeath = 0, HungerDebtClosing = 12 };
        Assert.False(causeless.HungerDebtBalanced);
        Assert.False(causeless.IsBalanced);
    }

    [Fact]
    public void InvalidStarvationConfigIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoObserverWorld(
            DemoScenario.Sample(1), DemoWeeklyTempConfig.Sample with { HungerDebtPerStarvationDeath = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoObserverWorld(
            DemoScenario.Sample(1), DemoWeeklyTempConfig.Sample with { HungerRecoveryPerPersonPerWeek = -1 }));
    }

    [Theory]
    [InlineData(42UL)]
    [InlineData(7UL)]
    [InlineData(20261009UL)]
    public void SettlementStaysStarvingInAWeekWhoseDeathsClearedAllHungerDebt(ulong seed)
    {
        // #59：餓死把飢餓債全部扣光、但當週仍有缺糧時，狀態必須仍是「飢餓中」（45C FOOD_UNMET）。
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.FamineSample);
        var sawDebtClearedWhileShort = false;
        for (var day = 1; day <= 12 * 7; day++)
        {
            world.AdvanceOneDay();
            foreach (var s in world.Sites.Settlements)
            {
                var ledger = world.LatestWeek?.Settlements.Single(l => l.SettlementId == s.Id);
                var shortage = ledger?.FoodShortage ?? 0;
                Assert.Equal(shortage, world.LatestWeekShortage(s.Id));

                var people = world.SettlementPopulation(s.Id);
                var debt = world.SettlementHungerDebt(s.Id);
                Assert.Equal(people > 0 && (debt > 0 || shortage > 0), world.IsStarving(s.Id));
                if (people > 0 && debt == 0 && shortage > 0)
                {
                    sawDebtClearedWhileShort = true;
                    Assert.True(world.IsStarving(s.Id));
                }
            }
        }

        Assert.True(sawDebtClearedWhileShort);
    }

    [Fact]
    public void FedSettlementIsNotStarvingAndNothingIsStarvingBeforeTheFirstWeek()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42));
        Assert.All(world.Sites.Settlements, s =>
        {
            Assert.Equal(0, world.LatestWeekShortage(s.Id));
            Assert.False(world.IsStarving(s.Id));
        });

        for (var i = 0; i < 7; i++) world.AdvanceOneDay();
        Assert.All(world.LatestWeek!.Settlements.Where(l => l.FoodShortage == 0 && l.HungerDebtClosing == 0),
            l => Assert.False(world.IsStarving(l.SettlementId)));
    }
}
