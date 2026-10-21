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
    OpenWater = 4
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
        var origin = map.GetCell(source.Column, source.Row);
        return Array.AsReadOnly(map.Cells.Where(c =>
            c.Terrain != DemoTerrain.Water
            && IsInReach(origin, c,
                source.Kind is DemoCatchmentSourceKind.Forest or DemoCatchmentSourceKind.GrasslandForage))
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
        DemoCatchmentAllocation mode = DemoCatchmentAllocation.LabourProportional)
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
        var remainingSources = new SortedDictionary<int, long>();
        var harvest = new List<DemoCatchmentHarvest>();
        foreach (var source in sourceList)
        {
            var origin = map.GetCell(source.Column, source.Row);
            var eligible = claimList.Where(claim => IsInReach(origin, map.GetCell(claim.Column, claim.Row),
                source.Kind is DemoCatchmentSourceKind.Forest or DemoCatchmentSourceKind.GrasslandForage)).ToArray();
            var takes = mode == DemoCatchmentAllocation.Greedy
                ? GreedyShares(source.FoodAvailable, eligible, needs, labour)
                : ProportionalShares(source.FoodAvailable, eligible, needs, labour);
            var remaining = source.FoodAvailable;
            foreach (var claim in eligible)
            {
                var take = takes.GetValueOrDefault(claim.SettlementId);
                if (take == 0) continue;
                harvest.Add(new DemoCatchmentHarvest(source.Id, claim.SettlementId, take));
                remaining -= take;
                needs[claim.SettlementId] -= take;
                labour[claim.SettlementId] -= take;
            }
            remainingSources.Add(source.Id, remaining);
        }
        // The source and claimant budgets are each conserved, with no invented provisions.
        if (sourceList.Any(s => harvest.Where(h => h.SourceId == s.Id).Sum(h => h.FoodHarvested) > s.FoodAvailable)
            || claimList.Any(c => harvest.Where(h => h.SettlementId == c.SettlementId)
                .Sum(h => h.FoodHarvested) > Math.Min(c.FoodNeeded, c.HarvestCapacity)))
            throw new InvalidOperationException("TEMP food catchment resource conservation failed.");
        return new DemoCatchmentFoodResult(harvest.AsReadOnly(), needs, remainingSources);
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
            var totalWeight = open.Sum(c => (Int128)c.HarvestCapacity);
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
        if (source.Kind == DemoCatchmentSourceKind.GrasslandForage && tile.Terrain != DemoTerrain.Grassland)
            throw new InvalidOperationException("Grassland foraging must be on undeveloped grassland.");
    }
}
