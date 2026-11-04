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
        Farmland = 7, Clearing = 8, Burning = 9, Scorched = 10, DryGrass = 11
    }

    /// <summary>【TEMP】每格半徑幾個圖像素；地塊交界的扭曲幅度（格半徑的倍數）。</summary>
    public const int TempTexelsPerRadius = 10;
    public const float TempWarp = 0.38f;

    private const float Sqrt3 = 1.7320508f;

    private ImageTexture? _texture;
    private byte[]? _pixels;
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
        if (!Active || _texture is null)
            return;
        var scale = _radius / TempTexelsPerRadius;
        var topLeft = _origin + new Vector2(-Sqrt3 / 2f, -1f) * _radius;
        DrawTextureRect(_texture, new Rect2(topLeft, new Vector2(_width, _height) * scale), false);
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
            BuildOwners();
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
        if (!painted && _texture is not null)
            return; // 沒有任何格改變：不重新上傳
        using var image = Image.CreateFromData(_width, _height, false, Image.Format.Rgba8, _pixels!);
        if (_texture is not null && !full)
            _texture.Update(image);
        else
        {
            _texture?.Dispose();
            _texture = ImageTexture.CreateFromImage(image);
        }
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
        Array.Fill(dist, (byte)7);
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
            if (dist[i] >= 6)
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
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                Put(x, y, Ground(codes, x, y));
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
        var i = (y * _width + x) * 4;
        _pixels![i] = (byte)(c.R * 255f);
        _pixels[i + 1] = (byte)(c.G * 255f);
        _pixels[i + 2] = (byte)(c.B * 255f);
        _pixels[i + 3] = (byte)(c.A * 255f);
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
        var h = Hash(x, y, 1);
        // 斑塊：大範圍平滑雜訊＋一點點逐像素變化（像素風的草地，不是噪點）。
        var patch = Mathf.Clamp(Noise(x / 9f, y / 9f, 3) * 0.85f + h * 0.3f - 0.07f, 0f, 0.999f);
        Color c;
        switch ((Code)code)
        {
            case Code.Sea:
                c = SeaShades[Math.Clamp(_seaDist![i] - 1 + (h < 0.08f ? -1 : 0), 0, SeaShades.Length - 1)];
                break;
            case Code.River:
                c = _seaDist![i] <= 1 ? RiverShades[0] : RiverShades[1];
                break;
            case Code.Shallow:
                c = h < 0.12f ? Stone : RiverShades[0];
                break;
            default:
                if (_seaDist![i] <= 2 && (_seaDist[i] <= 1 || h < 0.5f))
                {
                    c = Pick(Sand, patch); // 海邊一圈沙灘
                    break;
                }
                c = (Code)code switch
                {
                    Code.Forest => Pick(ForestFloor, patch),
                    Code.Hills or Code.HillsOre => Pick(HillGround, patch),
                    Code.Farmland => Field(x, y, 1f),
                    Code.Clearing => dev > 0f ? Field(x, y, dev) : Pick(Soil, patch),
                    Code.DryGrass => Pick(Dry, patch),
                    Code.Burning or Code.Scorched => Pick(Ash, patch),
                    _ => Pick(Grass, patch)
                };
                if (_waterDist![i] == 1 && code != (byte)Code.Farmland)
                    c = c.Darkened(0.12f); // 河岸一圈稍暗
                if (code == (byte)Code.HillsOre && Hash(x, y, 9) < 0.05f)
                    c = OreSpeck;
                break;
        }
        // 天災疊層：依強度把一部分像素換掉（像素風的漸變）。
        var hit = Hash(x, y, 5) < amount;
        switch ((DemoHexMap.Overlay)overlay)
        {
            case DemoHexMap.Overlay.Fire when hit:
                c = Hash(x, y, 6) < 0.25f ? (Hash(x, y, 7) < 0.5f ? Ember : EmberDark) : Pick(Ash, patch);
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
            case DemoHexMap.Overlay.Ore when Hash(x, y, 9) < 0.06f:
                c = OreSpeck;
                break;
            case DemoHexMap.Overlay.Debris or DemoHexMap.Overlay.Cracks or DemoHexMap.Overlay.Crater when hit && Hash(x, y, 8) < 0.4f:
                c = c.Darkened(0.4f);
                break;
        }
        return c;
    }

    /// <summary>田畦：全圖共用的 6×4 田塊格（相鄰的田連成一片），每塊一種作物、隔行深淺；
    /// <paramref name="dev"/>＜1 時依開發度從草地→翻開的泥土→田畦。</summary>
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
        if (x % 6 == 0 || y % 4 == 0)
            return Soil[0]; // 田埂
        var crop = Crops[(int)(Hash(x / 6, y / 4, 12) * Crops.Length) % Crops.Length];
        return y % 2 == 0 ? crop : crop.Darkened(0.12f);
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
        // 燒過、淹水的地方不畫樹（依疊層強度一棵棵消失）。
        bool Burnt(int x, int y) => overlay is DemoHexMap.Overlay.Fire or DemoHexMap.Overlay.Scorch or DemoHexMap.Overlay.Flood
                                    && Hash(x, y, 5) < amount;
        switch (code)
        {
            case Code.Forest:
            case Code.Grassland:
            {
                // 樹：交錯網格（每 4 像素一棵、隨機偏移）；草原上零星幾棵。
                const int step = 4;
                var reach = TempTexelsPerRadius + 2;
                var spots = new List<(int X, int Y, float H)>();
                for (var gy = (int)center.Y - reach; gy <= center.Y + reach; gy += step)
                    for (var gx = (int)center.X - reach + (gy / step & 1) * 2; gx <= center.X + reach; gx += step)
                    {
                        var h = Hash(gx, gy, 20);
                        var x = gx + (int)(Hash(gx, gy, 21) * 3f) - 1;
                        var y = gy + (int)(Hash(gx, gy, 22) * 3f) - 1;
                        if (x < 0 || y < 0 || x >= _width || y >= _height || _owner![y * _width + x] != cell)
                            continue;
                        if (code == Code.Grassland && h > 0.035f)
                            continue;
                        if (_seaDist![y * _width + x] <= 2 || _waterDist![y * _width + x] <= 1 || Burnt(x, y))
                            continue;
                        spots.Add((x, y, h));
                    }
                foreach (var (x, y, h) in spots.OrderBy(s => s.Y).ThenBy(s => s.X))
                {
                    var kind = Hash(x, y, 23);
                    if (kind < 0.55f)
                        Stamp(RoundTree, x, y, LeafShades, clip);
                    else if (kind < 0.88f)
                        Stamp(PineTree, x, y, PineShades, clip);
                    else
                        Stamp(Bush, x, y, LeafShades, clip);
                }
                break;
            }
            case Code.Hills:
            case Code.HillsOre:
            {
                // 山頭：每格一大一小（位置依格固定），礦脈格的山上有幾點金黃。
                var a = Hash(cell, 0, 30);
                var big = new Vector2I((int)(center.X + (a - 0.5f) * 6f), (int)(center.Y + 2 + Hash(cell, 0, 31) * 3f));
                var small = new Vector2I((int)(center.X + (a < 0.5f ? 5 : -5)), (int)(center.Y - 3));
                foreach (var (at, shape) in new[] { (small, Peak), (big, BigPeak) })
                {
                    if (at.X < 0 || at.Y < 0 || at.X >= _width || at.Y >= _height || _owner![at.Y * _width + at.X] != cell
                        || _seaDist![at.Y * _width + at.X] <= 1)
                        continue;
                    Stamp(shape, at.X, at.Y, RockShades, clip, code == Code.HillsOre);
                }
                break;
            }
        }
    }

    private void Stamp(string[] shape, int ax, int ay, Color[] shades, Rect2I clip, bool ore = false)
    {
        var height = shape.Length;
        for (var row = 0; row < height; row++)
        {
            var line = shape[row];
            for (var col = 0; col < line.Length; col++)
            {
                var x = ax - line.Length / 2 + col;
                var y = ay - height + 1 + row;
                if (!clip.HasPoint(new Vector2I(x, y)))
                    continue;
                Color? c = line[col] switch
                {
                    'd' => shades[0],
                    'm' => ore && Hash(x, y, 33) < 0.18f ? OreSpeck : shades[1],
                    'l' => shades[2],
                    's' => shades[0],
                    't' => Trunk,
                    _ => null
                };
                if (c is { } color)
                    Put(x, y, color);
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
