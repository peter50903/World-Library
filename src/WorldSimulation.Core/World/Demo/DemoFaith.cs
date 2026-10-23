using System.Collections.Immutable;
using System.Globalization;
using WorldSimulation.Core.Config;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-9 章 B3：一個信仰（08-8，48A 宗教是歷史事件）。核心信條抽自 P04A，制度教義抽自 P04B～P04J（06K 生成規則）。
/// 零信徒時不刪除，記滅絕週（<see cref="ExtinctWeek"/>＞0）。
/// </summary>
public sealed record DemoFaith(int Id, string Name, int FoundedWeek, int OriginSettlementId, long FounderId,
    ImmutableList<string> Tenets, ImmutableList<string> Doctrines, int ExtinctWeek = 0)
{
    public bool Extinct => ExtinctWeek > 0;

    public bool Equals(DemoFaith? other)
        => other is not null && Id == other.Id && Name == other.Name && FoundedWeek == other.FoundedWeek
        && OriginSettlementId == other.OriginSettlementId && FounderId == other.FounderId && ExtinctWeek == other.ExtinctWeek
        && Tenets.SequenceEqual(other.Tenets) && Doctrines.SequenceEqual(other.Doctrines);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Tenets.Count, Doctrines.Count);
}

/// <summary>某信仰（0＝無信仰）在一個聚落或一支隊伍背景人口中的千分比。</summary>
public sealed record DemoFaithShare(int FaithId, int Permille);

/// <summary>
/// 第 08-9 章 B3（08-8 第 4 節）：信仰的世界狀態——信仰清單、各聚落的信仰比例（含無信仰）、途中隊伍帶的比例、代表人物的信仰
/// （0＝無信仰）。開局人人無信仰。不可變。
/// </summary>
public sealed record DemoFaithState(
    ImmutableList<DemoFaith> Faiths,
    ImmutableSortedDictionary<int, ImmutableList<DemoFaithShare>> Settlements,
    ImmutableSortedDictionary<int, ImmutableList<DemoFaithShare>> Batches,
    ImmutableSortedDictionary<long, int> Representatives)
{
    public static DemoFaithState Empty { get; } = new(ImmutableList<DemoFaith>.Empty,
        ImmutableSortedDictionary<int, ImmutableList<DemoFaithShare>>.Empty,
        ImmutableSortedDictionary<int, ImmutableList<DemoFaithShare>>.Empty,
        ImmutableSortedDictionary<long, int>.Empty);

    public DemoFaith? Of(int faithId) => Faiths.FirstOrDefault(f => f.Id == faithId);

    /// <summary>聚落的信仰比例；沒有紀錄＝全部無信仰。</summary>
    public IReadOnlyList<DemoFaithShare> SharesOf(int settlementId)
        => Settlements.TryGetValue(settlementId, out var shares) ? shares : DemoFaiths.AllUnbelievers;

    public int PermilleOf(int settlementId, int faithId) => SharesOf(settlementId).FirstOrDefault(s => s.FaithId == faithId)?.Permille ?? 0;

    /// <summary>聚落信徒最多的信仰（不含無信仰）；沒有信徒回傳 null。</summary>
    public DemoFaith? MainFaithOf(int settlementId)
        => SharesOf(settlementId).Where(s => s.FaithId != 0).OrderByDescending(s => s.Permille).ThenBy(s => s.FaithId).FirstOrDefault() is { } top
            ? Of(top.FaithId) : null;

    /// <summary>代表人物的信仰（null＝無信仰或沒有紀錄）。</summary>
    public DemoFaith? FaithOfRepresentative(long representativeId)
        => Representatives.TryGetValue(representativeId, out var id) && id != 0 ? Of(id) : null;

    public string Fingerprint()
        => string.Concat(Faiths.Select(f => $"fa{f.Id}:{f.Name}@{f.FoundedWeek}/{f.OriginSettlementId}/{f.FounderId}x{f.ExtinctWeek}[{string.Join(",", f.Tenets)}|{string.Join(",", f.Doctrines)}];"))
        + string.Concat(Settlements.Select(kv => $"fs{kv.Key}={Shares(kv.Value)};"))
        + string.Concat(Batches.Select(kv => $"fb{kv.Key}={Shares(kv.Value)};"))
        + string.Concat(Representatives.Select(kv => $"fr{kv.Key}={kv.Value};"));

    private static string Shares(IEnumerable<DemoFaithShare> shares) => string.Join(",", shares.Select(s => $"{s.FaithId}:{s.Permille}"));

    public bool Equals(DemoFaithState? other) => other is not null && Fingerprint() == other.Fingerprint();

    public override int GetHashCode() => HashCode.Combine(Faiths.Count, Settlements.Count, Representatives.Count);
}

