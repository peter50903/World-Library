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
