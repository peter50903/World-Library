using System.Collections.Immutable;
using System.Globalization;
using WorldSimulation.Core.Config;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-8 章 B3：一個信仰（48A：零信徒不刪除，<see cref="ExtinctWeek"/> 記滅絕週次；同 ID 復興時歸零）。
/// <see cref="Tenets"/>＝核心信條（P04A，2～4 條）；<see cref="Doctrines"/>＝制度教義（P04B～P04J，依 06K）。
/// </summary>
public sealed record DemoFaith(int Id, string Name, int FounderSettlementId, long FounderId, int FoundedWeek,
    ImmutableList<string> Tenets, ImmutableList<string> Doctrines, int ExtinctWeek = 0);

/// <summary>
/// 第 08-8 章 B3：信仰狀態。聚落與途中隊伍的背景人口只存信仰比例（千分比；信仰編號 0＝無信仰，沿用文化的比例紀錄型別）；
/// 沒有紀錄＝全員無信仰。代表人物各自記一個信仰（沒有紀錄＝無信仰）。
/// </summary>
public sealed record DemoFaithState(
    ImmutableList<DemoFaith> Faiths,
    ImmutableList<DemoSettlementCulture> Settlements,
    ImmutableList<DemoBatchCulture> Batches,
    ImmutableList<DemoRepresentativeCulture> Representatives)
{
    public static DemoFaithState Empty { get; } = new(ImmutableList<DemoFaith>.Empty, ImmutableList<DemoSettlementCulture>.Empty,
        ImmutableList<DemoBatchCulture>.Empty, ImmutableList<DemoRepresentativeCulture>.Empty);

    public DemoFaith? Faith(int id) => Faiths.FirstOrDefault(f => f.Id == id);

    /// <summary>某聚落的信仰比例（含無信仰＝0）；沒有紀錄＝全員無信仰。</summary>
    public IReadOnlyList<DemoCultureShare> SharesOf(int settlementId)
        => Settlements.FirstOrDefault(s => s.SettlementId == settlementId)?.Shares ?? DemoFaiths.NoFaith;

    /// <summary>聚落的主要信仰（不含無信仰；比例最高、同比例取編號小）；沒有信徒＝null。</summary>
    public int? MainFaithOf(int settlementId)
        => SharesOf(settlementId).Where(s => s.CultureId != 0).OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId)
            .Select(s => (int?)s.CultureId).FirstOrDefault();

    public int FaithOfRepresentative(long representativeId)
        => Representatives.FirstOrDefault(r => r.RepresentativeId == representativeId)?.CultureId ?? 0;

    public string Fingerprint()
        => string.Concat(Faiths.Select(f => $"rel{f.Id}:{f.Name}:{f.FounderSettlementId}/{f.FounderId}@{f.FoundedWeek}:{string.Join('+', f.Tenets)}|{string.Join('+', f.Doctrines)}x{f.ExtinctWeek};"))
           + string.Concat(Settlements.Select(s => $"sr{s.SettlementId}=" + string.Join(',', s.Shares.Select(x => $"{x.CultureId}:{x.Permille}")) + ";"))
           + string.Concat(Batches.Select(s => $"br{s.BatchId}=" + string.Join(',', s.Shares.Select(x => $"{x.CultureId}:{x.Permille}")) + ";"))
           + string.Concat(Representatives.Select(r => $"rr{r.RepresentativeId}={r.CultureId};"));
}

/// <summary>P04A～P04J 信條／教義池的一列。</summary>
public sealed record DemoFaithPoolEntry(string PoolId, string EntryId, string Name, int Weight, int DrawMin, int DrawMax,
    string Required, string Forbidden, string ExclusiveGroup);

