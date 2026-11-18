using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-21 章（使用者 2026-11-17：往 CK3 的地圖世界開發，不要再走 WorldBox 路線）：政治地圖，預設畫法。
/// <list type="bullet">
/// <item>每格陸地屬於一個省份（Core <see cref="DemoProvinceMap"/>：聚落往外長，長不到的是荒野）；省份依地圖模式填色
///   （勢力＝勢力色，文化、信仰、家系、知識、糧食＝各自的顏色），同一組（同勢力、同文化…）之間不畫粗界。</item>
/// <item>著色器（<see cref="PoliticalMapShader"/>）畫地形、填色、省界、勢力界、海岸與怪物地盤；遠看填色濃、拉近淡出露出地形。</item>
/// <item>上面再畫河流、勢力名（遠看沿勢力的長軸排開的大字）、聚落徽章與名牌、怪物巢穴、戰線與交戰。</item>
/// </list>
/// 只改畫面，不改世界。第 08-25 章：舊的像素地圖已移除，這是唯一的地圖畫法。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】政治地圖最大放大（每格半徑像素）。</summary>
    private const float TempPoliticalMaxRadius = 64f;

    /// <summary>【TEMP】省份最短重算間隔（毫秒）：快轉時每週都會變，最多每這麼久重算一次。</summary>
    private const ulong TempProvinceRefreshMs = 250;

    private ColorRect? _politicalRect;
    private ShaderMaterial? _politicalMaterial;
    private ImageTexture? _idsTexture, _landTexture, _paletteTexture, _occupyTexture;
    private (object? Fronts, DemoProvinceMap? Provinces) _occupyKey;
    private DemoProvinceMap? _provinces;
    private (DemoHexWorld? World, object? Sites, object? Territory, object? Nests, object? Ruins, object? Scale) _provinceKey;
    private ulong _provincesBuiltMs;
    private bool _provincesStale;

    /// <summary>省份（聚落編號）→ 著色器用的索引（1 起；0＝荒野）。</summary>
    private readonly Dictionary<int, int> _provinceIndex = new();

    /// <summary>目前模式下各省份的分組（勢力、文化…；0＝沒有）與分組索引。</summary>
    private readonly Dictionary<int, long> _provinceGroup = new();
    private readonly Dictionary<long, int> _groupIndex = new();

    private (Mode Mode, DemoProvinceMap? Provinces, object? Culture, object? Faith, object? Knowledge, object? Food) _paintKey;

    /// <summary>勢力的顏色（勢力編號 → 色票位置；相鄰勢力不同色）。</summary>
    private readonly Dictionary<int, int> _realmColor = new();

    /// <summary>選到的勢力、文化、信仰（著色器整片提亮、金色邊）。</summary>
    private int _selectedRealm, _selectedCulture, _selectedFaith;

    /// <summary>【TEMP】勢力色票：低飽和、CK3 式（24 色；相鄰勢力依序錯開）。</summary>
    private static readonly Color[] TempRealmPalette =
    {
        new(0.70f, 0.33f, 0.30f), new(0.30f, 0.45f, 0.66f), new(0.80f, 0.63f, 0.30f), new(0.42f, 0.58f, 0.33f),
        new(0.55f, 0.38f, 0.62f), new(0.28f, 0.58f, 0.58f), new(0.78f, 0.50f, 0.60f), new(0.58f, 0.45f, 0.30f),
        new(0.40f, 0.42f, 0.72f), new(0.72f, 0.70f, 0.38f), new(0.55f, 0.25f, 0.28f), new(0.33f, 0.50f, 0.43f),
        new(0.85f, 0.72f, 0.52f), new(0.45f, 0.60f, 0.75f), new(0.66f, 0.42f, 0.24f), new(0.50f, 0.55f, 0.30f),
        new(0.62f, 0.30f, 0.45f), new(0.36f, 0.36f, 0.52f), new(0.75f, 0.58f, 0.42f), new(0.30f, 0.40f, 0.30f),
        new(0.84f, 0.48f, 0.38f), new(0.48f, 0.32f, 0.50f), new(0.60f, 0.68f, 0.50f), new(0.68f, 0.60f, 0.75f)
    };

    /// <summary>某勢力在政治地圖上的顏色（勢力頁、圖例共用）。</summary>
    public Color PoliticalRealmColor(int realmId)
        => _realmColor.TryGetValue(realmId, out var i) ? TempRealmPalette[i] : TempRealmPalette[(int)((uint)realmId * 2654435761u >> 27) % TempRealmPalette.Length];

    /// <summary>目前算好的省份（冒煙測試與詳情用；還沒畫過＝null）。</summary>
    public DemoProvinceMap? Provinces => _provinces;

    /// <summary>政治地圖畫面上的勢力名數量（冒煙測試用）。</summary>
    public int PoliticalLabelCount => _labels.Count;

    // ── 畫面 ──────────────────────────────────────────────────────────────

    /// <summary>政治地圖的縮放程度：每格幾像素。</summary>
    private static float CellPx(float radius) => HexCoordinates.Side(radius);

    /// <summary>【TEMP】遠看填色濃、拉近淡出露出地形；地形模式只留邊界。</summary>
    private float PoliticsStrength(float cellPx)
        => _mode == Mode.Terrain ? 0f : Mathf.Lerp(0.86f, 0.34f, Mathf.SmoothStep(24f, 96f, cellPx));

    private void DrawPolitical(Vector2 start, float radius)
    {
        EnsurePoliticalNode();
        EnsureProvinces();
        if (_provinces is null || _politicalMaterial is null)
            return;
        EnsurePaint();
        EnsureOccupation();
        var cellPx = CellPx(radius);
        var origin = start - new Vector2(radius, radius); // 第 (0,0) 格左上角
        if (_politicalRect!.Position != Vector2.Zero || _politicalRect.Size != Size)
        {
            _politicalRect.Position = Vector2.Zero;
            _politicalRect.Size = Size;
        }
        _politicalRect.Show();
        var m = _politicalMaterial;
        m.SetShaderParameter("grid", new Vector2(World!.Columns, World.Rows));
        m.SetShaderParameter("origin", origin);
        m.SetShaderParameter("cell_px", cellPx);
        m.SetShaderParameter("politics", PoliticsStrength(cellPx));
        m.SetShaderParameter("province_lines", _mode == Mode.Terrain ? 0.25f : Mathf.Lerp(0.35f, 0.75f, Mathf.SmoothStep(24f, 96f, cellPx)));
        m.SetShaderParameter("sel_province", SelectedProvinceIndex());
        m.SetShaderParameter("sel_group", SelectedGroupIndex());
        var (tint, vignette, shimmer) = AgeMood(WorldAge); // 第 12-2j 章：世界時代的氛圍
        m.SetShaderParameter("age_tint", tint);
        m.SetShaderParameter("age_vignette", vignette);
        m.SetShaderParameter("age_shimmer", shimmer);

        DrawRivers(origin, cellPx);
        DrawTradeLinks(origin, cellPx); // 第 08-23 章：商路
        DrawGroupLabels(origin, cellPx);
        DrawLairs(origin, cellPx);
        DrawHoldings(origin, cellPx);
        DrawWars(start, radius);
        DrawBattles(start, radius);
        // 選到的不是省份（荒野、水、地下城）時框出那一格。
        if (Highlight is { } focus && SelectedProvinceIndex() < 0 && World.Contains(focus.Column, focus.Row))
        {
            var corner = origin + new Vector2(focus.Column, focus.Row) * cellPx;
            DrawRect(new Rect2(corner, new Vector2(cellPx, cellPx)), new Color(0.1f, 0.08f, 0.05f, 0.8f), false, 4f);
            DrawRect(new Rect2(corner, new Vector2(cellPx, cellPx)), new Color(1f, 0.92f, 0.45f), false, 2f);
        }
    }

    private void EnsurePoliticalNode()
    {
        if (_politicalRect is not null)
            return;
        _politicalMaterial = new ShaderMaterial { Shader = new Shader { Code = PoliticalMapShader.Code } };
        _politicalRect = new ColorRect
        {
            Material = _politicalMaterial,
            ShowBehindParent = true, // 畫在地圖的圖示、名字後面
            MouseFilter = MouseFilterEnum.Ignore,
            Color = new Color(0.10f, 0.13f, 0.17f)
        };
        AddChild(_politicalRect);
        _politicalRect.Position = Vector2.Zero;
        _politicalRect.Size = Size;
        // 沒有戰爭時也要綁一張全透明的佔領圖（沒綁的取樣器是白色，會整張變斜紋）。
        _occupyTexture = Upload(null, 256, 256, new byte[256 * 256 * 4]);
        _politicalMaterial.SetShaderParameter("occupy", _occupyTexture);
    }

    /// <summary>戰爭中被圍攻的省份塗攻方顏色的斜紋（CK3 的佔領斜紋；戰線或省份變了才重建）。</summary>
    private void EnsureOccupation()
    {
        var key = ((object?)WarFronts, _provinces);
        if (key.Equals(_occupyKey) || _politicalMaterial is null)
            return;
        _occupyKey = key;
        var bytes = new byte[256 * 256 * 4];
        foreach (var front in WarFronts)
        {
            // 第 08-22a 章：攻陷之後才畫佔領斜紋（圍城中只有行軍路線與軍旗）；獨立戰爭、內戰照舊整場都畫。
            if (front.Score is not null && !front.Occupied || !_provinceIndex.TryGetValue(front.TargetSettlement, out var index))
                continue;
            var color = FrontColor(front, true);
            var at = index * 4;
            bytes[at] = (byte)Mathf.Clamp(color.R * 255f, 0f, 255f);
            bytes[at + 1] = (byte)Mathf.Clamp(color.G * 255f, 0f, 255f);
            bytes[at + 2] = (byte)Mathf.Clamp(color.B * 255f, 0f, 255f);
            bytes[at + 3] = 255;
        }
        _occupyTexture = Upload(_occupyTexture, 256, 256, bytes);
        _politicalMaterial.SetShaderParameter("occupy", _occupyTexture);
    }

    /// <summary>沒有世界時藏起來。</summary>
    private void HidePolitical() => _politicalRect?.Hide();

    // ── 省份 ──────────────────────────────────────────────────────────────

    private void EnsureProvinces()
    {
        if (World is null || Sites is null)
            return;
        var key = (World, (object?)Sites, (object?)Territory, (object?)MonsterNests, (object?)FadedRuins, (object?)SettlementScale);
        if (_provinces is not null && key.Equals(_provinceKey))
            return;
        var now = Time.GetTicksMsec();
        if (_provinces is not null && ReferenceEquals(key.World, _provinceKey.World) && now - _provincesBuiltMs < TempProvinceRefreshMs)
        {
            _provincesStale = true; // 間隔到了由 _Process 補畫
            return;
        }
        _provincesStale = false;
        _provincesBuiltMs = now;
        _provinceKey = key;
        var visible = Sites.Settlements.Where(s => !s.IsRuin && !FadedRuins.Contains(s.Id));
        _provinces = DemoProvinceMap.Compute(World, visible, Territory, id => SettlementScale.TryGetValue(id, out var s) ? s.Tier : DemoSettlementTier.Camp, MonsterNests);
        _provinceIndex.Clear();
        var ids = new SortedSet<int>();
        for (var r = 0; r < World.Rows; r++)
            for (var c = 0; c < World.Columns; c++)
                if (_provinces.ProvinceAt(c, r) is var p and > 0)
                    ids.Add(p);
        foreach (var id in ids)
        {
            if (_provinceIndex.Count >= PoliticalMapShader.WaterIndex - 2)
                break;
            _provinceIndex[id] = _provinceIndex.Count + 1;
        }
        _realmProvinces.Clear();
        foreach (var id in _provinceIndex.Keys)
        {
            var realm = Territory.RealmOf(id);
            _realmProvinces[realm] = _realmProvinces.GetValueOrDefault(realm) + 1;
        }
        AssignRealmColors();
        _paintKey = default; // 省份變了，填色與分組重算
        BuildLand();
    }

    /// <summary>勢力配色：每個勢力先照編號取一個顏色，跟已經上色的相鄰勢力撞色就往後挪（顏色大致固定，不會每週亂跳）。</summary>
    private void AssignRealmColors()
    {
        var provinces = _provinces!;
        var neighbours = new Dictionary<int, HashSet<int>>();
        void Link(int a, int b)
        {
            if (a == b)
                return;
            (neighbours.TryGetValue(a, out var x) ? x : neighbours[a] = new HashSet<int>()).Add(b);
            (neighbours.TryGetValue(b, out var y) ? y : neighbours[b] = new HashSet<int>()).Add(a);
        }
        var realms = new SortedSet<int>();
        for (var r = 0; r < provinces.Rows; r++)
            for (var c = 0; c < provinces.Columns; c++)
            {
                var p = provinces.ProvinceAt(c, r);
                if (p == DemoProvinceMap.Wild)
                    continue;
                var realm = Territory.RealmOf(p);
                realms.Add(realm);
                // 隔一兩格荒野的也算相鄰（遠看還是挨在一起）。
                for (var dr = 0; dr <= 2; dr++)
                    for (var dc = -2; dc <= 2; dc++)
                    {
                        if (dr == 0 && dc <= 0)
                            continue;
                        var q = provinces.ProvinceAt(c + dc, r + dr);
                        if (q != DemoProvinceMap.Wild)
                            Link(realm, Territory.RealmOf(q));
                    }
            }
        _realmColor.Clear();
        foreach (var realm in realms)
        {
            var n = TempRealmPalette.Length;
            var color = (int)((uint)realm * 2654435761u >> 27) % n;
            var near = neighbours.TryGetValue(realm, out var set) ? set : null;
            for (var tries = 0; tries < n && near is not null && near.Any(o => _realmColor.TryGetValue(o, out var oc) && oc == color); tries++)
                color = (color + 7) % n;
            _realmColor[realm] = color;
        }
    }

    /// <summary>地形與怪物地盤（省份變了才重建）：R＝地形（0 草原、1 森林、2 水、3 丘陵；河流算草原）、G＝怪物地盤。</summary>
    private void BuildLand()
    {
        var world = World!;
        var provinces = _provinces!;
        var bytes = new byte[world.Columns * world.Rows * 4];
        foreach (var cell in world.Cells)
        {
            var at = (cell.Row * world.Columns + cell.Column) * 4;
            bytes[at] = (byte)(DemoProvinceMap.IsRiver(cell) ? 0 : cell.Terrain switch
            {
                DemoTerrain.Forest => 1,
                DemoTerrain.Water => 2,
                DemoTerrain.Hills => 3,
                _ => 0
            });
            bytes[at + 1] = (byte)(provinces.NestAt(cell.Column, cell.Row) != 0 ? 255 : 0);
            bytes[at + 3] = 255;
        }
        _landTexture = Upload(_landTexture, world.Columns, world.Rows, bytes);
        _politicalMaterial!.SetShaderParameter("land", _landTexture);
        _rivers = null; // 換世界時河流重算
    }

    private static ImageTexture Upload(ImageTexture? texture, int width, int height, byte[] bytes)
    {
        using var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, bytes);
        if (texture is not null && texture.GetWidth() == width && texture.GetHeight() == height)
        {
            texture.Update(image);
            return texture;
        }
        return ImageTexture.CreateFromImage(image);
    }

    // ── 填色與分組（依地圖模式） ─────────────────────────────────────────────

    /// <summary>省份在目前模式下屬於哪一組（同一組之間不畫粗界）；0＝沒有。</summary>
    private long GroupOf(int province) => _mode switch
    {
        Mode.Culture => CultureOf.TryGetValue(province, out var c) ? c : 0,
        Mode.Faith => FaithOf.TryGetValue(province, out var f) ? f : 0,
        Mode.Lineage => Sites is null ? 0 : DemoLineageMap.LineageRoot(Sites, province),
        Mode.Knowledge => ((long)(KnowledgeDomain.GetValueOrDefault(province) ?? "").GetHashCode() << 8) + KnowledgeBand(KnowledgeCounts.GetValueOrDefault(province)) + 1,
        Mode.Food => (long)(FoodStatuses.TryGetValue(province, out var s) ? s : FoodStatus.Unsettled) + 1,
        _ => Territory.RealmOf(province)
    };

    /// <summary>省份在目前模式下的填色；null＝不填（例如文化模式裡沒有居民的聚落）。</summary>
    private Color? FillOf(int province) => _mode switch
    {
        Mode.Culture => CultureOf.TryGetValue(province, out var c) ? CultureColor(c) : (Color?)null,
        Mode.Faith => FaithOf.TryGetValue(province, out var f) ? FaithColor(f) : TempNoFaithColor,
        Mode.Lineage => Sites is null ? (Color?)null : LineageColor(DemoLineageMap.LineageRoot(Sites, province)),
        Mode.Knowledge => KnowledgeTint(province),
        Mode.Food => FoodStatusColor(FoodStatuses.TryGetValue(province, out var s) ? s : FoodStatus.Unsettled),
        _ => RealmFill(Territory.RealmOf(province))
    };

    /// <summary>勢力有幾個省份（填色與勢力名依大小分輕重）。</summary>
    private readonly Dictionary<int, int> _realmProvinces = new();

    /// <summary>【TEMP】羊皮紙色：獨立的小聚落（單一省份的勢力）只淡淡染上自己的顏色，大勢力才是重點。</summary>
    private static readonly Color TempParchment = new(0.80f, 0.76f, 0.64f);

    /// <summary>勢力的填色：兩個省份以上用勢力色；只有自己一個省份的獨立聚落混一半羊皮紙色（不會滿地都是碎色塊）。</summary>
    private Color RealmFill(int realm)
        => _realmProvinces.GetValueOrDefault(realm) >= 2 ? PoliticalRealmColor(realm) : PoliticalRealmColor(realm).Lerp(TempParchment, 0.6f);

    private void EnsurePaint()
    {
        var key = (_mode, _provinces, (object?)CultureOf, (object?)FaithOf, (object?)KnowledgeCounts, (object?)FoodStatuses);
        if (key.Equals(_paintKey))
            return;
        _paintKey = key;
        var world = World!;
        var provinces = _provinces!;
        _provinceGroup.Clear();
        _groupIndex.Clear();
        var palette = new byte[256 * 256 * 4];
        foreach (var (province, index) in _provinceIndex)
        {
            var group = GroupOf(province);
            _provinceGroup[province] = group;
            if (group != 0 && !_groupIndex.ContainsKey(group) && _groupIndex.Count < PoliticalMapShader.WaterIndex - 2)
                _groupIndex[group] = _groupIndex.Count + 1;
            if (FillOf(province) is not { } color)
                continue;
            var at = index * 4;
            palette[at] = (byte)Mathf.Clamp(color.R * 255f, 0f, 255f);
            palette[at + 1] = (byte)Mathf.Clamp(color.G * 255f, 0f, 255f);
            palette[at + 2] = (byte)Mathf.Clamp(color.B * 255f, 0f, 255f);
            palette[at + 3] = 255;
        }
        var bytes = new byte[world.Columns * world.Rows * 4];
        for (var r = 0; r < world.Rows; r++)
            for (var c = 0; c < world.Columns; c++)
            {
                var at = (r * world.Columns + c) * 4;
                int p, g;
                if (!DemoProvinceMap.IsLand(world.GetCell(c, r)) && provinces.ProvinceAt(c, r) == DemoProvinceMap.Wild)
                    p = g = PoliticalMapShader.WaterIndex;
                else
                {
                    var province = provinces.ProvinceAt(c, r);
                    p = _provinceIndex.GetValueOrDefault(province);
                    g = p == 0 ? 0 : _groupIndex.GetValueOrDefault(_provinceGroup.GetValueOrDefault(province));
                }
                bytes[at] = (byte)(p & 255);
                bytes[at + 1] = (byte)(p >> 8);
                bytes[at + 2] = (byte)(g & 255);
                bytes[at + 3] = (byte)(g >> 8);
            }
        _idsTexture = Upload(_idsTexture, world.Columns, world.Rows, bytes);
        _paletteTexture = Upload(_paletteTexture, 256, 256, palette);
        _politicalMaterial!.SetShaderParameter("ids", _idsTexture);
        _politicalMaterial.SetShaderParameter("palette", _paletteTexture);
        BuildGroupLabels();
    }

    private int SelectedProvinceIndex()
    {
        if (Highlight is not { } focus || _provinces is null)
            return -1;
        var province = _provinces.ProvinceAt(focus.Column, focus.Row);
        // 只有點到聚落（格上就是那個聚落）或省份時才整片框起；點空地選到勢力時框勢力。
        if (province == DemoProvinceMap.Wild || SelectedGroupKey() is not null)
            return -1;
        return _provinceIndex.TryGetValue(province, out var i) ? i : -1;
    }

    private long? SelectedGroupKey() => FocusGroup is { } focus && _mode is not (Mode.Food or Mode.Knowledge) ? focus : _mode switch
    {
        Mode.Realm or Mode.Terrain when _selectedRealm != 0 => _selectedRealm,
        Mode.Culture when _selectedCulture != 0 => _selectedCulture,
        Mode.Faith when _selectedFaith != 0 => _selectedFaith,
        _ => null
    };

    private int SelectedGroupIndex()
        => SelectedGroupKey() is { } key && _groupIndex.TryGetValue(key, out var i) ? i : -1;

    // ── 與著色器相同的扭曲（點選與疊在上面的圖示對齊邊界） ─────────────────────────

    private static float Fract(float x) => x - MathF.Floor(x);

    private static float Hash(float px, float py)
    {
        px = Fract(px * 123.34f);
        py = Fract(py * 456.21f);
        var d = px * (px + 45.32f) + py * (py + 45.32f);
        px += d;
        py += d;
        return Fract(px * py);
    }

    private static float ValueNoise(float x, float y)
    {
        var ix = MathF.Floor(x);
        var iy = MathF.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        var ux = fx * fx * (3f - 2f * fx);
        var uy = fy * fy * (3f - 2f * fy);
        var a = Hash(ix, iy);
        var b = Hash(ix + 1f, iy);
        var c = Hash(ix, iy + 1f);
        var d = Hash(ix + 1f, iy + 1f);
        return Mathf.Lerp(Mathf.Lerp(a, b, ux), Mathf.Lerp(c, d, ux), uy);
    }

    /// <summary>格座標 → 著色器取樣的格座標（加上雜訊扭曲）。</summary>
    private static Vector2 Warp(Vector2 c)
    {
        var w = new Vector2(ValueNoise(c.X * 1.7f, c.Y * 1.7f), ValueNoise(c.X * 1.7f + 17.3f, c.Y * 1.7f + 5.1f)) - new Vector2(0.5f, 0.5f);
        var w2 = new Vector2(ValueNoise(c.X * 4.1f + 3.1f, c.Y * 4.1f + 8.2f), ValueNoise(c.X * 4.1f + 9.7f, c.Y * 4.1f + 1.3f)) - new Vector2(0.5f, 0.5f);
        return c + w * 0.44f + w2 * 0.12f;
    }

    /// <summary>格座標上的一點在畫面上看起來的位置（扭曲的反方向，圖示才會落在自己的省份裡）。</summary>
    private static Vector2 Unwarp(Vector2 c) => c - (Warp(c) - c);

    /// <summary>格（欄、列）中心在政治地圖上的畫面位置。</summary>
    private static Vector2 PoliticalCenter(Vector2 origin, float cellPx, int column, int row)
        => origin + Unwarp(new Vector2(column + 0.5f, row + 0.5f)) * cellPx;

    /// <summary>政治地圖上滑鼠下的格：照著色器的加權選出省份，再回傳那個省份權重最大的格（水上回傳水格）。</summary>
    private DemoHexCell? PoliticalCellAt(Vector2 position)
    {
        if (World is null || _provinces is null)
            return null;
        var (start, radius) = ViewLayout();
        var cellPx = CellPx(radius);
        var origin = start - new Vector2(radius, radius);
        var cell = (position - origin) / cellPx;
        if (cell.X < 0f || cell.Y < 0f || cell.X >= World.Columns || cell.Y >= World.Rows)
            return null;
        var q = Warp(cell) - new Vector2(0.5f, 0.5f);
        var i0 = new Vector2I(Mathf.FloorToInt(q.X), Mathf.FloorToInt(q.Y));
        var f = q - new Vector2(i0.X, i0.Y);
        Span<float> ws = stackalloc float[] { (1f - f.X) * (1f - f.Y), f.X * (1f - f.Y), (1f - f.X) * f.Y, f.X * f.Y };
        Span<int> pid = stackalloc int[4];
        var cells = new Vector2I[4];
        for (var k = 0; k < 4; k++)
        {
            var at = i0 + new Vector2I(k % 2, k / 2);
            cells[k] = at;
            pid[k] = !World.Contains(at.X, at.Y) ? -1
                : !DemoProvinceMap.IsLand(World.GetCell(at.X, at.Y)) && _provinces.ProvinceAt(at.X, at.Y) == DemoProvinceMap.Wild ? -1
                : _provinces.ProvinceAt(at.X, at.Y);
        }
        float water = 0f, best = -1f;
        var winner = int.MinValue;
        for (var a = 0; a < 4; a++)
        {
            if (pid[a] == -1)
            {
                water += ws[a];
                continue;
            }
            var score = 0f;
            for (var b = 0; b < 4; b++)
                if (pid[b] == pid[a])
                    score += ws[b];
            if (score > best)
            {
                best = score;
                winner = pid[a];
            }
        }
        var want = water >= best ? -1 : winner;
        var pick = -1;
        for (var k = 0; k < 4; k++)
            if (pid[k] == want && World.Contains(cells[k].X, cells[k].Y) && (pick < 0 || ws[k] > ws[pick]))
                pick = k;
        if (pick < 0)
        {
            var plain = new Vector2I(Mathf.FloorToInt(cell.X), Mathf.FloorToInt(cell.Y));
            return World.Contains(plain.X, plain.Y) ? World.GetCell(plain.X, plain.Y) : null;
        }
        return World.GetCell(cells[pick].X, cells[pick].Y);
    }

    /// <summary>政治地圖的滑鼠提示：這一格是哪個聚落的省份（聚落的人口、糧食、文化…）、地形、怪物與戰況。</summary>
    private string PoliticalTooltip(DemoHexCell cell)
    {
        var world = World!;
        var place = DemoCellQuery.ShortName(world, null, cell);
        var province = _provinces?.ProvinceAt(cell.Column, cell.Row) ?? DemoProvinceMap.Wild;
        var site = province == DemoProvinceMap.Wild ? null : Sites?.Settlements.FirstOrDefault(s => s.Id == province);
        var title = site is not null && SettlementScale.TryGetValue(site.Id, out var scale) ? DemoSettlementHistory.TitledName(site, scale.Tier) : place;
        var home = site is not null && world.Contains(site.Column, site.Row) ? world.GetCell(site.Column, site.Row) : cell;
        BuildTipRows(home, title, "");
        if (site is not null)
            _tipRows.Insert(0, (IcPlace, place));
        var at = new DemoCellRef(cell.Column, cell.Row);
        if (home != cell)
            foreach (var nest in MonsterNests)
                if (nest.Dungeon.Cell == at)
                    _tipRows.Add((IcDungeon, nest.Dormant ? $"{nest.Dungeon.Name}：沉寂中" : $"{nest.Dungeon.Name}：怪物 {nest.Monsters}（{nest.Dungeon.Layers} 級）"));
                else if (nest.Cells.Contains(at))
                    _tipRows.Add((IcDungeon, $"{nest.Dungeon.Name}的怪物出沒（{nest.Monsters} 隻）"));
        var text = title;
        if (BattleTip(cell.Column, cell.Row) is { Length: > 0 } battle)
            text = battle + "\n" + text;
        else if (WarTip(cell.Column, cell.Row) is { Length: > 0 } war)
            text = war + "\n" + text;
        var meaning = ColorMeaning(home);
        _tipRows.Insert(0, (IcRealm, meaning));
        return $"{text}\n{meaning}";
    }

    // ── 冒煙測試與截圖用 ───────────────────────────────────────────────────

    /// <summary>政治地圖冒煙測試的結果：省份數、勢力數、勢力名數、聚落格點得到自己省份的數量、可以點的省份陸地（畫面位置）。</summary>
    public sealed record PoliticalCheck(int Provinces, int Realms, int Labels, int Hits, int Sites, Vector2? LandPoint);

    /// <summary>不畫圖、只把政治地圖的資料算好並核對點選（無畫面的冒煙測試）。</summary>
    public PoliticalCheck PoliticalSmokeCheck()
    {
        EnsurePoliticalNode();
        _provinces = null;
        _provinceKey = default;
        EnsureProvinces();
        EnsurePaint();
        var provinces = _provinces ?? throw new InvalidOperationException("政治地圖沒有算出省份");
        var (start, radius) = ViewLayout();
        var cellPx = CellPx(radius);
        var origin = start - new Vector2(radius, radius);
        var sites = Sites!.Settlements.Where(s => !s.IsRuin && _provinceIndex.ContainsKey(s.Id)).ToList();
        var hits = sites.Count(s => PoliticalCellAt(PoliticalCenter(origin, cellPx, s.Column, s.Row)) is { } cell
            && provinces.ProvinceAt(cell.Column, cell.Row) == s.Id);
        if (sites.Count == 0 || hits < sites.Count * 9 / 10)
            throw new InvalidOperationException($"政治地圖點聚落格點不到自己的省份（{hits}/{sites.Count}）");
        Vector2? land = null;
        foreach (var cell in World!.Cells)
        {
            var p = provinces.ProvinceAt(cell.Column, cell.Row);
            if (p == DemoProvinceMap.Wild || Sites.SettlementAt(cell.Column, cell.Row) is not null || !DemoProvinceMap.IsLand(cell)
                || Enumerable.Range(-1, 3).Any(dr => Enumerable.Range(-1, 3).Any(dc => provinces.ProvinceAt(cell.Column + dc, cell.Row + dr) != p)))
                continue;
            var at = PoliticalCenter(origin, cellPx, cell.Column, cell.Row);
            if (PoliticalCellAt(at) is { } hit && provinces.ProvinceAt(hit.Column, hit.Row) == p && Sites.SettlementAt(hit.Column, hit.Row) is null)
            {
                land = at;
                break;
            }
        }
        var realms = _provinceIndex.Keys.Select(id => Territory.RealmOf(id)).Distinct().Count();
        return new PoliticalCheck(_provinceIndex.Count, realms, _labels.Count, hits, sites.Count, land);
    }

    /// <summary>效能量測：重算一次省份、配色、填色與勢力名要幾毫秒（政治地圖每週最多一次）。</summary>
    public double PoliticalBuildMs()
    {
        EnsurePoliticalNode();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        _provinces = null;
        _provinceKey = default;
        EnsureProvinces();
        EnsurePaint();
        return watch.Elapsed.TotalMilliseconds;
    }

    /// <summary>省份最多的勢力的首都格（截圖用）。</summary>
    public DemoHexCell? LargestRealmCapital()
    {
        EnsureProvinces();
        var top = _provinceIndex.Keys.GroupBy(id => Territory.RealmOf(id)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).FirstOrDefault()?.Key;
        return top is { } realm ? SiteCell(realm) : null;
    }

    /// <summary>把鏡頭拉到每格 <paramref name="cellPx"/> 像素、<paramref name="cell"/> 在畫面中央（截圖用）。</summary>
    public void ZoomToCell(DemoHexCell cell, float cellPx)
    {
        RememberView();
        var (_, baseRadius) = Layout();
        _zoom = cellPx / 2f / baseRadius;
        _pan = Vector2.Zero;
        _pan = Size * 0.5f - CellCenter(cell);
        QueueRedraw();
    }

    /// <summary>一格的歸屬：照省份（每格陸地都有）；省份還沒算好時照勢力範圍（只有用到的地）。</summary>
    private DemoTerritoryCell? OwnerAt(DemoCellRef at)
    {
        if (_provinces is not null)
        {
            var province = _provinces.ProvinceAt(at.Column, at.Row);
            return province == DemoProvinceMap.Wild ? null : new DemoTerritoryCell(province, Territory.RealmOf(province));
        }
        return Territory.Cells.TryGetValue(at, out var owner) ? owner : null;
    }

    /// <summary>
    /// 第 12-2j 章【TEMP】世界時代的氛圍（畫面用，不是規則）：染色（rgb＝乘上的顏色、a＝濃淡）、四角壓暗、微光。
    /// 希望時代沒有；黑暗時代夜色加四角壓暗（長夜降臨、怪物猖獗）；烈日偏暖；霜雪偏冷；混沌偏紅；奇蹟時代金色微光。
    /// </summary>
    private static (Color Tint, float Vignette, float Shimmer) AgeMood(DemoAge age) => age switch
    {
        DemoAge.Dark => (new Color(0.62f, 0.66f, 0.92f, 0.55f), 0.42f, 0f),
        DemoAge.Sun => (new Color(1.10f, 0.98f, 0.80f, 0.35f), 0.08f, 0f),
        DemoAge.Frost => (new Color(0.88f, 0.96f, 1.12f, 0.40f), 0.10f, 0f),
        DemoAge.Chaos => (new Color(1.02f, 0.78f, 0.76f, 0.35f), 0.20f, 0f),
        DemoAge.Miracle => (new Color(1.04f, 1.00f, 0.92f, 0.20f), 0f, 1f),
        _ => (new Color(1f, 1f, 1f, 0f), 0f, 0f)
    };
}
