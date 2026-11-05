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

    /// <summary>【TEMP】第 08-13 章：野火開始後這麼多週畫成「燃燒中」，之後到結束畫成「焦土」（顯示用）。</summary>
    public const int TempBurningWeeks = 4;

    /// <summary>地圖上某格本週的環境外觀（顯示用；不影響規則）。</summary>
    public enum Look { None, Burning, Scorched, Dry }

    public static Look LookAt(IReadOnlyList<DemoEnvironmentSpell> spells, DemoHexCell cell, int week)
    {
        var at = new DemoCellRef(cell.Column, cell.Row);
        var look = Look.None;
        foreach (var x in spells.Where(x => week > x.FromWeek && week <= x.ToWeek && x.Cells.Contains(at)))
        {
            if (x.Kind == DemoSettlementEventKind.Wildfire)
                return week <= x.FromWeek + TempBurningWeeks ? Look.Burning : Look.Scorched;
            if (x.Kind == DemoSettlementEventKind.Heatwave && cell.Terrain == DemoTerrain.Grassland)
                look = Look.Dry;
        }
        return look;
    }

    private const string Namespace = "TEMP-ENVIRONMENT-v1";

    public static bool IsSpell(DemoSettlementEventKind kind)
        => kind is DemoSettlementEventKind.Heatwave or DemoSettlementEventKind.Wildfire
            or DemoSettlementEventKind.Logging or DemoSettlementEventKind.MiningBegan; // 第 08-16 章第 14 節：伐木、採礦

    /// <summary>各種環境狀態的固定持續週數（存檔驗證用）。</summary>
    public static int Duration(DemoSettlementEventKind kind) => kind switch
    {
        DemoSettlementEventKind.Heatwave => TempHeatwaveWeeks,
        DemoSettlementEventKind.Wildfire => TempWildfireWeeks,
        DemoSettlementEventKind.Logging => TempLoggingWeeks,
        DemoSettlementEventKind.MiningBegan => TempMiningWeeks,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "不是環境狀態。")
    };

    // ───────────── 第 08-16 章第 14 節：砍樹與挖礦（試玩回饋 2026-11-04 夜之 5） ─────────────

    /// <summary>
    /// 【TEMP】伐木：會基礎木工、住民至少 <see cref="TempLoggingMinResidents"/> 人的聚落，每年以「住民數 %」（最多 <see cref="TempLoggingMaxPercent"/>%）
    /// 砍掉 <see cref="TempLoggingRange"/> 格內最近的一片森林；那片森林 <see cref="TempLoggingWeeks"/> 週內採集 <see cref="TempLoggedSourcePercent"/>%，
    /// 畫面上先是樹樁、再長出幼樹，期滿長回森林。
    /// </summary>
    public const int TempLoggingMinResidents = 20;
    public const int TempLoggingMaxPercent = 90;
    public const int TempLoggingRange = 2;
    public const int TempLoggingWeeks = 520;
    public const long TempLoggedSourcePercent = -50;

    /// <summary>【TEMP】伐木狀態結束這麼多週後從狀態表移除（近期週報最多保留約 156 週；伐木每年可能上百筆，不能像天災一樣永遠留著）。</summary>
    public const int TempLoggingPruneWeeks = 260;

    /// <summary>
    /// 【TEMP】採礦：會探礦的聚落若 <see cref="DemoKnowledge.TempOreRange"/> 格內有還沒人開採的礦脈，就開採最近的一處（不抽亂數），
    /// <see cref="TempMiningWeeks"/> 週後礦脈挖空、留下礦坑——不再算「勢力範圍內有礦脈」。一個聚落同時只開採一處。
    /// </summary>
    public const int TempMiningWeeks = 1560;

    public const string LoggingTech = "TECH_BASIC_CARPENTRY";
    public const string MiningTech = "TECH_PROSPECTING";

    /// <summary>某週已挖空的礦脈格（開採狀態結束之後）。</summary>
    public static IReadOnlySet<DemoCellRef> ExhaustedOre(IReadOnlyList<DemoEnvironmentSpell> spells, int week)
        => spells.Where(x => x.Kind == DemoSettlementEventKind.MiningBegan && x.ToWeek < week).SelectMany(x => x.Cells).ToHashSet();

    /// <summary>礦脈格某週的開採狀況：-1＝沒有人開採；0～1＝開採中的進度；2＝已挖空。</summary>
    public static float MiningAt(IReadOnlyList<DemoEnvironmentSpell> spells, DemoCellRef cell, int week)
    {
        foreach (var x in spells)
            if (x.Kind == DemoSettlementEventKind.MiningBegan && x.Cells.Contains(cell))
                return week > x.ToWeek ? 2f : Math.Clamp((week - x.FromWeek) / (float)(x.ToWeek - x.FromWeek), 0f, 1f);
        return -1f;
    }

    /// <summary>森林格某週的伐木狀況：-1＝沒有砍；0～1＝砍下之後長回的進度（0＝剛砍、只剩樹樁）。</summary>
    public static float LoggedAt(IReadOnlyList<DemoEnvironmentSpell> spells, DemoCellRef cell, int week)
    {
        var best = -1f;
        foreach (var x in spells)
            if (x.Kind == DemoSettlementEventKind.Logging && week > x.FromWeek && week <= x.ToWeek && x.Cells.Contains(cell))
                best = best < 0f ? (week - x.FromWeek) / (float)(x.ToWeek - x.FromWeek) : Math.Min(best, (week - x.FromWeek) / (float)(x.ToWeek - x.FromWeek));
        return best;
    }

    /// <summary>聚落 <paramref name="range"/> 格內的格（含本格；只看範圍內，不掃全圖）。</summary>
    private static IEnumerable<DemoHexCell> Ring(DemoHexWorld map, DemoSettlement s, int range)
    {
        return map.Within(map.GetCell(s.Column, s.Row), range);
    }

    private const string LoggingNamespace = "TEMP-LOGGING-v1";

    private static long CellNumber(DemoHexWorld map, DemoCellRef c) => (long)c.Row * map.Columns + c.Column + 1;

    /// <summary>
    /// 年結算（天災週，熱浪野火之後）：結束到期的採礦（礦脈挖空）、會木工的聚落伐木、會探礦的聚落開始採礦。
    /// 直接改 <paramref name="spells"/>；事件編號從 <paramref name="nextEventId"/> 起，回傳新事件。
    /// </summary>
    internal static List<DemoSettlementEvent> AdvanceWorks(List<DemoEnvironmentSpell> spells, int week, TimeSpan worldTime, DemoHexWorld map,
        DemoWorldSites sites, IReadOnlyList<DemoSettlement> living, Func<int, long> residents, Func<int, bool> logs, Func<int, bool> mines,
        IReadOnlySet<DemoCellRef>? extraOre, RngRegistry rng, ref long nextEventId)
    {
        var events = new List<DemoSettlementEvent>();
        var year = week / DemoWildlife.TempYearWeeks;
        // 伐木
        var busy = spells.Where(x => x.Kind is DemoSettlementEventKind.Logging or DemoSettlementEventKind.Wildfire && week <= x.ToWeek)
            .SelectMany(x => x.Cells).ToHashSet();
        foreach (var s in living)
        {
            var people = residents(s.Id);
            if (people < TempLoggingMinResidents || !logs(s.Id))
                continue;
            var random = EntityLocalRandom.Create(rng, EntityId.From(2UL), RngStreams.Event, LoggingNamespace);
            var key = ((ulong)year << 32) | (uint)s.Id;
            if ((long)(random.GetUInt64(key * 2) % 100UL) >= Math.Min(TempLoggingMaxPercent, people))
                continue;
            var origin = map.GetCell(s.Column, s.Row);
            var forests = Ring(map, s, TempLoggingRange)
                .Where(c => c.Terrain == DemoTerrain.Forest && sites.SettlementAt(c.Column, c.Row) is null && !busy.Contains(new DemoCellRef(c.Column, c.Row)))
                .Select(c => (Cell: c, Distance: DemoWorldSites.HexDistance(origin, c))).ToList();
            if (forests.Count == 0)
                continue;
            var nearest = forests.Min(f => f.Distance);
            var pool = forests.Where(f => f.Distance == nearest).Select(f => f.Cell).OrderBy(c => c.Row).ThenBy(c => c.Column).ToList();
            var pick = pool[(int)(random.GetUInt64(key * 2 + 1) % (ulong)pool.Count)];
            var at = new DemoCellRef(pick.Column, pick.Row);
            busy.Add(at);
            var id = nextEventId++;
            events.Add(new DemoSettlementEvent(week, worldTime, s.Id, s.Name, DemoSettlementEventKind.Logging, CellNumber(map, at),
                "砍伐了附近一片森林（樹樁會慢慢長回幼樹，約十年後恢復成林）", EventId: id));
            spells.Add(new DemoEnvironmentSpell(DemoSettlementEventKind.Logging, s.Id, week, week + TempLoggingWeeks, id, ImmutableList.Create(at)));
        }
        // 採礦（不抽亂數：最近的一處，同距離取列、欄小者）
        var claimed = spells.Where(x => x.Kind == DemoSettlementEventKind.MiningBegan).SelectMany(x => x.Cells).ToHashSet();
        var digging = spells.Where(x => x.Kind == DemoSettlementEventKind.MiningBegan && week <= x.ToWeek).Select(x => x.SettlementId).ToHashSet();
        foreach (var s in living)
        {
            if (digging.Contains(s.Id) || !mines(s.Id))
                continue;
            var origin = map.GetCell(s.Column, s.Row);
            var vein = Ring(map, s, DemoKnowledge.TempOreRange)
                .Where(c => (c.Ore || extraOre?.Contains(new DemoCellRef(c.Column, c.Row)) == true) && !claimed.Contains(new DemoCellRef(c.Column, c.Row)))
                .OrderBy(c => DemoWorldSites.HexDistance(origin, c)).ThenBy(c => c.Row).ThenBy(c => c.Column).FirstOrDefault();
            if (vein is null)
                continue;
            var at = new DemoCellRef(vein.Column, vein.Row);
            claimed.Add(at);
            digging.Add(s.Id);
            var id = nextEventId++;
            events.Add(new DemoSettlementEvent(week, worldTime, s.Id, s.Name, DemoSettlementEventKind.MiningBegan, CellNumber(map, at),
                $"開始在 ({at.Column},{at.Row}) 的礦脈採礦（約三十年後挖空）", EventId: id));
            spells.Add(new DemoEnvironmentSpell(DemoSettlementEventKind.MiningBegan, s.Id, week, week + TempMiningWeeks, id, ImmutableList.Create(at)));
        }
        return events;
    }

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
            if (spell.Kind == DemoSettlementEventKind.Wildfire && source.Kind is DemoCatchmentSourceKind.Forest or DemoCatchmentSourceKind.GrasslandForage)
                p = Math.Min(p, TempWildfireSourcePercent); // 第 08-13 章：燒過的草原也一樣
            if (spell.Kind == DemoSettlementEventKind.Logging && source.Kind == DemoCatchmentSourceKind.Forest)
                p = Math.Min(p, TempLoggedSourcePercent); // 第 08-16 章第 14 節：砍過的森林
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
        DemoEra era, DemoHexWorld map, DemoWorldSites sites, IEnumerable<int> livingIds, RngRegistry rng, long nextEventId,
        Func<int, long>? residents = null, Func<int, bool>? logs = null, Func<int, bool>? mines = null, IReadOnlySet<DemoCellRef>? extraOre = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);
        var events = new List<DemoSettlementEvent>();
        var spells = state.Spells.ToList();
        foreach (var spell in spells.Where(x => x.ToWeek == week && x.Kind != DemoSettlementEventKind.Logging).OrderBy(x => x.EventId))
        {
            var s = sites.Settlements.First(x => x.Id == spell.SettlementId);
            if (spell.Kind == DemoSettlementEventKind.MiningBegan)
            {
                // 第 08-16 章第 14 節：礦脈挖空（伐木長回不記事件——每年上百筆，只在地圖上看得到）。
                var at = spell.Cells[0];
                events.Add(new DemoSettlementEvent(week, worldTime, s.Id, s.Name, DemoSettlementEventKind.OreExhausted, CellNumber(map, at),
                    $"({at.Column},{at.Row}) 的礦脈挖空了，只剩一座廢棄的礦坑", EventId: nextEventId++, CauseEventId: spell.EventId));
                continue;
            }
            events.Add(new DemoSettlementEvent(week, worldTime, s.Id, s.Name,
                spell.Kind == DemoSettlementEventKind.Heatwave ? DemoSettlementEventKind.HeatwaveEnded : DemoSettlementEventKind.ForestRegrown,
                spell.Cells.Count, spell.Kind == DemoSettlementEventKind.Heatwave ? "熱浪退去，草原與田地恢復" : "燒毀的森林與草原重新長出",
                EventId: nextEventId++, CauseEventId: spell.EventId));
        }
        // 結束的環境狀態仍保留（存檔近期週報要依它重算來源帳；每世紀約二十筆，不清除）。
        // 第 08-16 章第 14 節：伐木結束夠久的才移除；採礦一律保留（挖空的礦脈由它推得）。
        spells.RemoveAll(x => x.Kind == DemoSettlementEventKind.Logging && x.ToWeek < week - TempLoggingPruneWeeks);
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
                // 第 08-13 章：野火也燒草原（WorldBox 的火會沿草地蔓延）。
                var forests = Around(map, s, TempRange).Where(c => c.Terrain is DemoTerrain.Forest or DemoTerrain.Grassland && sites.SettlementAt(c.Column, c.Row) is null)
                    .OrderBy(c => c.Row).ThenBy(c => c.Column).ToList();
                var take = Math.Min(forests.Count, 1 + (int)(Draw(rng, year, 4) % 3UL));
                var start = forests.Count == 0 ? 0 : (int)(Draw(rng, year, 5) % (ulong)forests.Count);
                var burnt = Enumerable.Range(0, take).Select(k => forests[(start + k) % forests.Count])
                    .Select(c => new DemoCellRef(c.Column, c.Row)).OrderBy(c => c.Row).ThenBy(c => c.Column).ToImmutableList();
                if (burnt.Count > 0)
                {
                    var id = nextEventId++;
                    events.Add(new DemoSettlementEvent(week, worldTime, s.Id, s.Name, DemoSettlementEventKind.Wildfire, burnt.Count,
                        $"野火燒毀了附近 {burnt.Count} 片森林與草原（三年內無法採集、獵物逃散）", EventId: id));
                    spells.Add(new DemoEnvironmentSpell(DemoSettlementEventKind.Wildfire, s.Id, week, week + TempWildfireWeeks, id, burnt));
                    var p = prey.ToBuilder();
                    foreach (var c in burnt)
                        p[c.Row * map.Columns + c.Column] = 0;
                    prey = p.ToImmutable();
                }
            }
            // 第 08-16 章第 14 節：伐木與採礦（事件接在熱浪、野火之後）。
            if (residents is not null && logs is not null && mines is not null)
                events.AddRange(AdvanceWorks(spells, week, worldTime, map, sites, living, residents, logs, mines, extraOre, rng, ref nextEventId));
        }
        return (state with { Prey = prey, Spells = spells.OrderBy(x => x.EventId).ToImmutableList() }, events);
    }
}
