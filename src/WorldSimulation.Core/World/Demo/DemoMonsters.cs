using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 12-2 章：一個地下城的怪物（由事件推得，不另存）。<see cref="Cells"/>＝怪物實際佔據、巡遊的格（第 12-2a 節：從地下城往外一格一格佔，
/// 不進人類的勢力範圍與水；沒算時是空的）。<see cref="Radius"/>＝範圍離地下城最遠幾格（沉寂時 0）。
/// </summary>
public sealed record DemoMonsterNest(DemoDungeon Dungeon, int Monsters, bool Dormant, int DormantSinceWeek)
{
    public long CellId { get; init; }

    public IReadOnlyList<DemoCellRef> Cells { get; init; } = Array.Empty<DemoCellRef>();

    public int Radius => Monsters <= 0 ? 0
        : Cells.Count > 0 ? Cells.Max(c => DemoHexWorld.Distance(c, Dungeon.Cell)) : DemoMonsters.Radius(Monsters);

    /// <summary>怪物的範圍：已算好的佔據格；沒算時以舊規則的方形近似。</summary>
    public IEnumerable<DemoCellRef> Area()
    {
        if (Cells.Count > 0 || Monsters <= 0)
            return Cells;
        var r = Radius;
        var c = Dungeon.Cell;
        return Enumerable.Range(-r, 2 * r + 1).SelectMany(dr => Enumerable.Range(-r, 2 * r + 1).Select(dc => new DemoCellRef(c.Column + dc, c.Row + dr)));
    }
}

/// <summary>
/// 第 12-2 章【TEMP 規則】地下城的怪物（使用者 2026-11-07：怪物出來後要有自己的勢力範圍，會殺人，也會被人殺；殺光後地下城沉寂，之後再生）。
/// <list type="number">
/// <item>地下城出現時有 層數×<see cref="TempInitialPerLayer"/> 隻怪物；每 <see cref="TempSpawnWeeks"/> 週多生 1 隻，最多 層數×<see cref="TempCapPerLayer"/> 隻。</item>
/// <item>勢力範圍：以地下城為中心 2＋怪物數÷4 格（最多 4 格）。</item>
/// <item>每週（開拓時代起）每個有怪物的地下城：
///   <b>襲擊</b>——範圍內最近的有人聚落以 怪物數×3%（最多 25%）被襲，死 1＋怪物數÷5 人（結算時至少留一人），居民擊殺 1＋居民÷30 隻；
///   <b>討伐</b>——範圍外再兩格以內、居民至少 <see cref="TempHuntMinResidents"/> 人的聚落（居民最多的先）以 10% 出兵，殺 1＋居民÷25 隻，陣亡 0～怪物數÷4 人。
///   同一個聚落同一週只承受一件（天災優先，其次襲擊、討伐）。</item>
/// <item>怪物被殺光：記「地下城沉寂」，勢力範圍消失；<see cref="TempDormantWeeks"/> 週後「怪物再生」，回到初始數量。</item>
/// </list>
/// 事件：襲擊與討伐是聚落承受的事件（人數欄＝死亡人數，與天災同一欄週帳）；「擊殺怪物」人數欄＝格編號×1000＋擊殺數；沉寂、再生人數欄＝格編號。全部是根因。
/// 怪物數由事件依時間推得：存讀檔結果相同。亂數用事件流的實體局部亂數（索引＝週次），不推進計數器。
/// </summary>
public static class DemoMonsters
{
    /// <summary>【TEMP】每層的初始與上限怪物數。</summary>
    public const int TempInitialPerLayer = 3, TempCapPerLayer = 8;

    /// <summary>【TEMP】每幾週多生一隻。</summary>
    public const int TempSpawnWeeks = 8;

    /// <summary>【TEMP】殺光後沉寂幾週（三年）才再生。</summary>
    public const int TempDormantWeeks = 156;

    /// <summary>【TEMP】襲擊機率：每隻怪物每週 1%、最多 8%（第 08-18l 章：原 3%、25%，地下城附近的村子幾乎每月被襲擊而人口流失）。</summary>
    public const int TempRaidPercentPerMonster = 1, TempMaxRaidPercent = 8;

