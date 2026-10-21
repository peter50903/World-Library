using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 06-1 章：地圖生成改變後，固定種子 42 不一定還有需要的地形組合。測試改用「符合條件的第一個種子」
/// （確定性搜尋，不靠運氣），條件寫在名稱上。
/// </summary>
internal static class TestSeeds
{
    private static readonly Lazy<ulong> NormalMixed = new(() =>
    {
        for (ulong seed = 1; seed <= 500; seed++)
        {
            var sites = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.Sample).Sites;
            var kinds = sites.ResourcePoints.Select(p => p.Kind).ToHashSet();
            if (kinds.Contains(DemoResourceKind.Woodland) && kinds.Contains(DemoResourceKind.Uncultivated))
                return seed;
        }
        throw new InvalidOperationException("找不到同時有林地與未開墾草原聚落的一般示範種子。");
    });

    private static readonly Lazy<ulong> WoodlandAfterFirst = new(() =>
    {
        for (ulong seed = 1; seed <= 500; seed++)
        {
            var sites = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.Sample).Sites;
            var first = sites.Settlements[0].Id;
            if (sites.ResourcePoints.Any(p => p.SettlementId != first && p.Kind == DemoResourceKind.Woodland))
                return seed;
        }
        throw new InvalidOperationException("找不到第一個聚落以外還有林地聚落的一般示範種子。");
    });

    /// <summary>一般示範情境中，第一個聚落（局部饑荒的指定聚落）以外還有林地聚落的第一個種子。</summary>
    public static ulong NormalWithWoodlandAfterFirst => WoodlandAfterFirst.Value;

    /// <summary>一般示範情境中，開局同時有林地聚落與未開墾草原聚落的第一個種子。</summary>
    public static ulong NormalWithWoodlandAndGrassland => NormalMixed.Value;
}
