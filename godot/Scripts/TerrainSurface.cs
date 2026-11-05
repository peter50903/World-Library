using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 試玩回饋 2026-11-04 之 3、5、7（森林破碎、地圖模糊、參考 WorldBox）：地表改成 WorldBox 式的像素地圖。
/// 整張地圖預先烤成一張圖（每格半徑 <see cref="TempTexelsPerRadius"/> 個像素），放大時最近鄰取樣——邊緣清楚、不模糊；
/// 畫面上每幀只貼這一張圖（原本每個螢幕像素都跑一次著色器）。
/// 第 08-18b 章：每格 6×6 個方格、沿格邊切開（方格世界，不扭曲）；
/// 顏色是固定的小調色盤（每種地形 2～3 個深淺，依位置雜訊挑），海依離岸距離分深淺、海邊一圈沙灘；
/// 森林＝深綠底色＋一棵棵樹疊上去，丘陵＝底色＋小山頭，田地＝一塊塊田畦，開墾中依開發度從草地→泥土→田畦。
/// 天災疊層（火、焦土、乾旱、洪水、礦脈）也烤進圖裡；只有改變的格重畫。只影響畫面；格的歸屬、點選、規則依方格（第 08-18a 章）。
/// </summary>
public partial class TerrainSurface : Control
{
    /// <summary>地表代碼（每格一個位元組）。</summary>
    public enum Code : byte
    {
        Grassland = 0, Forest = 1, Hills = 2, HillsOre = 3, Sea = 4, River = 5, Shallow = 6,
        Farmland = 7, Clearing = 8, Burning = 9, Scorched = 10, DryGrass = 11,
        // 第 08-16 章第 14 節：砍過的森林（第 4 個位元組＝長回進度）、開採中的礦脈（＝開採進度）、挖空的礦坑。
        Logged = 12, HillsMining = 13, MinePit = 14
    }

    /// <summary>【TEMP】每格半徑幾個圖像素；地塊交界的扭曲幅度（格半徑的倍數）。</summary>
    public const int TempTexelsPerRadius = 24;

    /// <summary>程式畫的底色與樹（素材到之前）每個「大像素」佔幾個圖像素。試玩回饋 2026-11-04：像素太低、放大沒細節——
    /// 解析度由 10 提高到 24，像素零件素材以實際像素疊上；程式畫的部分維持原本的方塊大小。</summary>
    private const int K = TempTexelsPerRadius / 10;

    /// <summary>【TEMP】圖分塊上傳（每塊邊長）：只有改變的塊重新上傳。</summary>
    private const int TempChunk = 512;

    /// <summary>第 08-18a 章：方格世界，一格邊長＝2 個格半徑；地圖左上角（相對第 0 格中心，以格半徑為單位）。</summary>
    private static readonly Vector2 Corner = new(-1f, -1f);

    private ImageTexture?[,] _chunks = new ImageTexture?[0, 0];
    private ImageTexture?[,] _detailChunks = new ImageTexture?[0, 0];
    private bool[,] _dirty = new bool[0, 0];
    private byte[]? _pixels;
    private byte[]? _detail; // 試玩回饋 2026-11-05 之 6：草叢、野花、小石頭另一層，遠看不畫
    private bool _toDetail;

    /// <summary>【TEMP】細節層（草叢、野花、小石頭）在格半徑至少幾像素時才畫；整體調色（飽和度、亮度倍率）。</summary>
    public const float TempDetailMinRadius = 26f;
    public const float TempSaturation = 0.78f;
    public const float TempBrightness = 0.9f;
    private byte[]? _uploaded;
    private byte[]? _baked; // 上次烤圖時每格的四個位元組
    private int[]? _owner; // 每個圖像素屬於哪一格（-1＝地圖外）
    private byte[]? _seaDist; // 陸地像素離海幾格（最多 7）；水的像素離陸地幾格
    private byte[]? _waterDist; // 陸地像素離任何水幾格
    private int _columns, _rows, _width, _height;
    private Vector2 _origin;
    private float _radius;

    /// <summary>目前是否畫地表（近景層與區域層的地形模式）。</summary>
    public bool Active { get; private set; }

