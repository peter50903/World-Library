using System.Collections.Immutable;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-9 章 C1：一格野地的獸群（12a：草食獸＝獵物、掠食獸；聚合族群）。</summary>
public sealed record DemoHerd(long Grazers, long Predators);

/// <summary>
/// 第 08-9 章 C1（12a 第 2 節）：野生動物的世界狀態——各格的獸群（以格編號為鍵，只列有獸的格）與出現週（0＝尚未出現）。不可變。
/// </summary>
public sealed record DemoWildlifeState(ImmutableSortedDictionary<long, DemoHerd> Cells, int AppearedWeek)
{
    public static DemoWildlifeState Empty { get; } = new(ImmutableSortedDictionary<long, DemoHerd>.Empty, 0);

    public DemoHerd? At(long cellId) => Cells.TryGetValue(cellId, out var herd) ? herd : null;

    public long TotalGrazers => Cells.Values.Sum(h => h.Grazers);

    public long TotalPredators => Cells.Values.Sum(h => h.Predators);

    public string Fingerprint()
        => $"wild@{AppearedWeek};" + string.Concat(Cells.Select(kv => $"w{kv.Key}={kv.Value.Grazers}/{kv.Value.Predators};"));

    public bool Equals(DemoWildlifeState? other) => other is not null && Fingerprint() == other.Fingerprint();

    public override int GetHashCode() => HashCode.Combine(AppearedWeek, Cells.Count);
}

/// <summary>
/// 第 08-9 章 C1【TEMP 規則】野生動物與狩獵（12a 第 1～2 節；第 12 章 MON_POP、MON_CARRY、MON_PRED、MON_HUMAN）。純函式、不抽亂數。
/// <list type="bullet">
/// <item>生活圈＝聚落格與六鄰格；其餘森林、草原格是野地。進入開拓時代那週，每格野地放入承載量的獸群。</item>
/// <item>每年一次（第 52 週的倍數）：野地的草食獸＋20%（至少 1）到承載（森林 6、草原 8）；掠食獸承載＝本格與鄰格草食獸的五分之一，
/// 未滿＋10%（至少 1）、超過的死亡。生活圈內的獸群不繁殖（之後會被獵光或留著）。</item>
/// <item>狩獵：會狩獵的聚落每週以每 5 名成年人 1 頭的量，從 2 格內的獸群獵草食獸（格編號小的先），每頭化為糧食 20。</item>
/// </list>
/// </summary>
public static class DemoWildlife
{
    public const long TempForestGrazers = 6;
    public const long TempGrasslandGrazers = 8;
    public const int TempGrazerGrowthPercent = 20;
    public const int TempPredatorGrowthPercent = 10;
    public const long TempGrazersPerPredator = 5;
    public const long TempAdultsPerHunter = 5;
    public const long TempFoodPerGrazer = 20;
    public const int TempHuntRange = 2;

    public static readonly DemoTechnology Hunting = new("TECH_HUNTING");
    public static readonly DemoTechnology Archery = new("TECH_ARCHERY");
    public static readonly DemoTechnology WatchAndHunt = new("TECH_WATCH_AND_HUNT");

    /// <summary>【TEMP】第 08-9 章 C2：每 10 頭附近的掠食獸，途中隊伍每週被襲機率＋5%，最多 30%。</summary>
    public const int TempAttackPercentPer10Predators = 5;

    public const int TempMaxAttackPercent = 30;

