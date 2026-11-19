using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-37 章：建築的作用（研究、存糧、施工、士兵、醫護、城堡）。</summary>
public sealed class DemoBuildingEffectsTests
{
    private static DemoBuilding B(int id, string defId, DemoBuildingStatus status)
        => new(id, defId, status, 0, status == DemoBuildingStatus.Standing ? 1000 : 0, 1);

    [Fact]
    public void OnlyStandingBuildingsGiveTheirBonuses()
    {
        Assert.Equal(DemoBuildingBonus.None, DemoBuildingBonus.Of(null));
        Assert.Equal(DemoBuildingBonus.None, DemoBuildingBonus.Of(DemoHolding.EmptyFor(1)));
        Assert.False(DemoBuildingBonus.None.Any);
        Assert.Null(DemoBuildingBonus.None.Line());
        var holding = DemoHolding.EmptyFor(1) with
        {
            Buildings = new[]
            {
                B(1, "BUILD_SCHOOL", DemoBuildingStatus.Standing), B(2, "BUILD_SCRIPTORIUM", DemoBuildingStatus.Building),
                B(3, "BUILD_GRANARY", DemoBuildingStatus.Standing), B(4, "BUILD_GRANARY", DemoBuildingStatus.Standing),
                B(5, "BUILD_WORKSHOP", DemoBuildingStatus.Ruined), B(6, "BUILD_LUMBERYARD", DemoBuildingStatus.Standing),
                B(7, "BUILD_BARRACK", DemoBuildingStatus.Standing), B(8, "BUILD_ARCHERY_RANGE", DemoBuildingStatus.Standing),
                B(9, "BUILD_CLINIC", DemoBuildingStatus.Standing), B(10, "BUILD_HOSPITAL", DemoBuildingStatus.Standing)
            }
        };
        var bonus = DemoBuildingBonus.Of(holding);
        Assert.Equal(DemoBuildingBonus.TempSchoolResearch, bonus.ResearchPercent); // 抄寫室還在蓋
        Assert.Equal(DemoBuildingBonus.TempGranarySpoilageCut, bonus.SpoilageCutPercent); // 兩座糧倉只算一次
        Assert.Equal(0, bonus.BuildPercent); // 工坊毀損
        Assert.Equal((true, false, false), (bonus.FastWood, bonus.FastStone, bonus.FastIron));
        Assert.Equal(DemoBuildingBonus.TempBarrackSoldiers + DemoBuildingBonus.TempRangeSoldiers, bonus.SoldierPercent);
        Assert.Equal(DemoBuildingBonus.TempHospitalHeal, bonus.HealPercent); // 醫院與診療所取高者
        Assert.Equal("建築加成：研究 +15%、存糧腐敗 −50%、採木工時減半、士兵 +30%、怪物害死的人 −30%", bonus.Line());
        Assert.Equal("研究 +15%", DemoBuildingBonus.EffectOf("BUILD_SCHOOL"));
        Assert.Null(DemoBuildingBonus.EffectOf("BUILD_FARM"));
        Assert.Null(DemoBuildingBonus.EffectOf(DemoBuildings.WallStone)); // 城牆看城防

        // 領地列表：完好的那一列寫作用，施工中、毀損的不寫。
        var scene = new DemoHoldingScene(new DemoSettlement(1, "測試鎮", 3, 3), DemoSettlementTier.Town, holding, 0,
            new DemoBuildingNearby(true, false, false, false), DemoBuildingCatalog.Get(DemoBuildings.HouseBasic), 48);
        var lines = DemoHoldingReport.Lines(scene);
        Assert.Contains("── 建築加成 ──", lines);
        Assert.Contains("・學校：完成｜研究 +15%", lines);
        Assert.Contains("・抄寫室：施工中 0%", lines);
        Assert.Contains("・工坊：毀損（耐久度歸零）", lines);
        Assert.Contains("・糧倉 ×2：完成｜存糧腐敗 −50%", lines);
    }

