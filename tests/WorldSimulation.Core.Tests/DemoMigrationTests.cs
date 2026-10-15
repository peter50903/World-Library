using WorldSimulation.Core.Rng;
using System.Collections.Immutable;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// V0-06A：缺糧聚落人口沿相鄰格遷徙（不瞬移）＋遷徙帶原因事件。
/// 45G M1_MIG（遷徙帳：來源／途中／目的地人口帳一致；路線有效）在 TEMP 示範範圍內的自動驗收。
/// 路線、速度、門檻都是 TEMP；局部饑荒情境只是測試用。
/// </summary>
public sealed class DemoMigrationTests
{
    private static readonly ulong[] ManySeeds = Enumerable.Range(1, 30).Select(i => (ulong)i).ToArray();

    // 途中不會餓死的 TEMP 測試設定：確認抵達與併入目的地。
    private static readonly DemoWeeklyTempConfig Gentle =
        DemoWeeklyTempConfig.LocalFamineSample with { HungerDebtPerStarvationDeath = 1000 };

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

    private static long WorldPopulation(DemoObserverWorld world)
        => world.Sites.Settlements.Sum(s => world.SettlementPopulation(s.Id)) + world.PopulationInTransit;

    private static long InitialPopulation(DemoObserverWorld world)
        => world.Sites.PopGroups.Sum(p => (long)p.Size);

    [Fact]
    public void HexAdjacencyFollowsTheTempOddRowLayout()
    {
        var c = new DemoCellRef(4, 2); // 偶數列
        Assert.True(DemoMigrationRoutes.AreAdjacent(c, new DemoCellRef(5, 2)));
        Assert.True(DemoMigrationRoutes.AreAdjacent(c, new DemoCellRef(3, 2)));
        Assert.True(DemoMigrationRoutes.AreAdjacent(c, new DemoCellRef(3, 1)));
        Assert.True(DemoMigrationRoutes.AreAdjacent(c, new DemoCellRef(4, 1)));
        Assert.True(DemoMigrationRoutes.AreAdjacent(c, new DemoCellRef(3, 3)));
        Assert.True(DemoMigrationRoutes.AreAdjacent(c, new DemoCellRef(4, 3)));
        Assert.False(DemoMigrationRoutes.AreAdjacent(c, new DemoCellRef(5, 1)));
        Assert.False(DemoMigrationRoutes.AreAdjacent(c, new DemoCellRef(6, 2)));
        Assert.False(DemoMigrationRoutes.AreAdjacent(c, c));

        var odd = new DemoCellRef(4, 3); // 奇數列
        Assert.True(DemoMigrationRoutes.AreAdjacent(odd, new DemoCellRef(5, 2)));
        Assert.True(DemoMigrationRoutes.AreAdjacent(odd, new DemoCellRef(5, 4)));
        Assert.False(DemoMigrationRoutes.AreAdjacent(odd, new DemoCellRef(3, 2)));

        // 與 DemoHexWorld 的軸向座標一致。
        foreach (var cell in DemoHexWorld.Generate(42).Cells)
            Assert.Equal((cell.AxialQ, cell.AxialR), DemoMigrationRoutes.ToAxial(new DemoCellRef(cell.Column, cell.Row)));
    }

