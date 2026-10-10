namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #88：M1 示範的歷史因果索引（43F CAUSE_PARENT／EFFECT_CHILD／REL_TIME 的最小切片）。
/// 每筆聚落事件只存「直接原因事件編號」（<see cref="DemoSettlementEvent.CauseEventId"/>），
/// 直接後果由本索引反向建立，不另存（43F：只存直接邊，可由父項反向索引建立）。
///
/// 本類別同時是因果證據的檢查器：<see cref="Problems"/> 列出所有不合法之處，
/// 例如編號不連續、原因指向未來或不存在的事件、原因種類或聚落／隊伍不符、
/// 以及與週帳不符的事件（沒有缺糧的飢餓、沒有出發帳的外移）。M1_HISTORY 與 TEMP 讀檔都用它。
///
/// 限制（TEMP）：M1 示範每筆事件至多一個直接原因；正式 43F 允許 1～N 個直接原因，
/// 完整歷史核心（阿卡西紀錄）尚未實作。
/// </summary>
public sealed class DemoCausalIndex
{
    private readonly List<DemoSettlementEvent> _ordered = new();
    private readonly Dictionary<long, DemoSettlementEvent> _byId = new();
    private readonly Dictionary<long, List<DemoSettlementEvent>> _children = new();
    private readonly List<string> _problems = new();

    private DemoCausalIndex()
    {
    }

    /// <summary>全部事件，依發生順序（＝編號順序）。</summary>
    public IReadOnlyList<DemoSettlementEvent> Events => _ordered;

    /// <summary>所有因果證據問題（繁體中文）；空清單＝全部合法。</summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>依編號找事件；找不到回傳 null。</summary>
    public DemoSettlementEvent? Find(long eventId) => _byId.GetValueOrDefault(eventId);

    /// <summary>43F CAUSE_PARENT：直接原因事件；根因回傳 null。</summary>
    public DemoSettlementEvent? ParentOf(DemoSettlementEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return e.CauseEventId == 0 ? null : Find(e.CauseEventId);
    }

    /// <summary>43F EFFECT_CHILD：由直接原因反向索引得到的直接後果事件（依編號排序）。</summary>
    public IReadOnlyList<DemoSettlementEvent> ChildrenOf(DemoSettlementEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        return _children.TryGetValue(e.EventId, out var list) ? list : Array.Empty<DemoSettlementEvent>();
    }

    /// <summary>沿直接原因往回走到根因，回傳「根因 → … → 此事件」的鏈。</summary>
    public IReadOnlyList<DemoSettlementEvent> ChainTo(DemoSettlementEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var chain = new List<DemoSettlementEvent> { e };
        var current = e;
        // 原因編號必小於自身（已檢查），所以最多走事件總數步。
        for (var guard = 0; guard < _ordered.Count; guard++)
        {
            var parent = ParentOf(current);
            if (parent is null || parent.EventId >= current.EventId)
                break;
            chain.Add(parent);
            current = parent;
        }

        chain.Reverse();
        return chain;
    }

    /// <summary>從週報建立索引並逐筆檢查因果證據。</summary>
    public static DemoCausalIndex Build(IEnumerable<DemoWeekReport> weeks)
    {
        ArgumentNullException.ThrowIfNull(weeks);
        var index = new DemoCausalIndex();
        var reports = weeks.ToList();
        long expected = 1;
        foreach (var report in reports)
        {
            foreach (var e in report.Events)
            {
                if (e.EventId != expected)
                    index._problems.Add($"第 {report.Week} 週 {e.SettlementName} 的 {KindText(e.Kind)} 事件編號是 #{e.EventId}，應為 #{expected}（事件編號必須從 1 起連續遞增、不重用）");
                expected = Math.Max(expected, e.EventId) + 1;
                index._ordered.Add(e);
                if (e.EventId > 0)
                    index._byId.TryAdd(e.EventId, e);
            }
        }

        foreach (var report in reports)
        {
            foreach (var e in report.Events)
            {
                var problem = index.CheckOne(report, e, reports);
                if (problem is not null)
                    index._problems.Add($"第 {report.Week} 週 {e.SettlementName} 的 {KindText(e.Kind)} 事件 #{e.EventId}：{problem}");
                else if (index.ParentOf(e) is { } parent)
                {
                    if (!index._children.TryGetValue(parent.EventId, out var list))
                        index._children[parent.EventId] = list = new List<DemoSettlementEvent>();
                    list.Add(e);
                }
            }
        }

        return index;
    }

