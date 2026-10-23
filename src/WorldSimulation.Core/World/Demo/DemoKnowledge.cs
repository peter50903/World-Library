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

    /// <summary>第 08-9 章 A3：這個聚落已因實踐加速推進過這項技術（每聚落每技術一次）。</summary>
    public bool Boosted { get; init; }
}

/// <summary>
/// 第 08-6 章 a 節：知識樹的世界狀態——各聚落各技術的研究進度與習得週、目前時代與開始週。不可變。
/// </summary>
public sealed record DemoKnowledgeState(ImmutableList<DemoKnowledgeRecord> Records, DemoEra Era, int EraWeek)
{
    public static DemoKnowledgeState Initial { get; } = new(ImmutableList<DemoKnowledgeRecord>.Empty, DemoEra.Hope, 0);

    public DemoKnowledgeRecord? Of(int settlementId, DemoTechnology technology)
        => Records.FirstOrDefault(r => r.SettlementId == settlementId && r.Technology == technology);

    /// <summary>第 08-9 章 A4：開局知識每個聚落都知道（沒有紀錄）。</summary>
    public bool Knows(int settlementId, DemoTechnology technology)
        => DemoTechTree.IsInitial(technology) || Of(settlementId, technology)?.Learned == true;

    public IReadOnlyList<DemoTechnology> KnownBy(int settlementId)
        => Records.Where(r => r.SettlementId == settlementId && r.Learned).Select(r => r.Technology).ToList();

    public bool Equals(DemoKnowledgeState? other)
        => other is not null && Era == other.Era && EraWeek == other.EraWeek && Records.SequenceEqual(other.Records);

    public override int GetHashCode() => HashCode.Combine(Era, EraWeek, Records.Count);

    public string Fingerprint()
        => $"era={Era}@{EraWeek};" + string.Concat(Records.Select(r => $"know{r.SettlementId}/{r.Technology}={r.Progress}@{r.LearnedWeek}{(r.Boosted ? "+" : "")};"));
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
    /// <summary>
    /// 【TEMP】第 08-9 章 A4：工具階梯（世紀帝國的同產業升級）——石器改良＋10%、基礎鍛造再＋15%、熱處理再＋10%。
    /// 每一級在習得週的下一週起生效。
    /// </summary>
    public static readonly IReadOnlyList<(DemoTechnology Technology, long Percent)> TempToolLadder = new[]
    {
        (new DemoTechnology("TECH_IMPROVED_STONE_TOOLS"), 10L),
        (DemoTechnology.Smithing, 15L),
        (new DemoTechnology("TECH_HEAT_TREATMENT"), 10L)
    };

    /// <summary>第 08-9 章 A1：門檻＝正式設定的研究時間 × 325（<see cref="DemoTechTree.ThresholdOf"/>）。</summary>
    public static long ThresholdOf(DemoTechnology technology) => DemoTechTree.ThresholdOf(technology);

    /// <summary>第 08-6 章 e 節、第 08-9 章 A4：<paramref name="week"/> 結算時的工具加成（工具階梯中該週以前已習得的各級合計）。</summary>
    public static long ToolYieldPercent(DemoKnowledgeState state, int settlementId, int week)
        => TempToolLadder.Where(step => state.Of(settlementId, step.Technology) is { Learned: true } r && r.LearnedWeek < week).Sum(step => step.Percent);

    /// <summary>【TEMP】每個從已習得聚落抵達的人帶來的進度（門檻的百分比）。</summary>
    public const long TempCarryPercent = 25;

    /// <summary>技術中文名稱（正式設定的「中文名稱」）。</summary>
    public static string Name(DemoTechnology technology)
        => DemoTechTree.Contains(technology) ? DemoTechTree.Of(technology).Name : technology.Id;

    /// <summary>【TEMP】第 08-9 章 A3：鄰近傳播的範圍（格）；勢力範圍內已習得的聚落。</summary>
    public const int TempDiffusionRange = 3;

    /// <summary>【TEMP】第 08-9 章 A3：每個已習得的鄰近聚落每週帶來門檻的萬分之 5，最多 5 個聚落。</summary>
    public const long TempDiffusionPerTenThousand = 5;

    public const int TempDiffusionMaxNeighbours = 5;

