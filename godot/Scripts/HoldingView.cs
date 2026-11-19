using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-33 章：領地圖（排版見 <see cref="HoldingLayout"/>）。整張圖（動畫以外）烤成一張貼圖：縮小時用線性＋縮圖鏈（不閃爍），
/// 放大時用最近鄰（像素清楚）；風車、營火、受損建築的煙另外每幀畫。滑鼠移到建築上有名稱與狀態；
/// 大圖可以滾輪縮放、左鍵拖曳平移、雙擊回到全圖；縮圖（詳情面板裡）點一下打開大圖。只讀世界，不改世界。
/// </summary>
public partial class HoldingView : Control
{
    private static readonly Dictionary<int, (string Signature, HoldingLayout.Picture Picture)> Layouts = new();

    private readonly bool _thumbnail;
    private HoldingLayout.Picture? _picture;
    private ImageTexture? _baked;
    private readonly List<HoldingLayout.Piece> _animated = new();
    private float _zoom = 1f; // 1＝整張放得下；大於 1＝放大倍數
    private Vector2 _center; // 放大時畫面中心對準的畫布座標
    private bool _dragging;
    private Vector2 _dragFrom;
    private bool _dragged;
    private ulong _lastFrame;
    private bool _dirty;

    /// <summary>縮圖被點了（打開大圖）。</summary>
    public event Action? Opened;

    /// <summary>Godot 重新載入腳本時需要無參數建構子。</summary>
    public HoldingView() : this(false)
    {
    }

    public HoldingView(bool thumbnail)
    {
        _thumbnail = thumbnail;
        MouseFilter = MouseFilterEnum.Stop;
        TooltipText = " ";
        ClipContents = true;
        MouseDefaultCursorShape = thumbnail ? CursorShape.PointingHand : CursorShape.Arrow;
        Resized += () => { ClampView(); QueueRedraw(); };
    }

    public HoldingLayout.Picture? Picture => _picture;

    /// <summary>畫面上的建築編號（冒煙測試：每棟建築都要畫出來）。</summary>
    public IReadOnlySet<int> ShownBuildings => _picture?.Pieces.Where(p => p.BuildingId >= 0).Select(p => p.BuildingId).ToHashSet() ?? new HashSet<int>();

    /// <summary>上一次排版花的毫秒數（沿用快取時是 0）。</summary>
    public double LayoutMs { get; private set; }

