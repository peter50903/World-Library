using System.Collections.Immutable;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-6 章：世界的時代（使用者 2026-10-23）。全世界共用，以第一個達成條件的聚落為準。</summary>
public enum DemoEra
{
    /// <summary>希望時代：不餓死、只有老死；採集為生。</summary>
    Hope = 0,

    /// <summary>開拓時代：任一聚落習得知識樹上任一技術時開始。</summary>
    Pioneer = 1
}

/// <summary>某聚落對某技術的研究進度；<see cref="LearnedWeek"/>＞0＝已習得（第幾週）。</summary>
public sealed record DemoKnowledgeRecord(int SettlementId, DemoTechnology Technology, long Progress, int LearnedWeek)
{
    public bool Learned => LearnedWeek > 0;
}

/// <summary>
/// 第 08-6 章 a 節：知識樹的世界狀態——各聚落各技術的研究進度與習得週、目前時代與開始週。不可變。
/// </summary>
public sealed record DemoKnowledgeState(ImmutableList<DemoKnowledgeRecord> Records, DemoEra Era, int EraWeek)
{
    public static DemoKnowledgeState Initial { get; } = new(ImmutableList<DemoKnowledgeRecord>.Empty, DemoEra.Hope, 0);

    public DemoKnowledgeRecord? Of(int settlementId, DemoTechnology technology)
        => Records.FirstOrDefault(r => r.SettlementId == settlementId && r.Technology == technology);

    public bool Knows(int settlementId, DemoTechnology technology) => Of(settlementId, technology)?.Learned == true;

    public IReadOnlyList<DemoTechnology> KnownBy(int settlementId)
        => Records.Where(r => r.SettlementId == settlementId && r.Learned).Select(r => r.Technology).ToList();

    public bool Equals(DemoKnowledgeState? other)
        => other is not null && Era == other.Era && EraWeek == other.EraWeek && Records.SequenceEqual(other.Records);

    public override int GetHashCode() => HashCode.Combine(Era, EraWeek, Records.Count);

    public string Fingerprint()
        => $"era={Era}@{EraWeek};" + string.Concat(Records.Select(r => $"know{r.SettlementId}/{r.Technology}={r.Progress}@{r.LearnedWeek};"));
}

/// <summary>
/// 第 08-6 章 a 節【TEMP 規則】知識的累積與傳播（使用者 2026-10-23：經歷一段時間自動學會，該產業的人越多、學者越多越快；
/// 知識透過婚嫁與遷徙帶到別的聚落）。純函式、不抽亂數。
/// <list type="bullet">
/// <item>第 08-9 章 A1：技術與前置讀正式設定 10B～10N（<see cref="DemoTechTree"/>）；前置全部已習得才累積。</item>
/// <item>每週研究進度＋＝該產業從業者人數×1（<see cref="Practitioners"/>；目前只有作物栽培：成年人、基礎木工：可及森林的成年人，其餘領域待 A2）；學者尚未出現。</item>
/// <item>進度達門檻（<see cref="ThresholdOf"/>＝研究時間×325）就習得；第一個習得任何技術的聚落讓世界進入開拓時代。</item>
/// <item>從已習得的聚落抵達的人，每人帶來門檻的 <see cref="TempCarryPercent"/>% 進度（比自行研究快）。</item>
/// </list>
/// </summary>
public static class DemoKnowledge
{
    /// <summary>【TEMP】第 08-6 章 e 節：習得鍛造後，工具讓每名勞力的採集量增加的百分比（第 08-9 章 A4 改為工具階梯）。</summary>
    public const long TempToolYieldPercent = 25;

    /// <summary>第 08-9 章 A1：門檻＝正式設定的研究時間 × 325（<see cref="DemoTechTree.ThresholdOf"/>）。</summary>
    public static long ThresholdOf(DemoTechnology technology) => DemoTechTree.ThresholdOf(technology);

    /// <summary>第 08-6 章 e 節：<paramref name="week"/> 結算時的工具加成（該週以前已習得鍛造）。</summary>
    public static long ToolYieldPercent(DemoKnowledgeState state, int settlementId, int week)
        => state.Of(settlementId, DemoTechnology.Smithing) is { Learned: true } r && r.LearnedWeek < week ? TempToolYieldPercent : 0;

    /// <summary>【TEMP】每個從已習得聚落抵達的人帶來的進度（門檻的百分比）。</summary>
    public const long TempCarryPercent = 25;

    /// <summary>技術中文名稱（正式設定的「中文名稱」）。</summary>
    public static string Name(DemoTechnology technology)
        => DemoTechTree.Contains(technology) ? DemoTechTree.Of(technology).Name : technology.Id;

