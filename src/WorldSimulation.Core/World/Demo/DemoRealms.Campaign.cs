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

public static partial class DemoRealms
{
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
