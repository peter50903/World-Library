using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 12-1 章 C4：世界上的一個地下城（由「地下城出現」事件推得，不另存）。</summary>
public sealed record DemoDungeon(DemoCellRef Cell, string Name, int Layers, int AppearedWeek, long EventId, int NearSettlementId);

/// <summary>
/// 第 12-1 章 C4【TEMP 規則】地下城出現（開拓時代只做出現與記錄；怪物、核心、攻略、溢出留給第 12 章）：
/// 開拓時代起每年第 <see cref="TempYearWeek"/> 週，世界上的地下城少於目標數時以 <see cref="TempChancePercent"/>% 出現 1 個，
/// 位置在丘陵或森林、離每個有人聚落與既有地下城至少 <see cref="TempMinDistance"/> 格。
/// 第 12-2k 章：目標數依地圖格數（<see cref="TargetCount"/>），大地圖每年擲 <see cref="RollsPerYear"/> 次、每次最多 1 個。
/// 名字與層數只依年份與格編號由事件流的實體局部亂數決定（不推進計數器），所以清單可以從事件重算，存檔不必改版。
/// </summary>
public static class DemoDungeons
{
    /// <summary>【TEMP】每年在第幾週檢查（與野生動物、天災錯開）。</summary>
    public const int TempYearWeek = 39;

    /// <summary>【TEMP】少於目標數時，當年出現一個的機率（%）。</summary>
    public const int TempChancePercent = 25;

    /// <summary>【TEMP】離有人聚落與既有地下城的最少格數（第 12-2e 節：原 4 格，小地圖長滿聚落後再也沒有地方出現地下城）。</summary>
    public const int TempMinDistance = 3;

    /// <summary>【TEMP】聚落詳情列出幾格內的地下城。</summary>
    public const int TempNearbyRange = 6;

    /// <summary>【TEMP】層數範圍（開拓時代只有淺層）。</summary>
    public const int TempMinLayers = 1, TempMaxLayers = 3;

    /// <summary>【TEMP】名字後綴（接在地名前綴後面）。</summary>
    public static readonly string[] TempSuffixes = { "洞窟", "古墓", "地窟", "深淵", "迷宮" };

    /// <summary>【TEMP】第 08-42 章：海上地下城的名字後綴。</summary>
    public static readonly string[] TempSeaSuffixes = { "海淵", "漩渦", "沉船", "礁窟", "海底神殿" };

    /// <summary>
    /// 【TEMP】第 08-42 章（使用者 2026-11-19：地下城若是在海中無陸地靠近，怪物要能在海上擴張、自由移動，會擋住船的航線，人類要靠海戰消滅）：
    /// 海上地下城出現在外海——那一格與周圍 <see cref="TempSeaShore"/> 格內都是海、沒有陸地；
    /// 每 <see cref="TempSeaDungeonEvery"/> 個地下城最多一個在海上（第 4、8……個起才可能，開拓初期照舊都在陸地）。
    /// </summary>
    public const int TempSeaShore = 2, TempSeaDungeonEvery = 4;

    /// <summary>【TEMP】第 08-42 章：地下城所在的島（相連的陸地）不到幾格也算海上地下城（小島上的怪物也會下海）。</summary>
    public const int TempSeaIslandCells = 16;

    private const string Namespace = "TEMP-DUNGEON-v1";

    /// <summary>【TEMP】第 12-2k 章：每幾格多一個目標名額。</summary>
    public const int TempCellsPerDungeon = 384;

    /// <summary>【TEMP】第 12-2l 章：挑位置時只從「離既有地下城最近距離」達到最大值這個百分比以上的格裡挑（越大越分散）。</summary>
    public const int TempSpreadPercent = 75;

    /// <summary>【TEMP】第 12-2k 章：每幾個目標名額，每年多擲一次。</summary>
    public const int TempTargetsPerRoll = 3;

    /// <summary>
    /// 目標數＝1＋格數÷<see cref="TempCellsPerDungeon"/>（小 1、中 2、大 10、巨大 30）。
    /// 第 12-2k 章：原為 1＋欄數÷24、最多 3——大地圖兩百個聚落只有 3 個地下城，怪物在大地圖上幾乎不成氣候。
    /// </summary>
    public static int TargetCount(DemoHexWorld map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return 1 + map.Columns * map.Rows / TempCellsPerDungeon;
    }

    /// <summary>第 12-2k 章：每年檢查時擲幾次（每 <see cref="TempTargetsPerRoll"/> 個目標名額一次：小、中 1，大 4，巨大 10）。</summary>
    public static int RollsPerYear(DemoHexWorld map) => (TargetCount(map) + TempTargetsPerRoll - 1) / TempTargetsPerRoll;

