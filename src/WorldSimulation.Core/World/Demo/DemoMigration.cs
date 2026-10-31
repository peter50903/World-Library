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
/// V0-06B：Founding＝true 的隊伍是開拓隊，目的地是一個空格（ToSettlementId＝0，路線最後一格），
/// 抵達時以隊伍倖存者建立新聚落（45F SETTLE_NEW）；全數死於途中則開拓失敗（SETTLE_FAIL），不留下聚落。
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
    int NewsWeek = 0,
    bool Founding = false)
{
    /// <summary>目前所在格。</summary>
    public DemoCellRef Cell => Path[StepIndex];

    /// <summary>還要走幾格才抵達。</summary>
    public int StepsLeft => Path.Length - 1 - StepIndex;

    /// <summary>決定性的文字指紋（ImmutableArray 的 ToString 不含內容，所以另外展開路線）。</summary>
    internal string Fingerprint()
        => $"batch{Id}:{FromSettlementId}->{ToSettlementId}@{StepIndex}/{string.Join("", Path)}"
         + $":p{People}:f{Food}:d{HungerDebt}:w{DepartedWeek}:n{NewsWeek}:found{Founding}";

    /// <summary>V0-06B：開拓隊的目的格（路線最後一格）。</summary>
    public DemoCellRef Destination => Path[Path.Length - 1];
}

