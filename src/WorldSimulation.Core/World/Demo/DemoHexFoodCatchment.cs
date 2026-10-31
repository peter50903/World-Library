namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #112 TEMP terrain catchment source: a forest may feed itself and its six direct neighbours;
/// a main-channel river feeds adjacent land only. A river source must be explicitly marked:
/// ordinary DemoTerrain.Water is NOT automatically a river.
/// Not yet wired into weekly settlement accounting (#113).
/// </summary>
public enum DemoCatchmentSourceKind
{
    Forest = 1,
    MainRiver = 2,
    GrasslandForage = 3,
    OpenWater = 4,

    /// <summary>第 08-6 章 c 節：開墾完成的農田（草原格；供應見 <see cref="DemoLandClearing.TempFarmlandWeeklySupply"/>）。</summary>
    Farmland = 5
}

/// <summary>第 08-6 章 c 節：來源種類的可及規則。</summary>
public static class DemoCatchmentSourceKinds
{
    /// <summary>陸地來源（森林、草原野採、農田）可餵本格與六鄰格；水域來源只餵相鄰陸地。</summary>
    public static bool IncludesOrigin(DemoCatchmentSourceKind kind)
        => kind is DemoCatchmentSourceKind.Forest or DemoCatchmentSourceKind.GrasslandForage or DemoCatchmentSourceKind.Farmland;
}

/// <param name="FoodAvailable">The source's one shared, already-evidenced weekly harvest budget,
/// NOT a grant for each adjacent settlement. This slice does not regenerate reserves.</param>
public sealed record DemoCatchmentFoodSource(
    int Id,
    int Column,
    int Row,
    DemoCatchmentSourceKind Kind,
    long FoodAvailable);

/// <summary>Existing settlement claims at an actual land cell.
/// HarvestCapacity is its available real labour budget, not population count.</summary>
public sealed record DemoCatchmentFoodClaim(
    int SettlementId,
    int Column,
    int Row,
    long FoodNeeded,
    long HarvestCapacity);

public sealed record DemoCatchmentHarvest(
    int SourceId,
    int SettlementId,
    long FoodHarvested);

public sealed record DemoCatchmentFoodResult(
    IReadOnlyList<DemoCatchmentHarvest> Harvests,
    IReadOnlyDictionary<int, long> UnmetDemand,
    IReadOnlyDictionary<int, long> UnharvestedSourceFood)
{
    public long TotalHarvested => checked(Harvests.Sum(h => h.FoodHarvested));
}

/// <summary>第 06 章 c 節：同一來源內的分配方式。正式規則是依成年勞力比例；貪婪只供對照測試。</summary>
public enum DemoCatchmentAllocation
{
    /// <summary>依各聚落成年勞力（採集能力）比例分配，取整餘數依聚落 ID 遞增逐一分配。</summary>
    LabourProportional = 0,

    /// <summary>舊規則：依聚落 ID 順序先到先得（只供對照「可避免的餓死」）。</summary>
    Greedy = 1
}

