using System.Collections.Immutable;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-9 章 C1：野生動物（只在野地、開拓時代出現、每年繁殖到承載）與狩獵（12a 第 1～2 節）。</summary>
public sealed class DemoWildlifeTests
{
    [Fact]
    public void HerdsAppearInTheWildWhenThePioneerEraBegins_AndGrowToCapacityYearly()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        var wild = DemoWildlife.WildCells(world.Map, world.Sites);
        Assert.NotEmpty(wild);
        foreach (var s in world.Sites.Settlements)
        {
            var origin = world.Map.GetCell(s.Column, s.Row);
            Assert.All(wild, c => Assert.True(DemoWildlife.Distance(origin, c) >= 2)); // 生活圈＝本格與六鄰格
        }
        Assert.Same(DemoWildlifeState.Empty, DemoWildlife.Advance(DemoWildlifeState.Empty, world.Map, world.Sites, DemoEra.Hope, 10));
        var spawned = DemoWildlife.Advance(DemoWildlifeState.Empty, world.Map, world.Sites, DemoEra.Pioneer, 10);
        Assert.Equal(10, spawned.AppearedWeek);
        Assert.Equal(wild.Count, spawned.Cells.Count);
        Assert.All(spawned.Cells, kv => Assert.Equal(DemoWildlife.GrazerCapacity(DemoWildlife.CellOf(world.Map, kv.Key)), kv.Value.Grazers));

        // 每年：草食＋20%（至少 1）到承載；掠食不超過鄰近草食的五分之一。
        var id = spawned.Cells.Keys.First();
        var thinned = spawned with { Cells = spawned.Cells.SetItem(id, new DemoHerd(1, 50)) };
        var yearly = DemoWildlife.Advance(thinned, world.Map, world.Sites, DemoEra.Pioneer, 52);
        Assert.Equal(2, yearly.At(id)!.Grazers);
        var cell = DemoWildlife.CellOf(world.Map, id);
        var prey = yearly.At(id)!.Grazers + DemoRouteNetwork.Directions.Select(d => world.Map.AtAxial(cell.AxialQ + d.Dq, cell.AxialR + d.Dr))
            .Where(n => n is not null).Sum(n => yearly.At(DemoWildlife.CellId(world.Map, n!))?.Grazers ?? 0);
        Assert.True(yearly.At(id)!.Predators <= prey / DemoWildlife.TempGrazersPerPredator);
        Assert.Equal(spawned, DemoWildlife.Advance(spawned, world.Map, world.Sites, DemoEra.Pioneer, 53)); // 非年末不變
    }

    [Fact]
    public void HuntersTakeGrazersNearbyAndTurnThemIntoFood()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        var population = DemoCohortDemography.Restore(world.HumanPopulationSnapshot!);
        var herds = DemoWildlife.Advance(DemoWildlifeState.Empty, world.Map, world.Sites, DemoEra.Pioneer, 10);
        var hunter = world.Sites.Settlements.First(s => DemoWildlife.AnimalsNear(herds, world.Map, s)
            && population.AdultsIn(s.Id, DemoHumanLifecycle.Rules) >= DemoWildlife.TempAdultsPerHunter);
        var knows = DemoKnowledgeState.Initial with
        {
            Records = ImmutableList.Create(new DemoKnowledgeRecord(hunter.Id, DemoWildlife.Hunting, DemoKnowledge.ThresholdOf(DemoWildlife.Hunting), 5)),
            Era = DemoEra.Pioneer, EraWeek = 5
        };
        var (none, noFood) = DemoWildlife.Hunt(herds, world.Map, world.Sites, population, DemoKnowledgeState.Initial);
        Assert.Empty(noFood);
        Assert.Equal(herds, none);
        var (after, food) = DemoWildlife.Hunt(herds, world.Map, world.Sites, population, knows);
        var expected = population.AdultsIn(hunter.Id, DemoHumanLifecycle.Rules) / DemoWildlife.TempAdultsPerHunter;
        Assert.Equal(expected * DemoWildlife.TempFoodPerGrazer, food[hunter.Id]);
        Assert.Equal(herds.TotalGrazers - expected, after.TotalGrazers); // 獵物真的減少（守恆）
        // 有獸群的聚落：獸產大類取得得到，狩獵分支有從業者。
        Assert.True(DemoResourceCategories.CategoryAvailable(DemoResourceCategory.Animal, hunter, world.Map, 10, animalsNear: true));
        Assert.True(DemoKnowledge.Practitioners(DemoTechTree.Of(DemoWildlife.Hunting), hunter, population, world.Map, 10, knows, animalsNear: true) > 0);
        Assert.Equal(0, DemoKnowledge.Practitioners(DemoTechTree.Of(DemoWildlife.Hunting), hunter, population, world.Map, 10, knows, animalsNear: false));
    }

    [Fact]
    public void WildlifeIsSavedAndShownAfterThePioneerEraBegins()
    {
        DemoObserverWorld? world = null;
        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
            for (var day = 0; day < 60 * 365 && world.Wildlife.AppearedWeek == 0; day++)
                world.AdvanceOneDay();
            if (world.Wildlife.AppearedWeek > 0) break;
        }
        Assert.True(world!.Wildlife.AppearedWeek > 0);
        Assert.Equal(DemoEra.Pioneer, world.Era);
        var (id, herd) = world.Wildlife.Cells.First();
        var cell = DemoWildlife.CellOf(world.Map, id);
        Assert.Contains(world.QueryCell(cell.Column, cell.Row).Lines, l => l.StartsWith("野生動物：", StringComparison.Ordinal));
        Assert.Contains(DemoWorldOverview.Of(world, 365).Lines(), l => l.StartsWith("野生動物：", StringComparison.Ordinal));
        for (var day = 0; day < 2 * 365; day++)
            world.AdvanceOneDay();
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.Wildlife, loaded.Wildlife);
        for (var day = 0; day < 30; day++)
        {
            world.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(world.ExportTempSave(), loaded.ExportTempSave());
        Assert.All(world.WeekReports().SelectMany(w => w.Settlements), l => Assert.True(l.FoodBalanced));
    }
}
