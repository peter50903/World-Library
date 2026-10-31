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

    private static readonly Color[] White6 = Enumerable.Repeat(Colors.White, 6).ToArray();

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
            var shallowArt = cell.Shallow ? ArtAssets.Texture(ArtAssets.TileRiverShallow) : null;
            // 第 08-13 章：熱浪、野火的地形外觀（有素材換圖，沒有就在原圖塊上染色）。
            var look = DemoEnvironment.LookAt(Spells, cell, CurrentWeek);
            var (lookTile, lookTint) = LookArt(look);
            if ((lookTile ?? LandTexture(land) ?? shallowArt ?? TerrainTexture(cell.Terrain, isRiver)) is { } tile)
            {
                // 第 08-13 章（試玩回饋三之 2）：圖塊各自帶深色描邊，相鄰格看得出接縫——
                // 取樣時往內縮掉描邊（TempTileInset），並把六角形畫大一點點讓相鄰格重疊，格線關閉時地形連成一片。
                var full = HexCoordinates.Polygon(center, radius + TempTileOverlap);
                DrawPolygon(full, Tint6(lookTile is null && look != DemoEnvironment.Look.None ? lookTint : WaterTint(cell, shallowArt is not null)),
                    HexUvs(full, center, radius), tile);
                // 第 06-3 章：礦脈疊圖（農田、開墾中不疊）。
                if ((cell.Ore || MeteoriteOre.Contains(new DemoCellRef(cell.Column, cell.Row))) && LandTexture(land) is null && ArtAssets.Texture(ArtAssets.OverlayOreVein) is { } ore)
                    DrawPolygon(full, Tint6(Colors.White), HexUvs(full, center, radius), ore);
                if (cell.Shallow && shallowArt is null)
                    DrawStones(center, radius);
                if (VisibleSite(cell) is { } placed && _mode is not (Mode.Lineage or Mode.Knowledge or Mode.Culture or Mode.Faith))
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
            DrawColoredPolygon(vertices, terrainColor);
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
            else if (VisibleSite(cell) is { } settlement)
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

        // 第 05 章 b 節：糧食狀況模式——地形壓暗，聚落格塗上狀況色並加粗外框。
        if (_mode == Mode.Food && Sites is not null)
        {
            foreach (var cell in World.Cells)
            {
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                var hex = HexCoordinates.Polygon(center, radius - 1.4f);
                if (VisibleSite(cell) is { IsRuin: true } ruinSite)
                {
                    DrawColoredPolygon(hex, new Color(0f, 0f, 0f, 0.45f));
                    DrawMarker(SettlementIcon(ruinSite), center, radius * 1.4f);
                }
                else if (VisibleSite(cell) is { } site)
                {
                    var status = FoodStatuses.TryGetValue(site.Id, out var known) ? known : FoodStatus.Unsettled;
                    var color = FoodStatusColor(status);
                    DrawColoredPolygon(hex, new Color(color, 0.55f));
                    DrawPolyline(Closed(hex), color, 3.5f);
                    DrawMarker(SettlementIcon(site), center, IconSize(site, radius));
                }
                else
                {
                    DrawColoredPolygon(hex, new Color(0f, 0f, 0f, 0.45f));
                }
            }
        }

        // 第 08-10a 章 V2：家系模式——地形壓暗，聚落與採集範圍塗家系色，家系之間畫合併後的邊界（參考 WorldBox／CK3 分布圖層）。
        if (_mode == Mode.Lineage && Sites is not null)
            DrawTerritory(start, radius, owner => LineageColor(owner.LineageRootId), owner => owner.LineageRootId);
        // 第 08-8 章 B1：文化模式——同樣的領域，依聚落的主要文化著色。
        if (_mode == Mode.Culture && Sites is not null)
            DrawTerritory(start, radius, owner => CultureColor(CultureOf.GetValueOrDefault(owner.SettlementId)),
                owner => CultureOf.GetValueOrDefault(owner.SettlementId));
        // 第 08-8 章 B3：信仰模式——同樣的領域，依主要信仰著色；聖地（創立聚落）另畫標記。
        if (_mode == Mode.Faith && Sites is not null)
        {
            DrawTerritory(start, radius, owner => FaithColor(FaithOf.GetValueOrDefault(owner.SettlementId)),
                owner => FaithOf.GetValueOrDefault(owner.SettlementId));
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
                DrawPolyline(Closed(HexCoordinates.Polygon(start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius), radius)),
                    gridColor, Math.Max(1.2f, radius * TempGridWidthPerRadius));
        }

        // #89: outline the settlement of the selected event on top of the terrain and markers.
        if (Highlight is { } focus && World.Contains(focus.Column, focus.Row))
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
            var center = MigrantPosition(start, radius, batch);
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

    /// <summary>
    /// 第 08-10a 章：領域著色——地形壓暗，聚落與採集範圍（Core <see cref="DemoLineageMap"/>）依 <paramref name="color"/> 上色；
    /// 相鄰格 <paramref name="group"/> 不同（或沒有歸屬、在地圖外）時才畫邊界，同組連成一片。
    /// </summary>
    private void DrawTerritory(Vector2 start, float radius, Func<DemoLineageCell, Color> color, Func<DemoLineageCell, int> group)
    {
        foreach (var cell in World!.Cells)
        {
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            var hex = HexCoordinates.Polygon(center, radius);
            if (!Lineage.TryGetValue(new DemoCellRef(cell.Column, cell.Row), out var owner))
            {
                DrawColoredPolygon(hex, new Color(0f, 0f, 0f, 0.45f));
                continue;
            }
            var fill = color(owner);
            DrawColoredPolygon(hex, new Color(fill, 0.55f));
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
                DrawLine(hex[edge], hex[(edge + 1) % 6], fill.Darkened(0.35f), 3f);
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

    private static Color[] Tint6(Color color) => color == Colors.White ? White6 : Enumerable.Repeat(color, 6).ToArray();

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
        var (start, radius) = ViewLayout();
        foreach (var cell in World.Cells)
        {
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            if (Geometry2D.IsPointInPolygon(position, HexCoordinates.Polygon(center, Mathf.Max(0.1f, radius - 1.4f))))
                return cell;
        }
        return null;
    }

    /// <summary>
    /// 第 08-5 章（使用者 2026-10-23）：滑鼠停在格上只說明這一格是什麼（森林、大海、某某村莊…）。
    /// </summary>
    public override string _GetTooltip(Vector2 atPosition)
        => World is not null && CellAt(atPosition) is { } cell ? DemoCellQuery.ShortName(World, VisibleSite(cell) is null ? null : Sites, cell) : "";

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
