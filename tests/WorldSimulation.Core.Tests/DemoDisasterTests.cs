using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 12-1 章 C3a：天災的時機、地理前提、頻率與存檔。</summary>
public sealed class DemoDisasterTests
{
    [Fact]
    public void WorldPoolGivesAtMostOneDisasterPerYearFollowsEraAndGeography()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var ids = world.Sites.Settlements.Select(s => s.Id).ToList();
        var rng = RngRegistry.Create(5);
        int Week(int year) => year * DemoWildlife.TempYearWeeks + DemoDisasters.TempYearWeek;
        Assert.Empty(DemoDisasters.Roll(Week(3) + 1, DemoEra.Pioneer, 1000, world.Map, world.Sites, ids, rng));
        var all = new List<DemoDisasterStrike>();
        for (var year = 1; year <= 1000; year++)
        {
            Assert.Empty(DemoDisasters.Roll(Week(year), DemoEra.Hope, 1000, world.Map, world.Sites, ids, rng)); // 希望時代空池
            var strikes = DemoDisasters.Roll(Week(year), DemoEra.Pioneer, 1000, world.Map, world.Sites, ids, rng);
            Assert.InRange(strikes.Count, 0, 1);
            Assert.Equal(strikes, DemoDisasters.Roll(Week(year), DemoEra.Pioneer, 1000, world.Map, world.Sites, ids, rng));
            all.AddRange(strikes.Values);
            // 規模門檻：世界人口不足 300 時沒有地震與隕石。
            Assert.DoesNotContain(DemoDisasters.Roll(Week(year), DemoEra.Pioneer, 299, world.Map, world.Sites, ids, rng).Values,
                x => x.Kind is DemoSettlementEventKind.Earthquake or DemoSettlementEventKind.Meteorite);
        }
        // 抽中後 30% 才發生，且要有合格聚落：千年遠少於 300 件（不重抽）。
        Assert.InRange(all.Count, 50, 300);
        foreach (var strike in all)
            Assert.True(DemoDisasters.Eligible(strike.Kind, world.Map, world.Sites.Settlements.First(x => x.Id == strike.SettlementId)));
    }

    [Fact]
    public void FrequencyDoesNotGrowWithTheNumberOfSettlements()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var rng = RngRegistry.Create(9);
        var all = world.Sites.Settlements.Select(s => s.Id).ToList();
        var few = all.Take(2).ToList();
        int Count(IReadOnlyList<int> ids) => Enumerable.Range(1, 600).Count(y =>
            DemoDisasters.Roll(y * DemoWildlife.TempYearWeeks + DemoDisasters.TempYearWeek, DemoEra.Pioneer, 1000, world.Map, world.Sites, ids, rng)
                .Values.Any(x => x.Kind == DemoSettlementEventKind.Meteorite));
        Assert.Equal(Count(all), Count(few)); // 隕石任何聚落都合格：聚落多寡不改變年數
    }

    [Fact]
    public void LivingWorldRecordsDisastersAsChronicleEventsAndSavesStayConsistent()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 60 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var lines = world.Sites.Settlements.SelectMany(s => world.ChronicleOf(s.Id).Milestones).Select(m => m.Text).ToList();
        Assert.Contains(lines, l => l.StartsWith("洪水", StringComparison.Ordinal) || l.StartsWith("地震", StringComparison.Ordinal)
            || l.StartsWith("龍捲風", StringComparison.Ordinal) || l.StartsWith("隕石", StringComparison.Ordinal));
        Assert.Empty(world.BuildCausalIndex().Problems);
        var reloaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.ExportTempSave(), reloaded.ExportTempSave());
    }
}

