using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>TEMP resource-point kinds for the V0-03B fixture only; NOT official resource definitions.</summary>
public enum DemoResourceKind
{
    /// <summary>林地（TEMP）：位於森林格。</summary>
    Woodland = 1,

    /// <summary>漁場（TEMP）：位於水域格。</summary>
    Fishery = 2,
    /// <summary>TEMP 未開發草原：不自動產農糧（第 08 章 a 節起所有情境的草原都是這種；舊「農地＝0」退場）。</summary>
    Uncultivated = 3
}

/// <summary>
/// TEMP 示範聚落。Id 由世界唯一實體 ID 分配器發號（C04／P1.0.03-a；與資源點、人口群體、隊伍、個人共用一個 ID 空間）；只會遞增、永不重用。
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
    int FoundedByBatchId = 0,
    int RuinedWeek = 0)
{
    /// <summary>V0-06B：是否為遷徙隊伍建立的新聚落。</summary>
    public bool IsFounded => FoundedWeek > 0;

    /// <summary>第 07 章 c 節：人口連續為 0 滿 TEMP 週數後成為遺跡（保留編號與歷史；不佔格、不是遷徙目的地）。</summary>
    public bool IsRuin => RuinedWeek > 0;
}

/// <summary>TEMP 示範資源點，屬於一個示範聚落，位於該聚落所在格。Id 由世界唯一實體 ID 分配器發號。</summary>
public sealed record DemoResourcePoint(int Id, int SettlementId, DemoResourceKind Kind, int Column, int Row);

/// <summary>TEMP 示範人口群體，屬於一個示範聚落。Id 由世界唯一實體 ID 分配器發號。Size 只是示範人數，不是正式人口規則。</summary>
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
/// Stream use (isolated from GEOGRAPHY terrain): ECOLOGY_INIT only (41A SEED_ECOLOGY_INIT, world seed) —
/// first settlement count and placement (unchanged order), THEN a second pass of population-group sizes.
/// C05（P1.0.04-a）：初始人口不再抽歷史流 POP（POP 只管出生／死亡／繁殖），只換歷史種子時地圖與初始人口完全相同。
/// Resource kind follows the settlement cell's terrain and consumes no RNG.
/// </summary>
public sealed class DemoWorldSites
{
    private DemoWorldSites(
        IReadOnlyList<DemoSettlement> settlements,
        IReadOnlyList<DemoResourcePoint> resourcePoints,
        IReadOnlyList<DemoPopGroup> popGroups, bool foragingOnly = false)
    {
        Settlements = settlements;
        ResourcePoints = resourcePoints;
        PopGroups = popGroups;
        ForagingOnly = foragingOnly;
    }

    public IReadOnlyList<DemoSettlement> Settlements { get; }

    public IReadOnlyList<DemoResourcePoint> ResourcePoints { get; }

    public IReadOnlyList<DemoPopGroup> PopGroups { get; }
    public bool ForagingOnly { get; }

    public static DemoWorldSites Generate(DemoHexWorld map, RngRegistry rng)
        => Generate(map, rng, DemoSitesTempConfig.Sample);

    public static DemoWorldSites Generate(DemoHexWorld map, RngRegistry rng, DemoSitesTempConfig config,
        bool foragingOnly = false)
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
        // C05：開局完全不抽 POP；地圖（GEOGRAPHY）之後只用 ECOLOGY_INIT。

        var span = (ulong)(config.MaxSettlements - config.MinSettlements + 1);
        var count = config.MinSettlements + (int)(rng.NextUInt64(RngStreams.EcologyInit) % span);

        // The player cannot live on river/water tiles; no unsafe fallback.
        var land = map.Cells.Where(c => c.Terrain != DemoTerrain.Water).ToList();
        if (land.Count < count)
            throw new InvalidOperationException("Insufficient habitable land.");

        var settlements = new List<DemoSettlement>(count);
        var resources = new List<DemoResourcePoint>(count);
        var pops = new List<DemoPopGroup>(count);