/// <summary>
/// 第 08-9 章 B3【TEMP 規則】信仰的創立、擴散與滅絕（08-8 第 3～4 節）。純函式；抽選用以信仰／代表人物編號為鍵的局部亂數。
/// <list type="bullet">
/// <item>聚落習得「祖靈祭祀」：勢力範圍內（<see cref="TempContactRange"/> 格）已有信徒的信仰時，接觸該信仰（不另創）；否則由村長創立新信仰，
/// 依 06K 抽核心信條（P04A 三池，合計 2～4 條）與制度教義（P04B～P04J，必抽池各一、選抽池 0～1），權重依主要文化加倍。</item>
/// <item>擴散：創立聚落每週有 2% 的無信仰者改信；勢力範圍內有信徒的聚落，鄰近聚落每週 0.5% 的無信仰者改信信徒最多的信仰；
/// 教義「積極傳教」加倍、「封閉信仰」減半。</item>
/// <item>背景人口只存千分比；隊伍帶出發地比例，抵達依人數加權；代表人物就任時依比例抽信仰（可無信仰）；創立者信仰新信仰。</item>
/// <item>某信仰所有聚落比例為 0、隊伍與代表人物也沒有信徒時進入滅絕（48A：不刪除）。</item>
/// </list>
/// </summary>
public static class DemoFaiths
{
    public static readonly DemoTechnology AncestorRites = new("TECH_ANCESTOR_RITES");

    /// <summary>【TEMP】接觸與鄰近擴散的範圍（格）。</summary>
    public const int TempContactRange = 3;

    /// <summary>【TEMP】創立聚落每週改信的無信仰者千分比（2%）。</summary>
    public const int TempFounderConversionPermille = 20;

    /// <summary>【TEMP】鄰近聚落每週改信的無信仰者千分比（0.5%）。</summary>
    public const int TempNeighbourConversionPermille = 5;

    public static readonly IReadOnlyList<DemoFaithShare> AllUnbelievers = ImmutableList.Create(new DemoFaithShare(0, 1000));

    private const string Namespace = "TEMP-FAITH-v1";
    private static readonly string[] DoctrineSheets =
    {
        "P04B_婚姻家庭教義池.csv", "P04C_性別繼承教義池.csv", "P04D_神職制度教義池.csv", "P04E_戰爭和平教義池.csv",
        "P04F_財富戒律教義池.csv", "P04G_死亡喪葬教義池.csv", "P04H_改宗異端教義池.csv", "P04I_魔法死靈教義池.csv",
        "P04J_奴隸身分教義池.csv"
    };

    private static readonly Lazy<(IReadOnlyList<DemoPoolEntry> Core, IReadOnlyList<DemoPoolEntry> Doctrines, IReadOnlyDictionary<string, string> Names)> Loaded = new(Load);

    public static IReadOnlyList<DemoPoolEntry> CorePool => Loaded.Value.Core;

    public static IReadOnlyList<DemoPoolEntry> DoctrinePool => Loaded.Value.Doctrines;

    public static string Name(string traitId) => Loaded.Value.Names.TryGetValue(traitId, out var name) ? name : traitId;

    public static bool IsKnownTenet(string id) => CorePool.Any(e => e.TraitId == id);