/// <summary>
/// A pure, deterministic weekly food-allocation model. Each resource is accounted once,
/// even if many neighbours can use it; each settlement has a single aggregate demand
/// and a single real workforce harvest capacity across ALL eligible food sources.
/// 第 06 章 c 節：來源依編號處理；同一來源依各聚落成年勞力比例分配（每聚落不超過剩餘需求與勞力），
/// 取整餘數依聚落 ID 遞增逐一分配，可重現。
/// NO food, terrain, events, stores, or population are added by this calculation.
/// </summary>
public static class DemoHexFoodCatchment
{
    /// <summary>True for the same hex (when includeOrigin is true), or one of six direct neighbours.</summary>
    public static bool IsInReach(DemoHexCell origin, DemoHexCell target, bool includeOrigin)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentNullException.ThrowIfNull(target);
        var dq = origin.AxialQ - target.AxialQ;
        var dr = origin.AxialR - target.AxialR;
        var hexDistance = Math.Max(Math.Abs(dq), Math.Max(Math.Abs(dr), Math.Abs(dq + dr)));
        return hexDistance == 1 || (includeOrigin && hexDistance == 0);
    }

    /// <summary>Forest: self plus neighbours (including forest or grassland);
    /// river: adjacent LAND only, never the river cell or any other water.</summary>
    public static IReadOnlyList<DemoHexCell> EligibleCells(
        DemoHexWorld map, DemoCatchmentFoodSource source)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(source);
        ValidateSource(map, source);
        // 第 08-13 章第 5 節：可及格只取決於地圖、來源位置與種類，每張地圖快取（每週每個來源都會查）。
        var cache = EligibleCache.GetValue(map, _ => new Dictionary<(int, int, DemoCatchmentSourceKind), IReadOnlyList<DemoHexCell>>());
        var key = (source.Column, source.Row, source.Kind);
        if (!cache.TryGetValue(key, out var cells))
            cache[key] = cells = ComputeEligibleCells(map, source);
        return cells;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DemoHexWorld,
        Dictionary<(int, int, DemoCatchmentSourceKind), IReadOnlyList<DemoHexCell>>> EligibleCache = new();

    private static IReadOnlyList<DemoHexCell> ComputeEligibleCells(DemoHexWorld map, DemoCatchmentFoodSource source)
    {
        var origin = map.GetCell(source.Column, source.Row);
        // 第 07-1 章 c 節：只看本格與六個鄰格（原本逐格掃全圖，結果相同）。
        var nearby = DemoRouteNetwork.Directions.Select(d => map.AtAxial(origin.AxialQ + d.Dq, origin.AxialR + d.Dr))
            .Where(c => c is not null).Select(c => c!).Prepend(origin);
        return Array.AsReadOnly(nearby.Where(c =>
            c.Terrain != DemoTerrain.Water
            && IsInReach(origin, c,
                DemoCatchmentSourceKinds.IncludesOrigin(source.Kind)))
            .OrderBy(c => c.Row).ThenBy(c => c.Column).ToArray());
    }

    /// <summary>
    /// Rejects any claims for non-land tiles, duplicate settlements and source cells,
    /// plus all negative demands, capacities and source budgets. Demand and capacity are
    /// deliberately separate so future children cannot be counted as harvest workers.
    /// </summary>
    public static DemoCatchmentFoodResult Allocate(
        DemoHexWorld map,
        IEnumerable<DemoCatchmentFoodSource> sources,
        IEnumerable<DemoCatchmentFoodClaim> claims,
        DemoCatchmentAllocation mode = DemoCatchmentAllocation.LabourProportional,
        IReadOnlyDictionary<int, long>? baseNeeds = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(claims);

        var sourceList = sources.OrderBy(s => s.Id).ToArray();
        var claimList = claims.OrderBy(c => c.SettlementId).ToArray();
        if (sourceList.Any(s => s is null || s.Id <= 0 || s.FoodAvailable < 0)
            || sourceList.Select(s => s.Id).Distinct().Count() != sourceList.Length
            || sourceList.Select(s => (s.Column, s.Row)).Distinct().Count() != sourceList.Length)
            throw new InvalidOperationException("Duplicated or invalid TEMP food sources.");
        if (claimList.Any(c => c is null || c.SettlementId <= 0
                || c.FoodNeeded < 0 || c.HarvestCapacity < 0)
            || claimList.Select(c => c.SettlementId).Distinct().Count() != claimList.Length
            || claimList.Select(c => (c.Column, c.Row)).Distinct().Count() != claimList.Length)
            throw new InvalidOperationException("Duplicated or invalid TEMP settlement claims.");
        foreach (var source in sourceList)
            ValidateSource(map, source);
        foreach (var claim in claimList)
        {
            DemoHexCell site;
            try { site = map.GetCell(claim.Column, claim.Row); }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException("A TEMP settlement is outside the map.");
            }
            if (site.Terrain == DemoTerrain.Water)
                throw new InvalidOperationException("TEMP river/water source tiles cannot host settlements.");
        }

        var needs = claimList.ToDictionary(c => c.SettlementId, c => c.FoodNeeded);
        var labour = claimList.ToDictionary(c => c.SettlementId, c => c.HarvestCapacity);
        var remainingSources = sourceList.ToDictionary(s => s.Id, s => s.FoodAvailable);
        var taken = new Dictionary<(int Source, int Settlement), long>();
        // 第 08-13 章第 5 節：只看來源本格與六鄰格上的聚落（可及範圍就是這幾格），不再逐一比對全部聚落；
        // 結果仍依聚落編號排序，與原本相同。
        var claimAt = claimList.ToDictionary(c => (c.Column, c.Row));
        var eligibleOf = sourceList.ToDictionary(s => s.Id, s =>
        {
            var origin = map.GetCell(s.Column, s.Row);
            var found = new List<DemoCatchmentFoodClaim>(4);
            if (DemoCatchmentSourceKinds.IncludesOrigin(s.Kind) && claimAt.TryGetValue((origin.Column, origin.Row), out var self))
                found.Add(self);
            foreach (var d in DemoRouteNetwork.Directions)
                if (map.AtAxial(origin.AxialQ + d.Dq, origin.AxialR + d.Dr) is { } n && claimAt.TryGetValue((n.Column, n.Row), out var c))
                    found.Add(c);
            return found.OrderBy(c => c.SettlementId).ToArray();
        });

        void Pass(IEnumerable<DemoCatchmentFoodSource> order, Dictionary<int, long> want)
        {
            foreach (var source in order)
            {
                var eligible = eligibleOf[source.Id];
                var takes = mode == DemoCatchmentAllocation.Greedy
                    ? GreedyShares(remainingSources[source.Id], eligible, want, labour)
                    : ProportionalShares(remainingSources[source.Id], eligible, want, labour);
                foreach (var claim in eligible)
                {
                    var take = takes.GetValueOrDefault(claim.SettlementId);
                    if (take == 0) continue;
                    taken[(source.Id, claim.SettlementId)] = taken.GetValueOrDefault((source.Id, claim.SettlementId)) + take;
                    remainingSources[source.Id] -= take;
                    want[claim.SettlementId] -= take;
                    if (!ReferenceEquals(want, needs)) needs[claim.SettlementId] -= take;
                    labour[claim.SettlementId] -= take;
                }
            }
        }

        if (baseNeeds is null)
            Pass(sourceList, needs);
        else
        {
            // 第 08-3 章【TEMP 規則】（人類採集世界）兩階段分配：先只分本週需求，而且先分「只有少數聚落可及」的來源
            // （各聚落先用自己獨享的來源），共用來源留給沒有其他來源的聚落；需求都分完後，剩下的才給補存糧。
            // 避免某聚落用共用來源補存糧、鄰村卻挨餓。
            var first = claimList.ToDictionary(c => c.SettlementId, c => Math.Min(c.FoodNeeded, Math.Max(0, baseNeeds.GetValueOrDefault(c.SettlementId))));
            // 只計真的要採的聚落（有需求、有勞力）；沒有勞力的聚落（例如本週剛建立）不影響順序。
            Pass(sourceList.OrderBy(s => eligibleOf[s.Id].Count(c => c.HarvestCapacity > 0 && first[c.SettlementId] > 0)).ThenBy(s => s.Id), first);
            Pass(sourceList, needs);
        }

        var harvest = taken.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key.Source).ThenBy(kv => kv.Key.Settlement)
            .Select(kv => new DemoCatchmentHarvest(kv.Key.Source, kv.Key.Settlement, kv.Value)).ToList();
        var remainingSorted = new SortedDictionary<int, long>(remainingSources);
        // The source and claimant budgets are each conserved, with no invented provisions.
        var bySource = new Dictionary<int, long>();
        var bySettlement = new Dictionary<int, long>();
        foreach (var h in harvest)
        {
            bySource[h.SourceId] = bySource.GetValueOrDefault(h.SourceId) + h.FoodHarvested;
            bySettlement[h.SettlementId] = bySettlement.GetValueOrDefault(h.SettlementId) + h.FoodHarvested;
        }
        if (sourceList.Any(s => bySource.GetValueOrDefault(s.Id) > s.FoodAvailable)
            || claimList.Any(c => bySettlement.GetValueOrDefault(c.SettlementId) > Math.Min(c.FoodNeeded, c.HarvestCapacity)))
            throw new InvalidOperationException("TEMP food catchment resource conservation failed.");
        return new DemoCatchmentFoodResult(harvest.AsReadOnly(), needs, remainingSorted);
    }

    private static Dictionary<int, long> GreedyShares(long available, IReadOnlyList<DemoCatchmentFoodClaim> eligible,
        IReadOnlyDictionary<int, long> needs, IReadOnlyDictionary<int, long> labour)
    {
        var takes = new Dictionary<int, long>();
        foreach (var claim in eligible)
        {
            if (available == 0) break;
            var take = Math.Min(available, Math.Min(needs[claim.SettlementId], labour[claim.SettlementId]));
            takes[claim.SettlementId] = take;
            available -= take;
        }
        return takes;
    }

    /// <summary>
    /// 第 06 章 c 節：依成年勞力（本週原始採集能力）比例注水分配——每輪把剩餘量依權重按比例取整分給還沒滿的聚落
    /// （不超過其剩餘需求與勞力），取整後一點都分不出去時，餘數依聚落 ID 遞增每個 1 單位；直到分完或全部滿。
    /// </summary>
    private static Dictionary<int, long> ProportionalShares(long available, IReadOnlyList<DemoCatchmentFoodClaim> eligible,
        IReadOnlyDictionary<int, long> needs, IReadOnlyDictionary<int, long> labour)
    {
        var takes = eligible.ToDictionary(c => c.SettlementId, _ => 0L);
        long Room(DemoCatchmentFoodClaim c) => Math.Min(needs[c.SettlementId], labour[c.SettlementId]) - takes[c.SettlementId];
        while (available > 0)
        {
            var open = eligible.Where(c => Room(c) > 0 && c.HarvestCapacity > 0).ToArray();
            if (open.Length == 0)
                break;
            var totalWeight = open.Aggregate((Int128)0, (sum, c) => sum + c.HarvestCapacity);
            long distributed = 0;
            foreach (var claim in open)
            {
                var share = (long)((Int128)available * claim.HarvestCapacity / totalWeight);
                var give = Math.Min(share, Room(claim));
                takes[claim.SettlementId] += give;
                distributed += give;
            }
            if (distributed == 0)
            {
                foreach (var claim in open.OrderBy(c => c.SettlementId))
                {
                    if (available - distributed == 0) break;
                    takes[claim.SettlementId] += 1;
                    distributed += 1;
                }
            }
            available -= distributed;
        }
        return takes;
    }

    private static void ValidateSource(DemoHexWorld map, DemoCatchmentFoodSource source)
    {
        if (source.Id <= 0 || source.FoodAvailable < 0 || !Enum.IsDefined(source.Kind))
            throw new InvalidOperationException("Invalid TEMP catchment source.");
        DemoHexCell tile;
        try { tile = map.GetCell(source.Column, source.Row); }
        catch (ArgumentOutOfRangeException)
        {
            throw new InvalidOperationException("A TEMP food source is outside the map.");
        }
        if (source.Kind == DemoCatchmentSourceKind.Forest && tile.Terrain != DemoTerrain.Forest)
            throw new InvalidOperationException("A forest food source must actually be on forest terrain.");
        if (source.Kind is DemoCatchmentSourceKind.MainRiver or DemoCatchmentSourceKind.OpenWater
            && tile.Terrain != DemoTerrain.Water)
            throw new InvalidOperationException("Water food requires a water cell.");
        if (source.Kind == DemoCatchmentSourceKind.Farmland && tile.Terrain != DemoTerrain.Grassland)
            throw new InvalidOperationException("Farmland must be on grassland.");
        // 第 06-3 章：丘陵也是野採來源（草原的一半）。
        if (source.Kind == DemoCatchmentSourceKind.GrasslandForage && tile.Terrain is not (DemoTerrain.Grassland or DemoTerrain.Hills))
            throw new InvalidOperationException("Grassland foraging must be on grassland or hills.");
    }
}
