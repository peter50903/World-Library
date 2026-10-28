using System.Text.Json;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-9 章 A1：知識樹改讀正式設定 10B～10N、10R；TechID 取代列舉；四道閘門與從業者。</summary>
public sealed class DemoTechCatalogTests
{
    [Fact]
    public void CatalogReadsEveryEnabledRowOfTheTechSheets()
    {
        var all = DemoTechCatalog.All;
        // 10B 9、10C 8、10D 9（含 08-11 掘井）、10E～10K 各 8、10L～10N 各 7、10R 25。
        Assert.Equal(128, all.Length);
        Assert.Equal(Enumerable.Range(0, all.Length), all.Select(n => n.Index));
        Assert.Equal(all.Length, all.Select(n => n.Id).Distinct().Count());
        Assert.All(all, n => Assert.All(n.Parents, p => Assert.True(DemoTechCatalog.Contains(p))));
        // 門檻＝ResearchTimeBase × 110；鍛造前置照 10C（探礦→高溫冶煉→基礎鍛造）。
        Assert.Equal(80 * DemoKnowledge.TempResearchScale, DemoKnowledge.ThresholdOf(DemoTechnology.CropCultivation));
        Assert.Equal(100 * DemoKnowledge.TempResearchScale, DemoKnowledge.ThresholdOf(DemoTechnology.BasicCarpentry));
        Assert.Equal(new[] { "TECH_CHARCOAL_SMELTING" }, DemoTechnology.BasicForging.Node.Parents);
        Assert.Equal(new[] { "TECH_PROSPECTING" }, DemoTechCatalog.Get("TECH_CHARCOAL_SMELTING").Parents);
        Assert.Equal("基礎作物栽培", DemoKnowledge.Name(DemoTechnology.CropCultivation));
    }

    [Fact]
    public void OpeningKnowledgeIsKnownEverywhereWithoutRecords()
    {
        var opening = DemoTechnology.All.Where(DemoKnowledge.IsOpening).Select(t => t.Id).OrderBy(id => id, StringComparer.Ordinal);
        Assert.Equal(new[] { "TECH_BASIC_COOKING", "TECH_FIRE", "TECH_FISHING", "TECH_FORAGING", "TECH_STONE_TOOLS" }, opening);
        Assert.True(DemoKnowledgeState.Initial.Knows(7, DemoTechnology.Of("TECH_FIRE")));
        Assert.False(DemoKnowledgeState.Initial.Knows(7, DemoTechnology.CropCultivation));
        Assert.Empty(DemoKnowledgeState.Initial.KnownBy(7));
    }

