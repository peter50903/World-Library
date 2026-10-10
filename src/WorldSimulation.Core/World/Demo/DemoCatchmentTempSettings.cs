using System.Text.Json;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #112 TEMP-only machine-readable source budgets. Read from config/demo/terrain-food-temp.json;
/// NOT a formal world parameter and NOT connected to the existing weekly ledger before #113.
/// In particular, ordinary water cells are not automatically river food sources.
/// </summary>
public sealed record DemoCatchmentTempSettings(
    int SchemaVersion,
    string Status,
    string ApplicableRace,
    string FoodUnit,
    long ForestWeeklySupply,
    long MainRiverWeeklySupply,
    long UncultivatedFarmlandWeeklySupply,
    string Allocation,
    string ForestReach,
    string MainRiverReach,
    string Notes)
{
    public static DemoCatchmentTempSettings FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Missing TEMP catchment settings.");
        DemoCatchmentTempSettings? settings;
        try { settings = JsonSerializer.Deserialize<DemoCatchmentTempSettings>(json); }
        catch (JsonException ex) { throw new InvalidOperationException("Malformed TEMP catchment settings.", ex); }
        if (settings is null) throw new InvalidOperationException("Empty TEMP catchment settings.");
        settings.Validate();
        return settings;
    }

    public void Validate()
    {
        if (SchemaVersion != 1 || Status != "TEMP" || ApplicableRace != "RACE_HUMAN"
            || FoodUnit != "food_unit_per_source_hex_per_week"
            || ForestWeeklySupply < 0 || MainRiverWeeklySupply < 0
            || UncultivatedFarmlandWeeklySupply != 0
            || Allocation != "ONE_SOURCE_TOTAL"
            || ForestReach != "SELF_AND_SIX_ADJACENT_LAND"
            || MainRiverReach != "SIX_ADJACENT_LAND_ONLY")
            throw new InvalidOperationException("TEMP catchment settings violate current demonstration rules.");
    }

    /// <summary>
    /// A unique source for every forest tile, and a river source only for an explicitly
    /// declared main-channel water tile; the caller must supply that river evidence.
    /// This does not mint weekly food: FoodAvailable is a provisional potential budget,
    /// still subject to valid labour/harvest and later actual stock accounting.
    /// No grassland becomes Farmland automatically.
    /// </summary>
    public IReadOnlyList<DemoCatchmentFoodSource> BuildPotentialSources(
        DemoHexWorld map, IReadOnlyCollection<DemoCellRef> explicitMainRiverCells)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(explicitMainRiverCells);
        Validate();
        if (explicitMainRiverCells.Count != explicitMainRiverCells.Distinct().Count())
            throw new InvalidOperationException("Duplicate explicit main-river tile.");

        var rivers = explicitMainRiverCells.ToHashSet();
        foreach (var cell in rivers)
        {
            DemoHexCell sourceTile;
            try { sourceTile = map.GetCell(cell.Column, cell.Row); }
            catch (ArgumentOutOfRangeException ex)
            {
                throw new InvalidOperationException("Main-river source tile is off the map.", ex);
            }
            if (sourceTile.Terrain != DemoTerrain.Water)
                throw new InvalidOperationException("Main-river source must be explicit water terrain.");
        }

        var all = new List<DemoCatchmentFoodSource>();
        foreach (var tile in map.Cells)
        {
            var id = checked(tile.Row * DemoHexWorld.Columns + tile.Column + 1);
            if (tile.Terrain == DemoTerrain.Forest)
                all.Add(new DemoCatchmentFoodSource(
                    id, tile.Column, tile.Row, DemoCatchmentSourceKind.Forest, ForestWeeklySupply));
            else if (rivers.Contains(new DemoCellRef(tile.Column, tile.Row)))
                all.Add(new DemoCatchmentFoodSource(
                    id, tile.Column, tile.Row, DemoCatchmentSourceKind.MainRiver, MainRiverWeeklySupply));
        }
        return all.AsReadOnly();
    }
}
