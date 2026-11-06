namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-19 章：一格的勢力歸屬——直屬哪個聚落、最上層是哪個勢力。</summary>
public sealed record DemoTerritoryCell(int SettlementId, int RealmId);

/// <summary>第 08-19 章：全圖的勢力範圍與隸屬關係（每週由聚落位置與層級推得，不存檔）。</summary>
public sealed record DemoTerritoryMap(
    IReadOnlyDictionary<DemoCellRef, DemoTerritoryCell> Cells,
    IReadOnlyDictionary<int, int> Liege,
    IReadOnlyDictionary<int, int> Realm)
{
    public static DemoTerritoryMap Empty { get; } = new(new Dictionary<DemoCellRef, DemoTerritoryCell>(), new Dictionary<int, int>(), new Dictionary<int, int>());

    /// <summary>直屬的上級聚落（沒有＝獨立）。</summary>
    public int? LiegeOf(int settlementId) => Liege.TryGetValue(settlementId, out var l) ? l : null;

    /// <summary>最上層的勢力（獨立的聚落就是自己）。</summary>
    public int RealmOf(int settlementId) => Realm.TryGetValue(settlementId, out var r) ? r : settlementId;

    /// <summary>直屬於某聚落的下級聚落（依編號）。</summary>
    public IReadOnlyList<int> VassalsOf(int settlementId) => Liege.Where(kv => kv.Value == settlementId).Select(kv => kv.Key).OrderBy(id => id).ToList();
}

/// <summary>
/// 第 08-19 章（使用者 2026-11-06：村莊就要有自己的勢力範圍；城鎮的勢力範圍內可以包含村莊，隸屬於城鎮之下，其他以此類推）。
/// <list type="number">
/// <item>每個有人的聚落都有勢力範圍：以聚落格為中心、依層級的半徑【TEMP】（營地 1、村莊 2、城鎮 4、城市 6、首都 8 格，切比雪夫距離）。</item>
/// <item>隸屬：一個聚落落在更高層級聚落的勢力範圍內，就隸屬於它——有好幾個時選層級最接近的（村莊先屬城鎮、城鎮再屬城市），
///   同層級選最近、再選編號小的。層級只會往上，所以不會繞圈。</item>
/// <item>格的歸屬：每一格陸地屬於範圍涵蓋它的聚落中最近的一個（同距離選層級高、再選編號小）；那個聚落最上層的勢力就是這格的勢力。</item>
/// </list>
/// 純函式：同一張地圖、同樣的聚落與層級，結果永遠一樣；不抽亂數、不存檔，暫時只影響畫面與詳情（之後的稅收、徵兵、戰爭再接上）。
/// </summary>
public static class DemoTerritory
{
    /// <summary>【TEMP】勢力範圍半徑（格）。</summary>
    public static int TempRadius(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Village => 2,
        DemoSettlementTier.Town => 4,
        DemoSettlementTier.City => 6,
        DemoSettlementTier.Capital => 8,
        _ => 1
    };

    public static DemoTerritoryMap Compute(DemoHexWorld map, DemoWorldSites sites, Func<int, DemoSettlementTier> tierOf)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(tierOf);
        var alive = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        var tiers = alive.ToDictionary(s => s.Id, s => tierOf(s.Id));
        DemoCellRef At(DemoSettlement s) => new(s.Column, s.Row);

        // 隸屬：層級更高、範圍涵蓋本聚落的聚落裡，層級最接近、最近、編號小的。
        var liege = new Dictionary<int, int>();
        foreach (var s in alive)
        {
            var best = alive.Where(t => tiers[t.Id] > tiers[s.Id] && DemoHexWorld.Distance(At(t), At(s)) <= TempRadius(tiers[t.Id]))
                .OrderBy(t => tiers[t.Id]).ThenBy(t => DemoHexWorld.Distance(At(t), At(s))).ThenBy(t => t.Id)
                .FirstOrDefault();
            if (best is not null)
                liege[s.Id] = best.Id;
        }
        var realm = new Dictionary<int, int>();
        int Top(int id)
        {
            if (realm.TryGetValue(id, out var known))
                return known;
            return realm[id] = liege.TryGetValue(id, out var up) ? Top(up) : id;
        }
        foreach (var s in alive)
            Top(s.Id);

        // 格的歸屬：涵蓋這一格的聚落裡最近的（同距離層級高、編號小）。
        var owners = new Dictionary<DemoCellRef, (int Distance, int Tier, int Id)>();
        foreach (var s in alive)
        {
            var tier = (int)tiers[s.Id];
            foreach (var cell in map.Within(map.GetCell(s.Column, s.Row), TempRadius(tiers[s.Id])))
            {
                var at = new DemoCellRef(cell.Column, cell.Row);
                if (cell.Terrain == DemoTerrain.Water && at != At(s))
                    continue;
                var d = DemoHexWorld.Distance(cell, map.GetCell(s.Column, s.Row));
                if (!owners.TryGetValue(at, out var held) || d < held.Distance || d == held.Distance && (tier > held.Tier || tier == held.Tier && s.Id < held.Id))
                    owners[at] = (d, tier, s.Id);
            }
        }
        var cells = owners.ToDictionary(kv => kv.Key, kv => new DemoTerritoryCell(kv.Value.Id, realm[kv.Value.Id]));
        return new DemoTerritoryMap(cells, liege, realm);
    }

    /// <summary>詳情頁的一行：獨立或隸屬於誰（最上層是誰）、轄下幾個聚落。</summary>
    public static string Describe(DemoTerritoryMap territory, DemoWorldSites sites, int settlementId, Func<int, DemoSettlementTier> tierOf)
    {
        ArgumentNullException.ThrowIfNull(territory);
        ArgumentNullException.ThrowIfNull(sites);
        string Name(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id) is { } s ? DemoSettlementHistory.TitledName(s, tierOf(id)) : $"#{id}";
        var vassals = territory.VassalsOf(settlementId);
        var under = vassals.Count == 0 ? "" : $"｜轄下 {vassals.Count} 個聚落：{string.Join("、", vassals.Select(Name))}";
        if (territory.LiegeOf(settlementId) is not { } liege)
            return $"勢力：獨立{under}";
        var top = territory.RealmOf(settlementId);
        return $"勢力：隸屬於{Name(liege)}" + (top != liege ? $"（最上層：{Name(top)}）" : "") + under;
    }
}
