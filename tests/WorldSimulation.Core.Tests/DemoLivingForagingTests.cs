using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>#105: births and age mortality are published by the same playable Core world,
/// not a separate mock cohort or an invented annual population growth curve.</summary>
public sealed class DemoLivingForagingTests
{
    [Fact]
    public void RealHumanPregnanciesProduceNewWorldPopulationAndChildrenAreNotGatherers()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42),
            DemoWeeklyTempConfig.LivingForagingSample);
        Assert.NotNull(world.HumanPopulationSnapshot);
        var initial = world.WorldPopulation;
        for (var day = 0; day < 3 * 365; day++) world.AdvanceOneDay();
        var reports = world.WeekReports();
        // 第 07 章 b 節：遷徙隊伍途中的出生、餓死、自然死亡也計入世界人口。
        var births = reports.Sum(w => w.Settlements.Sum(l => l.Births) + w.Migrations.Sum(m => m.Births));
        var starvation = reports.Sum(w => w.Settlements.Sum(l => l.Deaths - l.NaturalDeaths) + w.Migrations.Sum(m => m.Deaths));
        var ageDeaths = reports.Sum(w => w.Settlements.Sum(l => l.NaturalDeaths) + w.Migrations.Sum(m => m.NaturalDeaths));
        Assert.True(births > 0, "The playable world must contain actual births, not Births:0 forever.");
        Assert.Equal(initial + births - starvation - ageDeaths, world.WorldPopulation);
        Assert.All(reports, w => Assert.All(w.Settlements, l =>
        {
            Assert.True(l.IsBalanced);
            Assert.InRange(l.AdultWorkersOpening, 0, l.PopulationOpening);
            Assert.Equal(0, l.FoodFromFarming);
        }));
        Assert.Contains(reports.SelectMany(w => w.Events), e =>
            e.Kind == DemoSettlementEventKind.Birth && e.Cause.Contains("懷孕"));
        Assert.True(world.WorldPopulationStats!.Children > 0);
        Assert.True(reports.Last().Settlements.Any(l => l.AdultWorkersOpening < l.PopulationOpening));
        // 第 07 章 b 節：途中隊伍的人數就是隊伍裡的群體人數。
        Assert.All(world.MigrationsInTransit(), b =>
            Assert.Equal(b.People, world.HumanPopulationSnapshot!.Groups.Where(g => g.BatchId == b.Id).Sum(g => g.Count)));
    }

    [Fact]
    public void PregnancyAndAgeSurviveSaveLoadAndResumingMatchesOriginalWorld()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(7),
            DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < 365 * 2; day++) world.AdvanceOneDay();
        var raw = world.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(raw);
        Assert.Equal("希望時代：人類採集", loaded.TempScenarioDisplayName);
        Assert.Equal(raw, loaded.ExportTempSave());
        for (var day = 0; day < 180; day++)
        {
            world.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(world.ExportTempSave(), loaded.ExportTempSave());
    }

    [Fact]
    public void ForgedPregnancyBirthRecordAndAdultWorkersAreRejectedOnLoad()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42),
            DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < 410; day++) world.AdvanceOneDay();
        var raw = world.ExportTempSave();
        var tampered = System.Text.Json.Nodes.JsonNode.Parse(raw)!;
        var state = tampered["State"]!;
        state["Demography"]!["Groups"]!.AsArray()[0]!["Count"] = 999;
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(tampered.ToJsonString()));

        var badLedger = System.Text.Json.Nodes.JsonNode.Parse(raw)!;
        var reports = badLedger["State"]!["WeekReports"]!.AsArray();
        var first = reports[0]!["Settlements"]!.AsArray()[0]!;
        first["AdultWorkersOpening"] = 0;
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(badLedger.ToJsonString()));
    }
}
