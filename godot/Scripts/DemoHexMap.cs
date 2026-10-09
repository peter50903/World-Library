using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// Non-authoritative 12x8 visual fixture. Grass/forest/water and settlement
/// markers are for observing the GUI, NOT actual World/Region/POP/RouteSegment
/// data. No connection to canonical world RNG, and no fake roads are drawn.
/// </summary>
public partial class DemoHexMap : Control
{
    private const int Columns = 12;
    private const int Rows = 8;

    public ulong DemoSeed { get; set; } = 42;

    public DemoHexMap()
    {
        CustomMinimumSize = new Vector2(540, 460);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Ignore;
        TooltipText = "TEMP：地形與聚落標記僅作示範；相鄰格不代表有路網可通行。";
    }

    public override void _Ready()
    {
        Resized += QueueRedraw;
        QueueRedraw();
    }

    public override void _Draw()
    {
        const float padding = 28f;
        var sqrtThree = Mathf.Sqrt(3f);
        // Odd-r offset grid: rows only shift by half a hex, rather than
        // marching diagonally into an axial parallelogram. TEMP display only.
        var horizontalUnits = sqrtThree * (Columns + 0.5f);
        var verticalUnits = 1.5f * (Rows - 1) + 2f;
        var radius = Mathf.Max(4f, Mathf.Min(
            (Size.X - 2f * padding) / horizontalUnits,
            (Size.Y - 2f * padding) / verticalUnits));

        var totalWidth = horizontalUnits * radius;
        var totalHeight = verticalUnits * radius;
        var start = new Vector2(
            (Size.X - totalWidth) / 2f + sqrtThree * radius / 2f,
            (Size.Y - totalHeight) / 2f + radius);

        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Columns; col++)
            {
                var axial = HexCoordinates.OddRowOffsetToAxial(col, row);
                var center = start + HexCoordinates.Center(axial.X, axial.Y, radius);
                var vertices = HexCoordinates.Polygon(center, radius - 1.4f);

                // The palette and classification are only scene decoration.
                Color terrain = TerrainCode(col, row) switch
                {
                    0 or 1 => new Color(0.20f, 0.39f, 0.54f), // water
                    2 or 3 or 4 => new Color(0.17f, 0.38f, 0.28f), // forest
                    _ => new Color(0.41f, 0.56f, 0.34f) // grass
                };

                DrawColoredPolygon(vertices, terrain);
                var outline = new Vector2[7];
                for (var i = 0; i < 6; i++)
                    outline[i] = vertices[i];
                outline[6] = vertices[0];
                DrawPolyline(outline, new Color(0.07f, 0.15f, 0.18f), 1.2f);

                if (IsDemoSettlement(col, row))
                {
                    DrawCircle(center, radius * 0.24f, new Color(0.12f, 0.13f, 0.15f));
                    DrawCircle(center, radius * 0.17f, new Color(0.97f, 0.76f, 0.35f));
                }
            }
        }
    }

    private static bool IsDemoSettlement(int q, int r)
        => (q, r) is (2, 2) or (7, 2) or (8, 5);

    private int TerrainCode(int q, int r)
    {
        if (IsDemoSettlement(q, r))
            return 8;

        // Independent visual hash: never consume the simulation's RNG streams.
        unchecked
        {
            ulong v = DemoSeed ^ ((ulong)(q + 1) * 0x9E3779B97F4A7C15UL);
            v ^= (ulong)(r + 11) * 0xBF58476D1CE4E5B9UL;
            v = (v ^ (v >> 30)) * 0xBF58476D1CE4E5B9UL;
            v = (v ^ (v >> 27)) * 0x94D049BB133111EBUL;
            return (int)((v ^ (v >> 31)) % 11UL);
        }
    }
}