/// <summary>
/// 第 08-8 章 B3【TEMP 規則】信仰：開局人人無信仰；聚落習得「祖靈祭祀」時，6 格內已有信徒聚落就只是接觸既有信仰，
/// 沒有才由村長創立新信仰（依 06K 抽核心信條 2～4 條與制度教義，權重依創立者的文化）。
/// 每週：創立聚落的無信仰者 2% 改信、6 格內有以某信仰為主的聚落時 0.5%（積極傳教×2、封閉信仰×0.5）；
/// 比例隨遷徙流動（同文化）；代表人物就任時依比例抽信仰；所有聚落、隊伍與代表人物都沒有信徒時滅絕（不刪除）。
/// 抽選用「文化信仰」（RELIGION）亂數流的實體局部亂數。
/// </summary>
public static class DemoFaiths
{
    public const string Namespace = "TEMP-FAITH-v1";

    public static readonly IReadOnlyList<DemoCultureShare> NoFaith = ImmutableList.Create(new DemoCultureShare(0, 1000));

    /// <summary>【TEMP】信仰的勢力範圍（格）：接觸既有信仰、鄰近傳播都用這個距離（原 6 格；第 08-13 章改 3 格，遠方聚落各自創立信仰）。</summary>
    public const int TempRange = 3;

    /// <summary>【TEMP】每週改信比例（萬分比，乘以無信仰者的千分比）：創立聚落 2%、鄰近聚落 0.5%。</summary>
    public const int TempFounderRate = 200;
    public const int TempNeighbourRate = 50;

    /// <summary>【TEMP】創立者文化加權：條件特性在文化內時，對應信條權重×2（08-8 第 3 節）。</summary>
    public static readonly IReadOnlyDictionary<string, string> CultureBias = new Dictionary<string, string>
    {
        ["CT_MARTIAL"] = "DOC_WAR",
        ["CT_FOREST_FOLK"] = "DOC_NATURE",
        ["CT_ROOTED"] = "DOC_ANCESTOR"
    };

    /// <summary>核心信條總數上限（06K CORE_TENETS 2～4）。</summary>
    public const int MaxTenets = 4;

    public const string AncestorRites = "TECH_ANCESTOR_RITES";

    private static readonly Lazy<(IReadOnlyList<DemoFaithPoolEntry> Entries, IReadOnlyList<(string PoolId, int Min, int Max, bool Core)> Rules)> Data = new(Load);

    public static IReadOnlyList<DemoFaithPoolEntry> Pool => Data.Value.Entries;

    public static string EntryName(string id) => Data.Value.Entries.FirstOrDefault(e => e.EntryId == id)?.Name ?? id;

    private static (IReadOnlyList<DemoFaithPoolEntry>, IReadOnlyList<(string, int, int, bool)>) Load()
    {
        var rules = Rows("06K_宗教生成規則.csv")
            .Where(r => r.GetValueOrDefault("GenerationRuleID", "").Length > 0 && Bool(r["Enabled"]))
            .Select(r => (PoolId: r["PoolID"], Min: Int(r["DrawMin"]), Max: Int(r["DrawMax"]), Core: r["階段"] == "核心信條", Sheet: r["PoolSheet"]))
            .ToList();
        var entries = new List<DemoFaithPoolEntry>();
        foreach (var sheet in rules.Select(r => r.Sheet).Distinct())
        {
            entries.AddRange(Rows(sheet + ".csv").Where(r => Bool(r["Enabled"])).Select(r => new DemoFaithPoolEntry(r["PoolID"], r["EntryID"], r["EntryName"],
                Int(r["Weight"]), Int(r["PoolDrawMin"]), Int(r["PoolDrawMax"]), r.GetValueOrDefault("RequiredConditions", ""),
                r.GetValueOrDefault("ForbiddenConditions", ""), r.GetValueOrDefault("ExclusiveGroup", ""))));
        }
        return (entries, rules.Select(r => (r.PoolId, r.Min, r.Max, r.Core)).ToList());
    }

    private static bool Bool(string s) => string.Equals(s, "TRUE", StringComparison.OrdinalIgnoreCase);

