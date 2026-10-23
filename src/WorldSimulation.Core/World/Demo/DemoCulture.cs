using System.Collections.Immutable;
using System.Globalization;
using WorldSimulation.Core.Config;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-9 章 B1：一個文化（08-8）。特性是正式設定 P02 抽出的 CultureTraitID；分化出的子文化記上層文化（B4）。</summary>
public sealed record DemoCulture(int Id, string Name, int FoundedWeek, int OriginSettlementId, ImmutableList<string> Traits, int ParentId = 0)
{
    public bool Equals(DemoCulture? other)
        => other is not null && Id == other.Id && Name == other.Name && FoundedWeek == other.FoundedWeek
        && OriginSettlementId == other.OriginSettlementId && ParentId == other.ParentId && Traits.SequenceEqual(other.Traits);

    public override int GetHashCode() => HashCode.Combine(Id, Name, Traits.Count);
}

/// <summary>某文化在一個聚落（或一支隊伍）背景人口中的千分比。</summary>
public sealed record DemoCultureShare(int CultureId, int Permille);

/// <summary>
/// 第 08-9 章 B1（08-8 第 4 節，使用者 2026-10-23：只有重要人物逐人記錄）：文化的世界狀態——
/// 文化清單、各聚落的文化比例（千分比）、途中隊伍帶的比例、代表人物的文化。不可變。
/// </summary>
public sealed record DemoCultureState(
    ImmutableList<DemoCulture> Cultures,
    ImmutableSortedDictionary<int, ImmutableList<DemoCultureShare>> Settlements,
    ImmutableSortedDictionary<int, ImmutableList<DemoCultureShare>> Batches,
    ImmutableSortedDictionary<long, int> Representatives)
{
    public static DemoCultureState Empty { get; } = new(ImmutableList<DemoCulture>.Empty,
        ImmutableSortedDictionary<int, ImmutableList<DemoCultureShare>>.Empty,
        ImmutableSortedDictionary<int, ImmutableList<DemoCultureShare>>.Empty,
        ImmutableSortedDictionary<long, int>.Empty);

    public DemoCulture? Of(int cultureId) => Cultures.FirstOrDefault(c => c.Id == cultureId);

    public IReadOnlyList<DemoCultureShare> SharesOf(int settlementId)
        => Settlements.TryGetValue(settlementId, out var shares) ? shares : Array.Empty<DemoCultureShare>();

    /// <summary>勢力的文化＝比例最高的文化（同比例取編號小者）；沒有比例回傳 null。</summary>
    public DemoCulture? MainCultureOf(int settlementId)
        => SharesOf(settlementId).OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId).FirstOrDefault() is { } top ? Of(top.CultureId) : null;

    public DemoCulture? CultureOfRepresentative(long representativeId)
        => Representatives.TryGetValue(representativeId, out var id) ? Of(id) : null;

    public string Fingerprint()
        => string.Concat(Cultures.Select(c => $"cul{c.Id}:{c.Name}@{c.FoundedWeek}/{c.OriginSettlementId}<{c.ParentId}[{string.Join(",", c.Traits)}];"))
        + string.Concat(Settlements.Select(kv => $"cs{kv.Key}={Shares(kv.Value)};"))
        + string.Concat(Batches.Select(kv => $"cb{kv.Key}={Shares(kv.Value)};"))
        + string.Concat(Representatives.Select(kv => $"cr{kv.Key}={kv.Value};"));

    private static string Shares(IEnumerable<DemoCultureShare> shares) => string.Join(",", shares.Select(s => $"{s.CultureId}:{s.Permille}"));

    public bool Equals(DemoCultureState? other) => other is not null && Fingerprint() == other.Fingerprint();

    public override int GetHashCode() => HashCode.Combine(Cultures.Count, Settlements.Count, Representatives.Count);
}

/// <summary>正式設定 P02 的一列（文化特性池）。</summary>
public sealed record DemoCulturePoolEntry(string PoolId, string TraitId, string Name, long Weight, int DrawMin, int DrawMax,
    IReadOnlyList<string> Required, IReadOnlyList<string> Forbidden, string ExclusiveGroup);

