using System.Collections.Immutable;

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

    /// <summary>【TEMP】第 08-44 章：開拓隊渡海最遠幾格海路（有港口的、只會基礎造船的）。</summary>
    public const int TempPortVoyageReach = 16, TempBoatVoyageReach = 8;

    /// <summary>
    /// 第 08-44 章【TEMP 規則】航海殖民：從 <paramref name="home"/>（靠海的聚落）沿海（八方向、只走海、避開 <paramref name="blocked"/> 海上怪物佔的海）
    /// 走 <paramref name="reach"/> 格內，找靠岸的**海外**空地（陸路走不到的陸地；<paramref name="siteOk"/> 允許、有水源）。
    /// 給了 <paramref name="score"/> 就挑分數最高的（同分取先找到的、較近的），否則取第一個。
    /// 回傳的路線：聚落 → 沿途每一格海（每週一格，與陸路一樣不瞬移，M1_MIG）→ 上岸的空地；找不到＝空。
    /// </summary>
    public static ImmutableArray<DemoCellRef> Voyage(DemoHexWorld map, DemoCellRef home, int reach,
        IReadOnlySet<DemoCellRef> blocked, Func<DemoCellRef, bool> siteOk, Func<DemoCellRef, long>? score = null, bool wells = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(blocked);
        ArgumentNullException.ThrowIfNull(siteOk);
        var none = ImmutableArray<DemoCellRef>.Empty;
        if (reach <= 0 || !map.Contains(home.Column, home.Row))
            return none;
        var homeland = map.LandTree(home);
        var parent = new Dictionary<DemoCellRef, DemoCellRef?>();
        var dist = new Dictionary<DemoCellRef, int>();
        var queue = new Queue<DemoCellRef>();
        bool Open(DemoCellRef c) => IsSea(map, c.Column, c.Row) && !blocked.Contains(c);
        foreach (var n in Ring(home))
            if (Open(n) && dist.TryAdd(n, 1))
            {
                parent[n] = null;
                queue.Enqueue(n);
            }
        (DemoCellRef Sea, DemoCellRef Land)? best = null;
        var bestScore = long.MinValue;
        var looked = new HashSet<DemoCellRef>();
        while (queue.Count > 0)
        {
            var here = queue.Dequeue();
            foreach (var land in Ring(here))
            {
                if (!map.Contains(land.Column, land.Row) || map.GetCell(land.Column, land.Row).Terrain == DemoTerrain.Water
                    || homeland.ContainsKey(land) || !looked.Add(land) || !siteOk(land) || !DemoMigrationRoutes.Habitable(map, land, wells))
                    continue;
                if (score is null)
                    return Route(here, land);
                var value = score(land);
                if (value > bestScore)
                {
                    bestScore = value;
                    best = (here, land);
                }
            }
            if (dist[here] >= reach)
                continue;
            foreach (var n in Ring(here))
                if (Open(n) && dist.TryAdd(n, dist[here] + 1))
                {
                    parent[n] = here;
                    queue.Enqueue(n);
                }
        }
        return best is { } b ? Route(b.Sea, b.Land) : none;

        ImmutableArray<DemoCellRef> Route(DemoCellRef lastSea, DemoCellRef landing)
        {
            var sea = new List<DemoCellRef>();
            for (DemoCellRef? at = lastSea; at is { } c; at = parent[c])
                sea.Add(c);
            sea.Reverse();
            var path = new List<DemoCellRef> { home };
            path.AddRange(sea);
            path.Add(landing);
            return path.ToImmutableArray();
        }
    }

    private static IEnumerable<DemoCellRef> Ring(DemoCellRef c)
    {
        for (var dr = -1; dr <= 1; dr++)
            for (var dc = -1; dc <= 1; dc++)
                if (dr != 0 || dc != 0)
                    yield return new DemoCellRef(c.Column + dc, c.Row + dr);
    }

    /// <summary>
    /// 第 08-44 章：路線上相鄰兩點走得通——陸路路線段，或上下船（陸地與相鄰的海），或海上一步（相鄰的兩格海）。
    /// </summary>
    public static bool Passable(DemoHexWorld map, DemoCellRef a, DemoCellRef b)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (map.Routes.IsLandPassable(a, b))
            return true;
        if (!map.Contains(a.Column, a.Row) || !map.Contains(b.Column, b.Row))
            return false;
        var (seaA, seaB) = (IsSea(map, a.Column, a.Row), IsSea(map, b.Column, b.Row));
        var d = DemoHexWorld.Distance(a, b);
        return seaA && seaB ? d == 1
            : seaA != seaB && d == 1 && map.GetCell((seaA ? b : a).Column, (seaA ? b : a).Row).Terrain != DemoTerrain.Water;
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
