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
/// A TEMP axial coordinate plus odd-row offset coordinates for a 12x8
/// demonstration display. No road, region or actual distance semantics.
/// </summary>
public sealed record DemoHexCell(
    int Column,
    int Row,
    int AxialQ,
    int AxialR,
    DemoTerrain Terrain);

/// <summary>
/// Deterministic display-oriented map produced exclusively with the existing
/// GEOGRAPHY RNG stream. Does not advance the live world's RNG, create a
/// canonical HexCell/Region, or decide the game's future coordinate system.
/// </summary>
public sealed class DemoHexWorld
{
    public const int Columns = 12;
    public const int Rows = 8;

    private readonly IReadOnlyList<DemoHexCell> _cells;

    private DemoHexWorld(ulong seed, IReadOnlyList<DemoHexCell> cells)
    {
        Seed = seed;
        _cells = cells;
    }

    public ulong Seed { get; }

    public IReadOnlyList<DemoHexCell> Cells => _cells;

    public static DemoHexWorld Generate(ulong seed)
    {
        var rng = RngRegistry.Create(seed);
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

        return new DemoHexWorld(seed, Array.AsReadOnly(cells));
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
