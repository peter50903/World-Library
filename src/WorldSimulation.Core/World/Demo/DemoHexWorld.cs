using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// TEMP terrain categories for the V0-03A fixture only. NOT the official 54G
/// terrain definitions; never load or write these values as formal world rules.
/// </summary>
public enum DemoTerrain
{
    Grassland = 0,
    Forest = 1,
    Water = 2
}

/// <summary>
/// 六角格（尖頂、odd-r 偏移＋軸向座標）。第 06 章 a 節加 <see cref="MainWaterway"/>：主水道格
/// （連續佔格的水域序列，禁止住宅與聚落）。跨格通行另見 <see cref="DemoRouteNetwork"/>。
/// 第 06-1 章：水域分成<b>海洋</b>（<see cref="Sea"/>，只有深水）與<b>河流</b>（主水道，可有深淺）；
/// <see cref="Shallow"/>＝河流淺灘（渡口，可涉水過河）。
/// </summary>
public sealed record DemoHexCell(
    int Column,
    int Row,
    int AxialQ,
    int AxialR,
    DemoTerrain Terrain,
    bool MainWaterway = false,
    bool Sea = false,
    bool Shallow = false)
{
    /// <summary>第 06-1 章：畫面水平位置（odd-r：奇數列往右半格），判斷河的兩岸用。</summary>
    public double ScreenX => Column + 0.5 * (Row & 1);
}

/// <summary>第 07-1 章 c 節：建立世界時選的地圖尺寸。</summary>
public enum DemoMapSize
{
    Small = 0,
    Medium = 1,
    Large = 2
}

/// <summary>第 07-1 章 c 節【TEMP】地圖尺寸：小 12×8、中 24×16、大 48×32。</summary>
public static class DemoMapSizes
{
    public static (int Columns, int Rows) Dimensions(DemoMapSize size) => size switch
    {
        DemoMapSize.Small => (DemoHexWorld.SmallColumns, DemoHexWorld.SmallRows),
        DemoMapSize.Medium => (24, 16),
        DemoMapSize.Large => (48, 32),
        _ => throw new ArgumentOutOfRangeException(nameof(size))
    };

    public static string Name(DemoMapSize size) => size switch
    {
        DemoMapSize.Small => "小",
        DemoMapSize.Medium => "中",
        DemoMapSize.Large => "大",
        _ => throw new ArgumentOutOfRangeException(nameof(size))
    };
}

/// <summary>
/// Deterministic display-oriented map produced exclusively with the existing
/// GEOGRAPHY RNG stream. Does not advance the live world's RNG, create a
/// canonical HexCell/Region, or decide the game's future coordinate system.
/// </summary>
public sealed class DemoHexWorld
{
    /// <summary>小地圖（預設）的欄列數。</summary>
    public const int SmallColumns = 12, SmallRows = 8;

    /// <summary>第 06-1 章：小地圖從 GEOGRAPHY 流抽的次數（陸地地形＋每欄海岸深度＋主水道＋渡口）。</summary>
    public const int GeographyDraws = SmallColumns * SmallRows + SmallColumns + SmallRows + 1;

    /// <summary>【TEMP】第 06-1 章：小地圖南緣海洋每欄的深度範圍（列數；較大地圖依列數等比放大）。</summary>
    public const int TempSeaMinDepth = 1, TempSeaMaxDepth = 2;

    /// <summary>【TEMP】第 07-1 章 c 節：地圖每 8 列一個淺灘渡口（小地圖 1 個）。</summary>
    public const int TempRowsPerFord = 8;

    /// <summary>第 07-1 章 c 節：某尺寸地圖從 GEOGRAPHY 流抽的次數。</summary>
    public static int GeographyDrawsFor(DemoMapSize size)
    {
        var (columns, rows) = DemoMapSizes.Dimensions(size);
        return columns * rows + columns + rows + FordsFor(rows);
    }

    private static int FordsFor(int rows) => Math.Max(1, rows / TempRowsPerFord);

    private readonly IReadOnlyList<DemoHexCell> _cells;
    private readonly Dictionary<(int Q, int R), DemoHexCell> _byAxial;
    private readonly HashSet<DemoCellRef> _waterAccess;

    // 第 07-1 章 c 節：每個起點的陸路廣度優先樹（地圖不可變，算一次就能重複用；與逐次搜尋結果完全相同）。
    private readonly System.Collections.Concurrent.ConcurrentDictionary<DemoCellRef, IReadOnlyDictionary<DemoCellRef, DemoCellRef?>> _landTrees = new();

