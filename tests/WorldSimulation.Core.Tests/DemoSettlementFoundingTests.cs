using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// V0-06B：遷徙人口建立新聚落（不憑空）。
/// 45F SETTLE_NEW／SETTLE_KNOW／SETTLE_SUPPLY／SETTLE_FAIL 與 45G M1_SETTLE（建立人口／物資從來源扣除）
/// 在 TEMP 示範範圍內的自動驗收。選址範圍、水源判定、開拓情境都是 TEMP；開拓情境只是測試用。
/// </summary>
public sealed class DemoSettlementFoundingTests
{
    private static readonly ulong[] ManySeeds = Enumerable.Range(1, 30).Select(i => (ulong)i).ToArray();
    private const int Weeks = 30;

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

    private static bool FoundsWithin(ulong seed, int weeks)
        => Run(seed, DemoWeeklyTempConfig.FrontierSample, weeks * 7).Sites.Settlements.Any(s => s.IsFounded);

    [Fact]
    public void FoundingSitesAreFreeLandWithWaterWithinTheTempRangeAndDeterministic()
    {
        var found = 0;
        foreach (var seed in ManySeeds)
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed));
            foreach (var s in world.Sites.Settlements)
            {
                var origin = new DemoCellRef(s.Column, s.Row);
                bool Free(DemoCellRef c) => world.Sites.SettlementAt(c.Column, c.Row) is null;
                var site = DemoMigrationRoutes.FindFoundingSite(world.Map, origin, 3, Free);
                Assert.Equal(site.ToArray(), DemoMigrationRoutes.FindFoundingSite(world.Map, origin, 3, Free).ToArray());
                if (site.IsEmpty)
                    continue;

                found++;
                Assert.Equal(origin, site[0]);
                Assert.InRange(site.Length - 1, 1, 3);
                for (var i = 1; i < site.Length; i++)
                {
                    Assert.True(DemoMigrationRoutes.AreAdjacent(site[i - 1], site[i]));
                    Assert.True(world.Map.Routes.IsLandPassable(site[i - 1], site[i]));
                    // 第 06 章 a 節：中途只有渡口可以是水域格；新聚落地點一定是陸地。
                    if (i == site.Length - 1 || world.Map.Routes.Ford != site[i])
                        Assert.NotEqual(DemoTerrain.Water, world.Map.GetCell(site[i].Column, site[i].Row).Terrain);
                }

                var end = site[^1];
                Assert.Null(world.Sites.SettlementAt(end.Column, end.Row));
                Assert.True(DemoMigrationRoutes.HasWaterAccess(world.Map, end));

                // 找到的是最近的合格空格：路線更短的格子都不合格。
                var shorter = DemoMigrationRoutes.FindFoundingSite(world.Map, origin, site.Length - 1, Free);
                Assert.Equal(site.ToArray(), shorter.ToArray());
                if (site.Length > 2)
                    Assert.True(DemoMigrationRoutes.FindFoundingSite(world.Map, origin, site.Length - 2, Free).IsEmpty);
            }
        }

        Assert.True(found > 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => DemoMigrationRoutes.FindFoundingSite(
            DemoHexWorld.Generate(1), new DemoCellRef(0, 0), 0, _ => true));
    }

    [Fact]
    public void NewSettlementsComeOnlyFromTheirFoundingBatch_M1Settle()
    {
        var seedsWithFounding = 0;
        foreach (var seed in ManySeeds)
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.FrontierSample);
            var initial = world.Sites.PopGroups.Sum(p => (long)p.Size);
            var originalIds = world.Sites.Settlements.Select(s => s.Id).ToArray();
            long deaths = 0;

            for (var day = 1; day <= Weeks * 7; day++)
            {
                world.AdvanceOneDay();
                if (day % 7 != 0)
                    continue;

                var week = world.LatestWeek!;
                deaths += week.Settlements.Sum(l => l.Deaths) + week.Migrations.Sum(m => m.Deaths);

                // 世界人口（各聚落＋途中，只計一次）只因死亡減少：新聚落沒有帶來任何人。
                Assert.Equal(initial - deaths, world.WorldPopulation);
                Assert.Equal(world.SettledPopulation, world.Sites.Settlements.Sum(s => world.SettlementPopulation(s.Id)));

                foreach (var founded in week.Events.Where(e => e.Kind == DemoSettlementEventKind.SettlementFounded))
                {
                    var settlement = world.Sites.Settlements.Single(s => s.Id == founded.SettlementId);
                    var batch = week.Migrations.Single(m => m.BatchId == founded.MigrationBatchId);
                    var ledger = week.Settlements.Single(l => l.SettlementId == settlement.Id);

                    Assert.True(settlement.IsFounded);
                    Assert.Equal(week.Week, settlement.FoundedWeek);
                    Assert.Equal(batch.BatchId, settlement.FoundedByBatchId);
                    Assert.Equal(batch.FromSettlementId, settlement.FoundedFromSettlementId);
                    Assert.True(batch.Founding && batch.Arrived);
                    Assert.Equal(settlement.Id, batch.ToSettlementId);
                    Assert.Equal(new DemoCellRef(settlement.Column, settlement.Row), batch.CellAfter);

                    // M1_SETTLE：新聚落人口＝建立它的遷徙群體人數；糧食、飢餓債也只來自隊伍；期初全部為 0。
                    Assert.Equal(batch.PopulationClosing, founded.Count);
                    Assert.Equal(0, ledger.PopulationOpening);
                    Assert.Equal(0, ledger.FoodOpening);
                    Assert.Equal(0, ledger.HungerDebtOpening);
                    Assert.Equal(0, ledger.ReserveOpening);
                    Assert.Equal(0, ledger.Births);
                    Assert.Equal(batch.PopulationClosing, ledger.Immigrants);
                    Assert.Equal(batch.FoodClosing, ledger.FoodCarriedIn);
                    Assert.Equal(batch.HungerDebtClosing, ledger.HungerDebtCarriedIn);
                    Assert.Equal(founded.Count, ledger.PopulationClosing);
                    Assert.True(ledger.IsBalanced);

                    // 開拓隊的人在出發那週從來源扣除。
                    var departure = world.WeekReports().SelectMany(w => w.Migrations)
                        .Single(m => m.BatchId == batch.BatchId && m.Departed);
                    Assert.True(departure.Founding);
                    Assert.Equal(0, departure.ToSettlementId);
                    var sourceLedger = world.WeekReports().Single(w => w.Week == departure.Week).Settlements
                        .Single(l => l.SettlementId == departure.FromSettlementId);
                    Assert.True(sourceLedger.Emigrants >= departure.PopulationOpening);

                    Assert.Contains("建立新聚落", founded.Describe());
                    Assert.False(string.IsNullOrWhiteSpace(founded.Cause));
                }
            }

            // 開局聚落不變；新聚落編號只會遞增、永不重用；一格最多一個聚落；新聚落不在水上。
            var ids = world.Sites.Settlements.Select(s => s.Id).ToArray();
            Assert.Equal(originalIds, ids.Take(originalIds.Length));
            // C04：新聚落 ID 由世界唯一分配器發號（與資源點、群體、隊伍共用），只遞增、互不重複。
            Assert.Equal(ids.OrderBy(id => id), ids);
            Assert.Equal(ids.Length, ids.Distinct().Count());
            Assert.Equal(ids.Length, world.Sites.Settlements.Select(s => (s.Column, s.Row)).Distinct().Count());
            Assert.Equal(world.Sites.ResourcePoints.Count, world.Sites.ResourcePoints.Select(p => p.Id).Distinct().Count());
            foreach (var s in world.Sites.Settlements.Where(s => s.IsFounded))
            {
                Assert.NotEqual(DemoTerrain.Water, world.Map.GetCell(s.Column, s.Row).Terrain);
                Assert.Single(world.Sites.ResourcePointsOf(s.Id));
                Assert.Empty(world.Sites.PopGroupsOf(s.Id)); // 沒有憑空的初始人口群體
                Assert.Single(world.SettlementEvents(),
                    e => e.Kind == DemoSettlementEventKind.SettlementFounded && e.SettlementId == s.Id);
            }

            Assert.Equal(world.Sites.Settlements.Count(s => s.IsFounded),
                world.SettlementEvents().Count(e => e.Kind == DemoSettlementEventKind.SettlementFounded));
            if (world.Sites.Settlements.Any(s => s.IsFounded))
                seedsWithFounding++;
        }

        Assert.True(seedsWithFounding > 0);
    }

    [Fact]
    public void FoundersLeaveOnlyAfterNewsFromEveryReachableSettlementSaysItIsStarving()
    {
        // SETTLE_KNOW＋MIG_PULL：開拓不是全知選址。出發時，已知範圍內每個可達聚落的消息都已傳到，
        // 而且消息裡沒有一個有糧；消息只來自已發布、且已沿路傳到的週報（新聚落建立前的週報裡沒有它）。
        var cfg = DemoWeeklyTempConfig.FrontierSample;
        var checkedDepartures = 0;
        foreach (var seed in ManySeeds)
        {
            var world = Run(seed, cfg, Weeks * 7);
            var reports = world.WeekReports();
            foreach (var week in reports)
            foreach (var m in week.Migrations.Where(m => m.Founding && m.Departed))
            {
                checkedDepartures++;
                var source = world.Sites.Settlements.Single(s => s.Id == m.FromSettlementId);
                var published = reports.Where(r => r.Week < week.Week).ToList();
                foreach (var other in world.Sites.Settlements.Where(s => s.Id != source.Id && s.FoundedWeek < week.Week))
                {
                    var route = DemoMigrationRoutes.FindRoute(world.Map,
                        new DemoCellRef(source.Column, source.Row), new DemoCellRef(other.Column, other.Row));
                    if (route.Length < 2 || route.Length - 1 > cfg.KnownRouteMaxSteps)
                        continue;
                    var news = DemoMigrationNews.Received(published, week.Week, other.Id, route.Length - 1,
                        cfg.NewsDelayWeeksPerCell);
                    Assert.NotNull(news);
                    Assert.False(DemoMigrationNews.SaysFed(news!.Value.Ledger));
                }

                // 開拓隊的目的格在出發時是空的陸地、有水源，且在 TEMP 選址範圍內。
                var batchEvent = week.Events.Single(e => e.Kind == DemoSettlementEventKind.Emigration && e.MigrationBatchId == m.BatchId);
                Assert.Contains("開拓新聚落", batchEvent.Cause);
                Assert.Contains("TEMP 每週 1 格", batchEvent.Cause);
            }
        }

        Assert.True(checkedDepartures > 0);
    }

    [Fact]
    public void FoundingIsOffUnlessTheTempSwitchIsOn()
    {
        foreach (var cfg in new[]
                 {
                     DemoWeeklyTempConfig.Sample, DemoWeeklyTempConfig.FamineSample, DemoWeeklyTempConfig.LocalFamineSample,
                     DemoWeeklyTempConfig.FrontierSample with { FoundingEnabled = false }
                 })
        {
            foreach (var seed in new[] { 42UL, 7UL, 1UL })
            {
                var world = Run(seed, cfg, 20 * 7);
                Assert.DoesNotContain(world.Sites.Settlements, s => s.IsFounded);
                Assert.DoesNotContain(world.SettlementEvents(), e => e.Kind == DemoSettlementEventKind.SettlementFounded);
                Assert.All(world.WeekReports().SelectMany(w => w.Migrations), m => Assert.False(m.Founding));
            }
        }

        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoObserverWorld(
            DemoScenario.Sample(1), DemoWeeklyTempConfig.FrontierSample with { FoundingSearchMaxSteps = 0 }));
    }

    [Fact]
    public void FailedExpeditionsLeaveNoSettlementAndEveryDeathHasACause()
    {
        // SETTLE_FAIL：開拓可失敗。全數死於途中的開拓隊不會留下聚落；死亡都有帶原因事件。
        foreach (var seed in ManySeeds)
        {
            var world = Run(seed, DemoWeeklyTempConfig.FrontierSample, Weeks * 7);
            foreach (var week in world.WeekReports())
            foreach (var m in week.Migrations.Where(m => m.Founding && m.PopulationClosing == 0 && m.PopulationOpening > 0))
            {
                Assert.False(m.Arrived);
                Assert.DoesNotContain(world.Sites.Settlements, s => s.FoundedByBatchId == m.BatchId);
                var caused = week.Events.Where(e => e.MigrationBatchId == m.BatchId
                    && e.Kind == DemoSettlementEventKind.MigrantStarvationDeath).ToList();
                Assert.Equal(m.Deaths, caused.Sum(e => e.Count));
                Assert.All(caused, e => Assert.Contains("開拓失敗", e.Cause));
            }
        }
    }

    [Fact]
    public void SameSeedFoundsTheSameSettlementsWithoutExtraRandomDraws()
    {
        var foundingSeed = ManySeeds.First(seed => FoundsWithin(seed, Weeks));
        foreach (var seed in new[] { 42UL, foundingSeed }.Distinct())
        {
            var a = Run(seed, DemoWeeklyTempConfig.FrontierSample, Weeks * 7);
            var b = Run(seed, DemoWeeklyTempConfig.FrontierSample, Weeks * 7);
            Assert.Equal(a.StateFingerprint(), b.StateFingerprint());
            Assert.Equal(a.Sites.Settlements.ToArray(), b.Sites.Settlements.ToArray());
            Assert.Equal(a.Sites.ResourcePoints.ToArray(), b.Sites.ResourcePoints.ToArray());
            // 開拓不抽亂數；C05 起執行期完全不抽 RESOURCE。
            Assert.Equal(0UL, a.RngState().Single(s => s.StreamId == RngStreams.Resource).DrawCounter);
        }
    }

    [Theory]
    [InlineData("AfterLedgerStaged")]
    [InlineData("BeforePublish")]
    public void FaultDuringAFoundingCommitChangesNothing(string stageName)
    {
        var stage = Enum.Parse<DemoCommitStage>(stageName);
        foreach (var seed in ManySeeds)
        {
            var probe = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.FrontierSample);
            var foundingDay = 0;
            for (var day = 1; day <= Weeks * 7 && foundingDay == 0; day++)
            {
                probe.AdvanceOneDay();
                if (probe.Sites.Settlements.Any(s => s.IsFounded))
                    foundingDay = day;
            }

            if (foundingDay == 0)
                continue;

            var world = Run(seed, DemoWeeklyTempConfig.FrontierSample, foundingDay - 1);
            var before = world.StateFingerprint();
            var sitesBefore = world.Sites;
            world.FaultInjector = s =>
            {
                if (s == stage) throw new InjectedFault($"注入故障：{s}");
            };
            Assert.Throws<InjectedFault>(() => world.AdvanceOneDay());

            Assert.Equal(before, world.StateFingerprint());
            Assert.Same(sitesBefore, world.Sites);
            Assert.DoesNotContain(world.Sites.Settlements, s => s.IsFounded);

            world.FaultInjector = null;
            world.AdvanceOneDay();
            Assert.Equal(Run(seed, DemoWeeklyTempConfig.FrontierSample, foundingDay).StateFingerprint(),
                world.StateFingerprint());
            Assert.Contains(world.Sites.Settlements, s => s.IsFounded);
            return;
        }

        Assert.Fail("No seed founded a settlement.");
    }

    [Fact]
    public void VerifyRejectsASettlementThatAppearsFromNowhere()
    {
        var worldTime = TimeSpan.FromDays(21);
        var source = new DemoSettlementWeekLedger(
            3, 1, "甲", 0, 0, 0, 0,
            FoodOpening: 0, FoodProduced: 0, FoodConsumed: 0, FoodClosing: 0, FoodShortage: 0,
            PopulationOpening: 1, Births: 0, Deaths: 0, PopulationClosing: 1);
        // 新聚落 #2：期初 0，遷入 2 人（由開拓隊 #1 帶來）。
        var founded = new DemoSettlementWeekLedger(
            3, 2, "新聚落2號", 0, 0, 0, 0,
            FoodOpening: 0, FoodProduced: 0, FoodConsumed: 0, FoodClosing: 0, FoodShortage: 0,
            PopulationOpening: 0, Births: 0, Deaths: 0, PopulationClosing: 2, Immigrants: 2);
        var arrived = new DemoMigrationWeekLedger(
            3, 1, 1, 2, new DemoCellRef(4, 2), new DemoCellRef(5, 2), Departed: false, Arrived: true,
            PopulationOpening: 2, Deaths: 0, PopulationClosing: 2,
            FoodOpening: 0, FoodConsumed: 0, FoodClosing: 0, FoodShortage: 0,
            HungerDebtOpening: 0, HungerDebtRecovered: 0, HungerDebtRemovedByDeath: 0, HungerDebtClosing: 0,
            Founding: true);
        var foundedEvent = new DemoSettlementEvent(3, worldTime, 2, "新聚落2號",
            DemoSettlementEventKind.SettlementFounded, 2, "測試開拓", 1);

        // 正確的一份：通過。
        DemoWeeklySettlement.Verify(new DemoWeekReport(3, worldTime, new[] { source, founded },
            new[] { foundedEvent }, new[] { arrived }));

        // 沒有建立事件（新聚落沒有原因）。
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(new DemoWeekReport(
            3, worldTime, new[] { source, founded }, Array.Empty<DemoSettlementEvent>(), new[] { arrived })));

        // 建立事件但沒有開拓隊：人口憑空出現（人口帳與世界人口都對不上）。
        var fromNowhere = founded with { Immigrants = 0, PopulationOpening = 0, Births = 2 };
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(new DemoWeekReport(
            3, worldTime, new[] { source, fromNowhere }, new[] { foundedEvent }, Array.Empty<DemoMigrationWeekLedger>())));

        // 新聚落期初就有人口或存量：不是從 0 開始。
        var notEmpty = founded with { PopulationOpening = 1, PopulationClosing = 3 };
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(new DemoWeekReport(
            3, worldTime, new[] { source, notEmpty }, new[] { foundedEvent }, new[] { arrived })));
        var withReserve = founded with { ReserveOpening = 5, ReserveClosing = 5 };
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(new DemoWeekReport(
            3, worldTime, new[] { source, withReserve }, new[] { foundedEvent }, new[] { arrived })));

        // 建立人數與抵達人數不一致。
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(new DemoWeekReport(
            3, worldTime, new[] { source, founded }, new[] { foundedEvent with { Count = 3 } }, new[] { arrived })));

        // 途中的開拓隊必須還沒有目的聚落（ToSettlementId＝0）。
        var travelling = arrived with { Arrived = false, ToSettlementId = 2 };
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(new DemoWeekReport(
            3, worldTime, new[] { source, founded with { Immigrants = 0, PopulationClosing = 0 } },
            Array.Empty<DemoSettlementEvent>(), new[] { travelling })));
    }

    [Fact]
    public void FrontierCenturyRunsWithZeroConservationErrors()
    {
        // V0-06B：3 種子（再加一個確定會開拓的種子）百年長測零錯誤；年度摘要列出新建聚落數。
        var foundingSeed = ManySeeds.First(seed => FoundsWithin(seed, Weeks));
        var anyFounded = false;
        foreach (var seed in new[] { 42UL, 7UL, 20261009UL, foundingSeed }.Distinct())
        {
            var report = DemoCenturyRun.Run(seed, 100, TestCalendar.DaysPerYear, DemoWeeklyTempConfig.FrontierSample);
            Assert.Empty(report.ConservationErrors);
            Assert.All(report.Years, y =>
            {
                Assert.Empty(y.Problems());
                Assert.Equal(y.PopulationClosing, y.PopulationOpening + y.Births - y.Deaths);
                Assert.Equal(y.Deaths, y.StarvationDeathsInEvents);
                Assert.Contains("新建聚落", y.Describe());
                Assert.True(y.InhabitedSettlements <= y.SettlementsClosing);
            });
            anyFounded |= report.Years.Any(y => y.SettlementsFounded > 0);

            var again = DemoCenturyRun.Run(seed, 100, TestCalendar.DaysPerYear, DemoWeeklyTempConfig.FrontierSample);
            Assert.Equal(report.Years.Select(y => y.Describe()), again.Years.Select(y => y.Describe()));
        }

        Assert.True(anyFounded);
    }
}