/// <summary>第 12-1 章：隕石落點留下礦脈。</summary>
public sealed class DemoMeteoriteOreTests
{
    [Fact]
    public void MeteoriteLeavesOreNextToTheStruckSettlementAndUnlocksMining()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var s = world.Sites.Settlements.First();
        var meteor = new DemoSettlementEvent(30, TimeSpan.FromDays(210), s.Id, s.Name, DemoSettlementEventKind.Meteorite, 1, "隕石", EventId: 900);
        var strikes = new Dictionary<int, DemoDisasterStrike> { [s.Id] = new(s.Id, DemoSettlementEventKind.Meteorite, 1, "隕石") };
        var events = DemoDisasters.MeteoriteOre(strikes, new[] { meteor }, world.Map, world.Sites, new HashSet<DemoCellRef>(), 30,
            TimeSpan.FromDays(210), RngRegistry.Create(1), 901);
        var ore = Assert.Single(events);
        Assert.Equal(DemoSettlementEventKind.MeteoriteOre, ore.Kind);
        Assert.Equal(900, ore.CauseEventId);
        var cell = DemoLandClearing.CellOf(world.Map, ore.Count);
        Assert.Equal(1, DemoWorldSites.HexDistance(world.Map.GetCell(s.Column, s.Row), world.Map.GetCell(cell.Column, cell.Row)));
        Assert.NotEqual(DemoTerrain.Water, world.Map.GetCell(cell.Column, cell.Row).Terrain);
        var set = DemoDisasters.OreCells(events, world.Map).ToHashSet();
        Assert.True(DemoKnowledge.NearOre(world.Map, s, set));
        // 同一格不會再落一次礦脈。
        Assert.DoesNotContain(DemoDisasters.MeteoriteOre(strikes, new[] { meteor }, world.Map, world.Sites, set, 30, TimeSpan.FromDays(210), RngRegistry.Create(1), 950),
            e => DemoLandClearing.CellOf(world.Map, e.Count) == cell);
    }
}

/// <summary>第 12-1 章 C3b：熱浪與野火是一段期間的環境狀態。</summary>
public sealed class DemoEnvironmentTests
{
    [Fact]
    public void SpellsCutTheirSourcesOnlyDuringTheirPeriod()
    {
        var cell = new DemoCellRef(2, 3);
        var heat = new DemoEnvironmentSpell(DemoSettlementEventKind.Heatwave, 1, 26, 78, 500, System.Collections.Immutable.ImmutableList.Create(cell));
        var fire = new DemoEnvironmentSpell(DemoSettlementEventKind.Wildfire, 1, 26, 182, 501, System.Collections.Immutable.ImmutableList.Create(new DemoCellRef(4, 4)));
        var grass = new DemoCatchmentFoodSource(1, 2, 3, DemoCatchmentSourceKind.GrasslandForage, 8);
        var forest = new DemoCatchmentFoodSource(2, 4, 4, DemoCatchmentSourceKind.Forest, 20); // 野火燒的格（08-13：也燒草原，與熱浪分開測）
        var spells = new[] { heat, fire };
        Assert.Equal(0, DemoEnvironment.SourcePercent(spells, grass, 26));
        Assert.Equal(DemoEnvironment.TempHeatwaveSourcePercent, DemoEnvironment.SourcePercent(spells, grass, 27));
        Assert.Equal(DemoEnvironment.TempHeatwaveSourcePercent, DemoEnvironment.SourcePercent(spells, grass, 78));
        Assert.Equal(0, DemoEnvironment.SourcePercent(spells, grass, 79));
        Assert.Equal(DemoEnvironment.TempWildfireSourcePercent, DemoEnvironment.SourcePercent(spells, forest, 100));
        Assert.Equal(0, DemoEnvironment.SourcePercent(spells, forest with { Column = 5 }, 100));
        var reduced = DemoForagingWorld.WithPercent(new[] { forest }, s => DemoEnvironment.SourcePercent(spells, s, 100));
        Assert.Equal(2, Assert.Single(reduced).FoodAvailable);
        var settlement = new DemoSettlement(1, "甲", 2, 3);
        Assert.Equal(500, DemoEnvironment.HeatwaveCause(spells, settlement, 40));
        Assert.Equal(0, DemoEnvironment.HeatwaveCause(spells, settlement, 80));
    }

    [Fact]
    public void LivingWorldHeatwavesAndWildfiresStartEndAndReplay()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 70 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var spells = world.Wildlife.Spells;
        Assert.NotEmpty(spells);
        var events = world.SettlementEvents();
        foreach (var spell in spells.Where(x => x.ToWeek <= world.CommittedWeeks
            && x.Kind is DemoSettlementEventKind.Heatwave or DemoSettlementEventKind.Wildfire))
            Assert.Contains(events, e => e.CauseEventId == spell.EventId
                && e.Kind is DemoSettlementEventKind.HeatwaveEnded or DemoSettlementEventKind.ForestRegrown);
        Assert.Empty(world.BuildCausalIndex().Problems);
        var reloaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.ExportTempSave(), reloaded.ExportTempSave());
    }
}

