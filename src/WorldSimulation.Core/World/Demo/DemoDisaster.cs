using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 12-1 章 C3：本週打在某聚落的天災（死亡人數為上限，結算時至少留一人）。</summary>
public sealed record DemoDisasterStrike(int SettlementId, DemoSettlementEventKind Kind, long Deaths, string Cause);

/// <summary>
/// 第 12-1 章 C3a【TEMP 規則】天災（使用者 2026-10-31 採用 WorldBox v09 研究的結構，不照搬數值）：
/// <b>世界層級事件池</b>——每年第 <see cref="TempYearWeek"/> 週，依目前時代的候選池與權重抽 1 件；
/// 抽中後才檢查世界人口門檻與發生機率，再從符合地理前提的有人聚落中選 1 個承受；任何一關不過當年就沒有天災，<b>不重抽</b>。
/// 頻率因此與聚落數無關。希望時代候選池為空。用事件流的實體局部亂數（索引＝年份），不推進計數器，重跑相同。
/// 洪水與龍捲風另毀農田（變回開墾一半，記「農田荒廢」事件）。玩家只看到編年史一行與地圖上的災害標記。
/// </summary>
public static class DemoDisasters
{
    /// <summary>【TEMP】每年在第幾週結算天災（與野生動物的年結算錯開）。</summary>
    public const int TempYearWeek = 26;

    /// <summary>【TEMP】抽中且過門檻後真的發生的機率（%）。</summary>
    public const int TempChancePercent = 30;

    /// <summary>候選：權重、世界人口門檻（等於門檻可通過）。</summary>
    public sealed record Candidate(DemoSettlementEventKind Kind, int Weight, long MinWorldPopulation);

    /// <summary>
    /// 【TEMP】各時代的候選池（WorldBox：時代決定候選範圍）。希望時代空池；開拓時代四種；征服時代之後再擴充。
    /// 地震、隕石要世界人口達 300 人（WorldBox 的規模門檻：避免世界太早被毀；原作讀全部單位，數字不照搬）。
    /// </summary>
    public static IReadOnlyList<Candidate> PoolOf(DemoEra era) => era switch
    {
        DemoEra.Hope => Array.Empty<Candidate>(),
        _ => new[]
        {
            new Candidate(DemoSettlementEventKind.Flood, 3, 0),
            new Candidate(DemoSettlementEventKind.Earthquake, 3, 300),
            new Candidate(DemoSettlementEventKind.Tornado, 3, 0),
            new Candidate(DemoSettlementEventKind.Meteorite, 1, 300)
        }
    };

    /// <summary>【TEMP】洪水毀掉範圍內每塊農田的機率（%）；被毀的農田變回開墾中的這個開發度。</summary>
    public const int TempFloodFarmlandPercent = 50;
    public const int TempRuinedFarmlandPercent = 50;

    private const string Namespace = "TEMP-DISASTER-v2";

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

    private static ulong Draw(RngRegistry rng, int year, int k)
        => EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Event, Namespace).GetUInt64((ulong)year * 16 + (ulong)k);

    private static IEnumerable<DemoHexCell> Around(DemoHexWorld map, DemoSettlement s, int range)
    {
        var origin = map.GetCell(s.Column, s.Row);
        return map.Cells.Where(c => DemoWorldSites.HexDistance(origin, c) <= range);
    }

    /// <summary>某聚落符合某天災的地理前提。</summary>
    public static bool Eligible(DemoSettlementEventKind kind, DemoHexWorld map, DemoSettlement s)
    {
        var near = Around(map, s, 1).ToList();
        return kind switch
        {
            DemoSettlementEventKind.Flood => near.Any(c => c.MainWaterway && !c.Sea),
            DemoSettlementEventKind.Earthquake => Around(map, s, 2).Any(c => c.Terrain == DemoTerrain.Hills),
            DemoSettlementEventKind.Tornado => near.Count(c => c.Terrain == DemoTerrain.Grassland) * 2 >= near.Count,
            _ => true // 隕石：任何地方
        };
    }

    private static (int Min, int Max, string Cause) Effect(DemoSettlementEventKind kind) => kind switch
    {
        DemoSettlementEventKind.Flood => (0, 2, "河水暴漲，洪水淹沒了聚落一帶"),
        DemoSettlementEventKind.Earthquake => (1, 4, "大地震動，房舍倒塌"),
        DemoSettlementEventKind.Tornado => (0, 2, "龍捲風橫掃草原，捲走了屋頂與田地"),
        _ => (1, 3, "一顆隕石從天而降，砸在聚落附近")
    };

    /// <summary>本週的天災（不是天災週、空池、門檻或機率不過、沒有合格聚落＝空；最多 1 件）。</summary>
    public static IReadOnlyDictionary<int, DemoDisasterStrike> Roll(int week, DemoEra era, long worldPopulation, DemoHexWorld map,
        DemoWorldSites sites, IEnumerable<int> livingIds, RngRegistry rng)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        var result = new Dictionary<int, DemoDisasterStrike>();
        if (week % DemoWildlife.TempYearWeeks != TempYearWeek)
            return result;
        var year = week / DemoWildlife.TempYearWeeks;
        var pool = PoolOf(era);
        var total = pool.Sum(c => c.Weight);
        if (total == 0)
            return result;
        // ① 依權重抽一件（還沒看門檻）。
        var pick = (int)(Draw(rng, year, 0) % (ulong)total);
        var chosen = pool[0];
        foreach (var c in pool)
        {
            if (pick < c.Weight) { chosen = c; break; }
            pick -= c.Weight;
        }
        // ② 門檻與機率；③ 合格聚落中選一個。任何一關不過：當年沒有天災，不重抽。
        if (worldPopulation < chosen.MinWorldPopulation || (int)(Draw(rng, year, 1) % 100UL) >= TempChancePercent)
            return result;
        var eligible = livingIds.OrderBy(i => i).Select(i => sites.Settlements.First(x => x.Id == i))
            .Where(s => Eligible(chosen.Kind, map, s)).ToList();
        if (eligible.Count == 0)
            return result;
        var target = eligible[(int)(Draw(rng, year, 2) % (ulong)eligible.Count)];
        var (min, max, cause) = Effect(chosen.Kind);
        var deaths = min + (long)(Draw(rng, year, 3) % (ulong)(max - min + 1));
        result[target.Id] = new DemoDisasterStrike(target.Id, chosen.Kind, deaths, cause);
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
                    ? (int)(Draw(rng, year, 100 + k++) % 100UL) < TempFloodFarmlandPercent
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
