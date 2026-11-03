using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>試玩回饋 2026-11-03 之 2：技術的滑鼠說明。</summary>
public sealed class DemoTechTooltipTests
{
    [Fact]
    public void EveryTechnologyHasAPlayerReadableTooltip()
    {
        foreach (var node in DemoTechCatalog.All)
        {
            var text = DemoTechTooltip.Text(node);
            Assert.StartsWith(node.Name + "（", text, StringComparison.Ordinal);
            Assert.Contains("目前作用：", text, StringComparison.Ordinal);
            // 不給玩家看開發用字句與數字。
            foreach (var word in new[] { "TEMP", "從業者", "研究時間", "%", "Claude", "08-", "第 0 階" })
                Assert.DoesNotContain(word, text, StringComparison.Ordinal);
            foreach (var parent in node.Parents)
                Assert.Contains(DemoTechCatalog.Get(parent).Name, text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EffectTableOnlyNamesRealTechnologies_AndCoversTheOnesTheWorldUses()
    {
        Assert.All(DemoTechTooltip.Effects.Keys, id => Assert.True(DemoTechCatalog.Contains(id), id));
        foreach (var id in new[] { "TECH_CROP_CULTIVATION", "TECH_HUNTING", "TECH_ARCHERY", "TECH_WELL_DIGGING", "TECH_ANCESTOR_RITES",
                     DemoSettlementHistory.CapitalTechId, DemoWildlife.HuntingTech, DemoKnowledge.WellDiggingId })
            Assert.True(DemoTechTooltip.Effects.ContainsKey(id), id);
        foreach (var (techId, _) in DemoKnowledge.TempToolLadder)
            Assert.True(DemoTechTooltip.Effects.ContainsKey(techId), techId);
        foreach (var tier in new[] { DemoSettlementTier.Village, DemoSettlementTier.Town })
            foreach (var facility in DemoSettlementHistory.FacilitiesOf(tier).Where(f => f.TechId is not null))
                Assert.True(DemoTechTooltip.Effects.ContainsKey(facility.TechId!), facility.TechId);
    }

    [Fact]
    public void SummaryDropsDeveloperNotes()
    {
        Assert.Equal("糾紛與繼承的基礎", DemoTechTooltip.Summary("【TEMP】研究時間由 Claude 暫定（08-7b 第 2 節）；糾紛與繼承的基礎；從業者＝村長＋老人"));
        Assert.Equal("", DemoTechTooltip.Summary(""));
    }
}
