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

/// <summary>
/// Deterministic display-oriented map produced exclusively with the existing
/// GEOGRAPHY RNG stream. Does not advance the live world's RNG, create a
/// canonical HexCell/Region, or decide the game's future coordinate system.
/// </summary>
public sealed class DemoHexWorld
{
    public const int Columns = 12;
    public const int Rows = 8;

    /// <summary>第 06-1 章：產生一張地圖從 GEOGRAPHY 流抽的次數（陸地地形＋每欄海岸深度＋主水道＋渡口）。</summary>
    public const int GeographyDraws = Columns * Rows + Columns + Rows + 1;

    /// <summary>【TEMP】第 06-1 章：南緣海洋每欄的深度範圍（列數）。</summary>
    public const int TempSeaMinDepth = 1, TempSeaMaxDepth = 2;

    private readonly IReadOnlyList<DemoHexCell> _cells;

    private DemoHexWorld(ulong seed, IReadOnlyList<DemoHexCell> cells, DemoRouteNetwork routes)
    {
        Seed = seed;
        _cells = cells;
        Routes = routes;
    }

    public ulong Seed { get; }

    /// <summary>第 06 章 a 節：路線網（跨格可通行性的單一權威）。</summary>
    public DemoRouteNetwork Routes { get; }

    /// <summary>第 06 章 a 節：主水道格（從北緣連續往南，第 06-1 章起流到海岸為止；依列遞增）。</summary>
    public IReadOnlyList<DemoCellRef> MainWaterway
        => _cells.Where(c => c.MainWaterway).OrderBy(c => c.Row).Select(c => new DemoCellRef(c.Column, c.Row)).ToArray();

    public IReadOnlyList<DemoHexCell> Cells => _cells;

    /// <summary>Convenience overload: a fresh registry for <paramref name="seed"/>.</summary>
    public static DemoHexWorld Generate(ulong seed) => Generate(RngRegistry.Create(seed));

    /// <summary>
    /// V0-03B: draws the terrain from the caller's single world RngRegistry
    /// (GEOGRAPHY stream only). The GEOGRAPHY stream must be untouched so the
    /// same WorldSeed always produces the same map.
    /// 第 06-1 章【TEMP 規則】依序抽：① 每格陸地地形（草原／森林各半）96 次；② 南緣海洋每欄深度 12 次
    /// （1～2 列，連成一片海岸，不再有散落的水域）；③ 主水道：北緣起點 1 次、每往南一列 1 次，流到海岸為止
    /// （碰到海就停，河口接海）；④ 淺灘（渡口）1 次：從抽到的位置起找第一個「兩岸都有陸地、不臨海」的河流格。
    /// 共 <see cref="GeographyDraws"/> 次。
    /// </summary>
    public static DemoHexWorld Generate(RngRegistry rng)
    {
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
            var depth = TempSeaMinDepth + (int)(rng.NextUInt64(RngStreams.Geography) % (ulong)(TempSeaMaxDepth - TempSeaMinDepth + 1));
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
        // 涉水過河才有意義；都沒有就沒有渡口。
        var fordStart = (int)(rng.NextUInt64(RngStreams.Geography) % (ulong)waterway.Count);
        DemoCellRef? ford = null;
        var byAxial = cells.ToDictionary(c => (c.AxialQ, c.AxialR));
        for (var k = 0; k < waterway.Count && ford is null; k++)
        {
            var (c, r) = waterway[(fordStart + k) % waterway.Count];
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

        var readOnly = Array.AsReadOnly(cells);
        return new DemoHexWorld(seed, readOnly, DemoRouteNetwork.Build(readOnly, ford, DemoRouteTempSettings.Current));
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