    /// <summary>
    /// 第 08-9 章 C2（12a 第 3 節，使用者 2026-10-23：只攻擊生活圈外的人）【TEMP 規則】結算前決定本週途中隊伍遭野獸襲擊的死亡人數。
    /// 隊伍所在格不在任何生活圈內時，依本格與鄰格的掠食獸數決定機率；被襲死 1～2 人；出發聚落會弓箭→損失減半（無條件進位），
    /// 會巡守與圍獵→機率減半。局部亂數以隊伍編號為鍵、週次為下標（不推進計數器）。
    /// </summary>
    public static IReadOnlyDictionary<int, long> Attacks(DemoWildlifeState state, DemoHexWorld map, DemoWorldSites sites,
        IEnumerable<DemoMigrationBatch> batches, DemoKnowledgeState knowledge, int week, RngRegistry rng)
    {
        var result = new Dictionary<int, long>();
        if (state.Cells.IsEmpty)
            return result;
        var wild = WildCells(map, sites).Select(c => CellId(map, c)).ToHashSet();
        foreach (var batch in batches.Where(b => b.People > 0).OrderBy(b => b.Id))
        {
            var cell = map.GetCell(batch.Cell.Column, batch.Cell.Row);
            if (!wild.Contains(CellId(map, cell)))
                continue; // 生活圈內（或水域）安全
            var predators = (state.At(CellId(map, cell))?.Predators ?? 0) + DemoRouteNetwork.Directions
                .Select(d => map.AtAxial(cell.AxialQ + d.Dq, cell.AxialR + d.Dr)).Where(n => n is not null)
                .Sum(n => state.At(CellId(map, n!))?.Predators ?? 0);
            var chance = Math.Min(TempMaxAttackPercent, predators / 10 * TempAttackPercentPer10Predators);
            if (knowledge.Knows(batch.FromSettlementId, WatchAndHunt))
                chance /= 2;
            if (chance <= 0)
                continue;
            var random = EntityLocalRandom.Create(rng, EntityId.From((ulong)batch.Id), RngStreams.Event, "TEMP-BEAST-v1");
            if ((long)(random.GetUInt64((ulong)week * 2) % 100UL) >= chance)
                continue;
            var killed = 1 + (long)(random.GetUInt64((ulong)week * 2 + 1) % 2UL);
            if (knowledge.Knows(batch.FromSettlementId, Archery))
                killed = (killed + 1) / 2;
            result[batch.Id] = killed;
        }
        return result;
    }

    public static long CellId(DemoHexWorld map, DemoHexCell cell) => checked((long)cell.Row * map.Columns + cell.Column + 1);

    /// <summary>野地：森林或草原、不在任何現存聚落的生活圈（本格與六鄰格）。</summary>
    public static IReadOnlyList<DemoHexCell> WildCells(DemoHexWorld map, DemoWorldSites sites)
    {
        var living = new HashSet<long>();
        foreach (var s in sites.Settlements.Where(s => !s.IsRuin))
        {
            var origin = map.GetCell(s.Column, s.Row);
            living.Add(CellId(map, origin));
            foreach (var d in DemoRouteNetwork.Directions)
                if (map.AtAxial(origin.AxialQ + d.Dq, origin.AxialR + d.Dr) is { } c)
                    living.Add(CellId(map, c));
        }
        return map.Cells.Where(c => c.Terrain is DemoTerrain.Forest or DemoTerrain.Grassland && !living.Contains(CellId(map, c))).ToArray();
    }

    public static long GrazerCapacity(DemoHexCell cell) => cell.Terrain == DemoTerrain.Forest ? TempForestGrazers : TempGrasslandGrazers;

    /// <summary>格距離（軸向座標）。</summary>
    public static int Distance(DemoHexCell a, DemoHexCell b)
    {
        var dq = a.AxialQ - b.AxialQ;
        var dr = a.AxialR - b.AxialR;
        return Math.Max(Math.Abs(dq), Math.Max(Math.Abs(dr), Math.Abs(dq + dr)));
    }

    /// <summary>聚落 2 格內有草食獸（獸產大類、狩獵與畜牧的從業條件）。</summary>
    public static bool AnimalsNear(DemoWildlifeState state, DemoHexWorld map, DemoSettlement settlement)
    {
        if (state.Cells.IsEmpty)
            return false;
        var origin = map.GetCell(settlement.Column, settlement.Row);
        return state.Cells.Any(kv => kv.Value.Grazers > 0 && Distance(origin, CellOf(map, kv.Key)) <= TempHuntRange);
    }

    public static DemoHexCell CellOf(DemoHexWorld map, long cellId)
        => map.GetCell((int)((cellId - 1) % map.Columns), (int)((cellId - 1) / map.Columns));

