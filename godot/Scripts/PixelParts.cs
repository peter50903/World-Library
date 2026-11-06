using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-16 章第 12 節：像素零件（GPT 畫、匯入工具縮成實際像素）。
/// 地形零件（樹、草、山、礦、田、灰燼…）由 <see cref="TerrainSurface"/> 直接疊進像素地表；
/// 建築、火焰、天災痕跡以同樣的像素密度、最近鄰取樣畫在地表上，透視與像素大小一致。
/// 第 08-18d 章：第五、六批素材（<see cref="Batch5"/>，res://assets/px2/）與地表同一密度（每格半徑 <see cref="TerrainSurface.TempTexelsPerRadius"/> 圖像素）；
/// 第四批（res://assets/px/）是一半密度（每格半徑 <see cref="TerrainSurface.TempArtTexelsPerRadius"/>），畫的時候放大兩倍。
/// 缺任何一張時該處退回舊素材或程式畫的圖樣。
/// </summary>
public static partial class PixelParts
{
    public static readonly string[] Names =
    {
        "px_tree_round_1", "px_tree_round_2", "px_tree_pine_1", "px_tree_pine_2", "px_bush", "px_sapling", "px_stump", "px_tree_burnt",
        "px_grass_tuft_1", "px_grass_tuft_2", "px_flowers", "px_dry_tuft", "px_mountain_1", "px_mountain_2", "px_rock_1", "px_rock_2",
        "px_ore_1", "px_ore_2", "px_ore_meteor", "px_mine_pit", "px_field_wheat", "px_field_green", "px_field_young", "px_field_soil",
        "px_field_fallow", "px_fire", "px_smoke", "px_ash", "px_flood_ripple", "px_crack", "px_crater", "px_debris", "px_icon_disaster",
        "bld_tent", "bld_hut", "bld_house", "bld_stone_house", "bld_market", "bld_temple", "bld_keep", "bld_palisade", "bld_stone_wall", "bld_ruin"
    };

    private static HashSet<string>? _batch5;

    /// <summary>這個名字有沒有第五、六批的素材（同名時優先）。</summary>
    public static bool IsBatch5(string name) => (_batch5 ??= Batch5.ToHashSet()).Contains(name);

    /// <summary>第四批的舊路徑（冒煙測試照舊確認讀得到）。</summary>
    public static string LegacyPath(string name) => $"res://assets/px/{name}.png";

    public static string Path(string name) => IsBatch5(name) ? $"res://assets/px2/{name}.png" : LegacyPath(name);

    /// <summary>一張零件的像素（列優先；透明度只有 0 或 1）；<see cref="Scale"/>＝疊進地表時每個零件像素佔幾個圖像素。</summary>
    public sealed record Sprite(int Width, int Height, Color[] Pixels, int Scale)
    {
        public Color At(int x, int y) => Pixels[y * Width + x];
    }

    private static readonly Dictionary<string, Sprite?> Sprites = new();
    private static readonly Dictionary<(string, bool), CanvasTexture?> Nearest = new();
    private static readonly Dictionary<(string, uint), CanvasTexture?> TintedCache = new();

    /// <summary>零件的像素（給地表烤圖用）；缺少時回傳 null。</summary>
    public static Sprite? Get(string name)
    {
        if (Sprites.TryGetValue(name, out var sprite))
            return sprite;
        sprite = null;
        if (Pixels(name) is { } image)
        {
            var pixels = new Color[image.GetWidth() * image.GetHeight()];
            for (var y = 0; y < image.GetHeight(); y++)
                for (var x = 0; x < image.GetWidth(); x++)
                    pixels[y * image.GetWidth() + x] = image.GetPixel(x, y);
            sprite = new Sprite(image.GetWidth(), image.GetHeight(), pixels,
                IsBatch5(name) ? 1 : TerrainSurface.TempTexelsPerRadius / TerrainSurface.TempArtTexelsPerRadius);
        }
        Sprites[name] = sprite;
        return sprite;
    }

    /// <summary>從 <paramref name="prefix"/>_1…_<paramref name="count"/> 依雜湊挑一張（缺的話往前找，全缺回傳 null）。</summary>
    public static Sprite? Variant(string prefix, int count, float hash)
    {
        var pick = Math.Clamp((int)(hash * count), 0, count - 1);
        for (var k = 0; k < count; k++)
            if (Get($"{prefix}_{(pick + k) % count + 1}") is { } sprite)
                return sprite;
        return null;
    }

    /// <summary>同 <see cref="Variant"/>，回傳名字（給畫在地圖上的貼圖用）。</summary>
    public static string? VariantName(string prefix, int count, float hash)
    {
        var pick = Math.Clamp((int)(hash * count), 0, count - 1);
        for (var k = 0; k < count; k++)
        {
            var name = $"{prefix}_{(pick + k) % count + 1}";
            if (Texture(name) is not null)
                return name;
        }
        return null;
    }

