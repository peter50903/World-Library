using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18b 章：方格世界的像素地表（WorldBox 式，像素密度約為 WorldBox 的兩倍）。
/// 一個規則格＝6×6 個畫面方格、每方格 16×16 圖像素（一格 96×96）；沿格邊切開、沒有扭曲。
/// 自然交界（草原、森林、丘陵、海）的邊上可有一方格的參差（固定雜湊），只是裝飾，格的歸屬、點選仍依規則格。
/// 生態區（溫帶、花原、乾草原、針葉林、紅葉林）只改畫面：同一種規則地形在不同生態區有不同的底色與樹。
/// 效能：近景層依畫面範圍逐塊（512×512）烤圖、最近用過的塊留在快取；區域層與還沒烤好的塊用四分之一解析度的縮圖。
/// 只影響畫面，不抽世界亂數、不存檔。
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

    /// <summary>【TEMP】第 08-18b 章：每格半徑幾個圖像素（一格邊長＝2 個半徑＝96 圖像素）。</summary>
    public const int TempTexelsPerRadius = 48;

    /// <summary>第四批像素零件與程式畫的建築的像素密度（每格半徑 24 圖像素）；疊進新地表時放大兩倍。</summary>
    public const int TempArtTexelsPerRadius = 24;

    private const int ArtScale = TempTexelsPerRadius / TempArtTexelsPerRadius;

    /// <summary>
    /// 第 08-18b 章第 1 節：畫面方格的邊長（圖像素）。一個規則格＝6×6 方格；
    /// 方格整塊屬於某一格（沿格邊切開），田地、房屋、道路對齊方格。
    /// </summary>
    public const int TempTile = 16;

    /// <summary>一格幾個方格（每邊）。</summary>
    public const int TilesPerCell = 2 * TempTexelsPerRadius / TempTile;

    /// <summary>程式畫的樹、山頭每個「大像素」佔幾個圖像素。</summary>
    private const int K = 4;

    /// <summary>【TEMP】烤圖塊邊長、快取上限、每幀最多烤幾塊；格半徑至少幾像素才逐塊烤（以下只畫縮圖）；縮圖倍率。</summary>
    private const int TempChunk = 512;
    private const int TempChunkCache = 64;
    private const int TempChunksPerFrame = 4;
    public const float TempChunkMinRadius = 24f;
    private const int TempThumb = 4;

    /// <summary>【TEMP】自然交界的參差：邊上的方格有多少機率畫成隔壁格的地形。</summary>
    private const float TempJitterChance = 0.35f;

    /// <summary>【TEMP】細節層（草叢、野花、小石頭）在格半徑至少幾像素時才畫；整體調色（飽和度、亮度倍率）。</summary>
    public const float TempDetailMinRadius = 26f;
    public const float TempSaturation = 0.78f;
    public const float TempBrightness = 0.9f;

    /// <summary>地圖左上角（相對第 0 格中心，以格半徑為單位）。</summary>
    private static readonly Vector2 Corner = new(-1f, -1f);

    private sealed class Chunk
    {
        public ImageTexture? Base;
        public ImageTexture? Detail;
        public ulong Used;
        public bool Dirty;
    }

    private readonly Dictionary<(int, int), Chunk> _chunks = new();
    private ulong _frame;
    private byte[] _buf = new byte[TempChunk * TempChunk * 4];
    private byte[] _detailBuf = new byte[TempChunk * TempChunk * 4];
    private int _bx, _by, _bw, _bh; // 正在烤的塊（圖像素範圍）
    private bool _toDetail, _wroteDetail;

    // 試玩回饋 2026-11-06 之 8：縮圖切成 256×256 的小塊，改變時只重新上傳改到的塊（原本每次上傳整張 14 MB）。
    private const int TempThumbTile = 256;
    private ImageTexture?[,] _thumbTiles = new ImageTexture?[0, 0];
    private bool[,] _thumbDirty = new bool[0, 0];
    private bool _thumbReady;
    private byte[]? _thumbBytes;
    private int _thumbW, _thumbH;

    private byte[]? _codes;   // 目前每格四個位元組
    private byte[]? _uploaded;
    private int[] _paint = Array.Empty<int>(); // 每方格畫成哪一格（參差後）
    private byte[] _seaDist = Array.Empty<byte>(); // 陸地方格離海幾方格；水的方格離陸地幾方格
    private byte[] _waterDist = Array.Empty<byte>(); // 陸地方格離任何水幾方格
    private byte[] _biome = Array.Empty<byte>(); // 每格的生態區
    private int _columns, _rows, _width, _height, _tilesX, _tilesY;
    private Vector2 _origin;
    private float _radius;

    /// <summary>目前是否畫地表（近景層與區域層的地形模式）。</summary>
    public bool Active { get; private set; }

    public TerrainSurface()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        ShowBehindParent = true;
    }

    public override void _Ready() => Resized += QueueRedraw;

    /// <summary>每次地圖重畫時呼叫：<paramref name="codes"/> 每格四個位元組（地表代碼、天災疊層、疊層強度、開發度），
    /// 依「列×欄數＋欄」排列；同一個陣列不重複處理，只重畫改變的格。</summary>
    public void Show(byte[] codes, int columns, int rows, Vector2 origin, float radius, IReadOnlyDictionary<int, byte>? ground = null)
    {
        ground ??= NoGround;
        if (!ReferenceEquals(codes, _uploaded) || columns != _columns || rows != _rows || !ReferenceEquals(ground, _ground))
        {
            Update(codes, columns, rows, ground);
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

    public override void _Draw()
    {
        if (!Active || !_thumbReady)
            return;
        _frame++;
        var scale = _radius / TempTexelsPerRadius;
        var topLeft = _origin + Corner * _radius;
        var thumbScale = scale * TempThumb;
        for (var ty = 0; ty < _thumbTiles.GetLength(1); ty++)
            for (var tx = 0; tx < _thumbTiles.GetLength(0); tx++)
                if (_thumbTiles[tx, ty] is { } part)
                {
                    var rect = new Rect2(topLeft + new Vector2(tx, ty) * TempThumbTile * thumbScale, part.GetSize() * thumbScale);
                    if (rect.Intersects(new Rect2(Vector2.Zero, Size)))
                        DrawTextureRect(part, rect, false);
                }
        if (_radius < TempChunkMinRadius)
            return;
        // 畫面範圍內的塊：有就畫，沒有（或過期）就烤，每幀最多烤幾塊，其餘下一幀再烤（先看到縮圖）。
        var x0 = Math.Max(0, (int)((0f - topLeft.X) / scale) / TempChunk);
        var y0 = Math.Max(0, (int)((0f - topLeft.Y) / scale) / TempChunk);
        var x1 = Math.Min((_width - 1) / TempChunk, (int)((Size.X - topLeft.X) / scale) / TempChunk);
        var y1 = Math.Min((_height - 1) / TempChunk, (int)((Size.Y - topLeft.Y) / scale) / TempChunk);
        var budget = TempChunksPerFrame;
        var pending = false;
        for (var cy = y0; cy <= y1; cy++)
            for (var cx = x0; cx <= x1; cx++)
            {
                if (!_chunks.TryGetValue((cx, cy), out var chunk))
                    _chunks[(cx, cy)] = chunk = new Chunk { Dirty = true };
                chunk.Used = _frame;
                if (chunk.Dirty)
                {
                    if (budget > 0)
                    {
                        budget--;
                        BakeChunk(cx, cy, chunk);
                    }
                    else
                        pending = true;
                }
                if (chunk.Base is not { } texture)
                    continue;
                var rect = new Rect2(topLeft + new Vector2(cx, cy) * TempChunk * scale, texture.GetSize() * scale);
                DrawTextureRect(texture, rect, false);
                if (_radius >= TempDetailMinRadius && chunk.Detail is { } detail)
                    DrawTextureRect(detail, rect, false);
            }
        Evict();
        if (pending)
            CallDeferred(CanvasItem.MethodName.QueueRedraw);
    }

    /// <summary>快取超過上限時丟掉最久沒畫的塊。</summary>
    private void Evict()
    {
        if (_chunks.Count <= TempChunkCache)
            return;
        foreach (var (key, chunk) in _chunks.OrderBy(c => c.Value.Used).Take(_chunks.Count - TempChunkCache).ToList())
        {
            if (chunk.Used == _frame)
                break;
            chunk.Base?.Dispose();
            chunk.Detail?.Dispose();
            _chunks.Remove(key);
        }
    }

    // ───────────── 選取與座標 ─────────────

    /// <summary>螢幕位置落在哪一格（地表沒畫或在地圖外回傳 -1）。</summary>
    public int CellAtScreen(Vector2 screen)
    {
        if (!Active || _columns == 0)
            return -1;
        var t = (screen - (_origin + Corner * _radius)) / (_radius / TempTexelsPerRadius);
        var column = Mathf.FloorToInt(t.X / (2 * TempTexelsPerRadius));
        var row = Mathf.FloorToInt(t.Y / (2 * TempTexelsPerRadius));
        return column < 0 || row < 0 || column >= _columns || row >= _rows ? -1 : row * _columns + column;
    }

    /// <summary>選取框：這一格的四條邊（成對的端點，螢幕座標）。</summary>
    public Vector2[] OutlineOf(int cell)
    {
        if (!Active || _columns == 0 || cell < 0 || cell >= _columns * _rows)
            return Array.Empty<Vector2>();
        var center = _origin + new Vector2(cell % _columns, cell / _columns) * 2f * _radius;
        var a = center + new Vector2(-_radius, -_radius);
        var b = center + new Vector2(_radius, -_radius);
        var c = center + new Vector2(_radius, _radius);
        var d = center + new Vector2(-_radius, _radius);
        return new[] { a, b, b, c, c, d, d, a };
    }

    // ───────────── 方格與聚落地面 ─────────────

    private static readonly IReadOnlyDictionary<int, byte> NoGround = new Dictionary<int, byte>();
    private IReadOnlyDictionary<int, byte> _ground = NoGround; // 方格編號（列×方格欄數＋欄）→ 聚落地面種類

    /// <summary>聚落地面種類（第 08-17 章第 4 節）。</summary>
    public const byte GroundPlazaDirt = 1, GroundPlazaStone = 2, GroundRoad = 3, GroundYard = 4, GroundCobble = 5, GroundStreet = 6;

    /// <summary>方格欄數、列數。</summary>
    public int TilesX => _tilesX;
    public int TilesY => _tilesY;

    /// <summary>依地圖大小準備好方格（聚落版面要先知道方格屬於哪一格）。</summary>
    public void EnsureGrid(int columns, int rows)
    {
        if (columns == _columns && rows == _rows)
            return;
        _columns = columns;
        _rows = rows;
        _tilesX = columns * TilesPerCell;
        _tilesY = rows * TilesPerCell;
        _width = _tilesX * TempTile;
        _height = _tilesY * TempTile;
        _codes = null;
    }

    /// <summary>方格屬於哪一格（地圖外 -1）。</summary>
    public int TileOwner(int tx, int ty)
        => tx < 0 || ty < 0 || tx >= _tilesX || ty >= _tilesY ? -1 : ty / TilesPerCell * _columns + tx / TilesPerCell;

    /// <summary>格中心所在的方格（一格 6×6 方格，中心在第 3 列第 3 欄）。</summary>
    public static Vector2I TileOfCell(int column, int row)
        => new(column * TilesPerCell + TilesPerCell / 2, row * TilesPerCell + TilesPerCell / 2);

    /// <summary>圖像素座標在畫面上的位置（<paramref name="origin"/>＝第 0 格中心的畫面位置）。</summary>
    public static Vector2 TexelToScreen(Vector2 texel, Vector2 origin, float radius)
        => origin + Corner * radius + texel * (radius / TempTexelsPerRadius);

    private int TileIndexOf(int x, int y) => y / TempTile * _tilesX + x / TempTile;

    private Color GroundColor(byte kind, int x, int y)
    {
        var tx = x / TempTile;
        var ty = y / TempTile;
        var art = kind switch
        {
            GroundPlazaStone => Tex(Art.PlazaStone, tx, ty, x, y),
            GroundCobble or GroundStreet => Tex(Art.RoadCobble, tx, ty, x, y),
            GroundRoad => Tex(Art.RoadDirt, tx, ty, x, y),
            GroundYard => Tex(Art.Yard, tx, ty, x, y),
            _ => Tex(Art.PlazaDirt, tx, ty, x, y)
        };
        if (art is { } c)
            return c;
        var h = Hash(x / K, y / K, 31);
        var ix = x % TempTile;
        var iy = y % TempTile;
        return kind switch
        {
            GroundPlazaStone => (x / 8 + y / 8) % 2 == 0 ? Paving[0] : Paving[1], // 石板廣場（8×8 小石板交錯）
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

    // ───────────── 更新：參差、離岸距離、生態區、縮圖 ─────────────

    private static bool IsWater(byte code) => code is (byte)Code.Sea or (byte)Code.River or (byte)Code.Shallow;

    /// <summary>可以參差的自然地形（河、人造的地、天災中的地不參差）。</summary>
    private static bool Natural(byte code) => code is (byte)Code.Grassland or (byte)Code.Forest or (byte)Code.Hills or (byte)Code.HillsOre or (byte)Code.Sea or (byte)Code.DryGrass;

    private void Update(byte[] codes, int columns, int rows, IReadOnlyDictionary<int, byte> ground)
    {
        EnsureGrid(columns, rows);
        var old = _codes;
        var oldGround = _ground;
        _ground = ground;
        _codes = codes;
        var oldTreeless = _treeless;
        if (!ReferenceEquals(oldGround, ground))
            BuildTreeless();
        var full = old is null || old.Length != codes.Length || !_thumbReady;
        if (!full)
            for (var i = 0; i < columns * rows && !full; i++)
                full = IsWater(codes[i * 4]) != IsWater(old![i * 4]);
        if (full)
        {
            BuildPaint(0, 0, _tilesX, _tilesY);
            BuildDistances();
            BuildBiomes();
            foreach (var chunk in _chunks.Values)
                chunk.Dirty = true;
            BuildThumb(0, 0, _thumbW = _width / TempThumb, _thumbH = _height / TempThumb, rebuild: true);
            return;
        }
        // 只重畫改變的格（含四周一格：參差與樹冠會跨格）。
        var changed = new HashSet<int>();
        for (var i = 0; i < columns * rows; i++)
            if (codes[i * 4] != old![i * 4] || codes[i * 4 + 1] != old[i * 4 + 1] || codes[i * 4 + 2] != old[i * 4 + 2] || codes[i * 4 + 3] != old[i * 4 + 3])
                changed.Add(i);
        if (!ReferenceEquals(oldGround, ground))
        {
            foreach (var (tile, kind) in ground)
                if (!oldGround.TryGetValue(tile, out var was) || was != kind)
                    changed.Add(TileOwner(tile % _tilesX, tile / _tilesX));
            foreach (var tile in oldGround.Keys)
                if (!ground.ContainsKey(tile))
                    changed.Add(TileOwner(tile % _tilesX, tile / _tilesX));
            foreach (var tile in _treeless)
                if (!oldTreeless.Contains(tile))
                    changed.Add(TileOwner(tile % _tilesX, tile / _tilesX));
            foreach (var tile in oldTreeless)
                if (!_treeless.Contains(tile))
                    changed.Add(TileOwner(tile % _tilesX, tile / _tilesX));
            changed.Remove(-1);
        }
        if (changed.Count == 0)
            return;
        if (changed.Count > columns * rows / 4)
        {
            BuildPaint(0, 0, _tilesX, _tilesY);
            foreach (var chunk in _chunks.Values)
                chunk.Dirty = true;
            BuildThumb(0, 0, _thumbW, _thumbH, rebuild: false);
            UploadThumb();
            return;
        }
        var cell = 2 * TempTexelsPerRadius;
        foreach (var i in changed) // 參差只看自己與隔壁格：重算改變的格與四周一格的方格
            BuildPaint((i % columns - 1) * TilesPerCell, (i / columns - 1) * TilesPerCell, (i % columns + 2) * TilesPerCell, (i / columns + 2) * TilesPerCell);
        foreach (var i in changed)
        {
            var x0 = (i % columns - 1) * cell;
            var y0 = (i / columns - 1) * cell;
            var x1 = (i % columns + 2) * cell;
            var y1 = (i / columns + 2) * cell;
            for (var cy = Math.Max(0, y0 / TempChunk); cy <= Math.Min((_height - 1) / TempChunk, y1 / TempChunk); cy++)
                for (var cx = Math.Max(0, x0 / TempChunk); cx <= Math.Min((_width - 1) / TempChunk, x1 / TempChunk); cx++)
                    if (_chunks.TryGetValue((cx, cy), out var chunk))
                        chunk.Dirty = true;
            BuildThumb(Math.Max(0, x0 / TempThumb), Math.Max(0, y0 / TempThumb), Math.Min(_thumbW, x1 / TempThumb), Math.Min(_thumbH, y1 / TempThumb), rebuild: false);
        }
        UploadThumb();
    }

    /// <summary>只重新上傳改到的縮圖小塊。</summary>
    private void UploadThumb()
    {
        for (var ty = 0; ty < _thumbDirty.GetLength(1); ty++)
            for (var tx = 0; tx < _thumbDirty.GetLength(0); tx++)
            {
                if (!_thumbDirty[tx, ty])
                    continue;
                _thumbDirty[tx, ty] = false;
                var u0 = tx * TempThumbTile;
                var v0 = ty * TempThumbTile;
                var w = Math.Min(TempThumbTile, _thumbW - u0);
                var h = Math.Min(TempThumbTile, _thumbH - v0);
                var bytes = new byte[w * h * 4];
                for (var y = 0; y < h; y++)
                    Array.Copy(_thumbBytes!, ((v0 + y) * _thumbW + u0) * 4, bytes, y * w * 4, w * 4);
                using var image = Image.CreateFromData(w, h, false, Image.Format.Rgba8, bytes);
                if (_thumbTiles[tx, ty] is { } part && part.GetWidth() == w && part.GetHeight() == h)
                    part.Update(image);
                else
                {
                    _thumbTiles[tx, ty]?.Dispose();
                    _thumbTiles[tx, ty] = ImageTexture.CreateFromImage(image);
                }
            }
        _thumbReady = true;
    }

    /// <summary>每個方格畫成哪一格：預設是自己的格；自然交界邊上的方格依固定雜湊畫成隔壁格（一方格以內的參差）。</summary>
    private void BuildPaint(int tx0, int ty0, int tx1, int ty1)
    {
        var count = _tilesX * _tilesY;
        if (_paint.Length != count)
            _paint = new int[count];
        var last = TilesPerCell - 1;
        for (var ty = Math.Max(0, ty0); ty < Math.Min(_tilesY, ty1); ty++)
            for (var tx = Math.Max(0, tx0); tx < Math.Min(_tilesX, tx1); tx++)
            {
                var own = TileOwner(tx, ty);
                var paint = own;
                int lx = tx % TilesPerCell, ly = ty % TilesPerCell;
                var (nx, ny) = lx == 0 ? (tx - 1, ty) : lx == last ? (tx + 1, ty) : ly == 0 ? (tx, ty - 1) : ly == last ? (tx, ty + 1) : (-1, -1);
                if (nx >= 0 && TileOwner(nx, ny) is var other and >= 0 && other != own)
                {
                    var a = _codes![own * 4];
                    var b = _codes[other * 4];
                    if (a != b && Natural(a) && Natural(b) && Hash(tx, ty, 77) < TempJitterChance)
                        paint = other;
                }
                _paint[ty * _tilesX + tx] = paint;
            }
    }

    private byte PaintCode(int tile) => _codes![_paint[tile] * 4];

    /// <summary>離岸距離（以方格為單位，四方向擴散，最多 15）。</summary>
    private void BuildDistances()
    {
        var count = _tilesX * _tilesY;
        _seaDist = new byte[count];
        _waterDist = new byte[count];
        var landDist = new byte[count];
        Spread(_seaDist, t => PaintCode(t) == (byte)Code.Sea);
        Spread(_waterDist, t => IsWater(PaintCode(t)));
        Spread(landDist, t => !IsWater(PaintCode(t)));
        for (var t = 0; t < count; t++)
            if (IsWater(PaintCode(t)))
                _seaDist[t] = landDist[t]; // 水的方格存離陸地的距離（海的深淺）
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
            var x = i % _tilesX;
            void Try(int n)
            {
                if (dist[n] > dist[i] + 1)
                {
                    dist[n] = (byte)(dist[i] + 1);
                    queue.Enqueue(n);
                }
            }
            if (x > 0) Try(i - 1);
            if (x < _tilesX - 1) Try(i + 1);
            if (i >= _tilesX) Try(i - _tilesX);
            if (i < dist.Length - _tilesX) Try(i + _tilesX);
        }
    }

    // ───────────── 生態區（只改畫面） ─────────────

    private enum Biome : byte { Temperate = 0, Meadow = 1, Steppe = 2, Conifer = 3, Autumn = 4 }

    /// <summary>【TEMP】生態區：以約 6 格為一片的平滑雜訊分區；草原離水 3 格以上、雜訊低的是乾草原，雜訊高的是花原；
    /// 森林雜訊低的是針葉林、高的是紅葉林，其餘溫帶。</summary>
    private void BuildBiomes()
    {
        _biome = new byte[_columns * _rows];
        for (var row = 0; row < _rows; row++)
            for (var col = 0; col < _columns; col++)
            {
                var i = row * _columns + col;
                var center = TileOfCell(col, row);
                var dry = _waterDist[center.Y * _tilesX + center.X] >= 3 * TilesPerCell / 2;
                var grass = Noise(col / 6f, row / 6f, 71);
                var wood = Noise(col / 5f, row / 5f, 83);
                _biome[i] = (byte)(_codes![i * 4] switch
                {
                    (byte)Code.Forest or (byte)Code.Logged => wood < 0.36f ? Biome.Conifer : wood > 0.68f ? Biome.Autumn : Biome.Temperate,
                    _ => grass < 0.36f && dry ? Biome.Steppe : grass > 0.66f ? Biome.Meadow : Biome.Temperate
                });
            }
    }

    private Biome BiomeOfTile(int tile) => (Biome)_biome[_paint[tile]];

    // ───────────── 縮圖（區域層、還沒烤好的塊） ─────────────

    private void BuildThumb(int u0, int v0, int u1, int v1, bool rebuild)
    {
        if (rebuild || _thumbBytes is null || _thumbBytes.Length != _thumbW * _thumbH * 4)
            _thumbBytes = new byte[_thumbW * _thumbH * 4];
        for (var v = v0; v < v1; v++)
            for (var u = u0; u < u1; u++)
            {
                var x = u * TempThumb + TempThumb / 2;
                var y = v * TempThumb + TempThumb / 2;
                var c = Ground(x, y);
                if (!_groundRaw)
                    c = Adjust(c);
                // 遠看時樹與山頭只剩色點（WorldBox 的遠景做法）：方格中央 2×2 縮圖像素畫成樹冠或岩石色。
                var tile = TileIndexOf(x, y);
                int lu = u % (TempTile / TempThumb), lv = v % (TempTile / TempThumb);
                if (lu is 1 or 2 && lv is 1 or 2 && (_ground.Count == 0 || !_ground.ContainsKey(tile)))
                {
                    if (TreeAt(tile % _tilesX, tile / _tilesX) is { } tree)
                        c = Canopy(TreeArt(tree, BiomeOfTile(tile), tile % _tilesX, tile / _tilesX)) ?? Adjust(tree.Shades[1]);
                    else if (PeakAt(tile % _tilesX, tile / _tilesX) > 0)
                        c = Adjust(RockShades[1]);
                }
                var o = (v * _thumbW + u) * 4;
                _thumbBytes[o] = (byte)(c.R * 255f);
                _thumbBytes[o + 1] = (byte)(c.G * 255f);
                _thumbBytes[o + 2] = (byte)(c.B * 255f);
                _thumbBytes[o + 3] = (byte)(c.A * 255f);
            }
        if (rebuild || _thumbTiles.GetLength(0) != (_thumbW + TempThumbTile - 1) / TempThumbTile || _thumbTiles.GetLength(1) != (_thumbH + TempThumbTile - 1) / TempThumbTile)
        {
            foreach (var part in _thumbTiles)
                part?.Dispose();
            _thumbTiles = new ImageTexture?[(_thumbW + TempThumbTile - 1) / TempThumbTile, (_thumbH + TempThumbTile - 1) / TempThumbTile];
            _thumbDirty = new bool[_thumbTiles.GetLength(0), _thumbTiles.GetLength(1)];
            u0 = 0; v0 = 0; u1 = _thumbW; v1 = _thumbH;
        }
        for (var ty = Math.Max(0, v0) / TempThumbTile; ty <= Math.Min(_thumbH - 1, v1 - 1) / TempThumbTile && ty < _thumbDirty.GetLength(1); ty++)
            for (var tx = Math.Max(0, u0) / TempThumbTile; tx <= Math.Min(_thumbW - 1, u1 - 1) / TempThumbTile && tx < _thumbDirty.GetLength(0); tx++)
                _thumbDirty[tx, ty] = true;
        if (rebuild)
            UploadThumb();
    }

    // ───────────── 烤一塊 ─────────────

    private void BakeChunk(int cx, int cy, Chunk chunk)
    {
        chunk.Dirty = false;
        _bx = cx * TempChunk;
        _by = cy * TempChunk;
        _bw = Math.Min(TempChunk, _width - _bx);
        _bh = Math.Min(TempChunk, _height - _by);
        Array.Clear(_detailBuf);
        _wroteDetail = false;
        for (var y = _by; y < _by + _bh; y++)
            for (var x = _bx; x < _bx + _bw; x++)
            {
                var c = Ground(x, y);
                Put(x, y, c, _groundRaw);
            }
        // 疊上樹、山頭、礦石等零件：由上而下（下面的蓋住上面的）；零件會往上、往左右超出自己的方格，所以多看幾個方格。
        var tx0 = Math.Max(0, _bx / TempTile - 2);
        var tx1 = Math.Min(_tilesX - 1, (_bx + _bw) / TempTile + 2);
        var ty0 = Math.Max(0, _by / TempTile - 1);
        var ty1 = Math.Min(_tilesY - 1, (_by + _bh) / TempTile + 4);
        for (var ty = ty0; ty <= ty1; ty++)
            for (var tx = tx0; tx <= tx1; tx++)
                StampTile(tx, ty);
        chunk.Base = Upload(_buf, chunk.Base);
        chunk.Detail = _wroteDetail ? Upload(_detailBuf, chunk.Detail) : null;
    }

    private ImageTexture Upload(byte[] source, ImageTexture? texture)
    {
        var bytes = source;
        if (_bw != TempChunk || _bh != TempChunk)
        {
            bytes = new byte[_bw * _bh * 4];
            for (var y = 0; y < _bh; y++)
                Array.Copy(source, y * TempChunk * 4, bytes, y * _bw * 4, _bw * 4);
        }
        using var image = Image.CreateFromData(_bw, _bh, false, Image.Format.Rgba8, bytes);
        if (texture is not null && texture.GetWidth() == _bw && texture.GetHeight() == _bh)
        {
            texture.Update(image);
            return texture;
        }
        texture?.Dispose();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>試玩回饋 2026-11-05 之 7（畫面刺眼）：自然景觀整體降飽和、略調暗；建築另外畫、保持原色。</summary>
    private static Color Adjust(Color c)
    {
        var gray = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
        return new Color(Mathf.Lerp(gray, c.R, TempSaturation) * TempBrightness, Mathf.Lerp(gray, c.G, TempSaturation) * TempBrightness,
            Mathf.Lerp(gray, c.B, TempSaturation) * TempBrightness, c.A);
    }

    private void Put(int x, int y, Color c, bool raw = false)
    {
        if (x < _bx || y < _by || x >= _bx + _bw || y >= _by + _bh)
            return;
        if (!raw)
            c = Adjust(c);
        var i = ((y - _by) * TempChunk + x - _bx) * 4;
        var target = _toDetail ? _detailBuf : _buf;
        _wroteDetail |= _toDetail;
        target[i] = (byte)(c.R * 255f);
        target[i + 1] = (byte)(c.G * 255f);
        target[i + 2] = (byte)(c.B * 255f);
        target[i + 3] = (byte)(c.A * 255f);
    }

    /// <summary>把底圖上一個像素調暗（落地陰影）。</summary>
    private void Shade(int x, int y, float amount)
    {
        if (x < _bx || y < _by || x >= _bx + _bw || y >= _by + _bh)
            return;
        var i = ((y - _by) * TempChunk + x - _bx) * 4;
        for (var k = 0; k < 3; k++)
            _buf[i + k] = (byte)(_buf[i + k] * (1f - amount));
    }

    // 【TEMP】WorldBox 式調色盤（每種 2～3 個深淺）；生態區各有一組。
    private static readonly Color[][] GrassBy =
    {
        new Color[] { new("4f9a34"), new("5aa83c"), new("67b649") },   // 溫帶
        new Color[] { new("5aaa3e"), new("66b648"), new("73c152") },   // 花原
        new Color[] { new("8c9f44"), new("98aa4c"), new("a4b456") },   // 乾草原
        new Color[] { new("4f9a34"), new("5aa83c"), new("67b649") },
        new Color[] { new("4f9a34"), new("5aa83c"), new("67b649") }
    };
    private static readonly Color[][] FloorBy =
    {
        new Color[] { new("2f6a26"), new("367429") },
        new Color[] { new("2f6a26"), new("367429") },
        new Color[] { new("2f6a26"), new("367429") },
        new Color[] { new("27552a"), new("2d5f30") },   // 針葉林
        new Color[] { new("5b6a2c"), new("677430") }    // 紅葉林
    };
    private static readonly Color[] LeafShades = { new("1f4f1f"), new("2e7a2c"), new("4fa03c") };
    private static readonly Color[] PineShades = { new("1b4527"), new("2a6634"), new("438445") };
    private static readonly Color[] AutumnShades = { new("7a3014"), new("b4501c"), new("e08a2a") };
    private static readonly Color[] RockShades = { new("5c564c"), new("7b7468"), new("a59d8e") };
    private static readonly Color[] HillGround = { new("7d7150"), new("8a7d58"), new("978a62") };
    private static readonly Color[] SeaShades = { new("6aaee0"), new("5a9ad2"), new("4a85c4"), new("3c72b6"), new("335fa6") };
    private static readonly Color[] RiverShades = { new("63a3dc"), new("4f8fd0") };
    private static readonly Color[] Sand = { new("d8c07a"), new("e3cf8a") };
    private static readonly Color[] Soil = { new("7a4e2a"), new("8a5a30"), new("94653a") };
    private static readonly Color[] Crops = { new("d8b84a"), new("8cbf3c"), new("c9a63c"), new("a5c94a") };
    private static readonly Color[] Dry = { new("a8923f"), new("b7a24f") };
    private static readonly Color[] Ash = { new("2c2624"), new("3b3330"), new("54504b") };
    private static readonly Color[] Petals = { new("f2e15c"), new("e86a8a"), new("f4f1ea"), new("9a7be0") };
    private static readonly Color Ember = new("ff8a1e"), EmberDark = new("d9481a"), FloodWater = new("5b98d4"), OreSpeck = new("e0aa40");
    private static readonly Color Stone = new("a7a596"), Trunk = new("5a3a1e"), ScarredSoil = new("b7a582");

    private static Color Pick(Color[] shades, float t) => shades[Math.Clamp((int)(t * shades.Length), 0, shades.Length - 1)];

    /// <summary>一個圖像素的底色（地形、生態區、離岸距離、開發度、天災疊層）。每個方格一個深淺，方格內只有零星變化。</summary>
    private Color Ground(int x, int y)
    {
        var tx = x / TempTile;
        var ty = y / TempTile;
        var tile = ty * _tilesX + tx;
        var owner = _paint[tile];
        var codes = _codes!;
        var code = codes[owner * 4];
        var overlay = codes[owner * 4 + 1];
        var amount = codes[owner * 4 + 2] / 255f;
        var dev = codes[owner * 4 + 3] / 255f;
        var groundKind = _ground.Count > 0 && _ground.TryGetValue(tile, out var gk) ? gk : (byte)0;
        var bx = x / K;
        var by = y / K;
        var h = Hash(bx, by, 1);
        var patch = Mathf.Clamp(Noise(tx / 5f, ty / 5f, 3) * 0.8f + Hash(tx, ty, 2) * 0.25f - 0.05f, 0f, 0.999f);
        if (h < 0.06f)
            patch = Mathf.Clamp(patch + (h < 0.03f ? -0.34f : 0.34f), 0f, 0.999f); // 方格內零星的深淺點
        var biome = (int)BiomeOfTile(tile);
        var sea = _seaDist[tile];
        _groundRaw = false;
        var art = Art;
        Color c;
        switch ((Code)code)
        {
            case Code.Sea:
                // 海依離岸距離分圈：1～2 淺海、3～5 海、更遠深海（第五批素材；越遠越深）。缺素材時 1、2、3～4、5～7、更遠五圈色。
                if (Tex(sea <= 2 ? art.SeaShallow : sea <= 5 ? art.Sea : art.SeaDeep, tx, ty, x, y) is { } seaArt)
                    c = sea > 9 ? seaArt.Darkened(Math.Min(0.25f, (sea - 9) * 0.03f)) : seaArt;
                else
                    c = SeaShades[sea switch { <= 1 => 0, 2 => 1, <= 4 => 2, <= 7 => 3, _ => 4 }];
                break;
            case Code.River:
                c = Tex(sea <= 1 ? art.RiverShallow : art.River, tx, ty, x, y) ?? (sea <= 1 ? RiverShades[0] : RiverShades[1]);
                break;
            case Code.Shallow:
                c = Tex(art.Ford, tx, ty, x, y) ?? (h < 0.12f ? Stone : RiverShades[0]);
                break;
            default:
                if (groundKind != 0)
                {
                    c = GroundColor(groundKind, x, y);
                    break;
                }
                if (sea == 1 || sea == 2 && Hash(tx, ty, 4) < 0.4f)
                {
                    c = Tex(sea == 1 ? art.SandWet : art.Sand, tx, ty, x, y) ?? Pick(Sand, patch); // 岸邊 1～2 方格沙灘（貼水的是濕沙）
                    break;
                }
                c = LandArt((Code)code, codes[owner * 4 + 3], (Biome)biome, tx, ty, x, y) ?? (Code)code switch
                {
                    Code.Forest or Code.Logged => Pick(FloorBy[biome], patch),
                    Code.Hills or Code.HillsOre or Code.HillsMining or Code.MinePit => Pick(HillGround, patch),
                    Code.Farmland => Plot(x, y) ?? Pick(GrassBy[biome], patch),
                    Code.Clearing when codes[owner * 4 + 3] == SettlementGround => Pick(GrassBy[biome], patch), // 聚落格裡廣場、道路、屋子以外是草地
                    Code.Clearing => dev > 0f ? Plot(x, y) ?? Pick(GrassBy[biome], patch) : Pick(Soil, patch),
                    Code.DryGrass => Pick(Dry, patch),
                    Code.Burning or Code.Scorched => Pick(Ash, patch),
                    _ => Pick(GrassBy[biome], patch)
                };
                if (_waterDist[tile] == 1 && code != (byte)Code.Farmland)
                    c = c.Darkened(0.10f); // 河岸、海岸內一圈稍暗
                if ((code == (byte)Code.HillsOre || code == (byte)Code.HillsMining && Hash(bx, by, 10) > dev) && Hash(bx, by, 9) < 0.04f)
                    c = OreSpeck;
                break;
        }
        // 天災疊層：依強度把一部分像素換掉（像素風的漸變）。
        var hit = Hash(bx, by, 5) < amount;
        switch ((DemoHexMap.Overlay)overlay)
        {
            case DemoHexMap.Overlay.Fire when hit:
                _groundRaw = false;
                c = Hash(bx, by, 6) < 0.25f ? (Hash(bx, by, 7) < 0.5f ? Ember : EmberDark) : Pick(Ash, patch);
                break;
            case DemoHexMap.Overlay.Scorch when hit && !IsWater(code):
                c = Tex(Art.Ash, tx, ty, x, y) ?? Pick(Ash, patch);
                break;
            case DemoHexMap.Overlay.Dry when hit && !IsWater(code):
                c = Tex(Art.GrassSteppe, tx, ty, x, y) ?? Pick(Dry, patch);
                break;
            case DemoHexMap.Overlay.Flood when amount > 0.85f ? hit : Noise(x / 22f, y / 22f, 58) < amount * 0.7f:
                c = Tex(Art.RiverShallow, tx, ty, x, y) ?? FloodWater; // 高水位時一片汪洋；退水時剩下塊狀的積水（第 08-18b 章第 5 節）
                break;
            case DemoHexMap.Overlay.Scar when !IsWater(code) && Hash(tx, ty, 57) < amount * 0.8f:
                c = c.Lerp(ScarredSoil, 0.55f); // 龍捲風刮淡的地面（以方格為單位，看得出一條掃過的帶子）
                break;
            case DemoHexMap.Overlay.Ore when Hash(bx, by, 9) < 0.05f:
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
    /// 開墾中：依進度一塊塊從草地→翻土→播種→綠苗。作物顏色每塊一種，畦溝橫向深淺相間。
    /// 回傳 null＝這個像素不是田也不是田埂（畫草地）。
    /// </summary>
    public const int TempPlotTilesX = 3, TempPlotTilesY = 3;

    private Color? Plot(int x, int y)
    {
        var codes = _codes!;
        var tx = x / TempTile;
        var ty = y / TempTile;
        var px = tx / TempPlotTilesX;
        var py = ty / TempPlotTilesY;
        var lx = tx - px * TempPlotTilesX;
        var ly = ty - py * TempPlotTilesY;
        bool FieldTile(int ax, int ay)
        {
            var o = TileOwner(ax, ay);
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
            return beside ? Tex(Art.Ridge, tx, ty, x, y) ?? Ridge : null;
        }
        if (!Whole(px, py))
            return null;
        // 一塊田只有一個階段：依田區左上方格所屬的格。
        var first = TileOwner(px * TempPlotTilesX, py * TempPlotTilesY);
        var dev = codes[first * 4] == (byte)Code.Farmland ? 1f : codes[first * 4 + 3] / 255f;
        var ix = x - px * TempPlotTilesX * TempTile;
        var iy = y - py * TempPlotTilesY * TempTile;
        var edge = ix <= 1 || iy <= 1 || ix >= (TempPlotTilesX - 1) * TempTile - 2 || iy >= (TempPlotTilesY - 1) * TempTile - 2;
        var p = Hash(px, py, 13);
        var furrow = iy / 3 % 2 == 0;
        Color c;
        if (FieldArt(dev, p, px, py, x, y) is { } fieldArt)
        {
            if (fieldArt.A <= 0f)
                return null; // 還沒開墾到這一塊（畫草地）
            c = fieldArt;
        }
        else if (dev >= 1f)
        {
            var crop = p < 0.65f ? Crops[0] : p < 0.85f ? Crops[2] : Crops[1];
            c = furrow ? crop : crop.Darkened(0.12f);
        }
        else if (p > dev * 1.6f)
            return null; // 還沒開墾到這一塊（畫草地）
        else if (dev < 0.5f)
            c = furrow ? Soil[1] : Soil[2]; // 翻土
        else if (dev < 0.8f)
            c = furrow && Hash(x / K, y / K, 14) < 0.35f ? Crops[1] : (furrow ? Soil[1] : Soil[2]); // 播種、冒出嫩苗
        else
            c = furrow ? Crops[1] : Crops[1].Darkened(0.12f); // 綠苗
        return edge ? c.Darkened(0.22f) : c;
    }

    private static readonly Color Ridge = new("6f8a3a");

    // ───────────── 疊在底色上的樹、山頭、礦石 ─────────────

    // 圖樣：每列由上而下；. 空、d 暗、m 中、l 亮、t 樹幹。錨點＝底部中央。
    private static readonly string[] RoundTree = { ".mml.", "mmmll", "dmmml", "ddmmd", ".ddd.", "..t.." };
    private static readonly string[] PineTree = { "..l..", ".mml.", ".mmm.", "mmmlm", "ddmdd", "..t.." };
    private static readonly string[] Bush = { "mlm", "ddd" };
    private static readonly string[] Peak = { "...l...", "..llm..", ".llmmd.", "lmmmmdd" };
    private static readonly string[] BigPeak = { "....l....", "...llm...", "..lllmd..", ".llmmmdd.", "lmmmmmmdd" };

    private readonly record struct Tree(bool Pine, bool Small, Color[] Shades);

    /// <summary>試玩回饋 2026-11-06 之 4（物件蓋住樹頂，透視奇怪）：聚落地面四周不長樹——
    /// 房子、城牆往上長出自己的方格，所以地面上方三方格、左右與下方一方格內也不長樹（樹不會被屋頂蓋住，也不會擋在房子前面）。</summary>
    private HashSet<int> _treeless = new();

    private void BuildTreeless()
    {
        _treeless = new HashSet<int>();
        foreach (var tile in _ground.Keys)
        {
            var tx = tile % _tilesX;
            var ty = tile / _tilesX;
            for (var dy = -3; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                    if (tx + dx >= 0 && tx + dx < _tilesX && ty + dy >= 0 && ty + dy < _tilesY)
                        _treeless.Add((ty + dy) * _tilesX + tx + dx);
        }
    }

    /// <summary>近景的小人與動物站在這個方格上會不會被樹或山頭擋住、或是站進水裡（試玩回饋 2026-11-06 之 4、5）：
    /// 本格與下方兩列（左右各一方格）沒有樹與山頭，而且本格是陸地才算空地。</summary>
    public bool OpenTile(int tx, int ty)
    {
        if (_codes is null || tx < 0 || ty < 0 || tx >= _tilesX || ty >= _tilesY)
            return false;
        if (IsWater(PaintCode(ty * _tilesX + tx)))
            return false;
        for (var dy = 0; dy <= 2; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var x = tx + dx;
                var y = ty + dy;
                if (x < 0 || y < 0 || x >= _tilesX || y >= _tilesY)
                    continue;
                if (TreeAt(x, y) is not null || PeakAt(x, y) > 0)
                    return false;
            }
        return true;
    }

    /// <summary>這個方格是不是水（畫面上的，含參差）。</summary>
    public bool WaterTile(int tx, int ty)
        => _codes is not null && tx >= 0 && ty >= 0 && tx < _tilesX && ty < _tilesY && IsWater(PaintCode(ty * _tilesX + tx));

    /// <summary>這個方格有沒有樹（一方格最多一棵）：森林大多有、林緣變疏、草原偶有一棵。聚落地面上沒有樹。</summary>
    private Tree? TreeAt(int tx, int ty)
    {
        var tile = ty * _tilesX + tx;
        if (_ground.Count > 0 && _ground.ContainsKey(tile) || _seaDist[tile] <= 1 || _treeless.Contains(tile))
            return null;
        var cell = _paint[tile];
        var code = (Code)_codes![cell * 4];
        var h = Hash(tx, ty, 20);
        var biome = (Biome)_biome[cell];
        if (code == Code.Forest)
        {
            var edge = tx > 0 && PaintCode(tile - 1) != (byte)Code.Forest || tx < _tilesX - 1 && PaintCode(tile + 1) != (byte)Code.Forest
                || ty > 0 && PaintCode(tile - _tilesX) != (byte)Code.Forest || ty < _tilesY - 1 && PaintCode(tile + _tilesX) != (byte)Code.Forest;
            if (h > (edge ? 0.5f : 0.88f))
                return null;
        }
        else if (code == Code.Logged)
        {
            // 砍過的森林：依長回進度（第 4 個位元組），長回的方格才有樹。
            if (Hash(tx, ty, 21) * 0.5f > _codes[cell * 4 + 3] / 255f - 0.5f || h > 0.85f)
                return null;
        }
        else if (code is Code.Grassland or Code.DryGrass)
        {
            if (h > 0.03f)
                return null;
        }
        else
            return null;
        var kind = Hash(tx, ty, 23);
        return biome switch
        {
            Biome.Conifer => new Tree(kind < 0.85f, kind > 0.93f, PineShades),
            Biome.Autumn => new Tree(kind > 0.85f, kind > 0.93f, kind > 0.85f ? PineShades : AutumnShades),
            _ => new Tree(kind > 0.8f, kind > 0.92f, kind > 0.8f ? PineShades : LeafShades)
        };
    }

    /// <summary>丘陵的山頭：0 沒有、1 小、2 大（平均約每格一兩座，散布不排成格子）。</summary>
    private int PeakAt(int tx, int ty)
    {
        var tile = ty * _tilesX + tx;
        var code = (Code)PaintCode(tile);
        if (code is not (Code.Hills or Code.HillsOre or Code.HillsMining or Code.MinePit) || _seaDist[tile] <= 1
            || _ground.Count > 0 && _ground.ContainsKey(tile))
            return 0;
        var h = Hash(tx, ty, 30);
        return h < 0.05f ? 2 : h < 0.14f ? 1 : 0;
    }

    private void StampTile(int tx, int ty)
    {
        var tile = ty * _tilesX + tx;
        var cell = _paint[tile];
        var codes = _codes!;
        var code = (Code)codes[cell * 4];
        var overlay = (DemoHexMap.Overlay)codes[cell * 4 + 1];
        var amount = codes[cell * 4 + 2] / 255f;
        var fire = overlay is DemoHexMap.Overlay.Fire or DemoHexMap.Overlay.Scorch;
        var groundTile = _ground.Count > 0 && _ground.ContainsKey(tile);
        // 錨點：方格下緣附近、左右稍微錯開（固定雜湊）。
        var ax = tx * TempTile + TempTile / 2 + (int)((Hash(tx, ty, 24) - 0.5f) * 6f);
        var ay = ty * TempTile + TempTile - 2 - (int)(Hash(tx, ty, 25) * 3f);
        var hit = Hash(tx, ty, 5) < amount;

        var scarred = overlay == DemoHexMap.Overlay.Scar && Hash(tx, ty, 57) < amount * 0.8f;
        if (TreeAt(tx, ty) is { } tree)
        {
            if (scarred)
                DeadTree(ax, ay); // 第 08-18b 章第 5 節：龍捲風刮過，樹只剩枯枝
            else if (fire && hit)
            {
                if (((Hash(tx, ty, 26) < 0.6f ? PixelParts.Variant("px_tree_burnt", 2, Hash(tx, ty, 28)) : PixelParts.Variant("px_stump", 2, Hash(tx, ty, 28)))
                    ?? PixelParts.Get(Hash(tx, ty, 26) < 0.6f ? "px_tree_burnt" : "px_stump")) is { } burnt)
                    StampSprite(burnt, ax, ay);
                else
                    Block(ax - K / 2, ay - 3 * K, K, 3 * K, Ash[0]);
            }
            else if (!(overlay == DemoHexMap.Overlay.Flood && hit))
                DrawTree(tree, ax, ay, tx, ty);
        }
        else if (code == Code.Logged && !groundTile && _seaDist[tile] > 1)
        {
            // 砍過還沒長回的方格：樹樁或幼樹。
            var stage = codes[cell * 4 + 3] / 255f - Hash(tx, ty, 21) * 0.5f;
            var part = PixelParts.Variant(stage > 0.1f ? "px_sapling" : "px_stump", 2, Hash(tx, ty, 28)) ?? PixelParts.Get(stage > 0.1f ? "px_sapling" : "px_stump");
            if (stage <= 0.1f && Hash(tx, ty, 27) > 0.9f && PixelParts.Get("px_log") is { } log)
                StampSprite(log, ax, ay); // 第 08-18d 章：砍下來還沒運走的原木
            else if (Hash(tx, ty, 27) < 0.6f)
            {
                if (part is not null)
                    StampSprite(part, ax, ay);
                else
                    Block(ax - K / 2, ay - (stage > 0.1f ? 2 : 1) * K, K, (stage > 0.1f ? 2 : 1) * K, stage > 0.1f ? LeafShades[2] : Trunk);
            }
        }

        if (PeakAt(tx, ty) is var peak and > 0)
        {
            // 第 08-18d 章：第五批的丘陵山頭（四種）；大山頭用整張，小山頭只在缺素材時畫小的。
            var art = PixelParts.Variant("px_hill", 4, Hash(tx, ty, 31)) ?? PixelParts.Get(peak == 2 ? "px_mountain_1" : "px_mountain_2");
            ShadowEllipse(ax + 2 * K, ay, art is { Scale: 1 } ? art.Width / 2 - K : peak == 2 ? 5 * K : 3 * K, K + 2);
            if (art is not null)
                StampSprite(art, ax, ay);
            else
                Stamp(peak == 2 ? BigPeak : Peak, ax, ay, RockShades, code is Code.HillsOre or Code.HillsMining);
        }

        var lx = tx % TilesPerCell;
        var ly = ty % TilesPerCell;
        var center = lx == TilesPerCell / 2 && ly == TilesPerCell / 2 && _paint[tile] == TileOwner(tx, ty);
        if (code is Code.HillsOre or Code.HillsMining && !groundTile && Hash(tx, ty, 34) < 0.22f * (code == Code.HillsMining ? 1f - codes[cell * 4 + 3] / 255f : 1f))
        {
            if (PixelParts.Variant("px_ore", 4, Hash(tx, ty, 35)) is { } ore)
                StampSprite(ore, ax, ay);
            else
                Block(ax - K, ay - K, 2 * K, K, OreSpeck);
        }
        if (center && code is Code.HillsMining or Code.MinePit)
        {
            // 第 08-16 章第 14 節：礦坑在格中央。
            if (((code == Code.HillsMining ? PixelParts.Get("px_mine_active") : null) ?? PixelParts.Get("px_mine_pit")) is { } pit)
                StampSprite(pit, ax, ay);
            else
                Block(ax - 2 * K, ay - 2 * K, 4 * K, 2 * K, Ash[1]);
        }
        if (center && overlay == DemoHexMap.Overlay.Ore)
        {
            if (PixelParts.Get("px_ore_meteor") is { } meteor)
                StampSprite(meteor, ax, ay);
            else
                Block(ax - K, ay - 2 * K, 2 * K, 2 * K, OreSpeck);
        }

        // 細節層（遠看不畫）：草叢、野花、小石頭、天災留在地上的零件。
        if (groundTile || _seaDist[tile] <= 1)
            return;
        var d = Hash(tx, ty, 40);
        _toDetail = true;
        if (code is Code.Grassland or Code.DryGrass && !(fire && hit) && !(overlay == DemoHexMap.Overlay.Flood && hit))
        {
            var meadow = BiomeOfTile(tile) == Biome.Meadow;
            var dry = code == Code.DryGrass || overlay == DemoHexMap.Overlay.Dry && hit;
            // 試玩回饋 2026-11-06 之 1（花太多、太雜）：花原 7%、一般草原 1.5% 的方格有花，草叢 12%。
            if (d < (meadow ? 0.07f : 0.015f) && !dry)
                Flowers(tx, ty, meadow);
            else if (_waterDist[tile] == 1 && d < 0.3f && PixelParts.Variant("px_reed", 2, Hash(tx, ty, 41)) is { } reed)
                StampSprite(reed, ax, ay); // 第 08-18d 章：水邊的蘆葦
            else if (d < 0.14f)
            {
                var tuft = (dry ? PixelParts.Variant("px_dry_tuft", 2, Hash(tx, ty, 41)) : PixelParts.Variant("px_grass_tuft", 4, Hash(tx, ty, 41)))
                    ?? PixelParts.Get(dry ? "px_dry_tuft" : d < 0.22f ? "px_grass_tuft_1" : "px_grass_tuft_2");
                if (tuft is not null)
                    StampSprite(tuft, ax, ay);
                else
                    Tuft(ax, ay, dry ? Dry[0] : GrassBy[(int)BiomeOfTile(tile)][0]);
            }
        }
        else if (code is Code.Forest && d < 0.03f && TreeAt(tx, ty) is null && PixelParts.Variant("px_mushroom", 2, Hash(tx, ty, 41)) is { } mushroom)
            StampSprite(mushroom, ax, ay); // 第 08-18d 章：林地上偶爾一叢蘑菇
        else if (code == Code.Hills && d < 0.12f)
        {
            if ((d < 0.015f ? PixelParts.Variant("px_rock_big", 2, Hash(tx, ty, 41)) : PixelParts.Variant("px_rock", 4, Hash(tx, ty, 41))) is { } rock)
                StampSprite(rock, ax, ay);
            else
                Block(ax - K / 2, ay - K, K + 2, K, RockShades[2]);
        }
        if (overlay == DemoHexMap.Overlay.Scorch && hit && d < 0.3f && (PixelParts.Get("dec_ash") ?? PixelParts.Get("px_ash")) is { } ash)
            StampSprite(ash, ax, ay);
        if (overlay == DemoHexMap.Overlay.Flood && hit && d < 0.35f && (PixelParts.Get("dec_puddle") ?? PixelParts.Get("px_flood_ripple")) is { } ripple)
            StampSprite(ripple, ax, ay);
        _toDetail = false;
    }

    /// <summary>一棵樹：右下落地陰影，再畫樹（有素材用素材，紅葉林把綠葉染成秋色；沒有就用程式畫的圖樣）。</summary>
    private void DrawTree(Tree tree, int ax, int ay, int tx, int ty)
    {
        if (TreeArt(tree, BiomeOfTile(ty * _tilesX + tx), tx, ty) is { } fresh)
        {
            // 第 08-18d 章：第五批的樹（依生態區：闊葉、針葉、紅葉、金葉、白樺；小的是灌木）。
            ShadowEllipse(ax + fresh.Width / 6, ay, fresh.Width * 3 / 8, K);
            StampSprite(fresh, ax, ay);
            return;
        }
        ShadowEllipse(ax + 2 * K, ay, tree.Small ? 2 * K : 3 * K, K);
        var autumn = ReferenceEquals(tree.Shades, AutumnShades);
        var name = tree.Small ? "px_bush" : tree.Pine ? (Hash(tx, ty, 28) < 0.5f ? "px_tree_pine_1" : "px_tree_pine_2")
            : (Hash(tx, ty, 28) < 0.5f ? "px_tree_round_1" : "px_tree_round_2");
        if (PixelParts.Get(name) is { } art)
            StampSprite(art, ax, ay, autumn);
        else
            Stamp(tree.Small ? Bush : tree.Pine ? PineTree : RoundTree, ax, ay, tree.Shades);
    }

    /// <summary>枯樹：光禿的樹幹與幾根枯枝（程式畫；第五批素材到了再換）。</summary>
    private void DeadTree(int ax, int ay)
    {
        if (PixelParts.Variant("px_tree_dead", 2, Hash(ax, ay, 32)) is { } dead)
        {
            ShadowEllipse(ax + 2 * K, ay, 2 * K, K);
            StampSprite(dead, ax, ay);
            return;
        }
        ShadowEllipse(ax + 2 * K, ay, 2 * K, K);
        var bark = new Color("4a3524");
        Block(ax - 2, ay - 5 * K, 4, 5 * K, bark);
        Block(ax - 3 * K, ay - 4 * K, 3 * K, 2, bark);
        Block(ax, ay - 3 * K - 2, 3 * K, 2, bark);
        Block(ax - 3 * K, ay - 5 * K, 2, K, bark);
        Block(ax + 3 * K - 2, ay - 4 * K - 2, 2, K, bark);
    }

    private void ShadowEllipse(int cx, int cy, int rx, int ry)
    {
        for (var y = -ry; y <= ry; y++)
            for (var x = -rx; x <= rx; x++)
                if (x * x * ry * ry + y * y * rx * rx <= rx * rx * ry * ry)
                    Shade(cx + x, cy + y, 0.28f);
    }

    private void Block(int x, int y, int w, int h, Color c)
    {
        for (var j = 0; j < h; j++)
            for (var i = 0; i < w; i++)
                Put(x + i, y + j, c);
    }

    private void Flowers(int tx, int ty, bool meadow)
    {
        // 第 08-18d 章：第五批的花（六種小花；花原偶爾一叢大花）。
        var flower = meadow && Hash(tx, ty, 42) < 0.25f ? PixelParts.Variant("px_flower_big", 3, Hash(tx, ty, 43)) : PixelParts.Variant("px_flower", 6, Hash(tx, ty, 43));
        if (flower is not null)
        {
            StampSprite(flower, tx * TempTile + TempTile / 2, ty * TempTile + TempTile - 2);
            return;
        }
        if (PixelParts.Get("px_flowers") is { } art && !meadow)
        {
            StampSprite(art, tx * TempTile + TempTile / 2, ty * TempTile + TempTile - 2);
            return;
        }
        // 程式畫的小花：方格內幾個 2×2 色點（花原較多、顏色依位置）。
        var n = meadow ? 3 : 2;
        for (var k = 0; k < n; k++)
        {
            var x = tx * TempTile + 2 + (int)(Hash(tx, ty, 50 + k) * (TempTile - 4));
            var y = ty * TempTile + 2 + (int)(Hash(tx, ty, 60 + k) * (TempTile - 4));
            Block(x, y + 2, 1, 2, GrassBy[0][0]);
            Block(x - 1, y, 2, 2, Petals[(int)(Hash(tx, ty, 70 + k) * Petals.Length) % Petals.Length]);
        }
    }

    private void Tuft(int ax, int ay, Color c)
    {
        for (var k = -1; k <= 1; k++)
            Block(ax + k * 3, ay - 4 - (k == 0 ? 2 : 0), 1, 4 + (k == 0 ? 2 : 0), c.Darkened(0.2f));
    }

    /// <summary>把像素零件疊進地表（第四批零件每像素放大兩倍）：底部置中在錨點，只畫不透明的像素；<paramref name="autumn"/>＝綠葉染成秋色。</summary>
    private void StampSprite(PixelParts.Sprite sprite, int ax, int ay, bool autumn = false)
    {
        var s = sprite.Scale;
        var raw = s == 1; // 第五批素材照原色，舊素材照舊降彩度
        var left = ax - sprite.Width * s / 2;
        var top = ay - sprite.Height * s + 1;
        for (var y = 0; y < sprite.Height; y++)
            for (var x = 0; x < sprite.Width; x++)
            {
                var c = sprite.At(x, y);
                if (c.A < 0.5f)
                    continue;
                var color = new Color(c.R, c.G, c.B);
                if (autumn && c.G > c.R && c.G > c.B)
                    color = new Color(Mathf.Clamp(c.G * 1.35f, 0f, 1f), c.G * 0.62f, c.B * 0.35f);
                for (var dy = 0; dy < s; dy++)
                    for (var dx = 0; dx < s; dx++)
                        Put(left + x * s + dx, top + y * s + dy, color, raw);
            }
    }

    private void Stamp(string[] shape, int ax, int ay, Color[] shades, bool ore = false)
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
                    't' => Trunk,
                    _ => null
                };
                if (c is { } color)
                    Block(x, y, K, K, color);
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
