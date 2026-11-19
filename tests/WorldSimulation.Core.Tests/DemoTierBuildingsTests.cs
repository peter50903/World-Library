using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-36 章：等級看真的建築（解鎖＋蓋好才升級、建築還在就保留等級、本級設施保留建築格、升級進度）。</summary>
public sealed class DemoTierBuildingsTests
{
    private static DemoTierFacility Facility(string name)
        => Enum.GetValues<DemoSettlementTier>().SelectMany(t => DemoSettlementHistory.FacilitiesOf(t)).First(f => f.Name == name);

    private static DemoBuilding B(int id, string defId, DemoBuildingStatus status, int progress = 0)
        => new(id, defId, status, progress, status == DemoBuildingStatus.Standing ? 1000 : 0, 1);

    [Fact]
    public void FacilitiesCountOnlyWhenTheirBuildingsStand()
    {
        var holding = DemoHolding.EmptyFor(1) with
        {
            Buildings = new[]
            {
                B(1, "BUILD_WORKSHOP", DemoBuildingStatus.Standing, 180),
                B(2, "BUILD_KITCHEN", DemoBuildingStatus.Building, 40),
                B(3, "BUILD_TANNERY", DemoBuildingStatus.Ruined),
                B(4, "BUILD_CHURCH", DemoBuildingStatus.Standing, 600),
                B(5, DemoBuildings.WallStone, DemoBuildingStatus.Standing, 100)
            }
        };
        Assert.True(DemoBuildings.FacilityBuilt(holding, Facility("木工坊"), standingOnly: true));
        Assert.False(DemoBuildings.FacilityBuilt(holding, Facility("磨坊"), standingOnly: true)); // 還在蓋
        Assert.True(DemoBuildings.FacilityBuilt(holding, Facility("磨坊"), standingOnly: false));
        Assert.False(DemoBuildings.FacilityBuilt(holding, Facility("獵屋"), standingOnly: true)); // 毀損
        Assert.True(DemoBuildings.FacilityBuilt(holding, Facility("獵屋"), standingOnly: false)); // 廢墟還在
        Assert.True(DemoBuildings.FacilityBuilt(holding, Facility("城牆"), standingOnly: true)); // 石牆也是城牆
        Assert.True(DemoBuildings.FacilityBuilt(holding, Facility("神殿"), standingOnly: true));
        Assert.False(DemoBuildings.FacilityBuilt(holding, Facility("大神殿"), standingOnly: false)); // 要第 2 座神殿
        Assert.False(DemoBuildings.FacilityBuilt((DemoHolding?)null, Facility("木工坊"), standingOnly: false));
    }

    [Fact]
    public void TiersRiseOnFinishedBuildingsAndRuinsDoNotDropThem()
    {
        var camp = DemoSettlementTier.Camp;
        var village = DemoSettlementTier.Village;
        bool Unlocked(DemoTierFacility f) => f.Name is "木工坊" or "磨坊";
        Func<DemoTierFacility, bool> Has(DemoHolding h) => f => Unlocked(f) && DemoBuildings.FacilityBuilt(h, f, standingOnly: true);
        Func<DemoTierFacility, bool> Keeps(DemoHolding h) => f => DemoBuildings.FacilityBuilt(h, f, standingOnly: false);

        // 有木匠、農師（解鎖）但磨坊還在蓋：仍是營地。
        var building = DemoHolding.EmptyFor(1) with
        {
            Buildings = new[] { B(1, "BUILD_WORKSHOP", DemoBuildingStatus.Standing, 180), B(2, "BUILD_KITCHEN", DemoBuildingStatus.Building, 40) }
        };
        Assert.Equal(camp, DemoSettlementHistory.TierFor(30, Has(building), camp, null, Keeps(building)));
        // 磨坊蓋好：升村莊。
        var done = building with { Buildings = new[] { building.Buildings[0], B(2, "BUILD_KITCHEN", DemoBuildingStatus.Standing, 80) } };
        Assert.Equal(village, DemoSettlementHistory.TierFor(30, Has(done), camp, null, Keeps(done)));
        // 獵屋蓋好了但沒有獵人（沒解鎖）：不算。
        var hut = DemoHolding.EmptyFor(1) with
        {
            Buildings = new[] { B(1, "BUILD_WORKSHOP", DemoBuildingStatus.Standing, 180), B(3, "BUILD_TANNERY", DemoBuildingStatus.Standing, 100) }
        };
        Assert.Equal(camp, DemoSettlementHistory.TierFor(30, Has(hut), camp, null, Keeps(hut)));
        // 村莊的工坊、磨坊被怪物打毀：建築還在（等著重建），不降級；人口不夠才降。
        var ruined = done with { Buildings = done.Buildings.Select(b => b with { Status = DemoBuildingStatus.Ruined, Durability = 0 }).ToArray() };
        Assert.Equal(village, DemoSettlementHistory.TierFor(30, Has(ruined), village, null, Keeps(ruined)));
        Assert.Equal(camp, DemoSettlementHistory.TierFor(7, Has(ruined), village, null, Keeps(ruined)));
        // 沒有給保留規則：跟升級用同一個（第 08-11 章的舊行為）。
        Assert.Equal(camp, DemoSettlementHistory.TierFor(30, Has(ruined), village));
    }