    /// <summary>
    /// 本週的狩獵計畫（結算前，用期初狀態）：各聚落獵到的頭數與糧食，以及獵後的獸群。只有開拓時代起、會狩獵的聚落。
    /// </summary>
    public static (DemoWildlifeState State, IReadOnlyDictionary<int, long> Food) Hunt(DemoWildlifeState state, DemoHexWorld map, DemoWorldSites sites,
        DemoCohortDemography population, DemoKnowledgeState knowledge)
    {
        var food = new Dictionary<int, long>();
        if (state.Cells.IsEmpty)
            return (state, food);
        var cells = state.Cells.ToBuilder();
        foreach (var s in sites.Settlements.Where(s => !s.IsRuin && knowledge.Knows(s.Id, Hunting)).OrderBy(s => s.Id))
        {
            var hunters = population.AdultsIn(s.Id, DemoHumanLifecycle.Rules) / TempAdultsPerHunter;
            if (hunters <= 0)
                continue;
            var origin = map.GetCell(s.Column, s.Row);
            var taken = 0L;
            foreach (var id in cells.Keys.Where(id => cells[id].Grazers > 0 && Distance(origin, CellOf(map, id)) <= TempHuntRange).ToList())
            {
                if (taken >= hunters)
                    break;
                var herd = cells[id];
                var take = Math.Min(herd.Grazers, hunters - taken);
                taken += take;
                cells[id] = herd with { Grazers = herd.Grazers - take };
            }
            if (taken > 0)
                food[s.Id] = checked(taken * TempFoodPerGrazer);
        }
        return (state with { Cells = Prune(cells) }, food);
    }

    /// <summary>
    /// 每週提交後：進入開拓時代那週放入獸群；每年（第 52 週的倍數）繁殖與承載。
    /// </summary>
    public static DemoWildlifeState Advance(DemoWildlifeState state, DemoHexWorld map, DemoWorldSites sites, DemoEra era, int week)
    {
        if (era == DemoEra.Hope)
            return state;
        var wild = WildCells(map, sites);
        if (state.AppearedWeek == 0)
            return new DemoWildlifeState(wild.Where(c => GrazerCapacity(c) > 0).ToImmutableSortedDictionary(
                c => CellId(map, c), c => new DemoHerd(GrazerCapacity(c), Math.Max(1, GrazerCapacity(c) / TempGrazersPerPredator))), week);
        if (week % 52 != 0)
            return state;
        var cells = state.Cells.ToBuilder();
        foreach (var c in wild)
        {
            var id = CellId(map, c);
            var herd = cells.TryGetValue(id, out var h) ? h : new DemoHerd(0, 0);
            var cap = GrazerCapacity(c);
            var grazers = herd.Grazers >= cap || herd.Grazers == 0 ? Math.Min(herd.Grazers, cap)
                : Math.Min(cap, herd.Grazers + Math.Max(1, herd.Grazers * TempGrazerGrowthPercent / 100));
            cells[id] = herd with { Grazers = grazers };
        }
        foreach (var c in wild)
        {
            var id = CellId(map, c);
            var herd = cells[id];
            var prey = herd.Grazers + DemoRouteNetwork.Directions.Select(d => map.AtAxial(c.AxialQ + d.Dq, c.AxialR + d.Dr))
                .Where(n => n is not null).Sum(n => cells.TryGetValue(CellId(map, n!), out var x) ? x.Grazers : 0);
            var cap = prey / TempGrazersPerPredator;
            var predators = herd.Predators > cap ? cap
                : herd.Predators == 0 ? 0 : Math.Min(cap, herd.Predators + Math.Max(1, herd.Predators * TempPredatorGrowthPercent / 100));
            cells[id] = herd with { Predators = predators };
        }
        return state with { Cells = Prune(cells) };
    }

    private static ImmutableSortedDictionary<long, DemoHerd> Prune(ImmutableSortedDictionary<long, DemoHerd>.Builder cells)
    {
        foreach (var id in cells.Where(kv => kv.Value.Grazers == 0 && kv.Value.Predators == 0).Select(kv => kv.Key).ToList())
            cells.Remove(id);
        return cells.ToImmutable();
    }
}
