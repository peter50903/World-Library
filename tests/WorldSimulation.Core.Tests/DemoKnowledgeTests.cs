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
    public void WoodworkingBringsACarpenter_AndACarpenterVillageIsAVillage()
    {
        // 第 08-6 章 d 節：可及森林的聚落研究木工；學會後補任木匠，有木匠且人口達門檻才是村莊。
        DemoObserverWorld? world = null;
        DemoSettlementEvent? learned = null;
        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
            for (var day = 0; day < 80 * 365 && !world.Knowledge.Records.Any(r => r.Technology == DemoTechnology.Woodworking && r.Learned); day++)
                world.AdvanceOneDay();
            learned = world.LatestWeek?.Events.FirstOrDefault(e => e.Kind == DemoSettlementEventKind.KnowledgeLearned
                && (DemoTechnology)e.Count == DemoTechnology.Woodworking);
            if (learned is not null) break;
        }
        Assert.NotNull(learned);
        var place = world!.Sites.Settlements.Single(s => s.Id == learned!.SettlementId);
        Assert.True(DemoKnowledge.NearForest(world.Map, place));
        for (var day = 0; day < 7; day++)
            world.AdvanceOneDay();
        var carpenter = world.RepresentativesOf(place.Id).Any(r => r.Role == DemoRepresentativeRole.Carpenter);
        var population = world.SettlementPopulation(place.Id);
        Assert.True(carpenter || population == 0, "學會木工的聚落應補任木匠");
        Assert.Equal(DemoSettlementHistory.TierFor(population, carpenter, world.ChronicleOf(place.Id).Tier), world.ChronicleOf(place.Id).Tier);
        if (world.ChronicleOf(place.Id).Tier == DemoSettlementTier.Village)
            Assert.Contains(DemoSettlementHistory.Lines(world, place.Id), l => l.Contains("發展為村莊", StringComparison.Ordinal));
        Assert.Empty(world.BuildCausalIndex().Problems);
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.ChronicleOf(place.Id), loaded.ChronicleOf(place.Id));
    }

    [Fact]
    public void VillageNeedsACarpenterAndEnoughPeople_WithASmallerDropThreshold()
    {
        Assert.Equal(DemoSettlementTier.Settlement, DemoSettlementHistory.TierFor(50, hasCarpenter: false));
        Assert.Equal(DemoSettlementTier.Settlement, DemoSettlementHistory.TierFor(DemoSettlementHistory.TempVillagePopulation - 1, true));
        Assert.Equal(DemoSettlementTier.Village, DemoSettlementHistory.TierFor(DemoSettlementHistory.TempVillagePopulation, true));
        // 已是村莊：人口降到保留門檻以下、或失去木匠才降一級。
        Assert.Equal(DemoSettlementTier.Village, DemoSettlementHistory.TierFor(DemoSettlementHistory.TempVillageKeepPopulation, true, DemoSettlementTier.Village));
        Assert.Equal(DemoSettlementTier.Settlement, DemoSettlementHistory.TierFor(DemoSettlementHistory.TempVillageKeepPopulation - 1, true, DemoSettlementTier.Village));
        Assert.Equal(DemoSettlementTier.Settlement, DemoSettlementHistory.TierFor(50, false, DemoSettlementTier.Village));
    }

    [Fact]
    public void SmithingToolsRaiseYieldFromTheWeekAfterLearning()
    {
        // 第 08-6 章 e 節：鍛造在木工之後；習得後下一週起每名勞力可採量＋25%。
        var state = DemoKnowledgeState.Initial with
        {
            Records = System.Collections.Immutable.ImmutableList.Create(
                new DemoKnowledgeRecord(1, DemoTechnology.Smithing, DemoKnowledge.TempSmithingThreshold, 10)),
            Era = DemoEra.Pioneer, EraWeek = 5
        };
        Assert.Equal(0, DemoKnowledge.ToolYieldPercent(state, 1, 10));
        Assert.Equal(DemoKnowledge.TempToolYieldPercent, DemoKnowledge.ToolYieldPercent(state, 1, 11));
        Assert.Equal(0, DemoKnowledge.ToolYieldPercent(state, 2, 11));
        Assert.Equal(DemoRepresentativeRole.Smith, Enum.GetValues<DemoRepresentativeRole>().Single(r => DemoRepresentative.TechnologyOf(r) == DemoTechnology.Smithing));
        Assert.True(DemoKnowledge.ThresholdOf(DemoTechnology.Smithing) > DemoKnowledge.ThresholdOf(DemoTechnology.Woodworking));

        var config = DemoWeeklyTempConfig.LivingForagingSample;
        var ledger = new DemoSettlementWeekLedger(1, 1, "測試", 0, 0, 0, 0, 0, 0, 0, 0, 0, 5, 0, 0, 5);
        Assert.Equal(config.YieldPerWorkerPerWeek, ledger.YieldPerWorker(config));
        Assert.Equal(config.YieldPerWorkerPerWeek * 125 / 100, (ledger with { ToolYieldPercent = 25 }).YieldPerWorker(config));
    }

    [Fact]
    public void ResearchGrowsWithAdults_AndIsCappedAtTheThreshold()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < 365; day++)
            world.AdvanceOneDay();
        Assert.Equal(DemoEra.Hope, world.Era);
        Assert.All(world.Knowledge.Records, r => Assert.InRange(r.Progress, 0, DemoKnowledge.ThresholdOf(r.Technology)));
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

    /// <summary>第 08-10a 章 V4：希望時代是序章——預設種子 25 年內進入開拓時代（V0 量到原門檻要 36～53 年）。</summary>
    [Fact]
    public void HopeEraIsAPrologue_EndingWithinTwentyFiveYears()
    {
        foreach (var seed in new ulong[] { 1, 2, 3 })
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
            for (var day = 0; day < 25 * TestCalendar.DaysPerYear && world.Era == DemoEra.Hope; day++)
                world.AdvanceOneDay();
            Assert.True(world.Era == DemoEra.Pioneer, $"種子 {seed} 25 年內沒有進入開拓時代");
        }
        Assert.Equal(80 * DemoKnowledge.TempResearchScale, DemoKnowledge.ThresholdOf(DemoTechnology.Farming));
    }

    /// <summary>第 08-10a 章 V4：鄰近傳播的距離是六角格距離。</summary>
    [Fact]
    public void DistanceIsTheHexDistanceBetweenSettlementCells()
    {
        var map = DemoHexWorld.Generate(1);
        var a = new DemoSettlement(1, "甲", 2, 2);
        Assert.Equal(0, DemoKnowledge.Distance(map, a, a));
        Assert.Equal(1, DemoKnowledge.Distance(map, a, new DemoSettlement(2, "乙", 3, 2)));
        Assert.Equal(3, DemoKnowledge.Distance(map, a, new DemoSettlement(3, "丙", 5, 2)));
        Assert.Equal(2, DemoKnowledge.Distance(map, a, new DemoSettlement(4, "丁", 2, 4)));
    }
}
