using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-19d 章：時代進度條與征服時代（第一個王國成立）。</summary>
public sealed class DemoEraProgressTests
{
    [Fact]
    public void KingdomNeedsAnIndependentCapitalWithKingshipAndVassals()
    {
        var alive = new[] { 1, 2, 3, 4 };
        var lieges = new Dictionary<int, int> { [2] = 1, [3] = 2 };
        DemoSettlementTier Tier(int id) => id == 1 ? DemoSettlementTier.Capital : DemoSettlementTier.Village;
        Assert.Equal(1, DemoEraProgress.Kingdom(lieges, alive, Tier, _ => true)); // 直屬 2、間接 3 都算
        Assert.Null(DemoEraProgress.Kingdom(lieges, alive, Tier, _ => false)); // 不會王權
        Assert.Null(DemoEraProgress.Kingdom(new Dictionary<int, int> { [2] = 1 }, alive, Tier, _ => true)); // 只有一個臣屬
        Assert.Null(DemoEraProgress.Kingdom(lieges, alive, _ => DemoSettlementTier.City, _ => true)); // 不是首都
        Assert.Null(DemoEraProgress.Kingdom(lieges, new[] { 1, 2 }, Tier, _ => true)); // 臣屬沒人了
    }

    [Fact]
    public void ProgressIsReadOnlyAndPointsAtTheNextEra()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        var opening = world.EraProgress();
        Assert.Equal(DemoEra.Hope, opening.Current);
        Assert.Equal(DemoEra.Pioneer, opening.Next);
        Assert.InRange(opening.Percent, 0, 99);
        for (var d = 0; d < 7 * 156 && world.Era == DemoEra.Hope; d++)
            world.AdvanceOneDay();
        var before = world.ExportTempSave();
        var progress = world.EraProgress();
        Assert.Equal(before, world.ExportTempSave());
        Assert.Equal(world.Era, progress.Current);
        if (progress.Current == DemoEra.Pioneer)
        {
            Assert.Equal(DemoEra.Conquest, progress.Next);
            Assert.Equal(3, progress.Conditions.Count);
            Assert.All(progress.Conditions, c => Assert.InRange(c.Percent, 0, 100));
            Assert.InRange(progress.Percent, 0, 99);
            Assert.True(progress.Lines(world).Count >= 4);
        }
        Assert.Equal("征服時代", DemoKnowledge.EraName(DemoEra.Conquest));
        Assert.Empty(world.BuildCausalIndex().Problems);
    }
}
