using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-50 章：遠征軍走到地下城、每週攻一層；獸潮先出巢、走到了才打。</summary>
public sealed class DemoExpeditionTests
{
    [Fact]
    public void AnExpeditionMarchesThenAssaultsOneLayerAWeek()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        var sites = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        // 同第 12-2e 節的遠征測試：地下城離每個聚落至少 6 格、最近的不超過 9 格（不會被襲擊，遠征軍到得了）。
        var spot = world.Map.Cells.Select(c => new DemoCellRef(c.Column, c.Row))
            .Select(c => (Cell: c, Near: sites.Select(s => (Site: s, D: DemoHexWorld.Distance(c, new DemoCellRef(s.Column, s.Row)))).OrderBy(x => x.D).ThenBy(x => x.Site.Id).First()))
            .Where(x => x.Near.D is >= 6 and <= 9)
            .Select(x => ((DemoCellRef Cell, DemoSettlement Home)?)(x.Cell, x.Near.Site)).FirstOrDefault();
        Assert.NotNull(spot);
        var (cell, home) = spot!.Value;
        var dungeon = new DemoDungeon(cell, "測試洞窟", 3, 0, 99, home.Id);
        var nest = new DemoMonsterNest(dungeon, 12, false, 0) { CellId = DemoLandClearing.CellId(world.Map, dungeon.Cell) };
        var army = new DemoCombatForce(10, 5, 100);
        DemoCombatForce Force(int id) => id == home.Id ? army : DemoCombatForce.None;
        long Residents(int id) => id == home.Id ? 100 : 10; // 只有出兵的聚落夠大（別的聚落不組民兵）
        var rng = RngRegistry.Create(3);
        var none = new HashSet<int>();
        var expected = DemoMonsters.MarchWeeks(new DemoCellRef(home.Column, home.Row), cell, DemoMonsters.TempExpeditionCellsPerWeek);
        Assert.Equal((DemoHexWorld.Distance(new DemoCellRef(home.Column, home.Row), cell) + 2) / 3, expected);

