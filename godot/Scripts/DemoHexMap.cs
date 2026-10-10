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

    /// <summary>Visible TEMP river bank markers, computed by Core only for the foraging scenario.</summary>
    public IReadOnlyList<DemoCellRef> MainRiverTiles { get; set; } = Array.Empty<DemoCellRef>();

    /// <summary>
    /// V0-06A: migrant batches currently travelling, set by ObserverRoot from Core after every
    /// advance. Drawn as red markers with the head count; this view never moves them itself.
    /// </summary>
    public IReadOnlyList<DemoMigrationBatch> Migrants { get; set; } = Array.Empty<DemoMigrationBatch>();

    public event Action<DemoHexCell>? CellSelected;

    /// <summary>#89：事件詳情選到的聚落格（偏移座標），畫亮色外框；null＝不標示。只影響顯示。</summary>
    public (int Column, int Row)? Highlight { get; set; }

    // TEMP display-only controls. Never change simulation state or formal settings.
    private const float TempZoomStep = 1.2f;
    private const float TempMinZoom = 0.6f;
    private const float TempMaxZoom = 3f;
    private float _zoom = 1f;
    private Vector2 _pan = Vector2.Zero;
    private bool _middleDragging;

    public DemoHexMap()
    {
        CustomMinimumSize = new Vector2(540, 460);
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.ExpandFill;
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        TooltipText = "滾輪縮放、中鍵拖曳、左鍵點格；地形與聚落資料來自模擬核心（TEMP）。";
    }

    public override void _Ready()
    {
        Resized += QueueRedraw;
        MouseExited += () => _middleDragging = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (World is null)
            return;

        var (start, radius) = ViewLayout();

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

            // Explicit TEMP river cells are teal, other Water cells remain blue (not automatically fish sources).
            if (cell.Terrain == DemoTerrain.Water
                && MainRiverTiles.Contains(new DemoCellRef(cell.Column, cell.Row)))
                terrainColor = new Color(0.10f, 0.67f, 0.70f);
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

        // #89: outline the settlement of the selected event on top of the terrain and markers.
        if (Highlight is { } focus && focus.Column >= 0 && focus.Column < DemoHexWorld.Columns
            && focus.Row >= 0 && focus.Row < DemoHexWorld.Rows)
        {
            var cell = World.GetCell(focus.Column, focus.Row);
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            var vertices = HexCoordinates.Polygon(center, radius - 1.4f);
            var ring = new Vector2[7];
            for (var i = 0; i < 6; i++)
                ring[i] = vertices[i];
            ring[6] = vertices[0];
            DrawPolyline(ring, new Color(1f, 0.95f, 0.35f), 3.5f);
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
        if (World is null)
            return;

        if (@event is InputEventMouseMotion motion)
        {
            // Clear a missed release (e.g. when the pointer leaves the map).
            if (_middleDragging && !motion.ButtonMask.HasFlag(MouseButtonMask.Middle))
                _middleDragging = false;
            if (_middleDragging)
            {
                _pan += motion.Relative;
                QueueRedraw();
                AcceptEvent();
            }
            return;
        }

        if (@event is not InputEventMouseButton button)
            return;

        if (button.ButtonIndex == MouseButton.Middle)
        {
            _middleDragging = button.Pressed;
            AcceptEvent();
            return;
        }

        if (button.Pressed && button.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown)
        {
            // Cursor-anchored zoom: the point under the pointer does not jump.
            var next = Mathf.Clamp(
                _zoom * (button.ButtonIndex == MouseButton.WheelUp ? TempZoomStep : 1f / TempZoomStep),
                TempMinZoom, TempMaxZoom);
            if (!Mathf.IsEqualApprox(next, _zoom))
            {
                var cursorFromCenter = button.Position - Size * 0.5f;
                _pan = cursorFromCenter - (cursorFromCenter - _pan) * (next / _zoom);
                _zoom = next;
                QueueRedraw();
            }
            AcceptEvent();
            return;
        }

        if (!button.Pressed || button.ButtonIndex != MouseButton.Left || _middleDragging)
            return;

        var (start, radius) = ViewLayout();
        foreach (var cell in World.Cells)
        {
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            // The visible gap must not select an invisible cell.
            if (!Geometry2D.IsPointInPolygon(
                    button.Position, HexCoordinates.Polygon(center, Mathf.Max(0.1f, radius - 1.4f))))
                continue;

            CellSelected?.Invoke(cell);
            AcceptEvent();
            return;
        }
    }

    /// <summary>
    /// #89：把指定格移到地圖中央並標示外框（只改顯示用的平移，不改縮放、不改世界狀態）。
    /// </summary>
    public void FocusCell(DemoHexCell cell)
    {
        _pan += Size * 0.5f - CellCenter(cell);
        Highlight = (cell.Column, cell.Row);
        QueueRedraw();
    }

    /// <summary>Used by the in-engine smoke test to inject a TEMP cell click.</summary>
    public Vector2 CellCenter(DemoHexCell cell)
    {
        var (start, radius) = ViewLayout();
        return start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
    }

    /// <summary>Shared geometry for drawing, hit-testing and all Core cell markers (TEMP view only).</summary>
    private (Vector2 Start, float Radius) ViewLayout()
    {
        var (baseStart, baseRadius) = Layout();
        var viewportCenter = Size * 0.5f;
        return (viewportCenter + (baseStart - viewportCenter) * _zoom + _pan,
            baseRadius * _zoom);
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
