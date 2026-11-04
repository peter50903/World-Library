using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-16 章：讓紀錄成故事（建村與遺跡的起因、代表人物小傳）。</summary>
public sealed class DemoStoriesTests
{
    private static DemoSettlementEvent E(long id, DemoSettlementEventKind kind, long cause = 0, string text = "", int week = 10)
        => new(week, TimeSpan.FromDays(week * 7), 1, "甲", kind, 1, text, EventId: id, CauseEventId: cause);

    [Fact]
    public void FoundingExplainsWhyPeopleLeft()
    {
        var heat = E(1, DemoSettlementEventKind.Heatwave);
        var hunger = E(2, DemoSettlementEventKind.Hunger, 1);
        var leave = E(3, DemoSettlementEventKind.Emigration, 2);
        var found = E(4, DemoSettlementEventKind.SettlementFounded, 3);
        var all = new[] { heat, hunger, leave, found }.ToDictionary(e => e.EventId);
        Assert.Equal(new[] { leave, hunger, heat }, DemoStories.CauseChain(found, all));
        Assert.Contains("熱浪", DemoStories.WhyFounded(found, all), StringComparison.Ordinal);

        var crowd = E(5, DemoSettlementEventKind.Emigration, 0, "人口接近附近來源上限：…");
        var split = E(6, DemoSettlementEventKind.SettlementFounded, 5);
        var byId = new[] { crowd, split }.ToDictionary(e => e.EventId);
        Assert.Contains("人口漸多", DemoStories.WhyFounded(split, byId), StringComparison.Ordinal);
        Assert.Null(DemoStories.WhyFounded(E(7, DemoSettlementEventKind.SettlementFounded), byId));
    }

    [Fact]
    public void RuinNamesWhatHappenedBefore()
    {
        var ruin = E(9, DemoSettlementEventKind.SettlementRuined, week: 200);
        var events = new[] { E(1, DemoSettlementEventKind.Flood, week: 150), E(2, DemoSettlementEventKind.StarvationDeath, week: 180), ruin };
        Assert.Equal("洪水、饑荒之後", DemoStories.WhyRuined(ruin, events));
        Assert.Null(DemoStories.WhyRuined(ruin, new[] { E(1, DemoSettlementEventKind.Flood, week: 10), ruin }));
    }

    [Fact]
    public void LivingWorldStoriesAreReadableAndSurviveSaveLoad()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 50 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var chronicle = DemoWorldChronicle.Build(world);
        var lines = chronicle.SelectMany(y => y.Entries).Select(e => e.Text).ToList();
        Assert.Contains(lines, l => l.Contains("——", StringComparison.Ordinal)); // 至少一筆建村說得出起因
        foreach (var s in world.Sites.Settlements.Where(s => !s.IsRuin))
            foreach (var r in world.RepresentativesOf(s.Id))
                foreach (var line in DemoStories.Biography(world, r))
                {
                    Assert.DoesNotContain("TEMP", line, StringComparison.Ordinal);
                    Assert.True(line.Length <= 80, line);
                }
        var reloaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(lines, DemoWorldChronicle.Build(reloaded).SelectMany(y => y.Entries).Select(e => e.Text).ToList());
    }
}