    private static ulong Draw(RngRegistry rng, int year, ulong k)
    {
        // 第 14-1 章第 5 項：k ≥ 64（取名重試到第 22 次以後）原本會用到下一年「是否出現地下城」的抽樣；範圍外另用一個命名空間。
        if (k < 64)
            return EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Event, Namespace).GetUInt64((ulong)year * 64 + k);
        return EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Event, Namespace + "-WIDE").GetUInt64(checked((ulong)year * 4096UL + k));
    }

    /// <summary>第 12-2k 章：同一年第 <paramref name="slot"/> 次（第 0 次＝原本的抽樣，結果不變；之後的另用一個命名空間）。</summary>
    private static ulong Draw(RngRegistry rng, int year, int slot, ulong k)
        => slot == 0 ? Draw(rng, year, k)
            : EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Event, Namespace + "-SLOT")
                .GetUInt64(checked(((ulong)year * 64UL + (ulong)slot) * 128UL + k));

    /// <summary>
    /// 名字與層數：只依出現年份、格編號與已用過的名字決定（重算結果相同）。
    /// 第 12-2k 章：<paramref name="slot"/>＝同一週出現的第幾個（第 0 個與原本相同）。
    /// </summary>
    public static (string Name, int Layers) Describe(RngRegistry rng, int year, long cellId, ISet<string> taken, int slot = 0, bool sea = false)
    {
        ArgumentNullException.ThrowIfNull(taken);
        var suffixes = sea ? TempSeaSuffixes : TempSuffixes; // 第 08-42 章：海上地下城
        var prefixes = DemoNameTempSettings.Current.PlacePrefix;
        var max = DemoNameTempSettings.Current.MaxLength;
        var layers = TempMinLayers + (int)(Draw(rng, year, slot, 10) % (ulong)(TempMaxLayers - TempMinLayers + 1));
        for (ulong attempt = 0; attempt < 32; attempt++)
        {
            var name = prefixes[(int)(Draw(rng, year, slot, 20 + attempt * 2) % (ulong)prefixes.Count)]
                + suffixes[(int)(Draw(rng, year, slot, 21 + attempt * 2) % (ulong)suffixes.Length)];
            if (DemoNames.Length(name) <= max && !taken.Contains(name))
                return (name, layers);
        }
        return ($"地下城{cellId}", layers);
    }

    /// <summary>某格可以出現地下城（丘陵或森林、不是聚落或遺跡格、離有人聚落與既有地下城夠遠）。</summary>
    public static bool Eligible(DemoHexWorld map, DemoWorldSites sites, IReadOnlyCollection<DemoSettlement> living,
        IReadOnlyCollection<DemoCellRef> dungeons, DemoHexCell cell)
    {
        if (cell.Terrain is not (DemoTerrain.Hills or DemoTerrain.Forest) || sites.SettlementAt(cell.Column, cell.Row) is not null)
            return false;
        if (living.Any(s => DemoWorldSites.HexDistance(map.GetCell(s.Column, s.Row), cell) < TempMinDistance))
            return false;
        return !dungeons.Any(d => DemoWorldSites.HexDistance(map.GetCell(d.Column, d.Row), cell) < TempMinDistance);
    }

    /// <summary>第 08-42 章：外海的格（海，而且周圍 <see cref="TempSeaShore"/> 格內沒有陸地）。</summary>
    public static bool OpenSea(DemoHexWorld map, DemoHexCell cell)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(cell);
        if (cell.Terrain != DemoTerrain.Water || !cell.Sea)
            return false;
        for (var dr = -TempSeaShore; dr <= TempSeaShore; dr++)
            for (var dc = -TempSeaShore; dc <= TempSeaShore; dc++)
                if (map.Contains(cell.Column + dc, cell.Row + dr) && map.GetCell(cell.Column + dc, cell.Row + dr).Terrain != DemoTerrain.Water)
                    return false;
        return true;
    }

    /// <summary>第 08-42 章：某格可以出現海上地下城（外海、離既有地下城夠遠；外海離聚落本來就夠遠）。</summary>
    public static bool EligibleSea(DemoHexWorld map, IReadOnlyCollection<DemoCellRef> dungeons, DemoHexCell cell)
        => OpenSea(map, cell) && !dungeons.Any(d => DemoWorldSites.HexDistance(map.GetCell(d.Column, d.Row), cell) < TempMinDistance);

    /// <summary>第 08-42 章：已有 <paramref name="total"/> 個地下城、其中 <paramref name="sea"/> 個在海上時，下一個可不可以在海上。</summary>
    public static bool SeaAllowed(int total, int sea) => (sea + 1) * TempSeaDungeonEvery <= total + 1;

    /// <summary>
    /// 第 08-42 章：海上地下城——在海上，或所在的島不到 <see cref="TempSeaIslandCells"/> 格（怪物下海、在海上擴張）。
    /// </summary>
    public static bool AtSea(DemoHexWorld map, DemoCellRef cell)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (!map.Contains(cell.Column, cell.Row))
            return false;
        if (map.GetCell(cell.Column, cell.Row).Terrain == DemoTerrain.Water)
            return true;
        return Island(map, cell, TempSeaIslandCells).Count < TempSeaIslandCells;
    }

    /// <summary>第 08-42 章：和這一格相連（八方向）的陸地，最多找 <paramref name="limit"/> 格。</summary>
    public static HashSet<DemoCellRef> Island(DemoHexWorld map, DemoCellRef cell, int limit)
    {
        ArgumentNullException.ThrowIfNull(map);
        var island = new HashSet<DemoCellRef>();
        if (!map.Contains(cell.Column, cell.Row) || map.GetCell(cell.Column, cell.Row).Terrain == DemoTerrain.Water)
            return island;
        var queue = new Queue<DemoCellRef>();
        island.Add(cell);
        queue.Enqueue(cell);
        while (queue.Count > 0 && island.Count < limit)
        {
            var here = queue.Dequeue();
            for (var dr = -1; dr <= 1; dr++)
                for (var dc = -1; dc <= 1; dc++)
                {
                    var n = new DemoCellRef(here.Column + dc, here.Row + dr);
                    if (island.Count >= limit || !map.Contains(n.Column, n.Row) || map.GetCell(n.Column, n.Row).Terrain == DemoTerrain.Water || !island.Add(n))
                        continue;
                    queue.Enqueue(n);
                }
        }
        return island;
    }

    /// <summary>
    /// 本週出現的地下城事件（不是檢查週、希望時代、已達目標數、機率不過、沒有有人聚落或合格格＝空）。
    /// 第 12-2k 章：一年擲 <see cref="RollsPerYear"/> 次、每次最多 1 個，總數不超過目標數。
    /// 事件掛在最近的有人聚落（同距離取編號小的），人數欄＝格編號，根因。
    /// </summary>
    public static IReadOnlyList<DemoSettlementEvent> Roll(int week, TimeSpan worldTime, DemoEra era, DemoHexWorld map, DemoWorldSites sites,
        IEnumerable<int> livingIds, IReadOnlyList<DemoDungeon> existing, RngRegistry rng, long nextEventId, DemoAge age = DemoAge.Hope) // 第 08-6c 章：黑暗時代
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(existing);
        var limit = TargetCount(map) * DemoAges.DungeonTargetPercent(age) / 100;
        if (era == DemoEra.Hope || week % DemoWildlife.TempYearWeeks != TempYearWeek || existing.Count >= limit)
            return Array.Empty<DemoSettlementEvent>();
        var year = week / DemoWildlife.TempYearWeeks;
        var chance = TempChancePercent * DemoAges.DungeonPercent(age) / 100;
        var passed = Enumerable.Range(0, RollsPerYear(map)).Where(slot => (int)(Draw(rng, year, slot, 0) % 100UL) < chance).ToList();
        if (passed.Count == 0)
            return Array.Empty<DemoSettlementEvent>();
        var living = livingIds.OrderBy(i => i).Select(i => sites.Settlements.First(s => s.Id == i)).ToList();
        if (living.Count == 0)
            return Array.Empty<DemoSettlementEvent>();
        var taken = existing.Select(d => d.Cell).ToList();
        var spots = map.Cells.Where(c => Eligible(map, sites, living, taken, c)).OrderBy(c => c.Row).ThenBy(c => c.Column).ToList();
        // 第 08-42 章：每 TempSeaDungeonEvery 個地下城最多一個在外海（合格的海格另列，輪到可以時才一起挑）。
        var sea = existing.Count(d => map.GetCell(d.Cell.Column, d.Cell.Row).Terrain == DemoTerrain.Water);
        var seaSpots = !SeaAllowed(existing.Count + passed.Count - 1, sea) ? new List<DemoHexCell>()
            : map.Cells.Where(c => EligibleSea(map, taken, c)).ToList();
        var placed = taken.ToList(); // 第 12-2l 章：既有的加上今年已出現的，下一個離它們越遠越好
        var names = existing.Select(d => d.Name).ToHashSet();
        var events = new List<DemoSettlementEvent>();
        foreach (var slot in passed)
        {
            var pool = SeaAllowed(existing.Count + events.Count, sea) && seaSpots.Count > 0
                ? spots.Concat(seaSpots).OrderBy(c => c.Row).ThenBy(c => c.Column).ToList() : spots;
            if (existing.Count + events.Count >= limit || pool.Count == 0)
                break;
            var spot = PickSpread(pool, placed, Draw(rng, year, slot, 1));
            spots.RemoveAll(c => DemoWorldSites.HexDistance(spot, c) < TempMinDistance); // 同一年的下一個也要離它夠遠
            seaSpots.RemoveAll(c => DemoWorldSites.HexDistance(spot, c) < TempMinDistance);
            var atSea = spot.Terrain == DemoTerrain.Water;
            if (atSea)
                sea++;
            placed.Add(new DemoCellRef(spot.Column, spot.Row));
            var near = living.OrderBy(s => DemoWorldSites.HexDistance(map.GetCell(s.Column, s.Row), spot)).ThenBy(s => s.Id).First();
            var cellId = DemoLandClearing.CellId(map, new DemoCellRef(spot.Column, spot.Row));
            var (name, layers) = Describe(rng, year, cellId, names, events.Count, atSea); // 同一週出現的第幾個（重算時 Collect 也這樣數）
            names.Add(name);
            var opening = atSea ? "外海的浪底" : spot.Terrain == DemoTerrain.Hills ? "附近的丘陵" : "附近的森林";
            events.Add(new DemoSettlementEvent(week, worldTime, near.Id, near.Name, DemoSettlementEventKind.DungeonAppeared, cellId,
                $"{opening}裂開了一道入口：{name}（{layers} 層地下城）出現在 ({spot.Column},{spot.Row})【TEMP】",
                EventId: checked(nextEventId + events.Count)));
        }
        return events;
    }

    /// <summary>
    /// 第 12-2l 章（使用者 2026-11-18：怪物散佈的位置太集中）：挑一個出現的位置——世界上還沒有地下城時照舊在合格的格裡隨機挑；
    /// 有了之後，只從「離既有地下城最近距離」達到最大值 <see cref="TempSpreadPercent"/>% 以上（至少 <see cref="TempMinDistance"/> 格）的格裡隨機挑，
    /// 地下城散在地圖各處，不擠在同一片荒野。
    /// </summary>
    internal static DemoHexCell PickSpread(IReadOnlyList<DemoHexCell> spots, IReadOnlyList<DemoCellRef> placed, ulong draw)
    {
        ArgumentNullException.ThrowIfNull(spots);
        ArgumentNullException.ThrowIfNull(placed);
        if (placed.Count == 0)
            return spots[(int)(draw % (ulong)spots.Count)];
        var distance = spots.Select(c => placed.Min(p => DemoHexWorld.Distance(new DemoCellRef(c.Column, c.Row), p))).ToArray();
        var bar = Math.Max(TempMinDistance, distance.Max() * TempSpreadPercent / 100);
        var far = spots.Where((_, i) => distance[i] >= bar).ToList();
        if (far.Count == 0)
            far = spots.ToList();
        return far[(int)(draw % (ulong)far.Count)];
    }

    /// <summary>從事件（由舊到新）重算地下城清單；可接續 <paramref name="known"/> 往後加。</summary>
    public static void Collect(IEnumerable<DemoSettlementEvent> events, DemoHexWorld map, RngRegistry rng, List<DemoDungeon> known)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(known);
        foreach (var e in events.Where(e => e.Kind == DemoSettlementEventKind.DungeonAppeared))
        {
            var year = e.Week / DemoWildlife.TempYearWeeks;
            var slot = known.Count(d => d.AppearedWeek == e.Week); // 第 12-2k 章：同一週出現的第幾個
            var cell = DemoLandClearing.CellOf(map, e.Count);
            var sea = map.Contains(cell.Column, cell.Row) && map.GetCell(cell.Column, cell.Row).Terrain == DemoTerrain.Water; // 第 08-42 章
            var (name, layers) = Describe(rng, year, e.Count, known.Select(d => d.Name).ToHashSet(), slot, sea);
            known.Add(new DemoDungeon(cell, name, layers, e.Week, e.EventId, e.SettlementId));
        }
    }

    /// <summary>某聚落 <see cref="TempNearbyRange"/> 格內的地下城（近的在前）。</summary>
    public static IEnumerable<DemoDungeon> Near(IEnumerable<DemoDungeon> dungeons, DemoHexWorld map, DemoSettlement s)
    {
        var origin = map.GetCell(s.Column, s.Row);
        return dungeons.Select(d => (d, Dist: DemoWorldSites.HexDistance(origin, map.GetCell(d.Cell.Column, d.Cell.Row))))
            .Where(x => x.Dist <= TempNearbyRange).OrderBy(x => x.Dist).ThenBy(x => x.d.EventId).Select(x => x.d);
    }
}