    public TerrainSurface()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ShowBehindParent = true;
    }

    /// <summary>每次地圖重畫時呼叫：<paramref name="codes"/> 每格四個位元組（地表代碼、天災疊層、疊層強度、開發度），
    /// 依「列×欄數＋欄」排列；同一個陣列不重複處理，只重畫改變的格。</summary>
    public void Show(byte[] codes, int columns, int rows, Vector2 origin, float radius, IReadOnlyDictionary<int, byte>? ground = null)
    {
        ground ??= NoGround;
        if (!ReferenceEquals(codes, _uploaded) || columns != _columns || rows != _rows || !ReferenceEquals(ground, _ground))
        {
            Bake(codes, columns, rows, ground);
            _uploaded = codes;
        }
        _origin = origin;
        _radius = radius;
        // 放大時最近鄰（像素清楚）；縮小到一個圖像素不到一個螢幕像素時改線性，避免閃爍。
        TextureFilter = radius >= TempTexelsPerRadius ? TextureFilterEnum.Nearest : TextureFilterEnum.Linear;
        Active = true;
        QueueRedraw();
    }

    public void Hide()
    {
        if (!Active)
            return;
        Active = false;
        QueueRedraw();
    }

    public override void _Ready() => Resized += QueueRedraw;

    public override void _Draw()
    {
        if (!Active)
            return;
        var scale = _radius / TempTexelsPerRadius;
        var topLeft = _origin + Corner * _radius;
        var view = new Rect2(Vector2.Zero, Size);
        for (var cy = 0; cy < _chunks.GetLength(1); cy++)
            for (var cx = 0; cx < _chunks.GetLength(0); cx++)
            {
                if (_chunks[cx, cy] is not { } chunk)
                    continue;
                var rect = new Rect2(topLeft + new Vector2(cx, cy) * TempChunk * scale, chunk.GetSize() * scale);
                if (!rect.Intersects(view))
                    continue;
                DrawTextureRect(chunk, rect, false);
                if (_radius >= TempDetailMinRadius && _detailChunks[cx, cy] is { } detail)
                    DrawTextureRect(detail, rect, false);
            }
    }

    // ───────────── 選取：沿像素邊緣 ─────────────

    private readonly Dictionary<int, Vector2[]> _outlines = new();

    /// <summary>螢幕位置落在哪一格（依地表實際的像素歸屬；地表沒畫或在地圖外回傳 -1）。</summary>
    public int CellAtScreen(Vector2 screen)
    {
        if (!Active || _owner is null)
            return -1;
        var t = (screen - (_origin + Corner * _radius)) / (_radius / TempTexelsPerRadius);
        var x = Mathf.FloorToInt(t.X);
        var y = Mathf.FloorToInt(t.Y);
        return x < 0 || y < 0 || x >= _width || y >= _height ? -1 : _owner[y * _width + x];
    }

    /// <summary>試玩回饋 2026-11-04 之 4：選取框沿這一格實際的像素邊緣（成對的端點，螢幕座標），不再是扭曲的六邊形。</summary>
    public Vector2[] OutlineOf(int cell)
    {
        if (!Active || _owner is null)
            return Array.Empty<Vector2>();
        if (!_outlines.TryGetValue(cell, out var segments))
            _outlines[cell] = segments = BuildOutline(cell);
        var scale = _radius / TempTexelsPerRadius;
        var topLeft = _origin + Corner * _radius;
        var screen = new Vector2[segments.Length];
        for (var i = 0; i < segments.Length; i++)
            screen[i] = topLeft + segments[i] * scale;
        return screen;
    }

    private Vector2[] BuildOutline(int cell)
    {
        var c = TexelOf(CenterOf(cell % _columns, cell / _columns));
        var reach = TempTexelsPerRadius * 2;
        int x0 = Math.Max(0, (int)c.X - reach), x1 = Math.Min(_width, (int)c.X + reach);
        int y0 = Math.Max(0, (int)c.Y - reach), y1 = Math.Min(_height, (int)c.Y + reach);
        bool Own(int x, int y) => x >= 0 && y >= 0 && x < _width && y < _height && _owner![y * _width + x] == cell;
        var points = new List<Vector2>();
        // 橫邊：每列把連續的邊接成一段；直邊同理。
        for (var y = y0; y <= y1; y++)
        {
            var run = -1;
            for (var x = x0; x <= x1; x++)
            {
                var edge = x < x1 && Own(x, y) != Own(x, y - 1);
                if (edge && run < 0)
                    run = x;
                else if (!edge && run >= 0)
                {
                    points.Add(new Vector2(run, y));
                    points.Add(new Vector2(x, y));
                    run = -1;
                }
            }
        }
        for (var x = x0; x <= x1; x++)
        {
            var run = -1;
            for (var y = y0; y <= y1; y++)
            {
                var edge = y < y1 && Own(x, y) != Own(x - 1, y);
                if (edge && run < 0)
                    run = y;
                else if (!edge && run >= 0)
                {
                    points.Add(new Vector2(x, run));
                    points.Add(new Vector2(x, y));
                    run = -1;
                }
            }
        }
        return points.ToArray();
    }

    // ───────────── 第 08-17 章：方格與聚落地面 ─────────────

    private static readonly IReadOnlyDictionary<int, byte> NoGround = new Dictionary<int, byte>();
    private IReadOnlyDictionary<int, byte> _ground = NoGround; // 方格編號（列×方格欄數＋欄）→ 聚落地面種類
    private bool _gridChanged = true;

    /// <summary>聚落地面種類（第 08-17 章第 4 節）。</summary>
    public const byte GroundPlazaDirt = 1, GroundPlazaStone = 2, GroundRoad = 3, GroundYard = 4, GroundCobble = 5, GroundStreet = 6;

    /// <summary>方格欄數、列數。</summary>
    public int TilesX => (_width + TempTile - 1) / TempTile;
    public int TilesY => (_height + TempTile - 1) / TempTile;

    /// <summary>依地圖大小建好方格歸屬（不需要地表代碼；聚落版面要先知道方格屬於哪一格）。</summary>
    public void EnsureGrid(int columns, int rows)
    {
        if (_owner is not null && columns == _columns && rows == _rows)
            return;
        _columns = columns;
        _rows = rows;
        _width = columns * 2 * TempTexelsPerRadius; // 第 08-18b 章：一格 6×6 方格、每方格 8×8 圖像素
        _height = rows * 2 * TempTexelsPerRadius;
        BuildOwners();
        _gridChanged = true;
    }

    /// <summary>方格屬於哪一格（地圖外 -1）。</summary>
    public int TileOwner(int tx, int ty)
    {
        var x = tx * TempTile + TempTile / 2;
        var y = ty * TempTile + TempTile / 2;
        return _owner is null || tx < 0 || ty < 0 || x >= _width || y >= _height ? -1 : _owner[y * _width + x];
    }

    /// <summary>格中心所在的方格。</summary>
    public static Vector2I TileOfCell(int column, int row)
    {
        var t = TexelOf(CenterOf(column, row));
        return new Vector2I((int)t.X / TempTile, (int)t.Y / TempTile);
    }

    /// <summary>圖像素座標在畫面上的位置（<paramref name="origin"/>＝第 0 格中心的畫面位置）。</summary>
    public static Vector2 TexelToScreen(Vector2 texel, Vector2 origin, float radius)
        => origin + Corner * radius + texel * (radius / TempTexelsPerRadius);

    private int TileIndexOf(int x, int y) => y / TempTile * TilesX + x / TempTile;

    private Color GroundColor(byte kind, int x, int y)
    {
        var h = Hash(x / K, y / K, 31);
        var ix = x % TempTile;
        var iy = y % TempTile;
        return kind switch
        {
            GroundPlazaStone => (x / 4 + y / 4) % 2 == 0 ? Paving[0] : Paving[1], // 石板廣場（4×4 小石板交錯）
            GroundCobble => ix == 0 || iy == 0 ? Paving[2] : Pick(Paving, h * 0.66f), // 城內石子路
            GroundStreet => h < 0.15f ? Paving[2] : Paving[1],
            GroundRoad => h < 0.12f ? Soil[0] : Pick(RoadDirt, h), // 土路
            GroundYard => Pick(Yard, h),
            _ => h < 0.1f ? Soil[1] : Pick(Yard, h) // 夯土廣場
        };
    }

    private static readonly Color[] Paving = { new("b9b2a0"), new("a7a08f"), new("8c8576") };
    private static readonly Color[] RoadDirt = { new("a8875a"), new("b39263") };
    private static readonly Color[] Yard = { new("9c7c52"), new("a6865a"), new("94754c") };

    // ───────────── 烤圖 ─────────────

    private static bool IsWater(byte code) => code is (byte)Code.Sea or (byte)Code.River or (byte)Code.Shallow;

    /// <summary>格中心（以格半徑為單位，原點＝第 0 格中心）。</summary>
    private static Vector2 CenterOf(int column, int row) => new(2f * column, 2f * row);

    private void Bake(byte[] codes, int columns, int rows, IReadOnlyDictionary<int, byte> ground)
    {
        EnsureGrid(columns, rows);
        var full = _pixels is null || _baked is null || _gridChanged;
        var oldGround = _ground;
        _ground = ground;
        if (full)
        {
            _gridChanged = false;
            _pixels = new byte[_width * _height * 4];
            _detail = new byte[_width * _height * 4];
            foreach (var old in _chunks)
                old?.Dispose();
            foreach (var old in _detailChunks)
                old?.Dispose();
            _chunks = new ImageTexture?[(_width + TempChunk - 1) / TempChunk, (_height + TempChunk - 1) / TempChunk];
            _detailChunks = new ImageTexture?[_chunks.GetLength(0), _chunks.GetLength(1)];
            _dirty = new bool[_chunks.GetLength(0), _chunks.GetLength(1)];
            _outlines.Clear();
        }
        // 水陸分布變了（換地圖）才重算離岸距離並整張重畫；平常只重畫改變的格與周圍。
        var waterChanged = full;
        if (!full)
            for (var i = 0; i < columns * rows && !waterChanged; i++)
                waterChanged = IsWater(codes[i * 4]) != IsWater(_baked![i * 4]);
        var painted = waterChanged;
        if (waterChanged)
        {
            BuildDistances(codes);
            PaintRect(codes, 0, 0, _width, _height);
        }
        else
        {
            var margin = TempTexelsPerRadius * 3; // 第 08-17 章：田區跨格，重畫範圍多留一塊田的寬度
            var changed = new HashSet<int>();
            for (var i = 0; i < columns * rows; i++)
                if (codes[i * 4] != _baked![i * 4] || codes[i * 4 + 1] != _baked[i * 4 + 1]
                    || codes[i * 4 + 2] != _baked[i * 4 + 2] || codes[i * 4 + 3] != _baked[i * 4 + 3])
                    changed.Add(i);
            // 第 08-17 章：聚落地面改變的方格，重畫它所屬的格。
            if (!ReferenceEquals(oldGround, ground))
            {
                foreach (var (tile, kind) in ground)
                    if (!oldGround.TryGetValue(tile, out var was) || was != kind)
                        changed.Add(TileOwner(tile % TilesX, tile / TilesX));
                foreach (var tile in oldGround.Keys)
                    if (!ground.ContainsKey(tile))
                        changed.Add(TileOwner(tile % TilesX, tile / TilesX));
                changed.Remove(-1);
            }
            if (changed.Count > TempFullRepaintCells)
                PaintRect(codes, 0, 0, _width, _height); // 一次改太多格（例如剛讀檔）時整張重畫比逐格快
            else
                foreach (var i in changed.OrderBy(i => i))
                {
                    var c = TexelOf(CenterOf(i % columns, i / columns));
                    PaintRect(codes, (int)c.X - margin, (int)c.Y - margin, (int)c.X + margin, (int)c.Y + margin);
                }
            painted = changed.Count > 0;
        }
        _baked = (byte[])codes.Clone();
        if (painted)
            Upload();
    }

    /// <summary>只把重畫過的塊重新上傳。</summary>
    private void Upload()
    {
        for (var cy = 0; cy < _dirty.GetLength(1); cy++)
            for (var cx = 0; cx < _dirty.GetLength(0); cx++)
            {
                if (!_dirty[cx, cy])
                    continue;
                _dirty[cx, cy] = false;
                var x0 = cx * TempChunk;
                var y0 = cy * TempChunk;
                var w = Math.Min(TempChunk, _width - x0);
                var h = Math.Min(TempChunk, _height - y0);
                UploadChunk(_pixels!, _chunks, cx, cy, x0, y0, w, h);
                UploadChunk(_detail!, _detailChunks, cx, cy, x0, y0, w, h);
            }
    }

    private void UploadChunk(byte[] source, ImageTexture?[,] chunks, int cx, int cy, int x0, int y0, int w, int h)
    {
        var bytes = new byte[w * h * 4];
        for (var y = 0; y < h; y++)
            Array.Copy(source, ((y0 + y) * _width + x0) * 4, bytes, y * w * 4, w * 4);
        using var image = Image.CreateFromData(w, h, false, Image.Format.Rgba8, bytes);
        if (chunks[cx, cy] is { } chunk)
            chunk.Update(image);
        else
            chunks[cx, cy] = ImageTexture.CreateFromImage(image);
    }

    private static Vector2 TexelOf(Vector2 world) => (world - Corner) * TempTexelsPerRadius;

    private Vector2 WorldOf(int x, int y) => Corner + new Vector2(x + 0.5f, y + 0.5f) / TempTexelsPerRadius;

    /// <summary>點落在哪一格（第 08-18a 章方格：沿格邊切開，沒有扭曲）；地圖外回傳 -1。</summary>
    private int CellAt(Vector2 p)
    {
        var column = Mathf.FloorToInt((p.X + 1f) / 2f);
        var row = Mathf.FloorToInt((p.Y + 1f) / 2f);
        return column < 0 || column >= _columns || row < 0 || row >= _rows ? -1 : row * _columns + column;
    }

    /// <summary>
    /// 第 08-18b 章第 1 節：畫面方格（正方形小格）的邊長（圖像素）。一個規則格＝6×6 方格；
    /// 方格整塊屬於某一格（沿格邊切開，沒有扭曲），田地、房屋、道路對齊方格。
    /// </summary>
    public const int TempTile = 8;

    private void BuildOwners()
    {
        _owner = new int[_width * _height];
        for (var ty = 0; ty * TempTile < _height; ty++)
            for (var tx = 0; tx * TempTile < _width; tx++)
            {
                // 第 08-18b 章：方格中心所在的規則格（不扭曲；自然交界的參差之後另做成裝飾，不改歸屬）。
                var owner = CellAt(WorldOf(tx * TempTile + TempTile / 2, ty * TempTile + TempTile / 2));
                for (var y = ty * TempTile; y < Math.Min(_height, (ty + 1) * TempTile); y++)
                    for (var x = tx * TempTile; x < Math.Min(_width, (tx + 1) * TempTile); x++)
                        _owner[y * _width + x] = owner;
            }
    }

    /// <summary>離岸距離（四方向逐格擴散，最多 7）：陸地離海、陸地離水、水離陸地。</summary>
    private void BuildDistances(byte[] codes)
    {
        var count = _width * _height;
        _seaDist = new byte[count];
        _waterDist = new byte[count];
        var landDist = new byte[count];
        Spread(_seaDist, i => _owner![i] >= 0 && codes[_owner[i] * 4] == (byte)Code.Sea);
        Spread(_waterDist, i => _owner![i] >= 0 && IsWater(codes[_owner[i] * 4]));
        Spread(landDist, i => _owner![i] >= 0 && !IsWater(codes[_owner[i] * 4]));
        // 水的像素存離陸地的距離（海的深淺）。
        for (var i = 0; i < count; i++)
            if (_owner![i] >= 0 && IsWater(codes[_owner[i] * 4]))
                _seaDist[i] = landDist[i];
    }

    private void Spread(byte[] dist, Func<int, bool> source)
    {
        Array.Fill(dist, (byte)15);
        var queue = new Queue<int>();
        for (var i = 0; i < dist.Length; i++)
            if (source(i))
            {
                dist[i] = 0;
                queue.Enqueue(i);
            }
        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            if (dist[i] >= 14)
                continue;
            var x = i % _width;
            void Try(int n)
            {
                if (dist[n] > dist[i] + 1)
                {
                    dist[n] = (byte)(dist[i] + 1);
                    queue.Enqueue(n);
                }
            }
            if (x > 0) Try(i - 1);
            if (x < _width - 1) Try(i + 1);
            if (i >= _width) Try(i - _width);
            if (i < dist.Length - _width) Try(i + _width);
        }
    }

    /// <summary>【TEMP】一次改變超過這麼多格就整張重畫。</summary>
    private const int TempFullRepaintCells = 400;

    /// <summary>重畫一塊矩形：先畫底色，再把中心在附近的格的樹、山頭疊上去（裁在矩形內，結果與整張重畫相同）。</summary>
    private void PaintRect(byte[] codes, int x0, int y0, int x1, int y1)
    {
        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(_width, x1);
        y1 = Math.Min(_height, y1);
        if (x0 >= x1 || y0 >= y1)
            return;
        for (var cy = y0 / TempChunk; cy <= (y1 - 1) / TempChunk; cy++)
            for (var cx = x0 / TempChunk; cx <= (x1 - 1) / TempChunk; cx++)
                _dirty[cx, cy] = true;
        for (var y = y0; y < y1; y++)
        {
            Array.Clear(_detail!, (y * _width + x0) * 4, (x1 - x0) * 4);
            for (var x = x0; x < x1; x++)
                Put(x, y, Ground(codes, x, y));
        }
        var reach = TempTexelsPerRadius * 2;
        for (var row = 0; row < _rows; row++)
            for (var column = 0; column < _columns; column++)
            {
                var c = TexelOf(CenterOf(column, row));
                if (c.X < x0 - reach || c.X > x1 + reach || c.Y < y0 - reach || c.Y > y1 + reach)
                    continue;
                StampCell(codes, row * _columns + column, c, new Rect2I(x0, y0, x1 - x0, y1 - y0));
            }
    }

    private void Put(int x, int y, Color c)
    {
        // 試玩回饋 2026-11-05 之 7（畫面刺眼）：自然景觀整體降飽和、略調暗；建築另外畫、保持原色，人類聚落因此更顯眼（之 8、13）。
        var gray = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
        c = new Color(Mathf.Lerp(gray, c.R, TempSaturation) * TempBrightness, Mathf.Lerp(gray, c.G, TempSaturation) * TempBrightness,
            Mathf.Lerp(gray, c.B, TempSaturation) * TempBrightness, c.A);
        var i = (y * _width + x) * 4;
        var target = _toDetail ? _detail! : _pixels!;
        target[i] = (byte)(c.R * 255f);
        target[i + 1] = (byte)(c.G * 255f);
        target[i + 2] = (byte)(c.B * 255f);
        target[i + 3] = (byte)(c.A * 255f);
    }

    // 【TEMP】WorldBox 式調色盤（亮、飽和、每種 2～3 個深淺）。
    private static readonly Color[] Grass = { new("4f9a34"), new("5aa83c"), new("67b649") };
    private static readonly Color[] ForestFloor = { new("2f6a26"), new("367429") };
    private static readonly Color[] HillGround = { new("7d7150"), new("8a7d58"), new("978a62") };
    private static readonly Color[] SeaShades = { new("6aaee0"), new("5793d0"), new("4a82c4"), new("3b72b8"), new("335fa6") };
    private static readonly Color[] RiverShades = { new("63a3dc"), new("4f8fd0") };
    private static readonly Color[] Sand = { new("d8c07a"), new("e3cf8a") };
    private static readonly Color[] Soil = { new("7a4e2a"), new("8a5a30"), new("94653a") };
    private static readonly Color[] Crops = { new("d8b84a"), new("8cbf3c"), new("c9a63c"), new("a5c94a") };
    private static readonly Color[] Dry = { new("a8923f"), new("b7a24f") };
    private static readonly Color[] Ash = { new("2c2624"), new("3b3330"), new("54504b") };
    private static readonly Color Ember = new("ff8a1e"), EmberDark = new("d9481a"), FloodWater = new("5b98d4"), OreSpeck = new("e0aa40");
    private static readonly Color Stone = new("a7a596"), Stump = new("5a3a1e");

    private static Color Pick(Color[] shades, float t) => shades[Math.Clamp((int)(t * shades.Length), 0, shades.Length - 1)];

    /// <summary>一個圖像素的底色（地形、離岸距離、開發度、天災疊層）。</summary>
    private Color Ground(byte[] codes, int x, int y)
    {
        var i = y * _width + x;
        var owner = _owner![i];
        if (owner < 0)
            return new Color(0, 0, 0, 0);
        var code = codes[owner * 4];
        var overlay = codes[owner * 4 + 1];
        var amount = codes[owner * 4 + 2] / 255f;
        var dev = codes[owner * 4 + 3] / 255f;
        // 第 08-17 章第 4 節：聚落的廣場、道路、屋前空地（對齊方格；天災疊層照樣蓋上去）。
        var groundKind = _ground.Count > 0 && _ground.TryGetValue(TileIndexOf(x, y), out var gk) ? gk : (byte)0;
        var bx = x / K;
        var by = y / K;
        var h = Hash(bx, by, 1);
        // 斑塊：大範圍平滑雜訊＋一點點逐像素變化（像素風的草地，不是噪點）。
        var patch = Mathf.Clamp(Noise(bx / 9f, by / 9f, 3) * 0.85f + h * 0.3f - 0.07f, 0f, 0.999f);
        Color c;
        switch ((Code)code)
        {
            case Code.Sea:
                c = SeaShades[Math.Clamp((_seaDist![i] - 1) / K + (h < 0.08f ? -1 : 0), 0, SeaShades.Length - 1)];
                break;
            case Code.River:
                c = _seaDist![i] <= K ? RiverShades[0] : RiverShades[1];
                break;
            case Code.Shallow:
                c = h < 0.12f ? Stone : RiverShades[0];
                break;
            default:
                if (groundKind != 0)
                {
                    c = GroundColor(groundKind, x, y);
                    break;
                }
                if (_seaDist![i] <= 2 * K && (_seaDist[i] <= K || h < 0.5f))
                {
                    c = Pick(Sand, patch); // 海邊一圈沙灘
                    break;
                }
                c = (Code)code switch
                {
                    Code.Forest or Code.Logged => Pick(ForestFloor, patch),
                    Code.Hills or Code.HillsOre or Code.HillsMining or Code.MinePit => Pick(HillGround, patch),
                    // 第 08-17 章第 4 節：田地是對齊方格的長方形田區，相鄰的田連成大片（不再依六角形或雜訊切）。
                    Code.Farmland => Plot(codes, x, y, 1f) ?? Pick(Grass, patch),
                    Code.Clearing when codes[owner * 4 + 3] == SettlementGround => Pick(Grass, patch), // 第 08-17 章：聚落格裡廣場、道路、屋子以外是草地
                    Code.Clearing => dev > 0f ? Plot(codes, x, y, dev) ?? Pick(Grass, patch) : Pick(Soil, patch),
                    Code.DryGrass => Pick(Dry, patch),
                    Code.Burning or Code.Scorched => Pick(Ash, patch),
                    _ => Pick(Grass, patch)
                };
                if (_waterDist![i] is >= 1 and <= K && code != (byte)Code.Farmland)
                    c = c.Darkened(0.12f); // 河岸一圈稍暗
                if ((code == (byte)Code.HillsOre || code == (byte)Code.HillsMining && Hash(bx, by, 10) > dev) && Hash(bx, by, 9) < 0.05f)
                    c = OreSpeck;
                break;
        }
        // 天災疊層：依強度把一部分像素換掉（像素風的漸變）。
        var hit = Hash(bx, by, 5) < amount;
        switch ((DemoHexMap.Overlay)overlay)
        {
            case DemoHexMap.Overlay.Fire when hit:
                c = Hash(bx, by, 6) < 0.25f ? (Hash(bx, by, 7) < 0.5f ? Ember : EmberDark) : Pick(Ash, patch);
                break;
            case DemoHexMap.Overlay.Scorch when hit && !IsWater(code):
                c = Pick(Ash, patch);
                break;
            case DemoHexMap.Overlay.Dry when hit && !IsWater(code):
                c = Pick(Dry, patch);
                break;
            case DemoHexMap.Overlay.Flood when hit:
                c = FloodWater;
                break;
            case DemoHexMap.Overlay.Ore when Hash(bx, by, 9) < 0.06f:
                c = OreSpeck;
                break;
            case DemoHexMap.Overlay.Debris or DemoHexMap.Overlay.Cracks or DemoHexMap.Overlay.Crater when hit && Hash(bx, by, 8) < 0.4f:
                c = c.Darkened(0.4f);
                break;
        }
        return c;
    }

    /// <summary>與 DemoHexMap 的 TempSettlementGround 相同：有房子的格（聚落空地）。</summary>
    public const byte SettlementGround = 100;

    /// <summary>
    /// 第 08-18b 章第 3 節：田區。全圖共用的田格——每 3×3 方格一區，其中 2×2 方格是田、右邊一行與下面一列是田埂；
    /// 一格（6×6 方格）剛好 2×2 塊田，相鄰的農田格連成大片。一塊田只在它的方格都屬於田（農田或開墾中）時才畫。
    /// <paramref name="dev"/>＜1（開墾中）：依進度一塊塊從草地→翻土→播種→綠苗。作物顏色每塊一種，畦溝橫向深淺相間。
    /// 回傳 null＝這個像素不是田也不是田埂（畫草地）。
    /// </summary>
    public const int TempPlotTilesX = 3, TempPlotTilesY = 3; // 第 08-18b 章：一格 6×6 方格＝2×2 塊田（每塊 2×2 是田、右一行與下一列是田埂）

    private Color? Plot(byte[] codes, int x, int y, float dev)
    {
        var tx = x / TempTile;
        var ty = y / TempTile;
        var px = tx / TempPlotTilesX;
        var py = ty / TempPlotTilesY;
        var lx = tx - px * TempPlotTilesX;
        var ly = ty - py * TempPlotTilesY;
        bool FieldTile(int ax, int ay)
        {
            var cx = ax * TempTile + TempTile / 2;
            var cy = ay * TempTile + TempTile / 2;
            if (cx >= _width || cy >= _height)
                return false;
            var o = _owner![cy * _width + cx];
            return o >= 0 && (codes[o * 4] == (byte)Code.Farmland
                || codes[o * 4] == (byte)Code.Clearing && codes[o * 4 + 3] > 0 && codes[o * 4 + 3] != SettlementGround);
        }
        bool Whole(int bx, int by)
        {
            for (var j = 0; j < TempPlotTilesY - 1; j++)
                for (var i = 0; i < TempPlotTilesX - 1; i++)
                    if (!FieldTile(bx * TempPlotTilesX + i, by * TempPlotTilesY + j))
                        return false;
            return true;
        }
        if (lx == TempPlotTilesX - 1 || ly == TempPlotTilesY - 1)
        {
            // 田埂：本身在田裡、且兩旁至少有一塊完整的田才畫成田埂，否則是草地。
            var beside = FieldTile(tx, ty) && (Whole(px, py) || lx == TempPlotTilesX - 1 && Whole(px + 1, py) || ly == TempPlotTilesY - 1 && Whole(px, py + 1));
            return beside ? Ridge : null;
        }
        if (!Whole(px, py))
            return null;
        // 一塊田只有一個階段：依田區左上方格所屬的格（跨兩格的田不會一半成熟、一半翻土）。
        var first = _owner![(py * TempPlotTilesY * TempTile + TempTile / 2) * _width + px * TempPlotTilesX * TempTile + TempTile / 2];
        dev = codes[first * 4] == (byte)Code.Farmland ? 1f : codes[first * 4 + 3] / 255f;
        // 田區外框 1 像素（畦邊），內部依階段上色。
        var ix = x - px * TempPlotTilesX * TempTile;
        var iy = y - py * TempPlotTilesY * TempTile;
        var edge = ix == 0 || iy == 0 || ix == (TempPlotTilesX - 1) * TempTile - 1 || iy == (TempPlotTilesY - 1) * TempTile - 1;
        var p = Hash(px, py, 13);
        var furrow = iy / 2 % 2 == 0;
        Color c;
        if (dev >= 1f)
        {
            var crop = p < 0.65f ? Crops[0] : p < 0.85f ? Crops[2] : Crops[1];
            c = furrow ? crop : crop.Darkened(0.12f);
        }
        else if (p > dev * 1.6f)
            return Pick(Grass, Hash(x / K, y / K, 3)); // 還沒開墾到這一塊
        else if (dev < 0.5f)
            c = furrow ? Soil[1] : Soil[2]; // 翻土
        else if (dev < 0.8f)
            c = furrow && Hash(x / K, y / K, 14) < 0.35f ? Crops[1] : (furrow ? Soil[1] : Soil[2]); // 播種、冒出嫩苗
        else
            c = furrow ? Crops[1] : Crops[1].Darkened(0.12f); // 綠苗
        return edge ? c.Darkened(0.22f) : c;
    }

    private static readonly Color Ridge = new("6f8a3a");

    // ───────────── 疊在底色上的樹與山頭 ─────────────

    // 圖樣：每列由上而下；. 空、d 暗、m 中、l 亮、t 樹幹、s 影。錨點＝底部中央。
    private static readonly string[] RoundTree = { ".mml.", "mmmll", "dmmml", "ddmmd", ".ddd.", "..t.." };
    private static readonly string[] PineTree = { "..l..", ".mml.", ".mmm.", "mmmlm", "ddmdd", "..t.." };
    private static readonly string[] Bush = { "mlm", "ddd" };
    private static readonly string[] Peak = { "...l...", "..llm..", ".llmms.", "lmmmmss" };
    private static readonly string[] BigPeak = { "....l....", "...llm...", "..lllms..", ".llmmmss.", "lmmmmmmss" };

    private static readonly Color[] LeafShades = { new("1f4f1f"), new("2e7a2c"), new("4fa03c") };
    private static readonly Color[] PineShades = { new("1b4527"), new("2a6634"), new("438445") };
    private static readonly Color[] RockShades = { new("5c564c"), new("7b7468"), new("a59d8e") };
    private static readonly Color Trunk = new("5a3a1e");

    private void StampCell(byte[] codes, int cell, Vector2 center, Rect2I clip)
    {
        var code = (Code)codes[cell * 4];
        var overlay = (DemoHexMap.Overlay)codes[cell * 4 + 1];
        var amount = codes[cell * 4 + 2] / 255f;
        bool Hit(int x, int y) => Hash(x / K, y / K, 5) < amount;
        var fire = overlay is DemoHexMap.Overlay.Fire or DemoHexMap.Overlay.Scorch;
        bool Owned(int x, int y) => x >= 0 && y >= 0 && x < _width && y < _height && _owner![y * _width + x] == cell;
        bool Dry(int x, int y) => Owned(x, y) && _seaDist![y * _width + x] > 2 * K && _waterDist![y * _width + x] > K
            && (_ground.Count == 0 || !_ground.ContainsKey(TileIndexOf(x, y))); // 第 08-17 章：廣場、道路、屋子上不長樹與草叢

        // 散布點：交錯網格、隨機偏移，只取屬於本格、不在水邊的點（由上而下排序，前面的蓋住後面的）。
        List<(int X, int Y, float H)> Scatter(int step, int salt)
        {
            var reach = TempTexelsPerRadius + 2 * K;
            var spots = new List<(int X, int Y, float H)>();
            for (var gy = (int)center.Y - reach; gy <= center.Y + reach; gy += step)
                for (var gx = (int)center.X - reach + (gy / step & 1) * step / 2; gx <= center.X + reach; gx += step)
                {
                    var x = gx + (int)((Hash(gx, gy, salt + 1) - 0.5f) * step * 0.6f);
                    var y = gy + (int)((Hash(gx, gy, salt + 2) - 0.5f) * step * 0.6f);
                    if (Dry(x, y))
                        spots.Add((x, y, Hash(gx, gy, salt)));
                }
            return spots.OrderBy(s => s.Y).ThenBy(s => s.X).ToList();
        }

        var trees = new[] { PixelParts.Get("px_tree_round_1"), PixelParts.Get("px_tree_round_2"), PixelParts.Get("px_tree_pine_1"), PixelParts.Get("px_tree_pine_2"), PixelParts.Get("px_bush") };
        var art = trees.All(t => t is not null);
        void Tree(int x, int y, float kind)
        {
            // 燒過的地方：樹變成焦黑枯樹（火正大時）或只剩樹樁；淹水的地方不畫樹。
            if (fire && Hit(x, y))
            {
                if (PixelParts.Get(kind < 0.6f ? "px_tree_burnt" : "px_stump") is { } burnt)
                    StampSprite(burnt, x, y, clip);
                return;
            }
            if (overlay == DemoHexMap.Overlay.Flood && Hit(x, y))
                return;
            if (art)
                StampSprite(trees[kind < 0.3f ? 0 : kind < 0.55f ? 1 : kind < 0.75f ? 2 : kind < 0.92f ? 3 : 4]!, x, y, clip);
            else if (kind < 0.55f)
                Stamp(RoundTree, x, y, LeafShades, clip);
            else if (kind < 0.88f)
                Stamp(PineTree, x, y, PineShades, clip);
            else
                Stamp(Bush, x, y, LeafShades, clip);
        }

        switch (code)
        {
            case Code.Forest:
                // 樹：素材樹寬約 17 像素，每 10 像素一棵、彼此交疊成林冠（程式畫的樹每 8 像素一棵）。
                // 試玩回饋 2026-11-05 之 5：林緣（附近有不是森林的地）樹木漸疏，看不出六角形的邊。
                foreach (var (x, y, h) in Scatter(art ? 10 : 4 * K, 20))
                    if (!(NearOther(codes, x, y, cell, 12) && h < 0.5f))
                        Tree(x, y, Hash(x / K, y / K, 23));
                break;
            case Code.Logged:
            {
                // 第 08-16 章第 14 節：砍過的森林——剛砍時是一片樹樁，之後長出幼樹，再一棵棵長回大樹（第 4 個位元組＝長回進度）。
                var grown = codes[cell * 4 + 3] / 255f;
                var stump = PixelParts.Get("px_stump");
                var sapling = PixelParts.Get("px_sapling");
                foreach (var (x, y, h) in Scatter(art ? 10 : 4 * K, 20))
                {
                    if (NearOther(codes, x, y, cell, 12) && h < 0.5f)
                        continue;
                    var stage = grown - h * 0.5f;
                    if (stage > 0.5f)
                        Tree(x, y, Hash(x / K, y / K, 23));
                    else if (stage > 0.1f && sapling is not null)
                        StampSprite(sapling, x, y, clip);
                    else if (h > 0.4f && stump is not null)
                        StampSprite(stump, x, y, clip);
                }
                break;
            }
            case Code.Grassland:
            {
                // 草原：草叢、野花（乾旱時是枯草）與零星幾棵樹。
                var tufts = new[] { PixelParts.Get("px_grass_tuft_1"), PixelParts.Get("px_grass_tuft_2"), PixelParts.Get("px_flowers") };
                if (tufts.All(t => t is not null))
                    foreach (var (x, y, h) in Scatter(11, 40))
                    {
                        if (h > 0.22f)
                            continue;
                        var dry = overlay == DemoHexMap.Overlay.Dry && Hit(x, y);
                        if (fire && Hit(x, y) || overlay == DemoHexMap.Overlay.Flood && Hit(x, y))
                            continue;
                        StampSprite(dry && PixelParts.Get("px_dry_tuft") is { } d ? d : tufts[(int)(h / 0.22f * 3f) % 3]!, x, y, clip, detail: true);
                    }
                foreach (var (x, y, h) in Scatter(4 * K, 20))
                    if (h < 0.035f)
                        Tree(x, y, Hash(x / K, y / K, 23));
                break;
            }
            case Code.Hills:
            case Code.HillsOre:
            case Code.HillsMining:
            case Code.MinePit:
            {
                // 試玩回饋 2026-11-05 之 1（山頭太密）：山頭改成隨機散布、平均每格一座左右（大小兩種），不再每格固定一大一小排成格子；
                // 礦脈格散布兩三塊礦石（日後挖完就消失），一般丘陵偶有小石頭（細節層）。
                var bigArt = PixelParts.Get("px_mountain_1");
                var smallArt = PixelParts.Get("px_mountain_2");
                var peaks = Scatter(22, 30).Where(p => p.H < 0.4f && _seaDist![p.Y * _width + p.X] > K).ToList();
                foreach (var (x, y, h) in peaks)
                {
                    if (bigArt is not null && smallArt is not null)
                        StampSprite(h < 0.18f ? bigArt : smallArt, x, y, clip);
                    else
                        Stamp(h < 0.18f ? BigPeak : Peak, x, y, RockShades, clip, code is Code.HillsOre or Code.HillsMining);
                }
                // 第 08-16 章第 14 節：開採中的礦脈礦石隨開採進度一塊塊減少、山腳多一座礦坑；挖空後只剩礦坑。
                var mined = code == Code.HillsMining ? codes[cell * 4 + 3] / 255f : 0f;
                if (code is Code.HillsMining or Code.MinePit && PixelParts.Get("px_mine_pit") is { } pit && Owned((int)center.X, (int)center.Y + 8))
                    StampSprite(pit, (int)center.X, (int)center.Y + 8, clip);
                foreach (var (x, y, h) in Scatter(14, 34))
                {
                    if (code == Code.HillsMining && h < 0.3f * (1f - mined) && PixelParts.Get(h < 0.15f ? "px_ore_1" : "px_ore_2") is { } left)
                        StampSprite(left, x, y, clip);
                    else if (code == Code.HillsOre && h < 0.3f && PixelParts.Get(h < 0.15f ? "px_ore_1" : "px_ore_2") is { } ore)
                        StampSprite(ore, x, y, clip);
                    else if (code == Code.Hills && h < 0.12f && PixelParts.Get(h < 0.06f ? "px_rock_1" : "px_rock_2") is { } rock)
                        StampSprite(rock, x, y, clip, detail: true);
                }
                break;
            }
        }

        // 天災留在地上的零件：隕石礦、焦土上的灰燼、洪水的波紋。
        if (overlay == DemoHexMap.Overlay.Ore && PixelParts.Get("px_ore_meteor") is { } meteor && Owned((int)center.X, (int)center.Y + 6))
            StampSprite(meteor, (int)center.X, (int)center.Y + 6, clip);
        if (overlay == DemoHexMap.Overlay.Scorch && PixelParts.Get("px_ash") is { } ash)
            foreach (var (x, y, h) in Scatter(12, 50))
                if (h < 0.3f && Hit(x, y))
                    StampSprite(ash, x, y, clip);
        if (overlay == DemoHexMap.Overlay.Flood && PixelParts.Get("px_flood_ripple") is { } ripple)
            foreach (var (x, y, h) in Scatter(12, 60))
                if (h < 0.35f && Hit(x, y))
                    StampSprite(ripple, x, y, clip);
    }

    /// <summary>上下左右 <paramref name="reach"/> 像素內有沒有屬於別種地形的格（森林旁的森林不算）。</summary>
    private bool NearOther(byte[] codes, int x, int y, int cell, int reach)
    {
        bool Other(int px, int py)
        {
            if (px < 0 || py < 0 || px >= _width || py >= _height)
                return false;
            var o = _owner![py * _width + px];
            return o >= 0 && o != cell && codes[o * 4] != codes[cell * 4];
        }
        return Other(x - reach, y) || Other(x + reach, y) || Other(x, y - reach) || Other(x, y + reach);
    }

    /// <summary>把像素零件疊進地表：底部置中在 (<paramref name="ax"/>, <paramref name="ay"/>)，只畫不透明的像素。</summary>
    private void StampSprite(PixelParts.Sprite sprite, int ax, int ay, Rect2I clip, bool detail = false)
    {
        _toDetail = detail;
        var left = ax - sprite.Width / 2;
        var top = ay - sprite.Height + 1;
        for (var y = 0; y < sprite.Height; y++)
            for (var x = 0; x < sprite.Width; x++)
            {
                var c = sprite.At(x, y);
                if (c.A < 0.5f || !clip.HasPoint(new Vector2I(left + x, top + y)))
                    continue;
                Put(left + x, top + y, new Color(c.R, c.G, c.B));
            }
        _toDetail = false;
    }

    private void Stamp(string[] shape, int ax, int ay, Color[] shades, Rect2I clip, bool ore = false)
    {
        var height = shape.Length;
        for (var row = 0; row < height; row++)
        {
            var line = shape[row];
            for (var col = 0; col < line.Length; col++)
            {
                var x = ax + (col - line.Length / 2) * K;
                var y = ay + (row - height + 1) * K;
                Color? c = line[col] switch
                {
                    'd' => shades[0],
                    'm' => ore && Hash(x, y, 33) < 0.18f ? OreSpeck : shades[1],
                    'l' => shades[2],
                    's' => shades[0],
                    't' => Trunk,
                    _ => null
                };
                if (c is not { } color)
                    continue;
                for (var dy = 0; dy < K; dy++)
                    for (var dx = 0; dx < K; dx++)
                        if (clip.HasPoint(new Vector2I(x + dx, y + dy)))
                            Put(x + dx, y + dy, color);
            }
        }
    }

    // ───────────── 雜訊（只供畫面；同一位置永遠一樣） ─────────────

    private static float Hash(int x, int y, int salt)
    {
        unchecked
        {
            var h = (uint)(x * 374761393 + y * 668265263 + salt * 1274126177);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    private static float Noise(float x, float y, int salt)
    {
        var x0 = Mathf.FloorToInt(x);
        var y0 = Mathf.FloorToInt(y);
        var fx = x - x0;
        var fy = y - y0;
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);
        var a = Mathf.Lerp(Hash(x0, y0, salt), Hash(x0 + 1, y0, salt), fx);
        var b = Mathf.Lerp(Hash(x0, y0 + 1, salt), Hash(x0 + 1, y0 + 1, salt), fx);
        return Mathf.Lerp(a, b, fy);
    }
}