    [Fact]
    public void TechnologiesSaveAsTechIdStrings()
    {
        var record = new DemoKnowledgeRecord(3, DemoTechnology.BasicCarpentry, 5, 0);
        var json = JsonSerializer.Serialize(record);
        Assert.Contains("\"Technology\":\"TECH_BASIC_CARPENTRY\"", json, StringComparison.Ordinal);
        Assert.Equal(record, JsonSerializer.Deserialize<DemoKnowledgeRecord>(json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DemoKnowledgeRecord>(json.Replace("TECH_BASIC_CARPENTRY", "TECH_NOPE")));
        Assert.False(DemoTechnology.IsDefined(DemoTechCatalog.All.Length));
    }

    [Fact]
    public void GatesFollowTierRegionAndPractitioners()
    {
        static DemoKnowledge.Context Ctx(DemoSettlementTier tier = DemoSettlementTier.Camp, bool forest = false, bool grass = false,
            bool water = false, bool sea = false, bool farmland = false, bool neighbour = false, bool leader = false)
            => new(tier, 20, 4, leader, 0, forest, grass, water, sea, farmland, neighbour);
        static bool None(string _) => false;
        long P(string id, DemoKnowledge.Context c, Func<string, bool>? knows = null)
            => DemoKnowledge.Practitioners(DemoTechCatalog.Get(id), c, knows ?? None);

        // 等級：第 1 階任何聚落、第 2 階村莊、第 3 階以上暫不可研究。
        Assert.True(DemoKnowledge.TierAllows(DemoTechCatalog.Get("TECH_CROP_CULTIVATION"), DemoSettlementTier.Camp));
        Assert.False(DemoKnowledge.TierAllows(DemoTechCatalog.Get("TECH_IRRIGATION"), DemoSettlementTier.Camp));
        Assert.True(DemoKnowledge.TierAllows(DemoTechCatalog.Get("TECH_IRRIGATION"), DemoSettlementTier.Village));
        Assert.False(DemoKnowledge.TierAllows(DemoTechCatalog.Get("TECH_SOIL_MANAGEMENT"), DemoSettlementTier.Village));
        Assert.True(DemoKnowledge.TierAllows(DemoTechCatalog.Get("TECH_SOIL_MANAGEMENT"), DemoSettlementTier.Town));

        // 地區：木工要森林、造船要森林加水、紡織要草原、鹽漬要鄰海、磨粉要農田；礦脈、牧群、魔法尚無＝0。
        Assert.Equal(0, P("TECH_BASIC_CARPENTRY", Ctx()));
        Assert.Equal(20, P("TECH_BASIC_CARPENTRY", Ctx(forest: true)));
        Assert.Equal(0, P("TECH_BASIC_BOATBUILDING", Ctx(forest: true)));
        Assert.Equal(20, P("TECH_BASIC_BOATBUILDING", Ctx(forest: true, water: true)));
        Assert.Equal(20, P("TECH_FIBER_PROCESSING", Ctx(grass: true)));
        Assert.Equal(0, P("TECH_SALTING", Ctx(water: true)));
        Assert.Equal(20, P("TECH_SALTING", Ctx(water: true, sea: true)));
        Assert.Equal(20, P("TECH_MILLING", Ctx(farmland: true)));
        Assert.Equal(0, P("TECH_PROSPECTING", Ctx(forest: true, grass: true, water: true)));
        Assert.Equal(0, P("TECH_ANIMAL_DOMESTICATION", Ctx(grass: true)));
        Assert.Equal(0, P("TECH_MANA_MATERIAL_HANDLING", Ctx()));
        Assert.Equal(0, P("TECH_MASONRY", Ctx()));
        // 皮革要已會狩獵；陶器要水與用火（開局知識）；書寫要村莊與村長。
        Assert.Equal(0, P("TECH_HIDE_CURING", Ctx()));
        Assert.Equal(20, P("TECH_HIDE_CURING", Ctx(), id => id == "TECH_HUNTING"));
        Assert.Equal(20, P("TECH_POTTERY_FIRING", Ctx(water: true), id => id == "TECH_FIRE"));
        Assert.Equal(0, P("TECH_WRITING_MATERIALS", Ctx(leader: true)));
        Assert.Equal(20, P("TECH_WRITING_MATERIALS", Ctx(DemoSettlementTier.Village, leader: true)));
        // 新領域：習慣法＝老人＋村長；以物易物要有往來的聚落；祖靈祭祀＝老人。
        Assert.Equal(5, P("TECH_ORAL_LAW", Ctx(leader: true)));
        Assert.Equal(0, P("TECH_BARTER", Ctx()));
        Assert.Equal(20, P("TECH_BARTER", Ctx(neighbour: true)));
        Assert.Equal(4, P("TECH_ANCESTOR_RITES", Ctx()));
    }

    [Fact]
    public void LivingWorldsLearnManyTechnologiesButNeverMiningOrMagic()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(2), DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < 40 * TestCalendar.DaysPerYear; day++)
            world.AdvanceOneDay();
        var learned = world.Knowledge.Records.Where(r => r.Learned).ToList();
        Assert.True(learned.Select(r => r.Technology).Distinct().Count() >= 5, $"40 年只學會 {learned.Select(r => r.Technology).Distinct().Count()} 種技術");
        Assert.All(learned, r =>
        {
            Assert.InRange(r.Technology.Node.Tier, 1, 3); // 第 08-11 章：城鎮可研究第 3 階
            Assert.DoesNotContain(r.Technology.Node.Domain, new[] { "10C", "10I", "10K", "10M" });
            Assert.All(r.Technology.Node.Parents, p => Assert.True(world.Knowledge.Knows(r.SettlementId, DemoTechnology.Of(p))));
        });
        // 第 2 階只出現在當時是村莊的聚落（降級後仍保留知識，只檢查曾經是村莊）。
        Assert.All(learned.Where(r => r.Technology.Node.Tier == 2),
            r => Assert.Contains(DemoSettlementHistory.Lines(world, r.SettlementId), l => l.Contains("發展為村莊", StringComparison.Ordinal)));
        Assert.Empty(world.BuildCausalIndex().Problems);
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.Knowledge, loaded.Knowledge);
    }
}
