using System.Collections.Immutable;

namespace WorldSimulation.Core.World.Demo;

/// <summary>TEMP 示範地圖上的一格（偏移座標）。</summary>
public readonly record struct DemoCellRef(int Column, int Row)
{
    public override string ToString() => $"({Column},{Row})";
}

/// <summary>
/// V0-06A 一支移動中的遷徙隊伍（45E MIG_LEDGER：原子出發＋稍後抵達；途中人口只計一次）。
/// Id 是示範用遞增序號，永不重用（不是正式 EntityID）。Path 是出發時決定的路線，
/// 第 0 格是來源聚落、最後一格是目的地聚落；StepIndex 是目前所在格。
/// 隊伍攜帶的糧食與飢餓債隨人移動，不會憑空增減。
/// NewsWeek 是出發決定所依據的目的地週報週次（MIG_PULL 稽核用；0＝測試直接建立、無消息來源）。
/// </summary>
public sealed record DemoMigrationBatch(
    int Id,
    int FromSettlementId,
    string FromName,
    int ToSettlementId,
    string ToName,
    ImmutableArray<DemoCellRef> Path,
    int StepIndex,
    long People,
    long Food,
    long HungerDebt,
    int DepartedWeek,
    int NewsWeek = 0)
{
    /// <summary>目前所在格。</summary>
    public DemoCellRef Cell => Path[StepIndex];

    /// <summary>還要走幾格才抵達。</summary>
    public int StepsLeft => Path.Length - 1 - StepIndex;

    /// <summary>決定性的文字指紋（ImmutableArray 的 ToString 不含內容，所以另外展開路線）。</summary>
    internal string Fingerprint()
        => $"batch{Id}:{FromSettlementId}->{ToSettlementId}@{StepIndex}/{string.Join("", Path)}"
         + $":p{People}:f{Food}:d{HungerDebt}:w{DepartedWeek}:n{NewsWeek}";
}

/// <summary>
/// V0-06A 一支遷徙隊伍一週的帳目（純量，可直接比較相等）。
/// 出發週（Departed）只記錄從來源扣出的人口／糧食／飢餓債，隊伍停在來源格、本週不再消耗。
/// 途中週：糧食帳 期初－消耗＝期末；飢餓債帳 期初＋缺糧－恢復－隨死者移除＝期末；
/// 人口帳 期初－死亡＝期末（途中沒有出生）。
/// M1_MIG：每週只能停在原格或移動到相鄰格（CellBefore→CellAfter）。
/// </summary>
public sealed record DemoMigrationWeekLedger(
    int Week,
    int BatchId,
    int FromSettlementId,
    int ToSettlementId,
    DemoCellRef CellBefore,
    DemoCellRef CellAfter,
    bool Departed,
    bool Arrived,
    long PopulationOpening,
    long Deaths,
    long PopulationClosing,
    long FoodOpening,
    long FoodConsumed,
    long FoodClosing,
    long FoodShortage,
    long HungerDebtOpening,
    long HungerDebtRecovered,
    long HungerDebtRemovedByDeath,
    long HungerDebtClosing)
{
    public bool PopulationBalanced => PopulationOpening - Deaths == PopulationClosing;

    public bool FoodBalanced => FoodOpening - FoodConsumed == FoodClosing;

    public bool HungerDebtBalanced
        => HungerDebtOpening + FoodShortage - HungerDebtRecovered - HungerDebtRemovedByDeath == HungerDebtClosing
        && (Deaths == 0 || HungerDebtRemovedByDeath > 0)
        && (PopulationClosing > 0 || HungerDebtClosing == 0);

    /// <summary>M1_MIG：不瞬移——原地或相鄰格；出發週必須停在原地；全數死亡的隊伍不再移動也不會抵達。</summary>
    public bool NoTeleport
        => (CellBefore == CellAfter || DemoMigrationRoutes.AreAdjacent(CellBefore, CellAfter))
        && (!Departed || (CellBefore == CellAfter && !Arrived && Deaths == 0 && FoodConsumed == 0))
        && (!Arrived || PopulationClosing > 0);

    public bool NonNegative
        => PopulationOpening >= 0 && Deaths >= 0 && PopulationClosing >= 0
        && FoodOpening >= 0 && FoodConsumed >= 0 && FoodClosing >= 0 && FoodShortage >= 0
        && HungerDebtOpening >= 0 && HungerDebtRecovered >= 0 && HungerDebtRemovedByDeath >= 0
        && HungerDebtClosing >= 0;

    public bool IsBalanced
        => PopulationBalanced && FoodBalanced && HungerDebtBalanced && NoTeleport && NonNegative;
}

/// <summary>
/// V0-06A 補修（#61）45E MIG_PULL 的 TEMP 消息模型：聚落不是全知的。
/// 一個聚落的週報消息沿 TEMP 陸路（與 <see cref="DemoMigrationRoutes"/> 同一條路）傳遞，
/// 每格需要 TEMP <c>NewsDelayWeeksPerCell</c> 週；所以第 W 週、路程 d 格的來源，
/// 只收得到目的地第 W－d×延遲 週的已發布週報，更新的週報還在路上。沒有陸路就沒有消息。
/// 純函式、不抽亂數。這是暫代規則，正式的消息／情報傳播未定案。
/// </summary>
public static class DemoMigrationNews
{
    /// <summary>來源在第 <paramref name="week"/> 週能收到的最新消息週次（可能 ≤ 0＝還沒有任何消息）。</summary>
    public static int NewsWeekFor(int week, int routeSteps, int delayWeeksPerCell)
    {
        if (routeSteps < 1 || delayWeeksPerCell < 1)
            throw new ArgumentOutOfRangeException(nameof(routeSteps), "News needs at least one cell and one week per cell.");
        return week - checked(routeSteps * delayWeeksPerCell);
    }

