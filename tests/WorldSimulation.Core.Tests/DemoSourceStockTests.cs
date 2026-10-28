using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 06 章 b、c 節：自然來源可及範圍反例、來源存量累積、依成年勞力比例分配。</summary>
public sealed class DemoSourceStockTests
{
    private static DemoHexWorld MapWith(DemoTerrain terrain)
    {
        // 找一張地圖上指定地形的角落或邊界格。
        for (ulong seed = 1; seed < 500; seed++)
        {
            var map = DemoHexWorld.Generate(seed);
            if (map.GetCell(0, 0).Terrain == terrain)
                return map;
        }
        throw new InvalidOperationException("找不到角落為指定地形的地圖。");
    }

    // ── b 節：可及範圍反例 ──

    [Fact]
    public void BoundaryForest_ReachesOnlyCellsThatExist()
    {
        var map = MapWith(DemoTerrain.Forest);
        var source = new DemoCatchmentFoodSource(1, 0, 0, DemoCatchmentSourceKind.Forest, 20);
        var reach = DemoHexFoodCatchment.EligibleCells(map, source);
        Assert.True(reach.Count <= 4, $"角落森林最多自身＋3 個存在的鄰格，實際 {reach.Count}");
        Assert.All(reach, c => Assert.InRange(c.Column, 0, DemoHexWorld.SmallColumns - 1));
        Assert.All(reach, c => Assert.NotEqual(DemoTerrain.Water, c.Terrain));
    }