    [Fact]
    public void SlotsAreKeptForTheFacilitiesOfTheCurrentTier()
    {
        var (map, site) = Place();
        var unlocked = new HashSet<string> { "木工坊", "磨坊" };
        var state = new DemoBuildingsState(new[] { DemoHolding.EmptyFor(site.Id) with { Dwellings = 40, Wood = 5000, Stone = 5000, Iron = 500 } }, 1);
        // 村莊想蓋的很多（農舍、兵營、靶場、馬廄、診療所、糧倉……），但村莊級設施還沒解鎖：留 2 格給它們。
        for (var week = 1; week <= 300; week++)
            state = DemoBuildings.Advance(state, Week(map, site, week, unlocked));
        var holding = state.Of(site.Id)!;
        var slots = DemoBuildings.Slots(DemoSettlementTier.Village);
        Assert.Equal(slots - 2, holding.Buildings.Count(b => !DemoBuildings.IsWallwork(b.DefId)));
        Assert.Equal(2, DemoBuildings.Reserve(holding.Buildings, DemoSettlementTier.Village, f => unlocked.Contains(f.Name)));
        // 有了鐵匠與貨幣（市集、鐵匠鋪解鎖）：蓋進保留的格子。
        unlocked.UnionWith(new[] { "市集", "鐵匠鋪" });
        for (var week = 301; week <= 600; week++)
            state = DemoBuildings.Advance(state, Week(map, site, week, unlocked));
        holding = state.Of(site.Id)!;
        Assert.Equal(slots, holding.Buildings.Count(b => !DemoBuildings.IsWallwork(b.DefId)));
        Assert.Contains(holding.Buildings, b => b.DefId == "BUILD_MARKET" && b.Status == DemoBuildingStatus.Standing);
        Assert.Contains(holding.Buildings, b => b.DefId == "BUILD_SMITHY" && b.Status == DemoBuildingStatus.Standing);
        Assert.Equal(0, DemoBuildings.Reserve(holding.Buildings, DemoSettlementTier.Village, f => unlocked.Contains(f.Name)));
    }

    [Fact]
    public void RuinsAreRebuiltInPlaceEvenWhenEverySlotIsTaken()
    {
        var (map, site) = Place();
        var unlocked = new HashSet<string> { "木工坊", "磨坊", "市集", "鐵匠鋪" };
        var full = new[]
        {
            B(1, "BUILD_WORKSHOP", DemoBuildingStatus.Standing, 180), B(2, "BUILD_KITCHEN", DemoBuildingStatus.Standing, 80),
            B(3, "BUILD_MARKET", DemoBuildingStatus.Standing, 220), B(4, "BUILD_SMITHY", DemoBuildingStatus.Ruined),
            B(5, "BUILD_FARM", DemoBuildingStatus.Standing, 100), B(6, "BUILD_BARRACK", DemoBuildingStatus.Standing, 100),
            B(7, "BUILD_CLINIC", DemoBuildingStatus.Standing, 100), B(8, "BUILD_GRANARY", DemoBuildingStatus.Standing, 100)
        };
        Assert.Equal(DemoBuildings.Slots(DemoSettlementTier.Village), full.Length);
        var state = new DemoBuildingsState(new[] { DemoHolding.EmptyFor(site.Id) with { Buildings = full, Dwellings = 40, Wood = 500, Stone = 500, Iron = 50 } }, 9);
        for (var week = 1; week <= 60; week++)
            state = DemoBuildings.Advance(state, Week(map, site, week, unlocked));
        var holding = state.Of(site.Id)!;
        Assert.Equal(DemoBuildingStatus.Standing, holding.Buildings.Single(b => b.Id == 4).Status); // 鐵匠鋪在原地重建
        Assert.Equal(full.Length, holding.Buildings.Count);
    }

