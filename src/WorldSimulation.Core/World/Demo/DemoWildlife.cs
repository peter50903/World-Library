using System.Collections.Immutable;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 12-1 章 C1：某年起（<see cref="FromWeek"/> 週結算之後）獵物豐富、可享狩獵加成的聚落（依編號排序）。
/// 週帳驗證用它找出某週結算時誰有加成。
/// </summary>
public sealed record DemoHuntingYear(int FromWeek, ImmutableList<int> SettlementIds);

/// <summary>
/// 第 12-1 章 C1：野生動物（聚合）。每格兩個數字：<see cref="Prey"/>（鹿、野豬）、<see cref="Wolves"/>（狼），
/// 依格編號（列×欄數＋欄）排列。每年結算一次；玩家只看到「獸群出沒」「狼群出沒」與聚落附近獵物多寡。
/// </summary>
public sealed record DemoWildlifeState(ImmutableArray<int> Prey, ImmutableArray<int> Wolves, int LastYearWeek)
{
    public static DemoWildlifeState Empty { get; } = new(ImmutableArray<int>.Empty, ImmutableArray<int>.Empty, 0);

    /// <summary>每一年的狩獵加成名單（依週次排序）。</summary>
    public ImmutableList<DemoHuntingYear> Hunting { get; init; } = ImmutableList<DemoHuntingYear>.Empty;

    /// <summary>第 <paramref name="week"/> 週結算時（該週以前的最後一次年結算）某聚落有沒有狩獵加成。</summary>
    public bool HuntingAt(int settlementId, int week)
        => Hunting.LastOrDefault(h => h.FromWeek < week) is { } h && h.SettlementIds.Contains(settlementId);

    public string Fingerprint()
        => $"wl{LastYearWeek}:" + string.Join(',', Prey) + "|" + string.Join(',', Wolves) + ";"
           + string.Concat(Hunting.Select(h => $"hy{h.FromWeek}=" + string.Join(',', h.SettlementIds) + ";"));
}

/// <summary>
/// 第 12-1 章 C1【TEMP 規則】野生動物：承載依地形；每 52 週結算——狩獵、驅狼、獵物成長、狼捕食、超出承載往鄰格擴散。
/// 不抽亂數（純函式）；希望時代也照常結算（動物不受時代影響），但狩獵要已會「狩獵」。
/// </summary>
public static class DemoWildlife
{
    /// <summary>【TEMP】結算週期（週）。</summary>
    public const int TempYearWeeks = 52;

    /// <summary>【TEMP】獵物年成長（往承載的缺口 %）、狼吃飽時年增加（%）、每隻狼每年吃幾隻獵物。</summary>
    public const int TempPreyGrowthPercent = 25;
    public const int TempWolfGrowthPercent = 20;
    public const int TempPreyPerWolf = 3;

    /// <summary>【TEMP】每名成年人每年狩獵取走的獵物（千分之幾隻）；聚落附近的狼每年被驅趕的比例（%）。</summary>
    public const int TempHuntPerAdultPermille = 200;
    public const int TempWolfDrivenPercent = 50;

    /// <summary>【TEMP】採集範圍內獵物達承載的這個比例（%）以上算「豐富」，森林與草原野採來源＋<see cref="TempHuntingSourcePercent"/>%。</summary>
    public const int TempRichPercent = 50;
    public const long TempHuntingSourcePercent = 10;

    public const string HuntingTech = "TECH_HUNTING";

    /// <summary>【TEMP】承載：森林 6／2、草原 8／1、丘陵 3／2；水域 0。</summary>
    public static (int Prey, int Wolves) Capacity(DemoHexCell cell) => cell.Terrain switch
    {
        DemoTerrain.Forest => (6, 2),
        DemoTerrain.Grassland => (8, 1),
        DemoTerrain.Hills => (3, 2),
        _ => (0, 0)
    };

    private static int Index(DemoHexWorld map, int column, int row) => row * map.Columns + column;

    /// <summary>人類佔用的格（聚落、農田、開墾中）沒有動物。</summary>
    private static (int Prey, int Wolves) CapacityAt(DemoHexWorld map, DemoHexCell cell, ISet<DemoCellRef> human)
        => human.Contains(new DemoCellRef(cell.Column, cell.Row)) ? (0, 0) : Capacity(cell);

    private static HashSet<DemoCellRef> Human(DemoWorldSites sites, DemoLandDevelopmentMap land)
        => sites.Settlements.Where(s => !s.IsRuin).Select(s => new DemoCellRef(s.Column, s.Row))
            .Concat(land.Parcels.Select(p => p.Cell)).ToHashSet();