    private DemoHexWorld(ulong seed, DemoMapSize size, IReadOnlyList<DemoHexCell> cells, DemoRouteNetwork routes)
    {
        Seed = seed;
        Size = size;
        (Columns, Rows) = DemoMapSizes.Dimensions(size);
        _cells = cells;
        Routes = routes;
        _byAxial = cells.ToDictionary(c => (c.AxialQ, c.AxialR));
        _waterAccess = cells.Where(c => DemoRouteNetwork.Directions.Any(d => _byAxial.TryGetValue((c.AxialQ + d.Dq, c.AxialR + d.Dr), out var n) && n.Terrain == DemoTerrain.Water))
            .Select(c => new DemoCellRef(c.Column, c.Row)).ToHashSet();
    }

    /// <summary>第 07-1 章 c 節：以軸向座標找格（地圖外回傳 null）。</summary>
    public DemoHexCell? AtAxial(int q, int r) => _byAxial.GetValueOrDefault((q, r));

    /// <summary>第 07-1 章 c 節：TEMP 水源（有水域鄰格；與原本逐次掃全圖的判斷相同），建圖時算好。</summary>
    public bool HasWaterAccess(DemoCellRef cell) => _waterAccess.Contains(cell);

    /// <summary>
    /// 第 07-1 章 c 節：從 <paramref name="origin"/> 出發的陸路廣度優先樹（每格的上一格；起點為 null），
    /// 鄰格依固定方向順序展開，與 <see cref="DemoMigrationRoutes.FindRoute"/> 原本逐次搜尋的結果相同。
    /// </summary>
    internal IReadOnlyDictionary<DemoCellRef, DemoCellRef?> LandTree(DemoCellRef origin)
        => _landTrees.GetOrAdd(origin, o => BuildLandTree(o));

