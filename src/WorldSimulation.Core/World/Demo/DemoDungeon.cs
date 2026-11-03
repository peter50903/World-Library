using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 12-1 章 C4：世界上的一個地下城（由「地下城出現」事件推得，不另存）。</summary>
public sealed record DemoDungeon(DemoCellRef Cell, string Name, int Layers, int AppearedWeek, long EventId, int NearSettlementId);

/// <summary>
/// 第 12-1 章 C4【TEMP 規則】地下城出現（開拓時代只做出現與記錄；怪物、核心、攻略、溢出留給第 12 章）：
/// 開拓時代起每年第 <see cref="TempYearWeek"/> 週，世界上的地下城少於目標數時以 <see cref="TempChancePercent"/>% 出現 1 個，
/// 位置在丘陵或森林、離每個有人聚落與既有地下城至少 <see cref="TempMinDistance"/> 格。
/// 名字與層數只依年份與格編號由事件流的實體局部亂數決定（不推進計數器），所以清單可以從事件重算，存檔不必改版。
/// </summary>
public static class DemoDungeons
{
    /// <summary>【TEMP】每年在第幾週檢查（與野生動物、天災錯開）。</summary>
    public const int TempYearWeek = 39;

    /// <summary>【TEMP】少於目標數時，當年出現一個的機率（%）。</summary>
    public const int TempChancePercent = 25;

    /// <summary>【TEMP】離有人聚落與既有地下城的最少格數。</summary>
    public const int TempMinDistance = 4;

    /// <summary>【TEMP】聚落詳情列出幾格內的地下城。</summary>
    public const int TempNearbyRange = 6;

    /// <summary>【TEMP】層數範圍（開拓時代只有淺層）。</summary>
    public const int TempMinLayers = 1, TempMaxLayers = 3;

    /// <summary>【TEMP】名字後綴（接在地名前綴後面）。</summary>
    public static readonly string[] TempSuffixes = { "洞窟", "古墓", "地窟", "深淵", "迷宮" };

    private const string Namespace = "TEMP-DUNGEON-v1";

    /// <summary>【TEMP】目標數＝1＋欄數÷24，最多 3（小 1、中 2、大與巨大 3）。</summary>
    public static int TargetCount(DemoHexWorld map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Math.Min(3, 1 + map.Columns / 24);
    }

    private static ulong Draw(RngRegistry rng, int year, ulong k)
        => EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Event, Namespace).GetUInt64((ulong)year * 64 + k);

    /// <summary>名字與層數：只依出現年份、格編號與已用過的名字決定（重算結果相同）。</summary>
    public static (string Name, int Layers) Describe(RngRegistry rng, int year, long cellId, ISet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        var prefixes = DemoNameTempSettings.Current.PlacePrefix;
        var max = DemoNameTempSettings.Current.MaxLength;
        var layers = TempMinLayers + (int)(Draw(rng, year, 10) % (ulong)(TempMaxLayers - TempMinLayers + 1));
        for (ulong attempt = 0; attempt < 32; attempt++)
        {
            var name = prefixes[(int)(Draw(rng, year, 20 + attempt * 2) % (ulong)prefixes.Count)]
                + TempSuffixes[(int)(Draw(rng, year, 21 + attempt * 2) % (ulong)TempSuffixes.Length)];
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

    /// <summary>
    /// 本週出現的地下城事件（不是檢查週、希望時代、已達目標數、機率不過、沒有有人聚落或合格格＝空；最多 1 件）。
    /// 事件掛在最近的有人聚落（同距離取編號小的），人數欄＝格編號，根因。
    /// </summary>
    public static IReadOnlyList<DemoSettlementEvent> Roll(int week, TimeSpan worldTime, DemoEra era, DemoHexWorld map, DemoWorldSites sites,
        IEnumerable<int> livingIds, IReadOnlyList<DemoDungeon> existing, RngRegistry rng, long nextEventId)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(existing);
        if (era == DemoEra.Hope || week % DemoWildlife.TempYearWeeks != TempYearWeek || existing.Count >= TargetCount(map))
            return Array.Empty<DemoSettlementEvent>();
        var year = week / DemoWildlife.TempYearWeeks;
        if ((int)(Draw(rng, year, 0) % 100UL) >= TempChancePercent)
            return Array.Empty<DemoSettlementEvent>();
        var living = livingIds.OrderBy(i => i).Select(i => sites.Settlements.First(s => s.Id == i)).ToList();
        if (living.Count == 0)
            return Array.Empty<DemoSettlementEvent>();
        var taken = existing.Select(d => d.Cell).ToList();
        var spots = map.Cells.Where(c => Eligible(map, sites, living, taken, c)).OrderBy(c => c.Row).ThenBy(c => c.Column).ToList();
        if (spots.Count == 0)
            return Array.Empty<DemoSettlementEvent>();
        var spot = spots[(int)(Draw(rng, year, 1) % (ulong)spots.Count)];
        var near = living.OrderBy(s => DemoWorldSites.HexDistance(map.GetCell(s.Column, s.Row), spot)).ThenBy(s => s.Id).First();
        var cellId = DemoLandClearing.CellId(map, new DemoCellRef(spot.Column, spot.Row));
        var (name, layers) = Describe(rng, year, cellId, existing.Select(d => d.Name).ToHashSet());
        return new[]
        {
            new DemoSettlementEvent(week, worldTime, near.Id, near.Name, DemoSettlementEventKind.DungeonAppeared, cellId,
                $"附近的{(spot.Terrain == DemoTerrain.Hills ? "丘陵" : "森林")}裂開了一道入口：{name}（{layers} 層地下城）出現在 ({spot.Column},{spot.Row})【TEMP】",
                EventId: nextEventId)
        };
    }

    /// <summary>從事件（由舊到新）重算地下城清單；可接續 <paramref name="known"/> 往後加。</summary>
    public static void Collect(IEnumerable<DemoSettlementEvent> events, DemoHexWorld map, RngRegistry rng, List<DemoDungeon> known)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(known);
        foreach (var e in events.Where(e => e.Kind == DemoSettlementEventKind.DungeonAppeared))
        {
            var year = e.Week / DemoWildlife.TempYearWeeks;
            var (name, layers) = Describe(rng, year, e.Count, known.Select(d => d.Name).ToHashSet());
            known.Add(new DemoDungeon(DemoLandClearing.CellOf(map, e.Count), name, layers, e.Week, e.EventId, e.SettlementId));
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
