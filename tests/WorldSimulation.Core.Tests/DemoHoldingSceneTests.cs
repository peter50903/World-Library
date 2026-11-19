using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-33 章：領地面板（只列有蓋、正在蓋、毀損的建築；依城牆、農田、城內建築、兵營、城堡分區）。</summary>
public sealed class DemoHoldingSceneTests
{
    [Fact]
    public void ZonesFollowTheRequestedOrder()
    {
        Assert.Equal(DemoHoldingZone.Walls, DemoHoldingReport.ZoneOf(DemoBuildings.WallWood, DemoSettlementTier.Town));
        Assert.Equal(DemoHoldingZone.Walls, DemoHoldingReport.ZoneOf(DemoBuildings.StoneTower, DemoSettlementTier.City));
        Assert.Equal(DemoHoldingZone.Fields, DemoHoldingReport.ZoneOf("BUILD_FARM", DemoSettlementTier.Town));
        Assert.Equal(DemoHoldingZone.Fields, DemoHoldingReport.ZoneOf("BUILD_KITCHEN", DemoSettlementTier.Village)); // 村的磨坊在田邊
        Assert.Equal(DemoHoldingZone.Town, DemoHoldingReport.ZoneOf("BUILD_KITCHEN", DemoSettlementTier.Town));
        Assert.Equal(DemoHoldingZone.Military, DemoHoldingReport.ZoneOf("BUILD_BARRACK", DemoSettlementTier.Town));
        Assert.Equal(DemoHoldingZone.Keep, DemoHoldingReport.ZoneOf("BUILD_CASTLE", DemoSettlementTier.City));
        Assert.Equal(DemoHoldingZone.Outside, DemoHoldingReport.ZoneOf("BUILD_PORT", DemoSettlementTier.City));
        Assert.Equal("磨坊", DemoHoldingReport.Name("BUILD_KITCHEN", DemoSettlementTier.Village));
        Assert.Equal("廚房", DemoHoldingReport.Name("BUILD_KITCHEN", DemoSettlementTier.City));
        Assert.Equal("神殿", DemoHoldingReport.Name("BUILD_CHURCH", DemoSettlementTier.Town));
        // 建築表每一種都有分區與名稱。
        Assert.All(DemoBuildingCatalog.All, d => Assert.False(string.IsNullOrWhiteSpace(DemoHoldingReport.Name(d.Id, DemoSettlementTier.City))));
    }

    [Fact]
    public void RowsListEveryBuildingByZoneAndMergeIdenticalOnes()
    {
        var scene = Scene(DemoSettlementTier.Town, 9,
            new DemoBuilding(1, "BUILD_FARM", DemoBuildingStatus.Standing, 150, 1000, 3),
            new DemoBuilding(2, "BUILD_BARRACK", DemoBuildingStatus.Building, 100, 0, 9),
            new DemoBuilding(3, DemoBuildings.WallWood, DemoBuildingStatus.Standing, 60, 720, 12),
            new DemoBuilding(4, "BUILD_FARM", DemoBuildingStatus.Standing, 150, 1000, 20),
            new DemoBuilding(5, "BUILD_INN", DemoBuildingStatus.Ruined, 250, 0, 30),
            new DemoBuilding(6, "BUILD_CASTLE", DemoBuildingStatus.Building, 400, 0, 31));
        var rows = DemoHoldingReport.Rows(scene);
        Assert.Equal(new[] { DemoHoldingZone.Walls, DemoHoldingZone.Fields, DemoHoldingZone.Town, DemoHoldingZone.Military, DemoHoldingZone.Keep },
            rows.Select(r => r.Zone).ToArray());
        Assert.Equal(6, rows.Sum(r => r.Count));
        Assert.Equal(2, rows.Single(r => r.DefId == "BUILD_FARM").Count);
        Assert.Equal(72, rows.Single(r => r.DefId == DemoBuildings.WallWood).Percent);
        Assert.Equal(25, rows.Single(r => r.DefId == "BUILD_BARRACK").Percent);
        Assert.Equal(10, rows.Single(r => r.DefId == "BUILD_CASTLE").Percent);
        var lines = DemoHoldingReport.Lines(scene);
        Assert.Contains("── 城牆與塔樓 ──", lines);
        Assert.Contains("・木牆：受損（耐久 72%）", lines);
        Assert.Contains("・附近已開墾的農田 9 塊", lines);
        Assert.Contains("・農舍 ×2：完成", lines);
        Assert.Contains("・旅店：毀損（耐久度歸零）", lines);
        Assert.Contains("・兵營：施工中 25%", lines);
        Assert.StartsWith("建築格 5/16", lines[0], StringComparison.Ordinal); // 城牆不佔格
        // 分區順序：城牆、農田、城內建築、兵營、城堡。
        var headers = lines.Where(l => l.StartsWith("── ", StringComparison.Ordinal)).ToList();
        Assert.Equal(new[] { "── 城牆與塔樓 ──", "── 農田與農舍 ──", "── 城內建築 ──", "── 兵營與訓練場 ──", "── 城堡與官署 ──" }, headers);
    }

    [Fact]
    public void SettlementDetailHasAHoldingTabForEveryLivingSettlement()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1, null, DemoMapSize.Small), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 4 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var before = world.StateFingerprint();
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin && world.SettlementPopulation(s.Id) > 0).ToList();
        Assert.NotEmpty(alive);
        foreach (var s in alive)
        {
            var scene = Assert.IsType<DemoHoldingScene>(world.HoldingScene(s.Id));
            Assert.Equal(world.ChronicleOf(s.Id).Tier, scene.Tier);
            Assert.Equal(world.SettlementPopulation(s.Id), scene.Residents);
            Assert.Equal(scene.Holding.Buildings.Count, DemoHoldingReport.Rows(scene).Sum(r => r.Count));
            var tab = world.PlayerDetail(s.Column, s.Row).Tabs.Single(t => t.Title == "領地");
            Assert.Equal(s.Id, tab.HoldingId);
            Assert.Contains(tab.Lines, l => l.StartsWith("住宅 ", StringComparison.Ordinal) || l.StartsWith("棚屋 ", StringComparison.Ordinal));
        }
        Assert.Contains(alive, s => world.HoldingScene(s.Id)!.Holding.Buildings.Count > 0);
        Assert.Null(world.HoldingScene(int.MaxValue));
        Assert.Equal(before, world.StateFingerprint()); // 只讀
    }

    private static DemoHoldingScene Scene(DemoSettlementTier tier, int fields, params DemoBuilding[] buildings)
        => new(new DemoSettlement(1, "測試鎮", 3, 3), tier,
            DemoHolding.EmptyFor(1) with { Buildings = buildings, Dwellings = 10, Wood = 5 },
            fields, new DemoBuildingNearby(true, false, false, false), DemoBuildingCatalog.Get(DemoBuildings.HouseBasic), 48);
}
