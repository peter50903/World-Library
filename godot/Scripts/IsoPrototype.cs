using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-20 章【原型】3D 等角地圖試看（使用者 2026-11-14：等角視角、霧面低多邊形建築、柔和日光、看得出厚度的地塊；
/// 建築像擺在地圖上的小模型，聚落靠格局辨認；地圖資訊蓋得上去也看得清下面）。
/// 正交鏡頭（真等角 35.26°）＋斜射暖光與柔和陰影；地塊是有側面的方塊（土層看得見）；建築全部由程式用方塊、斜頂、圓柱拼成。
/// 三層：近景（完整模型＋陰影）、區域（關陰影）、戰略（拉遠時改畫色塊）。點選先打建築、再打地面。
/// 只是試看用的獨立畫面：不讀也不改世界模擬，不存檔。
/// </summary>
public partial class IsoPrototypeView : Control
{
    public event Action? Closed;

    /// <summary>【TEMP】鏡頭大小（可見範圍的高度，格）：最近、最遠、預設；超過這兩個值換成區域層、戰略層。</summary>
    public const float TempMinSize = 3.5f, TempMaxSize = 22f, TempStartSize = 9f, TempRegionalSize = 8.5f, TempStrategicSize = 14f;

    public IsoProtoMap Map { get; } = IsoProtoMap.Create();

    /// <summary>建好的建築數（冒煙測試用）。</summary>
    public int BuildingCount => _picks.Count;

    public bool Built { get; private set; }

    private SubViewport _viewport = null!;
    private Node3D _world = null!;
    private Camera3D _camera = null!;
    private DirectionalLight3D _sun = null!;
    private Node3D _detail = null!, _trees = null!, _realms = null!, _strategic = null!;
    private Control _overlay = null!;
    private Label _info = null!;
    private CheckButton _showRealms = null!, _showWar = null!, _showIcons = null!;
    private Vector3 _target = new(8f, 0.3f, 6f);
    private float _size = TempStartSize;
    private Node3D? _wheel;
    private int? _selectedSettlement;
    private Vector2I? _selectedCell;
    private readonly List<(Aabb Box, int Settlement)> _picks = new();
    private readonly Dictionary<Color, StandardMaterial3D> _materials = new();

