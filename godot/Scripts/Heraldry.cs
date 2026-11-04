using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-16 章：把人像部件、紋章部件、信仰符號疊圖並染色成一張貼圖（部件由 GPT 畫、組合與顏色由程式決定）。
/// 結果依參數快取；任何部件缺少就回傳 null，介面退回純文字。只影響畫面。
/// </summary>
public static class Heraldry
{
    private const string PortraitDir = "res://assets/portraits/";
    private const string EmblemDir = "res://assets/emblems/";

    /// <summary>【TEMP】膚色倍率（淺→深）與自然髮色（黑、深棕、棕、金、紅、灰白）。</summary>
    private static readonly float[] TempSkinTones = { 1.10f, 1.00f, 0.88f, 0.72f, 0.58f };

    private static readonly Color[] TempHairColors =
    {
        new(0.17f, 0.14f, 0.13f), new(0.32f, 0.21f, 0.13f), new(0.50f, 0.33f, 0.19f),
        new(0.86f, 0.71f, 0.43f), new(0.64f, 0.27f, 0.12f), new(0.82f, 0.81f, 0.78f)
    };

    private static readonly Dictionary<string, Texture2D?> Cache = new();

    /// <summary>本版用到的全部部件檔（冒煙測試確認讀得到）。</summary>
    public static IEnumerable<string> AllParts()
    {
        foreach (var sex in new[] { "male", "female" })
        {
            for (var i = 1; i <= DemoHeraldry.FacesPerSex; i++) yield return $"{PortraitDir}face_{sex}_{i}.png";
            for (var i = 1; i <= DemoHeraldry.HairsPerSex; i++) yield return $"{PortraitDir}hair_{sex}_{i}.png";
            foreach (var c in new[] { "chief", "farmer", "carpenter", "blacksmith", "hunter", "shaman", "scholar", "commoner" })
                yield return $"{PortraitDir}clothes_{sex}_{c}.png";
        }
        for (var i = 1; i <= 3; i++) yield return $"{PortraitDir}beard_{i}.png";
        yield return $"{PortraitDir}age_middle.png";
        yield return $"{PortraitDir}age_old.png";
        foreach (var a in Accessories.Values) yield return a;
        foreach (var f in DemoHeraldry.Fields) yield return $"{EmblemDir}field_{f}.png";
        foreach (var c in DemoHeraldry.Charges) yield return $"{EmblemDir}charge_{c}.png";
        foreach (var s in DemoHeraldry.FaithSymbols) yield return $"{EmblemDir}faith_{s}.png";
    }

    /// <summary>職業衣著附帶的頭部配件（畫在髮型上面）。</summary>
    private static readonly Dictionary<(bool Female, string Clothes), string> Accessories = new()
    {
        [(true, "carpenter")] = $"{PortraitDir}accessory_female_carpenter_headwrap.png",
        [(true, "scholar")] = $"{PortraitDir}accessory_female_scholar_headband.png",
        [(false, "carpenter")] = $"{PortraitDir}accessory_male_carpenter_charcoal.png"
    };

    private static Image? Part(string path)
    {
        if (ArtAssets.Texture(path)?.GetImage() is not { } image)
            return null;
        image = (Image)image.Duplicate();
        if (image.IsCompressed())
            image.Decompress();
        image.Convert(Image.Format.Rgba8);
        return image;
    }

    private static Texture2D? Cached(string key, Func<Image?> build)
    {
        if (!Cache.TryGetValue(key, out var texture))
            Cache[key] = texture = build() is { } image ? ImageTexture.CreateFromImage(image) : null;
        return texture;
    }

    /// <summary>逐像素改色後疊到 <paramref name="target"/> 上（alpha 混合）。</summary>
    private static void Layer(Image target, Image? part, Func<Color, Color>? recolor = null)
    {
        if (part is null)
            return;
        for (var y = 0; y < part.GetHeight(); y++)
            for (var x = 0; x < part.GetWidth(); x++)
            {
                var c = part.GetPixel(x, y);
                if (c.A <= 0.01f)
                    continue;
                var src = recolor is null ? c : new Color(recolor(c), c.A);
                var dst = target.GetPixel(x, y);
                var a = src.A + dst.A * (1f - src.A);
                target.SetPixel(x, y, a <= 0f ? new Color(0, 0, 0, 0)
                    : new Color((src.R * src.A + dst.R * dst.A * (1f - src.A)) / a, (src.G * src.A + dst.G * dst.A * (1f - src.A)) / a,
                        (src.B * src.A + dst.B * dst.A * (1f - src.A)) / a, a));
            }
    }

    private static float Luma(Color c) => 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;

