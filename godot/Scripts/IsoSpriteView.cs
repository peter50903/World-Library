using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-20a 章【原型】2D 等角（偽 3D）試看：同 08-20 的小場景（河邊農村、木柵城鎮、河流、藍金勢力邊界），全部用 2D 畫。
/// 正方形格在畫面上轉 45 度、壓成 2:1 菱形；地塊頂面貼俯視紋理、前方兩個側面畫土層（看得出厚度），光從左上；
/// 建築、樹、岩石、水車用第七批等角精靈圖（素材還沒到時畫成色塊），依「列＋欄」由後往前排序。
/// 拉遠改畫色塊；點選先打精靈圖、再打地塊。只是試看用的獨立畫面：不讀也不改世界模擬，不存檔。
/// </summary>
public partial class IsoSpriteView : Control
{
    public event Action? Closed;

    /// <summary>【TEMP】最大放大時一格的半寬、半高（像素）、高度 1 對應的像素；縮放範圍；低於這個倍率改畫色塊。</summary>
    public const float TempHalfWidth = 128f, TempHalfHeight = 64f, TempHeightPx = 128f, TempMinZoom = 0.18f, TempMaxZoom = 1f, TempStrategicZoom = 0.32f;

    public IsoProtoMap Map { get; } = IsoProtoMap.Create();

    /// <summary>一個要畫的物件：精靈圖名稱、世界位置（欄、列、高）、左右翻轉、縮放、屬於哪個聚落、沒有素材時的色塊大小與顏色、動畫格數。</summary>
    public sealed record Sprite(string Art, Vector3 At, bool Flip, float Scale, int Settlement, Vector3 Block, Color Color, int Frames = 1, bool Ground = false);

    public IReadOnlyList<Sprite> Sprites => _sprites;

    private readonly List<Sprite> _sprites = new();
    private readonly Dictionary<string, Texture2D?> _art = new();
    private float _zoom = 0.6f;
    private Vector2 _pan;
    private bool _panned;
    private int? _selectedSettlement;
    private Vector2I? _selectedCell;
    private Label _info = null!;
    private CheckButton _showRealms = null!, _showWar = null!, _showIcons = null!;
    private readonly List<(Rect2 Box, int Settlement)> _hits = new();

