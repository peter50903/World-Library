using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-16 章第 12 節：像素零件（GPT 畫、匯入工具縮成實際像素）。
/// 地形零件（樹、草、山、礦、田、灰燼…）由 <see cref="TerrainSurface"/> 直接疊進像素地表；
/// 建築、火焰、天災痕跡以同樣的像素密度（第四批零件：每格半徑 <see cref="TerrainSurface.TempArtTexelsPerRadius"/> 像素，地表兩倍）、最近鄰取樣畫在地表上，透視與像素大小一致。
/// 缺任何一張時該處退回程式畫的圖樣。
/// </summary>
public static class PixelParts
{
    public static readonly string[] Names =
    {
        "px_tree_round_1", "px_tree_round_2", "px_tree_pine_1", "px_tree_pine_2", "px_bush", "px_sapling", "px_stump", "px_tree_burnt",
        "px_grass_tuft_1", "px_grass_tuft_2", "px_flowers", "px_dry_tuft", "px_mountain_1", "px_mountain_2", "px_rock_1", "px_rock_2",
        "px_ore_1", "px_ore_2", "px_ore_meteor", "px_mine_pit", "px_field_wheat", "px_field_green", "px_field_young", "px_field_soil",
        "px_field_fallow", "px_fire", "px_smoke", "px_ash", "px_flood_ripple", "px_crack", "px_crater", "px_debris", "px_icon_disaster",
        "bld_tent", "bld_hut", "bld_house", "bld_stone_house", "bld_market", "bld_temple", "bld_keep", "bld_palisade", "bld_stone_wall", "bld_ruin"
    };

    public static string Path(string name) => $"res://assets/px/{name}.png";

    /// <summary>一張零件的像素（列優先；透明度只有 0 或 1）。</summary>
    public sealed record Sprite(int Width, int Height, Color[] Pixels)
    {
        public Color At(int x, int y) => Pixels[y * Width + x];
    }

    private static readonly Dictionary<string, Sprite?> Sprites = new();
    private static readonly Dictionary<(string, bool), CanvasTexture?> Nearest = new();

    /// <summary>零件的像素（給地表烤圖用）；缺少時回傳 null。</summary>
    public static Sprite? Get(string name)
    {
        if (Sprites.TryGetValue(name, out var sprite))
            return sprite;
        sprite = null;
        if (ArtAssets.Texture(Path(name))?.GetImage() is { } image)
        {
            image = (Image)image.Duplicate();
            if (image.IsCompressed())
                image.Decompress();
            image.Convert(Image.Format.Rgba8);
            var pixels = new Color[image.GetWidth() * image.GetHeight()];
            for (var y = 0; y < image.GetHeight(); y++)
                for (var x = 0; x < image.GetWidth(); x++)
                    pixels[y * image.GetWidth() + x] = image.GetPixel(x, y);
            sprite = new Sprite(image.GetWidth(), image.GetHeight(), pixels);
        }
        Sprites[name] = sprite;
        return sprite;
    }

    /// <summary>以最近鄰取樣畫的零件貼圖（建築、火焰等畫在地表上的）；<paramref name="repeat"/>＝可橫向重複（城牆）。</summary>
    public static CanvasTexture? Texture(string name, bool repeat = false)
    {
        if (Nearest.TryGetValue((name, repeat), out var texture))
            return texture;
        texture = ArtAssets.Texture(Path(name)) is { } source
            ? new CanvasTexture
            {
                DiffuseTexture = source,
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                TextureRepeat = repeat ? CanvasItem.TextureRepeatEnum.Enabled : CanvasItem.TextureRepeatEnum.Disabled
            }
            : null;
        Nearest[(name, repeat)] = texture;
        return texture;
    }

    /// <summary>零件在畫面上的大小：圖像素數 × 每像素的螢幕大小（格半徑÷每格半徑的圖像素）。</summary>
    public static Vector2 ScreenSize(Texture2D texture, float radius, int frames = 1)
        => new Vector2(texture.GetWidth() / (float)frames, texture.GetHeight()) * (radius / TerrainSurface.TempArtTexelsPerRadius);
}
