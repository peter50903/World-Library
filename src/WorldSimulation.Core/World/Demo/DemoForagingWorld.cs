namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #113 TEMP playable forest/river scenario. Source budgets come from the embedded
/// config/demo/terrain-food-temp.json. Existing terrain is a placeholder, so explicit
/// main-river tiles are picked deterministically from some river-bank-like water cells.
/// Ordinary water tiles without this explicit marker never generate food.
/// </summary>
public static class DemoForagingWorld
{
    public static DemoCatchmentTempSettings Settings { get; } = LoadSettings();

    private static DemoCatchmentTempSettings LoadSettings()
    {
        using var stream = typeof(DemoForagingWorld).Assembly
            .GetManifestResourceStream("WorldSimulation.TerrainFoodTemp");
        if (stream is null)
            throw new InvalidOperationException("Embedded TEMP forest/river settings not found.");
        using var reader = new StreamReader(stream);
        return DemoCatchmentTempSettings.FromJson(reader.ReadToEnd());
    }

    /// <summary>
    /// TEMP river-main-channel geometry. Do not mistake this display fixture for
    /// the formal RouteSegment river system. No food is generated for unmarked water.
    /// </summary>
    public static IReadOnlyList<DemoCellRef> MainRiverTiles(DemoHexWorld map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return Array.AsReadOnly(map.Cells
            .Where(c => c.Terrain == DemoTerrain.Water && (c.Column + c.Row) % 3 == 0
                && map.Cells.Any(l => l.Terrain != DemoTerrain.Water
                    && DemoHexFoodCatchment.IsInReach(c, l, false)))
            .Select(c => new DemoCellRef(c.Column, c.Row)).ToArray());
    }

    public static IReadOnlyList<DemoCatchmentFoodSource> Sources(DemoHexWorld map)
        => Settings.BuildPotentialSources(map, MainRiverTiles(map));

    /// <summary>
    /// Food harvest is a one-week, one-source budget. No local farm and no source-specific
    /// accumulated stores in this TEMP mode: unharvested forage is left in nature, and only
    /// gathered food is counted in settlement stocks. Other scenarios retain old resources.
    /// </summary>
    public static DemoCatchmentFoodResult Allocate(
        DemoHexWorld map, DemoWorldSites sites,
        IReadOnlyDictionary<int, long> openingPopulation,
        DemoWeeklyTempConfig weekly)
    {
        if (!weekly.SharedCatchmentEnabled)
            throw new InvalidOperationException("Not a shared catchment world.");
        var claims = sites.Settlements.OrderBy(s => s.Id).Select(s =>
            new DemoCatchmentFoodClaim(s.Id, s.Column, s.Row,
                checked(openingPopulation[s.Id] * weekly.FoodPerPersonPerWeek),
                checked(openingPopulation[s.Id] * weekly.YieldPerWorkerPerWeek)));
        return DemoHexFoodCatchment.Allocate(map, Sources(map), claims);
    }
}
