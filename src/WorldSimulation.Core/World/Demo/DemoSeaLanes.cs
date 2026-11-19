namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-42 章：一條航線——兩個港口之間最短的海路（<see cref="Path"/>＝沿途的海格，從 <see cref="From"/> 那一端起）。
/// 路上有海上怪物佔據的格＝被那個地下城封鎖（<see cref="BlockedBy"/>＝地下城的格編號，0＝暢通）。
/// </summary>
public sealed record DemoSeaLane(int From, int To, IReadOnlyList<DemoCellRef> Path, long BlockedBy = 0, string BlockedName = "", int BlockedAt = -1)
{
    /// <remarks><see cref="BlockedAt"/>＝路上第一格被怪物佔的海在 <see cref="Path"/> 的第幾格（地圖畫封鎖記號用；暢通＝−1）。</remarks>
    public bool Blocked => BlockedBy != 0;

    /// <summary>海路幾格。</summary>
    public int Length => Path.Count;
}

/// <summary>
/// 第 08-42 章【TEMP 規則】航線（使用者 2026-11-19：怪物會阻擋船隻航線，人類需要透過海戰消滅怪物）。
/// <list type="bullet">
/// <item>港口＝有完好港口建築（<see cref="PortBuilding"/>）的聚落。每個港口沿海（八方向、只走海）找 <see cref="TempLaneReach"/> 格內最近的
///   <see cref="TempLanesPerPort"/> 個港口連成航線；兩個港口直線不到 <see cref="DemoFoodTrade.TempTradeReach"/> 格的不連（走陸路就到）。</item>
/// <item>航線走最短的海路；路上有海上怪物佔據的格＝被封鎖，船過不去，直到海戰把怪物殺光（範圍縮回去也會重新暢通）。</item>
/// <item>海運：賣糧的聚落可以經 <see cref="DemoFoodTrade.TempTradeReach"/> 格內的港口，沿暢通的航線賣給對岸的市集；
///   路程＝到港口的格數＋海路每 <see cref="TempSeaCellsPerStep"/> 格算 1 格，最遠 <see cref="TempSeaTradeReach"/>（比陸路的市集遠，陸路的滿了才走海路）。</item>
/// </list>
/// 由目前的港口與怪物範圍重算，不另存。
/// </summary>
public static class DemoSeaLanes
{
    /// <summary>港口建築。</summary>
    public const string PortBuilding = "BUILD_PORT";

    /// <summary>【TEMP】航線最長幾格海路；每個港口最多連幾個港口。</summary>
    public const int TempLaneReach = 30, TempLanesPerPort = 3;

    /// <summary>【TEMP】海運：海路每幾格算 1 格路程；最遠幾格路程。</summary>
    public const int TempSeaCellsPerStep = 3, TempSeaTradeReach = 12;

    private static bool IsSea(DemoHexWorld map, int column, int row)
        => map.Contains(column, row) && map.GetCell(column, row) is { Terrain: DemoTerrain.Water, Sea: true };

