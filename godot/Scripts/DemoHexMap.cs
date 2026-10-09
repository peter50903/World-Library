using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// V0-03B: Renders the Core TEMP hex map and TEMP settlements injected by
/// ObserverRoot from the observer world's single RngRegistry. This view NEVER
/// generates terrain or sites and never draws from RNG.
/// </summary>
public partial class DemoHexMap : Control
{
    /// <summary>Injected by ObserverRoot; nothing is drawn until it is set.</summary>
    public DemoHexWorld? World { get; set; }

    /// <summary>Injected by ObserverRoot; settlement markers come only from Core.</summary>
    public DemoWorldSites? Sites { get; set; }

    /// <summary>
    /// V0-06A: migrant batches currently travelling, set by ObserverRoot from Core after every
    /// advance. Drawn as red markers with the head count; this view never moves them itself.
    /// </summary>
    public IReadOnlyList<DemoMigrationBatch> Migrants { get; set; } = Array.Empty<DemoMigrationBatch>();

    public event Action<DemoHexCell>? CellSelected;

    public DemoHexMap()
    {
        CustomMinimumSize = new Vector2(540, 460);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = "TEMP：地形與金色示範聚落都由模擬核心依世界種子生成；不是正式世界設定。";
    }

    public override void _Ready()
    {
        Resized += QueueRedraw;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (World is null)
            return;

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

            if (Sites?.SettlementAt(cell.Column, cell.Row) is { } settlement)
            {
                DrawCircle(center, radius * 0.24f, new Color(0.12f, 0.13f, 0.15f));
                DrawCircle(center, radius * 0.17f, new Color(0.97f, 0.76f, 0.35f));
                // V0-06B: settlements founded by migrants get a white ring so the new dot stands out.
                if (settlement.IsFounded)
                    DrawArc(center, radius * 0.29f, 0f, Mathf.Tau, 24, Colors.White, 2f);
            }
        }

        // V0-06A: travelling migrants, offset to the lower right so a batch still standing on its
        // source settlement does not hide the golden settlement marker.
        var font = ThemeDB.FallbackFont;
        foreach (var batch in Migrants)
        {
            var cell = World.GetCell(batch.Cell.Column, batch.Cell.Row);
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius)
                + new Vector2(radius * 0.38f, radius * 0.30f);
            DrawCircle(center, radius * 0.22f, new Color(0.12f, 0.13f, 0.15f));
            DrawCircle(center, radius * 0.16f, new Color(0.86f, 0.27f, 0.25f));
            DrawString(font, center + new Vector2(-radius * 0.12f, radius * 0.10f), batch.People.ToString(),
                HorizontalAlignment.Left, -1, Mathf.Max(8, (int)(radius * 0.32f)), Colors.White);
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
        if (World is null)
            return;

        var (start, radius) = Layout();
        foreach (var cell in World.Cells)
        {
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            // Hit-test the full hex (the -1.4 inset is only a visual gap) so clicks on
            // the seam between two cells still select a cell.
            if (!Geometry2D.IsPointInPolygon(
                    click.Position, HexCoordinates.Polygon(center, radius)))
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
}