    private string? CheckOne(DemoWeekReport report, DemoSettlementEvent e, IReadOnlyList<DemoWeekReport> reports)
    {
        if (e.CauseEventId < 0)
            return "直接原因編號不可為負";
        if (e.CauseEventId != 0 && e.CauseEventId >= e.EventId)
            return $"直接原因 #{e.CauseEventId} 不早於此事件（原因不能來自未來）";
        var parent = ParentOf(e);
        if (e.CauseEventId != 0 && parent is null)
            return $"直接原因 #{e.CauseEventId} 不存在";
        if (parent is not null && parent.WorldTime > e.WorldTime)
            return $"直接原因 #{parent.EventId} 的世界時間晚於此事件";

        switch (e.Kind)
        {
            case DemoSettlementEventKind.Hunger:
            {
                if (parent is not null)
                    return "飢餓是根因（本週需求大於可取得），不應有直接原因事件";
                var ledger = report.Settlements.SingleOrDefault(l => l.SettlementId == e.SettlementId);
                if (ledger is null || ledger.FoodShortage <= 0 || ledger.FoodShortage != e.Count)
                    return $"週帳沒有對應的缺糧（事件缺糧 {e.Count}，週帳 {ledger?.FoodShortage.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "無"}）";
                return null;
            }

            case DemoSettlementEventKind.Emigration:
            {
                if (parent is null || parent.Kind != DemoSettlementEventKind.Hunger
                    || parent.SettlementId != e.SettlementId || parent.Week != e.Week)
                    return "外移的直接原因必須是同一聚落同一週的飢餓事件";
                var departed = report.Migrations.SingleOrDefault(m => m.BatchId == e.MigrationBatchId && m.Departed);
                if (departed is null || departed.FromSettlementId != e.SettlementId || departed.PopulationOpening != e.Count)
                    return $"週帳沒有對應的隊伍 #{e.MigrationBatchId} 出發紀錄（人數 {e.Count}）";
                return null;
            }

            case DemoSettlementEventKind.Arrival:
            case DemoSettlementEventKind.SettlementFounded:
            case DemoSettlementEventKind.MigrantStarvationDeath:
            {
                if (parent is null || parent.Kind != DemoSettlementEventKind.Emigration
                    || parent.MigrationBatchId != e.MigrationBatchId || parent.Week >= e.Week)
                    return $"直接原因必須是隊伍 #{e.MigrationBatchId} 在更早週次的外移事件";
                if (e.Kind == DemoSettlementEventKind.MigrantStarvationDeath && parent.SettlementId != e.SettlementId)
                    return "途中餓死事件的聚落必須是隊伍的出發聚落";
                return null;
            }

            case DemoSettlementEventKind.StarvationDeath:
            case DemoSettlementEventKind.HungerRecovered:
            {
                if (parent is null || parent.SettlementId != e.SettlementId)
                    return "直接原因必須是同一聚落帶來飢餓債的事件（飢餓，或帶著飢餓債抵達／建立）";
                if (parent.Kind == DemoSettlementEventKind.Hunger)
                    return parent.Week <= e.Week ? null : "原因週次晚於此事件";
                if (parent.Kind is DemoSettlementEventKind.Arrival or DemoSettlementEventKind.SettlementFounded
                    && parent.Week < e.Week && CarriedDebt(reports, parent))
                    return null;
                return "直接原因必須是同一聚落帶來飢餓債的事件（飢餓，或更早週次帶著飢餓債抵達／建立）";
            }

            default:
                return "未知的事件種類";
        }
    }

