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

/// <summary>
/// TEMP 示範聚落。Id 是示範用序號，不是正式 EntityID；只會遞增、永不重用。
/// V0-06B：<paramref name="FoundedWeek"/>＝0 表示開局就存在；大於 0 表示由遷徙隊伍在該週建立（存在起始週次），
/// <paramref name="FoundedFromSettlementId"/> 是出發聚落、<paramref name="FoundedByBatchId"/> 是建立它的遷徙隊伍。
/// </summary>
public sealed record DemoSettlement(
    int Id,
    string Name,
    int Column,
    int Row,
    int FoundedWeek = 0,
    int FoundedFromSettlementId = 0,
    int FoundedByBatchId = 0)
{
    /// <summary>V0-06B：是否為遷徙隊伍建立的新聚落。</summary>
    public bool IsFounded => FoundedWeek > 0;
}

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

    /// <summary>V0-06B：下一個新聚落編號（只會遞增，永不重用）。</summary>
    public int NextSettlementId => Settlements.Count == 0 ? 1 : checked(Settlements.Max(s => s.Id) + 1);

    /// <summary>V0-06B：下一個新資源點編號（只會遞增，永不重用）。</summary>
    public int NextResourcePointId => ResourcePoints.Count == 0 ? 1 : checked(ResourcePoints.Max(p => p.Id) + 1);

    /// <summary>
    /// V0-06B：加上一個遷徙隊伍建立的新聚落與它所在格的資源點，回傳新的唯讀副本（不改動自己）。
    /// 新聚落沒有初始人口群體：人口全部來自遷徙隊伍，記在每週人口帳的遷入（M1_SETTLE）。
    /// </summary>
    internal DemoWorldSites WithFoundedSettlement(DemoSettlement settlement, DemoHexWorld map)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(map);
        if (!settlement.IsFounded || settlement.Id != NextSettlementId)
            throw new InvalidOperationException("A founded settlement needs the next unused ID and a founding week.");
        if (SettlementAt(settlement.Column, settlement.Row) is not null)
            throw new InvalidOperationException("A settlement already stands on this cell.");
        var cell = map.GetCell(settlement.Column, settlement.Row);
        if (cell.Terrain == DemoTerrain.Water)
            throw new InvalidOperationException("TEMP: settlements cannot be founded on water cells.");

        var point = new DemoResourcePoint(NextResourcePointId, settlement.Id, KindFor(cell.Terrain),
            settlement.Column, settlement.Row);
        return new DemoWorldSites(
            Array.AsReadOnly(Settlements.Append(settlement).ToArray()),
            Array.AsReadOnly(ResourcePoints.Append(point).ToArray()),
            PopGroups);
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
