using System.Collections.Immutable;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-12 章：技術效果——灌溉、輪作、土壤管理提高農田供應，結網漁撈提高水域供應。</summary>
public sealed class DemoTechEffectTests
{
    [Fact]
    public void FarmingTechsRaiseFarmlandSupplyFromTheWeekAfterLearning()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        var site = world.Sites.Settlements[0];
        var home = world.Map.GetCell(site.Column, site.Row);
        var near = DemoRouteNetwork.Directions.Select(d => world.Map.AtAxial(home.AxialQ + d.Dq, home.AxialR + d.Dr)).First(c => c is not null)!;
        var farm = new DemoCatchmentFoodSource(1, near.Column, near.Row, DemoCatchmentSourceKind.Farmland, DemoLandClearing.TempFarmlandWeeklySupply);
        var forest = farm with { Kind = DemoCatchmentSourceKind.Forest };
        DemoKnowledgeRecord Learned(string id, int week)
        {
            var t = DemoTechnology.Of(id);
            return new DemoKnowledgeRecord(site.Id, t, DemoKnowledge.ThresholdOf(t), week);
        }
        var state = DemoKnowledgeState.Initial with
        {
            Records = ImmutableList.Create(Learned("TECH_IRRIGATION", 10), Learned("TECH_CROP_ROTATION", 20)),
            Era = DemoEra.Pioneer, EraWeek = 5
        };
        Assert.Equal(0, DemoKnowledge.SourcePercent(state, world.Sites, world.Map, farm, 10));
        Assert.Equal(50, DemoKnowledge.SourcePercent(state, world.Sites, world.Map, farm, 11));
        Assert.Equal(75, DemoKnowledge.SourcePercent(state, world.Sites, world.Map, farm, 21));
        Assert.Equal(0, DemoKnowledge.SourcePercent(state, world.Sites, world.Map, forest, 21));
        var boosted = DemoForagingWorld.WithPercent(new[] { farm }, s => DemoKnowledge.SourcePercent(state, world.Sites, world.Map, s, 21));
        Assert.Equal(DemoLandClearing.TempFarmlandWeeklySupply * 175 / 100, Assert.Single(boosted).FoodAvailable);
    }

    [Fact]
    public void LedgersRecordTheRealWeeklySupplyAndSavesReplayIt()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(2), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 45 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        Assert.All(world.WeekReports().SelectMany(w => w.Sources), s => Assert.True(s.WeeklySupply > 0));
        Assert.All(world.WeekReports().SelectMany(w => w.Sources).Where(s => s.Kind == DemoCatchmentSourceKind.Forest),
            s => Assert.Equal(DemoForagingWorld.Settings.WeeklySupply(DemoCatchmentSourceKind.Forest), s.WeeklySupply));
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        for (var d = 0; d < 60; d++)
        {
            world.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(world.ExportTempSave(), loaded.ExportTempSave());
    }
}
