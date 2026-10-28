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

/// <summary>第 08-6 章：知識樹上的技術（農耕、木工；鍛造、石工隨後各節加入）。</summary>
public enum DemoTechnology
{
    /// <summary>農耕：草原可開墾成農田，提高食物上限。</summary>
    Farming = 0,

    /// <summary>第 08-6 章 d 節：木工：有木匠；聚落升為村莊的條件。</summary>
    Woodworking = 1,

    /// <summary>第 08-6 章 e 節：鍛造：有鐵匠；工具提高採集效率。前置：木工。</summary>
    Smithing = 2
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
/// <item>每週研究進度＋＝該產業從業者人數×1（農耕：聚落的成年人，都在採集植物；木工：可及森林的聚落的成年人）；學者尚未出現。</item>
/// <item>進度達門檻（<see cref="ThresholdOf"/>）就習得；第一個習得任何技術的聚落讓世界進入開拓時代。</item>
/// <item>從已習得的聚落抵達的人，每人帶來門檻的 <see cref="TempCarryPercent"/>% 進度（比自行研究快）。</item>
/// </list>
/// </summary>
public static class DemoKnowledge
{
    /// <summary>
    /// 【TEMP】第 08-10a 章 V4：門檻＝正式設定 ResearchTimeBase × 本倍率（08-7a 原訂 325）。
    /// 使用者 2026-10-28 裁決希望時代是序章（10～20 年）；V0 量到 325 時第一項技術要 36～53 年，改 110。
    /// </summary>
    public const long TempResearchScale = 110;

    /// <summary>正式設定 10B TECH_CROP_CULTIVATION（基礎作物栽培）的 ResearchTimeBase。A1 改讀設定檔後移除。</summary>
    public const long CropCultivationTimeBase = 80;

    /// <summary>正式設定 10E TECH_BASIC_CARPENTRY（基礎木工）的 ResearchTimeBase。</summary>
    public const long BasicCarpentryTimeBase = 100;

    /// <summary>正式設定 10C TECH_BASIC_FORGING（基礎鍛造）的 ResearchTimeBase。</summary>
    public const long BasicForgingTimeBase = 180;

    /// <summary>農耕習得門檻（研究點）＝80×110。</summary>
    public const long TempThreshold = CropCultivationTimeBase * TempResearchScale;

    /// <summary>木工習得門檻＝100×110。</summary>
    public const long TempWoodworkingThreshold = BasicCarpentryTimeBase * TempResearchScale;

    /// <summary>鍛造習得門檻＝180×110（只有已習得木工的聚落累積）。</summary>
    public const long TempSmithingThreshold = BasicForgingTimeBase * TempResearchScale;

    /// <summary>
    /// 【TEMP】第 08-10a 章 V4（08-7a 鄰近傳播，參考 CK3 革新傳播）：範圍內每有一個已習得的聚落，每週＋門檻的萬分之 5（0.05%），
    /// 最多算 <see cref="TempDiffusionMaxNeighbours"/> 個（0.25%）。
    /// </summary>
    public const long TempDiffusionPerNeighbourBasisPoints = 5;

    /// <summary>【TEMP】鄰近傳播最多計入的已習得聚落數。</summary>
    public const int TempDiffusionMaxNeighbours = 5;

    /// <summary>【TEMP】鄰近傳播的範圍（六角格距離；與開拓選址的 6 格相同，勢力範圍出現前的暫代）。</summary>
    public const int TempDiffusionRange = 6;