    [Fact]
    public void TierProgressShowsWhatIsStillMissing()
    {
        var holding = DemoHolding.EmptyFor(1) with
        {
            Buildings = new[] { B(1, "BUILD_MARKET", DemoBuildingStatus.Standing, 220), B(2, "BUILD_SMITHY", DemoBuildingStatus.Building, 90) }
        };
        var progress = DemoTierProgress.Of(DemoSettlementTier.Village, 47, holding,
            role => role == DemoRepresentativeRole.Smith, tech => tech == "TECH_CURRENCY")!;
        Assert.Equal(DemoSettlementTier.Town, progress.Next);
        Assert.Equal(60, progress.NeedPopulation);
        Assert.Equal(1, progress.Done);
        Assert.False(progress.Ready);
        Assert.Null(progress.Kingship);
        Assert.Equal("升為城鎮：人口 47/60、本級設施 1/2", progress.Headline);
        var steps = progress.Steps.ToDictionary(s => s.Facility.Name);
        Assert.Equal(DemoTierStepState.Done, steps["市集"].State);
        Assert.Equal((DemoTierStepState.Building, 50), (steps["鐵匠鋪"].State, steps["鐵匠鋪"].Percent));
        Assert.Equal((DemoTierStepState.Locked, "學者"), (steps["抄寫所"].State, steps["抄寫所"].Lacking));
        Assert.Equal(DemoTierStepState.Locked, steps["陶窯"].State);
        Assert.Equal("鐵匠鋪：施工中 50%", steps["鐵匠鋪"].Line);
        Assert.Equal("抄寫所：缺學者", steps["抄寫所"].Line);
        Assert.StartsWith("陶窯：缺「", steps["陶窯"].Line, StringComparison.Ordinal);

        // 城市升首都另看王權；大神殿＝第 2 座神殿；圖書館就是高等學院。
        var city = DemoTierProgress.Of(DemoSettlementTier.City, 400, null, _ => true, _ => true)!;
        Assert.True(city.Kingship);
        var cityLines = city.Steps.Select(s => s.Line).ToList();
        Assert.Contains("大神殿（第 2 座神殿）：可以開工", cityLines);
        Assert.Contains("圖書館（高等學院）：可以開工", cityLines);
        Assert.Null(DemoTierProgress.Of(DemoSettlementTier.Capital, 900, null, _ => true, _ => true));
    }

    [Fact]
    public void RealWorldsOnlyRiseWithTheBuildingsStandingAndShowTheProgress()
    {
        // 中地圖二十五年：每個村莊以上的聚落，前一級的設施至少兩種有建築（廢墟、重建中也算）；領地分頁看得到升級進度。
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 25 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin && world.SettlementPopulation(s.Id) > 0).ToList();
        Assert.NotEmpty(alive);
        foreach (var s in alive)
        {
            var tier = world.ChronicleOf(s.Id).Tier;
            if (tier > DemoSettlementTier.Camp)
                Assert.True(DemoSettlementHistory.FacilitiesOf(tier - 1).Count(f => DemoBuildings.FacilityBuilt(world.HoldingOf(s.Id), f, standingOnly: false))
                    >= DemoSettlementHistory.TempFacilitiesNeeded, $"{s.Name}是{DemoSettlementHistory.TierName(tier)}，前一級設施的建築卻不到兩種");
            var progress = world.TierProgress(s.Id);
            Assert.Equal(tier == DemoSettlementTier.Capital, progress is null);
            if (progress is not null)
                Assert.Contains(progress.Headline, world.HoldingScene(s.Id)!.Progress!.Headline);
        }
        Assert.Contains(alive, s => world.TierProgress(s.Id) is not null
            && DemoHoldingReport.Lines(world.HoldingScene(s.Id)!).Any(l => l.StartsWith("── 升為", StringComparison.Ordinal)));
        Assert.Empty(world.BuildCausalIndex().Problems);
    }

    private static (DemoHexWorld Map, DemoSettlement Site) Place()
    {
        var map = DemoHexWorld.Generate(3, DemoMapSize.Medium);
        var land = map.Cells.First(c => c.Terrain == DemoTerrain.Grassland && !c.Sea && c.Column > 2 && c.Row > 2);
        return (map, new DemoSettlement(1, "測試村", land.Column, land.Row));
    }

    /// <summary>一個村莊：什麼技術都會、兵多田多、存糧充足；等級設施只有 <paramref name="unlocked"/> 解鎖。</summary>
    private static DemoBuildingWeek Week(DemoHexWorld map, DemoSettlement site, int week, IReadOnlySet<string> unlocked)
        => new(week, map, new[] { site },
            _ => 120,
            _ => 80,
            _ => DemoSettlementTier.Village,
            (_, _) => true,
            (_, f) => unlocked.Contains(f.Name),
            _ => 400,
            _ => 30,
            _ => 100000,
            _ => false,
            Array.Empty<DemoSettlementEvent>(),
            RngRegistry.Create(7));
}
