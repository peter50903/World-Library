using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 試玩回饋 2026-11-04 之 3、5、7（森林破碎、地圖模糊、參考 WorldBox）：地表改成 WorldBox 式的像素地圖。
/// 整張地圖預先烤成一張圖（每格半徑 <see cref="TempTexelsPerRadius"/> 個像素），放大時最近鄰取樣——邊緣清楚、不模糊；
/// 畫面上每幀只貼這一張圖（原本每個螢幕像素都跑一次著色器）。
/// 每個圖像素依「扭曲後最近的格」取地形（海岸線、林緣是彎曲的像素邊，不是六角形）；
/// 顏色是固定的小調色盤（每種地形 2～3 個深淺，依位置雜訊挑），海依離岸距離分深淺、海邊一圈沙灘；
/// 森林＝深綠底色＋一棵棵樹疊上去，丘陵＝底色＋小山頭，田地＝一塊塊田畦，開墾中依開發度從草地→泥土→田畦。
/// 天災疊層（火、焦土、乾旱、洪水、礦脈）也烤進圖裡；只有改變的格重畫。只影響畫面；格的歸屬、點選、規則仍是六角格。
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
    public const float TempWarp = 0.38f;

    private const float Sqrt3 = 1.7320508f;

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
    public void Show(byte[] codes, int columns, int rows, Vector2 origin, float radius)
    {
        if (!ReferenceEquals(codes, _uploaded) || columns != _columns || rows != _rows)
        {
            Bake(codes, columns, rows);
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
        var topLeft = _origin + new Vector2(-Sqrt3 / 2f, -1f) * _radius;
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
        var t = (screen - (_origin + new Vector2(-Sqrt3 / 2f, -1f) * _radius)) / (_radius / TempTexelsPerRadius);
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
        var topLeft = _origin + new Vector2(-Sqrt3 / 2f, -1f) * _radius;
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

    // ───────────── 烤圖 ─────────────

    private static bool IsWater(byte code) => code is (byte)Code.Sea or (byte)Code.River or (byte)Code.Shallow;

    /// <summary>格中心（以格半徑為單位，原點＝第 0 格中心）。</summary>
    private static Vector2 CenterOf(int column, int row) => new(Sqrt3 * (column + (row & 1) * 0.5f), 1.5f * row);

    private void Bake(byte[] codes, int columns, int rows)
    {
        var full = _pixels is null || _baked is null || columns != _columns || rows != _rows;
        if (full)
        {
            _columns = columns;
            _rows = rows;
            _width = Mathf.CeilToInt((Sqrt3 * columns + Sqrt3 / 2f) * TempTexelsPerRadius);
            _height = Mathf.CeilToInt((1.5f * (rows - 1) + 2f) * TempTexelsPerRadius);
            _pixels = new byte[_width * _height * 4];
            _detail = new byte[_width * _height * 4];
            foreach (var old in _chunks)
                old?.Dispose();
            foreach (var old in _detailChunks)
                old?.Dispose();
            _chunks = new ImageTexture?[(_width + TempChunk - 1) / TempChunk, (_height + TempChunk - 1) / TempChunk];
            _detailChunks = new ImageTexture?[_chunks.GetLength(0), _chunks.GetLength(1)];
            _dirty = new bool[_chunks.GetLength(0), _chunks.GetLength(1)];
            BuildOwners();
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
            var margin = TempTexelsPerRadius * 2;
            for (var i = 0; i < columns * rows; i++)
            {
                if (codes[i * 4] == _baked![i * 4] && codes[i * 4 + 1] == _baked[i * 4 + 1]
                    && codes[i * 4 + 2] == _baked[i * 4 + 2] && codes[i * 4 + 3] == _baked[i * 4 + 3])
                    continue;
                var c = TexelOf(CenterOf(i % columns, i / columns));
                painted = true;
                PaintRect(codes, (int)c.X - margin, (int)c.Y - margin, (int)c.X + margin, (int)c.Y + margin);
            }
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

    private static Vector2 TexelOf(Vector2 world) => (world - new Vector2(-Sqrt3 / 2f, -1f)) * TempTexelsPerRadius;

    private Vector2 WorldOf(int x, int y) => new Vector2(-Sqrt3 / 2f, -1f) + new Vector2(x + 0.5f, y + 0.5f) / TempTexelsPerRadius;

    /// <summary>點落在哪一格（尖頂六角，odd-r 位移座標）；地圖外回傳 -1。</summary>
    private int CellAt(Vector2 p)
    {
        var fq = Sqrt3 / 3f * p.X - p.Y / 3f;
        var fr = 2f / 3f * p.Y;
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
        var column = q + (r - (r & 1)) / 2;
        return column < 0 || column >= _columns || r < 0 || r >= _rows ? -1 : r * _columns + column;
    }

    private void BuildOwners()
    {
        _owner = new int[_width * _height];
        for (var y = 0; y < _height; y++)
            for (var x = 0; x < _width; x++)
            {
                var p = WorldOf(x, y);
                // 兩層平滑扭曲：交界是彎曲的像素邊（WorldBox 的海岸線），不是六角形，也不是抖點。
                var w = p + new Vector2(Noise(p.X / 1.1f, p.Y / 1.1f, 11) - 0.5f, Noise(p.X / 1.1f, p.Y / 1.1f, 23) - 0.5f) * (2f * TempWarp)
                          + new Vector2(Noise(p.X / 0.35f, p.Y / 0.35f, 37) - 0.5f, Noise(p.X / 0.35f, p.Y / 0.35f, 41) - 0.5f) * (0.5f * TempWarp);
                var owner = CellAt(w);
                _owner[y * _width + x] = owner >= 0 ? owner : CellAt(p);
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
                if (_seaDist![i] <= 2 * K && (_seaDist[i] <= K || h < 0.5f))
                {
                    c = Pick(Sand, patch); // 海邊一圈沙灘
                    break;
                }
                c = (Code)code switch
                {
                    Code.Forest or Code.Logged => Pick(ForestFloor, patch),
                    Code.Hills or Code.HillsOre or Code.HillsMining or Code.MinePit => Pick(HillGround, patch),
                    // 試玩回饋 2026-11-05 之 5：田不再填滿整格六角形——依平滑雜訊與離格邊遠近只留不規則的幾片，其餘是草地。
                    Code.Farmland => FieldMask(x, y, owner) ? FieldArt(x, y, 1f) ?? Field(bx, by, 1f) : Pick(Grass, patch),
                    Code.Clearing when codes[owner * 4 + 3] == SettlementGround => VillageGround(x, y, owner, patch),
                    Code.Clearing => dev > 0f ? (FieldMask(x, y, owner) ? FieldArt(x, y, dev) ?? Field(bx, by, dev) : Pick(Grass, patch)) : Pick(Soil, patch),
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

    /// <summary>田畦：全圖共用的 4×3 田塊格（相鄰的田連成一片），每塊一種作物、隔行深淺；
    /// <paramref name="dev"/>＜1 時依開發度從草地→翻開的泥土→田畦。</summary>
    /// <summary>田塊素材（20×14）鋪成全圖共用的田格：成熟的田多半是麥田、間雜綠色作物；
    /// 開墾中依開發度從草地→翻開的泥土→剛播種的田（素材缺少時回傳 null，改用程式畫的田畦）。</summary>
    /// <summary>與 DemoHexMap 的 TempSettlementGround 相同：有房子的格（聚落空地）。</summary>
    public const byte SettlementGround = 100;

    /// <summary>離格中心的距離（以格半徑為單位）。</summary>
    private float CenterDistance(int x, int y, int cell)
        => new Vector2(x, y).DistanceTo(TexelOf(CenterOf(cell % _columns, cell / _columns))) / TempTexelsPerRadius;

    /// <summary>田塊的範圍：靠格中心的大致都是田，往外依平滑雜訊參差收邊（相鄰的田格仍會連成一片）。</summary>
    private bool FieldMask(int x, int y, int cell)
        => CenterDistance(x, y, cell) + (Noise(x / 16f, y / 16f, 7) - 0.5f) * 0.9f < 0.82f;

    /// <summary>試玩回饋 2026-11-05 之 4、8、13：聚落的泥土空地——以格中心為圓心、邊緣參差的一片夯土，外圍是草地；
    /// 建築站在空地上，不再浮在樹梢或草叢上，也不被背景吃掉。</summary>
    private Color VillageGround(int x, int y, int cell, float patch)
        => CenterDistance(x, y, cell) + (Noise(x / 10f, y / 10f, 8) - 0.5f) * 0.6f < 0.78f
            ? Pick(Soil, Mathf.Clamp(Noise(x / 6f, y / 6f, 9) * 0.9f, 0f, 0.999f)).Lightened(0.08f)
            : Pick(Grass, patch);

    private static Color? FieldArt(int x, int y, float dev)
    {
        if (PixelParts.Get("px_field_wheat") is not { } wheat || PixelParts.Get("px_field_green") is not { } green
            || PixelParts.Get("px_field_young") is not { } young || PixelParts.Get("px_field_soil") is not { } soil)
            return null;
        var tx = x / wheat.Width;
        var ty = y / wheat.Height;
        var plot = Hash(tx, ty, 13);
        var h = Hash(x / K, y / K, 2);
        PixelParts.Sprite tile;
        if (dev >= 1f)
            tile = plot < 0.6f ? wheat : plot < 0.9f ? green : young;
        else if (h > dev * 1.6f)
            return Pick(Grass, Hash(x / K, y / K, 3));
        else
            tile = dev >= 0.5f && plot < (dev - 0.5f) * 2f ? young : soil;
        var c = tile.At(x % tile.Width, y % tile.Height);
        return c.A < 0.5f ? Soil[0] : new Color(c.R, c.G, c.B);
    }

    private static Color Field(int x, int y, float dev)
    {
        var h = Hash(x, y, 2);
        if (dev < 1f)
        {
            // 開發度一半以前只有草地與泥土（森林聚落的空地停在約四成），過半才開始長作物。
            if (h > dev * 1.6f)
                return Pick(Grass, Hash(x, y, 3));
            if (h > dev * 2f - 1f)
                return Hash(x, y, 4) < 0.08f ? Stump : Pick(Soil, Hash(x, y, 4));
        }
        // 試玩回饋 2026-11-04 之 1 更正：農田不是太多，是圖案太大——田塊縮成 4×3、作物交錯深淺。
        if (x % 4 == 0 || y % 3 == 0)
            return Soil[0]; // 田埂
        var crop = Crops[(int)(Hash(x / 4, y / 3, 12) * Crops.Length) % Crops.Length];
        return (x + y) % 2 == 0 ? crop : crop.Darkened(0.1f);
    }

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
        bool Dry(int x, int y) => Owned(x, y) && _seaDist![y * _width + x] > 2 * K && _waterDist![y * _width + x] > K;

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