    public static bool IsKnownDoctrine(string id) => DoctrinePool.Any(e => e.TraitId == id);

    /// <summary>【TEMP】主要文化讓某些信條與教義權重加倍（06K WeightInputs 的 Culture）。</summary>
    private static readonly IReadOnlyDictionary<string, string[]> CultureFavours = new Dictionary<string, string[]>
    {
        ["CT_MARTIAL"] = new[] { "DOC_WAR", "DOC_HONOR", "RDW_WARRIOR_FAITH" },
        ["CT_PACIFIST"] = new[] { "DOC_PEACE", "DOC_MERCY", "RDW_PACIFIST" },
        ["CT_FOREST_FOLK"] = new[] { "DOC_NATURE", "DOC_SACRED_GROVE" },
        ["CT_ROOTED"] = new[] { "DOC_ANCESTOR", "RDD_ANCESTOR_TOMB" },
        ["CT_ANCESTRAL_GRAVES"] = new[] { "DOC_ANCESTOR", "RDD_ANCESTOR_TOMB", "RDD_BURIAL" },
        ["CT_SCHOLARLY"] = new[] { "DOC_KNOWLEDGE", "DOC_SCHOLASTIC" },
        ["CT_MERCHANT"] = new[] { "DOC_WEALTH", "RDF_PROSPERITY" },
        ["CT_FAMILY_EXTENDED"] = new[] { "DOC_FAMILY", "RDM_FAMILY_DUTY" },
        ["CT_OPEN"] = new[] { "RDX_PLURALIST", "DOC_HOSPITALITY" },
        ["CT_TRADITIONAL"] = new[] { "RDX_ISOLATIONIST", "DOC_ORDER" },
        ["CT_HONOR"] = new[] { "DOC_HONOR", "DOC_REVENGE" },
        ["CT_ASCETIC"] = new[] { "DOC_ASCETICISM", "RDF_ASCETIC" }
    };

    /// <summary>依 06K 抽一個新信仰的核心信條（合計 2～4）與制度教義。</summary>
    public static (ImmutableList<string> Tenets, ImmutableList<string> Doctrines) Draw(IReadOnlySet<string> cultureTraits, Func<ulong, ulong> draw)
    {
        var favoured = cultureTraits.SelectMany(t => CultureFavours.TryGetValue(t, out var list) ? list : Array.Empty<string>()).ToHashSet();
        long Weight(DemoPoolEntry e) => favoured.Contains(e.TraitId) ? e.Weight * 2 : e.Weight;
        var none = new HashSet<string>();
        var tenets = DemoTraitPools.Draw(CorePool, none, draw, Weight).ToList();
        // 06K CORE_TENETS 合計 2～4：超過的捨去後面的；不足的再從三池補抽（下標從 1000 起，與上面不重疊）。
        if (tenets.Count > 4)
            tenets = tenets.Take(4).ToList();
        ulong extra = 1000;
        while (tenets.Count < 2)
        {
            var eligible = CorePool.Where(e => !tenets.Contains(e.TraitId)
                && e.Required.All(c => DemoTraitPools.Holds(c, none, tenets)) && !e.Forbidden.Any(c => DemoTraitPools.Holds(c, none, tenets))
                && (e.ExclusiveGroup.Length == 0 || !CorePool.Any(x => x.ExclusiveGroup == e.ExclusiveGroup && tenets.Contains(x.TraitId))))
                .Select(e => (e, W: Weight(e))).Where(x => x.W > 0).ToList();
            if (eligible.Count == 0) break;
            var pick = (long)(draw(extra++) % (ulong)eligible.Sum(x => x.W));
            tenets.Add(eligible.First(x => (pick -= x.W) < 0).e.TraitId);
        }
        var doctrines = DemoTraitPools.Draw(DoctrinePool, none, i => draw(i + 2000), Weight, tenets);
        return (tenets.ToImmutableList(), doctrines);
    }