    /// <summary>開局：各格為承載的一半。</summary>
    public static DemoWildlifeState Open(DemoHexWorld map, DemoWorldSites sites, DemoLandDevelopmentMap land)
    {
        ArgumentNullException.ThrowIfNull(map);
        var human = Human(sites, land);
        var caps = map.Cells.Select(c => CapacityAt(map, c, human)).ToArray();
        return new DemoWildlifeState(caps.Select(c => c.Prey / 2).ToImmutableArray(), caps.Select(c => c.Wolves / 2).ToImmutableArray(), 0);
    }

    /// <summary>聚落的採集範圍格（本格與相鄰格）。</summary>
    public static IEnumerable<DemoHexCell> Reach(DemoHexWorld map, DemoSettlement s)
    {
        var origin = map.GetCell(s.Column, s.Row);
        yield return origin;
        foreach (var d in DemoRouteNetwork.Directions)
            if (map.AtAxial(origin.AxialQ + d.Dq, origin.AxialR + d.Dr) is { } c)
                yield return c;
    }

    /// <summary>聚落附近獵物的程度：0＝稀少、1＝普通、2＝豐富（顯示用）。</summary>
    public static int PreyLevel(DemoWildlifeState state, DemoHexWorld map, DemoSettlement s)
    {
        if (state.Prey.IsDefaultOrEmpty) return 0;
        var cells = Reach(map, s).ToList();
        var cap = cells.Sum(c => Capacity(c).Prey);
        var prey = cells.Sum(c => state.Prey[Index(map, c.Column, c.Row)]);
        return cap == 0 || prey * 100 < cap * 20 ? 0 : prey * 100 >= cap * TempRichPercent ? 2 : 1;
    }

    /// <summary>點格顯示：獸群出沒（獵物過承載一半）、狼群出沒（狼 ≥ 2）。</summary>
    public static string? CellNote(DemoWildlifeState state, DemoHexWorld map, DemoHexCell cell)
    {
        if (state.Prey.IsDefaultOrEmpty) return null;
        var i = Index(map, cell.Column, cell.Row);
        var notes = new List<string>();
        var cap = Capacity(cell);
        if (cap.Prey > 0 && state.Prey[i] * 2 > cap.Prey) notes.Add("獸群出沒");
        if (state.Wolves[i] >= 2) notes.Add("狼群出沒");
        return notes.Count == 0 ? null : string.Join("、", notes);
    }