    private static readonly Color GoldColor = new(0.96f, 0.78f, 0.22f), BlueColor = new(0.28f, 0.52f, 0.96f);
    private static readonly Vector3 Light = new Vector3(-1f, 1.6f, -0.35f).Normalized(); // 從畫面左上方照來

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        ClipContents = true;
        TextureRepeat = TextureRepeatEnum.Enabled; // 地形紋理要能重複鋪
        BuildSprites();
        BuildPanel();
    }

    // ───────────────────────── 投影 ─────────────────────────

    private float HalfW => TempHalfWidth * _zoom;
    private float HalfH => TempHalfHeight * _zoom;

    /// <summary>世界（欄 x、列 z、高 y）→ 畫面。欄往右下、列往左下；離鏡頭越近（欄＋列越大）越下方。</summary>
    public Vector2 Screen(float x, float y, float z)
        => _pan + new Vector2((x - z) * HalfW, (x + z) * HalfH - y * TempHeightPx * _zoom);

    private Vector2 Screen(Vector3 p) => Screen(p.X, p.Y, p.Z);

    private void CenterView()
    {
        if (_panned || Size.X <= 0f)
            return;
        var mid = Screen(IsoProtoMap.Columns / 2f, 0f, IsoProtoMap.Rows / 2f) - _pan;
        _pan = Size / 2f - mid;
        _panned = true;
    }

    private Texture2D? Art(string name)
    {
        if (!_art.TryGetValue(name, out var texture))
            _art[name] = texture = ArtAssets.Texture($"res://assets/iso/{name}.png");
        return texture;
    }

    // ───────────────────────── 場景配置 ─────────────────────────

    private void Add(string art, float x, float z, int settlement, Vector3 block, Color color, bool flip = false, float scale = 1f, int frames = 1, bool ground = false)
    {
        var c = Math.Clamp((int)x, 0, IsoProtoMap.Columns - 1);
        var r = Math.Clamp((int)z, 0, IsoProtoMap.Rows - 1);
        var y = Map.IsWater(c, r) ? IsoProtoMap.WaterLevel : Map.TopOf(c, r);
        _sprites.Add(new Sprite(art, new Vector3(x, y, z), flip, scale, settlement, block, color, frames, ground));
    }

    /// <summary>依 08-20 小場景擺放：城鎮的木柵、角樓、城門、市集、房子；農村的房子、磨坊、水車、方田、橋；樹林與岩石。</summary>
    private void BuildSprites()
    {
        var house = new Vector3(0.22f, 0.22f, 0.28f);
        var wood = new Color(0.52f, 0.36f, 0.22f);
        var plaster = new Color(0.90f, 0.84f, 0.72f);
        var roof = new Color(0.74f, 0.33f, 0.22f);
        // 木柵城鎮（第 2～4 欄、第 5～7 列）。
        var town = Map.Settlements[0].Id;
        float x0 = 2.08f, x1 = 4.92f, z0 = 5.08f, z1 = 7.92f;
        var cx = (x0 + x1) / 2f;
        var cz = (z0 + z1) / 2f;
        for (var k = 0; k < 6; k++)
        {
            var f = (k + 0.5f) / 6f;
            Add("iso_palisade", x0, z0 + (z1 - z0) * f, town, new Vector3(0.06f, 0.3f, 0.48f), wood);
            Add("iso_palisade", x1, z0 + (z1 - z0) * f, town, new Vector3(0.06f, 0.3f, 0.48f), wood);
            Add("iso_palisade", x0 + (x1 - x0) * f, z0, town, new Vector3(0.48f, 0.3f, 0.06f), wood, flip: true);
            if (k is not (2 or 3))
                Add("iso_palisade", x0 + (x1 - x0) * f, z1, town, new Vector3(0.48f, 0.3f, 0.06f), wood, flip: true);
        }
        foreach (var (x, z) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1) })
            Add("iso_watchtower", x, z, town, new Vector3(0.24f, 0.6f, 0.24f), wood.Darkened(0.1f));
        Add("iso_gatehouse", cx, z1 + 0.02f, town, new Vector3(0.6f, 0.55f, 0.2f), wood.Darkened(0.05f));
        Add("iso_well", cx, cz, town, new Vector3(0.12f, 0.12f, 0.12f), new Color(0.65f, 0.64f, 0.60f));
        Add("iso_stall_red", cx - 0.3f, cz - 0.3f, town, new Vector3(0.22f, 0.18f, 0.22f), new Color(0.82f, 0.22f, 0.20f));
        Add("iso_stall_blue", cx + 0.3f, cz - 0.3f, town, new Vector3(0.22f, 0.18f, 0.22f), new Color(0.22f, 0.42f, 0.78f), flip: true);
        Add("iso_stall_blue", cx - 0.3f, cz + 0.3f, town, new Vector3(0.22f, 0.18f, 0.22f), new Color(0.22f, 0.42f, 0.78f));
        Add("iso_stall_red", cx + 0.3f, cz + 0.3f, town, new Vector3(0.22f, 0.18f, 0.22f), new Color(0.82f, 0.22f, 0.20f), flip: true);
        var houses = new[] { "iso_house_timber", "iso_house_timber_2", "iso_house_stone", "iso_house_timber" };
        var n = 0;
        foreach (var (qx, qz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            for (var i = 0; i < 4; i++, n++)
            {
                var x = cx + (0.5f + (i % 2) * 0.5f) * qx;
                var z = cz + (0.46f + (i / 2) * 0.52f) * qz;
                if (qx == 1 && qz == 1 && i == 0)
                    Add("iso_town_hall", x, z, town, new Vector3(0.4f, 0.4f, 0.45f), plaster);
                else
                    Add(houses[n % houses.Length], x, z, town, house, n % 3 == 2 ? new Color(0.66f, 0.64f, 0.60f) : plaster, flip: (n + qx) % 2 == 0);
            }
        Add("iso_crates", x0 + 0.25f, z0 + 0.25f, town, new Vector3(0.1f, 0.08f, 0.1f), wood);

        // 河邊農村（第 8～10 欄、第 6～8 列；方田第 11～13 欄、第 5～7 列）。
        var village = Map.Settlements[1].Id;
        var thatch = new Color(0.82f, 0.68f, 0.38f);
        var spots = new[] { (8.5f, 6.4f), (8.6f, 7.6f), (9.5f, 6.55f), (9.55f, 7.7f), (10.45f, 6.35f), (10.5f, 7.55f), (9.15f, 8.55f), (10.35f, 8.6f) };
        for (var i = 0; i < spots.Length; i++)
            Add(i % 2 == 0 ? "iso_house_thatch" : "iso_house_thatch_2", spots[i].Item1, spots[i].Item2, village, house, thatch, flip: i % 3 == 0);
        Add("iso_mill", 8.25f, 5.72f, village, new Vector3(0.35f, 0.4f, 0.4f), plaster);
        Add("iso_waterwheel", 7.93f, 5.72f, village, new Vector3(0.08f, 0.4f, 0.4f), wood, frames: 4);
        Add("iso_well", 9.95f, 7.3f, village, new Vector3(0.12f, 0.12f, 0.12f), new Color(0.65f, 0.64f, 0.60f));
        Add("iso_bridge", 7.5f, 7.5f, village, new Vector3(1.3f, 0.06f, 0.22f), wood, flip: true, ground: true);
        for (var c = 11; c <= 13; c++)
            for (var r = 5; r <= 7; r++)
                Add("iso_field_wheat", c + 0.5f, r + 0.5f, village, new Vector3(0.92f, 0.05f, 0.92f), new Color(0.88f, 0.72f, 0.26f), ground: true);
        foreach (var (x, z) in new[] { (14.3f, 5.4f), (14.4f, 5.75f), (10.9f, 4.75f) })
            Add("iso_haystack", x, z, village, new Vector3(0.12f, 0.12f, 0.12f), new Color(0.88f, 0.72f, 0.34f));

        // 樹林、草地上的樹與灌木、丘陵與海岸的岩石（不屬於聚落）。
        var trees = new[] { "iso_tree_pine", "iso_tree_pine_2", "iso_tree_oak", "iso_tree_oak_2" };
        var taken = Map.Settlements.SelectMany(s => s.Cells).ToHashSet();
        for (var c = 0; c < IsoProtoMap.Columns; c++)
            for (var r = 0; r < IsoProtoMap.Rows; r++)
            {
                float H(int k) => IsoProtoMap.Hash01(c, r, k);
                switch (Map.TerrainAt(c, r))
                {
                    case IsoTerrain.Forest when H(7) < 0.3f:
                        Add("iso_tree_grove", c + 0.5f, r + 0.55f, 0, new Vector3(0.6f, 0.45f, 0.6f), new Color(0.22f, 0.44f, 0.22f), flip: H(8) < 0.5f);
                        break;
                    case IsoTerrain.Forest:
                        for (var k = 0; k < 3 + (int)(H(5) * 2f); k++)
                            Add(trees[(int)(H(70 + k) * trees.Length) % trees.Length], c + 0.15f + 0.7f * H(20 + k), r + 0.15f + 0.7f * H(40 + k), 0,
                                new Vector3(0.18f, 0.45f, 0.18f), new Color(0.22f, 0.44f, 0.22f), flip: H(90 + k) < 0.5f, scale: 0.85f + 0.3f * H(60 + k));
                        break;
                    case IsoTerrain.Grass when !taken.Contains(new Vector2I(c, r)):
                        if (H(3) < 0.3f)
                            Add("iso_tree_oak", c + 0.2f + 0.6f * H(21), r + 0.2f + 0.6f * H(22), 0, new Vector3(0.18f, 0.4f, 0.18f), new Color(0.38f, 0.58f, 0.26f));
                        if (H(4) < 0.4f)
                            Add("iso_bush", c + 0.2f + 0.6f * H(24), r + 0.2f + 0.6f * H(25), 0, new Vector3(0.1f, 0.06f, 0.1f), new Color(0.32f, 0.5f, 0.24f));
                        break;
                    case IsoTerrain.Hill:
                        Add(H(6) < 0.5f ? "iso_rocks" : "iso_rock", c + 0.3f + 0.4f * H(26), r + 0.3f + 0.4f * H(27), 0, new Vector3(0.2f, 0.12f, 0.16f), new Color(0.6f, 0.59f, 0.56f));
                        break;
                    case IsoTerrain.Sand when H(11) < 0.5f:
                        Add("iso_rock", c + 0.15f, r + 0.2f + 0.6f * H(12), 0, new Vector3(0.14f, 0.08f, 0.12f), new Color(0.6f, 0.59f, 0.56f));
                        break;
                }
            }
        // 由後往前（欄＋列小的先畫）；地面類（方田、橋）先於同一格的物件。
        _sprites.Sort((a, b) =>
        {
            var d = (a.Ground ? 0 : 1).CompareTo(b.Ground ? 0 : 1);
            return d != 0 ? d : (a.At.X + a.At.Z).CompareTo(b.At.X + b.At.Z);
        });
    }

    // ───────────────────────── 繪圖 ─────────────────────────

    private static float Brightness(Vector3 normal) => 0.62f + 0.48f * Math.Max(0f, normal.Normalized().Dot(Light));

    private Color Lit(Color color, Vector3 normal)
    {
        var k = Brightness(normal);
        return new Color(Math.Min(1f, color.R * k), Math.Min(1f, color.G * k), Math.Min(1f, color.B * k), color.A);
    }

    private static Color TerrainColor(IsoTerrain t, int c, int r)
    {
        var baseColor = t switch
        {
            IsoTerrain.Sea => new Color(0.55f, 0.62f, 0.55f),
            IsoTerrain.River => new Color(0.50f, 0.56f, 0.45f),
            IsoTerrain.Sand => new Color(0.88f, 0.80f, 0.58f),
            IsoTerrain.Forest => new Color(0.34f, 0.50f, 0.25f),
            IsoTerrain.Hill => new Color(0.58f, 0.58f, 0.50f),
            _ => new Color(0.56f, 0.70f, 0.32f)
        };
        var j = IsoProtoMap.Hash01(c, r, 11) * 0.08f - 0.04f;
        return j >= 0f ? baseColor.Lightened(j) : baseColor.Darkened(-j);
    }

    private static string? TextureOf(IsoTerrain t) => t switch
    {
        IsoTerrain.Sand => "iso_tex_sand",
        IsoTerrain.Forest => "iso_tex_forest_floor",
        IsoTerrain.Hill => "iso_tex_hill",
        IsoTerrain.Grass => "iso_tex_grass",
        _ => null
    };

    /// <summary>畫一個面（有紋理就貼紋理，紋理座標依世界位置，每 2 格重複一次）。</summary>
    private void Face(Vector3[] world, Color color, Texture2D? texture, Func<Vector3, Vector2>? uv = null)
    {
        var points = world.Select(p => Screen(p)).ToArray();
        if (texture is null)
        {
            DrawColoredPolygon(points, color);
            return;
        }
        var uvs = world.Select(uv ?? (p => new Vector2(p.X, p.Z) * 0.5f)).ToArray();
        DrawPolygon(points, Enumerable.Repeat(color, points.Length).ToArray(), uvs, texture);
    }

    public override void _Draw()
    {
        CenterView();
        _hits.Clear();
        var strategic = _zoom < TempStrategicZoom;
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.80f, 0.86f, 0.90f));
        var cliff = Art("iso_tex_cliff");
        // 由後往前：一格一格畫頂面、前方兩個側面、水面。
        for (var sum = 0; sum <= IsoProtoMap.Columns + IsoProtoMap.Rows; sum++)
            for (var c = Math.Max(0, sum - IsoProtoMap.Rows + 1); c <= Math.Min(IsoProtoMap.Columns - 1, sum); c++)
                DrawCell(c, sum - c, cliff);
        DrawGridAndFoam();
        DrawRoads();
        if (_showRealms.ButtonPressed)
            DrawRealms(strategic ? 0.45f : 0.22f);
        if (strategic)
            DrawBlocks();
        else
            DrawSprites();
        DrawOverlay(strategic);
    }

    private void DrawCell(int c, int r, Texture2D? cliff)
    {
        var t = Map.TerrainAt(c, r);
        var h = Map.TopOf(c, r);
        var top = TerrainColor(t, c, r);
        var texture = TextureOf(t) is { } name ? Art(name) : null;
        var tint = texture is null ? top : Colors.White;
        // 頂面：丘陵與一般地面都分四個三角面（中心起伏，各面明暗不同）。
        var peak = new Vector3(c + 0.5f, h + Map.PeakOf(c, r), r + 0.5f);
        var corners = new[] { new Vector3(c, h, r), new Vector3(c + 1, h, r), new Vector3(c + 1, h, r + 1), new Vector3(c, h, r + 1) };
        for (var i = 0; i < 4; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % 4];
            var normal = (b - a).Cross(peak - a);
            if (normal.Y < 0f)
                normal = -normal;
            Face(new[] { a, b, peak }, Lit(tint, normal), texture);
        }
        // 前方兩個側面（東、南）：鄰格比較低或在地圖外才畫；上緣一圈表土色、下面是土層（有土層紋理就貼紋理）。
        foreach (var (dc, dr) in new[] { (1, 0), (0, 1) })
        {
            var below = IsoProtoMap.Contains(c + dc, r + dr) ? Map.TopOf(c + dc, r + dr) : IsoProtoMap.Bottom;
            if (below >= h - 1e-3f)
                continue;
            var (a, b) = dc == 1 ? (new Vector3(c + 1, 0, r), new Vector3(c + 1, 0, r + 1)) : (new Vector3(c, 0, r + 1), new Vector3(c + 1, 0, r + 1));
            var normal = dc == 1 ? Vector3.Right : Vector3.Back;
            var quad = new[] { a + Vector3.Up * h, b + Vector3.Up * h, b + Vector3.Up * below, a + Vector3.Up * below };
            if (cliff is not null)
            {
                // 紋理座標：橫向沿邊長（每格一張寬），縱向依深度（地表到地板＝一張高）。
                Face(quad, Lit(Colors.White, normal), cliff, p => new Vector2((p.X + p.Z) * 0.5f, (h - p.Y) / (h - IsoProtoMap.Bottom)));
                continue;
            }
            var band = Math.Max(below, h - 0.07f);
            Face(new[] { a + Vector3.Up * h, b + Vector3.Up * h, b + Vector3.Up * band, a + Vector3.Up * band }, Lit(top.Darkened(0.12f), normal), null);
            var mid = Math.Max(below, IsoProtoMap.Bottom * 0.45f);
            if (band > mid)
                Face(new[] { a + Vector3.Up * band, b + Vector3.Up * band, b + Vector3.Up * mid, a + Vector3.Up * mid }, Lit(new Color(0.50f, 0.36f, 0.23f), normal), null);
            if (mid > below)
                Face(new[] { a + Vector3.Up * mid, b + Vector3.Up * mid, b + Vector3.Up * below, a + Vector3.Up * below }, Lit(new Color(0.38f, 0.27f, 0.17f), normal), null);
        }
        // 水面（比河岸低）；地圖邊緣的水再補前方的水側面。
        if (Map.IsWater(c, r))
        {
            var y = IsoProtoMap.WaterLevel;
            var water = Art("iso_tex_water");
            var color = t == IsoTerrain.River ? new Color(0.32f, 0.64f, 0.82f, 0.85f) : c == 0 ? new Color(0.16f, 0.42f, 0.70f, 0.9f) : new Color(0.24f, 0.56f, 0.78f, 0.88f);
            var surface = new[] { new Vector3(c, y, r), new Vector3(c + 1, y, r), new Vector3(c + 1, y, r + 1), new Vector3(c, y, r + 1) };
            var flow = (float)(Time.GetTicksMsec() / 1000.0 * 0.03 % 1.0);
            Face(surface, water is null ? color : new Color(1f, 1f, 1f, 0.9f), water, p => new Vector2(p.X * 0.5f + flow, p.Z * 0.5f + flow * 0.6f));
            foreach (var (dc, dr) in new[] { (1, 0), (0, 1) })
                if (!IsoProtoMap.Contains(c + dc, r + dr))
                {
                    var (a, b) = dc == 1 ? (new Vector3(c + 1, 0, r), new Vector3(c + 1, 0, r + 1)) : (new Vector3(c, 0, r + 1), new Vector3(c + 1, 0, r + 1));
                    Face(new[] { a + Vector3.Up * y, b + Vector3.Up * y, b + Vector3.Up * h, a + Vector3.Up * h }, new Color(0.18f, 0.45f, 0.70f, 0.85f), null);
                }
        }
    }

    /// <summary>城鎮的十字石子主街（有 iso_tex_road 就貼紋理）。</summary>
    private void DrawRoads()
    {
        var road = Art("iso_tex_road");
        var y = IsoProtoMap.LandTop + 0.002f;
        const float x0 = 2.08f, x1 = 4.92f, z0 = 5.08f, z1 = 7.92f, half = 0.13f;
        var cx = (x0 + x1) / 2f;
        var cz = (z0 + z1) / 2f;
        var color = road is null ? new Color(0.78f, 0.72f, 0.60f) : Colors.White;
        Vector2 Uv(Vector3 p) => new(p.X * 1.5f, p.Z * 1.5f);
        Face(new[] { new Vector3(x0, y, cz - half), new Vector3(x1, y, cz - half), new Vector3(x1, y, cz + half), new Vector3(x0, y, cz + half) }, color, road, Uv);
        Face(new[] { new Vector3(cx - half, y, z0), new Vector3(cx + half, y, z0), new Vector3(cx + half, y, z1 + 0.4f), new Vector3(cx - half, y, z1 + 0.4f) }, color, road, Uv);
        Face(new[] { new Vector3(cx - 0.36f, y, cz - 0.36f), new Vector3(cx + 0.36f, y, cz - 0.36f), new Vector3(cx + 0.36f, y, cz + 0.36f), new Vector3(cx - 0.36f, y, cz + 0.36f) }, color, road, Uv);
    }

    /// <summary>淡淡的格線（陸地與水面）與水陸交界的浪花。</summary>
    private void DrawGridAndFoam()
    {
        if (_zoom < TempStrategicZoom)
            return;
        for (var c = 0; c < IsoProtoMap.Columns; c++)
            for (var r = 0; r < IsoProtoMap.Rows; r++)
            {
                if (Map.TerrainAt(c, r) == IsoTerrain.Hill)
                    continue;
                var water = Map.IsWater(c, r);
                var y = water ? IsoProtoMap.WaterLevel : Map.TopOf(c, r);
                var line = water ? new Color(1f, 1f, 1f, 0.12f) : new Color(0.1f, 0.12f, 0.05f, 0.12f);
                DrawLine(Screen(c, y, r), Screen(c + 1, y, r), line, 1f);
                DrawLine(Screen(c, y, r), Screen(c, y, r + 1), line, 1f);
                if (!water)
                    continue;
                foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    if (!IsoProtoMap.Contains(c + dc, r + dr) || Map.IsWater(c + dc, r + dr))
                        continue;
                    var (a, b) = dc switch
                    {
                        1 => (new Vector3(c + 1, y, r), new Vector3(c + 1, y, r + 1)),
                        -1 => (new Vector3(c, y, r), new Vector3(c, y, r + 1)),
                        _ => dr == 1 ? (new Vector3(c, y, r + 1), new Vector3(c + 1, y, r + 1)) : (new Vector3(c, y, r), new Vector3(c + 1, y, r))
                    };
                    DrawLine(Screen(a), Screen(b), new Color(1f, 1f, 1f, 0.55f), Math.Max(1.5f, 3f * _zoom));
                }
            }
    }

    /// <summary>勢力：半透明菱形色塊＋沿格邊的亮色邊界（河不屬於任何勢力，兩岸各一條）。</summary>
    private void DrawRealms(float fill)
    {
        for (var c = 0; c < IsoProtoMap.Columns; c++)
            for (var r = 0; r < IsoProtoMap.Rows; r++)
            {
                var realm = Map.RealmAt(c, r);
                if (realm == 0)
                    continue;
                var color = realm == IsoProtoMap.Gold ? GoldColor : BlueColor;
                var y = Map.TopOf(c, r) + 0.01f;
                DrawColoredPolygon(new[] { Screen(c, y, r), Screen(c + 1, y, r), Screen(c + 1, y, r + 1), Screen(c, y, r + 1) }, new Color(color, fill));
                var width = Math.Max(2f, 5f * _zoom);
                var edge = new Color(color.Lightened(0.15f), 0.95f);
                if (Map.RealmAt(c + 1, r) != realm) DrawLine(Screen(c + 1, y, r), Screen(c + 1, y, r + 1), edge, width);
                if (Map.RealmAt(c - 1, r) != realm) DrawLine(Screen(c, y, r), Screen(c, y, r + 1), edge, width);
                if (Map.RealmAt(c, r + 1) != realm) DrawLine(Screen(c, y, r + 1), Screen(c + 1, y, r + 1), edge, width);
                if (Map.RealmAt(c, r - 1) != realm) DrawLine(Screen(c, y, r), Screen(c + 1, y, r), edge, width);
            }
    }

    /// <summary>精靈圖（有素材就貼圖，沒有就畫等角色塊），先畫落地陰影；同時記下點選範圍。</summary>
    private void DrawSprites()
    {
        var frame = (int)(Time.GetTicksMsec() / 180 % 4);
        foreach (var s in _sprites)
        {
            if (_zoom < 0.45f && s.Art is "iso_crates" or "iso_bush" or "iso_haystack")
                continue; // 區域層省略小物件
            var foot = Screen(s.At);
            if (foot.X < -300f || foot.Y < -300f || foot.X > Size.X + 300f || foot.Y > Size.Y + 400f)
                continue;
            var texture = Art(s.Art);
            if (!s.Ground)
            {
                var r = Math.Max(s.Block.X, s.Block.Z) * HalfW * 1.1f * s.Scale;
                DrawSetTransform(foot + new Vector2(r * 0.25f, 0f), 0f, new Vector2(1f, 0.5f));
                DrawCircle(Vector2.Zero, r, new Color(0.1f, 0.08f, 0.05f, 0.28f));
                DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            }
            Rect2 box;
            if (texture is not null)
            {
                var fw = texture.GetWidth() / (float)s.Frames;
                var size = new Vector2(fw, texture.GetHeight()) * _zoom * s.Scale;
                // 地面類（方田、橋）：精靈圖底部對齊格的最前方角落；其他：底部中心對齊所在位置。
                var anchor = s.Ground ? new Vector2(foot.X, Screen(s.At.X + 0.5f * s.Block.X, s.At.Y, s.At.Z + 0.5f * s.Block.Z).Y + HalfH * 0.05f) : foot;
                box = new Rect2(anchor - new Vector2(size.X / 2f, size.Y), size);
                var source = new Rect2(fw * (s.Frames > 1 ? frame : 0), 0f, fw, texture.GetHeight());
                if (s.Flip)
                {
                    DrawSetTransform(new Vector2(box.Position.X * 2f + box.Size.X, 0f), 0f, new Vector2(-1f, 1f));
                    DrawTextureRectRegion(texture, box, source);
                    DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                }
                else
                    DrawTextureRectRegion(texture, box, source);
            }
            else
                box = DrawBlock(s);
            if (s.Settlement > 0)
                _hits.Add((box, s.Settlement));
        }
    }

    /// <summary>沒有素材時的等角色塊：方塊（頂面亮、前兩面依光向），房子類加一個斜頂。回傳點選範圍。</summary>
    private Rect2 DrawBlock(Sprite s)
    {
        var half = s.Block * s.Scale / 2f;
        var p = s.At;
        var y0 = p.Y;
        var y1 = p.Y + s.Block.Y * s.Scale;
        Vector3 P(float dx, float y, float dz) => new(p.X + dx, y, p.Z + dz);
        var south = new[] { P(-half.X, y1, half.Z), P(half.X, y1, half.Z), P(half.X, y0, half.Z), P(-half.X, y0, half.Z) };
        var east = new[] { P(half.X, y1, -half.Z), P(half.X, y1, half.Z), P(half.X, y0, half.Z), P(half.X, y0, -half.Z) };
        var top = new[] { P(-half.X, y1, -half.Z), P(half.X, y1, -half.Z), P(half.X, y1, half.Z), P(-half.X, y1, half.Z) };
        Face(south, Lit(s.Color, Vector3.Back), null);
        Face(east, Lit(s.Color, Vector3.Right), null);
        var house = s.Art.StartsWith("iso_house", StringComparison.Ordinal) || s.Art is "iso_town_hall" or "iso_mill";
        if (house)
        {
            var roof = s.Art.Contains("thatch", StringComparison.Ordinal) ? new Color(0.80f, 0.64f, 0.34f) : new Color(0.74f, 0.33f, 0.22f);
            var ridge = y1 + half.X * 1.2f;
            Face(new[] { P(-half.X * 1.2f, y1, -half.Z * 1.1f), P(0, ridge, -half.Z * 1.1f), P(0, ridge, half.Z * 1.1f), P(-half.X * 1.2f, y1, half.Z * 1.1f) }, Lit(roof, new Vector3(-1f, 1f, 0f)), null);
            Face(new[] { P(half.X * 1.2f, y1, -half.Z * 1.1f), P(0, ridge, -half.Z * 1.1f), P(0, ridge, half.Z * 1.1f), P(half.X * 1.2f, y1, half.Z * 1.1f) }, Lit(roof, new Vector3(1f, 1f, 0f)), null);
            Face(new[] { P(-half.X * 1.2f, y1, half.Z * 1.1f), P(half.X * 1.2f, y1, half.Z * 1.1f), P(0, ridge, half.Z * 1.1f) }, Lit(s.Color.Darkened(0.1f), Vector3.Back), null);
        }
        else
            Face(top, Lit(s.Color.Lightened(0.08f), Vector3.Up), null);
        var pts = south.Concat(east).Concat(top).Select(w => Screen(w)).ToArray();
        var box = new Rect2(pts[0], Vector2.Zero);
        foreach (var q in pts)
            box = box.Expand(q);
        return box;
    }

    /// <summary>戰略層（拉遠）：建築與樹改成色塊——聚落佔的格是勢力色的方塊。</summary>
    private void DrawBlocks()
    {
        foreach (var s in Map.Settlements)
        {
            var color = (s.Realm == IsoProtoMap.Gold ? GoldColor : BlueColor).Darkened(0.25f);
            foreach (var cell in s.Cells)
            {
                var y = Map.TopOf(cell.X, cell.Y);
                DrawColoredPolygon(new[] { Screen(cell.X + 0.05f, y + 0.12f, cell.Y + 0.05f), Screen(cell.X + 0.95f, y + 0.12f, cell.Y + 0.05f),
                    Screen(cell.X + 0.95f, y + 0.12f, cell.Y + 0.95f), Screen(cell.X + 0.05f, y + 0.12f, cell.Y + 0.95f) }, color);
            }
        }
    }

    /// <summary>名牌、工作地圖示、戰爭箭頭與交叉劍、選取外框、目前圖層。</summary>
    private void DrawOverlay(bool strategic)
    {
        var font = ThemeDB.FallbackFont;
        var outline = _selectedSettlement is { } sid ? Map.Settlements.First(s => s.Id == sid).Cells : _selectedCell is { } one ? new[] { one } : Array.Empty<Vector2I>();
        var set = outline.ToHashSet();
        foreach (var cell in outline)
        {
            var y = Map.IsWater(cell.X, cell.Y) ? IsoProtoMap.WaterLevel : Map.TopOf(cell.X, cell.Y);
            foreach (var (dc, dr, a, b) in new[] { (1, 0, new Vector2(1, 0), new Vector2(1, 1)), (-1, 0, new Vector2(0, 0), new Vector2(0, 1)), (0, 1, new Vector2(0, 1), new Vector2(1, 1)), (0, -1, new Vector2(0, 0), new Vector2(1, 0)) })
            {
                if (set.Contains(new Vector2I(cell.X + dc, cell.Y + dr)))
                    continue;
                var p = Screen(cell.X + a.X, y, cell.Y + a.Y);
                var q = Screen(cell.X + b.X, y, cell.Y + b.Y);
                DrawLine(p, q, new Color(0.1f, 0.08f, 0.05f, 0.8f), 5f);
                DrawLine(p, q, new Color(1f, 0.95f, 0.35f), 2.5f);
            }
        }
        foreach (var s in Map.Settlements)
        {
            var p = Screen(s.Center.X + 0.5f, IsoProtoMap.LandTop + 0.9f, s.Center.Y + 0.5f);
            var text = $"{s.Name}・{s.Kind}";
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 15).X + 16f;
            var box = new Rect2(p - new Vector2(width / 2f, 12f), new Vector2(width, 24f));
            DrawRect(box, new Color(0.13f, 0.10f, 0.07f, 0.82f));
            DrawRect(box, s.Realm == IsoProtoMap.Gold ? GoldColor : BlueColor, false, 2f);
            DrawString(font, box.Position + new Vector2(8f, 17f), text, HorizontalAlignment.Left, -1, 15, new Color(0.98f, 0.95f, 0.86f));
        }
        if (_showIcons.ButtonPressed && !strategic)
            foreach (var (at, glyph, _) in Map.Icons)
            {
                var foot = Screen(at.X, IsoProtoMap.LandTop + 0.4f, at.Y);
                var head = foot - new Vector2(0f, 26f);
                DrawColoredPolygon(new[] { foot, head + new Vector2(-6f, 10f), head + new Vector2(6f, 10f) }, new Color(0.98f, 0.95f, 0.86f));
                var box = new Rect2(head - new Vector2(15f, 15f), new Vector2(30f, 28f));
                DrawRect(box, new Color(0.98f, 0.95f, 0.86f));
                DrawRect(box, new Color(0.30f, 0.22f, 0.14f), false, 2f);
                DrawString(font, box.Position + new Vector2(6f, 21f), glyph, HorizontalAlignment.Left, -1, 18, new Color(0.30f, 0.22f, 0.14f));
            }
        if (_showWar.ButtonPressed)
        {
            var from = Screen(4.6f, IsoProtoMap.LandTop + 0.2f, 6.5f);
            var to = Screen(8.6f, IsoProtoMap.LandTop + 0.2f, 6.9f);
            var mid = (from + to) / 2f - new Vector2(0f, 40f);
            var points = Enumerable.Range(0, 25).Select(i => i / 24f).Select(t => from * (1 - t) * (1 - t) + mid * 2 * t * (1 - t) + to * t * t).ToArray();
            DrawPolyline(points, new Color(0.2f, 0.02f, 0.02f, 0.7f), 8f);
            DrawPolyline(points, new Color(0.88f, 0.16f, 0.14f), 5f);
            var dir = (points[^1] - points[^3]).Normalized();
            var side = new Vector2(-dir.Y, dir.X);
            DrawColoredPolygon(new[] { to + dir * 10f, to - dir * 6f + side * 9f, to - dir * 6f - side * 9f }, new Color(0.88f, 0.16f, 0.14f));
            var swords = points[12] - new Vector2(0f, 6f);
            DrawCircle(swords, 15f, new Color(0.10f, 0.07f, 0.05f, 0.85f));
            DrawString(font, swords + new Vector2(-10f, 8f), "⚔", HorizontalAlignment.Left, -1, 22, new Color(0.95f, 0.92f, 0.85f));
        }
        var missing = _sprites.Select(s => s.Art).Distinct().Count(a => Art(a) is null);
        var layer = strategic ? "戰略（色塊）" : _zoom < 0.45f ? "區域（省略小物件）" : "近景";
        DrawString(font, new Vector2(12f, Size.Y - 14f), $"圖層：{layer}｜倍率 {_zoom:0.00}｜還沒有素材、畫成色塊的精靈圖：{missing} 種", HorizontalAlignment.Left, -1, 15, new Color(0.1f, 0.08f, 0.06f));
    }

    // ───────────────────────── 操作 ─────────────────────────

    public override void _Process(double delta) => QueueRedraw();

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown } wheel:
                var next = Math.Clamp(_zoom * (wheel.ButtonIndex == MouseButton.WheelUp ? 1.12f : 1f / 1.12f), TempMinZoom, TempMaxZoom);
                _pan = wheel.Position - (wheel.Position - _pan) * (next / _zoom); // 以滑鼠位置為中心縮放
                _zoom = next;
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                Pick(click.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when (motion.ButtonMask & (MouseButtonMask.Middle | MouseButtonMask.Right)) != 0:
                _pan += motion.Relative;
                AcceptEvent();
                break;
        }
    }

    /// <summary>點選：先打精靈圖（由前往後），再打地塊（由前往後找第一個包含滑鼠的頂面或側面）。</summary>
    private void Pick(Vector2 screen)
    {
        int? hit = null;
        for (var i = _hits.Count - 1; i >= 0 && hit is null; i--)
            if (_hits[i].Box.HasPoint(screen))
                hit = _hits[i].Settlement;
        _selectedCell = CellAt(screen);
        if (hit is null && _selectedCell is { } cell)
            hit = Map.SettlementAt(cell.X, cell.Y)?.Id;
        _selectedSettlement = hit;
        if (hit is { } id && Map.Settlements.First(x => x.Id == id) is var s)
            _info.Text = $"選到聚落：{s.Name}（{s.Kind}，{(s.Realm == IsoProtoMap.Gold ? "金色" : "藍色")}勢力，佔 {s.Cells.Count} 格）";
        else if (_selectedCell is { } c)
            _info.Text = $"選到地塊：({c.X},{c.Y}) {IsoProtoMap.TerrainName(Map.TerrainAt(c.X, c.Y))}"
                + (Map.RealmAt(c.X, c.Y) switch { IsoProtoMap.Gold => "，金色勢力", IsoProtoMap.Blue => "，藍色勢力", _ => "，無主" });
        else
            _info.Text = "沒有點到地圖";
    }

    /// <summary>畫面座標落在哪一格（由前往後：頂面菱形或前方側面包含這一點的第一格）。</summary>
    public Vector2I? CellAt(Vector2 screen)
    {
        for (var sum = IsoProtoMap.Columns + IsoProtoMap.Rows; sum >= 0; sum--)
            for (var c = Math.Min(IsoProtoMap.Columns - 1, sum); c >= Math.Max(0, sum - IsoProtoMap.Rows + 1); c--)
            {
                var r = sum - c;
                var h = Map.IsWater(c, r) ? IsoProtoMap.WaterLevel : Map.TopOf(c, r);
                var top = new[] { Screen(c, h, r), Screen(c + 1, h, r), Screen(c + 1, h, r + 1), Screen(c, h, r + 1) };
                if (Geometry2D.IsPointInPolygon(screen, top))
                    return new Vector2I(c, r);
                foreach (var (dc, dr) in new[] { (1, 0), (0, 1) })
                {
                    var below = IsoProtoMap.Contains(c + dc, r + dr) ? Map.TopOf(c + dc, r + dr) : IsoProtoMap.Bottom;
                    if (below >= h)
                        continue;
                    var (a, b) = dc == 1 ? (new Vector3(c + 1, 0, r), new Vector3(c + 1, 0, r + 1)) : (new Vector3(c, 0, r + 1), new Vector3(c + 1, 0, r + 1));
                    var side = new[] { Screen(a.X, h, a.Z), Screen(b.X, h, b.Z), Screen(b.X, below, b.Z), Screen(a.X, below, a.Z) };
                    if (Geometry2D.IsPointInPolygon(screen, side))
                        return new Vector2I(c, r);
                }
            }
        return null;
    }

    private void BuildPanel()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.12f, 0.10f, 0.08f, 0.85f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6 });
        AddChild(panel);
        panel.Position = new Vector2(12f, 12f);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(new Label { Text = "2D 等角原型（試看用，不影響世界）　滾輪縮放｜右鍵或中鍵拖曳｜左鍵點選" });
        var row = new HBoxContainer();
        column.AddChild(row);
        _showRealms = new CheckButton { Text = "勢力", ButtonPressed = true };
        _showIcons = new CheckButton { Text = "工作地圖示", ButtonPressed = true };
        _showWar = new CheckButton { Text = "戰爭示意", ButtonPressed = false };
        row.AddChild(_showRealms);
        row.AddChild(_showIcons);
        row.AddChild(_showWar);
        var back = new Button { Text = "返回地圖" };
        back.Pressed += () => Closed?.Invoke();
        row.AddChild(back);
        _info = new Label { Text = "點聚落或地塊看選到什麼（第七批素材到之前，建築與樹畫成色塊）" };
        column.AddChild(_info);
    }

    /// <summary>冒煙測試：場景擺好、投影與反投影一致（格中心投到畫面再點回來是同一格）。只讀，不改世界。</summary>
    public string SelfCheck()
    {
        if (_sprites.Count < 60)
            return $"iso sprite prototype has only {_sprites.Count} sprites";
        foreach (var (c, r) in new[] { (3, 6), (7, 6), (12, 6), (0, 0), (15, 11) })
        {
            var h = Map.IsWater(c, r) ? IsoProtoMap.WaterLevel : Map.TopOf(c, r);
            if (CellAt(Screen(c + 0.5f, h, r + 0.5f)) != new Vector2I(c, r))
                return $"iso sprite prototype pick wrong at ({c},{r}): {CellAt(Screen(c + 0.5f, h, r + 0.5f))}";
        }
        return "";
    }
}