    [Fact]
    public void CastlesHoldOutEvenWithoutWalls()
    {
        var castle = DemoDefense.Of(DemoHolding.EmptyFor(1) with { Buildings = new[] { B(1, "BUILD_CASTLE", DemoBuildingStatus.Standing) } });
        Assert.True(castle.Any);
        Assert.True(castle.Castle);
        Assert.Equal((DemoDefense.TempCastleMonsterPercent, 100 - DemoDefense.TempCastleWarCut, DemoDefense.TempCastleSiegeWeeks - DemoDefense.TempOpenSiegeWeeks),
            (castle.MonsterPercent, castle.WarDamagePercent, castle.SiegeWeeks));
        Assert.StartsWith("城防（城堡）：", castle.Describe(), StringComparison.Ordinal);
        var walled = DemoDefense.Of(DemoHolding.EmptyFor(1) with
        {
            Buildings = new[]
            {
                B(1, "BUILD_CASTLE", DemoBuildingStatus.Standing), B(2, DemoBuildings.WallStone, DemoBuildingStatus.Standing),
                B(3, DemoBuildings.GateStone, DemoBuildingStatus.Standing)
            }
        });
        Assert.Equal((DemoDefense.TempStoneWallPercent + DemoDefense.TempCastleMonsterPercent, true,
                100 - DemoDefense.TempStoneWallWarCut - DemoDefense.TempCastleWarCut, DemoDefense.TempStoneSiegeWeeks + DemoDefense.TempCastleSiegeWeeks),
            (walled.MonsterPercent, walled.HoldsSwarm, walled.WarDamagePercent, walled.SiegeWeeks));
        Assert.StartsWith("城防（城堡、石牆）：", walled.Describe(), StringComparison.Ordinal);
        // 毀損的城堡沒有作用。
        Assert.False(DemoDefense.Of(DemoHolding.EmptyFor(1) with { Buildings = new[] { B(1, "BUILD_CASTLE", DemoBuildingStatus.Ruined) } }).Any);
    }

    [Fact]
    public void BarracksRaiseTheGarrisonOnlyWhereThereAreSoldiers()
    {
        static bool None(string _) => false;
        var plain = DemoCombat.Force(1000, DemoSettlementTier.Town, None);
        var drilled = DemoCombat.Force(1000, DemoSettlementTier.Town, None, soldierPercent: 30);
        Assert.Equal(plain.Soldiers * 130 / 100, drilled.Soldiers);
        Assert.Equal(1000 - drilled.Soldiers, drilled.Civilians);
        Assert.Equal(plain.SoldierLevel, drilled.SoldierLevel);
        Assert.Equal(0, DemoCombat.Force(1000, DemoSettlementTier.Village, None, soldierPercent: 30).Soldiers); // 村莊沒有駐軍
    }

    [Fact]
    public void WorkshopsAndResourceYardsBuildFaster()
    {
        var map = DemoHexWorld.Generate(3, DemoMapSize.Medium);
        var land = map.Cells.First(c => c.Terrain == DemoTerrain.Grassland && !c.Sea && c.Column > 2 && c.Row > 2);
        var site = new DemoSettlement(1, "測試村", land.Column, land.Row);
        int Built(params DemoBuilding[] buildings)
        {
            var state = new DemoBuildingsState(new[] { DemoHolding.EmptyFor(site.Id) with { Buildings = buildings, Dwellings = 20 } }, 20);
            for (var week = 1; week <= 300; week++)
            {
                state = DemoBuildings.Advance(state, new DemoBuildingWeek(week, map, new[] { site }, _ => 40, _ => 20, _ => DemoSettlementTier.Village,
                    (_, _) => true, (_, f) => f.Name == "鐵匠鋪", _ => 0, _ => 0, _ => 0, _ => false, Array.Empty<DemoSettlementEvent>(), RngRegistry.Create(7)));
                if (state.Of(site.Id)!.Buildings.Any(b => b.DefId == "BUILD_SMITHY" && b.Status == DemoBuildingStatus.Standing))
                    return week;
            }
            return int.MaxValue;
        }
        // 同樣三棟建築，有工坊、木材場、採石場的那邊鐵匠鋪蓋得比較快（採料與施工都快）。
        var fast = Built(B(1, "BUILD_WORKSHOP", DemoBuildingStatus.Standing), B(2, "BUILD_LUMBERYARD", DemoBuildingStatus.Standing),
            B(3, "BUILD_QUARRY", DemoBuildingStatus.Standing));
        var slow = Built(B(1, "BUILD_KILN", DemoBuildingStatus.Standing), B(2, "BUILD_TANNERY", DemoBuildingStatus.Standing),
            B(3, "BUILD_CLINIC", DemoBuildingStatus.Standing));
        Assert.True(slow < int.MaxValue, "鐵匠鋪蓋不起來");
        Assert.True(fast < slow, $"有工坊 {fast} 週、沒有 {slow} 週");
    }

