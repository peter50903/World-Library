namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-11 章（使用者 2026-10-28：不重要的遺跡就還原成原本的土地）：從未發展為村莊的遺跡，
/// 荒廢 <see cref="TempFadeYears"/> 年後從地圖與聚落清單消失（那一格只顯示原本的地形），
/// 編年史、聚落小史與事件仍保留。只影響呈現，不改世界狀態。曾是村莊以上的遺跡一直留著。
/// </summary>
public static class DemoRuins
{
    /// <summary>【TEMP】不重要的遺跡荒廢幾年後消失。</summary>
    public const int TempFadeYears = 5;

    /// <summary>重要的遺跡：曾發展為村莊以上（聚落小史有升級紀錄）。</summary>
    public static bool IsImportant(DemoObserverWorld world, DemoSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(settlement);
        return world.ChronicleOf(settlement.Id).Milestones.Any(l =>
            DemoSettlementChronicle.IsTierChange(l) && l.Text.Contains(DemoSettlementChronicle.TierUpMarker, StringComparison.Ordinal));
    }

    /// <summary>已消失的遺跡（地圖與清單不再顯示）。</summary>
    public static bool IsFaded(DemoObserverWorld world, DemoSettlement settlement)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(settlement);
        return IsFaded(world, settlement, world.Snapshot().DaysElapsed);
    }

    private static bool IsFaded(DemoObserverWorld world, DemoSettlement settlement, int day)
        => settlement.IsRuin && day / 7 - settlement.RuinedWeek >= TempFadeYears * DemoSettlementHistory.DaysPerYear / 7
           && !IsImportant(world, settlement);

    /// <summary>目前已消失的遺跡編號。</summary>
    public static IReadOnlySet<int> Faded(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var day = world.Snapshot().DaysElapsed;
        return world.Sites.Settlements.Where(s => IsFaded(world, s, day)).Select(s => s.Id).ToHashSet();
    }
}
