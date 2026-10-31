using System.Collections.Immutable;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 12-1 章 C3b：一段期間的環境狀態（熱浪、野火；結束後仍保留紀錄）。<see cref="FromWeek"/> 週結算時開始，
/// 第 FromWeek＋1～<see cref="ToWeek"/> 週的來源受影響；<see cref="ToWeek"/> 週結算時記結束事件。
/// <see cref="Cells"/>＝受影響的格；<see cref="EventId"/>＝開始事件（之後的結束事件、饑荒的原因邊指向它）。
/// </summary>
public sealed record DemoEnvironmentSpell(DemoSettlementEventKind Kind, int SettlementId, int FromWeek, int ToWeek, long EventId,
    ImmutableList<DemoCellRef> Cells);

/// <summary>
/// 第 12-1 章 C3b【TEMP 規則】熱浪與野火（WorldBox v09：乾旱、霜害是時代的持續作用，不走事件池——本專案做成一段期間的環境狀態）。
/// 每年第 <see cref="DemoDisasters.TempYearWeek"/> 週（開拓時代起）各擲一次：熱浪找周圍以草原為主、不鄰河的聚落；野火找相鄰有森林的聚落。
/// 熱浪：一年內周圍 2 格的草原野採與農田來源 −40%；野火：燒毀 2 格內 1～3 格森林，三年內這些森林來源 −90%、獵物歸零。
/// 編年史只記開始與結束；熱浪範圍內的缺糧以熱浪為原因（DST_CHAIN）。不扣人命（死亡由缺糧鏈產生）。
/// </summary>
public static class DemoEnvironment
{
    /// <summary>【TEMP】年機率（%）、持續週數、來源修正（%）。</summary>
    public const int TempHeatwavePercent = 12;
    public const int TempWildfirePercent = 12;
    public const int TempHeatwaveWeeks = 52;
    public const int TempWildfireWeeks = 156;
    public const long TempHeatwaveSourcePercent = -40;
    public const long TempWildfireSourcePercent = -90;
    public const int TempRange = 2;

    private const string Namespace = "TEMP-ENVIRONMENT-v1";

    public static bool IsSpell(DemoSettlementEventKind kind)
        => kind is DemoSettlementEventKind.Heatwave or DemoSettlementEventKind.Wildfire;

    private static ulong Draw(RngRegistry rng, int year, int k)
        => EntityLocalRandom.Create(rng, EntityId.From(2UL), RngStreams.Event, Namespace).GetUInt64((ulong)year * 16 + (ulong)k);

    private static IEnumerable<DemoHexCell> Around(DemoHexWorld map, DemoSettlement s, int range)
    {
        var origin = map.GetCell(s.Column, s.Row);
        return map.Cells.Where(c => DemoWorldSites.HexDistance(origin, c) <= range);
    }

    public static bool HeatwaveEligible(DemoHexWorld map, DemoSettlement s)
    {
        var near = Around(map, s, 1).ToList();
        return near.Count(c => c.Terrain == DemoTerrain.Grassland) * 2 >= near.Count && !near.Any(c => c.MainWaterway && !c.Sea);
    }

    public static bool WildfireEligible(DemoHexWorld map, DemoSettlement s) => Around(map, s, 1).Any(c => c.Terrain == DemoTerrain.Forest);

    /// <summary>某週某來源的環境修正（%；負數；沒有＝0）。期間＝開始週之後到結束週（含）。</summary>
    public static long SourcePercent(IReadOnlyList<DemoEnvironmentSpell> spells, DemoCatchmentFoodSource source, int week)
    {
        long p = 0;
        foreach (var spell in spells)
        {
            if (week <= spell.FromWeek || week > spell.ToWeek || !spell.Cells.Contains(new DemoCellRef(source.Column, source.Row)))
                continue;
            if (spell.Kind == DemoSettlementEventKind.Heatwave && source.Kind is DemoCatchmentSourceKind.GrasslandForage or DemoCatchmentSourceKind.Farmland)
                p = Math.Min(p, TempHeatwaveSourcePercent);
            if (spell.Kind == DemoSettlementEventKind.Wildfire && source.Kind == DemoCatchmentSourceKind.Forest)
                p = Math.Min(p, TempWildfireSourcePercent);
        }
        return p;
    }

    /// <summary>本週某聚落正處於熱浪範圍內時，熱浪開始事件的編號（缺糧的原因）；否則 0。</summary>
    public static long HeatwaveCause(IReadOnlyList<DemoEnvironmentSpell> spells, DemoSettlement s, int week)
        => spells.Where(x => x.Kind == DemoSettlementEventKind.Heatwave && week > x.FromWeek && week <= x.ToWeek
                && x.Cells.Contains(new DemoCellRef(s.Column, s.Row)))
            .OrderBy(x => x.EventId).Select(x => x.EventId).FirstOrDefault();