    /// <summary>
    /// 第 08-9 章 A1：每週的從業者人數。08-7b 第 3 節的其他領域在 A2 接上；這裡沒列的技術目前不會自行累積
    /// （仍可由遷入者帶來）。
    /// </summary>
    public static long Practitioners(DemoTechnology technology, DemoSettlement settlement, DemoCohortDemography population, DemoHexWorld? map)
    {
        if (technology == DemoTechnology.Farming)
            return population.AdultsIn(settlement.Id, DemoHumanLifecycle.Rules);
        if (technology == DemoTechnology.Woodworking)
            return map is not null && NearForest(map, settlement) ? population.AdultsIn(settlement.Id, DemoHumanLifecycle.Rules) : 0;
        return 0;
    }

    public static string EraName(DemoEra era) => era switch
    {
        DemoEra.Hope => "希望時代",
        DemoEra.Pioneer => "開拓時代",
        _ => era.ToString()
    };

    /// <summary>
    /// 依本週週報（生死與遷徙之後）與群體人口更新研究進度；回傳新狀態與要追加的事件（習得技術、時代開始），
    /// 事件編號從 <paramref name="nextEventId"/> 起。只有人類繁衍世界有知識。
    /// </summary>
    public static (DemoKnowledgeState State, IReadOnlyList<DemoSettlementEvent> Events) Advance(
        DemoKnowledgeState state, DemoWeekReport report, DemoCohortDemography population, DemoWorldSites sites, long nextEventId,
        DemoHexWorld? map = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(sites);
        var records = state.Records.ToDictionary(r => (r.SettlementId, r.Technology));
        var events = new List<DemoSettlementEvent>();
        var era = state.Era;
        var eraWeek = state.EraWeek;
        var arrivals = report.Migrations.Where(m => m.Arrived && m.PopulationClosing > 0).ToList();
        foreach (var settlement in sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id))
        {
            var incoming = arrivals.Where(m => m.ToSettlementId == settlement.Id).ToList();
            foreach (var node in DemoTechTree.All)
            {
                var technology = node.Technology;
                var key = (settlement.Id, technology);
                var current = records.GetValueOrDefault(key) ?? new DemoKnowledgeRecord(settlement.Id, technology, 0, 0);
                if (current.Learned)
                    continue;
                // 第 08-9 章 A1：前置（ParentTechIDs）全部已習得才累積（08-7a 第 2 節第一道閘門）。
                if (node.Parents.Any(p => !state.Knows(settlement.Id, p)))
                    continue;
                var practitioners = Practitioners(technology, settlement, population, map);
                var carried = incoming.Count == 0 ? 0
                    : incoming.Where(m => state.Knows(m.FromSettlementId, technology)).Sum(m => m.PopulationClosing);
                if (practitioners == 0 && carried == 0)
                    continue;
                var threshold = ThresholdOf(technology);
                var gain = checked(practitioners + carried * threshold * TempCarryPercent / 100);
                if (gain == 0)
                    continue;
                var progress = Math.Min(threshold, checked(current.Progress + gain));
                var learned = progress >= threshold;
                records[key] = current with { Progress = progress, LearnedWeek = learned ? report.Week : 0 };
                if (!learned)
                    continue;
                var cause = carried > 0
                    ? $"已習得的聚落有 {carried} 人遷入，帶來{Name(technology)}的經驗"
                    : technology == DemoTechnology.Woodworking
                        ? $"{practitioners} 名成年人長年在森林取木的經驗累積"
                        : $"{practitioners} 名成年人長年採集植物的經驗累積";
                var learnedEvent = new DemoSettlementEvent(report.Week, report.WorldTime, settlement.Id, settlement.Name,
                    DemoSettlementEventKind.KnowledgeLearned, node.Index, $"學會{Name(technology)}：{cause}【TEMP】",
                    EventId: nextEventId++);
                events.Add(learnedEvent);
                if (era == DemoEra.Hope)
                {
                    era = DemoEra.Pioneer;
                    eraWeek = report.Week;
                    events.Add(new DemoSettlementEvent(report.Week, report.WorldTime, settlement.Id, settlement.Name,
                        DemoSettlementEventKind.EraBegan, (long)DemoEra.Pioneer,
                        $"{settlement.Name}學會{Name(technology)}，世界進入{EraName(DemoEra.Pioneer)}",
                        EventId: nextEventId++, CauseEventId: learnedEvent.EventId));
                }
            }
        }

        var next = new DemoKnowledgeState(
            records.Values.OrderBy(r => r.SettlementId).ThenBy(r => r.Technology).ToImmutableList(), era, eraWeek);
        return (next, events);
    }

    /// <summary>第 08-6 章 d 節：聚落本格或六鄰格有森林（木工的從業條件）。</summary>
    public static bool NearForest(DemoHexWorld map, DemoSettlement settlement)
    {
        var origin = map.GetCell(settlement.Column, settlement.Row);
        return origin.Terrain == DemoTerrain.Forest || DemoRouteNetwork.Directions
            .Select(d => map.AtAxial(origin.AxialQ + d.Dq, origin.AxialR + d.Dr))
            .Any(c => c is { Terrain: DemoTerrain.Forest });
    }
}