    [Fact]
    public void RoutesStepOnlyToAdjacentLandCellsAndAreDeterministic()
    {
        var found = 0;
        foreach (var seed in ManySeeds)
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed));
            foreach (var a in world.Sites.Settlements)
            foreach (var b in world.Sites.Settlements.Where(s => s.Id != a.Id))
            {
                var source = new DemoCellRef(a.Column, a.Row);
                var target = new DemoCellRef(b.Column, b.Row);
                var route = DemoMigrationRoutes.FindRoute(world.Map, source, target);
                // ImmutableArray 的 Equals 是參照相等，所以比較展開後的內容。
                Assert.Equal(route.ToArray(), DemoMigrationRoutes.FindRoute(world.Map, source, target).ToArray());
                if (route.IsEmpty)
                    continue; // MIG_NO_TARGET：沒有陸路就沒有這個目的地。

                found++;
                Assert.Equal(source, route[0]);
                Assert.Equal(target, route[^1]);
                Assert.Equal(route.Length, route.Distinct().Count());
                for (var i = 1; i < route.Length; i++)
                    Assert.True(DemoMigrationRoutes.AreAdjacent(route[i - 1], route[i]));
                for (var i = 1; i < route.Length - 1; i++)
                    Assert.NotEqual(DemoTerrain.Water, world.Map.GetCell(route[i].Column, route[i].Row).Terrain);
            }
        }

        Assert.True(found > 0);
    }

    [Fact]
    public void StarvingPeopleWalkCellByCellAndWorldPopulationIsConservedEveryWeek()
    {
        var seedsWithMigration = 0;
        foreach (var seed in ManySeeds)
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LocalFamineSample);
            var initial = InitialPopulation(world);
            long deaths = 0;
            var position = new Dictionary<int, DemoCellRef>();

            for (var day = 1; day <= 20 * 7; day++)
            {
                world.AdvanceOneDay();
                if (day % 7 != 0)
                    continue;

                var week = world.LatestWeek!;
                deaths += week.Settlements.Sum(l => l.Deaths) + week.Migrations.Sum(m => m.Deaths);

                // 世界人口（聚落＋途中，只計一次）只因死亡減少。
                Assert.Equal(initial - deaths, WorldPopulation(world));

                foreach (var m in week.Migrations)
                {
                    // M1_MIG：原地或相鄰格；路線上的格子銜接上一週的位置。
                    Assert.True(m.NoTeleport);
                    Assert.True(m.IsBalanced);
                    if (m.Departed)
                    {
                        var source = world.Sites.Settlements.Single(s => s.Id == m.FromSettlementId);
                        Assert.Equal(new DemoCellRef(source.Column, source.Row), m.CellBefore);
                        Assert.False(position.ContainsKey(m.BatchId));
                    }
                    else
                    {
                        Assert.Equal(position[m.BatchId], m.CellBefore);
                    }

                    position[m.BatchId] = m.CellAfter;
                    if (m.Arrived)
                    {
                        var target = world.Sites.Settlements.Single(s => s.Id == m.ToSettlementId);
                        Assert.Equal(new DemoCellRef(target.Column, target.Row), m.CellAfter);
                    }
                }

                // 途中隊伍＝本週帳上沒有抵達、沒有全數死亡的隊伍。
                var travelling = world.MigrationsInTransit();
                Assert.Equal(
                    week.Migrations.Where(m => !m.Arrived && m.PopulationClosing > 0).Select(m => m.BatchId),
                    travelling.Select(b => b.Id));
                foreach (var b in travelling)
                {
                    Assert.Equal(position[b.Id], b.Cell);
                    Assert.True(b.People > 0);
                }
            }

            // 第一週沒有任何消息（MIG_PULL），所以不會遷徙。
            Assert.DoesNotContain(world.SettlementEvents(),
                e => e.Kind == DemoSettlementEventKind.Emigration && e.Week == 1);

            if (world.SettlementEvents().Any(e => e.Kind == DemoSettlementEventKind.Emigration))
                seedsWithMigration++;
        }

        Assert.True(seedsWithMigration > 0);
    }

    [Fact]
    public void ArrivalsJoinTheDestinationWithExactlyThePeopleFoodAndDebtThatSurvivedTheRoad()
    {
        var arrivals = 0;
        foreach (var seed in ManySeeds)
        {
            var world = Run(seed, Gentle, 30 * 7);
            var departures = world.SettlementEvents().Where(e => e.Kind == DemoSettlementEventKind.Emigration).ToList();
            foreach (var departure in departures)
            {
                Assert.True(departure.MigrationBatchId > 0);
                Assert.Contains("前往", departure.Cause);
                Assert.Contains("TEMP 每週 1 格", departure.Cause);
                Assert.Contains("因缺糧外移", departure.Describe());
            }

            foreach (var week in world.WeekReports())
            foreach (var m in week.Migrations.Where(m => m.Arrived))
            {
                arrivals++;
                var destination = week.Settlements.Single(l => l.SettlementId == m.ToSettlementId);
                Assert.True(destination.Immigrants >= m.PopulationClosing);
                Assert.Contains(week.Events, e => e.Kind == DemoSettlementEventKind.Arrival
                    && e.MigrationBatchId == m.BatchId && e.Count == m.PopulationClosing
                    && e.SettlementId == m.ToSettlementId && e.Describe().Contains("遷徙者抵達"));

                // 走了幾週就走了幾格：出發週停在原地，之後每週一格。
                var departedWeek = world.WeekReports()
                    .SelectMany(w => w.Migrations).Single(x => x.BatchId == m.BatchId && x.Departed).Week;
                var steps = world.WeekReports().SelectMany(w => w.Migrations)
                    .Count(x => x.BatchId == m.BatchId && !x.Departed);
                Assert.Equal(week.Week - departedWeek, steps);
            }
        }

        Assert.True(arrivals > 0);
    }

    [Fact]
    public void MigrantsWithoutFoodCanDieOnTheRoadAndEveryDeathHasACause()
    {
        foreach (var seed in ManySeeds)
        {
            var world = Run(seed, DemoWeeklyTempConfig.LocalFamineSample, 20 * 7);
            foreach (var week in world.WeekReports())
            foreach (var m in week.Migrations.Where(m => m.Deaths > 0))
            {
                var caused = week.Events.Where(e => e.Kind == DemoSettlementEventKind.MigrantStarvationDeath
                    && e.MigrationBatchId == m.BatchId).ToList();
                Assert.Equal(m.Deaths, caused.Sum(e => e.Count));
                Assert.All(caused, e => Assert.Contains("途中", e.Cause));
                if (m.PopulationClosing == 0)
                {
                    Assert.False(m.Arrived);
                    Assert.Equal(m.CellBefore, m.CellAfter);
                    Assert.Equal(0, m.HungerDebtClosing);
                }
            }
        }
    }

    [Fact]
    public void WithoutAKnownFedDestinationNobodyMigrates()
    {
        // MIG_NO_TARGET：全面饑荒時沒有任何有糧聚落的消息，人口留下承受後果，不被強迫平衡。
        foreach (var seed in new[] { 42UL, 7UL, 20261009UL })
        {
            var world = Run(seed, DemoWeeklyTempConfig.FamineSample, 20 * 7);
            Assert.All(world.WeekReports(), w => Assert.Empty(w.Migrations));
            Assert.Equal(0, world.PopulationInTransit);
        }

        // 關閉遷徙（TEMP 開關）時，局部饑荒也不會遷徙。
        var off = Run(42, DemoWeeklyTempConfig.LocalFamineSample with { MigrationEnabled = false }, 20 * 7);
        Assert.All(off.WeekReports(), w => Assert.Empty(w.Migrations));
    }

    [Fact]
    public void SameSeedGivesIdenticalMigrationsAndBatchIdsAreNeverReused()
    {
        foreach (var seed in new[] { 42UL, 1UL, 7UL })
        {
            var a = Run(seed, DemoWeeklyTempConfig.LocalFamineSample, 30 * 7);
            var b = Run(seed, DemoWeeklyTempConfig.LocalFamineSample, 30 * 7);
            Assert.Equal(a.StateFingerprint(), b.StateFingerprint());
            Assert.Equal(a.SettlementEvents().ToArray(), b.SettlementEvents().ToArray());

            var departedIds = a.WeekReports().SelectMany(w => w.Migrations).Where(m => m.Departed)
                .Select(m => m.BatchId).ToList();
            Assert.Equal(departedIds.Count, departedIds.Distinct().Count());
            Assert.Equal(departedIds.OrderBy(id => id), departedIds);
            Assert.All(departedIds, id => Assert.True(id > 0));

            // 遷徙不抽亂數；C05 起執行期完全不抽 RESOURCE。
            Assert.Equal(0UL, a.RngState().Single(s => s.StreamId == RngStreams.Resource).DrawCounter);
        }
    }

    [Theory]
    [InlineData("AfterLedgerStaged")]
    [InlineData("BeforePublish")]
    public void FaultDuringADepartureCommitChangesNothing(string stageName)
    {
        var stage = Enum.Parse<DemoCommitStage>(stageName);
        foreach (var seed in ManySeeds)
        {
            // 找出第一次出發的日子，在那一天的提交中途注入故障。
            var probe = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LocalFamineSample);
            var departureDay = 0;
            for (var day = 1; day <= 10 * 7 && departureDay == 0; day++)
            {
                probe.AdvanceOneDay();
                if (probe.MigrationsInTransit().Count > 0)
                    departureDay = day;
            }

            if (departureDay == 0)
                continue;

            var world = Run(seed, DemoWeeklyTempConfig.LocalFamineSample, departureDay - 1);
            var before = world.StateFingerprint();
            world.FaultInjector = s =>
            {
                if (s == stage) throw new InjectedFault($"注入故障：{s}");
            };
            Assert.Throws<InjectedFault>(() => world.AdvanceOneDay());

            Assert.Equal(before, world.StateFingerprint());
            Assert.Empty(world.MigrationsInTransit());

            world.FaultInjector = null;
            world.AdvanceOneDay();
            Assert.Equal(Run(seed, DemoWeeklyTempConfig.LocalFamineSample, departureDay).StateFingerprint(),
                world.StateFingerprint());
            Assert.NotEmpty(world.MigrationsInTransit());
            return;
        }

        Assert.Fail("No seed produced a departure.");
    }

    [Fact]
    public void VerifyRejectsTeleportsAndOneSidedMigrationLedgers()
    {
        var source = new DemoSettlementWeekLedger(
            2, 1, "甲", 0, 0, 0, 0,
            FoodOpening: 0, FoodProduced: 0, FoodConsumed: 0, FoodClosing: 0, FoodShortage: 8,
            PopulationOpening: 2, Births: 0, Deaths: 0, PopulationClosing: 1,
            HungerDebtOpening: 0, HungerDebtRecovered: 0, HungerDebtRemovedByDeath: 0, HungerDebtClosing: 4,
            Emigrants: 1, HungerDebtCarriedOut: 4);
        var target = new DemoSettlementWeekLedger(
            2, 2, "乙", 0, 0, 0, 0,
            FoodOpening: 0, FoodProduced: 0, FoodConsumed: 0, FoodClosing: 0, FoodShortage: 0,
            PopulationOpening: 3, Births: 0, Deaths: 0, PopulationClosing: 3);
        var departed = new DemoMigrationWeekLedger(
            2, 1, 1, 2, new DemoCellRef(4, 2), new DemoCellRef(4, 2), Departed: true, Arrived: false,
            PopulationOpening: 1, Deaths: 0, PopulationClosing: 1,
            FoodOpening: 0, FoodConsumed: 0, FoodClosing: 0, FoodShortage: 0,
            HungerDebtOpening: 4, HungerDebtRecovered: 0, HungerDebtRemovedByDeath: 0, HungerDebtClosing: 4);
        var events = new[]
        {
            new DemoSettlementEvent(2, TimeSpan.FromDays(14), 1, "甲", DemoSettlementEventKind.Emigration, 1, "測試", 1)
        };

        // 合法：一人出發、停在來源格。
        DemoWeeklySettlement.Verify(new DemoWeekReport(2, TimeSpan.FromDays(14), new[] { source, target }, events, new[] { departed }));

        // 瞬移：出發週就跑到兩格外。
        var teleport = departed with { CellAfter = new DemoCellRef(6, 2) };
        Assert.False(teleport.NoTeleport);
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(
            new DemoWeekReport(2, TimeSpan.FromDays(14), new[] { source, target }, events, new[] { teleport })));

        // 途中一週走兩格。
        var jump = departed with
        {
            Departed = false, CellBefore = new DemoCellRef(4, 2), CellAfter = new DemoCellRef(6, 2),
            HungerDebtOpening = 0, HungerDebtClosing = 0
        };
        Assert.False(jump.NoTeleport);

        // 來源扣了人卻沒有隊伍（人口憑空消失）。
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(
            new DemoWeekReport(2, TimeSpan.FromDays(14), new[] { source, target }, events)));

        // 隊伍憑空出現（來源沒有扣人）。
        var sourceNoOut = source with { PopulationClosing = 2, Emigrants = 0, HungerDebtClosing = 8, HungerDebtCarriedOut = 0 };
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(
            new DemoWeekReport(2, TimeSpan.FromDays(14), new[] { sourceNoOut, target }, events, new[] { departed })));

        // 抵達的人數與目的地遷入對不上。
        var arrived = departed with
        {
            Departed = false, Arrived = true, CellBefore = new DemoCellRef(4, 2), CellAfter = new DemoCellRef(5, 2),
            FoodShortage = 4, HungerDebtClosing = 8
        };
        Assert.True(arrived.IsBalanced);
        var sourceNone = source with { PopulationClosing = 2, Emigrants = 0, HungerDebtClosing = 8, HungerDebtCarriedOut = 0 };
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Verify(
            new DemoWeekReport(3, TimeSpan.FromDays(21), new[] { sourceNone, target }, Array.Empty<DemoSettlementEvent>(),
                new[] { arrived })));
    }

    [Fact]
    public void LocalFamineCenturyRunsWithZeroConservationErrors()
    {
        // #61：消息要時間傳到，固定種子未必會遷徙；另外加入第一個 20 週內確實有人出發的種子。
        var migratingSeed = ManySeeds.First(seed => Run(seed, DemoWeeklyTempConfig.LocalFamineSample, 20 * 7)
            .SettlementEvents().Any(e => e.Kind == DemoSettlementEventKind.Emigration));
        var anyMigration = false;
        foreach (var seed in new[] { 42UL, 7UL, 20261009UL, migratingSeed }.Distinct())
        {
            var report = DemoCenturyRun.Run(seed, 100, TestCalendar.DaysPerYear, DemoWeeklyTempConfig.LocalFamineSample);
            Assert.Empty(report.ConservationErrors);
            Assert.All(report.Years, y =>
            {
                Assert.Empty(y.Problems());
                Assert.Equal(y.PopulationClosing, y.PopulationOpening + y.Births - y.Deaths);
                Assert.Equal(y.Deaths, y.StarvationDeathsInEvents);
                Assert.Equal(y.FoodClosing, y.FoodOpening + y.FoodProduced - y.FoodConsumed);
                Assert.Contains("遷出", y.Describe());
            });
            anyMigration |= report.Years.Any(y => y.Emigrants > 0);
        }

        Assert.True(anyMigration);
    }

    [Fact]
    public void InvalidMigrationConfigIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoObserverWorld(
            DemoScenario.Sample(1), DemoWeeklyTempConfig.Sample with { EmigrationShareDivisor = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoObserverWorld(
            DemoScenario.Sample(1), DemoWeeklyTempConfig.Sample with { MinPopulationToEmigrate = 1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoObserverWorld(
            DemoScenario.Sample(1), DemoWeeklyTempConfig.Sample with { KnownRouteMaxSteps = 0 }));
    }

    [Fact]
    public void SettleRejectsReusedBatchIds()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42));
        var sites = world.Sites;
        var settlement = sites.Settlements[0];
        var cell = new DemoCellRef(settlement.Column, settlement.Row);
        var batch = new DemoMigrationBatch(5, settlement.Id, settlement.Name, sites.Settlements[1].Id,
            sites.Settlements[1].Name, ImmutableArray.Create(cell, cell), 0, 1, 0, 0, 1);
        var zero = sites.Settlements.ToImmutableSortedDictionary(s => s.Id, _ => 0L);

        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Settle(
            2, TimeSpan.FromDays(14), world.Map, sites,
            sites.ResourcePoints.ToImmutableSortedDictionary(p => p.Id, _ => 0L), zero, zero, zero,
            ImmutableList.Create(batch), new EntityIdAllocatorSnapshot(EntityIdAllocatorSnapshot.CurrentVersion, 5, false),
            publishedWeeks: Array.Empty<DemoWeekReport>(), DemoWeeklyTempConfig.Sample));
    }

    // ── #61：MIG_PULL 消息不可全知（GPT 審查 PR #58 意見 1）──

    [Fact]
    public void NewsTravelsAlongTheRouteOneCellPerTempWeek()
    {
        Assert.Equal(4, DemoMigrationNews.NewsWeekFor(5, 1, 1));
        Assert.Equal(2, DemoMigrationNews.NewsWeekFor(5, 3, 1));
        Assert.Equal(-1, DemoMigrationNews.NewsWeekFor(5, 3, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => DemoMigrationNews.NewsWeekFor(5, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DemoMigrationNews.NewsWeekFor(5, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DemoObserverWorld(
            DemoScenario.Sample(1), DemoWeeklyTempConfig.Sample with { NewsDelayWeeksPerCell = 0 }));

        // 只看已發布的週報：第 3 週路程 2 格 → 只收得到第 1 週；第 1 週路程 1 格 → 還沒有任何消息。
        var world = Run(42, DemoWeeklyTempConfig.Sample, 2 * 7);
        var target = world.Sites.Settlements[1];
        var news = DemoMigrationNews.Received(world.WeekReports(), 3, target.Id, 2, 1);
        Assert.NotNull(news);
        Assert.Equal(1, news!.Value.Week);
        Assert.Same(world.WeekReports()[0].Settlements.Single(l => l.SettlementId == target.Id), news.Value.Ledger);
        Assert.Null(DemoMigrationNews.Received(world.WeekReports(), 1, target.Id, 1, 1));
        Assert.Null(DemoMigrationNews.Received(world.WeekReports(), 3, target.Id, 3, 1));
    }

    [Fact]
    public void EveryDepartureUsesOnlyNewsThatHadTimeToArrive()
    {
        var departures = 0;
        var farDepartures = 0;
        foreach (var seed in ManySeeds)
        {
            var world = Run(seed, Gentle, 30 * 7);
            var weeks = world.WeekReports();
            var replay = new DemoObserverWorld(DemoScenario.Sample(seed), Gentle);
            var departedBatches = new Dictionary<int, DemoMigrationBatch>();
            for (var day = 1; day <= 30 * 7; day++)
            {
                replay.AdvanceOneDay();
                if (day % 7 != 0)
                    continue;
                foreach (var b in replay.MigrationsInTransit().Where(b => b.DepartedWeek == day / 7))
                    departedBatches[b.Id] = b;
            }

            foreach (var week in weeks)
            foreach (var m in week.Migrations.Where(m => m.Departed))
            {
                departures++;
                var batch = departedBatches[m.BatchId];
                var steps = batch.Path.Length - 1;
                if (steps > 1) farDepartures++;

                // 依據的消息週＝出發週－路程×延遲；那一週的消息確實說目的地有糧。
                Assert.Equal(m.Week, batch.DepartedWeek);
                Assert.Equal(m.Week - steps * Gentle.NewsDelayWeeksPerCell, batch.NewsWeek);
                Assert.True(batch.NewsWeek >= 1);
                var news = weeks[batch.NewsWeek - 1].Settlements.Single(l => l.SettlementId == m.ToSettlementId);
                Assert.True(DemoMigrationNews.SaysFed(news));

                var departure = week.Events.Single(e => e.Kind == DemoSettlementEventKind.Emigration
                    && e.MigrationBatchId == m.BatchId);
                Assert.Contains($"依第 {batch.NewsWeek} 週傳來的消息", departure.Cause);
            }
        }

        Assert.True(departures > 0);
        Assert.True(farDepartures > 0, "Need at least one departure to a destination more than one cell away.");
    }

    [Fact]
    public void FreshNewsFromAFarSettlementHasNotArrivedYet()
    {
        // 找一個路程 ≥ 2 格的出發，用直接結算重現：只給「上一週」週報（全知時代的做法）時不得出發，
        // 給完整已發布週報（含真正傳到的那一週）時才出發。
        foreach (var seed in ManySeeds)
        {
            var cfg = Gentle;
            var probe = new DemoObserverWorld(DemoScenario.Sample(seed), cfg);
            for (var day = 1; day <= 30 * 7; day++)
            {
                probe.AdvanceOneDay();
                if (day % 7 != 0)
                    continue;
                var departed = probe.LatestWeek!.Migrations.FirstOrDefault(m => m.Departed);
                if (departed is null)
                    continue;
                var batch = probe.MigrationsInTransit().Single(b => b.Id == departed.BatchId);
                if (batch.Path.Length - 1 < 2)
                    continue;

                // 每週輸入只在週結算時改變，所以出發前一週結束時的狀態就是本週結算的輸入。
                var before = Run(seed, cfg, day - 7);
                var week = departed.Week;
                var published = before.WeekReports();
                var latestOnly = published.Where(w => w.Week == week - 1).ToArray();
                var withoutArrived = published.Where(w => w.Week != batch.NewsWeek).ToArray();

                var full = before.SettleNextWeekForTest(published);
                Assert.Contains(full.Report.Migrations, m => m.Departed && m.FromSettlementId == departed.FromSettlementId);

                // 只有上一週（太新、還在路上）的消息：該來源不能前往這個目的地。
                var onlyFresh = before.SettleNextWeekForTest(latestOnly);
                Assert.DoesNotContain(onlyFresh.Report.Migrations, m => m.Departed
                    && m.FromSettlementId == departed.FromSettlementId && m.ToSettlementId == departed.ToSettlementId);

                // 拿掉真正傳到的那一週：同樣不能依據那份消息出發。
                var noNews = before.SettleNextWeekForTest(withoutArrived);
                Assert.DoesNotContain(noNews.Report.Migrations, m => m.Departed
                    && m.FromSettlementId == departed.FromSettlementId && m.ToSettlementId == departed.ToSettlementId);
                return;
            }
        }

        Assert.Fail("No seed produced a departure to a destination at least two cells away.");
    }

    [Fact]
    public void SettleRejectsReportsFromTheCurrentOrFutureWeek()
    {
        var world = Run(42, DemoWeeklyTempConfig.Sample, 2 * 7);
        var sites = world.Sites;
        var zero = sites.Settlements.ToImmutableSortedDictionary(s => s.Id, _ => 0L);
        Assert.Throws<InvalidOperationException>(() => DemoWeeklySettlement.Settle(
            2, TimeSpan.FromDays(14), world.Map, sites,
            sites.ResourcePoints.ToImmutableSortedDictionary(p => p.Id, _ => 0L), zero, zero, zero,
            ImmutableList<DemoMigrationBatch>.Empty, new EntityIdAllocatorSnapshot(EntityIdAllocatorSnapshot.CurrentVersion, world.EntityIdNextValue, false),
            publishedWeeks: world.WeekReports(), DemoWeeklyTempConfig.Sample));
    }

    [Fact]
    public void WorldPopulationIsSettledPlusInTransitCountedOnce()
    {
        foreach (var seed in new[] { 42UL, 1UL, 7UL })
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), Gentle);
            for (var day = 1; day <= 20 * 7; day++)
            {
                world.AdvanceOneDay();
                Assert.Equal(world.Sites.Settlements.Sum(s => world.SettlementPopulation(s.Id)), world.SettledPopulation);
                Assert.Equal(world.SettledPopulation + world.PopulationInTransit, world.WorldPopulation);
                Assert.Equal(WorldPopulation(world), world.WorldPopulation);
            }
        }
    }

}
