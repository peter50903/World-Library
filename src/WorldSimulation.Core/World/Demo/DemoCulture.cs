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

    /// <summary>第 08-9 章 B4：聚落與同文化本群分開的起始週（分化計時；重新相連就移除）。</summary>
    public ImmutableSortedDictionary<int, int> SeparatedSince { get; init; } = ImmutableSortedDictionary<int, int>.Empty;

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
        + string.Concat(Representatives.Select(kv => $"cr{kv.Key}={kv.Value};"))
        + string.Concat(SeparatedSince.Select(kv => $"csep{kv.Key}@{kv.Value};"));

    private static string Shares(IEnumerable<DemoCultureShare> shares) => string.Join(",", shares.Select(s => $"{s.CultureId}:{s.Permille}"));

    public bool Equals(DemoCultureState? other) => other is not null && Fingerprint() == other.Fingerprint();

    public override int GetHashCode() => HashCode.Combine(Cultures.Count, Settlements.Count, Representatives.Count);
}

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

    private static readonly Lazy<(IReadOnlyList<DemoPoolEntry> Pool, IReadOnlyDictionary<string, string> Names)> Loaded = new(Load);

    /// <summary>P02 全部啟用的列（依設定檔順序）。</summary>
    public static IReadOnlyList<DemoPoolEntry> Pool => Loaded.Value.Pool;

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
        => DemoTraitPools.Draw(Pool, conditions, draw);

    /// <summary>
    /// 每週提交時更新背景人口比例與代表人物文化：出發隊伍記出發地比例；抵達依人數加權併入；建立聚落沿用隊伍比例；
    /// 就任的代表人物依聚落比例抽文化、卸任移除；已不在途中的隊伍移除。
    /// </summary>
    public static DemoCultureState Advance(DemoCultureState state, DemoWeekReport report, IReadOnlyCollection<int> batchesInTransit, RngRegistry rng)
        => Advance(state, report, batchesInTransit, rng, null, null).State;

    /// <summary>【TEMP】第 08-9 章 B4：每年少數文化有 2%（至少千分之 1）轉為聚落主要文化（48C：不瞬間同化）。</summary>
    public const int TempAssimilationPercentPerYear = 2;

    /// <summary>【TEMP】第 08-9 章 B4：與同文化本群分開（婚嫁範圍外）滿 50 年就分化出子文化。</summary>
    public const int TempSplitWeeks = 50 * 52;

    /// <summary>
    /// 同上，另做 B4 的每年同化與分化（<paramref name="sites"/>、<paramref name="map"/> 為 null 時略過）；分化事件編號從 <paramref name="nextEventId"/> 起。
    /// </summary>
    public static (DemoCultureState State, IReadOnlyList<DemoSettlementEvent> Events) Advance(DemoCultureState state, DemoWeekReport report,
        IReadOnlyCollection<int> batchesInTransit, RngRegistry rng, DemoWorldSites? sites, DemoHexWorld? map, long nextEventId = 0)
    {
        var moved = Move(state, report, batchesInTransit, rng);
        if (sites is null || map is null || moved.Cultures.IsEmpty || report.Week % 52 != 0)
            return (moved, Array.Empty<DemoSettlementEvent>());
        return Yearly(Assimilate(moved, sites), report, sites, map, rng, nextEventId);
    }

    private static DemoCultureState Move(DemoCultureState state, DemoWeekReport report, IReadOnlyCollection<int> batchesInTransit, RngRegistry rng)
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

    /// <summary>每年：有人聚落裡的少數文化各有 2%（至少千分之 1）轉為主要文化。</summary>
    public static DemoCultureState Assimilate(DemoCultureState state, DemoWorldSites sites)
    {
        var settlements = state.Settlements.ToBuilder();
        foreach (var (id, shares) in state.Settlements)
        {
            if (shares.Count < 2 || sites.Settlements.FirstOrDefault(s => s.Id == id) is not { IsRuin: false })
                continue;
            var main = shares.OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId).First();
            var gained = 0;
            var next = new List<DemoCultureShare>();
            foreach (var s in shares.Where(s => s.CultureId != main.CultureId))
            {
                var loss = Math.Min(s.Permille, Math.Max(1, s.Permille * TempAssimilationPercentPerYear / 100));
                gained += loss;
                if (s.Permille - loss > 0)
                    next.Add(s with { Permille = s.Permille - loss });
            }
            next.Add(main with { Permille = main.Permille + gained });
            settlements[id] = next.OrderBy(s => s.CultureId).ToImmutableList();
        }
        return state with { Settlements = settlements.ToImmutable() };
    }

    /// <summary>
    /// 每年：以婚嫁範圍連通的同文化聚落（主要文化相同的有人聚落）為一群；不含發源聚落的群記分開起始週，滿 50 年分化出子文化
    /// （沿用上層文化的特性，換掉一個：同一池重抽），該群的這個文化比例改為子文化。
    /// </summary>
    private static (DemoCultureState, IReadOnlyList<DemoSettlementEvent>) Yearly(DemoCultureState state, DemoWeekReport report,
        DemoWorldSites sites, DemoHexWorld map, RngRegistry rng, long nextEventId)
    {
        var cultures = state.Cultures.ToBuilder();
        var settlements = state.Settlements.ToBuilder();
        var separated = state.SeparatedSince.ToBuilder();
        var events = new List<DemoSettlementEvent>();
        var inhabited = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        foreach (var culture in state.Cultures)
        {
            var members = inhabited.Where(s => state.MainCultureOf(s.Id)?.Id == culture.Id).ToList();
            if (members.Count == 0)
                continue;
            var groups = new List<List<DemoSettlement>>();
            var left = members.ToList();
            while (left.Count > 0)
            {
                var group = new List<DemoSettlement> { left[0] };
                left.RemoveAt(0);
                for (var i = 0; i < group.Count; i++)
                {
                    var near = left.Where(o => DemoKnowledge.HexDistance(map, group[i], o) <= TempClusterRange).ToList();
                    group.AddRange(near);
                    left.RemoveAll(near.Contains);
                }
                groups.Add(group);
            }
            var home = groups.FirstOrDefault(g => g.Any(s => s.Id == culture.OriginSettlementId)) ?? groups.OrderBy(g => g.Min(s => s.Id)).First();
            foreach (var s in home)
                separated.Remove(s.Id);
            foreach (var group in groups.Where(g => g != home))
            {
                foreach (var s in group)
                    if (!separated.ContainsKey(s.Id))
                        separated[s.Id] = report.Week;
                if (group.Any(s => report.Week - separated[s.Id] < TempSplitWeeks))
                    continue;
                var id = cultures.Count + 1;
                var origin = group.OrderBy(s => s.Id).First();
                var traits = Mutate(culture.Traits, index => EntityLocalRandom.Create(rng, EntityId.From((ulong)id), RngStreams.Character, Namespace).GetUInt64(index));
                var child = new DemoCulture(id, $"{origin.Name}文化", report.Week, origin.Id, traits, culture.Id);
                cultures.Add(child);
                foreach (var s in group)
                {
                    separated.Remove(s.Id);
                    settlements[s.Id] = settlements[s.Id].Select(x => x.CultureId == culture.Id ? x with { CultureId = id } : x).ToImmutableList();
                }
                events.Add(new DemoSettlementEvent(report.Week, report.WorldTime, origin.Id, origin.Name, DemoSettlementEventKind.CultureSplit, id,
                    $"與{culture.Name}的本群分開 50 年，分化出「{child.Name}」【TEMP】", EventId: nextEventId++));
            }
        }
        // 已成遺跡或不再屬於任何分開群的聚落不計時。
        foreach (var id in separated.Keys.Where(id => inhabited.All(s => s.Id != id)).ToList())
            separated.Remove(id);
        return (state with { Cultures = cultures.ToImmutable(), Settlements = settlements.ToImmutable(), SeparatedSince = separated.ToImmutable() }, events);
    }

    /// <summary>子文化：換掉上層文化的一個特性（同一池、不重複、守互斥組）；該池沒有可換的就沿用。</summary>
    public static ImmutableList<string> Mutate(ImmutableList<string> traits, Func<ulong, ulong> draw)
    {
        if (traits.IsEmpty)
            return traits;
        var index = (int)(draw(0) % (ulong)traits.Count);
        var removed = Pool.First(e => e.TraitId == traits[index]);
        var keep = traits.RemoveAt(index);
        var taken = keep.Select(t => Pool.First(e => e.TraitId == t).ExclusiveGroup).Where(g => g.Length > 0).ToHashSet();
        var candidates = Pool.Where(e => e.PoolId == removed.PoolId && e.TraitId != removed.TraitId && !keep.Contains(e.TraitId) && e.Weight > 0
            && (e.ExclusiveGroup.Length == 0 || !taken.Contains(e.ExclusiveGroup))).ToList();
        if (candidates.Count == 0)
            return traits;
        var pick = (long)(draw(1) % (ulong)candidates.Sum(e => e.Weight));
        return keep.Insert(index, candidates.First(e => (pick -= e.Weight) < 0).TraitId);
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

    /// <summary>【TEMP】第 08-9 章 B2：生計傳統的採集加成（地形成立時每項＋10%）。</summary>
    public const long TempLivelihoodYieldPercent = 10;

    /// <summary>生計傳統最多兩項（P02 生計池上限 2），所以採集加成最多＋20%。</summary>
    public const long TempLivelihoodYieldMax = 20;

    /// <summary>聚落主要文化的特性（沒有文化回傳空集合）。</summary>
    public static IReadOnlySet<string> MainTraits(DemoCultureState state, int settlementId)
        => state.MainCultureOf(settlementId)?.Traits.ToHashSet() ?? new HashSet<string>();

    /// <summary>
    /// 第 08-9 章 B2【TEMP 規則】主要文化的生計傳統：林地子民（可及森林）、河岸子民（鄰河）、海之民（鄰海）、草原子民（可及草原），
    /// 地形成立時每名勞力採集量各＋10%（與工具加成相加，記在週帳 ToolYieldPercent）。
    /// </summary>
    public static long LivelihoodYieldPercent(DemoCultureState state, DemoHexWorld map, DemoSettlement settlement)
    {
        var traits = MainTraits(state, settlement.Id);
        if (traits.Count == 0)
            return 0;
        var conditions = ConditionsOf(map, settlement).ToHashSet();
        var bonus = 0L;
        foreach (var (trait, condition) in new[] { ("CT_FOREST_FOLK", "NEAR_FOREST"), ("CT_RIVER_FOLK", "NEAR_RIVER"),
                     ("CT_SEA_FOLK", "NEAR_SEA"), ("CT_GRASSLAND_FOLK", "NEAR_GRASSLAND") })
            if (traits.Contains(trait) && conditions.Contains(condition))
                bonus += TempLivelihoodYieldPercent;
        return Math.Min(bonus, TempLivelihoodYieldMax);
    }

    /// <summary>
    /// 第 08-9 章 B2【TEMP 規則】主要文化對研究的影響（百分比，100＝不變）：重學＋10%（所有技術）；
    /// 尚武、草原子民：狩獵分支＋25%；海之民：造船分支＋25%。
    /// </summary>
    public static long ResearchPercent(IReadOnlySet<string> traits, DemoTechNode node)
    {
        var percent = 100L;
        if (traits.Contains("CT_SCHOLARLY")) percent += 10;
        if (node.Branch == "狩獵" && (traits.Contains("CT_MARTIAL") || traits.Contains("CT_GRASSLAND_FOLK"))) percent += 25;
        if (node.Branch == "造船" && traits.Contains("CT_SEA_FOLK")) percent += 25;
        return percent;
    }

    /// <summary>第 08-9 章 B2【TEMP】鄰近傳播倍率（百分比）：守舊減半、開放加倍。</summary>
    public static long DiffusionPercent(IReadOnlySet<string> traits)
        => traits.Contains("CT_TRADITIONAL") ? 50 : traits.Contains("CT_OPEN") ? 200 : 100;

    /// <summary>第 08-9 章 B2：強人統治的聚落，村長改由最壯年者（未滿老年的最年長成年人）擔任。</summary>
    public static bool StrongmanRule(DemoCultureState state, int settlementId) => MainTraits(state, settlementId).Contains("CT_STRONGMAN");

    /// <summary>點格詳情用：「文化：甲文化 70%、乙文化 30%」。</summary>
    public static string SharesLine(DemoCultureState state, int settlementId)
        => "文化：" + string.Join("、", state.SharesOf(settlementId).OrderByDescending(s => s.Permille).ThenBy(s => s.CultureId)
            .Select(s => $"{state.Of(s.CultureId)?.Name ?? "?"} {(s.Permille / 10.0).ToString("0.#", CultureInfo.InvariantCulture)}%"));

    /// <summary>文化特性一行：「甲文化：小家庭、尚武、林地子民…」。</summary>
    public static string TraitsLine(DemoCulture culture)
        => $"{culture.Name}的特性：{string.Join("、", culture.Traits.Select(TraitName))}";

    private static (IReadOnlyList<DemoPoolEntry>, IReadOnlyDictionary<string, string>) Load()
        => Build(OfficialConfig.ReadSheetText(PoolSheet), OfficialConfig.ReadSheetText(TraitSheet));

    internal static (IReadOnlyList<DemoPoolEntry> Pool, IReadOnlyDictionary<string, string> Names) Build(string poolText, string traitText)
    {
        var traitRows = ParameterSheet.ParseCsv(traitText);
        var th = traitRows[0].Select(h => h.Trim()).ToList();
        var (tid, tname) = (th.IndexOf("CultureTraitID"), th.IndexOf("中文名稱"));
        var names = traitRows.Skip(1).Where(r => r.Count > tname && r[tid].Trim().Length > 0)
            .ToDictionary(r => r[tid].Trim(), r => r[tname].Trim());
        var list = DemoTraitPools.Parse(poolText, PoolSheet, names.ContainsKey);
        return (list, names);
    }
}