    /// <summary>
    /// 從已發布的週報中找出已傳到來源的那一週、關於 <paramref name="settlementId"/> 的帳目；
    /// 還沒有任何消息傳到時回傳 null。只看 <paramref name="publishedWeeks"/>，不讀世界目前狀態。
    /// </summary>
    public static (int Week, DemoSettlementWeekLedger Ledger)? Received(
        IReadOnlyList<DemoWeekReport> publishedWeeks, int week, int settlementId, int routeSteps, int delayWeeksPerCell)
    {
        ArgumentNullException.ThrowIfNull(publishedWeeks);
        var newsWeek = NewsWeekFor(week, routeSteps, delayWeeksPerCell);
        if (newsWeek < 1)
            return null;
        // 已發布週報依週次 1、2、3… 排列，先用索引直接取；不符時才逐一尋找。
        var report = publishedWeeks.Count >= newsWeek && publishedWeeks[newsWeek - 1].Week == newsWeek
            ? publishedWeeks[newsWeek - 1]
            : publishedWeeks.FirstOrDefault(w => w.Week == newsWeek);
        var ledger = report?.Settlements.FirstOrDefault(l => l.SettlementId == settlementId);
        if (ledger is null)
            return null;
        return (newsWeek, ledger);
    }

    /// <summary>消息中的聚落是否「有糧」：仍有人、當週沒有缺糧、沒有飢餓債。</summary>
    public static bool SaysFed(DemoSettlementWeekLedger ledger)
        => ledger.PopulationClosing > 0 && ledger.FoodShortage == 0 && ledger.HungerDebtClosing == 0;
}

/// <summary>
/// V0-06A TEMP 路線：在 TEMP 六角格上找出發聚落到目的聚落的最短陸路。
/// 這是暫代規則，不是正式的 RouteSegment 路網（02 C：畫面相鄰不代表連通，正式路網未定案）：
/// 相鄰＝六角格六個鄰格；水域格不可通行（沒有橋梁／渡口資料），只有起點與終點聚落所在格例外。
/// 純函式、不抽亂數；鄰格依固定順序展開，所以同一張地圖永遠得到同一條路線。
/// </summary>
public static class DemoMigrationRoutes
{
    // 軸向座標的六個方向（固定順序 = 決定性）。
    private static readonly (int Dq, int Dr)[] Directions =
    {
        (1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1)
    };

    /// <summary>TEMP odd-r 偏移座標→軸向座標（與 DemoHexWorld 相同的換算）。</summary>
    public static (int Q, int R) ToAxial(DemoCellRef cell)
        => (cell.Column - (cell.Row - (cell.Row & 1)) / 2, cell.Row);

    /// <summary>兩格是否為六角格鄰格（不含同一格）。</summary>
    public static bool AreAdjacent(DemoCellRef a, DemoCellRef b)
    {
        var (aq, ar) = ToAxial(a);
        var (bq, br) = ToAxial(b);
        var dq = bq - aq;
        var dr = br - ar;
        return Directions.Contains((dq, dr));
    }

    /// <summary>
    /// 最短陸路（含起點與終點），找不到則回傳空陣列（45E MIG_NO_TARGET：沒有可達目的地就留下）。
    /// </summary>
    public static ImmutableArray<DemoCellRef> FindRoute(DemoHexWorld map, DemoCellRef origin, DemoCellRef destination)
    {
        ArgumentNullException.ThrowIfNull(map);
        var start = map.GetCell(origin.Column, origin.Row);
        var goal = map.GetCell(destination.Column, destination.Row);
        if (origin == destination)
            return ImmutableArray.Create(origin);

        var byAxial = map.Cells.ToDictionary(c => (c.AxialQ, c.AxialR));
        var previous = new Dictionary<DemoCellRef, DemoCellRef?> { [origin] = null };
        var queue = new Queue<DemoHexCell>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            if (ReferenceEquals(cell, goal))
                break;

            foreach (var (dq, dr) in Directions)
            {
                if (!byAxial.TryGetValue((cell.AxialQ + dq, cell.AxialR + dr), out var next))
                    continue;
                var key = new DemoCellRef(next.Column, next.Row);
                if (previous.ContainsKey(key))
                    continue;
                if (next.Terrain == DemoTerrain.Water && !ReferenceEquals(next, goal))
                    continue;
                previous[key] = new DemoCellRef(cell.Column, cell.Row);
                queue.Enqueue(next);
            }
        }

        if (!previous.ContainsKey(destination))
            return ImmutableArray<DemoCellRef>.Empty;

        var path = new List<DemoCellRef>();
        DemoCellRef? at = destination;
        while (at is { } current)
        {
            path.Add(current);
            at = previous[current];
        }

        path.Reverse();
        return path.ToImmutableArray();
    }
}