    /// <summary>
    /// 第 08-9 章 A3（文明帝國的靈光一現）：PracticeBoostCondition 在聚落真的發生時，該技術立刻推進門檻的 PracticeBoostEffect%。
    /// 目前接得到的條件：輪作＝農田連續耕作滿一年（本週與 52 週前都有農糧）；灌溉＝有農田且缺水。
    /// 其他條件在對應系統出現時加入（狩獵、傷口、倉儲…）。
    /// </summary>
    public static bool PracticeHappened(DemoTechnology technology, int settlementId, DemoWeekReport report, Func<int, DemoWeekReport?>? weekAt)
    {
        var ledger = report.Settlements.FirstOrDefault(l => l.SettlementId == settlementId);
        if (ledger is null)
            return false;
        return technology.Id switch
        {
            "TECH_CROP_ROTATION" => ledger.FoodFromFarming > 0 && weekAt?.Invoke(report.Week - 52)?.Settlements
                .FirstOrDefault(l => l.SettlementId == settlementId) is { FoodFromFarming: > 0 },
            "TECH_IRRIGATION" => ledger.FoodFromFarming > 0 && ledger.WaterShortage > 0,
            _ => false
        };
    }

    /// <summary>【TEMP】第 08-9 章 A2：每位同領域專家（代表人物）每週加的研究進度。</summary>
    public const long TempSpecialistWeight = 5;

    /// <summary>【TEMP】第 08-9 章 A2：每位學者每週加的研究進度（所有技術）。</summary>
    public const long TempScholarWeight = 10;

    /// <summary>
    /// 第 08-9 章 A2（08-7a 第 2 節第二道閘門）：技術階級要求的聚落等級——第 1 階任何聚落、第 2 階村莊；
    /// 第 3 階以上要城邦以上（尚未有），所以目前不會累積。
    /// </summary>
    public static bool TierAllows(DemoTechNode node, DemoSettlementTier tier)
        => node.Tier == 1 || (node.Tier == 2 && tier >= DemoSettlementTier.Village); // 第 0 階＝開局知識，不研究

    /// <summary>
    /// 第 08-9 章 A2【TEMP 規則】（08-7a 第三道閘門：地區）研究原料在聚落可取得才累積——
    /// 糧食（RES_GRAIN）以存糧支付、蔬果隨採集；木材要可及森林；纖維要可及草原；草藥要可及森林或草原；
    /// 水、黏土、沙要可取水；鹽要鄰海；布要已會織造。獸肉、獸皮（野獸出現後）、石材、礦、羊皮紙、寶石、魔晶目前都取不到。
    /// 原料暫不扣除（物品倉儲在第 09 章）。
    /// </summary>
    public static bool ResourceAvailable(string resource, DemoSettlement settlement, DemoHexWorld? map, long food, DemoKnowledgeState state)
    {
        bool Near(DemoTerrain terrain) => map is not null && NearTerrain(map, settlement, terrain);
        var cell = new DemoCellRef(settlement.Column, settlement.Row);
        return resource switch
        {
            "RES_GRAIN" => food > 0,
            "RES_VEGETABLE" => true,
            "RES_WOOD" => Near(DemoTerrain.Forest),
            "RES_FIBER" => Near(DemoTerrain.Grassland),
            "RES_MED_HERB" => Near(DemoTerrain.Forest) || Near(DemoTerrain.Grassland),
            "RES_WATER" or "RES_CLAY" or "RES_SAND" => map is not null && map.HasWaterAccess(cell),
            "RES_SALT" => map is not null && NearSea(map, settlement),
            "RES_CLOTH" => state.Knows(settlement.Id, new DemoTechnology("TECH_WEAVING")),
            _ => false
        };
    }