    private static Image? Pixels(string name)
    {
        if (ArtAssets.Texture(Path(name))?.GetImage() is not { } image)
            return null;
        image = (Image)image.Duplicate();
        if (image.IsCompressed())
            image.Decompress();
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    /// <summary>以最近鄰取樣畫的零件貼圖（建築、火焰等畫在地表上的）；<paramref name="repeat"/>＝可重複貼（城牆）。</summary>
    public static CanvasTexture? Texture(string name, bool repeat = false)
    {
        if (Nearest.TryGetValue((name, repeat), out var texture))
            return texture;
        texture = ArtAssets.Texture(Path(name)) is { } source ? Wrap(source, repeat) : null;
        Nearest[(name, repeat)] = texture;
        return texture;
    }

    private static CanvasTexture Wrap(Texture2D source, bool repeat) => ArtAssets.Track(new CanvasTexture
    {
        DiffuseTexture = source,
        TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
        TextureRepeat = repeat ? CanvasItem.TextureRepeatEnum.Enabled : CanvasItem.TextureRepeatEnum.Disabled
    });

    /// <summary>
    /// 第 08-18d 章：灰階部分染成文化色（屋頂、篷布、旗幟、上衣）。素材裡的灰階像素（彩度很低、不是近黑近白的描邊與高光）
    /// 依明暗換成代表色的深淺；<paramref name="top"/>＝只染圖的上面這個比例（建築只染屋頂，石牆不染），1＝整張。
    /// </summary>
    public static CanvasTexture? Tinted(string name, Color color, float top = 1f)
    {
        var key = (name + "|" + top.ToString("0.00"), color.ToRgba32());
        if (TintedCache.TryGetValue(key, out var texture))
            return texture;
        texture = null;
        if (IsBatch5(name) && Pixels(name) is { } image)
        {
            var rows = (int)Math.Ceiling(image.GetHeight() * top);
            int changed = 0, opaque = 0;
            for (var y = 0; y < rows; y++)
                for (var x = 0; x < image.GetWidth(); x++)
                {
                    var c = image.GetPixel(x, y);
                    if (c.A < 0.5f)
                        continue;
                    opaque++;
                    var max = Math.Max(c.R, Math.Max(c.G, c.B));
                    var min = Math.Min(c.R, Math.Min(c.G, c.B));
                    if (max - min > TempGrayChroma || max < 0.18f || min > 0.94f)
                        continue;
                    var v = (c.R + c.G + c.B) / 3f;
                    var shade = v < 0.55f ? color.Darkened((0.55f - v) / 0.55f * 0.75f) : color.Lightened((v - 0.55f) / 0.45f * 0.55f);
                    image.SetPixel(x, y, new Color(shade.R, shade.G, shade.B, c.A));
                    changed++;
                }
            // 畫的時候已經上了顏色（沒有灰階部分）的素材不能染色：回傳 null，呼叫端換一張。
            if (changed >= Math.Max(4, opaque * TempMinTinted))
                texture = Wrap(ArtAssets.Track(ImageTexture.CreateFromImage(image)), false);
        }
        TintedCache[key] = texture;
        return texture;
    }

    /// <summary>【TEMP】灰階像素的判斷：RGB 最大與最小差在這以內；染到的像素少於染色範圍的這個比例就算不能染。</summary>
    private const float TempGrayChroma = 0.07f;
    private const float TempMinTinted = 0.08f;

    /// <summary>從 <paramref name="prefix"/>_1…_<paramref name="count"/> 依雜湊挑一張可以染色的（都不能染時回傳 null）。</summary>
    public static CanvasTexture? TintedVariant(string prefix, int count, float hash, Color color, float top)
    {
        var pick = Math.Clamp((int)(hash * count), 0, count - 1);
        for (var k = 0; k < count; k++)
            if (Tinted($"{prefix}_{(pick + k) % count + 1}", color, top) is { } texture)
                return texture;
        return null;
    }

    /// <summary>這張貼圖每格半徑幾個圖像素（第五批＝地表密度，第四批＝一半）。</summary>
    public static int Density(Texture2D texture)
        => texture is CanvasTexture { DiffuseTexture: { } source } && (source.ResourcePath.Contains("/px2/") || source is ImageTexture)
            ? TerrainSurface.TempTexelsPerRadius
            : TerrainSurface.TempArtTexelsPerRadius;

    /// <summary>零件在畫面上的大小：圖像素數 × 每像素的螢幕大小（格半徑÷每格半徑的圖像素）。</summary>
    public static Vector2 ScreenSize(Texture2D texture, float radius, int frames = 1)
        => new Vector2(texture.GetWidth() / (float)frames, texture.GetHeight()) * (radius / Density(texture));

    /// <summary>離開場景時清掉快取（貼圖本身由 <see cref="ArtAssets.ReleaseCache"/> 釋放）。</summary>
    public static void ReleaseCache()
    {
        Nearest.Clear();
        TintedCache.Clear();
        Sprites.Clear();
    }
}