/// <summary>第 08-16 章第 14 節：砍樹與挖礦。</summary>
public sealed class DemoLoggingMiningTests
{
    private static int YearWeek(int year) => year * DemoWildlife.TempYearWeeks + DemoDisasters.TempYearWeek;

    [Fact]
    public void LoggedForestYieldsLessUntilItGrowsBack()
    {
        var cell = new DemoCellRef(4, 4);
        var cut = new DemoEnvironmentSpell(DemoSettlementEventKind.Logging, 1, 26, 26 + DemoEnvironment.TempLoggingWeeks, 600,
            System.Collections.Immutable.ImmutableList.Create(cell));
        var forest = new DemoCatchmentFoodSource(2, 4, 4, DemoCatchmentSourceKind.Forest, 20);
        var grass = new DemoCatchmentFoodSource(3, 4, 4, DemoCatchmentSourceKind.GrasslandForage, 8);
        var spells = new[] { cut };
        Assert.Equal(0, DemoEnvironment.SourcePercent(spells, forest, 26));
        Assert.Equal(DemoEnvironment.TempLoggedSourcePercent, DemoEnvironment.SourcePercent(spells, forest, 27));
        Assert.Equal(0, DemoEnvironment.SourcePercent(spells, grass, 27));
        Assert.Equal(0, DemoEnvironment.SourcePercent(spells, forest, 27 + DemoEnvironment.TempLoggingWeeks));
        Assert.Equal(-1f, DemoEnvironment.LoggedAt(spells, cell, 26));
        Assert.InRange(DemoEnvironment.LoggedAt(spells, cell, 30), 0f, 0.01f);
        Assert.InRange(DemoEnvironment.LoggedAt(spells, cell, 26 + DemoEnvironment.TempLoggingWeeks), 0.99f, 1f);
    }

