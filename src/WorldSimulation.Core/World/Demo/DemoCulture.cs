using System.Collections.Immutable;
using System.Globalization;
using WorldSimulation.Core.Config;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-8 章 B1：一個文化（特性取自 P02 特性池；<see cref="ParentId"/>＝分化前的上層文化，開局文化為 0）。</summary>
public sealed record DemoCulture(int Id, string Name, ImmutableList<string> Traits, int ParentId = 0, int FoundedWeek = 0);

/// <summary>某文化在一個聚落（或途中隊伍）背景人口裡占的千分比。</summary>
public sealed record DemoCultureShare(int CultureId, int Permille);

/// <summary>某聚落的文化比例（千分比，合計 1000）。</summary>
public sealed record DemoSettlementCulture(int SettlementId, ImmutableList<DemoCultureShare> Shares);

/// <summary>途中隊伍帶著的文化比例（出發地的比例）。</summary>
public sealed record DemoBatchCulture(int BatchId, ImmutableList<DemoCultureShare> Shares);

/// <summary>代表人物（重要人物）各自的文化。</summary>
public sealed record DemoRepresentativeCulture(long RepresentativeId, int CultureId);

/// <summary>
/// 第 08-8 章 B2：聚落主要文化的變化紀錄——從 <see cref="Week"/> 週結算之後主要文化是 <see cref="CultureId"/>
/// （開局＝第 0 週）。週帳驗證用它找出某週結算時的主要文化。
/// </summary>
public sealed record DemoMainCulture(int SettlementId, int Week, int CultureId);

/// <summary>第 08-8 章 B4：某聚落最後一次與同文化主體往來的週次（分化的計時）。</summary>
public sealed record DemoCultureContact(int SettlementId, int LastContactWeek);