/// <summary>
/// 第 08-9 章 B1【TEMP 規則】文化的生成與背景人口比例（08-8 第 3～4 節）。純函式；抽選用以文化／代表人物編號為鍵的
/// 局部亂數（不推進任何亂數流的計數器）。
/// <list type="bullet">
/// <item>開局：開局聚落依婚嫁範圍（<see cref="TempClusterRange"/> 格）連成群，每群一個文化，依 P02 各池的抽取上下限、權重、互斥組抽特性；
/// 條件（RequiredConditions）以群內任一聚落的地形判定：NEAR_FOREST、NEAR_GRASSLAND、NEAR_RIVER、NEAR_SEA；開局群另有 OPENING。</item>
/// <item>背景人口只存千分比；出生死亡不改比例；隊伍帶出發地比例，抵達後依人數加權併入；新建聚落沿用隊伍的比例。</item>
/// <item>代表人物就任時依聚落比例抽一個文化。</item>
/// </list>
/// </summary>
public static class DemoCultures
{
    /// <summary>【TEMP】開局文化群的範圍：婚嫁範圍 12 格。</summary>
    public const int TempClusterRange = 12;

    public const string PoolSheet = "P02_文化特性池.csv";
    public const string TraitSheet = "04_文化特性定義.csv";
    private const string Namespace = "TEMP-CULTURE-v1";

    private static readonly Lazy<(IReadOnlyList<DemoCulturePoolEntry> Pool, IReadOnlyDictionary<string, string> Names)> Loaded = new(Load);

    /// <summary>P02 全部啟用的列（依設定檔順序）。</summary>
    public static IReadOnlyList<DemoCulturePoolEntry> Pool => Loaded.Value.Pool;

    /// <summary>文化特性中文名稱（04）。</summary>
    public static string TraitName(string traitId) => Loaded.Value.Names.TryGetValue(traitId, out var name) ? name : traitId;

    public static bool IsKnownTrait(string traitId) => Pool.Any(e => e.TraitId == traitId);