    /// <summary>【TEMP】討伐：出兵機率、至少幾名居民、比勢力範圍遠幾格。</summary>
    public const int TempHuntPercent = 10, TempHuntMinResidents = 20, TempHuntReach = 3;

    // 第 12-2c 節：擊殺數與傷亡改由戰鬥人員、非戰鬥人員與雙方等級決定（DemoCombat）。

    /// <summary>
    /// 【TEMP】第 12-2b 節（使用者 2026-11-08：平常只能壓制或放任怪物擴張，只有出現軍隊時才會完全壓制地下城）：
    /// 居民的抵抗與討伐最多把怪物壓到每層 <see cref="TempSuppressFloorPerLayer"/> 隻，殺不光；第 12-2c 節：有等級不低於怪物的戰鬥人員（士兵、冒險者）才殺得光（地下城沉寂）。
    /// </summary>
    public const int TempSuppressFloorPerLayer = 1;

    /// <summary>沒有夠強的戰鬥人員時殺不到這個數以下。</summary>
    public static int SuppressFloor(DemoDungeon dungeon) => dungeon.Layers * TempSuppressFloorPerLayer;

    private const string Namespace = "TEMP-MONSTER-v1";

    public static int Initial(DemoDungeon dungeon) => dungeon.Layers * TempInitialPerLayer;

    public static int Cap(DemoDungeon dungeon) => dungeon.Layers * TempCapPerLayer;

    /// <summary>【TEMP】舊規則的勢力範圍：2＋怪物數÷4 格，最多 4 格；沒有怪物＝0（還沒算佔據格時的近似）。</summary>
    public static int Radius(int monsters) => monsters <= 0 ? 0 : Math.Min(4, 2 + monsters / 4);

    /// <summary>【TEMP】第 12-2a 節：每隻怪物佔幾格、離地下城最遠幾格；離範圍幾格內的聚落會被襲擊。</summary>
    public const int TempCellsPerMonster = 1, TempMaxReach = 4, TempRaidReach = 1;

