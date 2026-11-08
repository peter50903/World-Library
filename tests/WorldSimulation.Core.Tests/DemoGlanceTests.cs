using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-18i 章：聚落現況的頁首四塊、編年史把開墾農田合計成一條。</summary>
public sealed class DemoGlanceTests
{
    [Fact]
    public void SettlementPageStartsWithFourGlanceLinesAndFarmlandIsSummedPerYear()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 7 * 120; d++)
            world.AdvanceOneDay();
        var site = world.Sites.Settlements.First(s => !s.IsRuin);
        var glance = DemoPlayerDetail.Glance(world, site);
        Assert.Equal(new[] { "【人口】", "【糧食】", "【隸屬】", "【危機】" }, glance.Select(l => l[..4]));
        var now = DemoPlayerDetail.Of(world, site.Column, site.Row).Tabs[0].Lines;
        Assert.Equal(glance, now.Skip(1).Take(4));
        Assert.Contains("── 詳細 ──", now);
        Assert.DoesNotContain(now, l => l.Contains("的傳統：", StringComparison.Ordinal) || l.Contains("的信條：", StringComparison.Ordinal));
        // 開墾農田：每年每聚落一條一般事件，塊數等於當年的開墾事件數。
        var farmland = DemoWorldChronicle.Build(world).SelectMany(y => y.Entries).Where(e => e.Category == DemoChronicleCategory.Farmland
            && e.Segments.Any(s => s.Text.Contains("開墾", StringComparison.Ordinal))).ToList();
        Assert.All(farmland, e => Assert.Equal(DemoChronicleLevel.Normal, e.Level));
        var cleared = world.SettlementEvents().Count(e => e.Kind == DemoSettlementEventKind.FarmlandCleared);
        Assert.Equal(cleared, farmland.Sum(e => int.Parse(e.Segments.Last().Text.Split(' ')[2], System.Globalization.CultureInfo.InvariantCulture)));
    }
}