        // 第 08-1 章：自然採集世界（foragingOnly）的開局聚落只放在可取水的陸地（聚落格相鄰有水域）【TEMP 規則】，
        // 否則開局第一週就因缺水累積生存債；抽樣次數與順序不變，舊情境的位置完全不變。可取水格不夠時才退回全部陸地。
        if (foragingOnly)
        {
            var habitable = land.Where(c => DemoMigrationRoutes.HasWaterAccess(map, new DemoCellRef(c.Column, c.Row))).ToList();
            if (habitable.Count >= count)
                land = habitable;
        }

        // ① Placement: same ECOLOGY_INIT draw order as before C05, so positions are unchanged for every seed.
        for (var i = 0; i < count; i++)
        {
            var pool = land;
            var pick = (int)(rng.NextUInt64(RngStreams.EcologyInit) % (ulong)pool.Count);
            var cell = pool[pick];
            pool.RemoveAt(pick);

            var id = i + 1;
            settlements.Add(new DemoSettlement(id, config.SettlementNames[i], cell.Column, cell.Row));
            resources.Add(new DemoResourcePoint(id, id, KindFor(cell.Terrain, foragingOnly), cell.Column, cell.Row));
        }

        // ② C05：初始人口群體人數，依聚落順序另起一圈，仍抽 ECOLOGY_INIT（41A SEED_ECOLOGY_INIT）。
        var popSpan = (ulong)(config.MaxPopGroupSize - config.MinPopGroupSize + 1);
        for (var i = 0; i < count; i++)
        {
            var id = i + 1;
            var size = config.MinPopGroupSize + (int)(rng.NextUInt64(RngStreams.EcologyInit) % popSpan);
            pops.Add(new DemoPopGroup(id, id, size));
        }

