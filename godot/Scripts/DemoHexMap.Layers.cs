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
    private const float TempStrategicMaxRadius = 6f;
    private const float TempStrategicImageRadius = 4f;
    private const ulong TempStrategicRebuildMs = 250;

    public enum ViewLayer { Strategic, Regional, Local }

    /// <summary>目前縮放對應的視角層（冒煙測試與工具列讀取）。</summary>
    public ViewLayer CurrentLayer => LayerFor(ViewLayout().Radius);

    private static ViewLayer LayerFor(float radius)
        => radius < TempStrategicMaxRadius ? ViewLayer.Strategic : radius < TempFootprintMinRadius ? ViewLayer.Regional : ViewLayer.Local;

    // 每格的平面顏色（依格的索引）與戰略圖；鍵變了才重算。
    private Color[]? _flatColors;
    private (DemoHexWorld? World, Mode Mode, int Week, DemoWorldSites? Sites, object? Lineage, object? Land, object? Food, object? Culture, object? Faith, object? Knowledge) _flatKey;
    private ImageTexture? _strategicTexture;
    private Color[]? _strategicColors;
    private (DemoHexWorld? World, Mode Mode) _strategicStructure;
    private ulong _strategicBuiltAt;

    /// <summary>第 08-15 章：文化、信仰的名稱（色塊層的滑鼠提示用；ObserverRoot 刷新時注入）。</summary>
    public IReadOnlyDictionary<int, string> CultureNames { get; set; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<int, string> FaithNames { get; set; } = new Dictionary<int, string>();

    /// <summary>色塊層的滑鼠提示：這一格的顏色代表什麼（依地圖模式）。</summary>
    public string ColorMeaning(DemoHexCell cell)
    {
        var at = new DemoCellRef(cell.Column, cell.Row);
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
        var key = (World, _mode, CurrentWeek, Sites, (object?)Lineage, (object?)Land, (object?)FoodStatuses, (object?)CultureOf, (object?)FaithOf, (object?)KnowledgeCounts);
        if (_flatColors is not null && key.Equals(_flatKey))
            return _flatColors;
        var cells = World!.Cells;
        var colors = new Color[cells.Count];
        for (var i = 0; i < cells.Count; i++)
            colors[i] = FlatColor(cells[i]);
        _flatColors = colors;
        _flatKey = key;
        return colors;
    }

    private Color FlatColor(DemoHexCell cell)
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
        return _mode switch
        {
            Mode.Terrain => owned ? baseColor.Lerp(LineageColor(owner!.LineageRootId), 0.35f) : baseColor,
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
    private (Vector2 Offset, Vector2 Size) StrategicFrame(float radius)
    {
        var sqrtThree = Mathf.Sqrt(3f);
        return (new Vector2(-sqrtThree * radius / 2f, -radius),
            new Vector2(sqrtThree * (World!.Columns + 0.5f), 1.5f * (World.Rows - 1) + 2f) * radius);
    }

    private ImageTexture StrategicTexture()
    {
        var colors = FlatColors();
        var now = Time.GetTicksMsec();
        var structure = (World, _mode);
        // 換世界或模式立刻重畫；同一世界的每週變化最多每 TempStrategicRebuildMs 重畫一次（快轉時不每幀重畫）。
        if (_strategicTexture is not null && ReferenceEquals(_strategicColors, colors))
            return _strategicTexture;
        if (_strategicTexture is not null && structure.Equals(_strategicStructure) && now - _strategicBuiltAt < TempStrategicRebuildMs)
            return _strategicTexture;
        var r = TempStrategicImageRadius;
        var (offset, size) = StrategicFrame(r);
        var width = Math.Max(1, (int)Mathf.Ceil(size.X));
        var height = Math.Max(1, (int)Mathf.Ceil(size.Y));
        var bytes = new byte[width * height * 4];
        var index = new Dictionary<(int, int), int>(World!.Cells.Count);
        for (var i = 0; i < World.Cells.Count; i++)
            index[(World.Cells[i].AxialQ, World.Cells[i].AxialR)] = i;
        var sqrtThree = Mathf.Sqrt(3f);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                // 像素中心 → 軸向座標（Center 的反算）→ 立方座標取整。
                var px = x + 0.5f + offset.X;
                var py = y + 0.5f + offset.Y;
                var fr = py / (1.5f * r);
                var fq = px / (sqrtThree * r) - fr / 2f;
                var (q, rr) = CubeRound(fq, fr);
                if (!index.TryGetValue((q, rr), out var cellIndex))
                    continue;
                var c = colors[cellIndex];
                var o = (y * width + x) * 4;
                bytes[o] = (byte)(Mathf.Clamp(c.R, 0f, 1f) * 255f);
                bytes[o + 1] = (byte)(Mathf.Clamp(c.G, 0f, 1f) * 255f);
                bytes[o + 2] = (byte)(Mathf.Clamp(c.B, 0f, 1f) * 255f);
                bytes[o + 3] = 255;
            }
        }
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

    private static (int Q, int R) CubeRound(float fq, float fr)
    {
        var fs = -fq - fr;
        var q = Mathf.RoundToInt(fq);
        var r = Mathf.RoundToInt(fr);
        var s = Mathf.RoundToInt(fs);
        var dq = Mathf.Abs(q - fq);
        var dr = Mathf.Abs(r - fr);
        var ds = Mathf.Abs(s - fs);
        if (dq > dr && dq > ds)
            q = -r - s;
        else if (dr > ds)
            r = -q - s;
        return (q, r);
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
        else
        {
            var colors = FlatColors();
            for (var i = 0; i < World!.Cells.Count; i++)
            {
                var cell = World.Cells[i];
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                if (OnScreen(center, radius))
                    DrawColoredPolygon(HexCoordinates.Polygon(center, radius + 0.6f), colors[i]);
            }
        }
        if (Sites is null)
            return;
        foreach (var site in Sites.Settlements)
        {
            if (FadedRuins.Contains(site.Id) || (layer == ViewLayer.Strategic && site.IsRuin))
                continue;
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
            if (RecentDisasters.Contains(site.Id))
                DrawMarker(ArtAssets.IconDisaster, center + new Vector2(radius * 0.5f, -radius * 0.5f), radius * 0.8f);
            if (_mode == Mode.Faith && HolySites.Contains(site.Id))
                DrawMarker(ArtAssets.IconHolySite, center + new Vector2(-radius * 0.5f, -radius * 0.5f), radius * 0.9f);
        }
    }
}