    [Fact]
    public void SettlementsCutNearbyForestAndMineOreUntilItIsExhausted()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var s = world.Sites.Settlements.First();
        var origin = world.Map.GetCell(s.Column, s.Row);
        var vein = world.Map.Cells.First(c => DemoWorldSites.HexDistance(origin, c) == 1);
        var ore = new HashSet<DemoCellRef> { new(vein.Column, vein.Row) };
        var ids = new[] { s.Id };
        var week = YearWeek(5);
        var (state, events) = DemoEnvironment.Advance(world.Wildlife, week, TimeSpan.FromDays(week * 7), DemoEra.Pioneer, world.Map, world.Sites,
            ids, RngRegistry.Create(3), 1000, residents: _ => 500, logs: _ => true, mines: _ => true, extraOre: ore);
        var mining = Assert.Single(events, e => e.Kind == DemoSettlementEventKind.MiningBegan);
        var at2 = DemoLandClearing.CellOf(world.Map, mining.Count); // 最近的礦脈（天然的或給的）
        Assert.True(world.Map.GetCell(at2.Column, at2.Row).Ore || ore.Contains(at2));
        var hasForest = world.Map.Cells.Any(c => c.Terrain == DemoTerrain.Forest && DemoWorldSites.HexDistance(origin, c) <= DemoEnvironment.TempLoggingRange
            && world.Sites.SettlementAt(c.Column, c.Row) is null);
        if (hasForest)
        {
            // 住民 500 人：每年 90% 會砍；十年內一定有。
            var logging = Enumerable.Range(5, 10).SelectMany(y => DemoEnvironment.Advance(world.Wildlife, YearWeek(y), TimeSpan.FromDays(YearWeek(y) * 7),
                DemoEra.Pioneer, world.Map, world.Sites, ids, RngRegistry.Create(3), 1000, residents: _ => 500, logs: _ => true, mines: _ => false).Events)
                .First(e => e.Kind == DemoSettlementEventKind.Logging);
            var at = DemoLandClearing.CellOf(world.Map, logging.Count);
            Assert.Equal(DemoTerrain.Forest, world.Map.GetCell(at.Column, at.Row).Terrain);
            Assert.InRange(DemoWorldSites.HexDistance(origin, world.Map.GetCell(at.Column, at.Row)), 1, DemoEnvironment.TempLoggingRange);
        }
        // 沒有會木工、探礦的聚落：什麼都不做。
        Assert.DoesNotContain(DemoEnvironment.Advance(world.Wildlife, week, TimeSpan.FromDays(week * 7), DemoEra.Pioneer, world.Map, world.Sites,
            ids, RngRegistry.Create(3), 1000, residents: _ => 500, logs: _ => false, mines: _ => false, extraOre: ore).Events,
            e => e.Kind is DemoSettlementEventKind.Logging or DemoSettlementEventKind.MiningBegan);
        // 開採中仍有礦脈；期滿挖空，挖空的不再算礦脈。
        Assert.Empty(DemoEnvironment.ExhaustedOre(state.Spells, week + 1));
        Assert.InRange(DemoEnvironment.MiningAt(state.Spells, at2, week + 1), 0f, 0.01f);
        var end = week + DemoEnvironment.TempMiningWeeks;
        var (after, endEvents) = DemoEnvironment.Advance(state, end, TimeSpan.FromDays(end * 7), DemoEra.Pioneer, world.Map, world.Sites,
            ids, RngRegistry.Create(3), 2000, residents: _ => 500, logs: _ => true, mines: _ => true, extraOre: ore);
        var done = Assert.Single(endEvents, e => e.Kind == DemoSettlementEventKind.OreExhausted);
        Assert.Equal(mining.EventId, done.CauseEventId);
        Assert.Contains(at2, DemoEnvironment.ExhaustedOre(after.Spells, end + 1));
        Assert.Equal(2f, DemoEnvironment.MiningAt(after.Spells, at2, end + 1));
        if (!world.Map.Cells.Any(c => c.Ore && DemoWorldSites.HexDistance(origin, c) <= DemoKnowledge.TempOreRange))
        {
            Assert.True(DemoKnowledge.NearOre(world.Map, s, ore));
            Assert.False(DemoKnowledge.NearOre(world.Map, s, ore, DemoEnvironment.ExhaustedOre(after.Spells, end + 1)));
        }
        // 同一處礦脈不會再開採。
        var nextYear = YearWeek(5 + DemoEnvironment.TempMiningWeeks / DemoWildlife.TempYearWeeks + 1);
        Assert.DoesNotContain(DemoEnvironment.Advance(after, nextYear, TimeSpan.FromDays(nextYear * 7), DemoEra.Pioneer, world.Map, world.Sites,
            ids, RngRegistry.Create(3), 3000, residents: _ => 500, logs: _ => true, mines: _ => true, extraOre: ore).Events,
            e => e.Kind == DemoSettlementEventKind.MiningBegan && e.Count == mining.Count);
    }

    [Fact]
    public void OldLoggingIsPrunedButMinesAreKept()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var s = world.Sites.Settlements.First();
        var cut = new DemoEnvironmentSpell(DemoSettlementEventKind.Logging, s.Id, 26, 26 + DemoEnvironment.TempLoggingWeeks, 600,
            System.Collections.Immutable.ImmutableList.Create(new DemoCellRef(1, 1)));
        var mine = new DemoEnvironmentSpell(DemoSettlementEventKind.MiningBegan, s.Id, 26, 26 + DemoEnvironment.TempMiningWeeks, 601,
            System.Collections.Immutable.ImmutableList.Create(new DemoCellRef(2, 2)));
        var state = world.Wildlife with { Spells = System.Collections.Immutable.ImmutableList.Create(cut, mine) };
        var week = 26 + DemoEnvironment.TempMiningWeeks + 1;
        var (after, _) = DemoEnvironment.Advance(state, week, TimeSpan.FromDays(week * 7), DemoEra.Pioneer, world.Map, world.Sites,
            Array.Empty<int>(), RngRegistry.Create(3), 1000);
        Assert.Equal(new[] { DemoSettlementEventKind.MiningBegan }, after.Spells.Select(x => x.Kind));
    }
}