        return new DemoWorldSites(
            Array.AsReadOnly(settlements.ToArray()),
            Array.AsReadOnly(resources.ToArray()),
            Array.AsReadOnly(pops.ToArray()), foragingOnly);
    }

    /// <summary>
    /// C04：開局配號。依固定順序向世界唯一分配器取號：先全部聚落（生成順序，故仍為 1～N）→各聚落資源點→人口群體。
    /// 獨立於 <see cref="Generate"/>，不改亂數抽樣次數與順序。分配器必須是全新的（下一號為 1）。
    /// </summary>
    internal DemoWorldSites WithOpeningEntityIds(EntityIdAllocator ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.NextValue != 1 || Settlements.Any(s => s.IsFounded))
            throw new InvalidOperationException("Opening entity IDs need a fresh allocator and an unfounded world.");
        var settlementIds = new Dictionary<int, int>();
        var settlements = Settlements.Select(s =>
        {
            var id = DemoEntityIds.NextInt(ids);
            settlementIds[s.Id] = id;
            return s with { Id = id };
        }).ToArray();
        var points = ResourcePoints.Select(p => p with
        {
            Id = DemoEntityIds.NextInt(ids), SettlementId = settlementIds[p.SettlementId]
        }).ToArray();
        var pops = PopGroups.Select(g => g with
        {
            Id = DemoEntityIds.NextInt(ids), SettlementId = settlementIds[g.SettlementId]
        }).ToArray();
        return new DemoWorldSites(Array.AsReadOnly(settlements), Array.AsReadOnly(points), Array.AsReadOnly(pops),
            ForagingOnly);
    }

    /// <summary>C04：本世界目前所有聚落、資源點、人口群體的實體 ID。</summary>
    internal IEnumerable<long> EntityIds()
        => Settlements.Select(s => (long)s.Id).Concat(ResourcePoints.Select(p => (long)p.Id))
            .Concat(PopGroups.Select(g => (long)g.Id));

    /// <summary>C04：遷徙隊伍建立的新聚落個數（新聚落名稱「新聚落K號」的 K 依此遞增，不再拿 ID 當名字）。</summary>
    public int FoundedSettlementCount => Settlements.Count(s => s.IsFounded);

    /// <summary>
    /// V0-06B：加上一個遷徙隊伍建立的新聚落與它所在格的資源點，回傳新的唯讀副本（不改動自己）。
    /// 新聚落沒有初始人口群體：人口全部來自遷徙隊伍，記在每週人口帳的遷入（M1_SETTLE）。
    /// C04：兩個 ID 都由世界唯一分配器發號，必須大於既有全部 ID（只遞增、不重用）。
    /// </summary>
    internal DemoWorldSites WithFoundedSettlement(DemoSettlement settlement, int resourcePointId, DemoHexWorld map)
    {
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(map);
        var maxExisting = EntityIds().DefaultIfEmpty(0).Max();
        if (!settlement.IsFounded || settlement.Id <= maxExisting || resourcePointId <= settlement.Id)
            throw new InvalidOperationException("A founded settlement needs fresh entity IDs and a founding week.");
        if (SettlementAt(settlement.Column, settlement.Row) is not null)
            throw new InvalidOperationException("A settlement already stands on this cell.");
        var cell = map.GetCell(settlement.Column, settlement.Row);
        if (cell.Terrain == DemoTerrain.Water)
            throw new InvalidOperationException("TEMP: settlements cannot be founded on water cells.");

        var point = new DemoResourcePoint(resourcePointId, settlement.Id, KindFor(cell.Terrain, ForagingOnly),
            settlement.Column, settlement.Row);
        return new DemoWorldSites(
            Array.AsReadOnly(Settlements.Append(settlement).ToArray()),
            Array.AsReadOnly(ResourcePoints.Append(point).ToArray()),
            PopGroups, ForagingOnly);
    }

    /// <summary>第 07 章 c 節：聚落於第 <paramref name="week"/> 週末成為遺跡。</summary>
    internal DemoWorldSites WithRuin(int settlementId, int week)
    {
        var target = Settlements.SingleOrDefault(s => s.Id == settlementId)
            ?? throw new InvalidOperationException("Unknown settlement for ruin.");
        if (target.IsRuin || week < 1 || week <= target.FoundedWeek)
            throw new InvalidOperationException("A settlement can become a ruin only once, after it was founded.");
        return new DemoWorldSites(
            Array.AsReadOnly(Settlements.Select(s => s.Id == settlementId ? s with { RuinedWeek = week } : s).ToArray()),
            ResourcePoints, PopGroups, ForagingOnly);
    }

    /// <summary>
    /// 第 07 章 c 節：第 <paramref name="week"/> 週開始時的人造佔格（已建立、尚未成為遺跡的聚落所在格）。
    /// 聚落在第 F 週末建立，從第 F＋1 週起佔格；在第 R 週末成為遺跡，從第 R＋1 週起不再佔格。
    /// </summary>
    public IReadOnlyList<DemoCellRef> OccupiedCellsAtWeek(int week)
        => Array.AsReadOnly(Settlements.Where(s => s.FoundedWeek < week && (!s.IsRuin || s.RuinedWeek >= week))
            .Select(s => new DemoCellRef(s.Column, s.Row)).ToArray());

    /// <summary>目前的人造佔格（遺跡不佔格）。</summary>
    public IReadOnlyList<DemoCellRef> OccupiedCells()
        => Array.AsReadOnly(Settlements.Where(s => !s.IsRuin).Select(s => new DemoCellRef(s.Column, s.Row)).ToArray());

    /// <summary>Settlement at the offset cell, or null. Pure query.</summary>
    public DemoSettlement? SettlementAt(int column, int row)
        => Settlements.FirstOrDefault(s => s.Column == column && s.Row == row);

    public IReadOnlyList<DemoResourcePoint> ResourcePointsOf(int settlementId)
        => Array.AsReadOnly(ResourcePoints.Where(r => r.SettlementId == settlementId).ToArray());

    public IReadOnlyList<DemoPopGroup> PopGroupsOf(int settlementId)
        => Array.AsReadOnly(PopGroups.Where(p => p.SettlementId == settlementId).ToArray());

    private static DemoResourceKind KindFor(DemoTerrain terrain, bool foragingOnly = false) => terrain switch
    {
        DemoTerrain.Grassland => DemoResourceKind.Uncultivated, // 第 08 章 a 節：草原不再自動成為農地
        DemoTerrain.Forest => DemoResourceKind.Woodland,
        DemoTerrain.Water => DemoResourceKind.Fishery,
        _ => throw new InvalidOperationException("Unknown TEMP terrain value.")
    };
}
