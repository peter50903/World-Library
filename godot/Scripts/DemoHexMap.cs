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
        Food,
        Lineage, // 第 08-10a 章 V2：家系
        Knowledge, // 第 08-10a 章 V4：知識（已習得幾項技術）
        Culture, // 第 08-10a 章 V5、08-8 章 B1：文化（各聚落的主要文化）
        Faith // 第 08-8 章 B3：信仰（各聚落的主要信仰；無信仰灰色，聖地標記）
    }

    /// <summary>第 08-8 章 B3：聚落的主要信仰編號（沒有信徒的聚落不在表內）。</summary>
    public IReadOnlyDictionary<int, int> FaithOf { get; set; } = new Dictionary<int, int>();

    /// <summary>第 08-16 章：各文化的紋章、各信仰的符號（文化與信仰模式畫在聚落上）。</summary>
    public IReadOnlyDictionary<int, DemoEmblem> CultureEmblems { get; set; } = new Dictionary<int, DemoEmblem>();
    public IReadOnlyDictionary<int, string> FaithSymbolsById { get; set; } = new Dictionary<int, string>();

    /// <summary>第 08-8 章 B3：信仰的固定順序（信仰編號），決定配色。</summary>
    public IReadOnlyList<int> FaithOrder { get; set; } = Array.Empty<int>();

    /// <summary>第 08-8 章 B3：現存信仰的創立聚落（聖地）。</summary>
    public IReadOnlySet<int> HolySites { get; set; } = new HashSet<int>();

    /// <summary>無信仰聚落的顏色。</summary>
    public static readonly Color TempNoFaithColor = new(0.55f, 0.55f, 0.55f);

    public Color FaithColor(int faithId)
    {
        if (faithId == 0)
            return TempNoFaithColor;
        var index = 0;
        for (var i = 0; i < FaithOrder.Count; i++)
            if (FaithOrder[i] == faithId) index = i;
        // 與家系錯開四分之一圈、與文化錯開，三個模式切換時顏色不同。
        return TempLineagePalette[(index + TempLineagePalette.Length / 4) % TempLineagePalette.Length];
    }

    /// <summary>第 08-8 章 B1：聚落的主要文化編號（文化模式著色）。</summary>
    public IReadOnlyDictionary<int, int> CultureOf { get; set; } = new Dictionary<int, int>();

    /// <summary>第 08-8 章 B1：文化的固定順序（文化編號），決定配色。</summary>
    public IReadOnlyList<int> CultureOrder { get; set; } = Array.Empty<int>();

    public Color CultureColor(int cultureId)
    {
        var index = 0;
        for (var i = 0; i < CultureOrder.Count; i++)
            if (CultureOrder[i] == cultureId) index = i;
        // 與家系錯開半圈，兩個模式切換時顏色不會一樣。
        return TempLineagePalette[(index + TempLineagePalette.Length / 2) % TempLineagePalette.Length];
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

    /// <summary>第 08-10a 章 V2：每一格的家系歸屬（Core <see cref="DemoLineageMap.Compute"/>，ObserverRoot 刷新時注入）。</summary>
    public IReadOnlyDictionary<DemoCellRef, DemoLineageCell> Lineage { get; set; } = new Dictionary<DemoCellRef, DemoLineageCell>();

    /// <summary>第 08-10a 章 V2：家系的固定順序（開局聚落編號），決定配色。</summary>
    public IReadOnlyList<int> LineageRoots { get; set; } = Array.Empty<int>();

    /// <summary>第 08-10a 章 V4：聚落已習得的技術數（知識模式著色）。</summary>
    public IReadOnlyDictionary<int, int> KnowledgeCounts { get; set; } = new Dictionary<int, int>();

    // TEMP 顯示配色：知識（第 08-7a 章 A1 起技術變多，改依習得數分五段：0 灰褐、1～4 淡金、5～9 橙、10～14 赭紅、15 以上深紅）。
    public static readonly int[] KnowledgeBandStarts = { 0, 1, 5, 10, 15 }; // TEMP 分段

    public static int KnowledgeBand(int count) => Array.FindLastIndex(KnowledgeBandStarts, start => count >= start) is var i and >= 0 ? i : 0;

    /// <summary>第 08-13 章：聚落最擅長的領域（知識模式的色相）；沒有＝不在表內。</summary>
    public IReadOnlyDictionary<int, string> KnowledgeDomain { get; set; } = new Dictionary<int, string>();

    /// <summary>TEMP 顯示配色（第 08-13 章）：知識模式各領域的色相（飽和、彼此區隔）。</summary>
    public static Color DomainMapColor(string? domain) => domain switch
    {
        "10B" => new Color(0.45f, 0.75f, 0.20f), "10C" => new Color(0.55f, 0.55f, 0.60f), "10D" => new Color(0.80f, 0.62f, 0.30f),
        "10E" => new Color(0.55f, 0.35f, 0.15f), "10F" => new Color(0.85f, 0.40f, 0.65f), "10G" => new Color(0.95f, 0.70f, 0.15f),
        "10H" => new Color(0.20f, 0.75f, 0.60f), "10I" => new Color(0.75f, 0.15f, 0.15f), "10J" => new Color(0.25f, 0.45f, 0.90f),
        "10K" => new Color(0.60f, 0.30f, 0.90f), "10L" => new Color(0.90f, 0.45f, 0.25f), "10M" => new Color(0.95f, 0.85f, 0.30f),
        "10N" => new Color(0.50f, 0.42f, 0.30f), "HUNT" => new Color(0.20f, 0.50f, 0.25f), "SOCIETY" => new Color(0.45f, 0.35f, 0.75f),
        "FAITH" => new Color(0.95f, 0.95f, 0.75f),
        _ => new Color(0.45f, 0.42f, 0.38f)
    };

    /// <summary>第 08-13 章：知識模式顏色＝擅長領域的色相，技術越少越淡。</summary>
    public Color KnowledgeTint(int settlementId)
    {
        var count = KnowledgeCounts.GetValueOrDefault(settlementId);
        var hue = DomainMapColor(KnowledgeDomain.GetValueOrDefault(settlementId));
        return KnowledgeBand(count) switch
        {
            0 => DomainMapColor(null),
            1 => hue.Lerp(Colors.White, 0.45f),
            2 => hue.Lerp(Colors.White, 0.2f),
            _ => hue
        };
    }

    public static Color KnowledgeColor(int count) => KnowledgeBand(count) switch
    {
        0 => new Color(0.45f, 0.42f, 0.38f),
        1 => new Color(0.92f, 0.82f, 0.45f),
        2 => new Color(0.90f, 0.58f, 0.22f),
        3 => new Color(0.72f, 0.28f, 0.16f),
        _ => new Color(0.50f, 0.12f, 0.14f)
    };

    /// <summary>第 08-10a 章 V2：聚落人口與是否為村莊（決定圖示大小）。</summary>
    public IReadOnlyDictionary<int, (long Population, DemoSettlementTier Tier)> SettlementScale { get; set; } = new Dictionary<int, (long, DemoSettlementTier)>();

    // TEMP 顯示配色：家系（低飽和、羊皮紙地圖風格，12 色循環）。
    private static readonly Color[] TempLineagePalette =
    {
        new(0.78f, 0.33f, 0.27f), new(0.27f, 0.47f, 0.70f), new(0.85f, 0.65f, 0.24f), new(0.42f, 0.62f, 0.33f),
        new(0.58f, 0.38f, 0.65f), new(0.25f, 0.62f, 0.62f), new(0.80f, 0.48f, 0.62f), new(0.55f, 0.45f, 0.30f),
        new(0.40f, 0.40f, 0.75f), new(0.70f, 0.70f, 0.35f), new(0.50f, 0.25f, 0.25f), new(0.30f, 0.50f, 0.45f)
    };

    /// <summary>第 08-10a 章 V2：某家系的顏色（依開局聚落順序；同一世界不變）。</summary>
    public Color LineageColor(int rootId)
    {
        var index = 0;
        for (var i = 0; i < LineageRoots.Count; i++)
            if (LineageRoots[i] == rootId) index = i;
        return TempLineagePalette[index % TempLineagePalette.Length];
    }

    /// <summary>第 08-10a 章 V2、第 08-11 章【TEMP】聚落圖示大小：營地依人口 1.15～1.4，村莊起每升一級大一些（TEMP）。</summary>
    private float IconSize(DemoSettlement settlement, float radius)
    {
        if (settlement.IsRuin || !SettlementScale.TryGetValue(settlement.Id, out var scale))
            return radius * 1.4f;
        return radius * (scale.Tier switch
        {
            DemoSettlementTier.Camp => scale.Population < 10 ? 1.15f : 1.4f,
            DemoSettlementTier.Village => 1.65f,
            DemoSettlementTier.Town => 1.85f,
            _ => 2.0f
        });
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


    /// <summary>第 06-2 章：是否畫六角格線（預設不畫，地形看起來連成一片；功能列「格線」可切換）。只影響顯示。</summary>
    public bool ShowGrid
    {
        get => _showGrid;
        set
        {
            _showGrid = value;
            QueueRedraw();
        }
    }

    private bool _showGrid;

    /// <summary>第 08-11 章：已消失的遺跡（不重要的遺跡荒廢數年後還原成原本的土地，地圖不再畫）。</summary>
    public IReadOnlySet<int> FadedRuins { get; set; } = new HashSet<int>();

    /// <summary>第 12-1 章 C3：一年內遭天災的聚落（地圖畫災害標記）。</summary>
    public IReadOnlySet<int> RecentDisasters { get; set; } = new HashSet<int>();

    /// <summary>第 08-13 章：熱浪、野火（地形外觀）與目前週次。</summary>
    public IReadOnlyList<DemoEnvironmentSpell> Spells { get; set; } = Array.Empty<DemoEnvironmentSpell>();
    public int CurrentWeek { get; set; }

    /// <summary>第 08-13 章：環境外觀的圖塊；沒有素材時用原地形圖塊染色（燃燒橘紅、焦土暗褐、乾旱枯黃）。</summary>
    private static (Texture2D? Tile, Color Tint) LookArt(DemoEnvironment.Look look) => look switch
    {
        DemoEnvironment.Look.Burning => (ArtAssets.Texture(ArtAssets.TileBurning), new Color(1.0f, 0.45f, 0.25f)),
        DemoEnvironment.Look.Scorched => (ArtAssets.Texture(ArtAssets.TileScorched), new Color(0.45f, 0.38f, 0.32f)),
        DemoEnvironment.Look.Dry => (ArtAssets.Texture(ArtAssets.TileGrasslandDry), new Color(1.0f, 0.86f, 0.55f)),
        _ => (null, Colors.White)
    };

    /// <summary>第 12-1 章 C4：地下城所在格（近景與區域層畫地下城標記）。</summary>
    public IReadOnlyCollection<DemoCellRef> Dungeons { get; set; } = Array.Empty<DemoCellRef>();

    /// <summary>第 12-1 章：隕石留下的礦脈格（疊礦脈圖）。</summary>
    public IReadOnlySet<DemoCellRef> MeteoriteOre { get; set; } = new HashSet<DemoCellRef>();

    /// <summary>畫面上看得到的聚落（已消失的遺跡＝null）。</summary>
    private DemoSettlement? VisibleSite(DemoHexCell cell)
        => Sites?.SettlementAt(cell.Column, cell.Row) is { } s && !FadedRuins.Contains(s.Id) ? s : null;

    /// <summary>【TEMP】第 08-11 章格線：不透明度與線寬（格半徑的比例，至少 1.2 像素）。</summary>
    private const float TempGridAlpha = 0.7f;
    private const float TempGridWidthPerRadius = 0.04f;

    /// <summary>第 06-2 章：本週已過幾成（0～1）；遷徙隊伍圖示沿路線在本格與下一格之間平滑移動。只影響顯示。</summary>
    public float WeekFraction { get; set; }

    /// <summary>#89：事件詳情選到的聚落格（偏移座標），畫亮色外框；null＝不標示。只影響顯示。</summary>
    public (int Column, int Row)? Highlight { get; set; }

    // TEMP display-only controls. Never change simulation state or formal settings.
    private const float TempZoomStep = 1.2f;
    private const float TempMinZoom = 0.6f;
    private const float TempMaxZoom = 3f;
    /// <summary>第 08-13 章第 5 節【TEMP】大地圖：開新世界時鏡頭拉近到每格約這個半徑（像素），可縮小看全圖；最大縮放到每格這麼大。</summary>
    private const float TempComfortHexRadius = 26f;
    private const float TempMaxHexRadius = 64f;
    private DemoHexWorld? _viewWorld;
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
        TooltipText = " "; // 第 08-5 章：提示文字依滑鼠所在格（_GetTooltip）
    }

    private TerrainSurface? _surface;

    public override void _Ready()
    {
        // 第 06-4 章第 2 步：連續地表（畫在本節點的圖示與建築下面）。
        _surface = new TerrainSurface();
        AddChild(_surface);
        _surface.SetAnchorsPreset(LayoutPreset.FullRect);
        Resized += QueueRedraw;
        MouseExited += () => _middleDragging = false;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (World is null)
        {
            _surface?.Hide();
            return;
        }

        var (start, radius) = ViewLayout();

        // 第 08-15 章：遠看（戰略層、區域層）用簡化畫法；近景層才畫圖塊、建築與格線。
        var layer = LayerFor(radius);
        // 第 06-4 章第 2 步：近景層、區域層的地形模式改畫連續地表（不再一格一張六角形圖塊）。
        if (_surface is not null)
        {
            // 試玩回饋 2026-11-04：地表只畫在左上一塊——錨點沒有讓它跟著地圖變大（容器排版、視窗縮放時），
            // 每次重畫都直接把它設成與地圖同位置、同大小。
            if (_surface.Position != Vector2.Zero || _surface.Size != Size)
            {
                _surface.Position = Vector2.Zero;
                _surface.Size = Size;
            }
            if (layer == ViewLayer.Local || (layer == ViewLayer.Regional && _mode == Mode.Terrain))
                _surface.Show(SurfaceCodes(), World.Columns, World.Rows, start, radius, _ground);
            else
                _surface.Hide();
        }
        if (layer != ViewLayer.Local)
        {
            DrawSimplified(start, radius, layer);
            DrawHighlightAndMigrants(start, radius, migrants: false); // 試玩回饋 2026-11-05 之 3：遠看不畫遷徙與天災動畫，只看地塊與建築
            return;
        }

        foreach (var cell in World.Cells)
        {
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            if (!OnScreen(center, radius))
                continue; // 第 08-13 章第 5 節：畫面外的格不畫（大地圖）
            if (_surface is { Active: true })
            {
                // 地形已由連續地表畫好；這裡只畫聚落圖示（近看畫建築，遠看與遺跡畫圖示）。
                if (VisibleSite(cell) is { } site && _mode is not (Mode.Lineage or Mode.Knowledge or Mode.Culture or Mode.Faith)
                    && (site.IsRuin || !UseFootprints(radius))
                    // 第 08-16 章第 12 節：近看的遺跡畫像素零件（倒塌的石牆）。
                    && !(site.IsRuin && UseFootprints(radius) && DrawPixelPart("bld_ruin", center + new Vector2(0f, radius * 0.3f), radius)))
                    DrawMarker(SettlementIcon(site), center, IconSize(site, radius));
                continue;
            }
            var vertices = HexCoordinates.Polygon(center, radius - 1.4f);
            // 第 06 章 a 節：主水道來自 Core 地圖（每個世界都有）；MainRiverTiles 只是舊注入的相容來源。
            var isRiver = cell.MainWaterway || (cell.Terrain == DemoTerrain.Water
                && MainRiverTiles.Contains(new DemoCellRef(cell.Column, cell.Row)));
            // b 節：有地形素材就把圖塊貼進六角形（UV 對齊尖頂六角的外接矩形），沒有就用純色。
            var land = Land.StateOf(new DemoCellRef(cell.Column, cell.Row));
            var shallowArt = cell.Shallow ? ArtAssets.Texture(ArtAssets.TileRiverShallow) : null;
            // 第 08-13 章：熱浪、野火的地形外觀（有素材換圖，沒有就在原圖塊上染色）。
            var look = DemoEnvironment.LookAt(Spells, cell, CurrentWeek);
            var (lookTile, lookTint) = LookArt(look);
            if ((lookTile ?? LandTexture(land) ?? shallowArt ?? TerrainTexture(cell.Terrain, isRiver)) is { } tile)
            {
                // 第 08-13 章（試玩回饋三之 2）：圖塊各自帶深色描邊，相鄰格看得出接縫——
                // 取樣時往內縮掉描邊（TempTileInset），並把六角形畫大一點點讓相鄰格重疊，格線關閉時地形連成一片。
                // 第 08-15 章第 3 節：外形是不規則邊界（相鄰格共用同一條彎曲的邊）。
                var full = ScreenShape(cell, center, radius + TempTileOverlap);
                DrawShape(center, full, lookTile is null && look != DemoEnvironment.Look.None ? lookTint : WaterTint(cell, shallowArt is not null), tile, radius);
                // 第 06-3 章：礦脈疊圖（農田、開墾中不疊）。
                if ((cell.Ore || MeteoriteOre.Contains(new DemoCellRef(cell.Column, cell.Row))) && LandTexture(land) is null && ArtAssets.Texture(ArtAssets.OverlayOreVein) is { } ore)
                    DrawShape(center, full, Colors.White, ore, radius);
                if (cell.Shallow && shallowArt is null)
                    DrawStones(center, radius);
                if (VisibleSite(cell) is { } placed && _mode is not (Mode.Lineage or Mode.Knowledge or Mode.Culture or Mode.Faith)
                    && (placed.IsRuin || !UseFootprints(radius))) // 第 08-13 章第 4 節：近看畫建築（地形畫完後），遠看畫圖示
                    DrawMarker(SettlementIcon(placed), center, IconSize(placed, radius));
                continue;
            }
            var terrainColor = cell.Terrain switch
            {
                DemoTerrain.Water => new Color(0.20f, 0.39f, 0.54f),
                DemoTerrain.Forest => new Color(0.17f, 0.38f, 0.28f),
                DemoTerrain.Grassland => new Color(0.41f, 0.56f, 0.34f),
                DemoTerrain.Hills => new Color(0.52f, 0.47f, 0.33f), // 第 06-3 章
                _ => throw new InvalidOperationException("Unknown TEMP terrain value.")
            };

            // Explicit TEMP river cells are teal, other Water cells remain blue (not automatically fish sources).
            if (isRiver)
                terrainColor = cell.Shallow ? new Color(0.45f, 0.80f, 0.78f) : new Color(0.10f, 0.67f, 0.70f);
            else if (cell.Sea)
                terrainColor = new Color(0.10f, 0.22f, 0.40f); // 第 06-1 章：海洋深藍
            // 第 08 章 a 節（沒有圖塊時的後備）：農田土黃、開墾中褐色。
            if (land == DemoLandDevelopment.Farmland)
                terrainColor = new Color(0.72f, 0.62f, 0.30f);
            else if (land == DemoLandDevelopment.Clearing)
                terrainColor = new Color(0.52f, 0.42f, 0.28f);
            DrawShape(center, ScreenShape(cell, center, radius), terrainColor);
            var outline = new Vector2[7];
            for (var i = 0; i < 6; i++)
                outline[i] = vertices[i];
            outline[6] = vertices[0];

            if (VisibleSite(cell) is { IsRuin: true } ruin)
            {
                // 第 07 章 c 節：遺跡（沒有圖塊時的後備）：灰色小點。
                if (!DrawMarker(ArtAssets.IconRuin, center, radius * 1.4f))
                    DrawCircle(center, radius * 0.17f, new Color(0.45f, 0.43f, 0.40f));
            }
            else if (VisibleSite(cell) is { } settlement && !UseFootprints(radius))
            {
                // 第 12-1 章 C3：一年內遭天災的聚落在右上角疊災害標記。
                if (RecentDisasters.Contains(settlement.Id))
                    DrawMarker(ArtAssets.IconDisaster, center + new Vector2(radius * 0.45f, -radius * 0.45f), radius * 0.8f);
                DrawCircle(center, radius * 0.24f, new Color(0.12f, 0.13f, 0.15f));
                DrawCircle(center, radius * 0.17f, new Color(0.97f, 0.76f, 0.35f));
                // V0-06B: settlements founded by migrants get a white ring so the new dot stands out.
                if (settlement.IsFounded)
                    DrawArc(center, radius * 0.29f, 0f, Mathf.Tau, 24, Colors.White, 2f);
            }
        }

        // 第 08-13 章第 4 節：聚落建築與牆畫在全部地形之上（鄰格圖塊不會蓋住屋子）。
        if (UseFootprints(radius) && _mode is not (Mode.Lineage or Mode.Knowledge or Mode.Culture or Mode.Faith))
        {
            if (_surface is { Active: true })
                DrawLayouts(start, radius); // 第 08-17 章：對齊方格的聚落版面
            else
                DrawFootprints(start, radius);
        }

        // 第 05 章 b 節：糧食狀況模式——地形壓暗，聚落格塗上狀況色並加粗外框。
        if (_mode == Mode.Food && Sites is not null)
        {
            foreach (var cell in World.Cells)
            {
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                if (!OnScreen(center, radius))
                    continue;
                var hex = ScreenShape(cell, center, radius);
                if (VisibleSite(cell) is { IsRuin: true } ruinSite)
                {
                    DrawShape(center, hex, new Color(0f, 0f, 0f, 0.45f));
                    DrawMarker(SettlementIcon(ruinSite), center, radius * 1.4f);
                }
                else if (VisibleSite(cell) is { } site)
                {
                    var status = FoodStatuses.TryGetValue(site.Id, out var known) ? known : FoodStatus.Unsettled;
                    var color = FoodStatusColor(status);
                    DrawShape(center, hex, new Color(color, 0.55f));
                    DrawPolyline(Closed(hex), color, 3.5f);
                    DrawMarker(SettlementIcon(site), center, IconSize(site, radius));
                }
                else
                {
                    DrawShape(center, hex, new Color(0f, 0f, 0f, 0.45f));
                }
            }
        }

        // 第 08-10a 章 V2：家系模式——地形壓暗，聚落與採集範圍塗家系色，家系之間畫合併後的邊界（參考 WorldBox／CK3 分布圖層）。
        if (_mode == Mode.Lineage && Sites is not null)
            DrawTerritory(start, radius, owner => LineageColor(owner.LineageRootId), owner => owner.LineageRootId);
        // 第 08-8 章 B1：文化模式——同樣的領域，依聚落的主要文化著色。
        if (_mode == Mode.Culture && Sites is not null)
        {
            DrawTerritory(start, radius, owner => CultureColor(CultureOf.GetValueOrDefault(owner.SettlementId)),
                owner => CultureOf.GetValueOrDefault(owner.SettlementId));
            // 第 08-16 章：每個聚落插上主要文化的紋章。
            foreach (var site in Sites.Settlements.Where(s => !s.IsRuin && CultureOf.ContainsKey(s.Id)))
            {
                var cultureId = CultureOf[site.Id];
                if (!CultureEmblems.TryGetValue(cultureId, out var emblem) || Heraldry.Emblem(emblem, CultureColor(cultureId)) is not { } shield)
                    continue;
                var cell = World!.GetCell(site.Column, site.Row);
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                if (OnScreen(center, radius))
                    DrawTextureRect(shield, new Rect2(center - new Vector2(radius, radius) * 0.55f, new Vector2(radius, radius) * 1.1f), false);
            }
        }
        // 第 08-8 章 B3：信仰模式——同樣的領域，依主要信仰著色；聖地（創立聚落）另畫標記。
        if (_mode == Mode.Faith && Sites is not null)
        {
            DrawTerritory(start, radius, owner => FaithColor(FaithOf.GetValueOrDefault(owner.SettlementId)),
                owner => FaithOf.GetValueOrDefault(owner.SettlementId));
            // 第 08-16 章：每個有信徒的聚落畫上主要信仰的符號（深色圓底）。
            foreach (var site in Sites.Settlements.Where(s => !s.IsRuin && FaithOf.ContainsKey(s.Id) && !HolySites.Contains(s.Id)))
            {
                var faithId = FaithOf[site.Id];
                if (!FaithSymbolsById.TryGetValue(faithId, out var symbol) || Heraldry.FaithSymbol(symbol, FaithColor(faithId)) is not { } sign)
                    continue;
                var cell = World!.GetCell(site.Column, site.Row);
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                if (!OnScreen(center, radius))
                    continue;
                DrawCircle(center, radius * 0.45f, new Color(0.10f, 0.08f, 0.07f, 0.85f));
                DrawTextureRect(sign, new Rect2(center - new Vector2(radius, radius) * 0.4f, new Vector2(radius, radius) * 0.8f), false);
            }
            var holy = ArtAssets.Texture(ArtAssets.IconHolySite);
            foreach (var site in Sites.Settlements.Where(s => HolySites.Contains(s.Id)))
            {
                var cell = World!.GetCell(site.Column, site.Row);
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                if (holy is not null)
                    DrawTextureRect(holy, new Rect2(center - new Vector2(radius, radius) * 0.7f, new Vector2(radius, radius) * 1.4f), false);
                else
                {
                    DrawCircle(center, radius * 0.35f, new Color(0.95f, 0.80f, 0.35f));
                    DrawArc(center, radius * 0.35f, 0, Mathf.Tau, 24, Colors.Black, 2f);
                }
            }
        }
        // 第 08-10a 章 V4：知識模式——同樣的領域，依聚落已習得的技術數著色（看得出技術從哪裡傳開）。
        if (_mode == Mode.Knowledge && Sites is not null)
            DrawTerritory(start, radius, owner => KnowledgeTint(owner.SettlementId),
                owner => (KnowledgeDomain.GetValueOrDefault(owner.SettlementId) ?? "").GetHashCode() * 8 + KnowledgeBand(KnowledgeCounts.GetValueOrDefault(owner.SettlementId)));

        // 第 08-2 章：渡口不再畫褐色環與虛線（使用者 2026-10-22：不要圈和線）。第 06-1 章：渡口改畫成淺灘圖塊。

        // 第 08-2 章：不再畫選取來源的供應範圍外框（使用者 2026-10-21：不需要用圈和線標示共用食物範圍）；
        // FoodReachCells 仍由 Core 注入，只供冒煙測試核對幾何。

        // 第 08-11 章：格線最後畫（原本畫在每格圖塊之後，被鄰格圖塊與地圖模式疊層蓋掉，開了也看不出來）。
        if (_showGrid)
        {
            var gridColor = new Color(0.05f, 0.04f, 0.03f, TempGridAlpha);
            foreach (var cell in World.Cells)
            {
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                if (OnScreen(center, radius))
                    DrawPolyline(Closed(ScreenShape(cell, center, radius)), gridColor, Math.Max(1.2f, radius * TempGridWidthPerRadius));
            }
        }

        DrawHighlightAndMigrants(start, radius, migrants: true);
    }

    /// <summary>選取外框與途中隊伍（各視角層共用；戰略層不畫隊伍）。</summary>
    private void DrawHighlightAndMigrants(Vector2 start, float radius, bool migrants)
    {
        // 試玩回饋 2026-11-03 之 3：天災當下的動畫與燃燒的煙（近景與區域層）。
        if (migrants)
            DrawDisasterEffects(start, radius);
        // 第 12-1 章 C4：地下城標記（近景與區域層；戰略層只看色塊）。沒有素材時畫深色圓點加紅框。
        if (migrants)
        {
            foreach (var d in Dungeons)
            {
                if (!World!.Contains(d.Column, d.Row))
                    continue;
                var cell = World.GetCell(d.Column, d.Row);
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                if (!OnScreen(center, radius))
                    continue;
                if (!DrawMarker(ArtAssets.IconDungeon, center, Math.Max(radius * 1.3f, 14f)))
                {
                    DrawCircle(center, Math.Max(radius * 0.3f, 4f), new Color(0.10f, 0.08f, 0.10f));
                    DrawArc(center, Math.Max(radius * 0.3f, 4f), 0, Mathf.Tau, 24, new Color(0.75f, 0.20f, 0.18f), 2f);
                }
            }
        }

        // #89: outline the settlement of the selected event on top of the terrain and markers.
        if (Highlight is { } focus && World!.Contains(focus.Column, focus.Row))
        {
            var cell = World.GetCell(focus.Column, focus.Row);
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            // 試玩回饋 2026-11-04 之 4：畫了像素地表時，選取框沿這一格實際的像素邊緣（深色底線＋亮黃線）。
            if (_surface?.OutlineOf(focus.Row * World.Columns + focus.Column) is { Length: > 0 } outline)
            {
                DrawMultiline(outline, new Color(0.1f, 0.08f, 0.05f, 0.8f), 4.5f);
                DrawMultiline(outline, new Color(1f, 0.95f, 0.35f), 2f);
            }
            else
                DrawPolyline(Closed(ScreenShape(cell, center, radius - 1.4f)), new Color(1f, 0.95f, 0.35f), 3.5f);
        }

        // V0-06A: travelling migrants. 第 08-2 章：圖示畫在六角格正中央（使用者 2026-10-21），人數標籤在右下角。
        if (!migrants)
            return;
        DrawMigrants(start, radius); // 第 08-16 章：沿路線走路的小人群
    }

    /// <summary>
    /// 第 08-10a 章：領域著色——地形壓暗，聚落與採集範圍（Core <see cref="DemoLineageMap"/>）依 <paramref name="color"/> 上色；
    /// 相鄰格 <paramref name="group"/> 不同（或沒有歸屬、在地圖外）時才畫邊界，同組連成一片。
    /// </summary>
    private void DrawTerritory(Vector2 start, float radius, Func<DemoLineageCell, Color> color, Func<DemoLineageCell, int> group)
    {
        foreach (var cell in World!.Cells)
        {
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            if (!OnScreen(center, radius))
                continue; // 第 08-15 章：畫面外不畫
            var hex = ScreenShape(cell, center, radius);
            if (!Lineage.TryGetValue(new DemoCellRef(cell.Column, cell.Row), out var owner))
            {
                DrawShape(center, hex, new Color(0f, 0f, 0f, 0.45f));
                continue;
            }
            var fill = color(owner);
            DrawShape(center, hex, new Color(fill, 0.55f));
            // 邊界：相鄰格不同家系（或沒有歸屬、在地圖外）時才畫這一條邊，同家系之間不畫線，看起來是一整片。
            foreach (var (dq, dr, _) in DemoRouteNetwork.Directions)
            {
                var neighbour = World.AtAxial(cell.AxialQ + dq, cell.AxialR + dr);
                var same = neighbour is not null && Lineage.TryGetValue(new DemoCellRef(neighbour.Column, neighbour.Row), out var other)
                    && group(other) == group(owner);
                if (same)
                    continue;
                var offset = HexCoordinates.Center(cell.AxialQ + dq, cell.AxialR + dr, radius) - HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                var edge = (int)Mathf.PosMod(Mathf.Round(Mathf.RadToDeg(offset.Angle()) / 60f), 6f);
                DrawPolyline(EdgeOf(hex, edge), fill.Darkened(0.35f), 3f);
            }
        }
        foreach (var site in Sites!.Settlements)
        {
            var cell = World.GetCell(site.Column, site.Row);
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            DrawMarker(SettlementIcon(site), center, IconSize(site, radius));
        }
    }

    /// <summary>
    /// 第 06-1 章（TEMP 顯示）：淺灘沒有專屬圖塊時把河流圖塊調淡（海洋已有深海圖塊，不再調色）。
    /// </summary>
    private static Color WaterTint(DemoHexCell cell, bool hasShallowArt)
        => cell.Shallow && !hasShallowArt ? new Color(1.35f, 1.30f, 1.10f)
            : Colors.White;


    /// <summary>第 06-1 章（TEMP 後備）：淺灘的一排踏石（橫越河道，示意可涉水過河）。</summary>
    private void DrawStones(Vector2 center, float radius)
    {
        var stone = new Color(0.78f, 0.70f, 0.55f);
        var edge = new Color(0.35f, 0.30f, 0.22f);
        for (var i = -2; i <= 2; i++)
        {
            var p = center + new Vector2(i * radius * 0.30f, (i % 2 == 0 ? -1 : 1) * radius * 0.06f);
            DrawCircle(p, radius * 0.10f, edge);
            DrawCircle(p, radius * 0.08f, stone);
        }
    }

    /// <summary>第 08-2 章：遷徙隊伍圖示的中心＝所在六角格的中心（冒煙測試核對）。</summary>
    private static Vector2 MigrantMarkerCenter(Vector2 start, float radius, DemoHexCell cell)
        => start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);

    /// <summary>第 06-2 章【TEMP 顯示】：依本週已過幾成，在本格與路線下一格之間內插（抵達前一格或週初就停在本格）。</summary>
    private Vector2 MigrantPosition(Vector2 start, float radius, DemoMigrationBatch batch)
    {
        var here = MigrantMarkerCenter(start, radius, World!.GetCell(batch.Cell.Column, batch.Cell.Row));
        if (batch.StepIndex + 1 >= batch.Path.Length || WeekFraction <= 0f)
            return here;
        var next = batch.Path[batch.StepIndex + 1];
        var there = MigrantMarkerCenter(start, radius, World.GetCell(next.Column, next.Row));
        return here.Lerp(there, Mathf.Clamp(WeekFraction, 0f, 1f));
    }

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
        DemoTerrain.Hills => ArtAssets.Texture(ArtAssets.TileHills), // 第 06-3 章
        DemoTerrain.Water => ArtAssets.Texture(river ? ArtAssets.TileRiver : ArtAssets.TileWater),
        _ => null
    };

    /// <summary>尖頂六角頂點 → 圖塊 UV（圖塊＝六角形外接矩形：寬 √3·r、高 2r）。</summary>
    /// <summary>TEMP 顯示參數（第 08-13 章）：取樣往內縮的比例（藏住圖塊描邊）、六角形外擴的像素（相鄰格重疊、不留細縫）。</summary>
    public const float TempTileInset = 0.07f;
    public const float TempTileOverlap = 0.9f;

    /// <summary>【TEMP】森林聚落空地的開發度（0～255；約四成：泥土與草地相間、沒有田壟）。</summary>
    private const byte TempSettlementGround = TerrainSurface.SettlementGround;

    private readonly HashSet<DemoCellRef> _builtCells = new();
    private byte[]? _surfaceCodes;
    private ulong _surfaceBuiltMs;
    private bool _surfaceStale;

    /// <summary>【TEMP】地表資料最短重算間隔（毫秒）。</summary>
    private const ulong TempSurfaceRefreshMs = 250;
    private (DemoHexWorld? World, int Week, float Fraction, object? Land, object? Spells, object? Ore, object? Marks, object? Sites, object? Ruins, object? Scale, object? Lineage) _surfaceKey;

    /// <summary>
    /// 第 06-4 章第 2 步：每格四個位元組（列×欄數＋欄）：地表代碼、天災疊層種類、疊層強度、保留。
    /// 地圖、時間、土地、天災、礦脈變了才重算；試玩回饋 2026-11-03 之 3：天災改成依時間漸變的疊層。
    /// </summary>
    private byte[] SurfaceCodes()
    {
        var key = (World, CurrentWeek, WeekFraction, (object?)Land, (object?)Spells, (object?)MeteoriteOre, (object?)_disasterMarks, (object?)Sites, (object?)FadedRuins, (object?)SettlementScale, (object?)Lineage);
        if (_surfaceCodes is not null && key.Equals(_surfaceKey))
            return _surfaceCodes;
        // 試玩回饋 2026-11-04 之 4（遊戲進行時拖地圖很卡）：同一張地圖最多每 TempSurfaceRefreshMs 毫秒重算一次
        // （原本週內進度每幀都變、每幀重算並重新上傳）；換地圖立刻重算。
        var nowMs = Time.GetTicksMsec();
        if (_surfaceCodes is not null && ReferenceEquals(key.World, _surfaceKey.World) && nowMs - _surfaceBuiltMs < TempSurfaceRefreshMs)
        {
            _surfaceStale = true; // 間隔到了由 _Process 補畫一次
            return _surfaceCodes;
        }
        _surfaceStale = false;
        _surfaceBuiltMs = nowMs;
        // 第 08-17 章：聚落版面（廣場、道路、建築地塊）決定哪些格是聚落地；只在版面條件改變時重算。
        EnsureLayouts();
        var data = new byte[World!.Columns * World.Rows * 4];
        // 第 08-16 章第 14 節：伐木與採礦（每次先整理成依格查的表，不必每格掃一遍狀態表）。
        _logged.Clear();
        _mining.Clear();
        foreach (var spell in Spells)
            foreach (var c in spell.Cells)
            {
                if (spell.Kind == DemoSettlementEventKind.Logging && DemoEnvironment.LoggedAt(new[] { spell }, c, CurrentWeek) is var g and >= 0f)
                    _logged[c] = _logged.TryGetValue(c, out var old) ? Math.Min(old, g) : g;
                else if (spell.Kind == DemoSettlementEventKind.MiningBegan)
                    _mining[c] = DemoEnvironment.MiningAt(new[] { spell }, c, CurrentWeek);
            }
        foreach (var cell in World.Cells)
        {
            var at = (cell.Row * World.Columns + cell.Column) * 4;
            data[at] = (byte)SurfaceCode(cell);
            var here = new DemoCellRef(cell.Column, cell.Row);
            // 長回與開採進度分 16 階（每週都變，但只有跨階時那一格才重畫）。
            if (data[at] == (byte)TerrainSurface.Code.Logged)
                data[at + 3] = (byte)(Math.Clamp((int)(_logged[here] * 16f), 0, 15) * 17);
            else if (data[at] == (byte)TerrainSurface.Code.HillsMining)
                data[at + 3] = (byte)(Math.Clamp((int)(_mining[here] * 16f), 0, 15) * 17);
            // 第 08-16 章：開墾中與荒廢中的開發度（第 4 個位元組），地表依此從草地漸變到農田、或從農田漸變回草地。
            if (Land.ParcelAt(new DemoCellRef(cell.Column, cell.Row)) is { State: DemoLandDevelopment.Clearing } parcel)
                data[at + 3] = (byte)Math.Clamp(parcel.DevelopmentPercent * 255 / 100, 0, 255);
            else if (data[at] == (byte)TerrainSurface.Code.Clearing)
                data[at + 3] = TempSettlementGround; // 森林裡的聚落：砍出的空地（泥土夾草）
        }
        FillOverlays(data);
        _surfaceKey = key;
        return _surfaceCodes = data;
    }

    private readonly Dictionary<DemoCellRef, float> _logged = new();
    private readonly Dictionary<DemoCellRef, float> _mining = new();

    private TerrainSurface.Code SurfaceCode(DemoHexCell cell)
    {
        var at = new DemoCellRef(cell.Column, cell.Row);
        // 熱浪、野火不再直接換地表，改由天災疊層依時間漸變（DemoHexMap.Disasters.cs）。
        switch (Land.StateOf(at))
        {
            case DemoLandDevelopment.Farmland: return TerrainSurface.Code.Farmland;
            case DemoLandDevelopment.Clearing: return TerrainSurface.Code.Clearing;
        }
        // 試玩回饋 2026-11-04：森林裡的聚落像蓋在樹上——有房子的格（含村莊蓋到相鄰格的房子）把樹砍出一片空地。
        // 試玩回饋 2026-11-05 之 8、13：草原上的聚落也一樣有泥土空地（人類聚落不再被草地吃掉）。
        if (cell.Terrain is DemoTerrain.Forest or DemoTerrain.Grassland && _builtCells.Contains(at))
            return TerrainSurface.Code.Clearing;
        return cell.Terrain switch
        {
            DemoTerrain.Water when cell.Sea => TerrainSurface.Code.Sea,
            DemoTerrain.Water when cell.Shallow => TerrainSurface.Code.Shallow,
            DemoTerrain.Water => TerrainSurface.Code.River,
            DemoTerrain.Forest => _logged.ContainsKey(at) ? TerrainSurface.Code.Logged : TerrainSurface.Code.Forest,
            DemoTerrain.Hills when _mining.TryGetValue(at, out var mined) => mined > 1f ? TerrainSurface.Code.MinePit : TerrainSurface.Code.HillsMining,
            DemoTerrain.Hills => cell.Ore || MeteoriteOre.Contains(at) ? TerrainSurface.Code.HillsOre : TerrainSurface.Code.Hills,
            _ => TerrainSurface.Code.Grassland
        };
    }

    private static Vector2[] HexUvs(Vector2[] vertices, Vector2 center, float radius)
    {
        var halfWidth = Mathf.Sqrt(3f) * 0.5f * radius;
        var uvs = new Vector2[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var u = (vertices[i].X - center.X + halfWidth) / (2f * halfWidth);
            var v = (vertices[i].Y - center.Y + radius) / (2f * radius);
            // 往中心縮 TempTileInset：外圈的描邊不會畫出來。
            uvs[i] = new Vector2(0.5f + (u - 0.5f) * (1f - TempTileInset), 0.5f + (v - 0.5f) * (1f - TempTileInset));
        }
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
    /// <summary>
    /// 第 07 章 c 節：遺跡圖示；第 08-11 章：依居住地等級換圖（營地、村莊、城鎮、城市、首都）。
    /// 該等級的圖還沒交付時退回舊圖（營地＝新聚落圖、村莊以上＝村莊圖）。
    /// </summary>
    private string SettlementIcon(DemoSettlement settlement)
    {
        if (settlement.IsRuin)
            return ArtAssets.IconRuin;
        var tier = SettlementScale.TryGetValue(settlement.Id, out var scale) ? scale.Tier : DemoSettlementTier.Camp;
        var path = ArtAssets.SettlementTierIcon(tier);
        if (ArtAssets.Texture(path) is not null)
            return path;
        return tier == DemoSettlementTier.Camp ? ArtAssets.IconNewSettlement : ArtAssets.IconVillage;
    }

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
                TempMinZoom, Mathf.Max(TempMaxZoom, TempMaxHexRadius / Layout().Radius));
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

        if (CellAt(button.Position) is { } hit)
        {
            // 第 08-5 章（使用者 2026-10-23）：點格查看詳情時，地圖上的這一格以框標示。
            Highlight = (hit.Column, hit.Row);
            QueueRedraw();
            CellSelected?.Invoke(hit);
            AcceptEvent();
        }
    }

    /// <summary>畫面位置所在的格（格與格之間的縫隙不算）；不在任何格上回傳 null。</summary>
    private DemoHexCell? CellAt(Vector2 position)
    {
        if (World is null)
            return null;
        // 點選與畫面一致：畫了像素地表時，依滑鼠下那個像素屬於哪一格。
        if (_surface?.CellAtScreen(position) is int owner && owner >= 0)
            return World.GetCell(owner % World.Columns, owner / World.Columns);
        var (start, radius) = ViewLayout();
        foreach (var cell in World.Cells)
        {
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            if (center.DistanceSquaredTo(position) > radius * radius)
                continue; // 外接圓之外不可能在格內
            if (Geometry2D.IsPointInPolygon(position, HexCoordinates.Polygon(center, Mathf.Max(0.1f, radius - 1.4f))))
                return cell;
        }
        return null;
    }

    /// <summary>
    /// 第 08-5 章（使用者 2026-10-23）：滑鼠停在格上只說明這一格是什麼（森林、大海、某某村莊…）。
    /// </summary>
    public override string _GetTooltip(Vector2 atPosition)
    {
        if (World is null || CellAt(atPosition) is not { } cell)
            return "";
        var name = DemoCellQuery.ShortName(World, VisibleSite(cell) is null ? null : Sites, cell);
        // 第 08-15 章：色塊層（戰略、區域）再說明這一格的顏色代表什麼。
        var status = DisasterStatus(cell.Column, cell.Row);
        if (status.Length > 0)
            name += "\n" + status;
        return LayerFor(ViewLayout().Radius) == ViewLayer.Local ? name : $"{name}\n{ColorMeaning(cell)}";
    }

    /// <summary>試玩回饋 2026-11-05 之 2：這一格目前的天災狀況（依地表疊層；沒有回傳空字串）。</summary>
    public string DisasterStatus(int column, int row)
    {
        if (World is null || !World.Contains(column, row))
            return "";
        var data = _surfaceCodes ?? SurfaceCodes();
        var i = (row * World.Columns + column) * 4;
        var amount = data[i + 2] / 255f;
        var fading = amount < 0.5f;
        var kind = (Overlay)data[i + 1];
        // 有素材的坑、裂縫、殘骸改畫成貼圖、不在資料裡，另外查。
        foreach (var (c, k, _) in _decals)
            if (c.Column == column && c.Row == row)
                kind = k;
        // 第 08-16 章第 14 節：伐木、採礦的狀況（沒有天災時才顯示）。
        if (kind == Overlay.None)
            return (TerrainSurface.Code)data[i] switch
            {
                TerrainSurface.Code.Logged => data[i + 3] < 128 ? "🪓 森林剛被砍伐，只剩樹樁與幼苗" : "🪓 砍過的森林，樹木正慢慢長回",
                TerrainSurface.Code.HillsMining => "⛏ 礦脈開採中",
                TerrainSurface.Code.MinePit => "⛏ 礦脈已挖空，只剩廢棄的礦坑",
                _ => ""
            };
        return kind switch
        {
            Overlay.Fire => "🔥 野火正在燃燒，草木與屋舍陷入火海",
            Overlay.Scorch => fading ? "野火燒過的焦土，草木正慢慢長回" : "野火剛燒過：一片焦土，草木盡毀",
            Overlay.Dry => fading ? "熱浪漸退，草木開始返青" : "熱浪：草木枯黃、水源減少",
            Overlay.Flood => fading ? "洪水退去中，田地泥濘" : "洪水淹沒了這一帶",
            Overlay.Debris => "龍捲風過境，四處散落殘骸",
            Overlay.Cracks => "地震撕開的裂縫",
            Overlay.Crater => "隕石墜落留下的大坑",
            Overlay.Ore => "隕石帶來的礦石",
            _ => ""
        };
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
        // 第 08-13 章第 5 節：換了世界就重設鏡頭；全圖塞進畫面時每格太小（大地圖），先拉近到看得清楚的大小（地圖中央）。
        if (!ReferenceEquals(_viewWorld, World) && World is not null && Size.X > 0f && Size.Y > 0f)
        {
            _viewWorld = World;
            _zoom = baseRadius < TempComfortHexRadius ? TempComfortHexRadius / baseRadius : 1f;
            _pan = Vector2.Zero;
        }
        var viewportCenter = Size * 0.5f;
        return (viewportCenter + (baseStart - viewportCenter) * _zoom + _pan,
            baseRadius * _zoom);
    }

    private bool OnScreen(Vector2 center, float radius)
        => center.X > -radius * 2f && center.Y > -radius * 2f && center.X < Size.X + radius * 2f && center.Y < Size.Y + radius * 2f;

    private (Vector2 Start, float Radius) Layout()
    {
        const float padding = 28f;
        var sqrtThree = Mathf.Sqrt(3f);
        var columns = World?.Columns ?? DemoHexWorld.SmallColumns;
        var rows = World?.Rows ?? DemoHexWorld.SmallRows;
        var widthUnits = sqrtThree * (columns + 0.5f);
        var heightUnits = 1.5f * (rows - 1) + 2f;
        var radius = Mathf.Max(4f, Mathf.Min(
            (Size.X - 2f * padding) / widthUnits,
            (Size.Y - 2f * padding) / heightUnits));

        var start = new Vector2(
            (Size.X - widthUnits * radius) / 2f + sqrtThree * radius / 2f,
            (Size.Y - heightUnits * radius) / 2f + radius);
        return (start, radius);
    }
}