    private static readonly Color GoldColor = new(0.96f, 0.78f, 0.22f);
    private static readonly Color BlueColor = new(0.28f, 0.52f, 0.96f);
    private static readonly Color Wood = new(0.52f, 0.36f, 0.22f), DarkWood = new(0.38f, 0.26f, 0.16f);
    private static readonly Color Plaster = new(0.90f, 0.84f, 0.72f), Stone = new(0.66f, 0.64f, 0.60f);
    private static readonly Color Terracotta = new(0.74f, 0.33f, 0.22f), Thatch = new(0.78f, 0.62f, 0.32f);

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;
        var container = new SubViewportContainer { Stretch = true, MouseFilter = MouseFilterEnum.Ignore };
        AddChild(container);
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _viewport = new SubViewport { OwnWorld3D = true, Msaa3D = Viewport.Msaa.Msaa4X, HandleInputLocally = false };
        container.AddChild(_viewport);
        _world = new Node3D();
        _viewport.AddChild(_world);
        BuildScene();
        _overlay = new IsoPrototypeOverlay { MouseFilter = MouseFilterEnum.Ignore, Painter = DrawOverlay };
        AddChild(_overlay);
        _overlay.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildPanel();
        PlaceCamera();
        Built = true;
    }

    // ───────────────────────── 場景 ─────────────────────────

    private void BuildScene()
    {
        _world.AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color(0.80f, 0.86f, 0.90f),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color(0.78f, 0.84f, 0.95f),
                AmbientLightEnergy = 0.55f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        });
        // 柔和日光：從畫面左上方斜照，陰影落向右下（朝鏡頭這側，看得到）。
        _sun = new DirectionalLight3D
        {
            LightColor = new Color(1f, 0.94f, 0.82f),
            LightEnergy = 1.15f,
            ShadowEnabled = true,
            ShadowBlur = 2.5f,
            DirectionalShadowMaxDistance = 40f
        };
        _world.AddChild(_sun);
        _sun.LookAtFromPosition(new Vector3(-6f, 14f, 9f), new Vector3(8f, 0f, 6f), Vector3.Up);
        _camera = new Camera3D { Projection = Camera3D.ProjectionType.Orthogonal, Size = _size, Near = 0.05f, Far = 200f };
        _world.AddChild(_camera);
        _camera.Current = true;

        _detail = new Node3D();
        _trees = new Node3D();
        _realms = new Node3D();
        _strategic = new Node3D { Visible = false };
        _world.AddChild(_detail);
        _world.AddChild(_trees);
        _world.AddChild(_realms);
        _world.AddChild(_strategic);

        // 第 08-20b 章：有 KayKit 模型就用現成模型，沒有就退回程式拼的模型。
        UsesKit = KitAvailable();
        BuildTerrain();
        BuildWater();
        BuildTerrainDetail();
        if (UsesKit)
        {
            BuildKitNature();
            var roads = new IsoMeshBuilder { Variation = 0f };
            BuildKitRoads(roads);
            BuildKitTown(Map.Settlements[0], roads);
            BuildKitVillage(Map.Settlements[1], roads);
            _detail.AddChild(roads.Commit(VertexMaterial()));
        }
        else
        {
            BuildTrees();
            BuildTown(Map.Settlements[0]);
            BuildVillage(Map.Settlements[1]);
        }
        BuildRealms();
        BuildStrategic();
    }

    private StandardMaterial3D Material(Color color, bool unshaded = false)
    {
        var key = unshaded ? new Color(color.R, color.G, color.B, -color.A) : color;
        if (_materials.TryGetValue(key, out var m))
            return m;
        m = new StandardMaterial3D { AlbedoColor = color, Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        if (unshaded)
            m.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        if (color.A < 1f)
            m.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        _materials[key] = m;
        return m;
    }

    private static StandardMaterial3D VertexMaterial()
        => new() { VertexColorUseAsAlbedo = true, Roughness = 1f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };

    /// <summary>加一個方塊（中心、大小、顏色、Y 軸旋轉度數）。</summary>
    private MeshInstance3D Box(Node3D parent, Vector3 center, Vector3 size, Color color, float yaw = 0f)
    {
        var node = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Material(color), Position = center, RotationDegrees = new Vector3(0f, yaw, 0f) };
        parent.AddChild(node);
        return node;
    }

    /// <summary>
    /// 地塊：每格一個方塊——頂面四個三角面（中心點起伏，低多邊形霧面），比鄰格高的那幾面畫側面（上緣一圈表土色、下面是土層）；
    /// 地圖邊緣一路畫到底部，整塊地圖像一塊有厚度的模型。
    /// </summary>
    private void BuildTerrain()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(Vector3 a, Vector3 b, Vector3 c, Color color)
        {
            var n = (b - a).Cross(c - a).Normalized();
            if (n.Y < 0f)
                n = -n;
            foreach (var v in new[] { a, b, c })
            {
                st.SetColor(color);
                st.SetNormal(n);
                st.AddVertex(v);
            }
        }
        void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Color color)
        {
            foreach (var v in new[] { a, b, c, a, c, d })
            {
                st.SetColor(color);
                st.SetNormal(normal);
                st.AddVertex(v);
            }
        }
        var soil = new Color(0.47f, 0.33f, 0.21f);
        var deep = new Color(0.36f, 0.25f, 0.16f);
        for (var c = 0; c < IsoProtoMap.Columns; c++)
            for (var r = 0; r < IsoProtoMap.Rows; r++)
            {
                var t = Map.TerrainAt(c, r);
                var h = Map.TopOf(c, r);
                var jitter = IsoProtoMap.Hash01(c, r, 11) * 0.08f - 0.04f;
                var top = UsesKit ? KitTone(c, r, t) : t switch
                {
                    IsoTerrain.Sea => new Color(0.55f, 0.62f, 0.55f),
                    IsoTerrain.River => new Color(0.50f, 0.56f, 0.45f),
                    IsoTerrain.Sand => new Color(0.88f, 0.80f, 0.58f),
                    IsoTerrain.Forest => new Color(0.36f, 0.52f, 0.26f),
                    IsoTerrain.Hill => new Color(0.56f, 0.56f, 0.50f),
                    _ => new Color(0.56f, 0.70f, 0.32f)
                };
                top = top.Lightened(jitter > 0f ? jitter : 0f).Darkened(jitter < 0f ? -jitter : 0f);
                var peak = new Vector3(c + 0.5f, h + Map.PeakOf(c, r), r + 0.5f);
                var corners = new[] { new Vector3(c, h, r), new Vector3(c + 1, h, r), new Vector3(c + 1, h, r + 1), new Vector3(c, h, r + 1) };
                for (var i = 0; i < 4; i++)
                {
                    // 丘陵：高處帶一點雪白，低多邊形的每一面亮暗不同。
                    var face = t == IsoTerrain.Hill && peak.Y > IsoProtoMap.HillTop + 0.38f && i % 2 == 0 ? new Color(0.92f, 0.93f, 0.95f) : top;
                    Tri(corners[i], corners[(i + 1) % 4], peak, face.Darkened(i * 0.025f));
                }
                // 第 08-20b 章：KayKit 色調下，每格東、南兩邊一條稍暗的倒角，看得出一格一格。
                if (UsesKit && !Map.IsWater(c, r))
                {
                    const float rim = 0.035f;
                    var edge = top.Darkened(0.14f);
                    var ry = h + 0.002f;
                    Quad(new Vector3(c, ry, r + 1 - rim), new Vector3(c + 1, ry, r + 1 - rim), new Vector3(c + 1, ry, r + 1), new Vector3(c, ry, r + 1), Vector3.Up, edge);
                    Quad(new Vector3(c + 1 - rim, ry, r), new Vector3(c + 1, ry, r), new Vector3(c + 1, ry, r + 1), new Vector3(c + 1 - rim, ry, r + 1), Vector3.Up, edge);
                }
                // 側面：東、南、西、北；鄰格比較低（或在地圖外）才畫。
                foreach (var (dc, dr, normal) in new[] { (1, 0, Vector3.Right), (0, 1, Vector3.Back), (-1, 0, Vector3.Left), (0, -1, Vector3.Forward) })
                {
                    var below = IsoProtoMap.Contains(c + dc, r + dr) ? Map.TopOf(c + dc, r + dr) : IsoProtoMap.Bottom;
                    if (below >= h - 1e-3f)
                        continue;
                    Vector3 A, B;
                    if (dc == 1) { A = new Vector3(c + 1, 0, r); B = new Vector3(c + 1, 0, r + 1); }
                    else if (dc == -1) { A = new Vector3(c, 0, r + 1); B = new Vector3(c, 0, r); }
                    else if (dr == 1) { A = new Vector3(c + 1, 0, r + 1); B = new Vector3(c, 0, r + 1); }
                    else { A = new Vector3(c, 0, r); B = new Vector3(c + 1, 0, r); }
                    var band = Math.Max(below, h - 0.07f);
                    Quad(A + Vector3.Up * h, B + Vector3.Up * h, B + Vector3.Up * band, A + Vector3.Up * band, normal, top.Darkened(0.18f));
                    var mid = Math.Max(below, IsoProtoMap.Bottom * 0.45f);
                    if (band > mid)
                        Quad(A + Vector3.Up * band, B + Vector3.Up * band, B + Vector3.Up * mid, A + Vector3.Up * mid, normal, soil);
                    if (mid > below)
                        Quad(A + Vector3.Up * mid, B + Vector3.Up * mid, B + Vector3.Up * below, A + Vector3.Up * below, normal, deep);
                }
            }
        _world.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = VertexMaterial() });
    }

    /// <summary>水面：海與河一層半透明藍（水面比河岸低，看得出河道下陷），地圖邊緣的水也有側面。</summary>
    private void BuildWater()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (var c = 0; c < IsoProtoMap.Columns; c++)
            for (var r = 0; r < IsoProtoMap.Rows; r++)
            {
                if (!Map.IsWater(c, r))
                    continue;
                var deepSea = Map.TerrainAt(c, r) == IsoTerrain.Sea && c == 0;
                var color = UsesKit ? new Color(KitColor(72, 152, 214), deepSea ? 0.92f : 0.86f)
                    : Map.TerrainAt(c, r) == IsoTerrain.River ? new Color(0.30f, 0.62f, 0.80f, 0.82f)
                    : deepSea ? new Color(0.16f, 0.42f, 0.70f, 0.86f) : new Color(0.24f, 0.56f, 0.78f, 0.84f);
                var y = IsoProtoMap.WaterLevel;
                var v = new[] { new Vector3(c, y, r), new Vector3(c + 1, y, r), new Vector3(c + 1, y, r + 1), new Vector3(c, y, r), new Vector3(c + 1, y, r + 1), new Vector3(c, y, r + 1) };
                foreach (var p in v)
                {
                    st.SetColor(color);
                    st.SetNormal(Vector3.Up);
                    st.AddVertex(p);
                }
                if (c == 0 || r == 0 || r == IsoProtoMap.Rows - 1)
                {
                    // 地圖邊緣的水：前方再補一片水的側面（看起來像切開的模型）。
                    var (a, b, n) = c == 0 ? (new Vector3(c, 0, r + 1), new Vector3(c, 0, r), Vector3.Left)
                        : r == 0 ? (new Vector3(c, 0, r), new Vector3(c + 1, 0, r), Vector3.Forward)
                        : (new Vector3(c + 1, 0, r + 1), new Vector3(c, 0, r + 1), Vector3.Back);
                    var bed = Map.TopOf(c, r);
                    foreach (var p in new[] { a + Vector3.Up * y, b + Vector3.Up * y, b + Vector3.Up * bed, a + Vector3.Up * y, b + Vector3.Up * bed, a + Vector3.Up * bed })
                    {
                        st.SetColor(new Color(0.20f, 0.48f, 0.72f, 0.8f));
                        st.SetNormal(n);
                        st.AddVertex(p);
                    }
                }
            }
        var material = VertexMaterial();
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        material.Roughness = 0.35f;
        _world.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
    }

    /// <summary>勢力：半透明色塊（蓋上去還看得到下面的地形）＋沿格邊的亮色邊界；河本身不屬於任何勢力，所以兩岸各一條邊界。</summary>
    private void BuildRealms()
    {
        foreach (var realm in new[] { IsoProtoMap.Gold, IsoProtoMap.Blue })
        {
            var color = realm == IsoProtoMap.Gold ? GoldColor : BlueColor;
            var fill = new SurfaceTool();
            fill.Begin(Mesh.PrimitiveType.Triangles);
            var line = new SurfaceTool();
            line.Begin(Mesh.PrimitiveType.Triangles);
            void Add(SurfaceTool st, params Vector3[] points)
            {
                foreach (var p in points)
                {
                    st.SetNormal(Vector3.Up);
                    st.AddVertex(p);
                }
            }
            for (var c = 0; c < IsoProtoMap.Columns; c++)
                for (var r = 0; r < IsoProtoMap.Rows; r++)
                {
                    if (Map.RealmAt(c, r) != realm)
                        continue;
                    var y = Map.TopOf(c, r) + 0.07f;
                    Add(fill, new Vector3(c, y, r), new Vector3(c + 1, y, r), new Vector3(c + 1, y, r + 1), new Vector3(c, y, r), new Vector3(c + 1, y, r + 1), new Vector3(c, y, r + 1));
                    const float w = 0.07f;
                    var ly = y + 0.01f;
                    if (Map.RealmAt(c + 1, r) != realm) Add(line, new Vector3(c + 1 - w, ly, r), new Vector3(c + 1, ly, r), new Vector3(c + 1, ly, r + 1), new Vector3(c + 1 - w, ly, r), new Vector3(c + 1, ly, r + 1), new Vector3(c + 1 - w, ly, r + 1));
                    if (Map.RealmAt(c - 1, r) != realm) Add(line, new Vector3(c, ly, r), new Vector3(c + w, ly, r), new Vector3(c + w, ly, r + 1), new Vector3(c, ly, r), new Vector3(c + w, ly, r + 1), new Vector3(c, ly, r + 1));
                    if (Map.RealmAt(c, r + 1) != realm) Add(line, new Vector3(c, ly, r + 1 - w), new Vector3(c + 1, ly, r + 1 - w), new Vector3(c + 1, ly, r + 1), new Vector3(c, ly, r + 1 - w), new Vector3(c + 1, ly, r + 1), new Vector3(c, ly, r + 1));
                    if (Map.RealmAt(c, r - 1) != realm) Add(line, new Vector3(c, ly, r), new Vector3(c + 1, ly, r), new Vector3(c + 1, ly, r + w), new Vector3(c, ly, r), new Vector3(c + 1, ly, r + w), new Vector3(c, ly, r + w));
                }
            _realms.AddChild(new MeshInstance3D { Mesh = fill.Commit(), MaterialOverride = Material(new Color(color, 0.26f), unshaded: true), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            _realms.AddChild(new MeshInstance3D { Mesh = line.Commit(), MaterialOverride = Material(new Color(color.Lightened(0.15f), 0.95f), unshaded: true), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    /// <summary>戰略層（拉遠）：建築與樹改成色塊——聚落佔的格是一塊勢力色的厚方塊。</summary>
    private void BuildStrategic()
    {
        foreach (var s in Map.Settlements)
        {
            var color = (s.Realm == IsoProtoMap.Gold ? GoldColor : BlueColor).Darkened(0.25f);
            foreach (var cell in s.Cells)
                Box(_strategic, new Vector3(cell.X + 0.5f, Map.TopOf(cell.X, cell.Y) + 0.06f, cell.Y + 0.5f), new Vector3(0.96f, 0.12f, 0.96f), color);
        }
    }

    // ───────────────────────── 鏡頭與圖層 ─────────────────────────

    private void PlaceCamera()
    {
        _size = Math.Clamp(_size, TempMinSize, TempMaxSize);
        _target = new Vector3(Math.Clamp(_target.X, 0f, IsoProtoMap.Columns), 0.3f, Math.Clamp(_target.Z, 0f, IsoProtoMap.Rows));
        _camera.Size = _size;
        // 真等角：從東南上方看（方向 (1,1,1)），俯角 35.26°。
        _camera.LookAtFromPosition(_target + new Vector3(1f, 1f, 1f) * 30f, _target, Vector3.Up);
        var strategic = _size >= TempStrategicSize;
        _detail.Visible = !strategic;
        _trees.Visible = !strategic;
        _strategic.Visible = strategic;
        _sun.ShadowEnabled = _size < TempRegionalSize; // 第 08-20 章：陰影只在近景開
        _realms.Visible = _showRealms?.ButtonPressed ?? true;
    }

    public string LayerName => _size >= TempStrategicSize ? "戰略（色塊）" : _size >= TempRegionalSize ? "區域（無陰影）" : "近景（完整模型＋陰影）";

    public override void _Process(double delta)
    {
        if (_wheel is not null)
            _wheel.RotateObjectLocal(Vector3.Right, (float)delta * 1.2f);
        SpinKit((float)delta);
        _overlay?.QueueRedraw();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelUp }:
                _size /= 1.12f;
                PlaceCamera();
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.WheelDown }:
                _size *= 1.12f;
                PlaceCamera();
                AcceptEvent();
                break;
            case InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click:
                Pick(click.Position);
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when (motion.ButtonMask & (MouseButtonMask.Middle | MouseButtonMask.Right)) != 0:
                // 拖曳：畫面移動多少，地圖就跟著移多少（正交鏡頭：每像素＝Size／視窗高）。
                var perPixel = _size / Math.Max(1f, Size.Y);
                var basis = _camera.GlobalTransform.Basis;
                var right = new Vector3(basis.X.X, 0f, basis.X.Z).Normalized();
                var up = new Vector3(basis.Y.X, 0f, basis.Y.Z).Normalized();
                _target -= right * motion.Relative.X * perPixel - up * motion.Relative.Y * perPixel / 0.8165f; // 地面在畫面上被壓扁 sin(35.26°)
                PlaceCamera();
                AcceptEvent();
                break;
        }
    }

    /// <summary>點選：先打建築（點到屋頂就選它的聚落，不會選到屋頂後面的格），再打地面格。</summary>
    private void Pick(Vector2 screen)
    {
        var origin = _camera.ProjectRayOrigin(screen);
        var direction = _camera.ProjectRayNormal(screen);
        var best = float.MaxValue;
        int? hit = null;
        foreach (var (box, settlement) in _picks)
            if (_detail.Visible && RayBox(origin, direction, box) is { } t && t < best)
            {
                best = t;
                hit = settlement;
            }
        _selectedCell = Map.CellUnderRay(origin, direction);
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

    /// <summary>射線與方塊的交點距離（沒有交到回傳 null）。</summary>
    public static float? RayBox(Vector3 origin, Vector3 direction, Aabb box)
    {
        float tMin = 0f, tMax = float.MaxValue;
        for (var axis = 0; axis < 3; axis++)
        {
            var o = origin[axis];
            var d = direction[axis];
            var lo = box.Position[axis];
            var hi = box.End[axis];
            if (Math.Abs(d) < 1e-6f)
            {
                if (o < lo || o > hi)
                    return null;
                continue;
            }
            var t1 = (lo - o) / d;
            var t2 = (hi - o) / d;
            if (t1 > t2)
                (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax)
                return null;
        }
        return tMin;
    }

    // ───────────────────────── 介面與疊加圖層 ─────────────────────────

    private void BuildPanel()
    {
        var panel = new PanelContainer();
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.12f, 0.10f, 0.08f, 0.85f), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6 });
        AddChild(panel);
        panel.Position = new Vector2(12f, 12f);
        var column = new VBoxContainer();
        panel.AddChild(column);
        column.AddChild(new Label { Text = "3D 等角原型（試看用，不影響世界）　滾輪縮放｜右鍵或中鍵拖曳｜左鍵點選" });
        column.AddChild(new Label { Text = UsesKit ? "模型：KayKit Medieval Hexagon（CC0，Kay Lousberg）" : "模型：程式拼的低多邊形（找不到 KayKit 素材）" });
        var row = new HBoxContainer();
        column.AddChild(row);
        _showRealms = new CheckButton { Text = "勢力", ButtonPressed = true };
        _showRealms.Toggled += _ => PlaceCamera();
        _showIcons = new CheckButton { Text = "工作地圖示", ButtonPressed = true };
        _showWar = new CheckButton { Text = "戰爭示意", ButtonPressed = false };
        row.AddChild(_showRealms);
        row.AddChild(_showIcons);
        row.AddChild(_showWar);
        var back = new Button { Text = "返回地圖" };
        back.Pressed += () => Closed?.Invoke();
        row.AddChild(back);
        _info = new Label { Text = "點聚落或地塊看選到什麼" };
        column.AddChild(_info);
    }

    /// <summary>畫在 3D 畫面上方的 2D 圖層：名牌、工作地圖示、戰爭箭頭與交叉劍、選取外框、目前圖層。</summary>
    private void DrawOverlay(Control canvas)
    {
        {
            var v = this;
            void DrawLine(Vector2 a, Vector2 b, Color color, float width) => canvas.DrawLine(a, b, color, width);
            void DrawRect(Rect2 rect, Color color, bool filled = true, float width = -1f) => canvas.DrawRect(rect, color, filled, width);
            void DrawString(Font f, Vector2 at, string text, HorizontalAlignment align, float w, int size, Color color) => canvas.DrawString(f, at, text, align, w, size, color);
            void DrawColoredPolygon(Vector2[] points, Color color) => canvas.DrawColoredPolygon(points, color);
            void DrawPolyline(Vector2[] points, Color color, float width) => canvas.DrawPolyline(points, color, width);
            void DrawCircle(Vector2 at, float radius, Color color) => canvas.DrawCircle(at, radius, color);
            var Size = canvas.Size;
            if (v._camera is null || !v._camera.IsInsideTree())
                return;
            var font = ThemeDB.FallbackFont;
            Vector2 At(float x, float y, float z) => v._camera.UnprojectPosition(new Vector3(x, y, z));
            // 選取外框：聚落沿佔格的外緣、地塊沿格邊（等角下是菱形）。
            var outline = v._selectedSettlement is { } sid ? v.Map.Settlements.First(s => s.Id == sid).Cells : v._selectedCell is { } one ? new[] { one } : Array.Empty<Vector2I>();
            var set = outline.ToHashSet();
            foreach (var cell in outline)
            {
                var y = v.Map.TopOf(cell.X, cell.Y) + 0.09f;
                if (v.Map.IsWater(cell.X, cell.Y))
                    y = IsoProtoMap.WaterLevel + 0.02f;
                foreach (var (dc, dr, a, b) in new[] { (1, 0, new Vector2(1, 0), new Vector2(1, 1)), (-1, 0, new Vector2(0, 0), new Vector2(0, 1)), (0, 1, new Vector2(0, 1), new Vector2(1, 1)), (0, -1, new Vector2(0, 0), new Vector2(1, 0)) })
                {
                    if (set.Contains(new Vector2I(cell.X + dc, cell.Y + dr)))
                        continue;
                    var p = At(cell.X + a.X, y, cell.Y + a.Y);
                    var q = At(cell.X + b.X, y, cell.Y + b.Y);
                    DrawLine(p, q, new Color(0.1f, 0.08f, 0.05f, 0.8f), 5f);
                    DrawLine(p, q, new Color(1f, 0.95f, 0.35f), 2.5f);
                }
            }
            // 名牌（固定大小，任何縮放都看得清楚）。
            foreach (var s in v.Map.Settlements)
            {
                var p = At(s.Center.X + 0.5f, IsoProtoMap.LandTop + 0.8f, s.Center.Y + 0.5f);
                var text = $"{s.Name}・{s.Kind}";
                var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 15).X + 16f;
                var box = new Rect2(p - new Vector2(width / 2f, 12f), new Vector2(width, 24f));
                DrawRect(box, new Color(0.13f, 0.10f, 0.07f, 0.82f));
                DrawRect(box, s.Realm == IsoProtoMap.Gold ? GoldColor : BlueColor, false, 2f);
                DrawString(font, box.Position + new Vector2(8f, 17f), text, HorizontalAlignment.Left, -1, 15, new Color(0.98f, 0.95f, 0.86f));
            }
            // 工作地圖示：立在工作地上方的對話泡泡（近景、區域才畫）。
            if (v._showIcons.ButtonPressed && v._size < TempStrategicSize)
                foreach (var (at, glyph, _) in v.Map.Icons)
                {
                    var foot = At(at.X, IsoProtoMap.LandTop + 0.35f, at.Y);
                    var head = foot - new Vector2(0f, 26f);
                    DrawColoredPolygon(new[] { foot, head + new Vector2(-6f, 10f), head + new Vector2(6f, 10f) }, new Color(0.98f, 0.95f, 0.86f));
                    var box = new Rect2(head - new Vector2(15f, 15f), new Vector2(30f, 28f));
                    DrawRect(box, new Color(0.98f, 0.95f, 0.86f));
                    DrawRect(box, new Color(0.30f, 0.22f, 0.14f), false, 2f);
                    DrawString(font, box.Position + new Vector2(6f, 21f), glyph, HorizontalAlignment.Left, -1, 18, new Color(0.30f, 0.22f, 0.14f));
                }
            // 戰爭示意：城鎮往農村的紅色弧形箭頭，中段一把交叉劍。
            if (v._showWar.ButtonPressed)
            {
                var from = At(4.6f, IsoProtoMap.LandTop + 0.2f, 6.5f);
                var to = At(8.6f, IsoProtoMap.LandTop + 0.2f, 6.9f);
                var mid = (from + to) / 2f - new Vector2(0f, 40f);
                var points = new List<Vector2>();
                for (var i = 0; i <= 24; i++)
                {
                    var t = i / 24f;
                    points.Add(from * (1 - t) * (1 - t) + mid * 2 * t * (1 - t) + to * t * t);
                }
                DrawPolyline(points.ToArray(), new Color(0.2f, 0.02f, 0.02f, 0.7f), 8f);
                DrawPolyline(points.ToArray(), new Color(0.88f, 0.16f, 0.14f), 5f);
                var dir = (points[^1] - points[^3]).Normalized();
                var side = new Vector2(-dir.Y, dir.X);
                DrawColoredPolygon(new[] { to + dir * 10f, to - dir * 6f + side * 9f, to - dir * 6f - side * 9f }, new Color(0.88f, 0.16f, 0.14f));
                var swords = points[12] - new Vector2(0f, 6f);
                DrawCircle(swords, 15f, new Color(0.10f, 0.07f, 0.05f, 0.85f));
                DrawString(font, swords + new Vector2(-10f, 8f), "⚔", HorizontalAlignment.Left, -1, 22, new Color(0.95f, 0.92f, 0.85f));
            }
            DrawString(font, new Vector2(12f, Size.Y - 14f), $"圖層：{v.LayerName}", HorizontalAlignment.Left, -1, 15, new Color(0.1f, 0.08f, 0.06f));
        }
    }

    /// <summary>冒煙測試：場景建好、建築夠多、點選換算正確（正上方往下點到城鎮中心格）。只讀，不改世界。</summary>
    public string SelfCheck()
    {
        if (!Built || BuildingCount < 30)
            return $"iso prototype built={Built} buildings={BuildingCount}";
        if (UsesKit && KitPlaced < 60)
            return $"iso prototype kaykit placed={KitPlaced}";
        var cell = Map.CellUnderRay(new Vector3(3.5f, 5f, 6.5f), Vector3.Down);
        if (cell != new Vector2I(3, 6) || Map.SettlementAt(3, 6)?.Name != "金柵鎮")
            return $"iso prototype pick wrong: {cell}";
        if (Map.CellUnderRay(new Vector3(7.5f, 5f, 6.5f), Vector3.Down) is not { } river || Map.TerrainAt(river.X, river.Y) != IsoTerrain.River)
            return "iso prototype river pick wrong";
        if (RayBox(new Vector3(0.5f, 5f, 0.5f), Vector3.Down, new Aabb(Vector3.Zero, Vector3.One)) is not { } t || Math.Abs(t - 4f) > 1e-3f)
            return "iso prototype ray-box wrong";
        return "";
    }
}

/// <summary>第 08-20 章：3D 原型上方的 2D 疊加圖層（繪圖交給 <see cref="Painter"/>）。</summary>
public partial class IsoPrototypeOverlay : Control
{
    public Action<Control>? Painter { get; set; }

    public override void _Draw() => Painter?.Invoke(this);
}
