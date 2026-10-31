using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 12-1 章 C3：本週打在某聚落的天災（死亡人數為上限，結算時至少留一人）。</summary>
public sealed record DemoDisasterStrike(int SettlementId, DemoSettlementEventKind Kind, long Deaths, string Cause);

/// <summary>
/// 第 12-1 章 C3a【TEMP 規則】天災（參考 WorldBox 的地震、龍捲風、隕石，另加河岸洪水）：
/// 每年第 <see cref="TempYearWeek"/> 週對每個有人聚落依地理前提擲一次，依「洪水→地震→龍捲風→隕石」順序取第一個命中（一聚落一年最多一次）。
/// 用事件流的實體局部亂數（實體＝聚落、索引＝年份），不推進計數器，重跑相同。希望時代沒有天災。
/// 洪水與龍捲風另毀農田（變回開墾一半，記「農田荒廢」事件）。玩家只看到編年史一行與地圖上的災害標記。
/// </summary>
public static class DemoDisasters
{
    /// <summary>【TEMP】每年在第幾週結算天災（與野生動物的年結算錯開）。</summary>
    public const int TempYearWeek = 26;

    /// <summary>【TEMP】年機率（千分比）：洪水 30、地震 15、龍捲風 15、隕石 3。</summary>
    public const int TempFloodPermille = 30;
    public const int TempEarthquakePermille = 15;
    public const int TempTornadoPermille = 15;
    public const int TempMeteoritePermille = 3;

    /// <summary>【TEMP】洪水毀掉範圍內每塊農田的機率（%）；被毀的農田變回開墾中的這個開發度。</summary>
    public const int TempFloodFarmlandPercent = 50;
    public const int TempRuinedFarmlandPercent = 50;

    private const string Namespace = "TEMP-DISASTER-v1";

    public static bool IsDisaster(DemoSettlementEventKind kind)
        => kind is DemoSettlementEventKind.Earthquake or DemoSettlementEventKind.Tornado
            or DemoSettlementEventKind.Meteorite or DemoSettlementEventKind.Flood;

    public static string Name(DemoSettlementEventKind kind) => kind switch
    {
        DemoSettlementEventKind.Earthquake => "地震",
        DemoSettlementEventKind.Tornado => "龍捲風",
        DemoSettlementEventKind.Meteorite => "隕石",
        DemoSettlementEventKind.Flood => "洪水",
        _ => kind.ToString()
    };

    private static ulong Draw(RngRegistry rng, int settlementId, int year, int k)
        => EntityLocalRandom.Create(rng, EntityId.From((ulong)settlementId), RngStreams.Event, Namespace).GetUInt64((ulong)year * 16 + (ulong)k);

    private static IEnumerable<DemoHexCell> Around(DemoHexWorld map, DemoSettlement s, int range)
    {
        var origin = map.GetCell(s.Column, s.Row);
        return map.Cells.Where(c => DemoWorldSites.HexDistance(origin, c) <= range);
    }

    /// <summary>本週的天災（不是天災週＝空）。</summary>
    public static IReadOnlyDictionary<int, DemoDisasterStrike> Roll(int week, DemoHexWorld map, DemoWorldSites sites,
        IEnumerable<int> livingIds, RngRegistry rng)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        var result = new Dictionary<int, DemoDisasterStrike>();
        if (week % DemoWildlife.TempYearWeeks != TempYearWeek)
            return result;
        var year = week / DemoWildlife.TempYearWeeks;
        foreach (var id in livingIds.OrderBy(i => i))
        {
            var s = sites.Settlements.First(x => x.Id == id);
            var near = Around(map, s, 1).ToList();
            var candidates = new List<(DemoSettlementEventKind Kind, int Permille, long Deaths, string Cause)>();
            long Deaths(int k, int min, int max) => min + (long)(Draw(rng, id, year, 8 + k) % (ulong)(max - min + 1));
            if (near.Any(c => c.MainWaterway && !c.Sea))
                candidates.Add((DemoSettlementEventKind.Flood, TempFloodPermille, Deaths(0, 0, 2), "河水暴漲，洪水淹沒了聚落一帶"));
            if (Around(map, s, 2).Any(c => c.Terrain == DemoTerrain.Hills))
                candidates.Add((DemoSettlementEventKind.Earthquake, TempEarthquakePermille, Deaths(1, 1, 4), "大地震動，房舍倒塌"));
            if (near.Count(c => c.Terrain == DemoTerrain.Grassland) * 2 >= near.Count)
                candidates.Add((DemoSettlementEventKind.Tornado, TempTornadoPermille, Deaths(2, 0, 2), "龍捲風橫掃草原，捲走了屋頂與田地"));
            candidates.Add((DemoSettlementEventKind.Meteorite, TempMeteoritePermille, Deaths(3, 1, 3), "一顆隕石從天而降，砸在聚落附近"));
            foreach (var (kind, permille, deaths, cause) in candidates)
            {
                if ((int)(Draw(rng, id, year, (int)kind) % 1000UL) >= permille) continue;
                result[id] = new DemoDisasterStrike(id, kind, deaths, cause);
                break;
            }
        }
        return result;
    }

    /// <summary>
    /// 洪水與龍捲風毀農田：聚落本格與相鄰格的農田，洪水每塊 50%、龍捲風最多 1 塊，變回開墾中（開發度一半）並記「農田荒廢」事件。
    /// 事件編號從 <paramref name="nextEventId"/> 起。
    /// </summary>
    public static (DemoLandDevelopmentMap Land, IReadOnlyList<DemoSettlementEvent> Events) RuinFarmland(
        IReadOnlyDictionary<int, DemoDisasterStrike> strikes, DemoLandDevelopmentMap land, DemoHexWorld map, DemoWorldSites sites,
        int week, TimeSpan worldTime, RngRegistry rng, long nextEventId)
    {
        ArgumentNullException.ThrowIfNull(land);
        var events = new List<DemoSettlementEvent>();
        var year = week / DemoWildlife.TempYearWeeks;
        foreach (var strike in strikes.Values.Where(s => s.Kind is DemoSettlementEventKind.Flood or DemoSettlementEventKind.Tornado).OrderBy(s => s.SettlementId))
        {
            var s = sites.Settlements.First(x => x.Id == strike.SettlementId);
            var fields = Around(map, s, 1).Select(c => land.ParcelAt(new DemoCellRef(c.Column, c.Row)))
                .Where(p => p is { State: DemoLandDevelopment.Farmland }).Select(p => p!).OrderBy(p => p.Row).ThenBy(p => p.Column).ToList();
            var k = 0;
            foreach (var parcel in fields)
            {
                var hit = strike.Kind == DemoSettlementEventKind.Flood
                    ? (int)(Draw(rng, s.Id, year, 100 + k++) % 100UL) < TempFloodFarmlandPercent
                    : k++ == 0;
                if (!hit) continue;
                land = land.With(map, sites, new DemoLandParcel(parcel.Column, parcel.Row, DemoLandDevelopment.Clearing, TempRuinedFarmlandPercent));
                events.Add(new DemoSettlementEvent(week, worldTime, s.Id, s.Name, DemoSettlementEventKind.FarmlandAbandoned,
                    DemoLandClearing.CellId(map, parcel.Cell),
                    $"{Name(strike.Kind)}毀了 ({parcel.Column},{parcel.Row}) 的農田，要重新開墾【TEMP】", EventId: nextEventId++));
            }
        }
        return (land, events);
    }
}