    [Fact]
    public void HealersSaveSomeOfTheMonsterVictims()
    {
        // 同一個地下城、同一串運氣：有醫院的聚落被怪物害死的人比較少（至少還是死 1 人）。
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var home = world.Sites.Settlements.First(s => !s.IsRuin);
        var spot = world.Map.Cells.Where(c => c.Terrain != DemoTerrain.Water && world.Sites.SettlementAt(c.Column, c.Row) is null)
            .OrderBy(c => DemoHexWorld.Distance(new DemoCellRef(c.Column, c.Row), new DemoCellRef(home.Column, home.Row))).ThenBy(c => c.Row).ThenBy(c => c.Column)
            .First();
        var dungeon = new DemoDungeon(new DemoCellRef(spot.Column, spot.Row), "測試洞窟", 3, 0, 99, home.Id);
        var nest = new DemoMonsterNest(dungeon, 12, false, 0) { CellId = DemoLandClearing.CellId(world.Map, dungeon.Cell) };
        var rng = RngRegistry.Create(11);
        long Deaths(Func<int, int>? heal)
        {
            long total = 0;
            for (var week = 1; week <= 400; week++)
                total += DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, _ => 400, new[] { nest }, new HashSet<int>(), rng,
                    healPercent: heal).Strikes.Values.Where(x => x.Kind is DemoSettlementEventKind.MonsterRaid or DemoSettlementEventKind.MonsterSwarm)
                    .Sum(x => x.Deaths);
            return total;
        }
        var plain = Deaths(null);
        var healed = Deaths(_ => DemoBuildingBonus.TempHospitalHeal);
        Assert.True(plain > 0, "怪物沒有害死任何人");
        Assert.True(healed < plain, $"有醫院 {healed} 人、沒有 {plain} 人");
    }

    [Fact]
    public void RealGranariesCutTheWeeklySpoilage()
    {
        // 中地圖跑到有聚落蓋好糧倉（最多四十年）；再推一週，那一週的腐敗照建築加成少一半，其他聚落照舊。
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        for (var year = 0; year < 40 && !world.Buildings.Holdings.Any(h => DemoBuildingBonus.Of(h).SpoilageCutPercent > 0); year++)
            for (var d = 0; d < TestCalendar.DaysPerYear; d++)
                world.AdvanceOneDay();
        var cut = world.Sites.Settlements.ToDictionary(s => s.Id, s => world.BuildingBonus(s.Id).SpoilageCutPercent);
        Assert.Contains(cut.Values, c => c > 0);
        var weeks = world.WeekReports().Count;
        while (world.WeekReports().Count == weeks)
            world.AdvanceOneDay();
        foreach (var ledger in world.WeekReports()[^1].Settlements)
        {
            var spoils = (long)Math.Floor(ledger.FoodOpening * DemoSurvivalTempSettings.FoodLossPerWeek);
            Assert.Equal(spoils - spoils * cut.GetValueOrDefault(ledger.SettlementId) / 100, ledger.FoodSpoiled);
        }
        // 詳情頁看得到。
        var granary = world.Sites.Settlements.First(s => !s.IsRuin && world.BuildingBonus(s.Id).Any);
        Assert.Contains(DemoPlayerDetail.Of(world, granary.Column, granary.Row).Tabs[0].Lines, l => l.StartsWith("建築加成：", StringComparison.Ordinal));
        Assert.Empty(world.BuildCausalIndex().Problems);
    }
}
