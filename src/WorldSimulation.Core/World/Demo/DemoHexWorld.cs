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
/// </summary>
public sealed record DemoHexCell(
    int Column,
    int Row,
    int AxialQ,
    int AxialR,
    DemoTerrain Terrain,
    bool MainWaterway = false);

/// <summary>
/// Deterministic display-oriented map produced exclusively with the existing
/// GEOGRAPHY RNG stream. Does not advance the live world's RNG, create a
/// canonical HexCell/Region, or decide the game's future coordinate system.
/// </summary>
public sealed class DemoHexWorld
{
    public const int Columns = 12;
    public const int Rows = 8;

    /// <summary>第 06 章 a 節：產生一張地圖從 GEOGRAPHY 流抽的次數（地形＋主水道＋渡口）。</summary>
    public const int GeographyDraws = Columns * Rows + Rows + 1;

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

    /// <summary>第 06 章 a 節：主水道格（從北緣到南緣連續，依列遞增）。</summary>
    public IReadOnlyList<DemoCellRef> MainWaterway
        => _cells.Where(c => c.MainWaterway).OrderBy(c => c.Row).Select(c => new DemoCellRef(c.Column, c.Row)).ToArray();

    public IReadOnlyList<DemoHexCell> Cells => _cells;

    /// <summary>Convenience overload: a fresh registry for <paramref name="seed"/>.</summary>
    public static DemoHexWorld Generate(ulong seed) => Generate(RngRegistry.Create(seed));

    /// <summary>
    /// V0-03B: draws the terrain from the caller's single world RngRegistry
    /// (GEOGRAPHY stream only). The GEOGRAPHY stream must be untouched so the
    /// same WorldSeed always produces the same map.
    /// 第 06 章 a 節：地形 96 次之後再抽 <see cref="Rows"/>＋1 次產生主水道（北緣起點 1 次、每往南一列 1 次），
    /// 再抽 1 次決定渡口位置；共 <see cref="GeographyDraws"/> 次。
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
                var terrain = (DemoTerrain)(rng.NextUInt64(RngStreams.Geography) % 3UL);
                cells[index++] = new DemoHexCell(col, row, axialQ, row, terrain);
            }
        }

        // 第 06 章 a 節：主水道——北緣隨機起點，每往南一列走到兩個下方鄰格之一（odd-r：偶數列往 c-1／c，奇數列往 c／c+1），
        // 不出界，所以是一條從北到南連續佔格的水域序列。
        var column = (int)(rng.NextUInt64(RngStreams.Geography) % (ulong)Columns);
        var waterway = new List<(int Column, int Row)> { (column, 0) };
        for (var row = 1; row < Rows; row++)
        {
            var options = ((row - 1) & 1) == 0 ? new[] { column - 1, column } : new[] { column, column + 1 };
            var inside = options.Where(c => c >= 0 && c < Columns).ToArray();
            column = inside[(int)(rng.NextUInt64(RngStreams.Geography) % (ulong)inside.Length)];
            waterway.Add((column, row));
        }
        foreach (var (c, r) in waterway)
            cells[r * Columns + c] = cells[r * Columns + c] with { Terrain = DemoTerrain.Water, MainWaterway = true };

        // 渡口：從抽到的列往南找第一個至少有兩個陸地鄰格的水道格（過河的唯一陸路）；都沒有就沒有渡口。
        var fordStart = (int)(rng.NextUInt64(RngStreams.Geography) % (ulong)Rows);
        DemoCellRef? ford = null;
        var byAxial = cells.ToDictionary(c => (c.AxialQ, c.AxialR));
        for (var k = 0; k < Rows && ford is null; k++)
        {
            var (c, r) = waterway[(fordStart + k) % Rows];
            var here = cells[r * Columns + c];
            var land = DemoRouteNetwork.Directions.Count(d =>
                byAxial.TryGetValue((here.AxialQ + d.Dq, here.AxialR + d.Dr), out var n) && n.Terrain != DemoTerrain.Water);
            if (land >= 2)
                ford = new DemoCellRef(c, r);
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