    /// <summary>灰階部件染色：亮度決定深淺。</summary>
    private static Color Dye(Color c, Color dye) => new(
        Mathf.Clamp(dye.R * (0.35f + Luma(c) * 1.3f), 0f, 1f),
        Mathf.Clamp(dye.G * (0.35f + Luma(c) * 1.3f), 0f, 1f),
        Mathf.Clamp(dye.B * (0.35f + Luma(c) * 1.3f), 0f, 1f));

    /// <summary>代表人物人像（128×128）；<paramref name="fabric"/>＝文化代表色（衣著的灰色布料染這個色）。</summary>
    public static Texture2D? Portrait(DemoPortrait p, Color fabric)
    {
        var sex = p.Female ? "female" : "male";
        var key = $"portrait:{p}:{fabric.ToHtml()}";
        return Cached(key, () =>
        {
            var clothes = Part($"{PortraitDir}clothes_{sex}_{p.Clothes}.png");
            var face = Part($"{PortraitDir}face_{sex}_{p.Face}.png");
            var hair = Part($"{PortraitDir}hair_{sex}_{p.Hair}.png");
            if (clothes is null || face is null || hair is null)
                return null;
            var image = Image.CreateEmpty(clothes.GetWidth(), clothes.GetHeight(), false, Image.Format.Rgba8);
            var cloth = fabric.Lerp(new Color(0.5f, 0.5f, 0.5f), 0.35f);
            // 衣著：灰色（低彩度）布料染文化色，皮革金屬等有顏色的配件不動。
            Layer(image, clothes, c => c.S < 0.12f ? Dye(c, cloth) : c);
            var tone = TempSkinTones[Math.Clamp(p.SkinTone, 0, TempSkinTones.Length - 1)];
            Layer(image, face, c => new Color(Mathf.Clamp(c.R * tone, 0f, 1f), Mathf.Clamp(c.G * tone, 0f, 1f), Mathf.Clamp(c.B * tone, 0f, 1f)));
            if (p.Age > 0)
                Layer(image, Part($"{PortraitDir}age_{(p.Age >= 2 ? "old" : "middle")}.png"));
            var hairColor = TempHairColors[Math.Clamp(p.HairColor, 0, TempHairColors.Length - 1)];
            if (p.Beard > 0)
                Layer(image, Part($"{PortraitDir}beard_{p.Beard}.png"), c => Dye(c, hairColor));
            Layer(image, hair, c => Dye(c, hairColor));
            if (Accessories.TryGetValue((p.Female, p.Clothes), out var accessory))
                Layer(image, Part(accessory));
            return image;
        });
    }

    /// <summary>文化紋章（128×128）：盾面染兩種深淺的文化色、圖案染象牙白。</summary>
    public static Texture2D? Emblem(DemoEmblem e, Color culture)
        => Cached($"emblem:{e}:{culture.ToHtml()}", () =>
        {
            var field = Part($"{EmblemDir}field_{e.Field}.png");
            var charge = Part($"{EmblemDir}charge_{e.Charge}.png");
            if (field is null || charge is null)
                return null;
            var light = culture.Darkened(0.15f);
            var dark = culture.Darkened(0.55f);
            var image = Image.CreateEmpty(field.GetWidth(), field.GetHeight(), false, Image.Format.Rgba8);
            // 盾面：很暗的像素是描邊（不動），其餘依亮度在深淺兩色之間。
            Layer(image, field, c => Luma(c) < 0.25f ? c : dark.Lerp(light, Mathf.Clamp((Luma(c) - 0.4f) / 0.5f, 0f, 1f)));
            if (charge.GetSize() != field.GetSize())
                charge.Resize(field.GetWidth(), field.GetHeight(), Image.Interpolation.Nearest);
            var ivory = new Color(0.95f, 0.91f, 0.80f);
            Layer(image, charge, c => new Color(ivory.R * (0.6f + Luma(c) * 0.4f), ivory.G * (0.6f + Luma(c) * 0.4f), ivory.B * (0.6f + Luma(c) * 0.4f)));
            return image;
        });

    /// <summary>信仰符號（128×128）：白色剪影染成偏淡的信仰色（畫面上另墊深色圓底，任何背景都看得到）。</summary>
    public static Texture2D? FaithSymbol(string symbol, Color faith)
        => Cached($"faith:{symbol}:{faith.ToHtml()}", () =>
        {
            var part = Part($"{EmblemDir}faith_{symbol}.png");
            if (part is null)
                return null;
            var image = Image.CreateEmpty(part.GetWidth(), part.GetHeight(), false, Image.Format.Rgba8);
            var bright = faith.Lightened(0.55f);
            Layer(image, part, c => new Color(bright.R * (0.6f + Luma(c) * 0.4f), bright.G * (0.6f + Luma(c) * 0.4f), bright.B * (0.6f + Luma(c) * 0.4f)));
            return image;
        });
}
