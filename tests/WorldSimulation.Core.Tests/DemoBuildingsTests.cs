using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-32 章：正式建築系統第一版（建材＋工時建成、耐久度、破壞與廢墟）。</summary>
public sealed class DemoBuildingsTests
{
    [Fact]
    public void CatalogReadsTheFormalBuildingSheet()
    {
        Assert.Equal(44, DemoBuildingCatalog.All.Count);
        var house = DemoBuildingCatalog.Get(DemoBuildings.HouseBasic);
        Assert.Equal((20, 10, 0, 120, 5), (house.Wood, house.Stone, house.Iron, house.Hours, house.Capacity));
        Assert.Contains("TECH_BASIC_CONSTRUCTION", house.Techs);
        Assert.Equal(DemoBuildings.HouseLarge, house.UpgradeTo);
        Assert.Equal(4000, DemoBuildingCatalog.Get("BUILD_CASTLE").Hours);
        Assert.Contains("TECH_MASONRY", DemoBuildingCatalog.Get(DemoBuildings.WallStone).Techs);
        // 等級設施對應的建築都在建築表裡。
        Assert.All(DemoBuildings.FacilityBuildings.Values, m => Assert.True(DemoBuildingCatalog.Contains(m.DefId), m.DefId));
    }

    [Fact]
    public void VillageBuildsHomesForNewcomersFromGatheredMaterials()
    {
        var (map, site) = Place();
        var residents = 30L;
        var state = DemoBuildingsState.Empty;
        for (var week = 1; week <= 400; week++)
        {
            if (week == 2)
                residents = 60; // 有人搬進來：住宅不夠，要蓋
            state = DemoBuildings.Advance(state, Week(map, site, week, residents, DemoSettlementTier.Village));
        }
        var holding = Assert.IsType<DemoHolding>(state.Of(site.Id));
        Assert.True(holding.Housing >= 60, $"住得下 {holding.Housing} 人");
        Assert.True(holding.Wood >= 0 && holding.Stone >= 0 && holding.Iron >= 0);
        // 村莊的等級設施（鐵匠鋪、抄寫所…）都蓋起來，佔建築格。
        Assert.Contains(holding.Buildings, b => b.DefId == "BUILD_SMITHY" && b.Status == DemoBuildingStatus.Standing);
        Assert.True(holding.Buildings.Count(b => !DemoBuildings.IsWallwork(b.DefId)) <= DemoBuildings.Slots(DemoSettlementTier.Village));
    }

    [Fact]
    public void TownsRaiseWallsAndAGate()
    {
        var (map, site) = Place();
        var state = DemoBuildingsState.Empty;
        for (var week = 1; week <= 300; week++)
            state = DemoBuildings.Advance(state, Week(map, site, week, 250, DemoSettlementTier.Town));
        var holding = state.Of(site.Id)!;
        Assert.Contains(holding.Buildings, b => b.DefId == DemoBuildings.WallWood && b.Status == DemoBuildingStatus.Standing);
        Assert.Contains(holding.Buildings, b => b.DefId == DemoBuildings.GateWood && b.Status == DemoBuildingStatus.Standing);
        Assert.Equal(holding.Buildings.Count, holding.Buildings.Select(b => b.Id).Distinct().Count());
        // 同一個輸入得到同一個結果。
        var again = DemoBuildingsState.Empty;
        for (var week = 1; week <= 300; week++)
            again = DemoBuildings.Advance(again, Week(map, site, week, 250, DemoSettlementTier.Town));
        Assert.Equal(state.Fingerprint(), again.Fingerprint());
    }