    /// <summary>顯示一個聚落（同一組建築沿用上次的排版，只更新進度與耐久度）。</summary>
    public void ShowScene(DemoHoldingScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var signature = HoldingLayout.Signature(scene);
        LayoutMs = 0;
        if (!Layouts.TryGetValue(scene.Site.Id, out var cached) || cached.Signature != signature)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            cached = (signature, HoldingLayout.Compose(scene));
            LayoutMs = watch.Elapsed.TotalMilliseconds;
            if (Layouts.Count >= TempLayoutCache)
                Layouts.Clear();
            Layouts[scene.Site.Id] = cached;
        }
        var changedPicture = !ReferenceEquals(_picture, cached.Picture);
        _picture = cached.Picture;
        HoldingLayout.Refresh(_picture, scene);
        _dirty = true; // 要畫的時候才烤（詳情每週重建分頁，沒打開的分頁不必烤）
        if (changedPicture)
        {
            _zoom = 1f;
            _center = (Vector2)_picture.Size / 2f;
        }
        ClampView();
        QueueRedraw();
    }

    /// <summary>【TEMP】最多記住幾個聚落的排版。</summary>
    private const int TempLayoutCache = 24;

    /// <summary>離開場景或換世界時清掉排版快取。</summary>
    public static void ReleaseCache() => Layouts.Clear();

    // ── 烤圖 ─────────────────────────────────────────────────────────────

    private void Bake()
    {
        _animated.Clear();
        if (_picture is null)
            return;
        var size = _picture.Size;
        var image = Image.CreateEmpty(Math.Max(1, size.X), Math.Max(1, size.Y), false, Image.Format.Rgba8);
        image.Fill(new Color(0.24f, 0.42f, 0.18f));
        var t = HoldingLayout.T;
        // 草地
        for (var ty = 0; ty < size.Y; ty += t)
            for (var tx = 0; tx < size.X; tx += t)
                if (PixelParts.ImageOf(_picture.GrassAt(tx, ty)) is { } grass)
                    image.BlitRect(grass, new Rect2I(0, 0, t, t), new Vector2I(tx, ty));
        // 踩實的地面（四角 Wang 過渡圖塊：圖塊編號＝西北 1＋東北 2＋西南 4＋東南 8）
        if (_picture.Wang is { } wang && PixelParts.ImageOf(wang) is { } sheet)
            for (var ty = 0; ty < size.Y; ty += t)
                for (var tx = 0; tx < size.X; tx += t)
                {
                    int vx = tx / t, vy = ty / t;
                    var mask = (_picture.Yard.Contains(new Vector2I(vx, vy)) ? 1 : 0) | (_picture.Yard.Contains(new Vector2I(vx + 1, vy)) ? 2 : 0)
                        | (_picture.Yard.Contains(new Vector2I(vx, vy + 1)) ? 4 : 0) | (_picture.Yard.Contains(new Vector2I(vx + 1, vy + 1)) ? 8 : 0);
                    if (mask != 0)
                        image.BlendRect(sheet, new Rect2I(mask % 4 * t, mask / 4 * t, t, t), new Vector2I(tx, ty));
                }
        // 道路、廣場、田、沙灘、海、丘陵、菜園
        foreach (var (at, sprite) in _picture.Ground)
            if (at.X >= 0 && at.Y >= 0 && at.X < size.X && at.Y < size.Y && PixelParts.ImageOf(sprite) is { } tile)
                image.BlendRect(tile, new Rect2I(0, 0, t, t), at);
        // 毀損建築底下的焦痕
        if (PixelParts.ImageOf("dec_scorch") is { } scorch)
            foreach (var p in _picture.Pieces.Where(p => p.Kind == HoldingLayout.Kind.Ruin))
                image.BlendRect(scorch, new Rect2I(Vector2I.Zero, scorch.GetSize()),
                    new Vector2I((int)(p.X + p.W / 2 - scorch.GetWidth() / 2f), (int)(p.Foot - scorch.GetHeight() * 0.7f)));
        // 東西：由後往前（腳的位置），動畫的另外畫
        foreach (var p in _picture.Pieces.OrderBy(p => p.Foot).ThenBy(p => p.X))
        {
            if (p.Frames > 1)
            {
                _animated.Add(p);
                continue;
            }
            if (p.Group is { } group)
            {
                var x = p.X;
                foreach (var (sprite, width) in group)
                {
                    if (PixelParts.ImageOf(sprite) is { } part)
                        image.BlendRect(part, new Rect2I(Vector2I.Zero, part.GetSize()), new Vector2I((int)x, (int)(p.Foot - part.GetHeight())));
                    x += width;
                }
            }
            else if (PixelParts.ImageOf(p.Sprite) is { } art)
                image.BlendRect(art, new Rect2I(Vector2I.Zero, art.GetSize()), new Vector2I((int)p.X, (int)p.Y));
            else
                image.FillRect(new Rect2I((int)p.X, (int)p.Y, (int)p.W, (int)p.H), new Color(0.45f, 0.38f, 0.28f));
            // 受損：裂痕（耐久度七成以下）；施工中：底下一條進度。
            if (p.Progress < 0f && p.Durability is > 0 and < 700 && p.Kind is HoldingLayout.Kind.Building && PixelParts.ImageOf("dec_crack") is { } crack)
                image.BlendRect(crack, new Rect2I(Vector2I.Zero, crack.GetSize()),
                    new Vector2I((int)(p.X + p.W / 2 - crack.GetWidth() / 2f), (int)(p.Y + p.H * 0.55f - crack.GetHeight() / 2f)));
            if (p.Progress >= 0f)
            {
                var bar = new Rect2I((int)p.X + 4, (int)p.Foot + 2, Math.Max(4, (int)p.W - 8), 3);
                image.FillRect(bar, new Color(0.10f, 0.08f, 0.05f));
                image.FillRect(new Rect2I(bar.Position, new Vector2I(Math.Max(1, (int)(bar.Size.X * Math.Clamp(p.Progress, 0f, 1f))), bar.Size.Y)),
                    new Color(0.95f, 0.78f, 0.32f));
            }
            // 耐久度四成以下冒煙（另外每幀畫）
            if (p.Progress < 0f && p.Durability is > 0 and < 400 && p.Kind is HoldingLayout.Kind.Building)
                _animated.Add(new HoldingLayout.Piece
                {
                    Sprite = "fx_smoke", Frames = 4, Kind = HoldingLayout.Kind.Prop,
                    X = p.X + p.W * 0.45f, Y = p.Y - 6, W = 24, H = 24
                });
        }
        image.GenerateMipmaps();
        _baked?.Dispose();
        _baked = ImageTexture.CreateFromImage(image);
        image.Dispose();
    }

    public override void _ExitTree()
    {
        // 烤好的貼圖由這個節點自己持有：離開畫面就釋放（Godot .NET 的包裝物件不釋放會在結束時回報資源還在用）。
        _baked?.Dispose();
        _baked = null;
    }

    // ── 畫面 ─────────────────────────────────────────────────────────────

    /// <summary>整張放得下的比例（放大到整數倍以上時取整數，像素不變形）。</summary>
    private float FitScale()
    {
        if (_picture is null || _picture.Size.X <= 0 || _picture.Size.Y <= 0 || Size.X <= 0 || Size.Y <= 0)
            return 1f; // 還沒排版（沒有大小）時當作原尺寸
        var fit = Math.Min(Size.X / _picture.Size.X, Size.Y / _picture.Size.Y);
        return fit >= 1f ? MathF.Floor(fit) : fit;
    }

    private float ViewScale
    {
        get
        {
            var scale = FitScale() * _zoom;
            return scale >= 1f ? MathF.Round(scale) : scale;
        }
    }

    private Vector2 ViewOffset => Size / 2 - _center * ViewScale;

    private void ClampView()
    {
        if (_picture is null)
            return;
        var scale = ViewScale;
        var half = Size / 2 / scale;
        var picture = (Vector2)_picture.Size;
        _center = new Vector2(
            picture.X <= 2 * half.X ? picture.X / 2 : Math.Clamp(_center.X, half.X, picture.X - half.X),
            picture.Y <= 2 * half.Y ? picture.Y / 2 : Math.Clamp(_center.Y, half.Y, picture.Y - half.Y));
        var filter = scale < 1f ? TextureFilterEnum.LinearWithMipmaps : TextureFilterEnum.Nearest;
        if (TextureFilter != filter)
            TextureFilter = filter;
    }

    public override void _Process(double delta)
    {
        if (_animated.Count == 0 || !IsVisibleInTree())
            return;
        var now = Time.GetTicksMsec();
        if (now - _lastFrame < 150)
            return;
        _lastFrame = now;
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.16f, 0.13f, 0.09f));
        if (_dirty && _picture is not null)
        {
            _dirty = false;
            Bake();
        }
        if (_baked is null || _picture is null)
            return;
        var scale = ViewScale;
        DrawSetTransform(ViewOffset, 0f, new Vector2(scale, scale));
        DrawTexture(_baked, Vector2.Zero);
        var frame = (int)(Time.GetTicksMsec() / 150 % 4);
        foreach (var p in _animated)
            if (PixelParts.Texture(p.Sprite) is { } texture)
            {
                var w = texture.GetWidth() / (float)p.Frames;
                DrawTextureRectRegion(texture, p.Rect, new Rect2(w * frame, 0, w, texture.GetHeight()));
            }
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        if (_thumbnail)
        {
            var font = ThemeDB.FallbackFont;
            const string hint = "點圖放大";
            var at = new Vector2(Size.X - font.GetStringSize(hint, HorizontalAlignment.Left, -1, 13).X - 8, Size.Y - 8);
            DrawString(font, at + Vector2.One, hint, HorizontalAlignment.Left, -1, 13, new Color(0, 0, 0, 0.7f));
            DrawString(font, at, hint, HorizontalAlignment.Left, -1, 13, new Color(1f, 0.95f, 0.8f));
        }
    }

    /// <summary>畫布座標的那一點上最前面的東西（建築、住宅、城牆；樹與小東西不算）。</summary>
    public HoldingLayout.Piece? PieceAt(Vector2 local)
    {
        if (_picture is null)
            return null;
        var at = (local - ViewOffset) / ViewScale;
        return _picture.Pieces.Where(p => p.Tip.Length > 0 && p.Rect.HasPoint(at)).OrderByDescending(p => p.Foot).FirstOrDefault();
    }

    public override string _GetTooltip(Vector2 atPosition) => PieceAt(atPosition)?.Tip ?? (_thumbnail ? "點一下打開領地圖" : "");

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown, Pressed: true } wheel when !_thumbnail:
                ZoomAt(wheel.Position, wheel.ButtonIndex == MouseButton.WheelUp ? 1.25f : 0.8f);
                AcceptEvent();
                break;
            case InputEventMouseButton { ButtonIndex: MouseButton.Left } click:
                if (click.Pressed)
                {
                    _dragging = true;
                    _dragged = false;
                    _dragFrom = click.Position;
                    if (click.DoubleClick && !_thumbnail)
                    {
                        _zoom = 1f;
                        ClampView();
                        QueueRedraw();
                    }
                }
                else
                {
                    _dragging = false;
                    if (_thumbnail && !_dragged)
                        Opened?.Invoke();
                }
                AcceptEvent();
                break;
            case InputEventMouseMotion motion when _dragging && !_thumbnail:
                if ((motion.Position - _dragFrom).Length() > 3f)
                    _dragged = true;
                _center -= motion.Relative / ViewScale;
                ClampView();
                QueueRedraw();
                AcceptEvent();
                break;
        }
    }

    /// <summary>以滑鼠位置為中心縮放（最小＝整張放得下，最大＝放大 6 倍）。</summary>
    public void ZoomAt(Vector2 local, float factor)
    {
        if (_picture is null)
            return;
        var before = (local - ViewOffset) / ViewScale;
        _zoom = Math.Clamp(_zoom * factor, 1f, TempMaxZoom);
        _center = before - (local - Size / 2) / ViewScale;
        ClampView();
        QueueRedraw();
    }

    /// <summary>【TEMP】大圖最多放大幾倍（相對於整張放得下）。</summary>
    private const float TempMaxZoom = 6f;
}
