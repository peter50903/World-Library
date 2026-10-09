using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>TEMP resource-point kinds for the V0-03B fixture only; NOT official resource definitions.</summary>
public enum DemoResourceKind
{
    /// <summary>農地（TEMP）：位於草原格。</summary>
    Farmland = 0,

    /// <summary>林地（TEMP）：位於森林格。</summary>
    Woodland = 1,

    /// <summary>漁場（TEMP）：位於水域格。</summary>
    Fishery = 2
}

/// <summary>TEMP 示範聚落。Id 是示範用序號，不是正式 EntityID。</summary>
public sealed record DemoSettlement(int Id, string Name, int Column, int Row);

/// <summary>TEMP 示範資源點，屬於一個示範聚落，位於該聚落所在格。</summary>
public sealed record DemoResourcePoint(int Id, int SettlementId, DemoResourceKind Kind, int Column, int Row);

/// <summary>TEMP 示範人口群體，屬於一個示範聚落。Size 只是示範人數，不是正式人口規則。</summary>
public sealed record DemoPopGroup(int Id, int SettlementId, int Size);

/// <summary>
/// TEMP（暫定）示範設定：聚落數量範圍、人口群體人數範圍與聚落名稱。
/// 這些值沒有定案，只供 V0-03B 示範畫面使用；不得寫回正式設定檔或 Notion 規則。
/// </summary>
public sealed record DemoSitesTempConfig(
    int MinSettlements,
    int MaxSettlements,
    int MinPopGroupSize,
    int MaxPopGroupSize,
    IReadOnlyList<string> SettlementNames)
{
    /// <summary>TEMP：2～4 個聚落（03 §3 V0-03）；人口群體 3～8 人（TEMP，未定案）。</summary>
    public static DemoSitesTempConfig Sample { get; } = new(
        MinSettlements: 2,
        MaxSettlements: 4,
        MinPopGroupSize: 3,
        MaxPopGroupSize: 8,
        SettlementNames: Array.AsReadOnly(new[] { "示範聚落甲", "示範聚落乙", "示範聚落丙", "示範聚落丁" }));
}

/// <summary>
/// V0-03B TEMP world skeleton: settlements, one resource point per settlement and one
/// population group per settlement, generated from the SAME world RngRegistry as the map.
/// Stream use (isolated from GEOGRAPHY terrain and from the daily RESOURCE harvest draws):
/// ECOLOGY_INIT → settlement count and placement; POP → population-group sizes.
/// Resource kind follows the settlement cell's terrain and consumes no RNG.
/// </summary>
public sealed class DemoWorldSites
{
    private DemoWorldSites(
        IReadOnlyList<DemoSettlement> settlements,
        IReadOnlyList<DemoResourcePoint> resourcePoints,
        IReadOnlyList<DemoPopGroup> popGroups)
    {
        Settlements = settlements;
        ResourcePoints = resourcePoints;
        PopGroups = popGroups;
    }

    public IReadOnlyList<DemoSettlement> Settlements { get; }

    public IReadOnlyList<DemoResourcePoint> ResourcePoints { get; }

    public IReadOnlyList<DemoPopGroup> PopGroups { get; }

    public static DemoWorldSites Generate(DemoHexWorld map, RngRegistry rng)
        => Generate(map, rng, DemoSitesTempConfig.Sample);

    public static DemoWorldSites Generate(DemoHexWorld map, RngRegistry rng, DemoSitesTempConfig config)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(config);
        if (config.MinSettlements < 1 || config.MaxSettlements < config.MinSettlements)
            throw new ArgumentOutOfRangeException(nameof(config), "Invalid TEMP settlement range.");
        if (config.SettlementNames is null || config.SettlementNames.Count < config.MaxSettlements)
            throw new ArgumentOutOfRangeException(nameof(config), "Not enough TEMP settlement names.");
        if (config.MinPopGroupSize < 1 || config.MaxPopGroupSize < config.MinPopGroupSize)
            throw new ArgumentOutOfRangeException(nameof(config), "Invalid TEMP population-group range.");
        if (config.MaxSettlements > map.Cells.Count)
            throw new ArgumentOutOfRangeException(nameof(config), "More settlements than map cells.");
        if (rng.GetDrawCounter(RngStreams.EcologyInit) != 0 || rng.GetDrawCounter(RngStreams.Pop) != 0)
            throw new InvalidOperationException("Demo site generation requires unused ECOLOGY_INIT and POP streams.");

        var span = (ulong)(config.MaxSettlements - config.MinSettlements + 1);
        var count = config.MinSettlements + (int)(rng.NextUInt64(RngStreams.EcologyInit) % span);

        // Prefer land cells (row-major order); fall back to any free cell only if land is short.
        var land = map.Cells.Where(c => c.Terrain != DemoTerrain.Water).ToList();
        var water = map.Cells.Where(c => c.Terrain == DemoTerrain.Water).ToList();

        var settlements = new List<DemoSettlement>(count);
        var resources = new List<DemoResourcePoint>(count);
        var pops = new List<DemoPopGroup>(count);

        for (var i = 0; i < count; i++)
        {
            var pool = land.Count > 0 ? land : water;
            var pick = (int)(rng.NextUInt64(RngStreams.EcologyInit) % (ulong)pool.Count);
            var cell = pool[pick];
            pool.RemoveAt(pick);

            var id = i + 1;
            settlements.Add(new DemoSettlement(id, config.SettlementNames[i], cell.Column, cell.Row));
            resources.Add(new DemoResourcePoint(id, id, KindFor(cell.Terrain), cell.Column, cell.Row));

            var popSpan = (ulong)(config.MaxPopGroupSize - config.MinPopGroupSize + 1);
            var size = config.MinPopGroupSize + (int)(rng.NextUInt64(RngStreams.Pop) % popSpan);
            pops.Add(new DemoPopGroup(id, id, size));
        }

        return new DemoWorldSites(
            Array.AsReadOnly(settlements.ToArray()),
            Array.AsReadOnly(resources.ToArray()),
            Array.AsReadOnly(pops.ToArray()));
    }

    /// <summary>Settlement at the offset cell, or null. Pure query.</summary>
    public DemoSettlement? SettlementAt(int column, int row)
        => Settlements.FirstOrDefault(s => s.Column == column && s.Row == row);

    public IReadOnlyList<DemoResourcePoint> ResourcePointsOf(int settlementId)
        => Array.AsReadOnly(ResourcePoints.Where(r => r.SettlementId == settlementId).ToArray());

    public IReadOnlyList<DemoPopGroup> PopGroupsOf(int settlementId)
        => Array.AsReadOnly(PopGroups.Where(p => p.SettlementId == settlementId).ToArray());

    private static DemoResourceKind KindFor(DemoTerrain terrain) => terrain switch
    {
        DemoTerrain.Grassland => DemoResourceKind.Farmland,
        DemoTerrain.Forest => DemoResourceKind.Woodland,
        DemoTerrain.Water => DemoResourceKind.Fishery,
        _ => throw new InvalidOperationException("Unknown TEMP terrain value.")
    };
}