    private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);

    private static IEnumerable<Dictionary<string, string>> Rows(string sheet)
    {
        var rows = ParameterSheet.ParseCsv(OfficialConfig.ReadSheetText(sheet));
        var header = rows[0].Select(h => h.Trim().TrimStart('﻿')).ToList();
        return rows.Skip(1).Where(r => r.Count > 0 && !string.IsNullOrWhiteSpace(r[0]))
            .Select(r => header.Select((h, i) => (h, v: i < r.Count ? r[i].Trim() : "")).ToDictionary(x => x.h, x => x.v));
    }

    /// <summary>前置條件：逗號＝全部要有、直線＝任一即可；禁止條件：逗號列出的都不能有。</summary>
    public static bool Allowed(DemoFaithPoolEntry e, IReadOnlyCollection<string> chosen)
    {
        if (e.Required.Length > 0 && !e.Required.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .All(part => part.Split('|').Any(chosen.Contains)))
            return false;
        return e.Forbidden.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).All(f => !chosen.Contains(f));
    }

    /// <summary>
    /// 依 06K 抽核心信條（先價值、世界觀，再儀式，總數不超過 4）與制度教義（必抽者 1 項、選抽者 0～1 項）。
    /// <paramref name="cultureTraits"/>＝創立者文化特性（加權）；<paramref name="draw"/>(i)＝第 i 次抽樣值。
    /// </summary>
    public static (ImmutableList<string> Tenets, ImmutableList<string> Doctrines) Draw(IReadOnlyCollection<string> cultureTraits, Func<ulong, ulong> draw)
    {
        var bias = CultureBias.Where(kv => cultureTraits.Contains(kv.Key)).Select(kv => kv.Value).ToHashSet();
        ulong next = 0;
        var tenets = new List<string>();
        var doctrines = new List<string>();
        foreach (var (poolId, min, max, core) in Data.Value.Rules)
        {
            var target = core ? tenets : doctrines;
            var entries = Data.Value.Entries.Where(e => e.PoolId == poolId).ToList();
            if (entries.Count == 0)
                continue;
            // 核心信條池的抽數照 P04A（價值 1～2、世界觀 1～2、儀式 0～2）；制度教義照 06K。
            var lo = core ? entries[0].DrawMin : min;
            var hi = core ? entries[0].DrawMax : max;
            var count = lo + (int)(draw(next++) % (ulong)(hi - lo + 1));
            if (core)
                count = Math.Min(count, MaxTenets - tenets.Count);
            var chosen = tenets.Concat(doctrines).ToHashSet();
            var groups = Data.Value.Entries.Where(e => chosen.Contains(e.EntryId) && e.ExclusiveGroup.Length > 0).Select(e => e.ExclusiveGroup).ToHashSet();
            for (var k = 0; k < count; k++)
            {
                var candidates = entries.Where(e => !chosen.Contains(e.EntryId) && (e.ExclusiveGroup.Length == 0 || !groups.Contains(e.ExclusiveGroup))
                    && Allowed(e, chosen) && e.Weight > 0).ToList();
                if (candidates.Count == 0)
                    break;
                long Weight(DemoFaithPoolEntry e) => bias.Contains(e.EntryId) ? e.Weight * 2L : e.Weight;
                var pick = (long)(draw(next++) % (ulong)candidates.Sum(Weight));
                foreach (var e in candidates)
                {
                    if (pick < Weight(e))
                    {
                        target.Add(e.EntryId);
                        chosen.Add(e.EntryId);
                        if (e.ExclusiveGroup.Length > 0)
                            groups.Add(e.ExclusiveGroup);
                        break;
                    }
                    pick -= Weight(e);
                }
            }
        }
        return (tenets.ToImmutableList(), doctrines.ToImmutableList());
    }

    /// <summary>傳播倍率（萬分比率乘數，百分比）：積極傳教×2、封閉信仰×0.5。</summary>
    public static int SpreadPercent(DemoFaith faith)
        => faith.Doctrines.Contains("RDX_MISSIONARY") ? 200 : faith.Doctrines.Contains("RDX_ISOLATIONIST") ? 50 : 100;

    private static ulong Draw(RngRegistry registry, long entityId, ulong index)
        => EntityLocalRandom.Create(registry, EntityId.From((ulong)entityId), RngStreams.Religion, Namespace).GetUInt64(index);

    /// <summary>
    /// 每週（文化之後）：信仰比例隨遷徙流動、新任代表人物抽信仰；本週習得祖靈祭祀的聚落創立或接觸信仰；改信；滅絕與復興。
    /// 事件編號從 <paramref name="nextEventId"/> 起。
    /// </summary>
    public static (DemoFaithState State, IReadOnlyList<DemoSettlementEvent> Events) Advance(DemoFaithState state, DemoWeekReport report,
        IReadOnlyList<DemoSettlementEvent> learned, DemoWorldSites sites, DemoHexWorld map, RngRegistry registry,
        IReadOnlyList<DemoRepresentative> representatives, Func<int, IReadOnlyCollection<string>> cultureTraits, long nextEventId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(map);
        var settlements = state.Settlements.ToDictionary(s => s.SettlementId, s => (IReadOnlyList<DemoCultureShare>)s.Shares);
        var batches = state.Batches.ToDictionary(b => b.BatchId, b => (IReadOnlyList<DemoCultureShare>)b.Shares);
        IReadOnlyList<DemoCultureShare> Of(int id) => settlements.GetValueOrDefault(id) ?? NoFaith;
        DemoCultures.Flow(settlements, batches, report, sites, NoFaith);

        var reps = state.Representatives.ToDictionary(r => r.RepresentativeId, r => r.CultureId);
        foreach (var exit in report.RepresentativeExits)
            reps.Remove(exit.Id);
        foreach (var r in report.Appointments.OrderBy(r => r.Id))
        {
            var faith = DemoCultures.Pick(Of(r.SettlementId), Draw(registry, r.Id, 0));
            if (faith != 0) reps[r.Id] = faith;
        }

        var week = report.Week;
        var faiths = state.Faiths.ToList();
        var events = new List<DemoSettlementEvent>();
        var living = sites.Settlements.Where(s => !s.IsRuin && report.Settlements.Any(l => l.SettlementId == s.Id && l.PopulationClosing > 0))
            .OrderBy(s => s.Id).ToList();
        int? Main(int id) => Of(id).Where(x => x.CultureId != 0).OrderByDescending(x => x.Permille).ThenBy(x => x.CultureId)
            .Select(x => (int?)x.CultureId).FirstOrDefault();

        // 創立：本週習得祖靈祭祀、6 格內沒有信徒聚落的聚落由村長創立新信仰（一聚落一週最多一個）。
        var rites = DemoTechnology.Of(AncestorRites);
        foreach (var e in learned.Where(e => e.Kind == DemoSettlementEventKind.KnowledgeLearned && e.Count == rites.Index).OrderBy(e => e.EventId))
        {
            var site = living.FirstOrDefault(s => s.Id == e.SettlementId);
            if (site is null || Main(site.Id) is not null
                || living.Any(o => o.Id != site.Id && Main(o.Id) is not null && DemoKnowledge.Distance(map, site, o) <= TempRange))
                continue;
            var id = faiths.Count == 0 ? 1 : faiths.Max(f => f.Id) + 1;
            var founder = representatives.Concat(report.Appointments).Where(r => r.SettlementId == site.Id)
                .OrderBy(r => r.Role).ThenBy(r => r.Id).FirstOrDefault();
            var (tenets, doctrines) = Draw(cultureTraits(site.Id), i => Draw(registry, id, 1000 + i));
            var faith = new DemoFaith(id, $"{site.Name}祖靈信仰", site.Id, founder?.Id ?? 0, week, tenets, doctrines);
            faiths.Add(faith);
            settlements[site.Id] = Convert(Of(site.Id), id, TempFounderRate * SpreadPercent(faith) / 100);
            if (founder is not null)
                reps[founder.Id] = id;
            events.Add(new DemoSettlementEvent(week, report.WorldTime, site.Id, site.Name, DemoSettlementEventKind.FaithFounded, id,
                $"{(founder is null ? "族人" : $"{DemoRepresentative.RoleName(founder.Role)}{founder.Name}")}創立「{faith.Name}」"
                + $"（信條：{string.Join("、", tenets.Select(EntryName))}）",
                EventId: nextEventId++, CauseEventId: e.EventId));
        }

        // 改信（只改無信仰者；信仰之間的改宗留給第 11 章）。
        var mains = living.ToDictionary(s => s.Id, s => Main(s.Id));
        foreach (var s in living)
        {
            var own = faiths.FirstOrDefault(f => f.FounderSettlementId == s.Id && f.ExtinctWeek == 0);
            DemoFaith? target = own;
            var rate = own is null ? 0 : TempFounderRate * SpreadPercent(own) / 100;
            if (target is null)
            {
                var near = living.Where(o => o.Id != s.Id && mains[o.Id] is not null && DemoKnowledge.Distance(map, s, o) <= TempRange)
                    .GroupBy(o => mains[o.Id]!.Value).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault();
                if (near is null)
                    continue;
                target = faiths.First(f => f.Id == near.Key);
                rate = TempNeighbourRate * SpreadPercent(target) / 100;
            }
            var before = Of(s.Id);
            var after = Convert(before, target.Id, rate);
            if (!ReferenceEquals(after, before))
                settlements[s.Id] = after;
        }
        foreach (var id in settlements.Keys.Where(id => settlements[id].All(x => x.CultureId == 0)).ToList())
            settlements.Remove(id);
        foreach (var id in batches.Keys.Where(id => batches[id].All(x => x.CultureId == 0)).ToList())
            batches.Remove(id);

        // 滅絕（48A：不刪除）與同 ID 復興。
        var believed = settlements.Values.Concat(batches.Values).SelectMany(x => x).Select(x => x.CultureId).Concat(reps.Values).ToHashSet();
        for (var i = 0; i < faiths.Count; i++)
        {
            var f = faiths[i];
            if (f.ExtinctWeek == 0 && !believed.Contains(f.Id))
            {
                faiths[i] = f with { ExtinctWeek = week };
                var place = sites.Settlements.FirstOrDefault(s => s.Id == f.FounderSettlementId);
                events.Add(new DemoSettlementEvent(week, report.WorldTime, f.FounderSettlementId, place?.Name ?? "", DemoSettlementEventKind.FaithExtinct, f.Id,
                    $"最後的信徒消失，「{f.Name}」失傳", EventId: nextEventId++));
            }
            else if (f.ExtinctWeek != 0 && believed.Contains(f.Id))
            {
                faiths[i] = f with { ExtinctWeek = 0 };
            }
        }

        return (state with
        {
            Faiths = faiths.ToImmutableList(),
            Settlements = settlements.OrderBy(kv => kv.Key).Select(kv => new DemoSettlementCulture(kv.Key, kv.Value.ToImmutableList())).ToImmutableList(),
            Batches = batches.OrderBy(kv => kv.Key).Select(kv => new DemoBatchCulture(kv.Key, kv.Value.ToImmutableList())).ToImmutableList(),
            Representatives = reps.OrderBy(kv => kv.Key).Select(kv => new DemoRepresentativeCulture(kv.Key, kv.Value)).ToImmutableList()
        }, events);
    }

    /// <summary>無信仰者的 <paramref name="rate"/>／10000 改信（至少 1‰，無信仰者用完為止）；沒有變化時回傳原本的清單。</summary>
    public static IReadOnlyList<DemoCultureShare> Convert(IReadOnlyList<DemoCultureShare> shares, int faithId, int rate)
    {
        var none = shares.FirstOrDefault(x => x.CultureId == 0)?.Permille ?? 0;
        if (none == 0 || rate <= 0)
            return shares;
        var moved = Math.Min(none, Math.Max(1, (int)((long)none * rate / 10000)));
        var result = shares.Where(x => x.CultureId != 0 && x.CultureId != faithId).ToList();
        if (none - moved > 0)
            result.Add(new DemoCultureShare(0, none - moved));
        result.Add(new DemoCultureShare(faithId, (shares.FirstOrDefault(x => x.CultureId == faithId)?.Permille ?? 0) + moved));
        return result.OrderBy(x => x.CultureId).ToImmutableList();
    }
}
