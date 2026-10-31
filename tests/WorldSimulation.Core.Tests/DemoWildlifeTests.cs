using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 12-1 章 C1：野生動物的承載、年結算、狩獵加成與存檔。</summary>
public sealed class DemoWildlifeTests
{
    [Fact]
    public void WildlifeStaysWithinCapacityAndSettlementsEmptyTheirCells()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var opening = world.Wildlife;
        Assert.Equal(world.Map.Cells.Count, opening.Prey.Length);
        for (var d = 0; d < 30 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var w = world.Wildlife;
        Assert.True(w.LastYearWeek >= 52 * 29);
        foreach (var (cell, i) in world.Map.Cells.Select((c, i) => (c, i)))
        {
            var cap = DemoWildlife.Capacity(cell);
            Assert.InRange(w.Prey[i], 0, cap.Prey);
            Assert.InRange(w.Wolves[i], 0, cap.Wolves);
        }
        foreach (var s in world.Sites.Settlements.Where(s => !s.IsRuin))
            Assert.Equal(0, w.Prey[s.Row * world.Map.Columns + s.Column]);
        Assert.Contains(world.Sites.Settlements.Where(s => !s.IsRuin),
            s => DemoPlayerDetail.Of(world, s.Column, s.Row).Tabs[0].Lines.Any(l => l.StartsWith("附近獵物：", StringComparison.Ordinal)));
        // 讀檔依狩獵名單重算來源帳；不一致時讀檔失敗。
        var reloaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(w.Fingerprint(), reloaded.Wildlife.Fingerprint());
    }

    [Fact]
    public void HuntingBonusAppliesFromTheWeekAfterTheYearlySettlement()
    {
        var state = new DemoWildlifeState(System.Collections.Immutable.ImmutableArray<int>.Empty, System.Collections.Immutable.ImmutableArray<int>.Empty, 0)
        {
            Hunting = System.Collections.Immutable.ImmutableList.Create(new DemoHuntingYear(52, System.Collections.Immutable.ImmutableList.Create(3)))
        };
        Assert.False(state.HuntingAt(3, 52));
        Assert.True(state.HuntingAt(3, 53));
        Assert.False(state.HuntingAt(4, 53));
    }
}

/// <summary>第 12-1 章 C2：狼群襲擊途中隊伍。</summary>
public sealed class DemoBeastAttackTests
{
    [Fact]
    public void AttacksNeedWolvesAreDeterministicAndArcheryHalvesTheChance()
    {
        var map = DemoHexWorld.Generate(3);
        var cell = map.Cells.First(c => c.Terrain == DemoTerrain.Forest);
        var index = cell.Row * map.Columns + cell.Column;
        var none = System.Collections.Immutable.ImmutableArray.CreateRange(new int[map.Cells.Count]);
        var wolves = none.SetItem(index, 2);
        var batch = new DemoMigrationBatch(7, 1, "甲", 2, "乙", System.Collections.Immutable.ImmutableArray.Create(new DemoCellRef(cell.Column, cell.Row)), 0, 5, 0, 0, 1);
        var rng = WorldSimulation.Core.Rng.RngRegistry.Create(42);
        Assert.Equal(0, DemoWildlife.AttackDeaths(new DemoWildlifeState(none, none, 0), map, batch, 10, rng, false));
        var state = new DemoWildlifeState(none, wolves, 0);
        var hits = Enumerable.Range(1, 2000).Select(w => DemoWildlife.AttackDeaths(state, map, batch, w, rng, false)).ToList();
        var archers = Enumerable.Range(1, 2000).Select(w => DemoWildlife.AttackDeaths(state, map, batch, w, rng, true)).ToList();
        Assert.All(hits, d => Assert.InRange(d, 0, 2));
        Assert.InRange(hits.Count(d => d > 0), 40, 130);     // 4% × 2000 ≈ 80
        Assert.InRange(archers.Count(d => d > 0), 15, 70);   // 2% × 2000 ≈ 40
        Assert.Equal(hits, Enumerable.Range(1, 2000).Select(w => DemoWildlife.AttackDeaths(state, map, batch, w, rng, false)));
    }

    [Fact]
    public void AttackDeathsKeepTheTravelLedgerBalanced()
    {
        var ledger = new DemoMigrationWeekLedger(5, 7, 1, 2, new DemoCellRef(0, 0), new DemoCellRef(1, 0), false, false,
            10, 0, 8, 20, 20, 0, 0, 0, 0, 0, 0, AttackDeaths: 2);
        Assert.True(ledger.PopulationBalanced);
        Assert.True(ledger.NonNegative);
    }
}