/// <summary>
/// V0-06A 一支遷徙隊伍一週的帳目（純量，可直接比較相等）。
/// 出發週（Departed）只記錄從來源扣出的人口／糧食／飢餓債，隊伍停在來源格、本週不再消耗。
/// 途中週：糧食帳 期初－消耗＝期末；飢餓債帳 期初＋缺糧－恢復－隨死者移除＝期末；
/// 人口帳 期初－死亡＝期末（途中沒有出生）。
/// M1_MIG：每週只能停在原格或移動到相鄰格（CellBefore→CellAfter）。
/// V0-06B：Founding＝true 是開拓隊；途中 ToSettlementId＝0（目的地還不是聚落），
/// 抵達那一週 ToSettlementId＝它建立的新聚落編號。
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
    long HungerDebtClosing,
    bool Founding = false,
    long Births = 0,
    long NaturalDeaths = 0,
    long HungerDebtRemovedByNaturalDeath = 0,
    bool MateSeeking = false,
    long AttackDeaths = 0)
{
    /// <summary>
    /// 第 07 章 b 節：途中也可能分娩或自然死亡（Deaths 只算途中餓死）。
    /// 第 12-1 章 C2：<see cref="AttackDeaths"/>＝途中被野獸襲擊而死（不含在 Deaths 內、不減飢餓債）。
    /// </summary>
    public bool PopulationBalanced => PopulationOpening + Births - Deaths - NaturalDeaths - AttackDeaths == PopulationClosing;

    public bool FoodBalanced => FoodOpening - FoodConsumed == FoodClosing;

    public bool HungerDebtBalanced
        => HungerDebtOpening + FoodShortage - HungerDebtRecovered - HungerDebtRemovedByDeath
            - HungerDebtRemovedByNaturalDeath == HungerDebtClosing
        && (Deaths == 0 || HungerDebtRemovedByDeath > 0)
        && (PopulationClosing > 0 || HungerDebtClosing == 0);

    /// <summary>M1_MIG：不瞬移——原地或相鄰格；出發週必須停在原地；全數死亡的隊伍不再移動也不會抵達。</summary>
    public bool NoTeleport
        => (CellBefore == CellAfter || DemoMigrationRoutes.AreAdjacent(CellBefore, CellAfter))
        && (!Departed || (CellBefore == CellAfter && !Arrived && Deaths == 0 && AttackDeaths == 0 && FoodConsumed == 0))
        && (!Arrived || PopulationClosing > 0);

    public bool NonNegative
        => PopulationOpening >= 0 && Deaths >= 0 && PopulationClosing >= 0
        && FoodOpening >= 0 && FoodConsumed >= 0 && FoodClosing >= 0 && FoodShortage >= 0
        && HungerDebtOpening >= 0 && HungerDebtRecovered >= 0 && HungerDebtRemovedByDeath >= 0
        && HungerDebtClosing >= 0 && Births >= 0 && NaturalDeaths >= 0 && HungerDebtRemovedByNaturalDeath >= 0 && AttackDeaths >= 0
        && (Arrived ? Births == 0 && NaturalDeaths == 0 : true); // 抵達週的成員已併入目的聚落，生死記在聚落帳

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

    /// <summary>六個軸向方向（固定順序）。</summary>
    internal static IReadOnlyList<(int Dq, int Dr)> AxialDirections => Directions;

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
    /// 第 08-11 章【TEMP】可住的格：離水 2 格內；或聚落已會掘井（<paramref name="wells"/>）時任何陸地。
    /// </summary>
    public static bool Habitable(DemoHexWorld map, DemoCellRef cell, bool wells = false)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.Contains(cell.Column, cell.Row) && map.GetCell(cell.Column, cell.Row).Terrain != DemoTerrain.Water
            && (wells || map.IsNearWater(cell));
    }

    /// <summary>【TEMP】第 08-11 章：每名取水者供應的人數——鄰水或已掘井 10 人；離水 1 格（要挑水）5 人；離水更遠又沒有井＝0（取不到水）。</summary>
    public static long PeoplePerCarrier(DemoHexWorld map, DemoCellRef cell, bool wells, long normal)
        => HasWaterAccess(map, cell) || wells ? normal : map.IsNearWater(cell) ? Math.Max(1, normal / TempCarryWaterDivisor) : 0;

    /// <summary>
    /// 第 08-11 章：本週取水者人數與取到水的人數（週結算與讀檔驗證共用）。
    /// 【TEMP】每滿一份負擔量（<see cref="PeoplePerCarrier"/>）才需要一名專職取水者（不採集）；不滿一份的零頭由大家順手取水。
    /// 遺跡成因資料：原本無條件進位，2 人的新聚落一半勞力去取水、只剩 1 人時沒有人採集而餓死。
    /// </summary>
    public static (long Carriers, long Served) WaterPlan(DemoHexWorld map, DemoCellRef cell, bool wells, long people, long adults)
    {
        var per = PeoplePerCarrier(map, cell, wells, DemoSurvivalTempSettings.Current.PeoplePerWaterCarrier);
        if (per <= 0 || people <= 0)
            return (0, 0);
        var needed = people / per;
        var carriers = Math.Min(adults, needed);
        return (carriers, carriers == needed ? people : Math.Min(people, checked(carriers * per)));
    }

    /// <summary>【TEMP】挑水時取水者要加倍（每名取水者供應的人數÷2）。</summary>
    public const long TempCarryWaterDivisor = 2;

    /// <summary>TEMP 水源：格子本身不是水域，且至少一個六角鄰格是水域（45F SETTLE_NEW 的「水」）。</summary>
    public static bool HasWaterAccess(DemoHexWorld map, DemoCellRef cell)
    {
        ArgumentNullException.ThrowIfNull(map);
        // 第 07-1 章 c 節：建圖時預先算好（原本每次掃全圖）；地圖外的格一律沒有水源。
        if (map.Contains(cell.Column, cell.Row))
            return map.HasWaterAccess(cell);
        var (q, r) = ToAxial(cell);
        return map.Cells.Any(c => c.Terrain == DemoTerrain.Water && Directions.Contains((c.AxialQ - q, c.AxialR - r)));
    }

    /// <summary>
    /// V0-06B TEMP 開拓選址（45F SETTLE_NEW／SETTLE_KNOW）：從 <paramref name="origin"/> 沿陸路
    /// （與遷徙同一套 TEMP 規則：六鄰格、水域不可通行）走最多 <paramref name="maxSteps"/> 格，
    /// 找第一個「陸地、沒有聚落、<paramref name="isFree"/> 允許、有 TEMP 水源」的空格；
    /// 回傳含起點與終點的路線，找不到回傳空陣列。只看出發聚落附近 TEMP 範圍內的地形（在地人知道的土地），
    /// 不全知地挑全世界最好的地點。廣度優先、鄰格固定順序，所以結果是決定性的；不抽亂數。
    /// </summary>
    public static ImmutableArray<DemoCellRef> FindFoundingSite(
        DemoHexWorld map, DemoCellRef origin, int maxSteps, Func<DemoCellRef, bool> isFree,
        Func<DemoCellRef, long>? score = null, bool wells = false)
    {
        // 第 08-1 章：給了 score 時，走遍 maxSteps 內全部合格空地，挑分數最高者（同分取先找到的、較近的）。
        ImmutableArray<DemoCellRef>? best = null;
        var bestScore = long.MinValue;
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(isFree);
        if (maxSteps < 1)
            throw new ArgumentOutOfRangeException(nameof(maxSteps));
        var start = map.GetCell(origin.Column, origin.Row);
        var previous = new Dictionary<DemoCellRef, DemoCellRef?> { [origin] = null };
        var depth = new Dictionary<DemoCellRef, int> { [origin] = 0 };
        var queue = new Queue<DemoHexCell>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            var here = new DemoCellRef(cell.Column, cell.Row);
            if (depth[here] > 0 && cell.Terrain != DemoTerrain.Water && isFree(here) && Habitable(map, here, wells)) // 第 08-11 章：離水 2 格內；會掘井則任何陸地
            {
                var path = new List<DemoCellRef>();
                DemoCellRef? at = here;
                while (at is { } current)
                {
                    path.Add(current);
                    at = previous[current];
                }

                path.Reverse();
                if (score is null)
                    return path.ToImmutableArray();
                var value = score(here);
                if (value > bestScore)
                {
                    bestScore = value;
                    best = path.ToImmutableArray();
                }
            }

            if (depth[here] == maxSteps)
                continue;
            foreach (var (dq, dr) in Directions)
            {
                if (map.AtAxial(cell.AxialQ + dq, cell.AxialR + dr) is not { } next)
                    continue;
                var key = new DemoCellRef(next.Column, next.Row);
                // 第 06 章 a 節：只沿陸路路線段（野地／道路／橋／渡口）走；相鄰但沒有路線段＝不可通行。
                if (previous.ContainsKey(key) || !map.Routes.IsLandPassable(here, key))
                    continue;
                previous[key] = here;
                depth[key] = depth[here] + 1;
                queue.Enqueue(next);
            }
        }

        return best ?? ImmutableArray<DemoCellRef>.Empty;
    }

    /// <summary>
    /// 最短陸路（含起點與終點，依路線段步數），找不到則回傳空陣列（45E MIG_NO_TARGET：沒有可達目的地就留下）。
    /// 第 06 章 a 節：只沿陸路路線段走，過河只能經渡口。
    /// </summary>
    public static ImmutableArray<DemoCellRef> FindRoute(DemoHexWorld map, DemoCellRef origin, DemoCellRef destination)
    {
        ArgumentNullException.ThrowIfNull(map);
        _ = map.GetCell(origin.Column, origin.Row);
        _ = map.GetCell(destination.Column, destination.Row);
        if (origin == destination)
            return ImmutableArray.Create(origin);

        // 第 07-1 章 c 節：用地圖快取的起點廣度優先樹（與逐次搜尋同一棵樹：鄰格固定順序、先到先記）。
        var previous = map.LandTree(origin);

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
