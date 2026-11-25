namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-45 章：一場征服戰爭的行軍（由開戰週與地圖推得，不另存）。
/// <see cref="Route"/>＝攻方首都到戰爭目標的路線（陸路；陸路不通時直線，例如隔海）；<see cref="MarchWeeks"/>＝走到城下要幾週（沒有地圖＝0）；
/// <see cref="FieldWeek"/>、<see cref="FieldAt"/>＝守方出城迎擊時野戰的週次與地點（0＝不行軍）。
/// </summary>
public sealed record DemoCampaign(IReadOnlyList<DemoCellRef> Route, int MarchWeeks, int FieldWeek, DemoCellRef FieldAt)
{
    public static DemoCampaign None { get; } = new(Array.Empty<DemoCellRef>(), 0, 0, default);

    /// <summary>攻方軍隊在第 <paramref name="week"/> 週所在的格（行軍中沿路線、到了就在城下）。</summary>
    public DemoCellRef ArmyAt(int startWeek, int week)
        => Route.Count == 0 ? default : Route[Math.Clamp((week - startWeek) * DemoRealms.TempMarchCellsPerWeek, 0, Route.Count - 1)];
}

/// <summary>第 08-46 章：戰爭進行到哪一步（行軍中、圍城中、佔領完畢守著）。</summary>
public enum DemoWarStage
{
    Marching,
    Besieging,
    Holding
}

/// <summary>
/// 第 08-46 章：某一週的戰況（畫面與詳情用，由戰爭與地圖推得）：<see cref="ArmyAt"/>＝攻方軍隊所在格；
/// <see cref="Besieged"/>＝正在圍的聚落（0＝沒有）；<see cref="SiegeDone"/>／<see cref="SiegeNeed"/>＝圍了幾週、要幾週；
/// <see cref="MarchLeft"/>＝還要走幾週到城下。
/// </summary>
public sealed record DemoWarStatus(DemoWarStage Stage, DemoCampaign Campaign, DemoCellRef ArmyAt, int Besieged, int SiegeDone, int SiegeNeed, int MarchLeft);

public static partial class DemoRealms
{
    /// <summary>第 08-45 章：圍下一座要幾週（層級＋城牆，至少 2 週）。</summary>
    public static int SiegeNeed(int settlementId, Func<int, DemoSettlementTier> tier, Func<int, DemoDefense>? defense)
    {
        ArgumentNullException.ThrowIfNull(tier);
        return Math.Max(2, TempSiegeWeeks(tier(settlementId)) + (defense?.Invoke(settlementId).SiegeWeeks ?? 0));
    }

    /// <summary>第 08-46 章：第 <paramref name="week"/> 週的戰況（規則與 <see cref="Battles"/>、圍城相同）。</summary>
    public static DemoWarStatus Status(DemoWar war, int week, DemoHexWorld? map, DemoWorldSites sites, IReadOnlyDictionary<int, int> lieges,
        Func<int, bool> alive, Func<int, DemoSettlementTier> tier, Func<int, DemoDefense>? defense = null)
    {
        ArgumentNullException.ThrowIfNull(war);
        ArgumentNullException.ThrowIfNull(sites);
        var campaign = Campaign(war, map, sites);
        var arrive = war.StartWeek + campaign.MarchWeeks;
        DemoCellRef Cell(int id) => sites.Settlements.FirstOrDefault(s => s.Id == id) is { } s ? new DemoCellRef(s.Column, s.Row) : default;
        if (week < arrive)
            return new DemoWarStatus(DemoWarStage.Marching, campaign, campaign.ArmyAt(war.StartWeek, week), war.TargetId, 0,
                SiegeNeed(war.TargetId, tier, defense), arrive - week);
        var besieged = NextHolding(war, sites, lieges, alive);
        if (besieged == 0)
            return new DemoWarStatus(DemoWarStage.Holding, campaign, Cell(war.TargetId), 0, 0, 0, 0);
        var since = besieged == war.TargetId ? arrive : Math.Max(arrive, war.LastOccupiedWeek);
        var need = SiegeNeed(besieged, tier, defense);
        return new DemoWarStatus(DemoWarStage.Besieging, campaign, Cell(besieged), besieged, Math.Clamp(week - since, 0, need), need, 0);
    }