/// <summary>
/// 第 08-8 章 B1：文化的世界狀態——文化清單、各聚落與途中隊伍的文化比例、重要人物的文化。不可變。
/// 08-8 第 4 節：背景人口只存比例（千分比整數），重要人物逐人記錄。
/// </summary>
public sealed record DemoCultureState(
    ImmutableList<DemoCulture> Cultures,
    ImmutableList<DemoSettlementCulture> Settlements,
    ImmutableList<DemoBatchCulture> Batches,
    ImmutableList<DemoRepresentativeCulture> Representatives)
{
    public static DemoCultureState Empty { get; } = new(ImmutableList<DemoCulture>.Empty, ImmutableList<DemoSettlementCulture>.Empty,
        ImmutableList<DemoBatchCulture>.Empty, ImmutableList<DemoRepresentativeCulture>.Empty);

    /// <summary>第 08-8 章 B4：各聚落最後一次與同文化主體往來的週次。</summary>
    public ImmutableList<DemoCultureContact> Contacts { get; init; } = ImmutableList<DemoCultureContact>.Empty;

    /// <summary>第 08-8 章 B2：各聚落主要文化的變化紀錄（依聚落、週次排序）。</summary>
    public ImmutableList<DemoMainCulture> MainHistory { get; init; } = ImmutableList<DemoMainCulture>.Empty;

    /// <summary>第 <paramref name="week"/> 週結算時（該週以前）某聚落主要文化的特性；沒有紀錄＝空。</summary>
    public IReadOnlyCollection<string> TraitsAt(int settlementId, int week)
    {
        // 第 08-13 章第 5 節：依聚落分組的索引（以紀錄清單本身為鍵快取；清單不可變，換清單就換索引）。結果與線性搜尋相同。
        var bySettlement = HistoryIndex.GetValue(MainHistory, list => list.GroupBy(h => h.SettlementId).ToDictionary(g => g.Key, g => g.ToArray()));
        if (!bySettlement.TryGetValue(settlementId, out var entries))
            return Array.Empty<string>();
        for (var i = entries.Length - 1; i >= 0; i--)
            if (entries[i].Week < week)
                return Culture(entries[i].CultureId) is { } c ? c.Traits : Array.Empty<string>();
        return Array.Empty<string>();
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImmutableList<DemoMainCulture>, Dictionary<int, DemoMainCulture[]>> HistoryIndex = new();

    /// <summary>第 08-8 章 B3：信仰（與文化同存於「文化信仰」狀態）。</summary>
    public DemoFaithState Faith { get; init; } = DemoFaithState.Empty;

    public DemoCulture? Culture(int id) => Cultures.FirstOrDefault(c => c.Id == id);

    /// <summary>某聚落的文化比例（由多到少）；沒有紀錄＝空。</summary>
    public IReadOnlyList<DemoCultureShare> SharesOf(int settlementId)
        => Settlements.FirstOrDefault(s => s.SettlementId == settlementId)?.Shares ?? (IReadOnlyList<DemoCultureShare>)Array.Empty<DemoCultureShare>();

    /// <summary>聚落的主要文化（比例最高者；同比例取編號小）；沒有＝null。</summary>
    public int? MainCultureOf(int settlementId)
        => SharesOf(settlementId).OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId).Select(s => (int?)s.CultureId).FirstOrDefault();

    public int? CultureOfRepresentative(long representativeId)
        => Representatives.FirstOrDefault(r => r.RepresentativeId == representativeId)?.CultureId;

    public bool Equals(DemoCultureState? other) => other is not null && Fingerprint() == other.Fingerprint();

    public override int GetHashCode() => HashCode.Combine(Cultures.Count, Settlements.Count, Representatives.Count);

    public string Fingerprint()
        => string.Concat(Cultures.Select(c => $"cul{c.Id}:{c.Name}:{string.Join('+', c.Traits)}<{c.ParentId}@{c.FoundedWeek};"))
           + string.Concat(Settlements.Select(s => $"sc{s.SettlementId}=" + string.Join(',', s.Shares.Select(x => $"{x.CultureId}:{x.Permille}")) + ";"))
           + string.Concat(Batches.Select(s => $"bc{s.BatchId}=" + string.Join(',', s.Shares.Select(x => $"{x.CultureId}:{x.Permille}")) + ";"))
           + string.Concat(Representatives.Select(r => $"rc{r.RepresentativeId}={r.CultureId};"))
           + string.Concat(Contacts.Select(c => $"ct{c.SettlementId}@{c.LastContactWeek};"))
           + string.Concat(MainHistory.Select(h => $"mh{h.SettlementId}@{h.Week}={h.CultureId};"))
           + Faith.Fingerprint();
}

/// <summary>P02 特性池的一列（加權條件取自 04 文化特性定義）。</summary>
public sealed record DemoCultureTraitEntry(string PoolId, string PoolName, string TraitId, string Name, int Weight, int DrawMin, int DrawMax,
    string ExclusiveGroup, string Condition);

/// <summary>
/// 第 08-8 章 B1【TEMP 規則】文化：開局聚落依距離連成群，每群一個文化並依地形抽特性；之後隨遷徙、婚嫁、開拓以比例流動；
/// 代表人物就任時依聚落比例抽自己的文化。抽選用「文化信仰」（RELIGION）亂數流的實體局部亂數，不推進全域計數器。
/// </summary>
public static class DemoCultures
{
    public const string Namespace = "TEMP-CULTURE-v1";

    /// <summary>【TEMP】開局聚落相距不超過這麼多格就同一個文化（08-8 原訂婚嫁範圍 12 格；地圖小，12 格會讓整張圖只有一個文化）。</summary>
    public const int TempClusterRange = 6;

    /// <summary>【TEMP】特性的加權條件成立時，權重乘以這個倍數；條件是地形或出身且不成立時權重為 0（不抽）。</summary>
    public const int TempConditionWeightFactor = 4;

    private static readonly Lazy<IReadOnlyList<DemoCultureTraitEntry>> Pool = new(LoadPool);

    public static IReadOnlyList<DemoCultureTraitEntry> TraitPool => Pool.Value;

