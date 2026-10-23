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
        // 第 08-9 章 A2：任一項技術都能開啟開拓時代（作物栽培、纖維處理…看誰先到）。
        var first = DemoTechTree.ByIndex(learned.Count)!.Value;
        Assert.True(world.Knowledge.Knows(learned.SettlementId, first));
        Assert.Equal(era.Week, world.Knowledge.EraWeek);
        Assert.Equal("TEMP_EV_ERA_BEGAN", world.EventHistory.Find(era.EventId)!.EventTypeId);
        Assert.Contains("開拓時代", DemoWorldOverview.Of(world, 365).Lines()[0], StringComparison.Ordinal);
        Assert.Contains(DemoSettlementHistory.Lines(world, learned.SettlementId), l => l.Contains($"學會{DemoKnowledge.Name(first)}", StringComparison.Ordinal));
        Assert.Contains(world.QueryCell(world.Sites.Settlements.Single(s => s.Id == learned.SettlementId).Column,
            world.Sites.Settlements.Single(s => s.Id == learned.SettlementId).Row).Lines, l => l.StartsWith($"知識：{DemoKnowledge.Name(first)}（第", StringComparison.Ordinal));

        // 下一週補任農師（專業知識持有者）。
        for (var day = 0; day < 7; day++)
            world.AdvanceOneDay();
        if (first == DemoTechnology.Farming && world.SettlementPopulation(learned.SettlementId) > 0)
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
                && DemoTechTree.ByIndex(e.Count) == DemoTechnology.Woodworking);
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
    public void TechTreeComesFromTheOfficialSheets()
    {
        // 第 08-9 章 A1：10B～10N 全部啟用的節點；門檻＝研究時間×325；前置都在樹上；編號從 1 連續。
        Assert.True(DemoTechTree.All.Count >= 100, $"知識樹只有 {DemoTechTree.All.Count} 項");
        Assert.Equal(26_000, DemoKnowledge.ThresholdOf(DemoTechnology.Farming));
        Assert.Equal(32_500, DemoKnowledge.ThresholdOf(DemoTechnology.Woodworking));
        Assert.Equal(58_500, DemoKnowledge.ThresholdOf(DemoTechnology.Smithing));
        Assert.Equal("基礎作物栽培", DemoKnowledge.Name(DemoTechnology.Farming));
        Assert.Equal(Enumerable.Range(1, DemoTechTree.All.Count), DemoTechTree.All.Select(n => n.Index));
        Assert.All(DemoTechTree.All, n => Assert.All(n.Parents, p => Assert.True(DemoTechTree.Contains(p))));
        Assert.All(DemoTechTree.All, n => Assert.Equal(n.Technology, DemoTechTree.ByIndex(n.Index)));
        Assert.Null(DemoTechTree.ByIndex(0));
        Assert.Empty(DemoTechTree.Of(DemoTechnology.Farming).Parents);
        // 正式設定錯誤會被擋下：前置不存在。
        Assert.Throws<InvalidOperationException>(() => DemoTechTree.Build(new[] { ("10X.csv",
            "TechID,中文名稱,Tier,Branch,ParentTechIDs,ResearchTimeBase,PracticeBoostCondition,Enabled\nTECH_A,甲,1,b,TECH_NOPE,10,,TRUE\n") }));
    }

    [Fact]
    public void ResearchNeedsReachableMaterials_AndTheRightSettlementTier()
    {
        // 第 08-9 章 A2：研究原料取得得到才累積；第 2 階要村莊、第 3 階以上目前不累積；畜牧要等牧群。
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < 14; day++)
            world.AdvanceOneDay();
        var population = DemoCohortDemography.Restore(world.HumanPopulationSnapshot!);
        var state = world.Knowledge;
        DemoTechNode Node(string id) => DemoTechTree.Of(new DemoTechnology(id));
        foreach (var s in world.Sites.Settlements.Where(x => !x.IsRuin && world.SettlementPopulation(x.Id) > 0))
        {
            var food = world.SettlementFood(s.Id);
            var adults = population.AdultsIn(s.Id, DemoHumanLifecycle.Rules);
            // 石材、礦、羊皮紙目前都取不到。
            Assert.Equal(0, DemoKnowledge.Practitioners(Node("TECH_PROSPECTING"), s, population, world.Map, food, state));
            Assert.Equal(0, DemoKnowledge.Practitioners(Node("TECH_BASIC_CONSTRUCTION"), s, population, world.Map, food, state));
            Assert.Equal(0, DemoKnowledge.Practitioners(Node("TECH_WRITING_MATERIALS"), s, population, world.Map, food, state));
            Assert.Equal(0, DemoKnowledge.Practitioners(Node("TECH_ANIMAL_DOMESTICATION"), s, population, world.Map, food, state));
            // 木工要可及森林；纖維要可及草原。
            Assert.Equal(DemoKnowledge.NearForest(world.Map, s) ? adults : 0,
                DemoKnowledge.Practitioners(Node("TECH_BASIC_CARPENTRY"), s, population, world.Map, food, state));
            Assert.Equal(DemoKnowledge.NearTerrain(world.Map, s, DemoTerrain.Grassland) ? adults : 0,
                DemoKnowledge.Practitioners(Node("TECH_FIBER_PROCESSING"), s, population, world.Map, food, state));
        }
        Assert.True(DemoKnowledge.TierAllows(Node("TECH_CROP_CULTIVATION"), DemoSettlementTier.Settlement));
        Assert.False(DemoKnowledge.TierAllows(Node("TECH_CROP_ROTATION"), DemoSettlementTier.Settlement));
        Assert.True(DemoKnowledge.TierAllows(Node("TECH_CROP_ROTATION"), DemoSettlementTier.Village));
        Assert.False(DemoKnowledge.TierAllows(Node("TECH_SOIL_MANAGEMENT"), DemoSettlementTier.Village));
        Assert.Equal(new DemoTechnology("TECH_SCRIBAL_METHOD"), DemoRepresentative.TechnologyOf(DemoRepresentativeRole.Scholar));
        Assert.Contains(("RES_WOOD", 10L), Node("TECH_BASIC_CARPENTRY").ResearchCost);
        // 一年內進度只出現在第 1 階、原料取得得到的技術。
        for (var day = 0; day < 365; day++)
            world.AdvanceOneDay();
        Assert.All(world.Knowledge.Records, r => Assert.Equal(1, DemoTechTree.Of(r.Technology).Tier));
        Assert.DoesNotContain(world.Knowledge.Records, r => DemoTechTree.Of(r.Technology).Branch == "畜牧");
    }

    [Fact]
    public void KnowingNeighboursSpeedUpResearch_AndPracticeBoostsComeFromTheSheets()
    {
        // 第 08-9 章 A3：勢力範圍內（3 格）每個已會的聚落每週＋門檻萬分之 5；實踐加速的百分比讀正式設定。
        Assert.Equal(20, DemoTechTree.Of(DemoTechnology.Farming).PracticeBoostPercent);
        Assert.Equal(15, DemoTechTree.Of(new DemoTechnology("TECH_CROP_ROTATION")).PracticeBoostPercent);
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < 14; day++)
            world.AdvanceOneDay();
        var population = DemoCohortDemography.Restore(world.HumanPopulationSnapshot!);
        var report = world.LatestWeek!;
        var (s, o) = world.Sites.Settlements.Where(x => !x.IsRuin && world.SettlementPopulation(x.Id) > 0 && world.SettlementFood(x.Id) > 0)
            .SelectMany(x => world.Sites.Settlements.Where(y => y.Id != x.Id && !y.IsRuin
                && DemoKnowledge.HexDistance(world.Map, x, y) <= DemoKnowledge.TempDiffusionRange).Select(y => (x, y)))
            .First();
        var threshold = DemoKnowledge.ThresholdOf(DemoTechnology.Farming);
        var alone = DemoKnowledgeState.Initial;
        var withNeighbour = alone with
        {
            Records = System.Collections.Immutable.ImmutableList.Create(new DemoKnowledgeRecord(o.Id, DemoTechnology.Farming, threshold, 1)),
            Era = DemoEra.Pioneer, EraWeek = 1
        };
        long Progress(DemoKnowledgeState state) => DemoKnowledge.Advance(state, report, population, world.Sites, 1, world.Map)
            .State.Of(s.Id, DemoTechnology.Farming)?.Progress ?? 0;
        Assert.True(Progress(alone) > 0);
        Assert.Equal(threshold * DemoKnowledge.TempDiffusionPerTenThousand / 10_000, Progress(withNeighbour) - Progress(alone));
    }

    [Fact]
    public void EverySettlementStartsWithTheFiveDecidedKnowledges()
    {
        // 第 08-9 章 A4（使用者 2026-10-23）：採集、漁撈、用火、打製石器、基礎烹調；不必研究、沒有紀錄，新聚落也知道。
        var expected = new[] { "TECH_FORAGING", "TECH_FISHING", "TECH_FIRE", "TECH_STONE_TOOLS", "TECH_BASIC_COOKING" }
            .Select(id => new DemoTechnology(id)).ToHashSet();
        Assert.True(expected.SetEquals(DemoTechTree.Initial));
        Assert.All(expected, t => Assert.True(DemoKnowledgeState.Initial.Knows(12345, t)));
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < 365; day++)
            world.AdvanceOneDay();
        Assert.DoesNotContain(world.Knowledge.Records, r => DemoTechTree.IsInitial(r.Technology));
        // 石器改良的前置是開局知識，第一年就有進度；狩獵要等野生動物。
        Assert.Contains(world.Knowledge.Records, r => r.Technology == new DemoTechnology("TECH_IMPROVED_STONE_TOOLS") && r.Progress > 0);
        Assert.DoesNotContain(world.Knowledge.Records, r => r.Technology == new DemoTechnology("TECH_HUNTING"));
        var s = world.Sites.Settlements.First(x => !x.IsRuin);
        Assert.Contains(world.QueryCell(s.Column, s.Row).Lines, l => l.StartsWith("開局知識：採集、漁撈、用火、打製石器", StringComparison.Ordinal)
            || l.StartsWith("開局知識：", StringComparison.Ordinal) && l.Contains("基礎烹調", StringComparison.Ordinal));
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.Knowledge, loaded.Knowledge);
    }

    [Fact]
    public void KnowledgeIsLostWhenTheLastNearbyHolderBecomesARuin()
    {
        // 第 08-9 章 A5：成為遺跡的聚落紀錄移除；附近（3 格）沒有其他持有者才記「知識失傳」，直接原因是成為遺跡。
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < 14; day++)
            world.AdvanceOneDay();
        var population = DemoCohortDemography.Restore(world.HumanPopulationSnapshot!);
        var report = world.LatestWeek!;
        var (s, o) = world.Sites.Settlements.Where(x => !x.IsRuin)
            .SelectMany(x => world.Sites.Settlements.Where(y => y.Id != x.Id && !y.IsRuin
                && DemoKnowledge.HexDistance(world.Map, x, y) <= DemoKnowledge.TempDiffusionRange).Select(y => (x, y)))
            .First();
        var sites = world.Sites.WithRuin(s.Id, report.Week);
        var ruinEvent = new DemoSettlementEvent(report.Week, report.WorldTime, s.Id, s.Name, DemoSettlementEventKind.SettlementRuined, 4, "測試", EventId: 900);
        var ruinedWeek = report with { Events = report.Events.Append(ruinEvent).ToArray() };
        var farming = DemoTechnology.Farming;
        var threshold = DemoKnowledge.ThresholdOf(farming);
        DemoKnowledgeState With(params DemoKnowledgeRecord[] records) => DemoKnowledgeState.Initial with
        {
            Records = System.Collections.Immutable.ImmutableList.Create(records), Era = DemoEra.Pioneer, EraWeek = 1
        };
        var onlyHere = DemoKnowledge.Advance(With(new DemoKnowledgeRecord(s.Id, farming, threshold, 1)), ruinedWeek, population, sites, 901, world.Map);
        var lost = Assert.Single(onlyHere.Events, e => e.Kind == DemoSettlementEventKind.KnowledgeLost);
        Assert.Equal(900, lost.CauseEventId);
        Assert.Equal(farming, DemoTechTree.ByIndex(lost.Count));
        Assert.DoesNotContain(onlyHere.State.Records, r => r.SettlementId == s.Id);
        Assert.Equal(DemoEra.Pioneer, onlyHere.State.Era); // 時代不倒退
        var heldNearby = DemoKnowledge.Advance(With(new DemoKnowledgeRecord(s.Id, farming, threshold, 1),
            new DemoKnowledgeRecord(o.Id, farming, threshold, 1)), ruinedWeek, population, sites, 901, world.Map);
        Assert.DoesNotContain(heldNearby.Events, e => e.Kind == DemoSettlementEventKind.KnowledgeLost);
        Assert.DoesNotContain(heldNearby.State.Records, r => r.SettlementId == s.Id);
        Assert.Equal("TEMP_EV_KNOWLEDGE_LOST", DemoEventTypes.Of(DemoSettlementEventKind.KnowledgeLost).TypeId);
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
        // 第 08-9 章 A4：工具階梯——石器改良＋10%、基礎鍛造再＋15%，各在習得的下一週起生效。
        var stone = new DemoTechnology("TECH_IMPROVED_STONE_TOOLS");
        var state = DemoKnowledgeState.Initial with
        {
            Records = System.Collections.Immutable.ImmutableList.Create(
                new DemoKnowledgeRecord(1, stone, DemoKnowledge.ThresholdOf(stone), 4),
                new DemoKnowledgeRecord(1, DemoTechnology.Smithing, DemoKnowledge.ThresholdOf(DemoTechnology.Smithing), 10)),
            Era = DemoEra.Pioneer, EraWeek = 4
        };
        Assert.Equal(0, DemoKnowledge.ToolYieldPercent(state, 1, 4));
        Assert.Equal(10, DemoKnowledge.ToolYieldPercent(state, 1, 10));
        Assert.Equal(25, DemoKnowledge.ToolYieldPercent(state, 1, 11));
        Assert.Equal(0, DemoKnowledge.ToolYieldPercent(state, 2, 11));
        Assert.Equal(DemoRepresentativeRole.Smith, Enum.GetValues<DemoRepresentativeRole>().Single(r => DemoRepresentative.TechnologyOf(r) == DemoTechnology.Smithing));
        Assert.True(DemoKnowledge.ThresholdOf(DemoTechnology.Smithing) > DemoKnowledge.ThresholdOf(DemoTechnology.Woodworking));
        Assert.Contains(new DemoTechnology("TECH_CHARCOAL_SMELTING"), DemoTechTree.Of(DemoTechnology.Smithing).Parents);

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
}