    /// <summary>
    /// 每週（生死、知識之後）：滿 <see cref="TempYearWeeks"/> 週做一次年結算，否則原樣回傳。
    /// <paramref name="adultsOf"/>＝聚落成年人數；<paramref name="hunts"/>＝聚落已會狩獵。
    /// </summary>
    public static DemoWildlifeState Advance(DemoWildlifeState state, int week, DemoHexWorld map, DemoWorldSites sites,
        DemoLandDevelopmentMap land, Func<int, long> adultsOf, Func<int, bool> hunts)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);
        if (state.Prey.IsDefaultOrEmpty || week - state.LastYearWeek < TempYearWeeks)
            return state;
        var human = Human(sites, land);
        var prey = state.Prey.ToArray();
        var wolves = state.Wolves.ToArray();
        var cells = map.Cells;
        var living = sites.Settlements.Where(s => !s.IsRuin && adultsOf(s.Id) > 0).OrderBy(s => s.Id).ToList();

        // ① 人類：佔用格清空；會狩獵的聚落從採集範圍取走獵物（依格順序，取完為止）；附近的狼被驅散一半。
        foreach (var c in cells)
            if (human.Contains(new DemoCellRef(c.Column, c.Row)))
                prey[Index(map, c.Column, c.Row)] = wolves[Index(map, c.Column, c.Row)] = 0;
        foreach (var s in living)
        {
            var reach = Reach(map, s).Select(c => Index(map, c.Column, c.Row)).ToList();
            if (hunts(s.Id))
            {
                var quota = (int)Math.Min(int.MaxValue, (adultsOf(s.Id) * TempHuntPerAdultPermille + 999) / 1000);
                foreach (var i in reach)
                {
                    var take = Math.Min(prey[i], quota);
                    prey[i] -= take;
                    quota -= take;
                    if (quota == 0) break;
                }
            }
            foreach (var i in reach)
                wolves[i] -= wolves[i] * TempWolfDrivenPercent / 100; // 聚落附近的狼被驅散
        }

        // ② 自然：獵物往承載成長；狼吃獵物，吃飽才增加，不足按缺口死亡。
        for (var i = 0; i < cells.Count; i++)
        {
            var cap = CapacityAt(map, cells[i], human);
            if (cap.Prey > prey[i])
                prey[i] += Math.Max(1, (cap.Prey - prey[i]) * TempPreyGrowthPercent / 100);
            var need = wolves[i] * TempPreyPerWolf;
            if (need <= prey[i])
            {
                prey[i] -= need;
                if (wolves[i] > 0) wolves[i] += Math.Max(1, wolves[i] * TempWolfGrowthPercent / 100);
            }
            else
            {
                var fed = prey[i] / TempPreyPerWolf;
                prey[i] -= fed * TempPreyPerWolf;
                wolves[i] = fed;
            }
        }

        // ③ 擴散：超出承載的部分平均移往相鄰可住格（依方向順序），無處可去就死亡。
        Spread(prey, c => CapacityAt(map, c, human).Prey);
        Spread(wolves, c => CapacityAt(map, c, human).Wolves);

        void Spread(int[] counts, Func<DemoHexCell, int> capOf)
        {
            var overflow = new int[counts.Length];
            for (var i = 0; i < cells.Count; i++)
            {
                var cap = capOf(cells[i]);
                if (counts[i] > cap) { overflow[i] = counts[i] - cap; counts[i] = cap; }
            }
            for (var i = 0; i < cells.Count; i++)
            {
                if (overflow[i] == 0) continue;
                var c = cells[i];
                foreach (var d in DemoRouteNetwork.Directions)
                {
                    if (overflow[i] == 0) break;
                    if (map.AtAxial(c.AxialQ + d.Dq, c.AxialR + d.Dr) is not { } n) continue;
                    var j = Index(map, n.Column, n.Row);
                    var room = capOf(n) - counts[j];
                    if (room <= 0) continue;
                    var move = Math.Min(room, overflow[i]);
                    counts[j] += move;
                    overflow[i] -= move;
                }
            }
        }

        // ④ 狩獵加成名單：會狩獵且採集範圍內獵物豐富。
        var next = new DemoWildlifeState(prey.ToImmutableArray(), wolves.ToImmutableArray(), week) { Hunting = state.Hunting };
        var rich = living.Where(s => hunts(s.Id) && PreyLevel(next, map, s) == 2).Select(s => s.Id).ToImmutableList();
        var last = state.Hunting.LastOrDefault();
        var hunting = last is not null && last.SettlementIds.SequenceEqual(rich) ? state.Hunting : state.Hunting.Add(new DemoHuntingYear(week, rich));
        return next with { Hunting = hunting };
    }

    /// <summary>
    /// 【TEMP】第 12-1 章 C2：狼至少這麼多隻的格才會襲擊；每隻狼每週襲擊機率（%）；已會弓箭的出發聚落機率減半。
    /// （原訂 2 隻、2%：狼承載只有 2、聚落每年驅散一半，路線附近幾乎沒有 2 隻狼的格，百年 0 次，08-10c）
    /// </summary>
    public const int TempAttackMinWolves = 1;
    public const int TempAttackPercentPerWolf = 3;

    public const string ArcheryTech = "TECH_ARCHERY";

    private const string AttackNamespace = "TEMP-BEAST-v1";

    /// <summary>
    /// 第 12-1 章 C2：隊伍本週所在格的狼群襲擊死亡人數（0＝沒有襲擊；1～2 人）。
    /// 抽樣用戰鬥流的實體局部亂數（實體＝隊伍、索引＝週次），不推進計數器，重跑結果相同。
    /// </summary>
    public static long AttackDeaths(DemoWildlifeState state, DemoHexWorld map, DemoMigrationBatch batch, int week, RngRegistry rng, bool archery)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(batch);
        if (state.Wolves.IsDefaultOrEmpty)
            return 0;
        var wolves = state.Wolves[Index(map, batch.Cell.Column, batch.Cell.Row)];
        if (wolves < TempAttackMinWolves)
            return 0;
        var chance = wolves * TempAttackPercentPerWolf * (archery ? 50 : 100) / 100;
        var r = EntityLocalRandom.Create(rng, EntityId.From((ulong)batch.Id), RngStreams.Combat, AttackNamespace).GetUInt64((ulong)week);
        return (long)(r % 100UL) < chance ? 1 + (long)(r / 100UL % 2UL) : 0;
    }
}
