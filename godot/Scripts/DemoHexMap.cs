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

    /// <summary>第 08 章 a 節：土地開發狀態（開墾中、農田用專屬圖塊；與地形分離，只影響顯示）。</summary>
    public DemoLandDevelopmentMap Land { get; set; } = DemoLandDevelopmentMap.Empty;

    /// <summary>Visible TEMP river bank markers, computed by Core only for the foraging scenario.</summary>
    public IReadOnlyList<DemoCellRef> MainRiverTiles { get; set; } = Array.Empty<DemoCellRef>();

    /// <summary>Selected forest or main-river source supply range, computed exclusively by Core.</summary>
    public IReadOnlyList<DemoCellRef> FoodReachCells { get; set; } = Array.Empty<DemoCellRef>();

    /// <summary>
    /// V0-06A: migrant batches currently travelling, set by ObserverRoot from Core after every
    /// advance. Drawn as red markers with the head count; this view never moves them itself.
    /// </summary>
    public IReadOnlyList<DemoMigrationBatch> Migrants { get; set; } = Array.Empty<DemoMigrationBatch>();

    public event Action<DemoHexCell>? CellSelected;

    /// <summary>第 05 章 b 節：地圖模式（只影響顯示）。</summary>
    public enum Mode
    {
        Terrain,
        Food
    }

    private Mode _mode = Mode.Terrain;

    public Mode MapMode
    {
        get => _mode;
        set
        {
            _mode = value;
            QueueRedraw();
        }
    }

    /// <summary>聚落 ID → 糧食狀況（ObserverRoot 每次刷新從 Core 週帳算好注入）。</summary>
    public IReadOnlyDictionary<int, FoodStatus> FoodStatuses { get; set; } = new Dictionary<int, FoodStatus>();

    // TEMP 顯示配色：糧食狀況（充足綠、緊張黃、飢餓紅、未結算灰）。
    public static Color FoodStatusColor(FoodStatus status) => status switch
    {
        FoodStatus.Ample => new Color(0.30f, 0.75f, 0.30f),
        FoodStatus.Tight => new Color(0.95f, 0.78f, 0.20f),
        FoodStatus.Starving => new Color(0.88f, 0.22f, 0.18f),
        _ => new Color(0.65f, 0.65f, 0.65f)
    };

    /// <summary>b 節：地形是否已用素材圖塊繪製（冒煙測試用）。</summary>
    public bool UsesTerrainArt => TerrainTexture(DemoTerrain.Grassland, false) is not null;

    private static readonly Color[] White6 = Enumerable.Repeat(Colors.White, 6).ToArray();

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
            // 第 06 章 a 節：主水道來自 Core 地圖（每個世界都有）；MainRiverTiles 只是舊注入的相容來源。
            var isRiver = cell.MainWaterway || (cell.Terrain == DemoTerrain.Water
                && MainRiverTiles.Contains(new DemoCellRef(cell.Column, cell.Row)));
            // b 節：有地形素材就把圖塊貼進六角形（UV 對齊尖頂六角的外接矩形），沒有就用純色。
            var land = Land.StateOf(new DemoCellRef(cell.Column, cell.Row));
            if ((LandTexture(land) ?? TerrainTexture(cell.Terrain, isRiver)) is { } tile)
            {
                var full = HexCoordinates.Polygon(center, radius);
                DrawPolygon(full, White6, HexUvs(full, center, radius), tile);
                DrawPolyline(Closed(full), new Color(0.07f, 0.06f, 0.04f, 0.35f), 1f);
                if (Sites?.SettlementAt(cell.Column, cell.Row) is { } placed)
                    DrawMarker(SettlementIcon(placed), center, radius * 1.4f);
                continue;
            }
            var terrainColor = cell.Terrain switch
            {
                DemoTerrain.Water => new Color(0.20f, 0.39f, 0.54f),
                DemoTerrain.Forest => new Color(0.17f, 0.38f, 0.28f),
                DemoTerrain.Grassland => new Color(0.41f, 0.56f, 0.34f),
                _ => throw new InvalidOperationException("Unknown TEMP terrain value.")
            };

            // Explicit TEMP river cells are teal, other Water cells remain blue (not automatically fish sources).
            if (isRiver)
                terrainColor = new Color(0.10f, 0.67f, 0.70f);
            // 第 08 章 a 節（沒有圖塊時的後備）：農田土黃、開墾中褐色。
            if (land == DemoLandDevelopment.Farmland)
                terrainColor = new Color(0.72f, 0.62f, 0.30f);
            else if (land == DemoLandDevelopment.Clearing)
                terrainColor = new Color(0.52f, 0.42f, 0.28f);
            DrawColoredPolygon(vertices, terrainColor);
            var outline = new Vector2[7];
            for (var i = 0; i < 6; i++)
                outline[i] = vertices[i];
            outline[6] = vertices[0];
            DrawPolyline(outline, new Color(0.07f, 0.15f, 0.18f), 1.2f);

            if (Sites?.SettlementAt(cell.Column, cell.Row) is { IsRuin: true } ruin)
            {
                // 第 07 章 c 節：遺跡（沒有圖塊時的後備）：灰色小點。
                if (!DrawMarker(ArtAssets.IconRuin, center, radius * 1.4f))
                    DrawCircle(center, radius * 0.17f, new Color(0.45f, 0.43f, 0.40f));
            }
            else if (Sites?.SettlementAt(cell.Column, cell.Row) is { } settlement)
            {
                DrawCircle(center, radius * 0.24f, new Color(0.12f, 0.13f, 0.15f));
                DrawCircle(center, radius * 0.17f, new Color(0.97f, 0.76f, 0.35f));
                // V0-06B: settlements founded by migrants get a white ring so the new dot stands out.
                if (settlement.IsFounded)
                    DrawArc(center, radius * 0.29f, 0f, Mathf.Tau, 24, Colors.White, 2f);
            }
        }

        // 第 05 章 b 節：糧食狀況模式——地形壓暗，聚落格塗上狀況色並加粗外框。
        if (_mode == Mode.Food && Sites is not null)
        {
            foreach (var cell in World.Cells)
            {
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                var hex = HexCoordinates.Polygon(center, radius - 1.4f);
                if (Sites.SettlementAt(cell.Column, cell.Row) is { IsRuin: true } ruinSite)
                {
                    DrawColoredPolygon(hex, new Color(0f, 0f, 0f, 0.45f));
                    DrawMarker(SettlementIcon(ruinSite), center, radius * 1.4f);
                }
                else if (Sites.SettlementAt(cell.Column, cell.Row) is { } site)
                {
                    var status = FoodStatuses.TryGetValue(site.Id, out var known) ? known : FoodStatus.Unsettled;
                    var color = FoodStatusColor(status);
                    DrawColoredPolygon(hex, new Color(color, 0.55f));
                    DrawPolyline(Closed(hex), color, 3.5f);
                    DrawMarker(SettlementIcon(site), center, radius * 1.4f);
                }
                else
                {
                    DrawColoredPolygon(hex, new Color(0f, 0f, 0f, 0.45f));
                }
            }
        }

        // 第 06 章 a 節：渡口（過河唯一的陸路）畫一圈褐色虛線環，路線段全部來自 Core。
        if (World.Routes.Ford is { } ford)
        {
            var fordCell = World.GetCell(ford.Column, ford.Row);
            var fordCenter = start + HexCoordinates.Center(fordCell.AxialQ, fordCell.AxialR, radius);
            DrawArc(fordCenter, radius * 0.45f, 0f, Mathf.Tau, 24, new Color(0.55f, 0.36f, 0.16f, 0.95f), 3f);
            foreach (var segment in World.Routes.Segments.Where(x => x.Kind == DemoRouteKind.Ford))
            {
                var other = World.GetCell(segment.Other(ford).Column, segment.Other(ford).Row);
                var otherCenter = start + HexCoordinates.Center(other.AxialQ, other.AxialR, radius);
                DrawDashedLine(fordCenter, otherCenter, new Color(0.55f, 0.36f, 0.16f, 0.9f), 2f, 6f);
            }
        }

        // 第 08-2 章：不再畫選取來源的供應範圍外框（使用者 2026-10-21：不需要用圈和線標示共用食物範圍）；
        // FoodReachCells 仍由 Core 注入，只供冒煙測試核對幾何。

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

        // V0-06A: travelling migrants. 第 08-2 章：圖示畫在六角格正中央（使用者 2026-10-21），人數標籤在右下角。
        var font = ThemeDB.FallbackFont;
        foreach (var batch in Migrants)
        {
            var cell = World.GetCell(batch.Cell.Column, batch.Cell.Row);
            var center = MigrantMarkerCenter(start, radius, cell);
            if (!DrawMarker(ArtAssets.IconMigrants, center, radius * 0.95f))
            {
                DrawCircle(center, radius * 0.22f, new Color(0.12f, 0.13f, 0.15f));
                DrawCircle(center, radius * 0.16f, new Color(0.86f, 0.27f, 0.25f));
            }
            // 人數標籤：深色底＋白字，圖示或圓點上都看得清楚。
            var size = Mathf.Max(8, (int)(radius * 0.32f));
            var labelPos = center + new Vector2(radius * 0.05f, radius * 0.38f);
            DrawCircle(labelPos + new Vector2(size * 0.3f, -size * 0.35f), size * 0.62f, new Color(0.12f, 0.08f, 0.05f, 0.85f));
            DrawString(font, labelPos, batch.People.ToString(), HorizontalAlignment.Left, -1, size, Colors.White);
        }
    }

    /// <summary>第 08-2 章：遷徙隊伍圖示的中心＝所在六角格的中心（冒煙測試核對）。</summary>
    private static Vector2 MigrantMarkerCenter(Vector2 start, float radius, DemoHexCell cell)
        => start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);

    /// <summary>冒煙測試用：某一格遷徙圖示的畫面中心（應等於 <see cref="CellCenter"/>）。</summary>
    public Vector2 MigrantCenter(DemoHexCell cell)
    {
        var (start, radius) = ViewLayout();
        return MigrantMarkerCenter(start, radius, cell);
    }

    /// <summary>第 08 章 a 節：開發狀態的圖塊（未開發＝null，改用地形圖塊）。</summary>
    private static Texture2D? LandTexture(DemoLandDevelopment state) => state switch
    {
        DemoLandDevelopment.Farmland => ArtAssets.Texture(ArtAssets.TileFarmland),
        DemoLandDevelopment.Clearing => ArtAssets.Texture(ArtAssets.TileFarmlandClearing),
        _ => null
    };

    /// <summary>第 08 章 a 節：冒煙測試用——農田與開墾中的圖塊都讀得到。</summary>
    public static bool HasLandArt
        => LandTexture(DemoLandDevelopment.Farmland) is not null && LandTexture(DemoLandDevelopment.Clearing) is not null;

    private static Texture2D? TerrainTexture(DemoTerrain terrain, bool river) => terrain switch
    {
        DemoTerrain.Grassland => ArtAssets.Texture(ArtAssets.TileGrassland),
        DemoTerrain.Forest => ArtAssets.Texture(ArtAssets.TileForest),
        DemoTerrain.Water => ArtAssets.Texture(river ? ArtAssets.TileRiver : ArtAssets.TileWater),
        _ => null
    };

    /// <summary>尖頂六角頂點 → 圖塊 UV（圖塊＝六角形外接矩形：寬 √3·r、高 2r）。</summary>
    private static Vector2[] HexUvs(Vector2[] vertices, Vector2 center, float radius)
    {
        var halfWidth = Mathf.Sqrt(3f) * 0.5f * radius;
        var uvs = new Vector2[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
            uvs[i] = new Vector2((vertices[i].X - center.X + halfWidth) / (2f * halfWidth),
                (vertices[i].Y - center.Y + radius) / (2f * radius));
        return uvs;
    }

    private static Vector2[] Closed(Vector2[] vertices)
    {
        var ring = new Vector2[vertices.Length + 1];
        vertices.CopyTo(ring, 0);
        ring[^1] = vertices[0];
        return ring;
    }

    /// <summary>在中心畫一個正方形圖示；素材不存在回傳 false（呼叫端改畫圓點）。</summary>
    /// <summary>第 07 章 c 節：遺跡／新聚落／村莊圖示。</summary>
    private static string SettlementIcon(DemoSettlement settlement)
        => settlement.IsRuin ? ArtAssets.IconRuin : settlement.IsFounded ? ArtAssets.IconNewSettlement : ArtAssets.IconVillage;

    private bool DrawMarker(string path, Vector2 center, float size)
    {
        if (ArtAssets.Texture(path) is not { } icon)
            return false;
        DrawTextureRect(icon, new Rect2(center - new Vector2(size, size) * 0.5f, new Vector2(size, size)), false);
        return true;
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
