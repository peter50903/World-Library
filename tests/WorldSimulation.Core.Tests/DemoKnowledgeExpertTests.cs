using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-9 章 A2：專家依技術補任、專家×5／學者×10 加速、實踐加速（每聚落每技術一次）。</summary>
public sealed class DemoKnowledgeExpertTests
{
    [Fact]
    public void ExpertsNeedTheirTechnologyAndSpeedTheirOwnDomains()
    {
        Assert.Equal("TECH_ARCHERY", DemoRepresentative.TechnologyOf(DemoRepresentativeRole.Hunter)!.Value.Id);
        Assert.Equal("TECH_SHAMANISM", DemoRepresentative.TechnologyOf(DemoRepresentativeRole.Shaman)!.Value.Id);
        Assert.Equal("TECH_SCRIBAL_METHOD", DemoRepresentative.TechnologyOf(DemoRepresentativeRole.Scholar)!.Value.Id);
        Assert.Null(DemoRepresentative.TechnologyOf(DemoRepresentativeRole.Leader));
        Assert.Equal("學者", DemoRepresentative.RoleName(DemoRepresentativeRole.Scholar));

        var irrigation = DemoTechCatalog.Get("TECH_IRRIGATION");
        Assert.Equal(DemoKnowledge.TempExpertResearch, DemoKnowledge.ExpertResearch(irrigation, new[] { DemoRepresentativeRole.Farmer }));
        Assert.Equal(0, DemoKnowledge.ExpertResearch(irrigation, new[] { DemoRepresentativeRole.Carpenter, DemoRepresentativeRole.Leader }));
        // 學者加速所有領域，與專家可疊加。
        Assert.Equal(DemoKnowledge.TempExpertResearch + DemoKnowledge.TempScholarResearch,
            DemoKnowledge.ExpertResearch(irrigation, new[] { DemoRepresentativeRole.Farmer, DemoRepresentativeRole.Scholar }));
        Assert.Equal(DemoKnowledge.TempExpertResearch,
            DemoKnowledge.ExpertResearch(DemoTechCatalog.Get("TECH_ORAL_LAW"), new[] { DemoRepresentativeRole.Leader }));
    }

    [Fact]
    public void PracticeBoostConditionsMatchWhatTheWorldCanDo()
    {
        var none = new DemoKnowledge.Practice(false, false, false, false);
        Assert.All(DemoTechCatalog.All, n => Assert.False(DemoKnowledge.PracticeHappened(n, none)));
        Assert.True(DemoKnowledge.PracticeHappened(DemoTechCatalog.Get("TECH_IRRIGATION"), none with { HasFarmland = true }));
        Assert.True(DemoKnowledge.PracticeHappened(DemoTechCatalog.Get("TECH_BASIC_CONSTRUCTION"), none with { FoundedAVillage = true }));
        Assert.True(DemoKnowledge.PracticeHappened(DemoTechCatalog.Get("TECH_ANCESTOR_RITES"), none with { HadNaturalDeath = true }));
        Assert.True(DemoKnowledge.PracticeHappened(DemoTechCatalog.Get("TECH_BARTER"), none with { HadVisitors = true }));
        Assert.Equal(20, DemoTechCatalog.Get("TECH_IRRIGATION").PracticeBoostPercent);
        Assert.Equal("為逝者舉行祭祀", DemoTechCatalog.Get("TECH_ANCESTOR_RITES").PracticeCondition);
    }

    [Fact]
    public void LivingWorldsBoostOncePerTechAndAppointExpertsOnlyWhereKnown()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        for (var day = 0; day < 40 * TestCalendar.DaysPerYear; day++)
            world.AdvanceOneDay();
        var records = world.Knowledge.Records;
        // 老死很常見，祖靈祭祀幾乎每個聚落都得到實踐加速；加速過的紀錄進度至少有門檻的兩成。
        Assert.Contains(records, r => r.Boosted && r.Technology.Id == "TECH_ANCESTOR_RITES");
        Assert.All(records.Where(r => r.Boosted), r => Assert.True(r.Technology.Node.PracticeBoostPercent > 0));
        Assert.All(records.Where(r => r.Boosted),
            r => Assert.True(r.Progress >= DemoKnowledge.ThresholdOf(r.Technology) * r.Technology.Node.PracticeBoostPercent / 100));
        // 專家只在已習得對應技術的聚落。
        foreach (var s in world.Sites.Settlements)
            foreach (var r in world.RepresentativesOf(s.Id))
                if (DemoRepresentative.TechnologyOf(r.Role) is { } needed)
                    Assert.True(world.Knowledge.Knows(s.Id, needed), $"{s.Name}的{DemoRepresentative.RoleName(r.Role)}沒有對應技術");
        Assert.Empty(world.BuildCausalIndex().Problems);
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(world.Knowledge, loaded.Knowledge);
        for (var day = 0; day < 60; day++)
        {
            world.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(world.ExportTempSave(), loaded.ExportTempSave());
    }
}
