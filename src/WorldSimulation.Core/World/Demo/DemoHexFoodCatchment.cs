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

/// <summary>
/// A pure, deterministic weekly food-allocation model. Each resource is accounted once,
/// even if many neighbours can use it; each settlement has a single aggregate demand
/// and a single real workforce harvest capacity across ALL eligible food sources.
/// Allocation order (TEMP, pending formal rule): source ID, then settlement ID.
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
        IEnumerable<DemoCatchmentFoodClaim> claims)
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
            var remaining = source.FoodAvailable;
            var origin = map.GetCell(source.Column, source.Row);
            foreach (var claim in claimList)
            {
                if (remaining == 0) break;
                var cell = map.GetCell(claim.Column, claim.Row);
                if (!IsInReach(origin, cell,
                    source.Kind is DemoCatchmentSourceKind.Forest or DemoCatchmentSourceKind.GrasslandForage))
                    continue;
                var take = Math.Min(remaining, Math.Min(needs[claim.SettlementId], labour[claim.SettlementId]));
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