    /// <summary>
    /// 港口之間的航線（依兩端編號排序）。<paramref name="nests"/>＝目前的怪物（只有海上、有怪物的地下城佔的海格會封鎖；依序先到先算）。
    /// </summary>
    public static IReadOnlyList<DemoSeaLane> Build(DemoHexWorld map, IEnumerable<DemoSettlement> ports, IReadOnlyList<DemoMonsterNest> nests)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(ports);
        ArgumentNullException.ThrowIfNull(nests);
        var list = ports.OrderBy(p => p.Id).ToList();
        if (list.Count < 2)
            return Array.Empty<DemoSeaLane>();
        var columns = map.Columns;
        int Index(int c, int r) => r * columns + c;
        // 每一格海碰得到哪些港口（港口在陸地，周圍一格的海就是它的碼頭）。
        var touching = new Dictionary<int, List<int>>();
        foreach (var p in list)
            for (var dr = -1; dr <= 1; dr++)
                for (var dc = -1; dc <= 1; dc++)
                    if ((dr != 0 || dc != 0) && IsSea(map, p.Column + dc, p.Row + dr))
                    {
                        var at = Index(p.Column + dc, p.Row + dr);
                        (touching.TryGetValue(at, out var near) ? near : touching[at] = new List<int>()).Add(p.Id);
                    }
        var monster = new Dictionary<DemoCellRef, DemoMonsterNest>();
        foreach (var nest in nests.Where(n => n.AtSea && !n.Dormant && n.Monsters > 0))
            foreach (var c in nest.Cells)
                if (IsSea(map, c.Column, c.Row))
                    monster.TryAdd(c, nest);
        var byId = list.ToDictionary(p => p.Id);
        var lanes = new SortedDictionary<(int, int), DemoSeaLane>();
        foreach (var p in list)
        {
            var dist = new Dictionary<int, int>();
            var parent = new Dictionary<int, int>();
            var queue = new Queue<int>();
            for (var dr = -1; dr <= 1; dr++)
                for (var dc = -1; dc <= 1; dc++)
                    if ((dr != 0 || dc != 0) && IsSea(map, p.Column + dc, p.Row + dr) && dist.TryAdd(Index(p.Column + dc, p.Row + dr), 1))
                    {
                        parent[Index(p.Column + dc, p.Row + dr)] = -1;
                        queue.Enqueue(Index(p.Column + dc, p.Row + dr));
                    }
            var found = new List<(int Port, int End)>();
            var seen = new HashSet<int> { p.Id };
            while (queue.Count > 0 && found.Count < TempLanesPerPort)
            {
                var here = queue.Dequeue();
                if (touching.TryGetValue(here, out var near))
                    foreach (var q in near)
                        if (seen.Add(q) && DemoHexWorld.Distance(new DemoCellRef(p.Column, p.Row), new DemoCellRef(byId[q].Column, byId[q].Row)) > DemoFoodTrade.TempTradeReach)
                            found.Add((q, here));
                if (dist[here] >= TempLaneReach)
                    continue;
                var (hc, hr) = (here % columns, here / columns);
                for (var dr = -1; dr <= 1; dr++)
                    for (var dc = -1; dc <= 1; dc++)
                    {
                        if ((dr == 0 && dc == 0) || !IsSea(map, hc + dc, hr + dr))
                            continue;
                        var n = Index(hc + dc, hr + dr);
                        if (!dist.TryAdd(n, dist[here] + 1))
                            continue;
                        parent[n] = here;
                        queue.Enqueue(n);
                    }
            }
            foreach (var (q, end) in found.Take(TempLanesPerPort))
            {
                var key = (Math.Min(p.Id, q), Math.Max(p.Id, q));
                if (lanes.ContainsKey(key))
                    continue;
                var path = new List<DemoCellRef>();
                for (var at = end; at >= 0; at = parent[at])
                    path.Add(new DemoCellRef(at % columns, at / columns));
                path.Reverse(); // 從 p 那一端起
                if (p.Id != key.Item1)
                    path.Reverse();
                var hit = path.FindIndex(monster.ContainsKey);
                var block = hit < 0 ? null : monster[path[hit]];
                lanes[key] = new DemoSeaLane(key.Item1, key.Item2, path, block?.CellId ?? 0, block?.Dungeon.Name ?? "", hit);
            }
        }
        return lanes.Values.ToList();
    }

    /// <summary>
    /// 海運：某個賣糧的聚落（在 <paramref name="seller"/>）經暢通的航線賣得到的對岸市集與路程（還要是市集、有空間，由貿易判斷）。
    /// <paramref name="ports"/>＝港口所在格。
    /// </summary>
    public static IEnumerable<(int Market, int Distance)> Offers(IReadOnlyList<DemoSeaLane> lanes, IReadOnlyDictionary<int, DemoCellRef> ports, DemoCellRef seller)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(ports);
        foreach (var lane in lanes.Where(l => !l.Blocked))
            foreach (var (here, there) in new[] { (lane.From, lane.To), (lane.To, lane.From) })
            {
                if (!ports.TryGetValue(here, out var port) || !ports.ContainsKey(there))
                    continue;
                var walk = DemoHexWorld.Distance(seller, port);
                if (walk > DemoFoodTrade.TempTradeReach)
                    continue;
                var trip = walk + (lane.Length + TempSeaCellsPerStep - 1) / TempSeaCellsPerStep;
                if (trip <= TempSeaTradeReach)
                    yield return (there, trip);
            }
    }
}