    /// <summary>特性的中文名稱。</summary>
    public static string TraitName(string traitId) => Pool.Value.FirstOrDefault(e => e.TraitId == traitId)?.Name ?? traitId;

    private static IReadOnlyList<DemoCultureTraitEntry> LoadPool()
    {
        var conditions = Rows("04_文化特性定義.csv").ToDictionary(r => r["CultureTraitID"], r => r.GetValueOrDefault("加權條件", ""));
        return Rows("P02_文化特性池.csv")
            .Where(r => string.Equals(r["Enabled"], "TRUE", StringComparison.OrdinalIgnoreCase))
            .Select(r => new DemoCultureTraitEntry(r["PoolID"], r["PoolName"], r["EntryID"], r["EntryName"],
                int.Parse(r["Weight"], CultureInfo.InvariantCulture), int.Parse(r["PoolDrawMin"], CultureInfo.InvariantCulture),
                int.Parse(r["PoolDrawMax"], CultureInfo.InvariantCulture), r["ExclusiveGroup"], conditions.GetValueOrDefault(r["EntryID"], "")))
            .ToList();
    }

    private static IEnumerable<Dictionary<string, string>> Rows(string sheet)
    {
        var rows = ParameterSheet.ParseCsv(OfficialConfig.ReadSheetText(sheet));
        var header = rows[0].Select(h => h.Trim().TrimStart('﻿')).ToList();
        return rows.Skip(1).Where(r => r.Count > 0 && !string.IsNullOrWhiteSpace(r[0]))
            .Select(r => header.Select((h, i) => (h, v: i < r.Count ? r[i].Trim() : "")).ToDictionary(x => x.h, x => x.v));
    }

    /// <summary>創立地的條件（地形與出身），決定生計特性的權重。</summary>
    public sealed record Origin(bool NearForest, bool NearRiver, bool NearSea, bool NearGrassland, bool Founded);

    private static bool? Holds(string condition, Origin o) => condition switch
    {
        "NEAR_FOREST" => o.NearForest,
        "NEAR_RIVER" => o.NearRiver,
        "NEAR_SEA" => o.NearSea,
        "NEAR_GRASSLAND" => o.NearGrassland,
        "FOUNDED" => o.Founded,
        "OPENING" => !o.Founded,
        _ => null // 其他（家庭、戰爭…）不是地形條件，照基礎權重
    };