    /// <summary>
    /// 每週（生死、土地之後）：結束到期的環境狀態（記結束事件）、天災週擲新的熱浪與野火（記開始事件；野火讓燒毀格的獵物歸零）。
    /// 事件編號從 <paramref name="nextEventId"/> 起。
    /// </summary>
    public static (DemoWildlifeState State, IReadOnlyList<DemoSettlementEvent> Events) Advance(DemoWildlifeState state, int week, TimeSpan worldTime,
        DemoEra era, DemoHexWorld map, DemoWorldSites sites, IEnumerable<int> livingIds, RngRegistry rng, long nextEventId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);
        var events = new List<DemoSettlementEvent>();
        var spells = state.Spells.ToList();
        foreach (var spell in spells.Where(x => x.ToWeek == week).OrderBy(x => x.EventId))
        {
            var s = sites.Settlements.First(x => x.Id == spell.SettlementId);
            events.Add(new DemoSettlementEvent(week, worldTime, s.Id, s.Name,
                spell.Kind == DemoSettlementEventKind.Heatwave ? DemoSettlementEventKind.HeatwaveEnded : DemoSettlementEventKind.ForestRegrown,
                spell.Cells.Count, spell.Kind == DemoSettlementEventKind.Heatwave ? "熱浪退去，草原與田地恢復" : "燒毀的森林重新長出",
                EventId: nextEventId++, CauseEventId: spell.EventId));
        }
        // 結束的環境狀態仍保留（存檔近期週報要依它重算來源帳；每世紀約二十筆，不清除）。
        var prey = state.Prey;
        if (era != DemoEra.Hope && week % DemoWildlife.TempYearWeeks == DemoDisasters.TempYearWeek && !state.Prey.IsDefaultOrEmpty)
        {
            var year = week / DemoWildlife.TempYearWeeks;
            var living = livingIds.OrderBy(i => i).Select(i => sites.Settlements.First(x => x.Id == i)).ToList();
            // 熱浪
            var heat = living.Where(s => HeatwaveEligible(map, s)).ToList();
            if (heat.Count > 0 && (int)(Draw(rng, year, 0) % 100UL) < TempHeatwavePercent)
            {
                var s = heat[(int)(Draw(rng, year, 1) % (ulong)heat.Count)];
                var cells = Around(map, s, TempRange).Where(c => c.Terrain != DemoTerrain.Water)
                    .Select(c => new DemoCellRef(c.Column, c.Row)).OrderBy(c => c.Row).ThenBy(c => c.Column).ToImmutableList();
                var id = nextEventId++;
                events.Add(new DemoSettlementEvent(week, worldTime, s.Id, s.Name, DemoSettlementEventKind.Heatwave, cells.Count,
                    "熱浪來襲，草原枯黃、田地乾裂（一年內周圍採集與收成減少）", EventId: id));
                spells.Add(new DemoEnvironmentSpell(DemoSettlementEventKind.Heatwave, s.Id, week, week + TempHeatwaveWeeks, id, cells));
            }
            // 野火
            var fire = living.Where(s => WildfireEligible(map, s)).ToList();
            if (fire.Count > 0 && (int)(Draw(rng, year, 2) % 100UL) < TempWildfirePercent)
            {
                var s = fire[(int)(Draw(rng, year, 3) % (ulong)fire.Count)];
                var forests = Around(map, s, TempRange).Where(c => c.Terrain == DemoTerrain.Forest && sites.SettlementAt(c.Column, c.Row) is null)
                    .OrderBy(c => c.Row).ThenBy(c => c.Column).ToList();
                var take = Math.Min(forests.Count, 1 + (int)(Draw(rng, year, 4) % 3UL));
                var start = forests.Count == 0 ? 0 : (int)(Draw(rng, year, 5) % (ulong)forests.Count);
                var burnt = Enumerable.Range(0, take).Select(k => forests[(start + k) % forests.Count])
                    .Select(c => new DemoCellRef(c.Column, c.Row)).OrderBy(c => c.Row).ThenBy(c => c.Column).ToImmutableList();
                if (burnt.Count > 0)
                {
                    var id = nextEventId++;
                    events.Add(new DemoSettlementEvent(week, worldTime, s.Id, s.Name, DemoSettlementEventKind.Wildfire, burnt.Count,
                        $"野火燒毀了附近 {burnt.Count} 片森林（三年內無法採集、獵物逃散）", EventId: id));
                    spells.Add(new DemoEnvironmentSpell(DemoSettlementEventKind.Wildfire, s.Id, week, week + TempWildfireWeeks, id, burnt));
                    var p = prey.ToBuilder();
                    foreach (var c in burnt)
                        p[c.Row * map.Columns + c.Column] = 0;
                    prey = p.ToImmutable();
                }
            }
        }
        return (state with { Prey = prey, Spells = spells.OrderBy(x => x.EventId).ToImmutableList() }, events);
    }
}
