using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-19c 章：人物頁與世界法則。</summary>
public sealed class DemoPersonAndLawsTests
{
    private static DemoObserverWorld World(ulong seed, int days)
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(seed, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < days; d++)
            world.AdvanceOneDay();
        return world;
    }

    [Fact]
    public void PersonPageShowsTheRepresentativeAndDoesNotChangeTheWorld()
    {
        var world = World(1UL, 70);
        var person = world.Sites.Settlements.SelectMany(s => world.RepresentativesOf(s.Id)).First();
        var before = world.ExportTempSave();
        var detail = DemoPersonDetail.Of(world, person.Id);
        Assert.NotNull(detail);
        Assert.Equal(new[] { "人物", "生平", "同僚" }, detail!.Tabs.Select(t => t.Title));
        Assert.Equal(person.SettlementId, detail.SettlementId);
        Assert.Contains(person.Name, detail.Title);
        Assert.Equal(person.Id, Assert.Single(detail.Tabs[0].People).Id);
        Assert.Contains(detail.Tabs[0].Lines, l => l.StartsWith("職責：", StringComparison.Ordinal));
        Assert.DoesNotContain(detail.Tabs[2].People, p => p.Id == person.Id);
        Assert.NotEmpty(detail.Tabs[1].Lines);
        Assert.Null(DemoPersonDetail.Of(world, -1));
        Assert.Equal(before, world.ExportTempSave()); // 只讀
    }

    [Fact]
    public void LawsToggleAndSurviveASave()
    {
        var laws = DemoWorldLaws.Default;
        Assert.True(laws.IsDefault);
        Assert.All(laws.Entries, e => Assert.True(e.On));
        var off = laws.With("disasters", false).With("monsters", false).With("rebellions", false).With("beasts", false);
        Assert.Equal(new DemoWorldLaws(false, false, false, false), off);
        Assert.Throws<ArgumentOutOfRangeException>(() => laws.With("x", true));
        var world = World(2UL, 7);
        var defaultSave = world.ExportTempSave();
        world.Laws = off;
        var saved = world.ExportTempSave();
        Assert.NotEqual(defaultSave, saved);
        var loaded = DemoObserverWorld.ImportTempSave(saved);
        Assert.Equal(off, loaded.Laws);
        Assert.Equal(saved, loaded.ExportTempSave());
    }

    [Fact]
    public void ClosedLawsStopDisastersMonstersAndRebellions()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(4UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample)
        {
            Laws = new DemoWorldLaws(false, false, false, false)
        };
        for (var d = 0; d < 7 * 156; d++)
            world.AdvanceOneDay();
        var events = world.SettlementEvents();
        Assert.DoesNotContain(events, e => DemoDisasters.IsDisaster(e.Kind) || DemoMonsters.IsStrike(e.Kind)
            || e.Kind is DemoSettlementEventKind.DungeonAppeared or DemoSettlementEventKind.FactionJoined
                or DemoSettlementEventKind.SecessionArmed or DemoSettlementEventKind.SecessionPeaceful or DemoSettlementEventKind.BeastAttack);
        Assert.Empty(world.Dungeons);
        Assert.Empty(world.BuildCausalIndex().Problems);
    }
}
