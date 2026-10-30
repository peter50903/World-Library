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

/// <summary>第 08-8 章 B2：強人統治——村長改由未滿老年的最年長成年人擔任。</summary>
public sealed class DemoStrongRuleTests
{
    [Fact]
    public void StrongRuleLeadersWereNotElderlyWhenAppointed()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var elder = DemoSurvivalTempSettings.Current.ElderAgeYears;
        var leaders = new Dictionary<long, (DemoRepresentative Leader, bool Strong)>();
        for (var d = 0; d < 30 * TestCalendar.DaysPerYear; d++)
        {
            var before = world.Culture;
            world.AdvanceOneDay();
            // 就任週記下就任前的主要文化有沒有強人統治（村長人選依結算前的文化）。
            foreach (var r in world.Sites.Settlements.SelectMany(s => world.RepresentativesOf(s.Id)).Where(r => r.Role == DemoRepresentativeRole.Leader))
                if (!leaders.ContainsKey(r.Id))
                    leaders[r.Id] = (r, before.MainCultureOf(r.SettlementId) is { } c && before.Culture(c)!.Traits.Contains("CT_STRONG_RULE"));
        }
        foreach (var (leader, strong) in leaders.Values)
        {
            var age = DemoCohortDemography.AgeAt(leader.BirthYear, leader.AppointedWeek * 7);
            if (strong)
                Assert.True(age < elder, $"強人統治的村長 {leader.Name} 就任時 {age} 歲");
        }
    }
}
