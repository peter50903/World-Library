using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-8 章 B2：主要文化變化紀錄與生計文化的來源加成；B3：通靈者研究加成。</summary>
public sealed class DemoCultureSourceTests
{
    [Fact]
    public void MainHistoryTracksEverySettlementAndSurvivesSaving()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        Assert.All(world.Culture.Settlements, s => Assert.Contains(world.Culture.MainHistory, h => h.SettlementId == s.SettlementId && h.Week == 0));
        for (var d = 0; d < 30 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var culture = world.Culture;
        foreach (var s in culture.Settlements)
            Assert.Equal(culture.MainCultureOf(s.SettlementId), culture.MainHistory.Last(h => h.SettlementId == s.SettlementId).CultureId);
        var week = world.Snapshot().DaysElapsed / 7 + 1;
        var any = culture.Settlements[0].SettlementId;
        Assert.Equal(culture.Culture(culture.MainCultureOf(any)!.Value)!.Traits, culture.TraitsAt(any, week));
        Assert.Empty(culture.TraitsAt(any, 0));
        // 讀檔會依紀錄重算來源帳的文化加成；不一致時讀檔失敗。
        var reloaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(culture.Fingerprint(), reloaded.Culture.Fingerprint());
    }

    [Fact]
    public void SpiritMediumsSpeedUpFaithResearchOnly()
    {
        var rites = DemoTechCatalog.Get("TECH_ANCESTOR_RITES");
        var farming = DemoTechCatalog.Get("TECH_CROP_CULTIVATION");
        Assert.Equal(25, DemoKnowledge.CultureResearchPercent(rites, new[] { "DOC_SPIRIT_MEDIUM" }));
        Assert.Equal(0, DemoKnowledge.CultureResearchPercent(farming, new[] { "DOC_SPIRIT_MEDIUM" }));
    }

    [Fact]
    public void LivelihoodTraitsAddTenPercentToTheirOwnSourceKind()
    {
        Assert.Equal(4, DemoKnowledge.TempCultureSourceBonuses.Length);
        Assert.All(DemoKnowledge.TempCultureSourceBonuses, b => Assert.Equal(10, b.Percent));
        Assert.Equal(DemoCatchmentSourceKind.Forest, DemoKnowledge.TempCultureSourceBonuses.Single(b => b.TraitId == "CT_FOREST_FOLK").Kind);
    }
}
