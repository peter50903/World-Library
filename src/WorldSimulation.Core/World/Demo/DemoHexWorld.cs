using WorldSimulation.Core.Entity;
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
    Water = 2,

    /// <summary>第 06-3 章：丘陵（陸地；採集量為草原的一半、不能開墾農田；可能有礦脈）。</summary>
    Hills = 3
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
    bool Shallow = false,
    bool Ore = false)
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
        return columns * rows + columns + RiversFor(columns) * (rows + FordsFor(rows));
    }

    private static int FordsFor(int rows) => Math.Max(1, rows / TempRowsPerFord);

    /// <summary>【TEMP】第 06-2 章：每 12 欄一條河（小地圖 1 條、中 2 條、大 4 條）。</summary>
    public const int TempColumnsPerRiver = 12;

    /// <summary>第 06-2 章：某寬度地圖的河流數。</summary>
    public static int RiversFor(int columns) => Math.Max(1, columns / TempColumnsPerRiver);

    /// <summary>【TEMP】第 06-2 章：陸地地形的多數決平滑次數（讓森林與草原成片，不再每格各擲一次硬幣）。</summary>
    public const int TempSmoothingPasses = 2;

    /// <summary>
    /// 第 06-2 章：陸地地形平滑（參考 WorldBox／FMS 成片的生態區）。每一輪每格看自己與相鄰格（含水域前的原始地形）
    /// 的多數：森林多就是森林、草原多就是草原、平手不變。純函式、不抽亂數。
    /// </summary>
    public static DemoTerrain[] SmoothLand(IReadOnlyList<DemoTerrain> raw, int columns, int rows, int passes = TempSmoothingPasses)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var current = raw.ToArray();
        for (var pass = 0; pass < passes; pass++)
        {
            var next = new DemoTerrain[current.Length];
            for (var row = 0; row < rows; row++)
            {
                for (var col = 0; col < columns; col++)
                {
                    var q = col - (row - (row & 1)) / 2;
                    int forest = 0, total = 0;
                    void Count(int cq, int cr)
                    {
                        var c = cq + (cr - (cr & 1)) / 2;
                        if (cr < 0 || cr >= rows || c < 0 || c >= columns) return;
                        total++;
                        if (current[cr * columns + c] == DemoTerrain.Forest) forest++;
                    }
                    Count(q, row);
                    foreach (var (dq, dr, _) in DemoRouteNetwork.Directions)
                        Count(q + dq, row + dr);
                    var own = current[row * columns + col];
                    next[row * columns + col] = forest * 2 > total ? DemoTerrain.Forest
                        : forest * 2 < total ? DemoTerrain.Grassland : own;
                }
            }
            current = next;
        }
        return current;
    }

    private readonly IReadOnlyList<DemoHexCell> _cells;
    private readonly Dictionary<(int Q, int R), DemoHexCell> _byAxial;
    private readonly HashSet<DemoCellRef> _waterAccess;
    private readonly HashSet<DemoCellRef> _nearWater; // 第 08-11 章：離水 2 格內（可挑水）

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
        _nearWater = cells.Where(c => c.Terrain != DemoTerrain.Water
                && (_waterAccess.Contains(new DemoCellRef(c.Column, c.Row))
                    || DemoRouteNetwork.Directions.Any(d => _byAxial.TryGetValue((c.AxialQ + d.Dq, c.AxialR + d.Dr), out var n)
                        && _waterAccess.Contains(new DemoCellRef(n.Column, n.Row)))))
            .Select(c => new DemoCellRef(c.Column, c.Row)).ToHashSet();
    }

    /// <summary>第 08-11 章：離水 2 格內的陸地（相鄰有水，或相鄰格相鄰有水）——不掘井也能挑水度日。</summary>
    public bool IsNearWater(DemoCellRef cell) => _nearWater.Contains(cell);

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

        // ①b 第 06-2 章：陸地多數決平滑，森林與草原成片（不另抽亂數）。
        var smoothed = SmoothLand(cells.Select(c => c.Terrain).ToArray(), Columns, Rows);
        for (var i = 0; i < cells.Length; i++)
            cells[i] = cells[i] with { Terrain = smoothed[i] };

        // ② 南緣海洋：每欄深度 1～2 列（TEMP），海洋只有深水。
        for (var col = 0; col < Columns; col++)
        {
            var depth = seaScale * TempSeaMinDepth + (int)(rng.NextUInt64(RngStreams.Geography) % (ulong)(seaScale * (TempSeaMaxDepth - TempSeaMinDepth) + 1));
            for (var row = Rows - depth; row < Rows; row++)
                cells[row * Columns + col] = cells[row * Columns + col] with { Terrain = DemoTerrain.Water, Sea = true };
        }

        // ③ 河流——第 06-2 章：每 12 欄一條（小地圖 1 條，與之前相同）；第 k 條從北緣第 k 段欄位中隨機起點，
        // 每往南一列走到兩個下方鄰格之一（odd-r：偶數列往 c-1／c，奇數列往 c／c+1），不出界；碰到海就停（河口接海），
        // 碰到已有的河就停（成為支流匯入）。抽樣次數固定，停下後的抽樣不使用。
        // ④ 淺灘（渡口）：每條河每 8 列一段各抽一次，從抽到的位置往南找第一個兩岸（畫面左右）都有陸地鄰格、
        // 而且不臨海的河流格——涉水過河才有意義；都沒有就沒有渡口。
        var rivers = RiversFor(Columns);
        var band = Columns / rivers;
        for (var k = 0; k < rivers; k++)
        {
            var column = k * band + (int)(rng.NextUInt64(RngStreams.Geography) % (ulong)band);
            var waterway = new List<(int Column, int Row)>();
            var stopped = cells[column].MainWaterway;
            if (!stopped)
                waterway.Add((column, 0));
            for (var row = 1; row < Rows; row++)
            {
                var options = ((row - 1) & 1) == 0 ? new[] { column - 1, column } : new[] { column, column + 1 };
                var inside = options.Where(c => c >= 0 && c < Columns).ToArray();
                column = inside[(int)(rng.NextUInt64(RngStreams.Geography) % (ulong)inside.Length)];
                if (stopped || cells[row * Columns + column].Sea || cells[row * Columns + column].MainWaterway)
                {
                    stopped = true;
                    continue;
                }
                waterway.Add((column, row));
            }
            foreach (var (c, r) in waterway)
                cells[r * Columns + c] = cells[r * Columns + c] with { Terrain = DemoTerrain.Water, MainWaterway = true };

            var byAxial = cells.ToDictionary(c => (c.AxialQ, c.AxialR));
            var fords = FordsFor(Rows);
            for (var part = 0; part < fords; part++)
            {
                var segment = waterway.Where(w => fords == 1 || w.Row * fords / Rows == part).ToList();
                var fordStart = segment.Count == 0 ? 0 : (int)(rng.NextUInt64(RngStreams.Geography) % (ulong)segment.Count);
                if (segment.Count == 0) { rng.NextUInt64(RngStreams.Geography); continue; }
                FindFord(segment, fordStart, byAxial);
            }
        }

        void FindFord(List<(int Column, int Row)> segment, int fordStart, Dictionary<(int, int), DemoHexCell> byAxial)
        {
            for (var k = 0; k < segment.Count; k++)
            {
                var (c, r) = segment[(fordStart + k) % segment.Count];
                var here = cells[r * Columns + c];
                var neighbours = DemoRouteNetwork.Directions
                    .Select(d => byAxial.TryGetValue((here.AxialQ + d.Dq, here.AxialR + d.Dr), out var n) ? n : null)
                    .Where(n => n is not null).Select(n => n!).ToList();
                var land = neighbours.Where(n => n.Terrain != DemoTerrain.Water).ToList();
                if (!neighbours.Any(n => n.Sea) && land.Any(n => n.ScreenX < here.ScreenX) && land.Any(n => n.ScreenX > here.ScreenX))
                {
                    cells[r * Columns + c] = here with { Shallow = true };
                    return;
                }
            }
        }

        // ④ 第 06-3 章：丘陵帶與礦脈（實體局部亂數，不推進地理流計數器；前面的地形與後續開局聚落不受影響）。
        PlaceHills(rng, cells, Columns, Rows);

        var readOnly = Array.AsReadOnly(cells);
        return new DemoHexWorld(seed, size, readOnly, DemoRouteNetwork.Build(readOnly, DemoRouteTempSettings.Current));
    }

    /// <summary>【TEMP】第 06-3 章：每多少欄一條丘陵帶（至少一條）、每條長度 4～7 格、丘陵格有礦脈的機率 1/3。</summary>
    public const int TempColumnsPerHillRange = 8;
    public const int TempHillRangeMin = 4;
    public const int TempHillRangeMax = 7;
    public const int TempOreOneIn = 3;

    private const string HillNamespace = "TEMP-HILLS-v1";

    /// <summary>
    /// 第 06-3 章：每條丘陵帶從隨機一格陸地（非水域）出發，往隨機相鄰陸地格走 4～7 步，走過的格成為丘陵；
    /// 每個丘陵格 1/3 有礦脈。抽樣用地理流的實體局部亂數（丘陵帶 i＝實體 i+1；礦脈＝格編號＋10000）。
    /// </summary>
    private static void PlaceHills(RngRegistry rng, DemoHexCell[] cells, int columns, int rows)
    {
        ulong Draw(ulong entity, ulong i) => EntityLocalRandom.Create(rng, EntityId.From(entity), RngStreams.Geography, HillNamespace).GetUInt64(i);
        var byAxial = cells.Select((c, i) => (c, i)).ToDictionary(x => (x.c.AxialQ, x.c.AxialR), x => x.i);
        var land = Enumerable.Range(0, cells.Length).Where(i => cells[i].Terrain != DemoTerrain.Water).ToArray();
        if (land.Length == 0)
            return;
        var ranges = Math.Max(1, columns / TempColumnsPerHillRange);
        for (var k = 0; k < ranges; k++)
        {
            var entity = (ulong)k + 1;
            var at = land[(int)(Draw(entity, 0) % (ulong)land.Length)];
            var length = TempHillRangeMin + (int)(Draw(entity, 1) % (ulong)(TempHillRangeMax - TempHillRangeMin + 1));
            for (var step = 0; step < length; step++)
            {
                cells[at] = cells[at] with { Terrain = DemoTerrain.Hills };
                var here = cells[at];
                var next = DemoRouteNetwork.Directions
                    .Select(d => byAxial.TryGetValue((here.AxialQ + d.Dq, here.AxialR + d.Dr), out var n) ? n : -1)
                    .Where(n => n >= 0 && cells[n].Terrain is DemoTerrain.Grassland or DemoTerrain.Forest).ToArray();
                if (next.Length == 0)
                    break;
                at = next[(int)(Draw(entity, 2 + (ulong)step) % (ulong)next.Length)];
            }
        }
        for (var i = 0; i < cells.Length; i++)
            if (cells[i].Terrain == DemoTerrain.Hills && Draw(10_000UL + (ulong)i, 0) % TempOreOneIn == 0)
                cells[i] = cells[i] with { Ore = true };
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
