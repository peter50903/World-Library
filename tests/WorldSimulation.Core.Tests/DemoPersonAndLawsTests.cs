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

    // 第 14-1 章第 1 項：法則切換記下週次，存檔帶著走，重播驗證在同一週套用。
    [Fact]
    public void LawChangesAreLoggedAndReplayed()
    {
        var world = World(2UL, 14);
        Assert.Empty(world.LawChanges);
        var off = DemoWorldLaws.Default.With("disasters", false);
        world.Laws = off;
        world.Laws = off.With("wars", false);          // 同一週再改：只留最後一次
        Assert.Equal(new[] { new DemoLawChange(2, off.With("wars", false)) }, world.LawChanges);
        for (var day = 0; day < 21; day++)
            world.AdvanceOneDay();
        world.Laws = DemoWorldLaws.Default.With("wars", false);
        world.Laws = off.With("wars", false);          // 改回原樣：這一週等於沒改
        Assert.Single(world.LawChanges);
        world.Laws = DemoWorldLaws.Default;
        Assert.Equal(new[] { 2, 5 }, world.LawChanges.Select(c => c.AfterWeek));

        var saved = world.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(saved);
        Assert.Equal(world.LawChanges, loaded.LawChanges);
        Assert.Equal(saved, loaded.ExportTempSave());
        var verdict = DemoObserverWorld.VerifyTempSaveByReplay(saved);
        Assert.True(verdict.Ok, verdict.FirstDifference);
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

    // 第 14-1 章第 7 項：具名人物上任就登記，卸任後仍用編號查得到；存檔帶著走。
    [Fact]
    public void PeopleStayInTheRegisterAfterTheyLeaveOffice()
    {
        var world = World(1UL, 70);
        var inOffice = world.Sites.Settlements.SelectMany(s => world.RepresentativesOf(s.Id)).ToList();
        Assert.NotEmpty(inOffice);
        Assert.All(inOffice, r =>
        {
            var record = world.PersonRecord(r.Id);
            Assert.NotNull(record);
            Assert.True(record!.InOffice);
            Assert.Equal((r.Name, r.Role, r.SettlementId, r.AppointedWeek), (record.Name, record.Role, record.SettlementId, record.AppointedWeek));
        });
        Assert.Equal(inOffice.Select(r => r.Id).Order(), world.PersonRecords.Where(p => p.InOffice).Select(p => p.Id));
        Assert.Null(world.PersonRecord(-1));

        // 登記只增不減：每週週報的上任都在，卸任的補上卸任週與原因。
        var appointed = world.WeekReports().SelectMany(w => w.Appointments).Select(r => r.Id).ToList();
        Assert.All(appointed, id => Assert.NotNull(world.PersonRecord(id)));
        foreach (var exit in world.WeekReports().SelectMany(w => w.RepresentativeExits.Select(x => (w.Week, x))))
        {
            var record = world.PersonRecord(exit.x.Id);
            Assert.NotNull(record);
            Assert.False(record!.InOffice);
            Assert.Equal((exit.Week, exit.x.Reason), (record.ExitWeek, record.ExitReason));
        }

        var saved = world.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(saved);
        Assert.Equal(world.PersonRecords, loaded.PersonRecords);
        Assert.Equal(saved, loaded.ExportTempSave());
    }
}