    [Fact]
    public void MainWaterway_DoesNotFeedItself_NorTheSecondRing_NorOccupiedCells()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.ForagingSample);
        var map = world.Map;
        var sources = world.SharedFoodSources();
        var river = sources.First(s => s.Kind == DemoCatchmentSourceKind.MainRiver);
        var origin = map.GetCell(river.Column, river.Row);
        var reach = DemoHexFoodCatchment.EligibleCells(map, river);
        Assert.DoesNotContain(reach, c => c.Column == river.Column && c.Row == river.Row);
        Assert.All(reach, c => Assert.True(DemoHexFoodCatchment.IsInReach(origin, c, includeOrigin: false)));
        // 第二圈：不在六鄰的格子永遠不在範圍內。
        var secondRing = map.Cells.Where(c => !DemoHexFoodCatchment.IsInReach(origin, c, true));
        Assert.All(secondRing, c => Assert.DoesNotContain(c, reach));
        // 被聚落佔用的格子不再是自然來源。
        Assert.All(world.Sites.Settlements, s => Assert.DoesNotContain(sources, x => x.Column == s.Column && x.Row == s.Row));
        // 主河道來源＝地圖主水道（除被佔用者，主水道上不會有聚落）。
        Assert.Equal(map.MainWaterway.OrderBy(c => c.Row),
            sources.Where(s => s.Kind == DemoCatchmentSourceKind.MainRiver).Select(s => new DemoCellRef(s.Column, s.Row)).OrderBy(c => c.Row));
    }

    // ── c 節：比例分配 ──

    private static (DemoHexWorld Map, DemoCatchmentFoodSource Forest, DemoCellRef A, DemoCellRef B) TwoNeighboursOfAForest()
    {
        for (ulong seed = 1; seed < 500; seed++)
        {
            var map = DemoHexWorld.Generate(seed);
            foreach (var forest in map.Cells.Where(c => c.Terrain == DemoTerrain.Forest))
            {
                var land = map.Cells.Where(c => c.Terrain != DemoTerrain.Water
                    && DemoHexFoodCatchment.IsInReach(forest, c, false)).ToArray();
                if (land.Length >= 2)
                    return (map, new DemoCatchmentFoodSource(1, forest.Column, forest.Row, DemoCatchmentSourceKind.Forest, 20),
                        new DemoCellRef(land[0].Column, land[0].Row), new DemoCellRef(land[1].Column, land[1].Row));
            }
        }
        throw new InvalidOperationException("找不到兩個陸地鄰格的森林。");
    }

    [Fact]
    public void OneSource_IsSharedByAdultLabour_WithRemaindersByAscendingId()
    {
        var (map, forest, a, b) = TwoNeighboursOfAForest();
        // 勞力 2：1，需求都足夠大：20 → 14／6（13.33／6.67 取整 13／6，餘 1 依 ID 遞增給 A）。
        var result = DemoHexFoodCatchment.Allocate(map, new[] { forest }, new[]
        {
            new DemoCatchmentFoodClaim(1, a.Column, a.Row, 100, 40),
            new DemoCatchmentFoodClaim(2, b.Column, b.Row, 100, 20)
        });
        Assert.Equal(14, result.Harvests.Single(h => h.SettlementId == 1).FoodHarvested);
        Assert.Equal(6, result.Harvests.Single(h => h.SettlementId == 2).FoodHarvested);
        Assert.Equal(0, result.UnharvestedSourceFood[1]);

        // 一方需求小：滿了以後剩下的再分給另一方；不超過需求與勞力。
        var capped = DemoHexFoodCatchment.Allocate(map, new[] { forest }, new[]
        {
            new DemoCatchmentFoodClaim(1, a.Column, a.Row, 3, 40),
            new DemoCatchmentFoodClaim(2, b.Column, b.Row, 100, 10)
        });
        Assert.Equal(3, capped.Harvests.Single(h => h.SettlementId == 1).FoodHarvested);
        Assert.Equal(10, capped.Harvests.Single(h => h.SettlementId == 2).FoodHarvested);
        Assert.Equal(7, capped.UnharvestedSourceFood[1]);
    }

    [Fact]
    public void ProportionalSharing_NeverCausesMoreStarvationThanGreedy_ForTwoSettlementsOnOneForest()
    {
        var (map, forest, a, b) = TwoNeighboursOfAForest();
        var threshold = DemoWeeklyTempConfig.ForagingSample.HungerDebtPerStarvationDeath;
        for (long need = 4; need <= 40; need += 4)
        for (long labourA = 4; labourA <= 40; labourA += 9)
        {
            var claims = new[]
            {
                new DemoCatchmentFoodClaim(1, a.Column, a.Row, need, labourA),
                new DemoCatchmentFoodClaim(2, b.Column, b.Row, need, 40 - labourA + 4)
            };
            long Deaths(DemoCatchmentFoodResult r) => claims.Sum(c =>
                (c.FoodNeeded - r.Harvests.Where(h => h.SettlementId == c.SettlementId).Sum(h => h.FoodHarvested)) / threshold);
            var fair = DemoHexFoodCatchment.Allocate(map, new[] { forest }, claims);
            var greedy = DemoHexFoodCatchment.Allocate(map, new[] { forest }, claims, DemoCatchmentAllocation.Greedy);
            Assert.True(Deaths(fair) <= Deaths(greedy), $"需求 {need} 勞力 {labourA}：比例 {Deaths(fair)} > 貪婪 {Deaths(greedy)}");
            Assert.True(fair.TotalHarvested <= forest.FoodAvailable);
        }
    }

    // ── c 節：來源存量 ──

    [Fact]
    public void UnharvestedFood_Accumulates_UpToTheTempCap()
    {
        var cap = DemoForagingWorld.Settings.SourceStockCapWeeks;
        var source = new DemoCatchmentFoodSource(7, 0, 0, DemoCatchmentSourceKind.Forest, 20);
        var stocks = new Dictionary<int, long>();
        for (var week = 1; week <= cap + 3; week++)
        {
            var plan = DemoSourceWeekLedger.Plan(new[] { source }, stocks, cap);
            var (s, opening, regenerated, supply) = Assert.Single(plan);
            Assert.Equal(20, supply);
            Assert.Equal(Math.Min(20L * week, 20L * cap), s.FoodAvailable);
            Assert.Equal(opening + regenerated, s.FoodAvailable);
            stocks[7] = s.FoodAvailable; // 沒有人採
        }
        Assert.Equal(20L * cap, stocks[7]);
    }

    [Theory]
    [InlineData(1UL)]
    [InlineData(2UL)]
    [InlineData(42UL)]
    public void HundredYears_SourceStocksConserve_AndHarvestNeverExceedsStock(ulong seed)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.ForagingSample);
        var closing = new Dictionary<int, long>();
        long totalRegen = 0, totalHarvest = 0;
        for (var day = 1; day <= 100 * 365; day++)
        {
            if (world.AdvanceOneDay().SettledWeek is not { } week)
                continue;
            foreach (var source in week.Sources)
            {
                Assert.Equal(closing.GetValueOrDefault(source.SourceId), source.StockOpening);
                Assert.True(source.Harvested <= source.FoodAvailable);
                Assert.Equal(source.StockOpening + source.Regenerated, source.Harvested + source.StockClosing);
                totalRegen += source.Regenerated;
                totalHarvest += source.Harvested;
            }
            closing = week.Sources.Where(x => x.StockClosing > 0).ToDictionary(x => x.SourceId, x => x.StockClosing);
        }
        Assert.Equal(totalRegen, totalHarvest + closing.Values.Sum()); // 百年守恆：再生＝採收＋目前存量
    }

    [Fact]
    public void SaveLoad_KeepsSourceStocks_AndRejectsAForgedStock()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(2), DemoWeeklyTempConfig.ForagingSample);
        for (var i = 0; i < 200; i++)
            world.AdvanceOneDay();
        var json = world.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(json);
        Assert.All(world.LatestWeek!.Sources, x => Assert.Equal(world.SourceStock(x.SourceId), loaded.SourceStock(x.SourceId)));

        var doc = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        var stocks = doc["State"]!["SourceStocks"]!.AsObject();
        if (stocks.Count > 0)
        {
            var key = stocks.First().Key;
            stocks[key] = stocks[key]!.GetValue<long>() + 5;
            DemoSaveLedgerTamperTests.RecomputeFingerprint(doc);
            Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
        }
    }
}