    /// <summary>擴散倍率（百分比）：積極傳教加倍、封閉信仰減半。</summary>
    public static long SpreadPercent(DemoFaith faith)
        => faith.Doctrines.Contains("RDX_MISSIONARY") ? 200 : faith.Doctrines.Contains("RDX_ISOLATIONIST") ? 50 : 100;

    /// <summary>
    /// 每週提交時更新信仰：本週習得祖靈祭祀的聚落創立或接觸信仰（創立事件接在 <paramref name="nextEventId"/>）；
    /// 改信擴散；隊伍帶走與併入；代表人物就任抽信仰、卸任移除；零信徒滅絕。
    /// </summary>
    public static (DemoFaithState State, IReadOnlyList<DemoSettlementEvent> Events) Advance(
        DemoFaithState state, DemoWeekReport report, IReadOnlyList<DemoSettlementEvent> learned, DemoWorldSites sites, DemoHexWorld map,
        DemoCultureState culture, IReadOnlyList<DemoRepresentative> inOffice, IReadOnlyCollection<int> batchesInTransit, RngRegistry rng, long nextEventId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(report);
        var faiths = state.Faiths.ToBuilder();
        var settlements = state.Settlements.ToBuilder();
        var batches = state.Batches.ToBuilder();
        var reps = state.Representatives.ToBuilder();
        var events = new List<DemoSettlementEvent>();
        var founders = new HashSet<long>();
        IReadOnlyList<DemoFaithShare> Shares(int id) => settlements.TryGetValue(id, out var s) ? s : AllUnbelievers;
        int Believers(int settlementId, int faithId) => Shares(settlementId).FirstOrDefault(x => x.FaithId == faithId)?.Permille ?? 0;

        // ① 習得祖靈祭祀：接觸或創立。
        foreach (var e in learned.Where(e => e.Kind == DemoSettlementEventKind.KnowledgeLearned && DemoTechTree.ByIndex(e.Count) == AncestorRites)
                     .OrderBy(e => e.EventId))
        {
            var place = sites.Settlements.First(s => s.Id == e.SettlementId);
            var nearby = faiths.Where(f => !f.Extinct && sites.Settlements.Any(o => !o.IsRuin && Believers(o.Id, f.Id) > 0
                && DemoKnowledge.HexDistance(map, place, o) <= TempContactRange)).Select(f => f.Id).ToList();
            if (nearby.Count > 0)
                continue; // 接觸既有信仰：之後依鄰近擴散改信，不另創
            var founder = inOffice.Where(r => r.SettlementId == place.Id && r.Role == DemoRepresentativeRole.Leader).Select(r => r.Id).FirstOrDefault();
            var id = faiths.Count + 1;
            var (tenets, doctrines) = Draw(DemoCultures.MainTraits(culture, place.Id),
                index => EntityLocalRandom.Create(rng, EntityId.From((ulong)id), RngStreams.Religion, Namespace).GetUInt64(index));
            var faith = new DemoFaith(id, $"{place.Name}信仰", report.Week, place.Id, founder, tenets, doctrines);
            faiths.Add(faith);
            if (founder != 0)
            {
                reps[founder] = id;
                founders.Add(founder);
            }
            events.Add(new DemoSettlementEvent(report.Week, report.WorldTime, place.Id, place.Name, DemoSettlementEventKind.FaithFounded, id,
                $"創立「{faith.Name}」：{(founder == 0 ? "聚落" : "村長")}以祖靈祭祀凝聚眾人，核心信條{string.Join("、", tenets.Select(Name))}【TEMP】",
                EventId: nextEventId++, CauseEventId: e.EventId));
        }

        // ② 改信擴散（只轉無信仰者）：創立聚落 2%；勢力範圍內有信徒的鄰近聚落 0.5%。
        var live = faiths.Where(f => !f.Extinct).ToList();
        foreach (var place in sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id))
        {
            var shares = Shares(place.Id);
            var unbelievers = shares.FirstOrDefault(x => x.FaithId == 0)?.Permille ?? 0;
            if (unbelievers == 0)
                continue;
            DemoFaith? target = null;
            var rate = 0L;
            // 創立聚落：本週剛創立，或仍有信徒時才繼續改信（信徒全數消失後不會自己復燃）。
            if (live.FirstOrDefault(f => f.OriginSettlementId == place.Id && (f.FoundedWeek == report.Week || Believers(place.Id, f.Id) > 0)) is { } own)
                (target, rate) = (own, TempFounderConversionPermille);
            else
            {
                var source = sites.Settlements.Where(o => o.Id != place.Id && !o.IsRuin && DemoKnowledge.HexDistance(map, place, o) <= TempContactRange)
                    .SelectMany(o => Shares(o.Id).Where(x => x.FaithId != 0 && live.Any(f => f.Id == x.FaithId)))
                    .GroupBy(x => x.FaithId).Select(g => (Id: g.Key, P: g.Sum(x => x.Permille))).OrderByDescending(x => x.P).ThenBy(x => x.Id)
                    .Select(x => (int?)x.Id).FirstOrDefault();
                if (source is { } sid && live.First(f => f.Id == sid) is var near)
                    (target, rate) = (near, TempNeighbourConversionPermille);
            }
            if (target is null)
                continue;
            var moved = (int)Math.Min(unbelievers, Math.Max(1, (long)unbelievers * rate * SpreadPercent(target) / 100 / 1000));
            var next = shares.Where(x => x.FaithId != 0 && x.FaithId != target.Id).ToList();
            next.Add(new DemoFaithShare(target.Id, Believers(place.Id, target.Id) + moved));
            if (unbelievers - moved > 0)
                next.Add(new DemoFaithShare(0, unbelievers - moved));
            settlements[place.Id] = next.OrderBy(x => x.FaithId).ToImmutableList();
        }