    /// <summary>
    /// 【TEMP】第 08-45 章（使用者 2026-11-25：戰爭的表現還有加強的空間；參考 CK3 的行軍、野戰與圍城）：
    /// 軍隊每週走 <see cref="TempMarchCellsPerWeek"/> 格；守方兵力（含本土加成）至少攻方的 <see cref="TempSallyPercent"/>% 就在半路出城迎擊，
    /// 野戰雙方死傷 ×<see cref="TempFieldBattlePercent"/>%；攻下戰爭目標後，接著圍攻守方在目標 <see cref="TempOccupyRange"/> 格內的聚落，
    /// 最多再佔 <see cref="TempMaxHoldings"/> 座，每座戰爭分數 +<see cref="TempHoldingScore"/>（議和只割讓戰爭目標，其餘歸還）。
    /// </summary>
    public const int TempMarchCellsPerWeek = 3, TempSallyPercent = 70, TempFieldBattlePercent = 200, TempOccupyRange = 4, TempMaxHoldings = 3, TempHoldingScore = 10;

    /// <summary>第 08-45 章：這場戰爭的行軍（沒有地圖或找不到雙方＝<see cref="DemoCampaign.None"/>）。</summary>
    public static DemoCampaign Campaign(DemoWar war, DemoHexWorld? map, DemoWorldSites sites)
    {
        ArgumentNullException.ThrowIfNull(war);
        ArgumentNullException.ThrowIfNull(sites);
        if (map is null || sites.Settlements.FirstOrDefault(s => s.Id == war.AttackerId) is not { } from
            || sites.Settlements.FirstOrDefault(s => s.Id == war.TargetId) is not { } to)
            return DemoCampaign.None;
        var route = WarRoute(map, new DemoCellRef(from.Column, from.Row), new DemoCellRef(to.Column, to.Row));
        if (route.Count < 2)
            return DemoCampaign.None;
        var march = Math.Max(1, (route.Count - 1 + TempMarchCellsPerWeek - 1) / TempMarchCellsPerWeek);
        var half = (march + 1) / 2;
        return new DemoCampaign(route, march, war.StartWeek + half, route[Math.Min(route.Count - 1, half * TempMarchCellsPerWeek)]);
    }

    /// <summary>第 08-45 章：行軍路線——陸路（與遷徙同一套路線段）；陸路不通（隔海、隔河無渡口）時取直線。</summary>
    public static IReadOnlyList<DemoCellRef> WarRoute(DemoHexWorld map, DemoCellRef from, DemoCellRef to)
    {
        ArgumentNullException.ThrowIfNull(map);
        if (from == to)
            return new[] { from };
        var land = DemoMigrationRoutes.FindRoute(map, from, to);
        if (land.Length >= 2)
            return land;
        var steps = DemoHexWorld.Distance(from, to);
        var line = new List<DemoCellRef>(steps + 1);
        for (var i = 0; i <= steps; i++)
            line.Add(new DemoCellRef(
                (int)Math.Round(from.Column + (to.Column - from.Column) * (double)i / steps, MidpointRounding.AwayFromZero),
                (int)Math.Round(from.Row + (to.Row - from.Row) * (double)i / steps, MidpointRounding.AwayFromZero)));
        return line;
    }

    /// <summary>
    /// 第 08-45 章：攻方現在圍攻的聚落——戰爭目標還沒攻下就是目標；攻下後是守方在目標 <see cref="TempOccupyRange"/> 格內、還沒佔的最近一座
    /// （已佔滿 <see cref="TempMaxHoldings"/> 座或沒有了＝0）。
    /// </summary>
    public static int NextHolding(DemoWar war, DemoWorldSites sites, IReadOnlyDictionary<int, int> lieges, Func<int, bool> alive)
    {
        ArgumentNullException.ThrowIfNull(war);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(lieges);
        ArgumentNullException.ThrowIfNull(alive);
        if (!war.Occupied)
            return alive(war.TargetId) ? war.TargetId : 0;
        if (war.Holdings.Count >= TempMaxHoldings || sites.Settlements.FirstOrDefault(s => s.Id == war.TargetId) is not { } target)
            return 0;
        var at = new DemoCellRef(target.Column, target.Row);
        return sites.Settlements
            .Where(s => !s.IsRuin && s.Id != war.TargetId && alive(s.Id) && !war.Holdings.Contains(s.Id) && Top(s.Id, lieges) == war.DefenderId)
            .Select(s => (s.Id, Distance: DemoHexWorld.Distance(at, new DemoCellRef(s.Column, s.Row))))
            .Where(x => x.Distance <= TempOccupyRange)
            .OrderBy(x => x.Distance).ThenBy(x => x.Id)
            .Select(x => x.Id).FirstOrDefault();
    }
}