    private IReadOnlyDictionary<DemoCellRef, DemoCellRef?> BuildLandTree(DemoCellRef origin)
    {
        var start = GetCell(origin.Column, origin.Row);
        var previous = new Dictionary<DemoCellRef, DemoCellRef?> { [origin] = null };
        var queue = new Queue<DemoHexCell>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            var here = new DemoCellRef(cell.Column, cell.Row);
            foreach (var (dq, dr) in DemoMigrationRoutes.AxialDirections)
            {
                if (!_byAxial.TryGetValue((cell.AxialQ + dq, cell.AxialR + dr), out var next))
                    continue;
                var key = new DemoCellRef(next.Column, next.Row);
                if (previous.ContainsKey(key) || !Routes.IsLandPassable(here, key))
                    continue;
                previous[key] = here;
                queue.Enqueue(next);
            }
        }
        return previous;
    }

    public ulong Seed { get; }

    /// <summary>第 07-1 章 c 節：地圖尺寸。</summary>
    public DemoMapSize Size { get; }

    public int Columns { get; }

    public int Rows { get; }

    /// <summary>第 07-1 章 c 節：相對小地圖的格數倍率（聚落數等依此放大）。</summary>
    public int AreaFactor => Columns * Rows / (SmallColumns * SmallRows);

    public bool Contains(int column, int row) => column >= 0 && column < Columns && row >= 0 && row < Rows;

    /// <summary>第 06 章 a 節：路線網（跨格可通行性的單一權威）。</summary>
    public DemoRouteNetwork Routes { get; }

    /// <summary>第 06 章 a 節：主水道格（從北緣連續往南，第 06-1 章起流到海岸為止；依列遞增）。</summary>
    public IReadOnlyList<DemoCellRef> MainWaterway
        => _cells.Where(c => c.MainWaterway).OrderBy(c => c.Row).Select(c => new DemoCellRef(c.Column, c.Row)).ToArray();

    public IReadOnlyList<DemoHexCell> Cells => _cells;

    /// <summary>Convenience overload: a fresh registry for <paramref name="seed"/>.</summary>
    public static DemoHexWorld Generate(ulong seed, DemoMapSize size = DemoMapSize.Small) => Generate(RngRegistry.Create(seed), size);

    /// <summary>
    /// V0-03B: draws the terrain from the caller's single world RngRegistry
    /// (GEOGRAPHY stream only). The GEOGRAPHY stream must be untouched so the
    /// same WorldSeed always produces the same map.
    /// 第 06-1 章【TEMP 規則】依序抽：① 每格陸地地形（草原／森林各半）96 次；② 南緣海洋每欄深度 12 次
    /// （1～2 列，連成一片海岸，不再有散落的水域）；③ 主水道：北緣起點 1 次、每往南一列 1 次，流到海岸為止
    /// （碰到海就停，河口接海）；④ 淺灘（渡口）1 次：從抽到的位置起找第一個「兩岸都有陸地、不臨海」的河流格。
    /// 共 <see cref="GeographyDraws"/> 次。
    /// </summary>
    public static DemoHexWorld Generate(RngRegistry rng, DemoMapSize size = DemoMapSize.Small)
    {
        var (Columns, Rows) = DemoMapSizes.Dimensions(size);
        var seaScale = Math.Max(1, Rows / SmallRows);
        ArgumentNullException.ThrowIfNull(rng);
        if (rng.GetDrawCounter(RngStreams.Geography) != 0)
            throw new InvalidOperationException("Demo map generation requires an unused GEOGRAPHY stream.");

        var seed = rng.WorldSeed;
        var cells = new DemoHexCell[Columns * Rows];
        var index = 0;

        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Columns; col++)
            {
                // TEMP odd-r offset -> axial mapping. The final coordinate
                // convention is still an open design decision.
                var axialQ = col - (row - (row & 1)) / 2;
                var terrain = (DemoTerrain)(rng.NextUInt64(RngStreams.Geography) % 2UL); // 草原或森林
                cells[index++] = new DemoHexCell(col, row, axialQ, row, terrain);
            }
        }

        // ② 南緣海洋：每欄深度 1～2 列（TEMP），海洋只有深水。
        for (var col = 0; col < Columns; col++)
        {
            var depth = seaScale * TempSeaMinDepth + (int)(rng.NextUInt64(RngStreams.Geography) % (ulong)(seaScale * (TempSeaMaxDepth - TempSeaMinDepth) + 1));
            for (var row = Rows - depth; row < Rows; row++)
                cells[row * Columns + col] = cells[row * Columns + col] with { Terrain = DemoTerrain.Water, Sea = true };
        }

        // ③ 主水道——北緣隨機起點，每往南一列走到兩個下方鄰格之一（odd-r：偶數列往 c-1／c，奇數列往 c／c+1），
        // 不出界；碰到海就停（抽樣次數固定，停下後的抽樣不使用）。
        var column = (int)(rng.NextUInt64(RngStreams.Geography) % (ulong)Columns);
        var waterway = new List<(int Column, int Row)> { (column, 0) };
        var reachedSea = false;
        for (var row = 1; row < Rows; row++)
        {
            var options = ((row - 1) & 1) == 0 ? new[] { column - 1, column } : new[] { column, column + 1 };
            var inside = options.Where(c => c >= 0 && c < Columns).ToArray();
            column = inside[(int)(rng.NextUInt64(RngStreams.Geography) % (ulong)inside.Length)];
            if (reachedSea || cells[row * Columns + column].Sea)
            {
                reachedSea = true;
                continue;
            }
            waterway.Add((column, row));
        }
        foreach (var (c, r) in waterway)
            cells[r * Columns + c] = cells[r * Columns + c] with { Terrain = DemoTerrain.Water, MainWaterway = true };

        // ④ 淺灘（渡口）：從抽到的位置往南找第一個兩岸（畫面左右）都有陸地鄰格、而且不臨海的河流格——
        // 涉水過河才有意義；都沒有就沒有渡口。第 07-1 章 c 節：每 8 列一段河道各抽一次（小地圖只有一段、一次，與之前相同）。
        var byAxial = cells.ToDictionary(c => (c.AxialQ, c.AxialR));
        var fords = FordsFor(Rows);
        for (var part = 0; part < fords; part++)
        {
            var segment = waterway.Where(w => fords == 1 || w.Row * fords / Rows == part).ToList();
            var fordStart = segment.Count == 0 ? 0 : (int)(rng.NextUInt64(RngStreams.Geography) % (ulong)segment.Count);
            if (segment.Count == 0) { rng.NextUInt64(RngStreams.Geography); continue; }
            FindFord(segment, fordStart);
        }

        void FindFord(List<(int Column, int Row)> segment, int fordStart)
        {
        DemoCellRef? ford = null;
        for (var k = 0; k < segment.Count && ford is null; k++)
        {
            var (c, r) = segment[(fordStart + k) % segment.Count];
            var here = cells[r * Columns + c];
            var neighbours = DemoRouteNetwork.Directions
                .Select(d => byAxial.TryGetValue((here.AxialQ + d.Dq, here.AxialR + d.Dr), out var n) ? n : null)
                .Where(n => n is not null).Select(n => n!).ToList();
            var land = neighbours.Where(n => n.Terrain != DemoTerrain.Water).ToList();
            if (!neighbours.Any(n => n.Sea) && land.Any(n => n.ScreenX < here.ScreenX) && land.Any(n => n.ScreenX > here.ScreenX))
            {
                ford = new DemoCellRef(c, r);
                cells[r * Columns + c] = here with { Shallow = true };
            }
        }
        }

        var readOnly = Array.AsReadOnly(cells);
        return new DemoHexWorld(seed, size, readOnly, DemoRouteNetwork.Build(readOnly, DemoRouteTempSettings.Current));
    }

    public DemoHexCell GetCell(int column, int row)
    {
        if (column < 0 || column >= Columns)
            throw new ArgumentOutOfRangeException(nameof(column));
        if (row < 0 || row >= Rows)
            throw new ArgumentOutOfRangeException(nameof(row));

        return _cells[row * Columns + column];
    }
}