        // ③ 隊伍：出發記比例；抵達依人數加權；不在途中的移除。
        foreach (var m in report.Migrations.Where(m => m.Departed).OrderBy(m => m.BatchId))
            batches[m.BatchId] = Shares(m.FromSettlementId).ToImmutableList();
        foreach (var m in report.Migrations.Where(m => m.Arrived && m.PopulationClosing > 0).OrderBy(m => m.BatchId))
        {
            if (!batches.TryGetValue(m.BatchId, out var carried))
                continue;
            var ledger = report.Settlements.FirstOrDefault(l => l.SettlementId == m.ToSettlementId);
            var resident = ledger is null ? 0 : Math.Max(0, ledger.PopulationClosing - m.PopulationClosing);
            settlements[m.ToSettlementId] = m.Founding || resident == 0 ? carried
                : Blend(Shares(m.ToSettlementId), resident, carried, m.PopulationClosing);
        }
        foreach (var id in batches.Keys.Where(id => !batchesInTransit.Contains(id)).ToList())
            batches.Remove(id);

        // ④ 代表人物：卸任移除；就任依比例抽（0＝無信仰）。
        foreach (var exit in report.RepresentativeExits)
            reps.Remove(exit.Id);
        foreach (var r in report.Appointments.Where(r => !founders.Contains(r.Id)).OrderBy(r => r.Id))
        {
            var shares = Shares(r.SettlementId);
            var pick = (long)(EntityLocalRandom.Create(rng, EntityId.From((ulong)r.Id), RngStreams.Religion, Namespace).GetUInt64(0) % 1000UL);
            reps[r.Id] = shares.First(x => (pick -= x.Permille) < 0 || x == shares[^1]).FaithId;
        }