    /// <summary>
    /// 依 P02 各池抽特性（池內抽 DrawMin～DrawMax 個、依權重不放回、同互斥組只取一個）。
    /// <paramref name="draw"/>(i) 回傳第 i 次抽樣的 64 位元值。
    /// </summary>
    public static ImmutableList<string> DrawTraits(Origin origin, Func<ulong, ulong> draw)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(draw);
        var traits = ImmutableList.CreateBuilder<string>();
        var groups = new HashSet<string>();
        ulong index = 0;
        foreach (var pool in Pool.Value.GroupBy(e => e.PoolId))
        {
            var first = pool.First();
            var count = first.DrawMin + (int)(draw(index++) % (ulong)(first.DrawMax - first.DrawMin + 1));
            for (var n = 0; n < count; n++)
            {
                var candidates = pool.Where(e => !traits.Contains(e.TraitId) && (e.ExclusiveGroup.Length == 0 || !groups.Contains(e.ExclusiveGroup)))
                    .Select(e => (Entry: e, Weight: Holds(e.Condition, origin) switch
                    {
                        true => e.Weight * TempConditionWeightFactor,
                        false => 0,
                        null => e.Weight
                    }))
                    .Where(x => x.Weight > 0).ToList();
                if (candidates.Count == 0)
                    break;
                var pick = (long)(draw(index++) % (ulong)candidates.Sum(x => x.Weight));
                foreach (var (entry, weight) in candidates)
                {
                    if (pick < weight)
                    {
                        traits.Add(entry.TraitId);
                        if (entry.ExclusiveGroup.Length > 0)
                            groups.Add(entry.ExclusiveGroup);
                        break;
                    }
                    pick -= weight;
                }
            }
        }
        return traits.ToImmutable();
    }

    private static ulong Draw(RngRegistry registry, long entityId, ulong index)
        => EntityLocalRandom.Create(registry, EntityId.From((ulong)entityId), RngStreams.Religion, Namespace).GetUInt64(index);

    /// <summary>某格本格與六鄰格的地形條件。</summary>
    public static Origin OriginOf(DemoHexWorld map, IEnumerable<DemoSettlement> settlements, bool founded)
    {
        ArgumentNullException.ThrowIfNull(map);
        var cells = settlements.SelectMany(s =>
        {
            var o = map.GetCell(s.Column, s.Row);
            return DemoRouteNetwork.Directions.Select(d => map.AtAxial(o.AxialQ + d.Dq, o.AxialR + d.Dr)).Prepend(o).Where(c => c is not null).Select(c => c!);
        }).ToList();
        return new Origin(cells.Any(c => c.Terrain == DemoTerrain.Forest), cells.Any(c => c.MainWaterway && !c.Sea),
            cells.Any(c => c.Sea), cells.Any(c => c.Terrain == DemoTerrain.Grassland), founded);
    }

    /// <summary>
    /// 開局：開局聚落相距 ≤ <see cref="TempClusterRange"/> 格者連成群（並查集），每群一個文化，
    /// 文化編號依群內最小聚落編號排序、名稱＝該聚落名＋「人」，各聚落 100% 屬於所屬文化。
    /// </summary>
    public static DemoCultureState Open(DemoHexWorld map, DemoWorldSites sites, RngRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(registry);
        var opening = sites.Settlements.Where(s => !s.IsFounded).OrderBy(s => s.Id).ToList();
        var parent = opening.ToDictionary(s => s.Id, s => s.Id);
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        foreach (var a in opening)
            foreach (var b in opening.Where(b => b.Id > a.Id && DemoKnowledge.Distance(map, a, b) <= TempClusterRange))
                parent[Find(b.Id)] = Find(a.Id);
        var clusters = opening.GroupBy(s => Find(s.Id)).OrderBy(g => g.Min(s => s.Id)).ToList();
        var cultures = ImmutableList.CreateBuilder<DemoCulture>();
        var settlements = ImmutableList.CreateBuilder<DemoSettlementCulture>();
        foreach (var cluster in clusters)
        {
            var id = cultures.Count + 1;
            var founder = cluster.OrderBy(s => s.Id).First();
            var traits = DrawTraits(OriginOf(map, cluster, founded: false), i => Draw(registry, id, i));
            cultures.Add(new DemoCulture(id, $"{founder.Name}人", traits));
            foreach (var s in cluster)
                settlements.Add(new DemoSettlementCulture(s.Id, ImmutableList.Create(new DemoCultureShare(id, 1000))));
        }
        var opened = settlements.OrderBy(s => s.SettlementId).ToImmutableList();
        return new DemoCultureState(cultures.ToImmutable(), opened,
            ImmutableList<DemoBatchCulture>.Empty, ImmutableList<DemoRepresentativeCulture>.Empty)
        {
            MainHistory = opened.Select(s => new DemoMainCulture(s.SettlementId, 0, s.Shares[0].CultureId)).ToImmutableList()
        };
    }

    /// <summary>
    /// 依人數加權平均兩組比例，結果取整到千分比（最大餘數法，合計 1000、去掉 0）。
    /// </summary>
    public static ImmutableList<DemoCultureShare> Mix(IReadOnlyList<DemoCultureShare> a, long weightA, IReadOnlyList<DemoCultureShare> b, long weightB)
    {
        if (weightB <= 0 || b.Count == 0) return a.ToImmutableList();
        if (weightA <= 0 || a.Count == 0) return b.ToImmutableList();
        var total = checked(weightA + weightB);
        var raw = a.Select(x => (x.CultureId, Value: (long)x.Permille * weightA))
            .Concat(b.Select(x => (x.CultureId, Value: (long)x.Permille * weightB)))
            .GroupBy(x => x.CultureId).Select(g => (Id: g.Key, Value: g.Sum(x => x.Value))).ToList();
        var floors = raw.Select(x => (x.Id, Floor: (int)(x.Value / total), Rem: x.Value % total)).ToList();
        var left = 1000 - floors.Sum(x => x.Floor);
        var bonus = floors.OrderByDescending(x => x.Rem).ThenBy(x => x.Id).Take(left).Select(x => x.Id).ToHashSet();
        return floors.Select(x => new DemoCultureShare(x.Id, x.Floor + (bonus.Contains(x.Id) ? 1 : 0)))
            .Where(x => x.Permille > 0).OrderBy(x => x.CultureId).ToImmutableList();
    }

    /// <summary>依比例抽一個文化（<paramref name="raw"/> 為 64 位元抽樣值）。</summary>
    public static int Pick(IReadOnlyList<DemoCultureShare> shares, ulong raw)
    {
        var pick = (int)(raw % 1000UL);
        foreach (var s in shares.OrderBy(s => s.CultureId))
        {
            if (pick < s.Permille) return s.CultureId;
            pick -= s.Permille;
        }
        return shares.OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId).First().CultureId;
    }

    /// <summary>
    /// 每週（生死、遷徙之後）：出發的隊伍帶走出發地比例；抵達者依人數加權併入目的聚落；開拓隊建立的新聚落沿用隊伍比例；
    /// 本週就任的代表人物依聚落比例抽文化，卸任者移除。
    /// </summary>
    public static DemoCultureState Advance(DemoCultureState state, DemoWeekReport report, DemoWorldSites sites, RngRegistry registry)
        => Advance(state, report, sites, registry, null, 0).State;

    /// <summary>
    /// 同上；有地圖時另做第 08-8 章 B4：文化分化（與同文化主體斷絕往來滿 <see cref="TempSplitYears"/> 年）與同化
    /// （每 <see cref="TempAssimilationYears"/> 年，少數文化有 <see cref="TempAssimilationPercent"/>% 轉為主要文化）。
    /// 分化事件編號從 <paramref name="nextEventId"/> 起。
    /// </summary>
    public static (DemoCultureState State, IReadOnlyList<DemoSettlementEvent> Events) Advance(DemoCultureState state, DemoWeekReport report,
        DemoWorldSites sites, RngRegistry registry, DemoHexWorld? map, long nextEventId,
        IReadOnlyList<DemoRepresentative>? representatives = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(registry);
        if (state.Cultures.IsEmpty)
            return (state, Array.Empty<DemoSettlementEvent>());
        var settlements = state.Settlements.ToDictionary(s => s.SettlementId, s => (IReadOnlyList<DemoCultureShare>)s.Shares);
        var batches = state.Batches.ToDictionary(b => b.BatchId, b => (IReadOnlyList<DemoCultureShare>)b.Shares);
        Flow(settlements, batches, report, sites, Array.Empty<DemoCultureShare>());

        var reps = state.Representatives.ToDictionary(r => r.RepresentativeId, r => r.CultureId);
        var repSettlement = new Dictionary<long, int>();
        foreach (var r in (representatives ?? Array.Empty<DemoRepresentative>()).Concat(report.Appointments))
            repSettlement[r.Id] = r.SettlementId;
        foreach (var exit in report.RepresentativeExits)
            reps.Remove(exit.Id);
        foreach (var r in report.Appointments.OrderBy(r => r.Id))
        {
            var shares = settlements.GetValueOrDefault(r.SettlementId);
            if (shares is { Count: > 0 })
                reps[r.Id] = Pick(shares, Draw(registry, r.Id, 0));
        }

        var cultures = state.Cultures.ToBuilder();
        var contacts = state.Contacts.ToDictionary(c => c.SettlementId, c => c.LastContactWeek);
        var events = new List<DemoSettlementEvent>();
        if (map is not null)
        {
            var week = report.Week;
            var living = sites.Settlements.Where(s => !s.IsRuin && report.Settlements.Any(l => l.SettlementId == s.Id && l.PopulationClosing > 0))
                .OrderBy(s => s.Id).ToList();
            int? MainOf(int id) => settlements.GetValueOrDefault(id) is { Count: > 0 } sh
                ? sh.OrderByDescending(x => x.Permille).ThenBy(x => x.CultureId).First().CultureId : null;

            // 同化：每 TempAssimilationYears 年，聚落內少數文化有一部分轉為主要文化（48C：不瞬間同化）。
            if (week % (TempAssimilationYears * WeeksPerYear) == 0)
                foreach (var s in living)
                    if (settlements.GetValueOrDefault(s.Id) is { Count: > 1 } sh && MainOf(s.Id) is { } main)
                        settlements[s.Id] = Assimilate(sh, main);

            // 分化：同文化聚落依 TempContactRange 連成區塊；最大的區塊是主體（同大取編號小），主體與從主體來的移入者刷新往來。
            foreach (var group in living.Where(s => MainOf(s.Id) is not null).GroupBy(s => MainOf(s.Id)!.Value).OrderBy(g => g.Key))
            {
                var blocks = Blocks(map, group.ToList());
                var core = blocks.OrderByDescending(b => b.Count).ThenBy(b => b.Min(s => s.Id)).First();
                foreach (var s in core)
                    contacts[s.Id] = week;
                var coreIds = core.Select(s => s.Id).ToHashSet();
                foreach (var leg in report.Migrations.Where(m => m.Arrived && coreIds.Contains(m.FromSettlementId)))
                    if (group.Any(s => s.Id == leg.ToSettlementId))
                        contacts[leg.ToSettlementId] = week;
                foreach (var block in blocks.Where(b => !ReferenceEquals(b, core)))
                {
                    foreach (var s in block.Where(s => !contacts.ContainsKey(s.Id)))
                        contacts[s.Id] = week; // 第一次看到（例：剛開拓）從現在起算
                    if (block.Any(s => week - contacts[s.Id] < TempSplitYears * WeeksPerYear))
                        continue;
                    // 整個區塊分化出子文化：沿用上層文化的特性，換掉一個（重新抽同一池）。
                    var parent = cultures.First(c => c.Id == group.Key);
                    var id = cultures.Max(c => c.Id) + 1;
                    var founder = block.OrderBy(s => s.Id).First();
                    var child = new DemoCulture(id, $"{founder.Name}人", Mutate(parent.Traits, i => Draw(registry, id, 1000 + i)), parent.Id, week);
                    cultures.Add(child);
                    foreach (var s in block)
                    {
                        settlements[s.Id] = settlements[s.Id].Select(x => x.CultureId == parent.Id ? x with { CultureId = id } : x).ToImmutableList();
                        contacts[s.Id] = week;
                        foreach (var r in reps.Where(r => r.Value == parent.Id).Select(r => r.Key).ToList())
                            if (IsIn(r, s.Id))
                                reps[r] = id;
                    }
                    events.Add(new DemoSettlementEvent(week, report.WorldTime, founder.Id, founder.Name,
                        DemoSettlementEventKind.CultureSplit, id,
                        $"與其他{parent.Name}聚落斷絕往來滿 {TempSplitYears} 年（TEMP），{string.Join("、", block.Select(s => s.Name))}分化出新的文化「{child.Name}」"
                        + $"（{DemoCultureTraitName(parent.Traits, child.Traits)}）",
                        EventId: nextEventId++));
                }
            }
            // 已不是有人聚落的計時移除。
            foreach (var id in contacts.Keys.Where(id => living.All(s => s.Id != id)).ToList())
                contacts.Remove(id);
        }

        bool IsIn(long repId, int settlementId) => repSettlement.GetValueOrDefault(repId) == settlementId;

        // 第 08-8 章 B2：主要文化有變化（建立、遷入、分化、同化）時記一筆。
        var history = state.MainHistory.ToBuilder();
        var last = state.MainHistory.GroupBy(h => h.SettlementId).ToDictionary(g => g.Key, g => g.Last().CultureId);
        foreach (var (id, shares) in settlements.OrderBy(kv => kv.Key))
        {
            if (shares.Count == 0) continue;
            var main = shares.OrderByDescending(x => x.Permille).ThenBy(x => x.CultureId).First().CultureId;
            if (!last.TryGetValue(id, out var before) || before != main)
                history.Add(new DemoMainCulture(id, report.Week, main));
        }

        return (state with
        {
            MainHistory = history.OrderBy(h => h.SettlementId).ThenBy(h => h.Week).ToImmutableList(),
            Cultures = cultures.ToImmutable(),
            Settlements = settlements.OrderBy(kv => kv.Key).Select(kv => new DemoSettlementCulture(kv.Key, kv.Value.ToImmutableList())).ToImmutableList(),
            Batches = batches.OrderBy(kv => kv.Key).Select(kv => new DemoBatchCulture(kv.Key, kv.Value.ToImmutableList())).ToImmutableList(),
            Representatives = reps.OrderBy(kv => kv.Key).Select(kv => new DemoRepresentativeCulture(kv.Key, kv.Value)).ToImmutableList(),
            Contacts = contacts.OrderBy(kv => kv.Key).Select(kv => new DemoCultureContact(kv.Key, kv.Value)).ToImmutableList()
        }, events);
    }

    /// <summary>
    /// 比例隨遷徙流動（文化與信仰共用）：出發的隊伍帶走出發地比例；抵達者依人數加權併入目的聚落；開拓隊建立的新聚落沿用隊伍比例；
    /// 途中全員死亡、解散的隊伍移除。<paramref name="none"/>＝沒有紀錄的聚落或隊伍視為的比例（文化＝空、信仰＝全員無信仰）。
    /// </summary>
    public static void Flow(Dictionary<int, IReadOnlyList<DemoCultureShare>> settlements, Dictionary<int, IReadOnlyList<DemoCultureShare>> batches,
        DemoWeekReport report, DemoWorldSites sites, IReadOnlyList<DemoCultureShare> none)
    {
        foreach (var leg in report.Migrations.Where(m => m.Departed).OrderBy(m => m.BatchId))
            batches[leg.BatchId] = settlements.GetValueOrDefault(leg.FromSettlementId) ?? none;

        var arrivedPeople = new Dictionary<int, long>();
        foreach (var leg in report.Migrations.Where(m => m.Arrived).OrderBy(m => m.BatchId))
        {
            var carried = batches.GetValueOrDefault(leg.BatchId) ?? none;
            batches.Remove(leg.BatchId);
            if (carried.Count == 0 || leg.PopulationClosing <= 0)
                continue;
            if (leg.Founding)
            {
                if (sites.Settlements.FirstOrDefault(s => s.FoundedByBatchId == leg.BatchId) is { } founded)
                    settlements[founded.Id] = carried;
                continue;
            }
            var target = leg.ToSettlementId;
            var ledger = report.Settlements.FirstOrDefault(l => l.SettlementId == target);
            var already = arrivedPeople.GetValueOrDefault(target);
            var existing = ledger is null ? 0 : Math.Max(0, ledger.PopulationClosing - ledger.Immigrants + already);
            settlements[target] = Mix(settlements.GetValueOrDefault(target) ?? none, existing,
                carried, leg.PopulationClosing);
            arrivedPeople[target] = already + leg.PopulationClosing;
        }
        // 途中全員死亡、解散的隊伍不再保留。
        var travelling = report.Migrations.Where(m => !m.Arrived && m.PopulationClosing > 0).Select(m => m.BatchId).ToHashSet();
        foreach (var id in batches.Keys.Where(id => !travelling.Contains(id)).ToList())
            batches.Remove(id);
    }


    /// <summary>【TEMP】第 08-8 章 B4：與同文化主體斷絕往來滿這麼多年就分化（48C CUL_SPLIT；08-8 原訂 50 年，百年只分化 1 次，改 30 年，08-10c）。</summary>
    public const int TempSplitYears = 30;

    /// <summary>【TEMP】同化週期（年）與每次少數文化轉為主要文化的比例（%）。</summary>
    public const int TempAssimilationYears = 10;
    public const int TempAssimilationPercent = 20;

    private const int WeeksPerYear = 52;

    /// <summary>少數文化各有 <see cref="TempAssimilationPercent"/>%（向下取整）轉為主要文化。</summary>
    public static ImmutableList<DemoCultureShare> Assimilate(IReadOnlyList<DemoCultureShare> shares, int main)
    {
        var moved = 0;
        var result = new List<DemoCultureShare>();
        foreach (var s in shares.Where(s => s.CultureId != main))
        {
            var take = s.Permille * TempAssimilationPercent / 100;
            moved += take;
            if (s.Permille - take > 0)
                result.Add(s with { Permille = s.Permille - take });
        }
        var mainShare = shares.FirstOrDefault(s => s.CultureId == main)?.Permille ?? 0;
        result.Add(new DemoCultureShare(main, mainShare + moved));
        return result.OrderBy(s => s.CultureId).ToImmutableList();
    }

    /// <summary>
    /// 【TEMP】分化判定的往來範圍：同文化聚落相距不超過這麼多格才算同一區塊（開局分群用 6 格；
    /// 06 格時整張圖連成一串、百年內從不分化，08-10c 第 B4 列）。
    /// </summary>
    public const int TempContactRange = 3;

    /// <summary>同文化聚落依 <see cref="TempContactRange"/> 格連成的區塊（並查集）。</summary>
    public static List<List<DemoSettlement>> Blocks(DemoHexWorld map, IReadOnlyList<DemoSettlement> settlements)
    {
        var parent = settlements.ToDictionary(s => s.Id, s => s.Id);
        int Find(int x) => parent[x] == x ? x : parent[x] = Find(parent[x]);
        foreach (var a in settlements)
            foreach (var b in settlements.Where(b => b.Id > a.Id && DemoKnowledge.Distance(map, a, b) <= TempContactRange))
                parent[Find(b.Id)] = Find(a.Id);
        return settlements.GroupBy(s => Find(s.Id)).Select(g => g.OrderBy(s => s.Id).ToList()).OrderBy(b => b[0].Id).ToList();
    }

    /// <summary>子文化：隨機換掉上層文化的一個特性（重新抽同一池，互斥組照舊）。</summary>
    public static ImmutableList<string> Mutate(ImmutableList<string> traits, Func<ulong, ulong> draw)
    {
        if (traits.IsEmpty)
            return traits;
        var drop = traits[(int)(draw(0) % (ulong)traits.Count)];
        var pool = Pool.Value.First(e => e.TraitId == drop).PoolId;
        var keep = traits.Remove(drop);
        var groups = Pool.Value.Where(e => keep.Contains(e.TraitId) && e.ExclusiveGroup.Length > 0).Select(e => e.ExclusiveGroup).ToHashSet();
        var candidates = Pool.Value.Where(e => e.PoolId == pool && e.TraitId != drop && !keep.Contains(e.TraitId)
            && (e.ExclusiveGroup.Length == 0 || !groups.Contains(e.ExclusiveGroup)) && e.Weight > 0).ToList();
        if (candidates.Count == 0)
            return traits;
        var pick = (long)(draw(1) % (ulong)candidates.Sum(e => e.Weight));
        foreach (var e in candidates)
        {
            if (pick < e.Weight)
                return keep.Insert(traits.IndexOf(drop), e.TraitId);
            pick -= e.Weight;
        }
        return traits;
    }

    private static string DemoCultureTraitName(IReadOnlyList<string> before, IReadOnlyList<string> after)
    {
        var lost = before.Except(after).Select(TraitName).ToList();
        var gained = after.Except(before).Select(TraitName).ToList();
        return lost.Count == 0 ? "傳統不變" : $"不再{string.Join("、", lost)}，改為{string.Join("、", gained)}";
    }
}
