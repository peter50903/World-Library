using System.Collections.Immutable;
using System.Globalization;
using WorldSimulation.Core.Config;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-8 章 B3：一個信仰（48A：零信徒不刪除，<see cref="ExtinctWeek"/> 記滅絕週次；同 ID 復興時歸零）。
/// <see cref="Tenets"/>＝核心信條（P04A，2～4 條）；<see cref="Doctrines"/>＝制度教義（P04B～P04J，依 06K）。
/// 第 08-22g 章：<see cref="ParentId"/>＝由哪個信仰分裂出來（異端；不是＝0），用來判斷同源（誤入歧途）。
/// </summary>
public sealed record DemoFaith(int Id, string Name, int FounderSettlementId, long FounderId, int FoundedWeek,
    ImmutableList<string> Tenets, ImmutableList<string> Doctrines, int ExtinctWeek = 0, int ParentId = 0);

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
        => SettlementIndex.GetValue(Settlements, list => list.GroupBy(s => s.SettlementId).ToDictionary(g => g.Key, g => g.First()))
            .GetValueOrDefault(settlementId)?.Shares ?? DemoFaiths.NoFaith; // 第 08-13 章第 5 節：依聚落索引（同 FirstOrDefault）

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImmutableList<DemoSettlementCulture>, Dictionary<int, DemoSettlementCulture>> SettlementIndex = new();

    /// <summary>聚落的主要信仰（不含無信仰；比例最高、同比例取編號小）；沒有信徒＝null。</summary>
    public int? MainFaithOf(int settlementId)
        => SharesOf(settlementId).Where(s => s.CultureId != 0).OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId)
            .Select(s => (int?)s.CultureId).FirstOrDefault();

    public int FaithOfRepresentative(long representativeId)
        => Representatives.FirstOrDefault(r => r.RepresentativeId == representativeId)?.CultureId ?? 0;

    public string Fingerprint()
        => string.Concat(Faiths.Select(f => $"rel{f.Id}:{f.Name}:{f.FounderSettlementId}/{f.FounderId}@{f.FoundedWeek}:{string.Join('+', f.Tenets)}|{string.Join('+', f.Doctrines)}x{f.ExtinctWeek}^{f.ParentId};"))
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

    /// <summary>第 08-22d 章【TEMP】異端分裂：每幾週檢查一次（每個聚落錯開）；信仰創立滿幾週後才會分裂；離聖地幾格以上算遙遠；每次的機率。</summary>
    public const int TempSchismCheckWeeks = 52, TempSchismMinWeeks = 1040, TempSchismDistance = 12, TempSchismPercent = 6;

    /// <summary>
    /// 第 08-22g 章【TEMP】狂熱度（0～100，參考 CK3；由現況算、不另存）：基本 <see cref="TempFervorBase"/>；
    /// 聖地在手（創立聚落還有人住、主要信仰還是它）+<see cref="TempFervorHolySite"/>，否則 <see cref="TempFervorHolySiteLost"/>；
    /// 每個以它為主要信仰的聚落 +<see cref="TempFervorPerSettlement"/>（最多 +<see cref="TempFervorBreadthMax"/>）；
    /// 每個還有信徒、從它分裂出去的異端 <see cref="TempFervorPerHeresy"/>。
    /// 狂熱度低的信仰容易分裂出異端（機率 ×(150−狂熱度)%），改宗時也守不住信徒。
    /// </summary>
    public const int TempFervorBase = 40, TempFervorHolySite = 30, TempFervorHolySiteLost = -20, TempFervorPerSettlement = 2,
        TempFervorBreadthMax = 30, TempFervorPerHeresy = -15;

    /// <summary>
    /// 第 08-22g 章【TEMP】改宗（參考 CK3：統治者讓領地改信）：上級的主要信仰和自己不同時，每週有 <see cref="TempLiegeConvertRate"/>／10000
    /// 的人（其他信仰的信徒與無信仰者）改信上級的信仰，再乘以（100＋上級信仰的狂熱度−自己信仰的狂熱度）%（限 20～200%）。
    /// 主要信仰因此換成上級的信仰時記「改宗」。
    /// </summary>
    public const int TempLiegeConvertRate = 30;

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

    /// <summary>第 08-22g 章：兩個信仰的敵意——0＝同一個信仰、1＝誤入歧途（同源：追到最早的母信仰是同一個）、2＝敵對（不同源）。</summary>
    public static int Hostility(DemoFaithState state, int a, int b)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (a == b)
            return 0;
        return state.Faith(a) is { } fa && state.Faith(b) is { } fb && Root(state, fa) == Root(state, fb) ? 1 : 2;
    }

    /// <summary>敵意的名稱。</summary>
    public static string HostilityName(int level) => level switch { 0 => "同一信仰", 1 => "誤入歧途", _ => "敵對" };

    private static int Root(DemoFaithState state, DemoFaith faith)
    {
        for (var hops = 0; hops < 64 && faith.ParentId != 0 && state.Faith(faith.ParentId) is { } parent; hops++)
            faith = parent;
        return faith.Id;
    }

    /// <summary>
    /// 第 08-22g 章：某信仰的狂熱度與明細。<paramref name="living"/>＝有人住的聚落；<paramref name="mainOf"/>＝聚落的主要信仰。
    /// </summary>
    public static (int Value, IReadOnlyList<(string Text, int Value)> Factors) Fervor(IReadOnlyList<DemoFaith> faiths, int faithId,
        IReadOnlyCollection<DemoSettlement> living, Func<int, int?> mainOf)
    {
        ArgumentNullException.ThrowIfNull(faiths);
        ArgumentNullException.ThrowIfNull(living);
        ArgumentNullException.ThrowIfNull(mainOf);
        var faith = faiths.FirstOrDefault(f => f.Id == faithId);
        var factors = new List<(string, int)> { ("基本", TempFervorBase) };
        if (faith is null)
            return (TempFervorBase, factors);
        var holy = living.Any(s => s.Id == faith.FounderSettlementId) && mainOf(faith.FounderSettlementId) == faith.Id;
        factors.Add(holy ? ("聖地在手", TempFervorHolySite) : ("失去聖地", TempFervorHolySiteLost));
        var settlements = living.Count(s => mainOf(s.Id) == faith.Id);
        if (settlements > 0)
            factors.Add(($"{settlements} 個聚落以它為主要信仰", Math.Min(TempFervorBreadthMax, TempFervorPerSettlement * settlements)));
        var heresies = faiths.Count(f => f.ParentId == faith.Id && f.ExtinctWeek == 0);
        if (heresies > 0)
            factors.Add(($"分裂出 {heresies} 個異端", TempFervorPerHeresy * heresies));
        return (Math.Clamp(factors.Sum(f => f.Item2), 0, 100), factors);
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
        IReadOnlyList<DemoRepresentative> representatives, Func<int, IReadOnlyCollection<string>> cultureTraits, long nextEventId,
        Func<int, DemoSettlementTier>? tierOf = null,
        Func<int, int?>? liegeOf = null) // 第 08-22g 章：上級（改宗）
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
        // 第 08-22g 章：本週開始時各信仰的狂熱度（分裂與改宗都用它）。
        var mainAtStart = living.ToDictionary(s => s.Id, s => Main(s.Id));
        var fervor = state.Faiths.Where(f => f.ExtinctWeek == 0)
            .ToDictionary(f => f.Id, f => Fervor(state.Faiths, f.Id, living, id => mainAtStart.GetValueOrDefault(id)).Value);

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
                $"{(founder is null ? "族人" : $"{DemoRepresentative.RoleName(founder.Role, tierOf?.Invoke(site.Id) ?? DemoSettlementTier.Village)}{founder.Name}")}創立「{faith.Name}」"
                + $"（信條：{string.Join("、", tenets.Select(EntryName))}）",
                EventId: nextEventId++, CauseEventId: e.EventId));
        }

        // 第 08-22d 章【TEMP】異端分裂（參考 CK3 的異端；使用者 2026-11-18 定案要加入）：信仰創立滿 TempSchismMinWeeks 週後，
        // 主要信仰的聖地遙遠（TempSchismDistance 格以上）或已成廢墟的聚落，每年一次以 TempSchismPercent% 另立新的信仰——
        // 信條與教義依自己的文化重抽，本聚落原本的信徒與代表人物全部改信新信仰。一週最多一個。
        foreach (var s in living.Where(s => week % TempSchismCheckWeeks == s.Id % TempSchismCheckWeeks))
        {
            if (Main(s.Id) is not { } fid || faiths.FirstOrDefault(x => x.Id == fid) is not { } old || old.FounderSettlementId == s.Id
                || week - old.FoundedWeek < TempSchismMinWeeks || faiths.Any(x => x.FounderSettlementId == s.Id && x.ExtinctWeek == 0))
                continue;
            var holy = sites.Settlements.FirstOrDefault(x => x.Id == old.FounderSettlementId);
            if (holy is not null && !holy.IsRuin && DemoKnowledge.Distance(map, s, holy) < TempSchismDistance)
                continue;
            // 第 08-22g 章：狂熱度低的信仰容易分裂（×(150−狂熱度)%）。
            if ((int)(Draw(registry, s.Id, 5000UL + (ulong)week) % 100UL) >= TempSchismPercent * (150 - fervor.GetValueOrDefault(old.Id, TempFervorBase)) / 100)
                continue;
            var id = faiths.Max(f => f.Id) + 1;
            var founder = representatives.Concat(report.Appointments).Where(r => r.SettlementId == s.Id)
                .OrderBy(r => r.Role).ThenBy(r => r.Id).FirstOrDefault();
            var (tenets, doctrines) = Draw(cultureTraits(s.Id), i => Draw(registry, id, 1000 + i));
            var heresy = new DemoFaith(id, $"{s.Name}教派", s.Id, founder?.Id ?? 0, week, tenets, doctrines, ParentId: old.Id); // 第 08-22g 章：記下母信仰
            faiths.Add(heresy);
            settlements[s.Id] = Of(s.Id).Select(x => x.CultureId == fid ? x with { CultureId = id } : x).ToList();
            foreach (var r in representatives.Concat(report.Appointments).Where(r => r.SettlementId == s.Id && reps.GetValueOrDefault(r.Id) == fid))
                reps[r.Id] = id;
            events.Add(new DemoSettlementEvent(week, report.WorldTime, s.Id, s.Name, DemoSettlementEventKind.FaithSchism, id,
                $"{(founder is null ? "族人" : $"{DemoRepresentative.RoleName(founder.Role, tierOf?.Invoke(s.Id) ?? DemoSettlementTier.Village)}{founder.Name}")}"
                + $"不再遵奉遠方聖地{holy?.Name ?? ""}的「{old.Name}」，另立異端「{heresy.Name}」（信條：{string.Join("、", tenets.Select(EntryName))}）",
                EventId: nextEventId++));
            break;
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
        // 第 08-22g 章【TEMP】改宗：上級的主要信仰和自己不同時，慢慢改信上級的信仰（含其他信仰的信徒）；主要信仰換掉時記「改宗」。
        if (liegeOf is not null)
            foreach (var s in living)
            {
                if (liegeOf(s.Id) is not { } liege || mains.GetValueOrDefault(liege) is not { } theirs || theirs == mains[s.Id]
                    || faiths.FirstOrDefault(f => f.Id == theirs) is not { } target)
                    continue;
                var ours = mains[s.Id] is { } m ? fervor.GetValueOrDefault(m, TempFervorBase) : 0;
                var rate = TempLiegeConvertRate * Math.Clamp(100 + fervor.GetValueOrDefault(theirs, TempFervorBase) - ours, 20, 200) / 100;
                var before = Of(s.Id);
                var after = ConvertAll(before, theirs, rate);
                if (ReferenceEquals(after, before))
                    continue;
                settlements[s.Id] = after;
                if (Main(s.Id) == theirs && mains[s.Id] is { } was)
                {
                    var lord = sites.Settlements.FirstOrDefault(x => x.Id == liege)?.Name ?? "";
                    events.Add(new DemoSettlementEvent(week, report.WorldTime, s.Id, s.Name, DemoSettlementEventKind.FaithConverted, theirs,
                        $"多數人改信上級{lord}的「{target.Name}」（原本信「{faiths.FirstOrDefault(f => f.Id == was)?.Name ?? ""}」）",
                        EventId: nextEventId++));
                }
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

    /// <summary>
    /// 第 08-22g 章：其他信仰（含無信仰）的人各有 <paramref name="rate"/>／10000 改信 <paramref name="faithId"/>
    /// （合計至少 1‰，從人數最多的一群扣）；沒有其他信仰的人或 rate ≤ 0 時回傳原本的清單。
    /// </summary>
    public static IReadOnlyList<DemoCultureShare> ConvertAll(IReadOnlyList<DemoCultureShare> shares, int faithId, int rate)
    {
        ArgumentNullException.ThrowIfNull(shares);
        var others = shares.Where(x => x.CultureId != faithId && x.Permille > 0).ToList();
        if (others.Count == 0 || rate <= 0)
            return shares;
        var moved = others.Select(x => (Share: x, Moved: (int)((long)x.Permille * rate / 10000))).ToList();
        if (moved.All(x => x.Moved == 0))
        {
            var largest = others.OrderByDescending(x => x.Permille).ThenBy(x => x.CultureId).First();
            moved = moved.Select(x => x.Share.CultureId == largest.CultureId ? (Share: x.Share, Moved: 1) : x).ToList();
        }
        var result = moved.Where(x => x.Share.Permille - x.Moved > 0).Select(x => x.Share with { Permille = x.Share.Permille - x.Moved }).ToList();
        result.Add(new DemoCultureShare(faithId, (shares.FirstOrDefault(x => x.CultureId == faithId)?.Permille ?? 0) + moved.Sum(x => x.Moved)));
        return result.OrderBy(x => x.CultureId).ToImmutableList();
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
