using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-34 章：建築的作用第一版——城牆、城門、塔樓讓守方佔便宜（怪物來襲、獸潮、大軍攻城）。</summary>
public sealed class DemoDefenseTests
{
    private static DemoHolding Holding(params DemoBuilding[] buildings) => DemoHolding.EmptyFor(1) with { Buildings = buildings };

    private static DemoBuilding B(int id, string defId, int durability = 1000, DemoBuildingStatus status = DemoBuildingStatus.Standing)
        => new(id, defId, status, 0, durability, 1);

    [Fact]
    public void WallsGatesAndTowersAddUp()
    {
        Assert.Equal(DemoDefense.None, DemoDefense.Of(null));
        Assert.False(DemoDefense.Of(Holding()).Any);
        Assert.Equal(-DemoDefense.TempOpenSiegeWeeks, DemoDefense.Of(Holding()).SiegeWeeks);

        var wood = DemoDefense.Of(Holding(B(1, DemoBuildings.WallWood), B(2, DemoBuildings.GateWood)));
        Assert.Equal((40, true, 80, 1), (wood.MonsterPercent, wood.HoldsSwarm, wood.WarDamagePercent, wood.SiegeWeeks));
        Assert.Contains("獸潮攻不破", wood.Describe(), StringComparison.Ordinal);

        // 沒有城門：一半，獸潮攻得破。
        var open = DemoDefense.Of(Holding(B(1, DemoBuildings.WallWood)));
        Assert.Equal((20, false), (open.MonsterPercent, open.HoldsSwarm));

        // 石牆耐久一半、兩座石造塔樓、一座前哨塔。
        var stone = DemoDefense.Of(Holding(B(1, DemoBuildings.WallStone, 500), B(2, DemoBuildings.GateStone),
            B(3, DemoBuildings.StoneTower), B(4, DemoBuildings.StoneTower), B(5, DemoBuildings.Outpost)));
        Assert.Equal((40 + 25, true, 100 - 17, 3, 3), (stone.MonsterPercent, stone.HoldsSwarm, stone.WarDamagePercent, stone.SiegeWeeks, stone.Towers));

        // 城牆耐久不到三成：守不住獸潮；毀損、施工中的城牆不算。
        Assert.False(DemoDefense.Of(Holding(B(1, DemoBuildings.WallStone, 250), B(2, DemoBuildings.GateStone))).HoldsSwarm);
        Assert.Null(DemoDefense.Of(Holding(B(1, DemoBuildings.WallStone, 0, DemoBuildingStatus.Ruined), B(2, DemoBuildings.GateStone))).Wall);
        Assert.Null(DemoDefense.Of(Holding(B(1, DemoBuildings.WallWood, 0, DemoBuildingStatus.Building))).Wall);
        // 塔樓最多算四座。
        var towers = DemoDefense.Of(Holding(Enumerable.Range(1, 6).Select(i => B(i, DemoBuildings.StoneTower)).ToArray()));
        Assert.Equal((4 * DemoDefense.TempStoneTowerPercent, 4), (towers.MonsterPercent, towers.Towers));
    }

    [Fact]
    public void DefendersBehindWallsKillMoreAndDieLess()
    {
        var force = new DemoCombatForce(10, 1, 200);
        for (var luck = 0; luck < 100; luck += 7)
        {
            var open = DemoCombat.Clash(30, 2, force, defending: true, luck);
            var walled = DemoCombat.Clash(30, 2, force, defending: true, luck, 80);
            Assert.True(walled.Kills >= open.Kills, $"luck {luck}");
            Assert.True(walled.Deaths <= open.Deaths, $"luck {luck}");
        }
        // 出城討伐不算城牆。
        Assert.Equal(DemoCombat.Clash(30, 2, force, defending: false, 50), DemoCombat.Clash(30, 2, force, defending: false, 50, 80));
    }

    [Fact]
    public void AStandingWallHoldsASwarmThatWouldWipeOutTheVillage()
    {
        // 同第 12-2h 章的獸潮測試：沒有城牆的村莊會全村覆滅；城牆耐久三成以上、城門完好時只能死守。
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var home = world.Sites.Settlements.First(s => !s.IsRuin);
        var spot = world.Map.Cells.Where(c => c.Terrain != DemoTerrain.Water && world.Sites.SettlementAt(c.Column, c.Row) is null)
            .OrderBy(c => DemoHexWorld.Distance(new DemoCellRef(c.Column, c.Row), new DemoCellRef(home.Column, home.Row))).ThenBy(c => c.Row).ThenBy(c => c.Column)
            .First();
        var dungeon = new DemoDungeon(new DemoCellRef(spot.Column, spot.Row), "測試深淵", 2, 0, 99, home.Id);
        var full = new DemoMonsterNest(dungeon, DemoMonsters.Cap(dungeon), false, 0) { CellId = DemoLandClearing.CellId(world.Map, dungeon.Cell) };
        var walls = DemoDefense.Of(Holding(B(1, DemoBuildings.WallWood), B(2, DemoBuildings.GateWood)));
        DemoDisasterStrike? Swarm(Func<int, DemoDefense>? defense)
        {
            var current = full;
            for (var week = 1; week <= 400; week++)
            {
                var p = DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, _ => 10, new[] { current }, new HashSet<int>(), RngRegistry.Create(11),
                    tierOf: _ => DemoSettlementTier.Village, age: DemoAge.Dark, lastStand: 0, defense: defense);
                if (p.Strikes.Values.FirstOrDefault(s => s.Kind == DemoSettlementEventKind.MonsterSwarm) is { } hit)
                    return hit;
                current = MonsterTrips.After(current, p, week); // 第 08-50 章：獸潮先出巢
            }
            return null;
        }
        var wiped = Assert.IsType<DemoDisasterStrike>(Swarm(null));
        Assert.True(wiped.Annihilate);
        var held = Assert.IsType<DemoDisasterStrike>(Swarm(id => id == wiped.SettlementId ? walls : DemoDefense.None));
        Assert.Equal(wiped.SettlementId, held.SettlementId);
        Assert.False(held.Annihilate);
        Assert.Contains("據城牆死守", held.Cause, StringComparison.Ordinal);
        Assert.InRange(held.Deaths, 1, 10 * DemoMonsters.TempSwarmDeathPercent / 100);
    }

    [Fact]
    public void TheWorldUsesItsOwnBuildingsForDefense()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1, null, DemoMapSize.Small), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        foreach (var s in world.Sites.Settlements)
            Assert.Equal(DemoDefense.Of(world.HoldingOf(s.Id)), world.Defense(s.Id));
    }
}