    /// <summary>兩個聚落的六角格距離（軸向座標）。</summary>
    public static int Distance(DemoHexWorld map, DemoSettlement a, DemoSettlement b)
    {
        ArgumentNullException.ThrowIfNull(map);
        var x = map.GetCell(a.Column, a.Row);
        var y = map.GetCell(b.Column, b.Row);
        var dq = x.AxialQ - y.AxialQ;
        var dr = x.AxialR - y.AxialR;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    /// <summary>【TEMP】第 08-6 章 e 節：習得鍛造後，工具讓每名勞力的採集量增加的百分比。</summary>
    public const long TempToolYieldPercent = 25;

    public static long ThresholdOf(DemoTechnology technology) => technology switch
    {
        DemoTechnology.Woodworking => TempWoodworkingThreshold,
        DemoTechnology.Smithing => TempSmithingThreshold,
        _ => TempThreshold
    };

    /// <summary>第 08-6 章 e 節：<paramref name="week"/> 結算時的工具加成（該週以前已習得鍛造）。</summary>
    public static long ToolYieldPercent(DemoKnowledgeState state, int settlementId, int week)
        => state.Of(settlementId, DemoTechnology.Smithing) is { Learned: true } r && r.LearnedWeek < week ? TempToolYieldPercent : 0;

    /// <summary>【TEMP】每個從已習得聚落抵達的人帶來的進度（門檻的百分比）。</summary>
    public const long TempCarryPercent = 25;

    public static string Name(DemoTechnology technology) => technology switch
    {
        DemoTechnology.Farming => "農耕",
        DemoTechnology.Woodworking => "木工",
        DemoTechnology.Smithing => "鍛造",
        _ => technology.ToString()
    };

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
        var rules = DemoHumanLifecycle.Rules;
        var living = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        foreach (var settlement in living)
        {
            foreach (var technology in Enum.GetValues<DemoTechnology>())
            {
                var key = (settlement.Id, technology);
                var current = records.GetValueOrDefault(key) ?? new DemoKnowledgeRecord(settlement.Id, technology, 0, 0);
                if (current.Learned)
                    continue;
                // 第 08-6 章 e 節：鍛造的前置是木工（燃料與工具柄）。
                var practitioners = (technology == DemoTechnology.Woodworking && (map is null || !NearForest(map, settlement)))
                    || (technology == DemoTechnology.Smithing && !state.Knows(settlement.Id, DemoTechnology.Woodworking))
                    ? 0 : population.AdultsIn(settlement.Id, rules);
                var threshold = ThresholdOf(technology);
                var carried = report.Migrations
                    .Where(m => m.Arrived && m.ToSettlementId == settlement.Id && state.Knows(m.FromSettlementId, technology))
                    .Sum(m => m.PopulationClosing);
                // 第 08-10a 章 V4：鄰近傳播——上週已習得、範圍內的其他聚落（有從業條件才傳得進來）。
                var neighbours = map is null || practitioners == 0 ? 0
                    : living.Count(o => o.Id != settlement.Id && state.Knows(o.Id, technology) && Distance(map, o, settlement) <= TempDiffusionRange);
                var diffusion = checked(threshold * Math.Min(neighbours, TempDiffusionMaxNeighbours) * TempDiffusionPerNeighbourBasisPoints / 10_000);
                var gain = checked(practitioners + diffusion + carried * threshold * TempCarryPercent / 100);
                if (gain == 0)
                    continue;
                var progress = Math.Min(threshold, checked(current.Progress + gain));
                var learned = progress >= threshold;
                records[key] = current with { Progress = progress, LearnedWeek = learned ? report.Week : 0 };
                if (!learned)
                    continue;
                var cause = carried > 0
                    ? $"已習得的聚落有 {carried} 人遷入，帶來{Name(technology)}的經驗"
                    : neighbours > 0
                    ? $"鄰近 {neighbours} 個已習得{Name(technology)}的聚落傳來做法，加上 {practitioners} 名成年人的經驗"
                    : technology switch
                    {
                        DemoTechnology.Woodworking => $"{practitioners} 名成年人長年在森林取木的經驗累積",
                        DemoTechnology.Smithing => $"{practitioners} 名成年人長年用火與製作工具的經驗累積",
                        _ => $"{practitioners} 名成年人長年採集植物的經驗累積"
                    };
                var learnedEvent = new DemoSettlementEvent(report.Week, report.WorldTime, settlement.Id, settlement.Name,
                    DemoSettlementEventKind.KnowledgeLearned, (long)technology, $"學會{Name(technology)}：{cause}【TEMP】",
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