    [Fact]
    public void RaidsHitTheWallsFirstAndARuinedSettlementLeavesRuins()
    {
        var (map, site) = Place();
        var wall = new DemoBuilding(1, DemoBuildings.WallWood, DemoBuildingStatus.Standing, 60, 1000, 3);
        var smithy = new DemoBuilding(2, "BUILD_SMITHY", DemoBuildingStatus.Standing, 180, 1000, 4);
        var state = new DemoBuildingsState(new[] { DemoHolding.EmptyFor(site.Id) with { Buildings = new[] { wall, smithy }, Dwellings = 10 } }, 3);
        var raid = Event(site, DemoSettlementEventKind.MonsterRaid, 10, 77);
        var after = DemoBuildings.Advance(state, Week(map, site, 10, 0, DemoSettlementTier.Town, raid)).Of(site.Id)!;
        Assert.True(after.Buildings.Single(b => b.Id == 1).Durability < 1000, "城牆沒有受損");
        Assert.Equal(1000, after.Buildings.Single(b => b.Id == 2).Durability);
        Assert.Equal(10, after.Dwellings); // 有城牆擋著，住宅沒事

        var ruin = Event(site, DemoSettlementEventKind.SettlementRuined, 11, 78);
        var ruined = DemoBuildings.Advance(new DemoBuildingsState(new[] { after }, 3), Week(map, site, 11, 0, DemoSettlementTier.Town, ruin)).Of(site.Id)!;
        Assert.All(ruined.Buildings, b => Assert.Equal((DemoBuildingStatus.Ruined, 0), (b.Status, b.Durability)));
        Assert.Equal((0, 0, 10), (ruined.Dwellings, ruined.LargeDwellings, ruined.RuinedDwellings));
    }

    [Fact]
    public void DamagedBuildingsAreRepairedAndRuinsRebuiltUnderTheSameNumber()
    {
        var (map, site) = Place();
        var smithy = new DemoBuilding(5, "BUILD_SMITHY", DemoBuildingStatus.Ruined, 0, 0, 2);
        var kiln = new DemoBuilding(6, "BUILD_KILN", DemoBuildingStatus.Standing, 140, 400, 2);
        var state = new DemoBuildingsState(new[] { DemoHolding.EmptyFor(site.Id) with { Buildings = new[] { smithy, kiln }, Dwellings = 20 } }, 7);
        for (var week = 3; week <= 320; week++)
            state = DemoBuildings.Advance(state, Week(map, site, week, 60, DemoSettlementTier.Village));
        var holding = state.Of(site.Id)!;
        Assert.Equal(1000, holding.Buildings.Single(b => b.Id == 6).Durability);
        Assert.Equal(DemoBuildingStatus.Standing, holding.Buildings.Single(b => b.Id == 5).Status); // 鐵匠鋪在原地重建
        Assert.DoesNotContain(holding.Buildings, b => b.Id != 5 && b.DefId == "BUILD_SMITHY");
    }

    [Fact]
    public void WorldsKeepTheirBuildingsAcrossSaveAndLoad()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1, null, DemoMapSize.Small), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 3 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin && world.SettlementPopulation(s.Id) > 0).ToList();
        Assert.NotEmpty(alive);
        Assert.All(alive, s => Assert.True(world.HoldingOf(s.Id)?.Housing > 0, $"{s.Name}沒有住宅"));
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.Buildings.Fingerprint(), loaded.Buildings.Fingerprint());
        Assert.Equal(world.StateFingerprint(), loaded.StateFingerprint());
    }

    private static (DemoHexWorld Map, DemoSettlement Site) Place()
    {
        var map = DemoHexWorld.Generate(3, DemoMapSize.Medium);
        var land = map.Cells.First(c => c.Terrain == DemoTerrain.Grassland && !c.Sea && c.Column > 2 && c.Row > 2);
        return (map, new DemoSettlement(1, "測試村", land.Column, land.Row));
    }

    private static DemoSettlementEvent Event(DemoSettlement site, DemoSettlementEventKind kind, int week, long eventId)
        => new(week, TimeSpan.FromDays(week * 7), site.Id, site.Name, kind, 1, "測試", EventId: eventId);

    /// <summary>一個什麼技術都會、各級設施都有的聚落（成年人＝居民的一半）。</summary>
    private static DemoBuildingWeek Week(DemoHexWorld map, DemoSettlement site, int week, long residents, DemoSettlementTier tier,
        params DemoSettlementEvent[] events)
        => new(week, map, new[] { site },
            _ => residents,
            _ => residents / 2,
            _ => tier,
            (_, _) => true,
            (_, f) => DemoSettlementHistory.FacilitiesOf(tier).Contains(f) || DemoSettlementHistory.FacilitiesOf(tier - 1 < DemoSettlementTier.Camp ? tier : tier - 1).Contains(f),
            _ => 0,
            _ => 0,
            _ => 0,
            _ => false,
            events,
            RngRegistry.Create(7));
}