    /// <summary>
    /// 第 12-2a 節（使用者 2026-11-08：地下城的勢力範圍只限怪物出去後移動、控制的範圍；範圍內不可以有人類居住，除非被討伐完畢）：
    /// 怪物佔據的格——從地下城所在格開始，往外（八方向）一格一格佔：離地下城近的先、同距離依列、欄排序；
    /// 不進水、不進人類的勢力範圍（<paramref name="human"/>）、離地下城最多 <see cref="TempMaxReach"/> 格；
    /// 佔的格數＝怪物數×<see cref="TempCellsPerMonster"/>。被人類圍住時範圍就小。沒有怪物＝空。
    /// </summary>
    public static IReadOnlyList<DemoCellRef> Claim(DemoHexWorld map, DemoCellRef dungeon, int monsters, Func<DemoCellRef, bool> human)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(human);
        var cells = new List<DemoCellRef>();
        if (monsters <= 0 || !map.Contains(dungeon.Column, dungeon.Row))
            return cells;
        var want = monsters * TempCellsPerMonster;
        var seen = new HashSet<DemoCellRef> { dungeon };
        var frontier = new List<DemoCellRef> { dungeon };
        while (frontier.Count > 0 && cells.Count < want)
        {
            var next = frontier.OrderBy(c => DemoHexWorld.Distance(c, dungeon)).ThenBy(c => c.Row).ThenBy(c => c.Column).First();
            frontier.Remove(next);
            cells.Add(next);
            for (var dr = -1; dr <= 1; dr++)
                for (var dc = -1; dc <= 1; dc++)
                {
                    var n = new DemoCellRef(next.Column + dc, next.Row + dr);
                    if (!map.Contains(n.Column, n.Row) || !seen.Add(n) || DemoHexWorld.Distance(n, dungeon) > TempMaxReach
                        || map.GetCell(n.Column, n.Row).Terrain == DemoTerrain.Water || human(n))
                        continue;
                    frontier.Add(n);
                }
        }
        return cells;
    }

    public static long EncodeKills(long cellId, int kills) => checked(cellId * 1000 + kills);

    public static (long CellId, int Kills) DecodeKills(long count) => (count / 1000, (int)(count % 1000));

    public static bool IsStrike(DemoSettlementEventKind kind) => kind is DemoSettlementEventKind.MonsterRaid or DemoSettlementEventKind.MonsterHunt;

    /// <summary>
    /// 由事件推得的怪物數（依序餵入已提交的事件；<see cref="At"/> 查某週結束時的狀態）。
    /// 每個地下城記一個「基準」：從哪一週起、當時幾隻；之後每 <see cref="TempSpawnWeeks"/> 週加一隻到上限。
    /// </summary>
    public sealed class Fold
    {
        private readonly Dictionary<long, (int Week, int Count, bool Dormant)> _base = new();
        private readonly Dictionary<int, long> _killsBy = new();

        /// <summary>第 12-2c 節：這個聚落累計殺了幾隻怪物（冒險者的經驗）。</summary>
        public long KillsBy(int settlementId) => _killsBy.GetValueOrDefault(settlementId);

        /// <summary>餵入一筆事件（地下城清單要已包含它所屬的地下城）。</summary>
        public void Apply(DemoSettlementEvent e, Func<long, DemoDungeon?> dungeonAt)
        {
            switch (e.Kind)
            {
                case DemoSettlementEventKind.MonsterSlain:
                {
                    var (cell, kills) = DecodeKills(e.Count);
                    if (dungeonAt(cell) is not { } d)
                        return;
                    var now = Count(d, cell, e.Week);
                    _killsBy[e.SettlementId] = _killsBy.GetValueOrDefault(e.SettlementId) + kills;
                    _base[cell] = (e.Week, Math.Max(0, now - kills), false);
                    break;
                }
                case DemoSettlementEventKind.DungeonCleared when dungeonAt(e.Count) is not null:
                    _base[e.Count] = (e.Week, 0, true);
                    break;
                case DemoSettlementEventKind.DungeonAwakened when dungeonAt(e.Count) is { } awake:
                    _base[e.Count] = (e.Week, Initial(awake), false);
                    break;
            }
        }

        /// <summary>某週結束時的怪物數。</summary>
        public int Count(DemoDungeon dungeon, long cellId, int week)
        {
            var (from, count, dormant) = _base.TryGetValue(cellId, out var b) ? b : (dungeon.AppearedWeek, Initial(dungeon), false);
            if (dormant)
                return 0;
            return Math.Min(Cap(dungeon), count + Math.Max(0, week - from) / TempSpawnWeeks);
        }

        public DemoMonsterNest At(DemoDungeon dungeon, long cellId, int week)
        {
            var dormant = _base.TryGetValue(cellId, out var b) && b.Dormant;
            return new DemoMonsterNest(dungeon, Count(dungeon, cellId, week), dormant, dormant ? b.Week : 0) { CellId = cellId };
        }
    }

    /// <summary>本週怪物的行動：打在聚落的事件（與天災同一欄週帳）、擊殺、沉寂與再生。</summary>
    public sealed record Plan(
        IReadOnlyDictionary<int, DemoDisasterStrike> Strikes,
        IReadOnlyList<(int SettlementId, long CellId, int Kills, string Text)> Kills,
        IReadOnlyList<DemoMonsterNest> Cleared,
        IReadOnlyList<DemoMonsterNest> Awakened)
    {
        public static Plan Empty { get; } = new(new Dictionary<int, DemoDisasterStrike>(), Array.Empty<(int, long, int, string)>(),
            Array.Empty<DemoMonsterNest>(), Array.Empty<DemoMonsterNest>());
    }

    private static ulong Draw(RngRegistry rng, int week, int slot, int k)
        => EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Event, Namespace).GetUInt64(checked((ulong)week * 4096UL + (ulong)slot * 16UL + (ulong)k));

    /// <summary>
    /// 本週的怪物行動。<paramref name="nests"/>＝上週結束時的狀態（依地下城出現順序）；<paramref name="taken"/>＝本週已經有天災的聚落（不再承受怪物）。
    /// 希望時代、沒有地下城＝空。
    /// </summary>
    public static Plan Roll(int week, DemoEra era, DemoHexWorld map, DemoWorldSites sites, Func<int, long> residents,
        IReadOnlyList<DemoMonsterNest> nests, IReadOnlySet<int> taken, RngRegistry rng, Func<int, DemoCombatForce>? force = null)
    {
        // 第 12-2c 節：沒給人力時，成年人都算非戰鬥人員（測試與舊呼叫）。
        force ??= id => new DemoCombatForce(0, 0, 0, 0, residents(id));
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(residents);
        ArgumentNullException.ThrowIfNull(nests);
        ArgumentNullException.ThrowIfNull(taken);
        if (era == DemoEra.Hope || nests.Count == 0)
            return Plan.Empty;
        var strikes = new Dictionary<int, DemoDisasterStrike>();
        var kills = new List<(int, long, int, string)>();
        var cleared = new List<DemoMonsterNest>();
        var awakened = new List<DemoMonsterNest>();
        var living = sites.Settlements.Where(s => !s.IsRuin && residents(s.Id) > 0).OrderBy(s => s.Id).ToList();
        for (var slot = 0; slot < nests.Count; slot++)
        {
            var nest = nests[slot];
            if (nest.Dormant)
            {
                if (week - nest.DormantSinceWeek >= TempDormantWeeks)
                    awakened.Add(nest);
                continue;
            }
            var monsters = nest.Monsters;
            if (monsters <= 0)
                continue;
            // 第 12-2a 節：距離量到怪物佔據的範圍（最近的一格）；範圍內不會有聚落（人類的勢力範圍不被佔）。
            var area = nest.Area().ToList();
            int Distance(DemoSettlement s)
            {
                var home = new DemoCellRef(s.Column, s.Row);
                return area.Count == 0 ? int.MaxValue : area.Min(c => DemoHexWorld.Distance(c, home));
            }
            var left = monsters;
            var level = DemoCombat.MonsterLevel(nest.Dungeon);
            // 第 12-2b、12-2c 節：沒有等級不低於怪物的戰鬥人員時，最多壓到壓制下限。
            int Room(DemoCombatForce f) => DemoCombat.CanClear(f, level) ? left : Math.Max(0, left - SuppressFloor(nest.Dungeon));
            // 襲擊：離範圍一格內最近的有人聚落；約一半的怪物出擊，全村（戰鬥與非戰鬥人員）守村。
            var target = living.Where(s => !taken.Contains(s.Id) && !strikes.ContainsKey(s.Id) && Distance(s) <= TempRaidReach)
                .OrderBy(Distance).ThenBy(s => s.Id).FirstOrDefault();
            if (target is not null && (int)(Draw(rng, week, slot, 0) % 100UL) < Math.Min(TempMaxRaidPercent, monsters * TempRaidPercentPerMonster))
            {
                var guard = force(target.Id);
                var (hit, fallen) = DemoCombat.Clash(Math.Max(1, monsters / 2), level, guard, defending: true, (int)(Draw(rng, week, slot, 3) % 100UL));
                var slain = Math.Min(hit, Room(guard));
                var deaths = Math.Min(Math.Max(1L, fallen), Math.Max(0L, residents(target.Id) - 1));
                left -= slain;
                strikes[target.Id] = new DemoDisasterStrike(target.Id, DemoSettlementEventKind.MonsterRaid, deaths,
                    slain > 0 ? $"{nest.Dungeon.Name}的 {level} 級怪物來襲，{Defenders(guard)}擊殺 {slain} 隻" : $"{nest.Dungeon.Name}的 {level} 級怪物來襲，{Defenders(guard)}只能守住村子");
                kills.Add((target.Id, nest.CellId, slain, $"擊退{nest.Dungeon.Name}的怪物，殺死 {slain} 隻（剩 {left} 隻）"));
            }
            // 討伐：離範圍 TempHuntReach 格內的聚落出兵——有戰鬥人員的先（戰力高的先），沒有才由人多的聚落組民兵。
            var hunter = left <= 0 ? null : living.Where(s => !taken.Contains(s.Id) && !strikes.ContainsKey(s.Id)
                    && Distance(s) <= TempHuntReach && residents(s.Id) >= TempHuntMinResidents
                    && (force(s.Id).Fighters > 0 || left > SuppressFloor(nest.Dungeon)))
                .OrderByDescending(s => force(s.Id).Soldiers * force(s.Id).SoldierLevel + force(s.Id).Adventurers * force(s.Id).AdventurerLevel)
                .ThenByDescending(s => residents(s.Id)).ThenBy(s => s.Id).FirstOrDefault();
            if (hunter is not null && (int)(Draw(rng, week, slot, 1) % 100UL) < TempHuntPercent)
            {
                var party = force(hunter.Id);
                var (hit, fallen) = DemoCombat.Clash(left, level, party, defending: false, (int)(Draw(rng, week, slot, 2) % 100UL));
                var slain = Math.Min(hit, Room(party));
                var deaths = Math.Min(fallen, Math.Max(0L, residents(hunter.Id) - 1));
                left -= slain;
                strikes[hunter.Id] = new DemoDisasterStrike(hunter.Id, DemoSettlementEventKind.MonsterHunt, deaths,
                    $"派{Party(party)}討伐{nest.Dungeon.Name}的 {level} 級怪物，殺死 {slain} 隻");
                kills.Add((hunter.Id, nest.CellId, slain, $"討伐{nest.Dungeon.Name}，殺死 {slain} 隻怪物（剩 {left} 隻）"));
            }
            if (left <= 0)
                cleared.Add(nest); // 第 12-2c 節：只有等級不低於怪物的戰鬥人員殺得光
        }
        return new Plan(strikes, kills, cleared, awakened);
    }

    private static string Defenders(DemoCombatForce f) => f.Fighters > 0 ? $"{Party(f)}帶著村民" : "村民";

    private static string Party(DemoCombatForce f)
    {
        var parts = new List<string>();
        if (f.Soldiers > 0)
            parts.Add($"{f.SoldierLevel} 級士兵 {f.Soldiers} 人");
        if (f.Adventurers > 0)
            parts.Add($"{f.AdventurerLevel} 級冒險者 {f.Adventurers} 人");
        return parts.Count == 0 ? "民兵" : string.Join("與", parts);
    }

    /// <summary>本週怪物的擊殺、沉寂、再生事件（接在本週其他事件之後，編號從 <paramref name="nextEventId"/> 起）。</summary>
    public static IReadOnlyList<DemoSettlementEvent> Events(Plan plan, int week, TimeSpan worldTime, DemoWorldSites sites, long nextEventId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(sites);
        var events = new List<DemoSettlementEvent>();
        string Name(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        void Add(int settlementId, DemoSettlementEventKind kind, long count, string cause)
            => events.Add(new DemoSettlementEvent(week, worldTime, settlementId, Name(settlementId), kind, count, cause + "【TEMP】",
                EventId: checked(nextEventId + events.Count)));
        foreach (var (settlementId, cellId, slain, text) in plan.Kills.Where(k => k.Kills > 0))
            Add(settlementId, DemoSettlementEventKind.MonsterSlain, EncodeKills(cellId, slain), text);
        foreach (var nest in plan.Cleared)
            Add(nest.Dungeon.NearSettlementId, DemoSettlementEventKind.DungeonCleared, nest.CellId, $"{nest.Dungeon.Name}的怪物被殺光，地下城沉寂下來");
        foreach (var nest in plan.Awakened)
            Add(nest.Dungeon.NearSettlementId, DemoSettlementEventKind.DungeonAwakened, nest.CellId, $"沉寂的{nest.Dungeon.Name}又爬出了怪物");
        return events;
    }
}