        // ⑤ 零信徒滅絕（48A）。
        for (var i = 0; i < faiths.Count; i++)
        {
            var f = faiths[i];
            // 遺跡的人口已歸零，不算信徒。
            if (f.Extinct || settlements.Any(kv => sites.Settlements.Any(x => x.Id == kv.Key && !x.IsRuin) && kv.Value.Any(x => x.FaithId == f.Id))
                || batches.Values.Any(l => l.Any(x => x.FaithId == f.Id))
                || reps.Values.Contains(f.Id))
                continue;
            faiths[i] = f with { ExtinctWeek = report.Week };
            var origin = sites.Settlements.First(s => s.Id == f.OriginSettlementId);
            events.Add(new DemoSettlementEvent(report.Week, report.WorldTime, origin.Id, origin.Name, DemoSettlementEventKind.FaithExtinct, f.Id,
                $"「{f.Name}」最後的信徒消失，信仰滅絕（不刪除，可復興）【TEMP】", EventId: nextEventId++));
        }
        return (new DemoFaithState(faiths.ToImmutable(), settlements.ToImmutable(), batches.ToImmutable(), reps.ToImmutable()), events);
    }

    /// <summary>依人數加權合併兩組比例（最大餘數法補足 1000，同餘數取編號小者）。</summary>
    public static ImmutableList<DemoFaithShare> Blend(IReadOnlyList<DemoFaithShare> a, long aPeople, IReadOnlyList<DemoFaithShare> b, long bPeople)
    {
        var total = checked(aPeople + bPeople);
        var weights = a.Select(s => (s.FaithId, W: checked(s.Permille * aPeople))).Concat(b.Select(s => (s.FaithId, W: checked(s.Permille * bPeople))))
            .GroupBy(x => x.FaithId).Select(g => (Id: g.Key, W: g.Sum(x => x.W))).OrderBy(x => x.Id).ToList();
        var floors = weights.Select(x => (x.Id, P: x.W / total, R: x.W % total)).ToList();
        var bonus = floors.OrderByDescending(x => x.R).ThenBy(x => x.Id).Take((int)(1000 - floors.Sum(x => x.P))).Select(x => x.Id).ToHashSet();
        return floors.Select(x => new DemoFaithShare(x.Id, (int)(x.P + (bonus.Contains(x.Id) ? 1 : 0)))).Where(x => x.Permille > 0).ToImmutableList();
    }

    /// <summary>點格詳情：「信仰：甲信仰 12%、無信仰 88%」。</summary>
    public static string SharesLine(DemoFaithState state, int settlementId)
        => "信仰：" + string.Join("、", state.SharesOf(settlementId).OrderByDescending(s => s.Permille).ThenBy(s => s.FaithId)
            .Select(s => $"{(s.FaithId == 0 ? "無信仰" : state.Of(s.FaithId)?.Name ?? "?")} {(s.Permille / 10.0).ToString("0.#", CultureInfo.InvariantCulture)}%"));

    public static string TenetsLine(DemoFaith faith)
        => $"{faith.Name}：核心信條 {string.Join("、", faith.Tenets.Select(Name))}｜教義 {string.Join("、", faith.Doctrines.Select(Name))}"
            + (faith.Extinct ? "（已滅絕）" : "");

    private static (IReadOnlyList<DemoPoolEntry>, IReadOnlyList<DemoPoolEntry>, IReadOnlyDictionary<string, string>) Load()
    {
        var defs = ParameterSheet.ParseCsv(OfficialConfig.ReadSheetText("06A_宗教核心信條定義.csv"));
        var h = defs[0].Select(x => x.Trim()).ToList();
        var (id, name) = (h.IndexOf("DoctrineTraitID"), h.IndexOf("中文名稱"));
        var names = defs.Skip(1).Where(r => r.Count > name && r[id].Trim().Length > 0).ToDictionary(r => r[id].Trim(), r => r[name].Trim());
        var core = DemoTraitPools.Parse(OfficialConfig.ReadSheetText("P04A_宗教核心信條池.csv"), "P04A", names.ContainsKey);
        var doctrines = DoctrineSheets.SelectMany(sheet => DemoTraitPools.Parse(OfficialConfig.ReadSheetText(sheet), sheet)).ToList();
        foreach (var d in doctrines)
            names.TryAdd(d.TraitId, d.Name);
        return (core, doctrines, names);
    }
}
