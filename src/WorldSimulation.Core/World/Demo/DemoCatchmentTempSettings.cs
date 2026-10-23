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
    string Notes,
    long GrasslandForageWeeklySupply = 0,
    long OpenWaterWeeklySupply = 0,
    int SourceStockCapWeeks = 0)
{
    /// <summary>第 08-1 章：某類自然來源每週可持續供應（建立來源時用的同一組值）。</summary>
    public long WeeklySupply(DemoCatchmentSourceKind kind) => kind switch
    {
        DemoCatchmentSourceKind.Forest => ForestWeeklySupply,
        DemoCatchmentSourceKind.GrasslandForage => GrasslandForageWeeklySupply,
        DemoCatchmentSourceKind.MainRiver => MainRiverWeeklySupply,
        DemoCatchmentSourceKind.OpenWater => OpenWaterWeeklySupply,
        DemoCatchmentSourceKind.Farmland => DemoLandClearing.TempFarmlandWeeklySupply, // 第 08-6 章 c 節
        _ => 0
    };

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
            || GrasslandForageWeeklySupply <= 0 || OpenWaterWeeklySupply <= 0
            || SourceStockCapWeeks < 1
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
        DemoHexWorld map, IReadOnlyCollection<DemoCellRef> explicitMainRiverCells,
        IReadOnlyCollection<DemoCellRef>? artificialOccupation = null)
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

        // Settlements (and future roads/cities with explicit footprint) replace the natural
        // source at the occupied hex. Nearby free sources can still feed the settlement.
        var occupied = artificialOccupation?.ToHashSet() ?? new HashSet<DemoCellRef>();
        var all = new List<DemoCatchmentFoodSource>();
        foreach (var tile in map.Cells)
        {
            if (occupied.Contains(new DemoCellRef(tile.Column, tile.Row))) continue;
            var id = checked(tile.Row * map.Columns + tile.Column + 1);
            if (tile.Terrain == DemoTerrain.Forest)
                all.Add(new DemoCatchmentFoodSource(
                    id, tile.Column, tile.Row, DemoCatchmentSourceKind.Forest, ForestWeeklySupply));
            else if (tile.Terrain == DemoTerrain.Grassland)
                all.Add(new DemoCatchmentFoodSource(
                    id, tile.Column, tile.Row, DemoCatchmentSourceKind.GrasslandForage, GrasslandForageWeeklySupply));
            else if (rivers.Contains(new DemoCellRef(tile.Column, tile.Row)))
                all.Add(new DemoCatchmentFoodSource(
                    id, tile.Column, tile.Row, DemoCatchmentSourceKind.MainRiver, MainRiverWeeklySupply));
            else if (tile.Terrain == DemoTerrain.Water)
                all.Add(new DemoCatchmentFoodSource(
                    id, tile.Column, tile.Row, DemoCatchmentSourceKind.OpenWater, OpenWaterWeeklySupply));
        }
        return all.AsReadOnly();
    }
}