        int? departed = null;
        var floors = new List<string>();
        var ended = false;
        var current = nest;
        for (var week = 1; week <= 400 && !ended; week++)
        {
            var plan = DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, Residents, new[] { current }, none, rng, Force);
            var hunt = plan.Strikes.Values.FirstOrDefault(s => s.Kind == DemoSettlementEventKind.MonsterHunt);
            if (departed is null)
            {
                Assert.Null(hunt); // 出發前、出發那一週都還沒開打
                if (plan.Departures.Count > 0)
                {
                    var d = Assert.Single(plan.Departures);
                    Assert.Equal((home.Id, DemoSettlementEventKind.ExpeditionSetOut, nest.CellId, expected), (d.SettlementId, d.Kind, d.CellId, d.Weeks));
                    Assert.Contains("遠征", d.Text, StringComparison.Ordinal);
                    departed = week;
                    var events = DemoMonsters.Events(plan, week, TimeSpan.Zero, world.Sites, 1);
                    var e = Assert.Single(events, e => e.Kind == DemoSettlementEventKind.ExpeditionSetOut);
                    Assert.Equal((nest.CellId, expected), DemoMonsters.DecodeKills(e.Count));
                }
            }
            else if (week < departed + expected)
            {
                Assert.Null(hunt); // 行軍中
                Assert.Empty(plan.Departures); // 同一個聚落不會再派第二支
            }
            else
            {
                // 到了：每週攻一層，只打這一層的怪物。
                var hit = Assert.IsType<DemoDisasterStrike>(hunt);
                Assert.Equal(home.Id, hit.SettlementId);
                var layer = week - (departed.Value + expected);
                Assert.Contains($"第 {layer + 1}/3 層", hit.Cause, StringComparison.Ordinal);
                var layersLeft = 3 - layer;
                Assert.InRange(plan.Kills.Sum(k => k.Kills), 0, (current.Monsters + layersLeft - 1) / layersLeft);
                floors.Add(hit.Cause);
                ended = plan.Cleared.Count > 0 || plan.Returns.Count > 0;
                if (layer == 2)
                    Assert.True(ended, "第三層打完一定結束");
                if (plan.Returns.Count > 0)
                {
                    var events = DemoMonsters.Events(plan, week, TimeSpan.Zero, world.Sites, 1);
                    Assert.Contains(events, e => e.Kind == DemoSettlementEventKind.ExpeditionReturned && e.Count == nest.CellId && e.SettlementId == home.Id);
                }
            }
            current = MonsterTrips.After(current with { Monsters = Math.Max(0, current.Monsters - plan.Kills.Sum(k => k.Kills)) }, plan, week);
        }
        Assert.NotNull(departed);
        Assert.True(ended);
        Assert.InRange(floors.Count, 1, 3);
        Assert.Null(current.Expedition);
    }

    [Fact]
    public void AWeakExpeditionTurnsBackAndDoesNotSetOutInVain()
    {
        // 等級太低：只能壓到下限。怪物已在下限時根本不出兵；比下限多一隻時出兵，打到殺不動就撤回（「攻不下」）。
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var home = world.Sites.Settlements.First(s => !s.IsRuin);
        var dungeon = new DemoDungeon(new DemoCellRef(home.Column, home.Row), "測試洞窟", 2, 0, 99, home.Id);
        var floor = DemoMonsters.SuppressFloor(dungeon);
        var cellId = DemoLandClearing.CellId(world.Map, dungeon.Cell);
        var weak = new DemoCombatForce(5, 1, 100);
        var rng = RngRegistry.Create(4);
        var atFloor = new DemoMonsterNest(dungeon, floor, false, 0) { CellId = cellId };
        for (var week = 1; week <= 200; week++)
            Assert.Empty(DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, _ => 100, new[] { atFloor }, new HashSet<int>(), rng, _ => weak).Departures);
        var nest = atFloor with { Monsters = floor + 1 };
        var returned = false;
        for (var week = 1; week <= 400 && !returned; week++)
        {
            var plan = DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, _ => 100, new[] { nest }, new HashSet<int>(), rng, _ => weak);
            Assert.Empty(plan.Cleared);
            if (plan.Returns.Count > 0)
            {
                Assert.Contains("攻不下", Assert.Single(plan.Returns).Text, StringComparison.Ordinal);
                Assert.Contains("/2 層", plan.Strikes.Values.Single(s => s.Kind == DemoSettlementEventKind.MonsterHunt).Cause, StringComparison.Ordinal);
                returned = true;
            }
            nest = MonsterTrips.After(nest with { Monsters = Math.Max(0, nest.Monsters - plan.Kills.Sum(k => k.Kills)) }, plan, week);
        }
        Assert.True(returned);
        Assert.Null(nest.Expedition);
    }

    [Fact]
    public void ASwarmLeavesTheLairFirstAndStrikesWhenItArrives()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var home = world.Sites.Settlements.First(s => !s.IsRuin);
        var spot = world.Map.Cells.Where(c => c.Terrain != DemoTerrain.Water && world.Sites.SettlementAt(c.Column, c.Row) is null)
            .OrderBy(c => DemoHexWorld.Distance(new DemoCellRef(c.Column, c.Row), new DemoCellRef(home.Column, home.Row))).ThenBy(c => c.Row).ThenBy(c => c.Column)
            .First();
        var dungeon = new DemoDungeon(new DemoCellRef(spot.Column, spot.Row), "測試深淵", 2, 0, 99, home.Id);
        var nest = new DemoMonsterNest(dungeon, DemoMonsters.Cap(dungeon), false, 0) { CellId = DemoLandClearing.CellId(world.Map, dungeon.Cell) };
        int? departed = null;
        var arrive = 0;
        var prey = 0;
        DemoDisasterStrike? hit = null;
        for (var week = 1; week <= 400 && hit is null; week++)
        {
            var plan = DemoMonsters.Roll(week, DemoEra.Pioneer, world.Map, world.Sites, _ => 10, new[] { nest }, new HashSet<int>(), RngRegistry.Create(11),
                tierOf: _ => DemoSettlementTier.Village, age: DemoAge.Dark, lastStand: 0);
            if (departed is null && plan.Departures.Count > 0)
            {
                var d = Assert.Single(plan.Departures);
                Assert.Equal(DemoSettlementEventKind.SwarmSetOut, d.Kind);
                var target = world.Sites.Settlements.Single(s => s.Id == d.SettlementId);
                Assert.Equal(DemoMonsters.MarchWeeks(dungeon.Cell, new DemoCellRef(target.Column, target.Row), DemoMonsters.TempSwarmCellsPerWeek), d.Weeks);
                Assert.Contains("出巢", d.Text, StringComparison.Ordinal);
                Assert.Empty(plan.Strikes); // 出巢那一週還沒打到，也不另外襲擊
                (departed, arrive, prey) = (week, week + d.Weeks, d.SettlementId);
            }
            else if (departed is not null && week < arrive)
                Assert.Empty(plan.Strikes); // 怪物都在路上
            else if (departed is not null)
            {
                Assert.Equal(arrive, week);
                hit = Assert.IsType<DemoDisasterStrike>(plan.Strikes.Values.Single(s => s.Kind == DemoSettlementEventKind.MonsterSwarm));
                Assert.Equal(prey, hit.SettlementId);
            }
            nest = MonsterTrips.After(nest, plan, week);
        }
        Assert.NotNull(hit);
        Assert.Null(nest.Swarm);
    }

    [Fact]
    public void TheFoldKnowsWhichTripsAreUnderWay()
    {
        var dungeon = new DemoDungeon(new DemoCellRef(3, 4), "測試洞窟", 3, 10, 99, 1);
        const long cell = 77;
        DemoDungeon? At(long c) => c == cell ? dungeon : null;
        DemoSettlementEvent Ev(int week, int settlement, DemoSettlementEventKind kind, long count)
            => new(week, TimeSpan.FromDays(week * 7), settlement, "測試", kind, count, "測試");
        var fold = new DemoMonsters.Fold();
        fold.Apply(Ev(20, 5, DemoSettlementEventKind.ExpeditionSetOut, DemoMonsters.EncodeKills(cell, 2)), At);
        fold.Apply(Ev(21, 6, DemoSettlementEventKind.SwarmSetOut, DemoMonsters.EncodeKills(cell, 3)), At);
        Assert.Equal(new DemoMonsterTrip(5, 20, 22), fold.At(dungeon, cell, 20).Expedition);
        Assert.Null(fold.At(dungeon, cell, 20).Swarm);
        Assert.Equal(new DemoMonsterTrip(6, 21, 24), fold.At(dungeon, cell, 21).Swarm);
        Assert.Equal(0.5f, fold.At(dungeon, cell, 21).Expedition!.Progress(21));
        // 遠征軍：抵達週 22 起每週攻一層，3 層打到第 24 週；獸潮第 24 週到。
        Assert.NotNull(fold.At(dungeon, cell, 23).Expedition);
        Assert.Null(fold.At(dungeon, cell, 24).Expedition);
        Assert.NotNull(fold.At(dungeon, cell, 23).Swarm);
        Assert.Null(fold.At(dungeon, cell, 24).Swarm);
        // 撤回就結束；殺光（沉寂）時遠征與獸潮都結束。
        fold.Apply(Ev(22, 5, DemoSettlementEventKind.ExpeditionReturned, cell), At);
        Assert.Null(fold.At(dungeon, cell, 22).Expedition);
        fold.Apply(Ev(30, 5, DemoSettlementEventKind.ExpeditionSetOut, DemoMonsters.EncodeKills(cell, 1)), At);
        fold.Apply(Ev(30, 6, DemoSettlementEventKind.SwarmSetOut, DemoMonsters.EncodeKills(cell, 4)), At);
        fold.Apply(Ev(31, 5, DemoSettlementEventKind.DungeonCleared, cell), At);
        var asleep = fold.At(dungeon, cell, 31);
        Assert.True(asleep.Dormant);
        Assert.Null(asleep.Expedition);
        Assert.Null(asleep.Swarm);
    }
}