    private static bool CarriedDebt(IReadOnlyList<DemoWeekReport> reports, DemoSettlementEvent arrival)
    {
        var report = FindWeek(reports, arrival.Week);
        var ledger = report?.Migrations.SingleOrDefault(m => m.BatchId == arrival.MigrationBatchId && m.Arrived);
        return ledger is not null && ledger.HungerDebtClosing > 0;
    }

    private static DemoWeekReport? FindWeek(IReadOnlyList<DemoWeekReport> reports, int week)
    {
        if (week >= 1 && week <= reports.Count && reports[week - 1].Week == week)
            return reports[week - 1];
        for (var i = reports.Count - 1; i >= 0; i--)
        {
            if (reports[i].Week == week)
                return reports[i];
        }

        return null;
    }

    // ── 結算時用：只看已發布的週報（MIG_PULL 同原則），不讀未來。 ──

    /// <summary>下一個事件編號＝最後一筆已發布事件編號＋1（沒有事件時為 1）。</summary>
    internal static long NextEventId(IReadOnlyList<DemoWeekReport> published)
    {
        for (var i = published.Count - 1; i >= 0; i--)
        {
            var events = published[i].Events;
            if (events.Count > 0)
                return Math.Max(1, checked(events[events.Count - 1].EventId + 1));
        }

        return 1;
    }

    /// <summary>隊伍出發時的外移事件編號；找不到（只在人工測試輸入時）回傳 0。</summary>
    internal static long EmigrationEventId(IReadOnlyList<DemoWeekReport> published, DemoMigrationBatch batch)
    {
        var report = FindWeek(published, batch.DepartedWeek);
        return report?.Events.FirstOrDefault(e => e.Kind == DemoSettlementEventKind.Emigration
            && e.MigrationBatchId == batch.Id && e.SettlementId == batch.FromSettlementId)?.EventId ?? 0;
    }

    /// <summary>
    /// 聚落最近一筆帶來飢餓債的已發布事件：飢餓（缺糧），或帶著飢餓債抵達／建立。
    /// 找不到（只在人工測試輸入期初就有飢餓債時）回傳 0，交給 <see cref="Problems"/> 指出。
    /// </summary>
    internal static long LatestDebtSourceId(IReadOnlyList<DemoWeekReport> published, int settlementId)
    {
        for (var i = published.Count - 1; i >= 0; i--)
        {
            var report = published[i];
            for (var j = report.Events.Count - 1; j >= 0; j--)
            {
                var e = report.Events[j];
                if (e.SettlementId != settlementId)
                    continue;
                if (e.Kind == DemoSettlementEventKind.Hunger)
                    return e.EventId;
                if (e.Kind is DemoSettlementEventKind.Arrival or DemoSettlementEventKind.SettlementFounded
                    && report.Migrations.Any(m => m.BatchId == e.MigrationBatchId && m.Arrived && m.HungerDebtClosing > 0))
                    return e.EventId;
            }
        }

        return 0;
    }

    /// <summary>玩家可讀的事件種類名稱。</summary>
    public static string KindText(DemoSettlementEventKind kind) => kind switch
    {
        DemoSettlementEventKind.Hunger => "飢餓",
        DemoSettlementEventKind.StarvationDeath => "餓死",
        DemoSettlementEventKind.HungerRecovered => "飢餓解除",
        DemoSettlementEventKind.Emigration => "外移",
        DemoSettlementEventKind.Arrival => "抵達",
        DemoSettlementEventKind.MigrantStarvationDeath => "途中餓死",
        DemoSettlementEventKind.SettlementFounded => "建立新聚落",
        _ => "未知"
    };
}