    /// <summary>
    /// 第 08-9 章 A2：每週的從業者人數＝成年人（研究原料都取得到時）；畜牧要等野生動物與牧群（第 08-9 章 C1）。
    /// </summary>
    public static long Practitioners(DemoTechNode node, DemoSettlement settlement, DemoCohortDemography population, DemoHexWorld? map,
        long food, DemoKnowledgeState state)
    {
        if (node.Branch is "畜牧" or "狩獵") // 第 08-9 章 A4：狩獵也要等野生動物（C1）
            return 0;
        if (node.ResearchCost.Any(c => !ResourceAvailable(c.Resource, settlement, map, food, state)))
            return 0;
        return population.AdultsIn(settlement.Id, DemoHumanLifecycle.Rules);
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
        DemoHexWorld? map = null, Func<int, DemoSettlementTier>? tierOf = null, Func<int, DemoWeekReport?>? weekAt = null,
        Func<int, IReadOnlySet<string>>? cultureTraitsOf = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(sites);
        var records = state.Records.ToDictionary(r => (r.SettlementId, r.Technology));
        var events = new List<DemoSettlementEvent>();
        var era = state.Era;
        var eraWeek = state.EraWeek;
        // 第 08-9 章 A5（08-7a 失傳）：本週成為遺跡的聚落，知識隨人消失；勢力範圍內沒有其他持有者時記「知識失傳」。
        foreach (var ruined in report.Events.Where(e => e.Kind == DemoSettlementEventKind.SettlementRuined).OrderBy(e => e.EventId))
        {
            var place = sites.Settlements.FirstOrDefault(s => s.Id == ruined.SettlementId);
            foreach (var record in records.Values.Where(r => r.SettlementId == ruined.SettlementId).OrderBy(r => DemoTechTree.Of(r.Technology).Index).ToList())
            {
                records.Remove((record.SettlementId, record.Technology));
                if (!record.Learned || place is null)
                    continue;
                var heldNearby = sites.Settlements.Any(o => o.Id != place.Id && !o.IsRuin
                    && (map is null || HexDistance(map, place, o) <= TempDiffusionRange)
                    && records.TryGetValue((o.Id, record.Technology), out var other) && other.Learned);
                if (heldNearby)
                    continue;
                events.Add(new DemoSettlementEvent(report.Week, report.WorldTime, place.Id, place.Name,
                    DemoSettlementEventKind.KnowledgeLost, DemoTechTree.Of(record.Technology).Index,
                    $"{DemoKnowledge.Name(record.Technology)}失傳：最後的持有者隨聚落消失，附近沒有其他聚落會【TEMP】",
                    EventId: nextEventId++, CauseEventId: ruined.EventId));
            }
        }

        var arrivals = report.Migrations.Where(m => m.Arrived && m.PopulationClosing > 0).ToList();
        foreach (var settlement in sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id))
        {
            var incoming = arrivals.Where(m => m.ToSettlementId == settlement.Id).ToList();
            var food = report.Settlements.FirstOrDefault(l => l.SettlementId == settlement.Id)?.FoodClosing ?? 0;
            var tier = tierOf?.Invoke(settlement.Id) ?? DemoSettlementTier.Settlement;
            var reps = population.Representatives.Where(r => r.SettlementId == settlement.Id).ToList();
            var scholars = reps.Count(r => r.Role == DemoRepresentativeRole.Scholar);
            var traits = cultureTraitsOf?.Invoke(settlement.Id) ?? new HashSet<string>(); // 第 08-9 章 B2：主要文化
            // 第 08-9 章 A3：勢力範圍內（TEMP 3 格）的其他有人聚落，鄰近傳播用。
            var neighbours = map is null ? new List<DemoSettlement>() : sites.Settlements
                .Where(o => o.Id != settlement.Id && !o.IsRuin && HexDistance(map, settlement, o) <= TempDiffusionRange).ToList();
            foreach (var node in DemoTechTree.All)
            {
                var technology = node.Technology;
                var key = (settlement.Id, technology);
                var current = records.GetValueOrDefault(key) ?? new DemoKnowledgeRecord(settlement.Id, technology, 0, 0);
                if (current.Learned || DemoTechTree.IsInitial(technology))
                    continue;
                // 第 08-9 章 A1：前置（ParentTechIDs）全部已習得才累積（08-7a 第 2 節第一道閘門）。
                if (node.Parents.Any(p => !state.Knows(settlement.Id, p)))
                    continue;
                // 第 08-9 章 A2：等級閘門（第 2 階要村莊）。
                if (!TierAllows(node, tier))
                    continue;
                var practitioners = Practitioners(node, settlement, population, map, food, state);
                var carried = incoming.Count == 0 ? 0
                    : incoming.Where(m => state.Knows(m.FromSettlementId, technology)).Sum(m => m.PopulationClosing);
                if (practitioners == 0 && carried == 0)
                    continue;
                // 專家（同領域的代表人物）與學者只在有人從業時加速。
                var specialists = practitioners == 0 ? 0 : reps.Count(r => DemoRepresentative.TechnologyOf(r.Role) is { } held
                    && DemoTechTree.Contains(held) && DemoTechTree.Of(held).Sheet == node.Sheet);
                var experts = practitioners == 0 ? 0 : checked(specialists * TempSpecialistWeight + scholars * TempScholarWeight);
                var threshold = ThresholdOf(technology);
                // 第 08-9 章 A3：鄰近傳播（十字軍之王：鄰近已知者越多越快）與實踐加速，都只在有人從業時。
                var knowers = practitioners == 0 ? 0 : Math.Min(TempDiffusionMaxNeighbours, neighbours.Count(o => state.Knows(o.Id, technology)));
                var diffusion = checked(knowers * threshold * TempDiffusionPerTenThousand / 10_000 * DemoCultures.DiffusionPercent(traits) / 100);
                var boost = practitioners > 0 && !current.Boosted && node.PracticeBoostPercent > 0
                    && PracticeHappened(technology, settlement.Id, report, weekAt)
                    ? checked(threshold * node.PracticeBoostPercent / 100) : 0;
                var own = checked((practitioners + experts) * DemoCultures.ResearchPercent(traits, node) / 100); // 第 08-9 章 B2：文化影響研究
                var gain = checked(own + diffusion + boost + carried * threshold * TempCarryPercent / 100);
                if (gain == 0)
                    continue;
                var progress = Math.Min(threshold, checked(current.Progress + gain));
                var learned = progress >= threshold;
                records[key] = current with { Progress = progress, LearnedWeek = learned ? report.Week : 0, Boosted = current.Boosted || boost > 0 };
                if (!learned)
                    continue;
                var cause = carried > 0
                    ? $"已習得的聚落有 {carried} 人遷入，帶來{Name(technology)}的經驗"
                    : technology == DemoTechnology.Woodworking
                        ? $"{practitioners} 名成年人長年在森林取木的經驗累積"
                        : technology == DemoTechnology.Farming
                            ? $"{practitioners} 名成年人長年採集植物的經驗累積"
                            : $"{practitioners} 名成年人長年{node.Branch}的經驗累積" + (experts > 0 ? "（專家與學者協助）" : "")
                                + (knowers > 0 ? $"（鄰近 {knowers} 個聚落已會）" : "");
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
    public static bool NearForest(DemoHexWorld map, DemoSettlement settlement) => NearTerrain(map, settlement, DemoTerrain.Forest);

    /// <summary>第 08-9 章 A2：聚落本格或六鄰格有該地形。</summary>
    public static bool NearTerrain(DemoHexWorld map, DemoSettlement settlement, DemoTerrain terrain)
    {
        var origin = map.GetCell(settlement.Column, settlement.Row);
        return origin.Terrain == terrain || Neighbours(map, origin).Any(c => c.Terrain == terrain);
    }

    /// <summary>六角格距離（軸向座標）。</summary>
    public static int HexDistance(DemoHexWorld map, DemoSettlement a, DemoSettlement b)
    {
        var x = map.GetCell(a.Column, a.Row);
        var y = map.GetCell(b.Column, b.Row);
        var dq = x.AxialQ - y.AxialQ;
        var dr = x.AxialR - y.AxialR;
        return Math.Max(Math.Abs(dq), Math.Max(Math.Abs(dr), Math.Abs(dq + dr)));
    }

    /// <summary>第 08-9 章 A2：六鄰格有海。</summary>
    public static bool NearSea(DemoHexWorld map, DemoSettlement settlement)
        => Neighbours(map, map.GetCell(settlement.Column, settlement.Row)).Any(c => c.Sea);

    private static IEnumerable<DemoHexCell> Neighbours(DemoHexWorld map, DemoHexCell origin)
        => DemoRouteNetwork.Directions.Select(d => map.AtAxial(origin.AxialQ + d.Dq, origin.AxialR + d.Dr))
            .Where(c => c is not null).Select(c => c!);
}
