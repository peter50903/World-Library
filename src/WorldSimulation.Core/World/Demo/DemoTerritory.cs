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

/// <summary>第 08-19a 章：算勢力範圍時要看的「人實際去的地方」——土地開發、伐木採礦、會不會狩獵（沒有＝只算建築）。</summary>
public sealed record DemoTerritoryWork(DemoLandDevelopmentMap Land, IReadOnlyList<DemoEnvironmentSpell> Spells, int Week, Func<int, bool> Hunts);

/// <summary>
/// 第 08-19 章（使用者 2026-11-06：村莊就要有自己的勢力範圍；城鎮的勢力範圍內可以包含村莊，隸屬於城鎮之下，其他以此類推）。
/// 第 08-19a 章（使用者 2026-11-08：各勢力的範圍是按照建築、人口移動範圍來計算的）：格的歸屬不再是固定半徑，
/// 而是聚落實際用到的地——建築佔的格、人去工作的格（農田、開墾地、伐木、採礦、獵場）與從家走過去經過的格；
/// 依層級的半徑（<see cref="TempRadius"/>）只剩「影響範圍」，用來決定隸屬。
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

    /// <summary>【TEMP】第 08-19a 章：建築佔幾格（營 1、村 3、鎮 5、城 10、都 16，同畫面的聚落佔地）；建築與工作地離聚落最遠幾格。</summary>
    public static int TempBuildingCells(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Village => 3,
        DemoSettlementTier.Town => 5,
        DemoSettlementTier.City => 10,
        DemoSettlementTier.Capital => 16,
        _ => 1
    };

    public const int TempBuildingReach = 2, TempWorkReach = 3;

    /// <summary>
    /// 第 08-19a 章：一個聚落實際用到的格（聚落格在第一個）。
    /// <list type="number">
    /// <item><b>建築</b>：聚落格加最近的陸地格，共 <see cref="TempBuildingCells"/> 格（不跨別的聚落格，離聚落最多 <see cref="TempBuildingReach"/> 格；近的先、同距離依列、欄）。</item>
    /// <item><b>工作地</b>（有 <paramref name="work"/> 時）：離聚落 <see cref="TempWorkReach"/> 格內的農田與開墾地、自己正在伐木與採礦的格、
    ///   會狩獵時最近的森林格。</item>
    /// <item><b>人走的路</b>：從聚落格直線走到每個工作地經過的陸地格。</item>
    /// </list>
    /// 水域不算（聚落格本身除外）。
    /// </summary>
    public static IReadOnlyList<DemoCellRef> Footprint(DemoHexWorld map, DemoWorldSites sites, DemoSettlement settlement, DemoSettlementTier tier, DemoTerritoryWork? work)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(settlement);
        var home = new DemoCellRef(settlement.Column, settlement.Row);
        var origin = map.GetCell(settlement.Column, settlement.Row);
        bool Land(DemoHexCell c) => c.Terrain != DemoTerrain.Water;
        var cells = new List<DemoCellRef> { home };
        var seen = new HashSet<DemoCellRef> { home };
        void Add(DemoCellRef c)
        {
            if (map.Contains(c.Column, c.Row) && Land(map.GetCell(c.Column, c.Row)) && seen.Add(c))
                cells.Add(c);
        }
        foreach (var c in map.Within(origin, TempBuildingReach).Where(c => Land(c) && sites.SettlementAt(c.Column, c.Row) is null)
                     .OrderBy(c => DemoHexWorld.Distance(c, origin)).ThenBy(c => c.Row).ThenBy(c => c.Column).Take(TempBuildingCells(tier) - 1))
            Add(new DemoCellRef(c.Column, c.Row));
        if (work is null)
            return cells;
        var targets = new List<DemoCellRef>();
        targets.AddRange(map.Within(origin, TempWorkReach).Select(c => new DemoCellRef(c.Column, c.Row))
            .Where(c => work.Land.StateOf(c) is DemoLandDevelopment.Farmland or DemoLandDevelopment.Clearing && sites.SettlementAt(c.Column, c.Row) is null)
            .OrderBy(c => DemoHexWorld.Distance(c, home)).ThenBy(c => c.Row).ThenBy(c => c.Column));
        foreach (var spell in work.Spells.Where(x => x.SettlementId == settlement.Id && x.FromWeek < work.Week && work.Week <= x.ToWeek
                     && x.Kind is DemoSettlementEventKind.Logging or DemoSettlementEventKind.MiningBegan))
            targets.AddRange(spell.Cells.Where(c => DemoHexWorld.Distance(c, home) <= TempWorkReach));
        if (work.Hunts(settlement.Id) && map.Within(origin, TempBuildingReach).Where(c => c.Terrain == DemoTerrain.Forest)
                .OrderBy(c => DemoHexWorld.Distance(c, origin)).ThenBy(c => c.Row).ThenBy(c => c.Column).FirstOrDefault() is { } forest)
            targets.Add(new DemoCellRef(forest.Column, forest.Row));
        foreach (var target in targets)
        {
            // 人從家直線走過去（切比雪夫步）：經過的格與目的格都算。
            var steps = DemoHexWorld.Distance(home, target);
            for (var k = 1; k <= steps; k++)
                Add(new DemoCellRef(home.Column + (int)Math.Round((target.Column - home.Column) * (double)k / steps, MidpointRounding.AwayFromZero),
                    home.Row + (int)Math.Round((target.Row - home.Row) * (double)k / steps, MidpointRounding.AwayFromZero)));
        }
        return cells;
    }

    public static DemoTerritoryMap Compute(DemoHexWorld map, DemoWorldSites sites, Func<int, DemoSettlementTier> tierOf, DemoTerritoryWork? work = null)
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

        // 第 08-19a 章 格的歸屬：聚落實際用到的格（建築、工作地、人走的路）；兩個聚落都用到時歸最近的（同距離層級高、編號小）。
        var owners = new Dictionary<DemoCellRef, (int Distance, int Tier, int Id)>();
        foreach (var s in alive)
        {
            var tier = (int)tiers[s.Id];
            foreach (var at in Footprint(map, sites, s, tiers[s.Id], work))
            {
                var d = DemoHexWorld.Distance(at, At(s));
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

    /// <summary>
    /// 試玩回饋 2026-11-07 之 4：勢力詳情（遠看點地圖時打開）。標題是最上層聚落的名字；「概況」寫層級、轄下聚落數、勢力人口與範圍；
    /// 「隸屬」由上而下列出整棵隸屬樹（每層縮排，同層依層級高、人口多、編號小排列）。<paramref name="realmId"/> 不是最上層時改用它的最上層。
    /// </summary>
    public static DemoPlayerDetail Detail(DemoTerritoryMap territory, DemoWorldSites sites, int realmId, Func<int, DemoSettlementTier> tierOf, Func<int, long> peopleOf)
    {
        ArgumentNullException.ThrowIfNull(territory);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(tierOf);
        ArgumentNullException.ThrowIfNull(peopleOf);
        var top = territory.RealmOf(realmId);
        var byId = sites.Settlements.ToDictionary(s => s.Id);
        string Name(int id) => byId.TryGetValue(id, out var s) ? DemoSettlementHistory.TitledName(s, tierOf(id)) : $"#{id}";
        var members = territory.Realm.Where(kv => kv.Value == top).Select(kv => kv.Key).Append(top).Distinct().ToList();
        var people = members.Sum(peopleOf);
        var cells = territory.Cells.Values.Count(c => c.RealmId == top);
        IEnumerable<int> Ordered(IEnumerable<int> ids) => ids.OrderByDescending(id => tierOf(id)).ThenByDescending(peopleOf).ThenBy(id => id);
        var tree = new List<string>();
        void Walk(int id, int depth)
        {
            tree.Add($"{new string('　', depth)}{(depth > 0 ? "└ " : "")}{Name(id)}（{DemoSettlementHistory.TierName(tierOf(id))}，人口 {peopleOf(id)}）");
            foreach (var vassal in Ordered(territory.VassalsOf(id)))
                Walk(vassal, depth + 1);
        }
        Walk(top, 0);
        var direct = territory.VassalsOf(top).Count;
        var summary = new List<string>
        {
            $"最上層：{Name(top)}",
            $"轄下聚落：{members.Count - 1} 個（直屬 {direct} 個）",
            $"勢力人口：{people}",
            $"勢力範圍：{cells} 格"
        };
        return new DemoPlayerDetail($"{Name(top)}（勢力）", new[] { new DemoDetailTab("概況", summary), new DemoDetailTab("隸屬", tree) }, top);
    }
}
