using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-15 章：視角分層（使用者 2026-11-01：巨大地圖移動卡頓；遠看只用顏色區分地形與勢力，放大才逐步顯示細節）。
/// 依每格在畫面上的半徑分三層——
/// 戰略層（&lt;6 像素）：整張地圖預先畫成一張圖（每格一個顏色：地形色，聚落領域疊家系色；其他地圖模式疊該模式顏色），
///   平移、縮放只是搬動這張圖；世界每週變化或切換模式時才重畫（最多每 0.25 秒一次）。聚落畫成小點。
/// 區域層（6～14 像素）：只畫畫面內的格，純色（不貼圖塊、不畫建築與格線），聚落用圖示。
/// 近景層（≥14 像素）：原本的完整畫法（圖塊、建築、城牆、格線）。
/// 只影響顯示，不改世界狀態。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】戰略層的格半徑上限（像素）、戰略圖每格的取樣半徑、重畫的最短間隔（毫秒）。</summary>
    /// <summary>第 09-1d 章（參考 CK3 最遠一層的紙地圖）：6 → 12 像素以下畫紙地圖；紙地圖每格取樣半徑 4 → 8（放大時不糊）。</summary>
    private const float TempStrategicMaxRadius = 12f;
    private const float TempStrategicImageRadius = 8f;
    private const ulong TempStrategicRebuildMs = 250;

    public enum ViewLayer { Strategic, Regional, Local }

    /// <summary>目前縮放對應的視角層（冒煙測試與工具列讀取）。</summary>
    public ViewLayer CurrentLayer => LayerFor(ViewLayout().Radius);

    private ViewLayer LayerFor(float radius)
        => radius < TempStrategicMaxRadius ? ViewLayer.Strategic
            : radius < TempFootprintMinRadius || _schematic ? ViewLayer.Regional // 第 05-1 章：色塊檢視拉近也用區域層的畫法
            : ViewLayer.Local;

    private bool _schematic;

    /// <summary>
    /// 第 05-1 章：色塊檢視（使用者 2026-11-17：用色塊拼接顯示的世界，看底層規則的運作）。
    /// 開著時不畫連續地表、建築與城牆——不論遠近，每格一個顏色（地形色，聚落領域疊色；其他地圖模式照該模式的顏色），
    /// 人用棋子表示（大小＝人數），勢力、戰爭與天災的標記照常。只影響顯示。
    /// </summary>
    public bool Schematic
    {
        get => _schematic;
        set
        {
            if (_schematic == value)
                return;
            _schematic = value;
            QueueRedraw();
        }
    }

    // 每格的平面顏色（依格的索引）與戰略圖；鍵變了才重算。
    private Color[]? _flatColors;
    private (DemoHexWorld? World, Mode Mode, int Week, DemoWorldSites? Sites, object? Lineage, object? Land, object? Food, object? Culture, object? Faith, object? Knowledge, object? Territory) _flatKey;
    private ImageTexture? _strategicTexture;
    private Color[]? _strategicColors;
    private (DemoHexWorld? World, Mode Mode, int? Focus) _strategicStructure;
    private ulong _strategicBuiltAt;

    /// <summary>第 08-15 章：文化、信仰的名稱（色塊層的滑鼠提示用；ObserverRoot 刷新時注入）。</summary>
    public IReadOnlyDictionary<int, string> CultureNames { get; set; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<int, string> FaithNames { get; set; } = new Dictionary<int, string>();

    /// <summary>色塊層的滑鼠提示：這一格的顏色代表什麼（依地圖模式）。</summary>
    public string ColorMeaning(DemoHexCell cell)
    {
        var at = new DemoCellRef(cell.Column, cell.Row);
        if (_mode is Mode.Realm or Mode.Terrain)
            return RealmMeaning(at); // 第 08-19 章
        string SiteName(int id) => Sites?.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        if (!Lineage.TryGetValue(at, out var owner))
            return _mode == Mode.Food ? "糧食：沒有聚落" : "無人之地";
        var site = SiteName(owner.SettlementId);
        return _mode switch
        {
            Mode.Food => Sites?.SettlementAt(cell.Column, cell.Row) is { IsRuin: false } here
                ? "糧食：" + (FoodStatuses.TryGetValue(here.Id, out var s) ? s : FoodStatus.Unsettled) switch
                {
                    FoodStatus.Ample => "充足",
                    FoodStatus.Tight => "緊張",
                    FoodStatus.Starving => "飢餓",
                    _ => "尚未結算"
                } + $"（{here.Name}）"
                : $"{site}的領域（糧食模式只標聚落格）",
            Mode.Culture => $"文化：{CultureNames.GetValueOrDefault(CultureOf.GetValueOrDefault(owner.SettlementId), "未定")}（{site}）",
            Mode.Faith => FaithOf.GetValueOrDefault(owner.SettlementId) is var f and > 0
                ? $"信仰：{FaithNames.GetValueOrDefault(f, $"#{f}")}（{site}）" + (HolySites.Contains(owner.SettlementId) ? "｜聖地" : "")
                : $"無信仰（{site}）",
            Mode.Knowledge => (KnowledgeDomain.GetValueOrDefault(owner.SettlementId) is { } d ? $"擅長：{DemoPlayerDetail.DomainName(d)}，" : "")
                + $"已習得 {KnowledgeCounts.GetValueOrDefault(owner.SettlementId)} 項技術（{site}）",
            _ => $"{SiteName(owner.LineageRootId)}一系的領域（屬於 {site}）"
        };
    }

    /// <summary>每格的平面顏色（地形＋模式疊色）；世界資料換了才重算（每週一次左右）。</summary>
    private Color[] FlatColors()
    {
        var key = (World, _mode, CurrentWeek, Sites, (object?)Lineage, (object?)Land, (object?)FoodStatuses, (object?)CultureOf, (object?)FaithOf, (object?)KnowledgeCounts, (object?)Territory);
        if (_flatColors is not null && key.Equals(_flatKey) && _flatFocus == FocusGroup)
            return _flatColors;
        var cells = World!.Cells;
        var colors = new Color[cells.Count];
        for (var i = 0; i < cells.Count; i++)
            colors[i] = FlatColor(cells[i]);
        _flatColors = colors;
        _flatKey = key;
        _flatFocus = FocusGroup;
        return colors;
    }

    /// <summary>
    /// 第 08-18i 章（GPT 試玩回饋 06：點圖例某一項時其他淡化）：目前模式下要突顯的那一群（勢力＝最上層聚落、家系＝源頭聚落、文化、信仰的編號；null＝不突顯）。
    /// </summary>
    public int? FocusGroup
    {
        get => _focusGroup;
        set
        {
            if (_focusGroup == value)
                return;
            _focusGroup = value;
            QueueRedraw();
        }
    }

    private int? _focusGroup;
    private int? _flatFocus;

    /// <summary>這一格在目前模式下屬於哪一群（沒有＝null）。</summary>
    private int? GroupOf(DemoCellRef at)
    {
        var owned = Lineage.TryGetValue(at, out var owner);
        return _mode switch
        {
            Mode.Realm => Territory.Cells.TryGetValue(at, out var held) ? held.RealmId : null,
            Mode.Lineage when owned => owner!.LineageRootId,
            Mode.Culture when owned && CultureOf.TryGetValue(owner!.SettlementId, out var c) => c,
            Mode.Faith when owned && FaithOf.TryGetValue(owner!.SettlementId, out var f) => f,
            _ => null
        };
    }

    private Color FlatColor(DemoHexCell cell)
    {
        var color = FlatColorCore(cell);
        if (FocusGroup is { } focus && GroupOf(new DemoCellRef(cell.Column, cell.Row)) != focus)
            color = color.Darkened(0.55f).Lerp(new Color(0.35f, 0.35f, 0.35f), 0.4f); // 其他群淡化
        return color;
    }

    private Color FlatColorCore(DemoHexCell cell)
    {
        var at = new DemoCellRef(cell.Column, cell.Row);
        var land = Land.StateOf(at);
        var baseColor = cell.Terrain switch
        {
            DemoTerrain.Water => cell.Sea ? new Color(0.10f, 0.22f, 0.40f) : new Color(0.16f, 0.45f, 0.62f),
            DemoTerrain.Forest => new Color(0.17f, 0.38f, 0.28f),
            DemoTerrain.Grassland => new Color(0.41f, 0.56f, 0.34f),
            DemoTerrain.Hills => new Color(0.52f, 0.47f, 0.33f),
            _ => new Color(0.4f, 0.4f, 0.4f)
        };
        if (land == DemoLandDevelopment.Farmland)
            baseColor = new Color(0.72f, 0.62f, 0.30f);
        baseColor = DemoEnvironment.LookAt(Spells, cell, CurrentWeek) switch
        {
            DemoEnvironment.Look.Burning => new Color(0.85f, 0.35f, 0.12f),
            DemoEnvironment.Look.Scorched => new Color(0.25f, 0.20f, 0.16f),
            DemoEnvironment.Look.Dry => new Color(0.78f, 0.68f, 0.40f),
            _ => baseColor
        };
        var owned = Lineage.TryGetValue(at, out var owner);
        var realm = Territory.Cells.TryGetValue(at, out var held) ? held : null;
        return _mode switch
        {
            Mode.Terrain => baseColor, // 第 08-18k 章：地形模式不罩勢力色
            Mode.Realm => realm is not null ? baseColor.Darkened(0.3f).Lerp(RealmColor(realm.RealmId), 0.7f) : baseColor.Darkened(0.45f),
            Mode.Food => Sites?.SettlementAt(cell.Column, cell.Row) is { IsRuin: false } site
                ? FoodStatusColor(FoodStatuses.TryGetValue(site.Id, out var s) ? s : FoodStatus.Unsettled)
                : baseColor.Darkened(0.45f),
            _ when !owned => baseColor.Darkened(0.45f),
            Mode.Lineage => baseColor.Darkened(0.3f).Lerp(LineageColor(owner!.LineageRootId), 0.7f),
            Mode.Culture => baseColor.Darkened(0.3f).Lerp(CultureColor(CultureOf.GetValueOrDefault(owner!.SettlementId)), 0.7f),
            Mode.Faith => baseColor.Darkened(0.3f).Lerp(FaithColor(FaithOf.GetValueOrDefault(owner!.SettlementId)), 0.7f),
            Mode.Knowledge => baseColor.Darkened(0.3f).Lerp(KnowledgeTint(owner!.SettlementId), 0.7f),
            _ => baseColor
        };
    }

    /// <summary>戰略圖的左上角相對於 (0,0) 格中心的位移與大小（以取樣半徑為單位，同 <see cref="Layout"/> 的外框）。</summary>
    private (Vector2 Offset, Vector2 Size) StrategicFrame(float radius) => HexCoordinates.Frame(World!.Columns, World.Rows, radius);

    private ImageTexture StrategicTexture()
    {
        var colors = FlatColors();
        var now = Time.GetTicksMsec();
        var structure = (World, _mode, FocusGroup); // 第 08-18i 章：突顯的群換了立刻重畫
        // 換世界或模式立刻重畫；同一世界的每週變化最多每 TempStrategicRebuildMs 重畫一次（快轉時不每幀重畫）。
        if (_strategicTexture is not null && ReferenceEquals(_strategicColors, colors))
            return _strategicTexture;
        if (_strategicTexture is not null && structure.Equals(_strategicStructure) && now - _strategicBuiltAt < TempStrategicRebuildMs)
        {
            _animatedOnScreen = true; // 第 09-1e 章：這次先沿用舊圖；間隔到了要補畫一次（原本靠遷徙隊伍的持續重畫順便補上）
            return _strategicTexture;
        }
        var (width, height, index) = PaperIndex();
        var bytes = PaperBytes(width, height, index, colors);
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, bytes);
        if (_strategicTexture is not null && _strategicTexture.GetWidth() == width && _strategicTexture.GetHeight() == height)
            _strategicTexture.Update(image);
        else
        {
            _strategicTexture?.Dispose();
            _strategicTexture = ImageTexture.CreateFromImage(image);
        }
        image.Dispose();
        _strategicColors = colors;
        _strategicStructure = structure;
        _strategicBuiltAt = now;
        return _strategicTexture;
    }

    /// <summary>戰略層與區域層的畫法（近景層用原本的完整畫法）。</summary>
    private void DrawSimplified(Vector2 start, float radius, ViewLayer layer)
    {
        if (layer == ViewLayer.Strategic)
        {
            var texture = StrategicTexture();
            var (offset, size) = StrategicFrame(radius);
            DrawTextureRect(texture, new Rect2(start + offset, size), false);
        }
        else if (_surface is not { Active: true }) // 第 06-4 章第 2 步：地形模式由連續地表畫
        {
            var colors = FlatColors();
            for (var i = 0; i < World!.Cells.Count; i++)
            {
                var cell = World.Cells[i];
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                if (OnScreen(center, radius))
                    DrawShape(center, ScreenShape(cell, center, radius + 0.6f), colors[i]); // 第 08-15 章第 3 節：不規則邊界
            }
        }
        if (Sites is null)
            return;
        foreach (var site in Sites.Settlements)
        {
            if (FadedRuins.Contains(site.Id) || (layer == ViewLayer.Strategic && site.IsRuin))
                continue;
            // 第 08-17 章第 5 節：有人的聚落改畫成居民棋（大小＝規模），在下面 DrawTokens 一起畫；遺跡照舊畫圖示。
            if (!site.IsRuin && SettlementScale.TryGetValue(site.Id, out var people) && people.Item1 > 0)
            {
                if (layer == ViewLayer.Regional)
                {
                    var c = World!.GetCell(site.Column, site.Row);
                    var at = start + HexCoordinates.Center(c.AxialQ, c.AxialR, radius);
                    if (ShowDisasterMarks && OnScreen(at, radius * 2f) && RecentDisasters.Contains(site.Id))
                        DrawMarker(ArtAssets.IconDisaster, at + new Vector2(radius * 0.7f, -radius * 0.7f), radius * 0.8f);
                    if (OnScreen(at, radius * 2f) && _mode == Mode.Faith && HolySites.Contains(site.Id))
                        DrawMarker(ArtAssets.IconHolySite, at + new Vector2(-radius * 0.7f, -radius * 0.7f), radius * 0.9f);
                }
                continue;
            }
            var cell = World!.GetCell(site.Column, site.Row);
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            if (!OnScreen(center, radius * 2f))
                continue;
            if (layer == ViewLayer.Strategic)
            {
                // 戰略層：聚落是小點，等級越高越大（城鎮以上加白框）。
                var tier = SettlementScale.TryGetValue(site.Id, out var scale) ? scale.Tier : DemoSettlementTier.Camp;
                var dot = Math.Max(1.5f, radius * (0.45f + 0.15f * (int)tier));
                DrawCircle(center, dot + 1f, new Color(0.08f, 0.07f, 0.06f));
                DrawCircle(center, dot, tier >= DemoSettlementTier.Town ? Colors.White : new Color(0.97f, 0.80f, 0.40f));
                continue;
            }
            DrawMarker(SettlementIcon(site), center, IconSize(site, radius));
            if (ShowDisasterMarks && RecentDisasters.Contains(site.Id))
                DrawMarker(ArtAssets.IconDisaster, center + new Vector2(radius * 0.5f, -radius * 0.5f), radius * 0.8f);
            if (_mode == Mode.Faith && HolySites.Contains(site.Id))
                DrawMarker(ArtAssets.IconHolySite, center + new Vector2(-radius * 0.5f, -radius * 0.5f), radius * 0.9f);
        }
        // 第 08-19 章：勢力邊界——區域層在地形模式畫邊界加淡色、勢力模式整片著色（戰略層已烤在色塊圖裡，只補邊界）。
        // 第 08-18k 章（試玩回饋 2026-11-11 之 10）：地形模式不畫勢力框，只有勢力模式畫。
        if (_mode == Mode.Realm)
            DrawRealms(start, radius, layer == ViewLayer.Regional ? 0.45f : 0f, inner: false);
        if (_mode == Mode.Realm)
            DrawMonsterRealms(start, radius, fill: true); // 第 12-2 章：怪物的勢力範圍
        DrawTokens(start, radius, layer); // 第 08-17 章第 5 節
        // 試玩回饋 2026-11-07 之 4：遠看只標各勢力最上層的名字（區域層村莊以上、戰略層城鎮以上）。
        if (_mode is Mode.Terrain or Mode.Realm)
            DrawNameplates(start, radius, layer == ViewLayer.Regional ? DemoSettlementTier.Village : DemoSettlementTier.Town, realmsOnly: true);
        else if (layer == ViewLayer.Regional)
            DrawNameplates(start, radius, DemoSettlementTier.Town, realmsOnly: true); // 第 08-18k 章：勢力合併後只標首都
    }

    // ───────────── 第 09-1d 章：紙地圖（參考 CK3 最遠一層） ─────────────

    private (DemoHexWorld? World, int W, int H, int[] Index)? _paperIndex;

    /// <summary>紙地圖每個像素屬於哪一格（-1＝地圖外）；只在換世界時算一次，之後每週重畫只查表。</summary>
    private (int W, int H, int[] Index) PaperIndex()
    {
        if (_paperIndex is { } cached && ReferenceEquals(cached.World, World))
            return (cached.W, cached.H, cached.Index);
        var r = TempStrategicImageRadius;
        var (offset, size) = StrategicFrame(r);
        var width = Math.Max(1, (int)Mathf.Ceil(size.X));
        var height = Math.Max(1, (int)Mathf.Ceil(size.Y));
        var index = new int[width * height];
        System.Threading.Tasks.Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                // 第 08-18b 章：一格一塊方形色塊（像素中心 → 欄列，不扭曲）。
                var at = HexCoordinates.CellOf(new Vector2(x + 0.5f + offset.X, y + 0.5f + offset.Y), r);
                index[y * width + x] = World!.Contains(at.X, at.Y) ? at.Y * World.Columns + at.X : -1;
            }
        });
        _paperIndex = (World, width, height, index);
        return (width, height, index);
    }

    // 紙、墨、海的顏色（【TEMP】第 09-1d 章調色）。
    private static readonly Color TempPaper = new(0.88f, 0.81f, 0.65f), TempPaperForest = new(0.74f, 0.76f, 0.56f),
        TempPaperHills = new(0.80f, 0.70f, 0.52f), TempPaperFarm = new(0.90f, 0.78f, 0.50f), TempPaperSea = new(0.56f, 0.66f, 0.64f),
        TempPaperLake = new(0.50f, 0.64f, 0.68f), TempInk = new(0.28f, 0.21f, 0.15f);

    /// <summary>
    /// 紙地圖：陸地是羊皮紙色（森林偏綠、丘陵偏褐、田偏黃），海是灰藍；海岸線與勢力邊界用深褐墨線，勢力模式整片淡淡上色；
    /// 其他地圖模式照原本的色塊、一樣描海岸線。紙面有一點固定的顆粒。
    /// </summary>
    private byte[] PaperBytes(int width, int height, int[] index, Color[] colors)
    {
        var cells = World!.Cells;
        var paper = new Color[cells.Count];
        var realmOf = new int[cells.Count];
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var at = new DemoCellRef(cell.Column, cell.Row);
            realmOf[i] = Territory.Cells.TryGetValue(at, out var held) ? held.RealmId : -1;
            if (_mode is not (Mode.Terrain or Mode.Realm))
            {
                paper[i] = colors[i];
                continue;
            }
            var c = cell.Terrain switch
            {
                DemoTerrain.Water => cell.Sea ? TempPaperSea : TempPaperLake,
                DemoTerrain.Forest => TempPaperForest,
                DemoTerrain.Hills => TempPaperHills,
                _ => Land.StateOf(at) == DemoLandDevelopment.Farmland ? TempPaperFarm : TempPaper
            };
            if (_mode == Mode.Realm && realmOf[i] >= 0 && cell.Terrain != DemoTerrain.Water)
                c = c.Lerp(RealmColor(realmOf[i]), 0.38f);
            if (FocusGroup is { } focus && GroupOf(at) != focus)
                c = c.Darkened(0.35f);
            paper[i] = c;
        }
        var bytes = new byte[width * height * 4];
        var realm = _mode == Mode.Realm;
        System.Threading.Tasks.Parallel.For(0, height, y =>
        {
            for (var x = 0; x < width; x++)
            {
                var k = index[y * width + x];
                if (k < 0)
                    continue;
                var c = paper[k];
                // 墨線：右邊或下面的像素換了水陸（海岸線），或勢力模式換了勢力（邊界）。
                var water = cells[k].Terrain == DemoTerrain.Water;
                for (var side = 0; side < 2; side++)
                {
                    var n = side == 0 ? (x + 1 < width ? index[y * width + x + 1] : -1) : (y + 1 < height ? index[(y + 1) * width + x] : -1);
                    if (n < 0 || n == k)
                        continue;
                    if ((cells[n].Terrain == DemoTerrain.Water) != water)
                        c = TempInk;
                    else if (realm && !water && realmOf[n] != realmOf[k])
                        c = c.Lerp(TempInk, 0.7f);
                }
                // 紙的顆粒（固定雜湊，±4%）。
                var grain = 1f + ((((uint)(x * 73856093) ^ (uint)(y * 19349663)) % 1000u) / 1000f - 0.5f) * 0.08f;
                var o = (y * width + x) * 4;
                bytes[o] = (byte)(Mathf.Clamp(c.R * grain, 0f, 1f) * 255f);
                bytes[o + 1] = (byte)(Mathf.Clamp(c.G * grain, 0f, 1f) * 255f);
                bytes[o + 2] = (byte)(Mathf.Clamp(c.B * grain, 0f, 1f) * 255f);
                bytes[o + 3] = 255;
            }
        });
        return bytes;
    }
}
