using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-16 章第 15 節：文化頁與信仰頁。</summary>
public sealed class DemoBeliefDetailTests
{
    [Fact]
    public void EveryTraitTenetAndDoctrineHasADescription()
    {
        foreach (var trait in DemoCultures.TraitPool)
            Assert.False(string.IsNullOrEmpty(DemoBeliefDetail.Description(trait.TraitId)), trait.TraitId);
        foreach (var entry in DemoFaiths.Pool)
            Assert.False(string.IsNullOrEmpty(DemoBeliefDetail.Description(entry.EntryId)), entry.EntryId);
    }

    [Fact]
    public void CultureAndFaithPagesExplainTraditionsAndListFollowers()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 100 * TestCalendar.DaysPerYear && world.Culture.Faith.Faiths.IsEmpty; d++)
            world.AdvanceOneDay();
        for (var d = 0; d < 5 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var before = world.ExportTempSave();

        var settlement = world.Sites.Settlements.First(s => !s.IsRuin && world.Culture.MainCultureOf(s.Id) is not null);
        var cultureId = world.Culture.MainCultureOf(settlement.Id)!.Value;
        var culture = world.Culture.Culture(cultureId)!;
        // 聚落現況帶著主要文化（介面放連結）。
        var now = world.PlayerDetail(settlement.Column, settlement.Row).Tabs[0];
        Assert.Equal(cultureId, now.CultureId);

        var page = world.CultureDetail(cultureId)!;
        Assert.Equal(new[] { "傳統", "分布", "歷史" }, page.Tabs.Select(t => t.Title));
        Assert.NotNull(page.Tabs[0].Emblem);
        foreach (var trait in culture.Traits)
            Assert.Contains(page.Tabs[0].Lines, l => l.StartsWith("・" + DemoCultures.TraitName(trait), StringComparison.Ordinal)
                && l.Contains(DemoBeliefDetail.Description(trait), StringComparison.Ordinal));
        Assert.Contains(page.Tabs[1].Lines, l => l.Contains(settlement.Name, StringComparison.Ordinal));
        Assert.DoesNotContain("TEMP", page.Text, StringComparison.Ordinal);

        var faith = world.Culture.Faith.Faiths.First();
        var faithPage = world.FaithDetail(faith.Id)!;
        Assert.Equal(new[] { "信條", "信徒", "歷史" }, faithPage.Tabs.Select(t => t.Title));
        Assert.NotNull(faithPage.Tabs[0].FaithSymbol);
        Assert.Contains(faithPage.Tabs[0].Lines, l => l.StartsWith("聖地：", StringComparison.Ordinal));
        foreach (var id in faith.Tenets.Concat(faith.Doctrines))
            Assert.Contains(faithPage.Tabs[0].Lines, l => l.StartsWith("・" + DemoFaiths.EntryName(id), StringComparison.Ordinal));
        Assert.Contains(faithPage.Tabs[2].Lines, l => l.Contains("創立", StringComparison.Ordinal) || l.Contains("信仰", StringComparison.Ordinal));
        Assert.DoesNotContain("TEMP", faithPage.Text, StringComparison.Ordinal);

        Assert.Null(world.CultureDetail(999_999));
        Assert.Null(world.FaithDetail(999_999));
        Assert.Equal(before, world.ExportTempSave()); // 只讀
    }
}
