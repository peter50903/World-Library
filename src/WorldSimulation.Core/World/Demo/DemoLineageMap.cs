namespace WorldSimulation.Core.World.Demo;

/// <summary>某一格屬於哪個聚落、哪個家系（家系＝源頭開局聚落的編號）。</summary>
public sealed record DemoLineageCell(int Column, int Row, int SettlementId, int LineageRootId);

/// <summary>
/// 第 08-10a 章 V2：家系地圖（參考 WorldBox 分布圖層、CK3 地圖模式）。每個未成為遺跡的聚落佔本格與相鄰的陸地格
/// （約等於它的採集範圍）；兩個聚落搶同一格時，本格優先，其次編號小者【TEMP 顯示規則】。
/// 家系沿「由哪個聚落的開拓隊建立」追到開局聚落。只讀世界，不改規則。
/// </summary>
public static class DemoLineageMap
{
    /// <summary>某聚落的家系源頭（開局聚落編號）。</summary>
    public static int LineageRoot(DemoWorldSites sites, int settlementId)
    {
        ArgumentNullException.ThrowIfNull(sites);
        var byId = sites.Settlements.ToDictionary(s => s.Id);
        var id = settlementId;
        var guard = 0;
        while (guard++ <= byId.Count
               && byId.TryGetValue(id, out var current)
               && current.IsFounded
               && byId.ContainsKey(current.FoundedFromSettlementId))
        {
            id = current.FoundedFromSettlementId;
        }
        return id;
    }

    /// <summary>家系的固定順序（開局聚落依編號），介面用來配色：同一世界顏色不會隨時間改變。</summary>
    public static IReadOnlyList<int> Roots(DemoWorldSites sites)
    {
        ArgumentNullException.ThrowIfNull(sites);
        return sites.Settlements.Where(s => !s.IsFounded).Select(s => s.Id).OrderBy(id => id).ToList().AsReadOnly();
    }

    /// <summary>每一格的歸屬（沒有聚落的格不在結果裡）。</summary>
    public static IReadOnlyDictionary<DemoCellRef, DemoLineageCell> Compute(DemoHexWorld map, DemoWorldSites sites)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        var owners = new Dictionary<DemoCellRef, (int Distance, int SettlementId)>();
        foreach (var s in sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id))
        {
            var home = map.GetCell(s.Column, s.Row);
            Claim(new DemoCellRef(s.Column, s.Row), 0, s.Id);
            foreach (var (dq, dr, _) in DemoRouteNetwork.Directions)
            {
                if (map.AtAxial(home.AxialQ + dq, home.AxialR + dr) is { Terrain: not DemoTerrain.Water } next)
                    Claim(new DemoCellRef(next.Column, next.Row), 1, s.Id);
            }
        }

        void Claim(DemoCellRef cell, int distance, int settlementId)
        {
            // 依編號由小到大處理，所以同距離時先到者（編號小）保留。
            if (!owners.TryGetValue(cell, out var held) || distance < held.Distance)
                owners[cell] = (distance, settlementId);
        }

        var roots = new Dictionary<int, int>();
        return owners.ToDictionary(
            kv => kv.Key,
            kv => new DemoLineageCell(kv.Key.Column, kv.Key.Row, kv.Value.SettlementId,
                roots.TryGetValue(kv.Value.SettlementId, out var r) ? r : roots[kv.Value.SettlementId] = LineageRoot(sites, kv.Value.SettlementId)));
    }
}
