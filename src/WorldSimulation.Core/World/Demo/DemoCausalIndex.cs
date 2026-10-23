namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #88：M1 示範的歷史因果索引（43F CAUSE_PARENT／EFFECT_CHILD／REL_TIME 的最小切片）。
/// 每筆聚落事件只存「直接原因事件編號」（<see cref="DemoSettlementEvent.CauseEventId"/>），
/// 直接後果由本索引反向建立，不另存（43F：只存直接邊，可由父項反向索引建立）。
///
/// 本類別同時是因果證據的檢查器：<see cref="Problems"/> 列出所有不合法之處，
/// 例如編號不連續、原因指向未來或不存在的事件、原因種類或聚落／隊伍不符、
/// 以及與週帳不符的事件（沒有缺糧的飢餓、沒有出發帳的外移、沒有隊伍帳的抵達／建立／途中餓死、
/// 有隊伍帳卻漏記的事件；#97 起聚落帳的缺糧、死亡、飢餓解除也必須恰好各有一筆事件，不能漏記或拆分），
/// 餓死與飢餓解除必須精確指向「最近一筆」飢餓債來源。M1_HISTORY 與 TEMP 讀檔都用它。
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

    // 第 04-1 章：讀檔後只保留近期週報；第一份保留週報的週次（新世界＝0，表示週報完整）。
    private int _firstRetainedWeek;

    /// <summary>此週次之前的週報已不保存（新世界為 false）。</summary>
    private bool Truncated(int week) => _firstRetainedWeek > 1 && week < _firstRetainedWeek;

    /// <summary>此報告之前保留的週數不足遺跡判定所需（讀檔後頭幾週無法核對空置證據）。</summary>
    private bool RuinEvidenceTruncated(int reportIndex)
        => _firstRetainedWeek > 1 && reportIndex < DemoSettlementTempSettings.Current.RuinAfterEmptyWeeks - 1;

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
        => Build(Array.Empty<DemoSettlementEvent>(), weeks);

    /// <summary>
    /// 第 04-1 章：讀檔後的世界——<paramref name="earlier"/>＝存檔保存的較早事件（只建索引，不再逐筆對帳：
    /// 存檔時已驗證、之前的週報已不保存），<paramref name="weeks"/>＝保留的近期週報（照常檢查）。
    /// </summary>
    public static DemoCausalIndex Build(IEnumerable<DemoSettlementEvent> earlier, IEnumerable<DemoWeekReport> weeks)
    {
        ArgumentNullException.ThrowIfNull(earlier);
        ArgumentNullException.ThrowIfNull(weeks);
        var index = new DemoCausalIndex();
        var reports = weeks.ToList();
        index._firstRetainedWeek = reports.Count > 0 && reports[0].Week > 1 ? reports[0].Week : 0;
        long expected = 1;
        foreach (var e in earlier)
        {
            if (e.EventId != expected)
                index._problems.Add($"第 {e.Week} 週 {e.SettlementName} 的 {KindText(e.Kind)} 事件編號是 #{e.EventId}，應為 #{expected}");
            expected = Math.Max(expected, e.EventId) + 1;
            index._ordered.Add(e);
            if (e.EventId > 0)
                index._byId.TryAdd(e.EventId, e);
            if (e.CauseEventId > 0 && index._byId.ContainsKey(e.CauseEventId))
            {
                if (!index._children.TryGetValue(e.CauseEventId, out var list))
                    index._children[e.CauseEventId] = list = new List<DemoSettlementEvent>();
                list.Add(e);
            }
        }
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

        for (var reportIndex = 0; reportIndex < reports.Count; reportIndex++)
        {
            var report = reports[reportIndex];
            foreach (var e in report.Events)
            {
                var problem = index.CheckOne(reportIndex, e, reports);
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

        index.CheckTravelLedgersHaveEvents(reports);
        index.CheckSettlementLedgersHaveEvents(reports,
            index._ordered.Where(e => e.Kind == DemoSettlementEventKind.SettlementRuined && reports.All(r => r.Week != e.Week)).Select(e => e.SettlementId));
        return index;
    }

    /// <summary>
    /// #97 反向核對（聚落帳 → 事件）：每個聚落每週的飢餓、餓死、飢餓解除事件筆數必須恰好由週帳決定，
    /// 不能漏記、拆成多筆或多出一筆。規則與結算時產生事件的條件相同：
    /// 缺糧＞0 → 恰好 1 筆飢餓；死亡＞0 → 恰好 1 筆餓死；期初有飢餓債、本週沒有缺糧、
    /// 恢復與死亡移除後飢餓債歸 0 且有人倖存 → 恰好 1 筆飢餓解除；其他情形 0 筆。
    /// 這可抓到「刪掉一筆沒有後果的飢餓或飢餓解除事件、再把後面的編號全部往前補」這類整段改寫。
    /// </summary>
    private void CheckSettlementLedgersHaveEvents(IReadOnlyList<DemoWeekReport> reports, IEnumerable<int> ruinedEarlier)
    {
        var ruined = ruinedEarlier.ToHashSet(); // 第 04-1 章：讀檔前已成為遺跡的聚落
        for (var reportIndex = 0; reportIndex < reports.Count; reportIndex++)
        {
            var report = reports[reportIndex];
            foreach (var ledger in report.Settlements)
            {
                var survival = ledger.FoodShortage + ledger.WaterShortage; // 第 06 章 d 節：缺水併入生存短缺
                var debtAfterDeaths = ledger.HungerDebtOpening + survival
                    - ledger.HungerDebtRecovered - ledger.HungerDebtRemovedByDeath - ledger.HungerDebtRemovedByNaturalDeath;
                var recovered = ledger.HungerDebtOpening > 0 && survival == 0
                    && debtAfterDeaths == 0 && ledger.PopulationOpening - (ledger.Deaths - ledger.NaturalDeaths) > 0;
                CheckCount(report, ledger, DemoSettlementEventKind.Hunger, survival > 0 ? 1 : 0);
                CheckCount(report, ledger, DemoSettlementEventKind.StarvationDeath, ledger.Deaths > ledger.NaturalDeaths ? 1 : 0);
                CheckCount(report, ledger, DemoSettlementEventKind.Birth, ledger.Births > 0 ? 1 : 0);
                CheckCount(report, ledger, DemoSettlementEventKind.NaturalDeath, ledger.NaturalDeaths > 0 ? 1 : 0);
                CheckCount(report, ledger, DemoSettlementEventKind.HungerRecovered, recovered ? 1 : 0);
                // 第 07 章 c 節：空置滿 TEMP 週數的那一週恰好 1 筆成為遺跡（之前已成遺跡、或仍有隊伍正前往則 0 筆）。
                var ruin = ledger.PopulationClosing == 0
                    && !ruined.Contains(ledger.SettlementId)
                    && DemoSettlementTempSettings.Current.EmptyLongEnough(ledger.SettlementId, report.Week, 0, reports)
                    && !report.Migrations.Any(m => !m.Arrived && m.PopulationClosing > 0 && m.ToSettlementId == ledger.SettlementId);
                if (RuinEvidenceTruncated(reportIndex))
                {
                    // 第 04-1 章：空置證據在未保存的週報裡，只記下已成遺跡的聚落。
                    if (report.Events.Any(e => e.Kind == DemoSettlementEventKind.SettlementRuined && e.SettlementId == ledger.SettlementId))
                        ruined.Add(ledger.SettlementId);
                    continue;
                }
                CheckCount(report, ledger, DemoSettlementEventKind.SettlementRuined, ruin ? 1 : 0);
                if (ruin) ruined.Add(ledger.SettlementId);
            }
        }
    }

    private void CheckCount(DemoWeekReport report, DemoSettlementWeekLedger ledger, DemoSettlementEventKind kind, int expected)
    {
        var n = report.Events.Count(e => e.Kind == kind && e.SettlementId == ledger.SettlementId
            && (kind is not (DemoSettlementEventKind.Birth or DemoSettlementEventKind.NaturalDeath) || e.MigrationBatchId == 0));
        if (n != expected)
            _problems.Add($"第 {report.Week} 週 {ledger.SettlementName} 的週帳應有 {expected} 筆{KindText(kind)}事件，實際 {n} 筆（漏記、重複或拆分）");
    }

    private string? CheckOne(int reportIndex, DemoSettlementEvent e, IReadOnlyList<DemoWeekReport> reports)
    {
        var report = reports[reportIndex];
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
            case DemoSettlementEventKind.Birth:
            case DemoSettlementEventKind.NaturalDeath:
            {
                // 第 07 章 b 節：途中隊伍的出生／自然死亡以隊伍帳為證據。
                long? budget;
                if (e.MigrationBatchId > 0)
                {
                    var leg = report.Migrations.SingleOrDefault(m => m.BatchId == e.MigrationBatchId);
                    budget = leg is null || leg.FromSettlementId != e.SettlementId ? null
                        : e.Kind == DemoSettlementEventKind.Birth ? leg.Births : leg.NaturalDeaths;
                }
                else
                {
                    var ledger = report.Settlements.SingleOrDefault(l => l.SettlementId == e.SettlementId);
                    budget = e.Kind == DemoSettlementEventKind.Birth ? ledger?.Births : ledger?.NaturalDeaths;
                }
                // 第 04-2 章：每個聚落（或隊伍）每週合計一筆，人數＝週帳人數。
                if (parent is not null || budget is null || budget == 0 || e.Count != budget
                    || string.IsNullOrWhiteSpace(e.Cause))
                    return "自然出生／死亡需要本週人口帳、個體來源與自足的合法根因";
                return null;
            }
            case DemoSettlementEventKind.Hunger:
            {
                if (parent is not null)
                    return "飢餓是根因（本週需求大於可取得），不應有直接原因事件";
                var ledger = report.Settlements.SingleOrDefault(l => l.SettlementId == e.SettlementId);
                var survival = ledger is null ? 0 : ledger.FoodShortage + ledger.WaterShortage;
                if (ledger is null || survival <= 0 || survival != e.Count)
                    return $"週帳沒有對應的缺糧或缺水（事件 {e.Count}，週帳 {survival.ToString(System.Globalization.CultureInfo.InvariantCulture)}）";
                return null;
            }

            case DemoSettlementEventKind.Emigration:
            {
                var departed = report.Migrations.SingleOrDefault(m => m.BatchId == e.MigrationBatchId && m.Departed);
                if (parent is null)
                {
                    // 第 08-1 章：擁擠外移是根因——證據是本週聚落帳沒缺糧、人口夠多且取用來源餘量低於 TEMP 門檻，
                    // 而且只能是開拓隊（不搬進別的聚落）。
                    var ledger = report.Settlements.SingleOrDefault(l => l.SettlementId == e.SettlementId);
                    // 婚嫁遷移也是根因：一人、非開拓、隊伍帳標明婚嫁（名冊重播另外核對出發者確實找不到伴侶）。
                    var mate = departed is not null && departed.MateSeeking && !departed.Founding && departed.PopulationOpening == 1;
                    if (!mate && (ledger is null || departed is null || !departed.Founding || !DemoCrowding.Crowded(report, ledger)))
                        return "外移的直接原因必須是同一聚落同一週的飢餓事件（或有週帳證據的擁擠開拓、婚嫁遷移）";
                }
                else if (parent.Kind != DemoSettlementEventKind.Hunger
                    || parent.SettlementId != e.SettlementId || parent.Week != e.Week)
                    return "外移的直接原因必須是同一聚落同一週的飢餓事件";
                if (departed is null || departed.FromSettlementId != e.SettlementId || departed.PopulationOpening != e.Count)
                    return $"週帳沒有對應的隊伍 #{e.MigrationBatchId} 出發紀錄（人數 {e.Count}）";
                return null;
            }

            case DemoSettlementEventKind.Arrival:
            case DemoSettlementEventKind.SettlementFounded:
            case DemoSettlementEventKind.MigrantStarvationDeath:
            case DemoSettlementEventKind.MigrantKilledByBeasts:
            {
                if (parent is null || parent.Kind != DemoSettlementEventKind.Emigration
                    || parent.MigrationBatchId != e.MigrationBatchId || parent.Week >= e.Week)
                    return $"直接原因必須是隊伍 #{e.MigrationBatchId} 在更早週次的外移事件";
                if (e.Kind is DemoSettlementEventKind.MigrantStarvationDeath or DemoSettlementEventKind.MigrantKilledByBeasts && parent.SettlementId != e.SettlementId)
                    return "途中餓死事件的聚落必須是隊伍的出發聚落";
                // GPT 審 #91：事件本身也要有當週隊伍帳為證據，偽造的抵達／建立／途中餓死不能只靠父事件過關。
                return TravelLedgerProblem(report, e);
            }

            case DemoSettlementEventKind.StarvationDeath:
            case DemoSettlementEventKind.HungerRecovered:
            {
                if (parent is null || parent.SettlementId != e.SettlementId)
                    return "直接原因必須是同一聚落帶來飢餓債的事件（飢餓，或帶著飢餓債抵達／建立）";
                if (parent.Kind == DemoSettlementEventKind.Hunger)
                {
                    if (parent.Week > e.Week)
                        return "原因週次晚於此事件";
                }
                else if (!(parent.Kind is DemoSettlementEventKind.Arrival or DemoSettlementEventKind.SettlementFounded
                    && parent.Week < e.Week && (Truncated(parent.Week) || CarriedDebt(reports, parent))))
                {
                    return "直接原因必須是同一聚落帶來飢餓債的事件（飢餓，或更早週次帶著飢餓債抵達／建立）";
                }

                // GPT 審 #91：必須精確指向「最近一筆」飢餓債來源，指到更早的舊事件也不合法。
                var latest = ExpectedDebtSourceId(reports, reportIndex, e);
                if (latest == 0 && Truncated(parent.Week))
                    return null; // 第 04-1 章：債源在未保存的週報裡（保留範圍內沒有更新的債源）
                if (latest == 0)
                    return "此聚落在此事件之前沒有任何帶來飢餓債的事件";
                return e.CauseEventId == latest
                    ? null
                    : $"直接原因 #{e.CauseEventId} 不是此聚落最近一筆帶來飢餓債的事件（應為 #{latest}）";
            }

            case DemoSettlementEventKind.SettlementRuined:
            {
                // 第 07 章 c 節：根因；證據是此週與前面連續週帳的期末人口都是 0。
                var rule = DemoSettlementTempSettings.Current;
                var ledger = report.Settlements.SingleOrDefault(l => l.SettlementId == e.SettlementId);
                if (parent is not null || ledger is null || e.Count != rule.RuinAfterEmptyWeeks
                    || (!RuinEvidenceTruncated(reportIndex) && !rule.EmptyLongEnough(e.SettlementId, report.Week, ledger.PopulationClosing, reports)))
                    return "成為遺跡需要連續空置週數的週帳證據，且是根因";
                if (reports.Take(reportIndex).Any(r => r.Events.Any(x => x.Kind == DemoSettlementEventKind.SettlementRuined
                    && x.SettlementId == e.SettlementId)))
                    return "同一聚落不能重複成為遺跡";
                return null;
            }

            case DemoSettlementEventKind.KnowledgeLearned:
                // 第 08-6 章：根因（經驗累積或遷入者帶來；進度另存於知識狀態）。
                return parent is null && DemoTechTree.ByIndex(e.Count) is not null ? null : "習得技術是根因，事件人數欄必須是知識樹上的技術編號";
            case DemoSettlementEventKind.EraBegan:
                return parent is { Kind: DemoSettlementEventKind.KnowledgeLearned } && parent.Week == e.Week
                    && parent.SettlementId == e.SettlementId && Enum.IsDefined((DemoEra)e.Count)
                    ? null : "時代開始的直接原因必須是同週同聚落的習得技術事件";
            case DemoSettlementEventKind.KnowledgeLost:
                // 第 08-9 章 A5：直接原因是同週同聚落成為遺跡。
                return parent is { Kind: DemoSettlementEventKind.SettlementRuined } && parent.Week == e.Week
                    && parent.SettlementId == e.SettlementId && DemoTechTree.ByIndex(e.Count) is not null
                    ? null : "知識失傳的直接原因必須是同週同聚落成為遺跡，人數欄必須是知識樹上的技術編號";
            case DemoSettlementEventKind.FaithFounded:
                // 第 08-9 章 B3：直接原因是同週同聚落習得祖靈祭祀。
                return parent is { Kind: DemoSettlementEventKind.KnowledgeLearned } && parent.Week == e.Week && parent.SettlementId == e.SettlementId
                    && DemoTechTree.ByIndex(parent.Count) == DemoFaiths.AncestorRites && e.Count > 0
                    ? null : "創立信仰的直接原因必須是同週同聚落習得祖靈祭祀";
            case DemoSettlementEventKind.CultureSplit:
                return parent is null && e.Count > 0 ? null : "文化分化是根因，人數欄必須是文化編號";
            case DemoSettlementEventKind.FaithExtinct:
                return parent is null && e.Count > 0 ? null : "信仰滅絕是根因，人數欄必須是信仰編號";
            case DemoSettlementEventKind.FarmlandCleared:
                // 第 08-6 章 c 節：根因（開墾進度另存於土地開發狀態）。
                return parent is null && e.Count > 0 ? null : "開墾農田是根因，事件人數欄必須是格編號";

            default:
                return "未知的事件種類";
        }
    }

    /// <summary>抵達／建立／途中餓死事件必須與當週隊伍帳一致（隊伍、聚落、人數）。</summary>
    private static string? TravelLedgerProblem(DemoWeekReport report, DemoSettlementEvent e)
    {
        var ledger = report.Migrations.FirstOrDefault(m => m.BatchId == e.MigrationBatchId && !m.Departed);
        if (ledger is null)
            return $"週帳沒有隊伍 #{e.MigrationBatchId} 本週的途中紀錄";
        return e.Kind switch
        {
            DemoSettlementEventKind.Arrival when !ledger.Arrived || ledger.Founding
                || ledger.ToSettlementId != e.SettlementId || ledger.PopulationClosing != e.Count
                => $"週帳沒有隊伍 #{e.MigrationBatchId} 抵達此聚落的紀錄（事件人數 {e.Count}，週帳抵達 {(ledger.Arrived ? ledger.PopulationClosing : 0)}）",
            DemoSettlementEventKind.SettlementFounded when !ledger.Arrived || !ledger.Founding
                || ledger.ToSettlementId != e.SettlementId || ledger.PopulationClosing != e.Count
                => $"週帳沒有開拓隊 #{e.MigrationBatchId} 建立此聚落的紀錄（事件人數 {e.Count}，週帳抵達 {(ledger.Arrived ? ledger.PopulationClosing : 0)}）",
            DemoSettlementEventKind.MigrantStarvationDeath when ledger.FromSettlementId != e.SettlementId
                || ledger.StarvationDeaths <= 0 || ledger.StarvationDeaths != e.Count
                => $"週帳沒有對應的隊伍 #{e.MigrationBatchId} 途中死亡（事件死亡 {e.Count}，週帳餓死 {ledger.StarvationDeaths}）",
            DemoSettlementEventKind.MigrantKilledByBeasts when ledger.FromSettlementId != e.SettlementId
                || ledger.BeastDeaths <= 0 || ledger.BeastDeaths != e.Count
                => $"週帳沒有對應的隊伍 #{e.MigrationBatchId} 遭襲死亡（事件 {e.Count}，週帳 {ledger.BeastDeaths}）",
            _ => null
        };
    }

    /// <summary>
    /// 反向核對：每筆抵達、建立或有死亡的途中隊伍帳，都要有恰好一筆對應事件（不能漏記）。
    /// </summary>
    private void CheckTravelLedgersHaveEvents(IReadOnlyList<DemoWeekReport> reports)
    {
        foreach (var report in reports)
        {
            // 出發帳 → 外移事件：每支本週出發的隊伍恰好一筆同隊伍、同來源聚落的外移事件（不能漏記或重複）。
            foreach (var ledger in report.Migrations.Where(m => m.Departed))
            {
                var n = report.Events.Count(e => e.Kind == DemoSettlementEventKind.Emigration
                    && e.MigrationBatchId == ledger.BatchId && e.SettlementId == ledger.FromSettlementId);
                if (n != 1)
                    _problems.Add($"第 {report.Week} 週隊伍 #{ledger.BatchId} 週帳記為出發，但有 {n} 筆外移事件（應恰好 1 筆）");
            }

            foreach (var ledger in report.Migrations)
            {
                // 第 07 章 b 節：途中出生／自然死亡（出發週也可能）：每人恰好一筆事件。
                foreach (var (kind, expected) in new[] { (DemoSettlementEventKind.Birth, ledger.Births), (DemoSettlementEventKind.NaturalDeath, ledger.NaturalDeaths) })
                {
                    var n = report.Events.Count(e => e.Kind == kind && e.MigrationBatchId == ledger.BatchId);
                    if (n != (expected > 0 ? 1 : 0)) // 第 04-2 章：合計一筆
                        _problems.Add($"第 {report.Week} 週隊伍 #{ledger.BatchId} 週帳{KindText(kind)} {expected}，但有 {n} 筆事件");
                }
            }

            foreach (var ledger in report.Migrations.Where(m => !m.Departed))
            {
                if (ledger.Arrived)
                {
                    var kind = ledger.Founding ? DemoSettlementEventKind.SettlementFounded : DemoSettlementEventKind.Arrival;
                    var n = report.Events.Count(e => e.Kind == kind && e.MigrationBatchId == ledger.BatchId);
                    if (n != 1)
                        _problems.Add($"第 {report.Week} 週隊伍 #{ledger.BatchId} 週帳記為{KindText(kind)}，但有 {n} 筆{KindText(kind)}事件（應恰好 1 筆）");
                }

                if (ledger.Deaths > 0)
                {
                    var n = report.Events.Count(e => e.Kind == DemoSettlementEventKind.MigrantStarvationDeath
                        && e.MigrationBatchId == ledger.BatchId);
                    if (n != 1)
                        _problems.Add($"第 {report.Week} 週隊伍 #{ledger.BatchId} 週帳途中死亡 {ledger.Deaths}，但有 {n} 筆途中餓死事件（應恰好 1 筆）");
                }
            }
        }
    }

    /// <summary>
    /// 餓死／飢餓解除應指向的直接原因：同聚落同週的飢餓事件；沒有則為更早週次中最近一筆
    /// 帶來飢餓債的事件（與結算時 <see cref="LatestDebtSourceId"/> 同一規則，只看此週之前）。
    /// </summary>
    private static long ExpectedDebtSourceId(IReadOnlyList<DemoWeekReport> reports, int reportIndex, DemoSettlementEvent e)
    {
        var report = reports[reportIndex];
        var sameWeekHunger = report.Events.FirstOrDefault(h => h.Kind == DemoSettlementEventKind.Hunger
            && h.SettlementId == e.SettlementId && h.EventId < e.EventId);
        if (sameWeekHunger is not null)
            return sameWeekHunger.EventId;
        // 週報依週次排列，此週之前＝索引 reportIndex 之前的全部週報。
        return LatestDebtSourceIdBefore(reports, reportIndex, e.SettlementId);
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
        => LatestDebtSourceIdBefore(published, published.Count, settlementId);

    /// <summary>同 <see cref="LatestDebtSourceId"/>，但只看索引 <paramref name="count"/> 之前的週報。</summary>
    private static long LatestDebtSourceIdBefore(IReadOnlyList<DemoWeekReport> published, int count, int settlementId)
    {
        for (var i = count - 1; i >= 0; i--)
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
        DemoSettlementEventKind.Birth => "出生",
        DemoSettlementEventKind.NaturalDeath => "自然死亡",
        DemoSettlementEventKind.SettlementRuined => "成為遺跡",
        _ => "未知"
    };
}