    /// <summary>世界建立時的開局文化（人類繁衍世界）。</summary>
    public static DemoCultureState Opening(DemoHexWorld map, DemoWorldSites sites, RngRegistry rng)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(rng);
        var settlements = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        var group = settlements.ToDictionary(s => s.Id, s => s.Id);
        int Root(int id) { while (group[id] != id) id = group[id] = group[group[id]]; return id; }
        foreach (var a in settlements)
            foreach (var b in settlements.Where(b => b.Id > a.Id && DemoKnowledge.HexDistance(map, a, b) <= TempClusterRange))
            {
                var (x, y) = (Root(a.Id), Root(b.Id));
                if (x != y) group[Math.Max(x, y)] = Math.Min(x, y);
            }
        var cultures = ImmutableList.CreateBuilder<DemoCulture>();
        var shares = ImmutableSortedDictionary.CreateBuilder<int, ImmutableList<DemoCultureShare>>();
        foreach (var cluster in settlements.GroupBy(s => Root(s.Id)).OrderBy(g => g.Key))
        {
            var id = cultures.Count + 1;
            var origin = cluster.First();
            var conditions = cluster.SelectMany(s => ConditionsOf(map, s)).Append("OPENING").ToHashSet();
            var traits = DrawTraits(conditions, index => EntityLocalRandom.Create(rng, EntityId.From((ulong)id), RngStreams.EcologyInit, Namespace).GetUInt64(index));
            cultures.Add(new DemoCulture(id, $"{origin.Name}文化", 0, origin.Id, traits));
            foreach (var s in cluster)
                shares[s.Id] = ImmutableList.Create(new DemoCultureShare(id, 1000));
        }
        return new DemoCultureState(cultures.ToImmutable(), shares.ToImmutable(),
            ImmutableSortedDictionary<int, ImmutableList<DemoCultureShare>>.Empty, ImmutableSortedDictionary<long, int>.Empty);
    }

    /// <summary>地形條件（P02 RequiredConditions 用）。</summary>
    public static IEnumerable<string> ConditionsOf(DemoHexWorld map, DemoSettlement settlement)
    {
        if (DemoKnowledge.NearTerrain(map, settlement, DemoTerrain.Forest)) yield return "NEAR_FOREST";
        if (DemoKnowledge.NearTerrain(map, settlement, DemoTerrain.Grassland)) yield return "NEAR_GRASSLAND";
        if (DemoKnowledge.NearSea(map, settlement)) yield return "NEAR_SEA";
        var origin = map.GetCell(settlement.Column, settlement.Row);
        if (DemoRouteNetwork.Directions.Select(d => map.AtAxial(origin.AxialQ + d.Dq, origin.AxialR + d.Dr))
                .Any(c => c is { Terrain: DemoTerrain.Water, Sea: false }))
            yield return "NEAR_RIVER";
        if (settlement.IsFounded) yield return "FOUNDED";
    }

    /// <summary>
    /// 依 P02 抽特性：每個池依出現順序，抽 DrawMin～DrawMax 個（亂數決定個數），權重不放回，抽到後同互斥組的其他列不再抽；
    /// RequiredConditions 全部成立、ForbiddenConditions 都不成立的列才可抽。
    /// </summary>
    public static ImmutableList<string> DrawTraits(IReadOnlySet<string> conditions, Func<ulong, ulong> draw)
    {
        var result = ImmutableList.CreateBuilder<string>();
        var taken = new HashSet<string>();
        ulong index = 0;
        foreach (var pool in Pool.GroupBy(e => e.PoolId))
        {
            var entries = pool.ToList();
            var (min, max) = (entries.Min(e => e.DrawMin), entries.Max(e => e.DrawMax));
            var count = min + (int)(draw(index++) % (ulong)(max - min + 1));
            var eligible = entries.Where(e => e.Weight > 0 && e.Required.All(conditions.Contains) && !e.Forbidden.Any(conditions.Contains)
                && (e.ExclusiveGroup.Length == 0 || !taken.Contains(e.ExclusiveGroup))).ToList();
            for (var i = 0; i < count && eligible.Count > 0; i++)
            {
                var total = eligible.Sum(e => e.Weight);
                var pick = (long)(draw(index++) % (ulong)total);
                var chosen = eligible.First(e => (pick -= e.Weight) < 0);
                result.Add(chosen.TraitId);
                if (chosen.ExclusiveGroup.Length > 0)
                    taken.Add(chosen.ExclusiveGroup);
                eligible = eligible.Where(e => e != chosen && (e.ExclusiveGroup.Length == 0 || !taken.Contains(e.ExclusiveGroup))).ToList();
            }
        }
        return result.ToImmutable();
    }

    /// <summary>
    /// 每週提交時更新背景人口比例與代表人物文化：出發隊伍記出發地比例；抵達依人數加權併入；建立聚落沿用隊伍比例；
    /// 就任的代表人物依聚落比例抽文化、卸任移除；已不在途中的隊伍移除。
    /// </summary>
    public static DemoCultureState Advance(DemoCultureState state, DemoWeekReport report, IReadOnlyCollection<int> batchesInTransit, RngRegistry rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(report);
        if (state.Cultures.IsEmpty)
            return state;
        var settlements = state.Settlements.ToBuilder();
        var batches = state.Batches.ToBuilder();
        foreach (var m in report.Migrations.Where(m => m.Departed).OrderBy(m => m.BatchId))
            if (settlements.TryGetValue(m.FromSettlementId, out var origin))
                batches[m.BatchId] = origin;
        foreach (var m in report.Migrations.Where(m => m.Arrived && m.PopulationClosing > 0).OrderBy(m => m.BatchId))
        {
            if (!batches.TryGetValue(m.BatchId, out var carried))
                continue;
            var ledger = report.Settlements.FirstOrDefault(l => l.SettlementId == m.ToSettlementId);
            var before = settlements.TryGetValue(m.ToSettlementId, out var existing) ? existing : ImmutableList<DemoCultureShare>.Empty;
            var resident = ledger is null ? 0 : Math.Max(0, ledger.PopulationClosing - m.PopulationClosing);
            settlements[m.ToSettlementId] = before.IsEmpty || m.Founding || resident == 0
                ? carried
                : Blend(before, resident, carried, m.PopulationClosing);
        }
        foreach (var id in batches.Keys.Where(id => !batchesInTransit.Contains(id)).ToList())
            batches.Remove(id);

        var reps = state.Representatives.ToBuilder();
        foreach (var exit in report.RepresentativeExits)
            reps.Remove(exit.Id);
        foreach (var r in report.Appointments.OrderBy(r => r.Id))
        {
            var shares = settlements.TryGetValue(r.SettlementId, out var s) ? s : ImmutableList<DemoCultureShare>.Empty;
            if (shares.IsEmpty)
                continue;
            var pick = (long)(EntityLocalRandom.Create(rng, EntityId.From((ulong)r.Id), RngStreams.Character, Namespace).GetUInt64(0) % 1000UL);
            var chosen = shares.First(x => (pick -= x.Permille) < 0 || x == shares[^1]);
            reps[r.Id] = chosen.CultureId;
        }
        return state with { Settlements = settlements.ToImmutable(), Batches = batches.ToImmutable(), Representatives = reps.ToImmutable() };
    }

    /// <summary>依人數加權合併兩組比例，取整後以最大餘數法補足 1000（同餘數取文化編號小者）。</summary>
    public static ImmutableList<DemoCultureShare> Blend(IReadOnlyList<DemoCultureShare> a, long aPeople, IReadOnlyList<DemoCultureShare> b, long bPeople)
    {
        var total = checked(aPeople + bPeople);
        var weights = a.Select(s => (s.CultureId, W: checked(s.Permille * aPeople))).Concat(b.Select(s => (s.CultureId, W: checked(s.Permille * bPeople))))
            .GroupBy(x => x.CultureId).Select(g => (Id: g.Key, W: g.Sum(x => x.W))).OrderBy(x => x.Id).ToList();
        var floors = weights.Select(x => (x.Id, P: x.W / total, R: x.W % total)).ToList();
        var missing = 1000 - floors.Sum(x => x.P);
        var bonus = floors.OrderByDescending(x => x.R).ThenBy(x => x.Id).Take((int)missing).Select(x => x.Id).ToHashSet();
        return floors.Select(x => new DemoCultureShare(x.Id, (int)(x.P + (bonus.Contains(x.Id) ? 1 : 0))))
            .Where(x => x.Permille > 0).ToImmutableList();
    }

    /// <summary>點格詳情用：「文化：甲文化 70%、乙文化 30%」。</summary>
    public static string SharesLine(DemoCultureState state, int settlementId)
        => "文化：" + string.Join("、", state.SharesOf(settlementId).OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId)
            .Select(s => $"{state.Of(s.CultureId)?.Name ?? "?"} {(s.Permille / 10.0).ToString("0.#", CultureInfo.InvariantCulture)}%"));

    /// <summary>文化特性一行：「甲文化：小家庭、尚武、林地子民…」。</summary>
    public static string TraitsLine(DemoCulture culture)
        => $"{culture.Name}的特性：{string.Join("、", culture.Traits.Select(TraitName))}";

    private static (IReadOnlyList<DemoCulturePoolEntry>, IReadOnlyDictionary<string, string>) Load()
        => Build(OfficialConfig.ReadSheetText(PoolSheet), OfficialConfig.ReadSheetText(TraitSheet));

    internal static (IReadOnlyList<DemoCulturePoolEntry> Pool, IReadOnlyDictionary<string, string> Names) Build(string poolText, string traitText)
    {
        var traitRows = ParameterSheet.ParseCsv(traitText);
        var th = traitRows[0].Select(h => h.Trim()).ToList();
        var (tid, tname) = (th.IndexOf("CultureTraitID"), th.IndexOf("中文名稱"));
        var names = traitRows.Skip(1).Where(r => r.Count > tname && r[tid].Trim().Length > 0)
            .ToDictionary(r => r[tid].Trim(), r => r[tname].Trim());
        var rows = ParameterSheet.ParseCsv(poolText);
        var h = rows[0].Select(x => x.Trim()).ToList();
        int C(string name) => h.IndexOf(name) is var i && i >= 0 ? i : throw new InvalidOperationException($"文化特性池缺少欄位「{name}」。");
        var (pool, entry, entryName, weight, min, max, req, forb, excl, enabled) = (C("PoolID"), C("EntryID"), C("EntryName"), C("Weight"),
            C("PoolDrawMin"), C("PoolDrawMax"), C("RequiredConditions"), C("ForbiddenConditions"), C("ExclusiveGroup"), C("Enabled"));
        var list = new List<DemoCulturePoolEntry>();
        foreach (var r in rows.Skip(1))
        {
            string Cell(int i) => i < r.Count ? r[i].Trim() : string.Empty;
            if (Cell(entry).Length == 0 || !string.Equals(Cell(enabled), "TRUE", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!names.ContainsKey(Cell(entry)))
                throw new InvalidOperationException($"文化特性池的 {Cell(entry)} 不在 04_文化特性定義。");
            static IReadOnlyList<string> Split(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!long.TryParse(Cell(weight), NumberStyles.None, CultureInfo.InvariantCulture, out var w)
                || !int.TryParse(Cell(min), NumberStyles.None, CultureInfo.InvariantCulture, out var lo)
                || !int.TryParse(Cell(max), NumberStyles.None, CultureInfo.InvariantCulture, out var hi) || hi < lo)
                throw new InvalidOperationException($"文化特性池的 {Cell(entry)}：權重與抽取上下限須為整數且上限不小於下限。");
            list.Add(new DemoCulturePoolEntry(Cell(pool), Cell(entry), Cell(entryName), w, lo, hi, Split(Cell(req)), Split(Cell(forb)), Cell(excl)));
        }
        return (list, names);
    }
}
