namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-6 章 c 節【TEMP 規則】農田開墾（使用者 2026-10-23：農耕可以把草原變成農田）。
/// 第 08 章 a 節的土地開發狀態接上真正的開墾行動。純函式、不抽亂數。
/// <list type="bullet">
/// <item>已習得農耕、至少 <see cref="TempMinAdults"/> 名成年人的聚落，每週開墾一塊相鄰草原（本聚落鄰格中
/// 已在開墾的優先，否則依列、欄最小的未開發草原開工）；不是聚落格、不是開拓隊的目的格。</item>
/// <item>每週開墾進度＋<see cref="TempPercentPerWeek"/>%（同一塊地鄰接多個開墾聚落時各自累加），滿 100% 成為農田。</item>
/// <item>農田每週供應 <see cref="TempFarmlandWeeklySupply"/>（草原野採的三倍），可及規則與草原野採相同。</item>
/// <item>開墾的人仍照常採集（第一版不扣勞力）。</item>
/// </list>
/// </summary>
public static class DemoLandClearing
{
    /// <summary>【TEMP】農田每週供應（糧食單位／格）；草原野採是 8。</summary>
    public const long TempFarmlandWeeklySupply = 24;

    /// <summary>【TEMP】每週開墾進度（%）：約一年開好一塊。</summary>
    public const int TempPercentPerWeek = 2;

    /// <summary>【TEMP】開墾所需的最少成年人數。</summary>
    public const long TempMinAdults = 2;

    /// <summary>格的事件編號（與食物來源編號相同：列×欄數＋欄＋1）。</summary>
    public static long CellId(DemoHexWorld map, DemoCellRef cell) => checked((long)cell.Row * map.Columns + cell.Column + 1);

    public static DemoCellRef CellOf(DemoHexWorld map, long id)
        => new((int)((id - 1) % map.Columns), (int)((id - 1) / map.Columns));

    /// <summary>
    /// 依本週提交後的聚落、人口與知識推進開墾；回傳新土地狀態與「開墾完成」事件（編號從 <paramref name="nextEventId"/> 起）。
    /// </summary>
    public static (DemoLandDevelopmentMap Land, IReadOnlyList<DemoSettlementEvent> Events) Advance(
        DemoLandDevelopmentMap land, DemoHexWorld map, DemoWorldSites sites, DemoCohortDemography population,
        DemoKnowledgeState knowledge, IEnumerable<DemoCellRef> foundingDestinations, int week, TimeSpan worldTime, long nextEventId)
    {
        ArgumentNullException.ThrowIfNull(land);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(knowledge);
        var reserved = foundingDestinations.ToHashSet();
        var events = new List<DemoSettlementEvent>();
        var rules = DemoHumanLifecycle.Rules;
        foreach (var settlement in sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id))
        {
            if (!knowledge.Knows(settlement.Id, DemoTechnology.CropCultivation) || population.AdultsIn(settlement.Id, rules) < TempMinAdults)
                continue;
            var target = Candidates(land, map, sites, settlement, reserved)
                .OrderByDescending(c => land.StateOf(c) == DemoLandDevelopment.Clearing)
                .ThenBy(c => c.Row).ThenBy(c => c.Column)
                .Select(c => (DemoCellRef?)c).FirstOrDefault();
            if (target is not { } cell)
                continue;
            var progress = Math.Min(100, (land.ParcelAt(cell)?.DevelopmentPercent ?? 0) + TempPercentPerWeek);
            if (progress < 100)
            {
                land = land.With(map, sites, new DemoLandParcel(cell.Column, cell.Row, DemoLandDevelopment.Clearing, progress));
                continue;
            }
            land = land.With(map, sites, new DemoLandParcel(cell.Column, cell.Row, DemoLandDevelopment.Farmland, 100));
            events.Add(new DemoSettlementEvent(week, worldTime, settlement.Id, settlement.Name,
                DemoSettlementEventKind.FarmlandCleared, CellId(map, cell),
                $"開墾完成：({cell.Column},{cell.Row}) 的草原成為農田【TEMP】", EventId: nextEventId++));
        }
        return (land, events);
    }

    /// <summary>聚落六鄰格中可開墾的草原（未開發或開墾中）。</summary>
    public static IEnumerable<DemoCellRef> Candidates(
        DemoLandDevelopmentMap land, DemoHexWorld map, DemoWorldSites sites, DemoSettlement settlement, ISet<DemoCellRef> reserved)
    {
        var origin = map.GetCell(settlement.Column, settlement.Row);
        foreach (var d in DemoRouteNetwork.Directions)
        {
            var c = map.AtAxial(origin.AxialQ + d.Dq, origin.AxialR + d.Dr);
            if (c is null || c.Terrain != DemoTerrain.Grassland)
                continue;
            var cell = new DemoCellRef(c.Column, c.Row);
            if (sites.SettlementAt(c.Column, c.Row) is { IsRuin: false } || reserved.Contains(cell)
                || land.StateOf(cell) == DemoLandDevelopment.Farmland)
                continue;
            yield return cell;
        }
    }
}
