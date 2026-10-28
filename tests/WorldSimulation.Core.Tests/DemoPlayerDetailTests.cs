using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-11 章：玩家用詳情分頁籤，不含座標、路線、TEMP 等開發數值。</summary>
public sealed class DemoPlayerDetailTests
{
    private static readonly Lazy<DemoObserverWorld> World = new(() =>
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(1), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 30 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        return world;
    });

    [Fact]
    public void SettlementsHaveFiveTabsAndNoDeveloperNumbers()
    {
        var world = World.Value;
        foreach (var s in world.Sites.Settlements)
        {
            var detail = world.PlayerDetail(s.Column, s.Row);
            Assert.Equal(new[] { "現況", "代表人物", "技術", "研究", "歷史" }, detail.Tabs.Select(t => t.Title));
            Assert.Equal(s.Id, detail.SettlementId);
            Assert.StartsWith(s.Name, detail.Title, StringComparison.Ordinal);
            foreach (var word in new[] { "TEMP", "座標", "路線", "實體 ID", "供糧", "資源點" })
                Assert.DoesNotContain(word, detail.Text, StringComparison.Ordinal);
            Assert.All(world.RepresentativesOf(s.Id), r => Assert.Contains(detail.Tabs[1].Lines, l => l.Contains(r.Name, StringComparison.Ordinal)));
            Assert.All(world.Knowledge.KnownBy(s.Id), t => Assert.Contains(DemoKnowledge.Name(t), string.Join("\n", detail.Tabs[2].Lines), StringComparison.Ordinal));
            Assert.True(detail.Tabs[3].Lines.Count <= DemoPlayerDetail.MaxResearchLines);
            Assert.NotEmpty(detail.Tabs[4].Lines);
            if (s.IsRuin)
                Assert.Contains(detail.Tabs[0].Lines, l => l.Contains("無人居住", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TechTabsCarrySkillBadgesWithDomainAndTier()
    {
        var world = World.Value;
        foreach (var s in world.Sites.Settlements.Where(s => !s.IsRuin))
        {
            var detail = world.PlayerDetail(s.Column, s.Row);
            var known = detail.Tabs[2].Badges;
            Assert.Equal(DemoTechnology.All.Count(DemoKnowledge.IsOpening) + world.Knowledge.KnownBy(s.Id).Count, known.Count);
            Assert.All(known, b => Assert.Equal(100, b.Percent));
            Assert.All(known.Where(b => !b.Opening), b => Assert.NotNull(b.LearnedYear));
            Assert.Equal(DemoTechnology.All.Count(DemoKnowledge.IsOpening), known.Count(b => b.Opening));
            Assert.All(detail.Tabs[3].Badges, b => Assert.InRange(b.Percent, 0, 99));
            Assert.All(known.Concat(detail.Tabs[3].Badges), b => Assert.NotEqual("？", b.Glyph));
        }
    }

    [Fact]
    public void UnimportantRuinsFadeBackToLand_ImportantOnesStay()
    {
        var world = World.Value;
        var day = world.Snapshot().DaysElapsed;
        var faded = DemoRuins.Faded(world);
        foreach (var s in world.Sites.Settlements)
        {
            var expected = s.IsRuin && !DemoRuins.IsImportant(world, s)
                && day / 7 - s.RuinedWeek >= DemoRuins.TempFadeYears * DemoSettlementHistory.DaysPerYear / 7;
            Assert.Equal(expected, faded.Contains(s.Id));
            if (!expected) continue;
            var detail = world.PlayerDetail(s.Column, s.Row);
            Assert.Null(detail.SettlementId);
            Assert.Contains(detail.Tabs[0].Lines, l => l.StartsWith($"舊址：{s.Name}", StringComparison.Ordinal));
        }
        Assert.All(world.Sites.Settlements.Where(s => !s.IsRuin), s => Assert.DoesNotContain(s.Id, faded));
    }

    [Fact]
    public void OtherCellsOnlyNameThePlace()
    {
        var world = World.Value;
        var cell = world.Map.Cells.First(c => world.Sites.SettlementAt(c.Column, c.Row) is null);
        var detail = world.PlayerDetail(cell.Column, cell.Row);
        Assert.Null(detail.SettlementId);
        Assert.Equal("地點", Assert.Single(detail.Tabs).Title);
        Assert.Equal(DemoCellQuery.ShortName(world.Map, world.Sites, cell), detail.Title);
        Assert.DoesNotContain("座標", detail.Text, StringComparison.Ordinal);
    }
}
