using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// V0-03A: Renders already-generated Core TEMP hex data. This view NEVER
/// generates terrain or draws from RNG. Gold circles remain TEMP decorative
/// landmarks pending real settlements in V0-03B.
/// </summary>
public partial class DemoHexMap : Control
{
    public DemoHexWorld World { get; set; } = DemoHexWorld.Generate(42);

    public event Action<DemoHexCell>? CellSelected;

    public DemoHexMap()
    {
        CustomMinimumSize = new Vector2(540, 460);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "TEMP：地形由模擬核心的地理亂數流生成；金色標記仍只是示範，沒有正式道路或聚落。";
    }

    public override void _Ready()
    {
        Resized += QueueRedraw;
        QueueRedraw();
    }

    public override void _Draw()
    {
        var (start, radius) = Layout();

        foreach (var cell in World.Cells)
        {
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            var vertices = HexCoordinates.Polygon(center, radius - 1.4f);
            var terrainColor = cell.Terrain switch
            {
                DemoTerrain.Water => new Color(0.20f, 0.39f, 0.54f),
                DemoTerrain.Forest => new Color(0.17f, 0.38f, 0.28f),
                DemoTerrain.Grassland => new Color(0.41f, 0.56f, 0.34f),
                _ => throw new InvalidOperationException("Unknown TEMP terrain value.")
            };

            DrawColoredPolygon(vertices, terrainColor);
            var outline = new Vector2[7];
            for (var i = 0; i < 6; i++)
                outline[i] = vertices[i];
            outline[6] = vertices[0];
            DrawPolyline(outline, new Color(0.07f, 0.15f, 0.18f), 1.2f);

            if (IsDemoLandmark(cell.Column, cell.Row))
            {
                DrawCircle(center, radius * 0.24f, new Color(0.12f, 0.13f, 0.15f));
                DrawCircle(center, radius * 0.17f, new Color(0.97f, 0.76f, 0.35f));
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton
            {
                ButtonIndex: MouseButton.Left,
                Pressed: true
            } click)
            return;

        var (start, radius) = Layout();
        foreach (var cell in World.Cells)
        {
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            if (!Geometry2D.IsPointInPolygon(
                    click.Position, HexCoordinates.Polygon(center, radius - 1.4f)))
                continue;

            CellSelected?.Invoke(cell);
            AcceptEvent();
            return;
        }
    }

    /// <summary>Used by the in-engine smoke test to inject a TEMP cell click.</summary>
    public Vector2 CellCenter(DemoHexCell cell)
    {
        var (start, radius) = Layout();
        return start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
    }

    private (Vector2 Start, float Radius) Layout()
    {
        const float padding = 28f;
        var sqrtThree = Mathf.Sqrt(3f);
        var widthUnits = sqrtThree * (DemoHexWorld.Columns + 0.5f);
        var heightUnits = 1.5f * (DemoHexWorld.Rows - 1) + 2f;
        var radius = Mathf.Max(4f, Mathf.Min(
            (Size.X - 2f * padding) / widthUnits,
            (Size.Y - 2f * padding) / heightUnits));

        var start = new Vector2(
            (Size.X - widthUnits * radius) / 2f + sqrtThree * radius / 2f,
            (Size.Y - heightUnits * radius) / 2f + radius);
        return (start, radius);
    }

    private static bool IsDemoLandmark(int column, int row)
        => (column, row) is (2, 2) or (7, 2) or (8, 5);
}
