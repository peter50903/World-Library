using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-6 章 a 節：知識樹（農耕）與時代（希望時代→開拓時代）。</summary>
public sealed class DemoKnowledgeTests
{
    /// <summary>希望時代世界跑到第一個聚落學會農耕的那一週（最多 60 年，種子依序找）。</summary>
    private static DemoObserverWorld UntilPioneer()
    {
        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
            for (var day = 0; day < 60 * 365 && world.Era == DemoEra.Hope; day++)
                world.AdvanceOneDay();
            if (world.Era == DemoEra.Pioneer)
                return world;
        }
        throw new InvalidOperationException("三個種子 60 年內都沒有聚落學會農耕。");
    }

    [Fact]
    public void FirstFarmingVillage_StartsThePioneerEra_WithAFarmerAndHistory()
    {
        var world = UntilPioneer();
        var events = world.SettlementEvents();
        var era = Assert.Single(events, e => e.Kind == DemoSettlementEventKind.EraBegan);
        var learned = events.Single(e => e.EventId == era.CauseEventId);
        Assert.Equal(DemoSettlementEventKind.KnowledgeLearned, learned.Kind);
        Assert.Equal(DemoTechnology.Farming, (DemoTechnology)learned.Count);
        Assert.True(world.Knowledge.Knows(learned.SettlementId, DemoTechnology.Farming));
        Assert.Equal(era.Week, world.Knowledge.EraWeek);
        Assert.Equal("TEMP_EV_ERA_BEGAN", world.EventHistory.Find(era.EventId)!.EventTypeId);
        Assert.Contains("開拓時代", DemoWorldOverview.Of(world, 365).Lines()[0], StringComparison.Ordinal);
        Assert.Contains(DemoSettlementHistory.Lines(world, learned.SettlementId), l => l.Contains("學會農耕", StringComparison.Ordinal));
        Assert.Contains(world.QueryCell(world.Sites.Settlements.Single(s => s.Id == learned.SettlementId).Column,
            world.Sites.Settlements.Single(s => s.Id == learned.SettlementId).Row).Lines, l => l.StartsWith("知識：農耕（第", StringComparison.Ordinal));

        // 下一週補任農師（專業知識持有者）。
        for (var day = 0; day < 7; day++)
            world.AdvanceOneDay();
        Assert.Contains(world.RepresentativesOf(learned.SettlementId), r => r.Role == DemoRepresentativeRole.Farmer);
        Assert.Empty(world.BuildCausalIndex().Problems);

        // 存讀檔後知識樹與時代相同，續跑一致。
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.Knowledge, loaded.Knowledge);
        for (var day = 0; day < 60; day++)
        {
            world.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(world.ExportTempSave(), loaded.ExportTempSave());
    }

    [Fact]
    public void FarmingVillagesClearGrassland_IntoFarmlandThatFeedsThem()
    {
        // 第 08-6 章 c 節：學會農耕的聚落開墾相鄰草原（每週＋2%），完成後成為農田來源、採收記為農糧。
        var world = UntilPioneer();
        for (var day = 0; day < 6 * 365 && !world.SettlementEvents().Any(e => e.Kind == DemoSettlementEventKind.FarmlandCleared); day++)
            world.AdvanceOneDay();
        var cleared = world.SettlementEvents().Where(e => e.Kind == DemoSettlementEventKind.FarmlandCleared).ToList();
        Assert.True(cleared.Count > 0, $"開拓時代 6 年內沒有開墾完成的農田；開墾中 {world.Land.Parcels.Count} 塊");
        var cell = DemoLandClearing.CellOf(world.Map, cleared[0].Count);
        Assert.Equal(DemoLandDevelopment.Farmland, world.Land.StateOf(cell));
        Assert.Equal(DemoTerrain.Grassland, world.Map.GetCell(cell.Column, cell.Row).Terrain);
        Assert.True(world.Knowledge.Knows(cleared[0].SettlementId, DemoTechnology.Farming));
        Assert.Contains(DemoSettlementHistory.Lines(world, cleared[0].SettlementId), l => l.Contains("開墾一塊農田", StringComparison.Ordinal));
        Assert.Contains(world.QueryCell(cell.Column, cell.Row).Lines, l => l.StartsWith("土地開發：農田", StringComparison.Ordinal));

        for (var day = 0; day < 14; day++)
            world.AdvanceOneDay();
        var source = Assert.Single(world.LatestWeek!.Sources, x => x.Column == cell.Column && x.Row == cell.Row);
        Assert.Equal(DemoCatchmentSourceKind.Farmland, source.Kind);
        Assert.True(world.LatestWeek!.Settlements.Sum(l => l.FoodFromFarming) > 0);
        Assert.All(world.LatestWeek!.Settlements, l => Assert.True(l.NoMagicFood));
        Assert.Empty(world.BuildCausalIndex().Problems);

        // 存讀檔後土地相同，續跑一致；竄改農田（沒有開墾事件）會被拒讀。
        var save = world.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(save);
        Assert.Equal(world.Land.Parcels, loaded.Land.Parcels);
        for (var day = 0; day < 30; day++)
        {
            world.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(world.ExportTempSave(), loaded.ExportTempSave());
        var doc = System.Text.Json.Nodes.JsonNode.Parse(save)!;
        var parcels = doc["State"]!["Land"]!.AsArray();
        var farm = parcels.First(p => (int)p!["State"]! == (int)DemoLandDevelopment.Farmland)!;
        farm["State"] = (int)DemoLandDevelopment.Clearing;
        farm["DevelopmentPercent"] = 50;
        doc["Manifest"]!["StateChecksum"] = DemoSaveHeader.Checksum(System.Text.Encoding.UTF8.GetBytes(doc["State"]!.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
    }

    [Fact]
    public void ResearchGrowsWithAdults_AndIsCappedAtTheThreshold()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < 365; day++)
            world.AdvanceOneDay();
        Assert.Equal(DemoEra.Hope, world.Era);
        Assert.All(world.Knowledge.Records, r => Assert.InRange(r.Progress, 0, DemoKnowledge.TempThreshold));
        // 一年後每個有人的聚落都有研究進度，人多的聚落進度較多（每週＋成年人數）。
        var inhabited = world.Sites.Settlements.Where(s => world.SettlementPopulation(s.Id) > 0).ToList();
        Assert.All(inhabited, s => Assert.True(world.Knowledge.Of(s.Id, DemoTechnology.Farming)?.Progress > 0));
        // 非人類繁衍世界沒有知識樹。
        var plain = new DemoObserverWorld(DemoScenario.Sample(1));
        for (var day = 0; day < 70; day++)
            plain.AdvanceOneDay();
        Assert.Empty(plain.Knowledge.Records);
    }
    [Fact]
    public void HopeEraDebtStaysBelowTheLethalThreshold()
    {
        // 第 08-6 章 b 節：希望時代飢餓債不超過致死門檻－1，進入開拓時代時不會一次餓死。
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        var limit = DemoWeeklyTempConfig.LivingForagingSample.HungerDebtPerStarvationDeath;
        for (var day = 0; day < 10 * 365; day++)
        {
            world.AdvanceOneDay();
            if (world.Era != DemoEra.Hope) break;
            if (day % 7 == 6)
                Assert.All(world.Sites.Settlements, s => Assert.True(world.SettlementHungerDebt(s.Id) < limit));
        }
    }
}
