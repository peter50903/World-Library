using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18d 章：第五、六批素材畫在地圖上的共用工具。素材與像素地表同一密度（每格半徑 <see cref="TerrainSurface.TempTexelsPerRadius"/> 圖像素）、
/// 最近鄰取樣、底部置中；動畫是橫排 4 格；側面的素材面向右，往左走時左右翻轉。缺素材時各處退回原本程式畫的圖樣。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>畫一張素材（或動畫的第 <paramref name="frame"/> 格）：底部置中在 <paramref name="bottom"/>；<paramref name="flip"/>＝左右翻轉。</summary>
    private void DrawArt(Texture2D texture, Vector2 bottom, float radius, int frames = 1, int frame = 0, bool flip = false, float alpha = 1f, float scale = 1f)
    {
        var size = PixelParts.ScreenSize(texture, radius, frames) * scale;
        var fw = texture.GetWidth() / (float)frames;
        var source = new Rect2(fw * (frame % frames), 0f, fw, texture.GetHeight());
        DrawSetTransform(bottom, 0f, new Vector2(flip ? -1f : 1f, 1f));
        DrawTextureRectRegion(texture, new Rect2(-size.X / 2f, -size.Y, size.X, size.Y), source, new Color(1f, 1f, 1f, alpha));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>名字對應的素材在就畫、回傳 true；沒有回傳 false。</summary>
    private bool DrawArt(string name, Vector2 bottom, float radius, int frames = 1, int frame = 0, bool flip = false, float alpha = 1f)
    {
        if (PixelParts.Texture(name) is not { } texture)
            return false;
        DrawArt(texture, bottom, radius, frames, frame, flip, alpha);
        return true;
    }

    /// <summary>素材底下的落地陰影（光源左上：往右下偏）。</summary>
    private void GroundShadow(Vector2 bottom, float width, float alpha = 0.32f)
    {
        DrawSetTransform(bottom + new Vector2(width * 0.08f, -width * 0.02f), 0f, new Vector2(1f, 0.3f));
        DrawCircle(Vector2.Zero, width * 0.5f, new Color(0.08f, 0.06f, 0.04f, alpha));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    /// <summary>
    /// 第 08-20c 章（使用者 2026-11-14：沿用 KayKit 原型的建築）：KayKit 模型烘焙的建築素材（tools/kaykit_bake.py，px2/kk_*）。
    /// 屋頂與旗幟是灰階，整張染文化色；沒有這張素材回傳 null（退回第五批或程式畫的）。
    /// </summary>
    private static Texture2D? KitArt(string name, Color? culture = null)
    {
        if (!PixelParts.IsBatch5(name))
            return null;
        if (culture is { } color && PixelParts.Tinted(name, color, 1f) is { } tinted)
            return tinted;
        return PixelParts.Texture(name);
    }

    /// <summary>橫向重複用的烘焙素材（牆面、木柵）。</summary>
    private static CanvasTexture? KitRepeat(string name) => PixelParts.IsBatch5(name) ? PixelParts.Texture(name, repeat: true) : null;

    /// <summary>有四個方向的烘焙素材：<paramref name="name"/>_s／_e／_n／_w。</summary>
    private static Texture2D? KitFacing(string name, int facing, Color culture) => KitArt($"{name}_{FacingSuffix[facing & 3]}", culture);

    /// <summary>住屋的素材（灰階屋頂染文化色；畫的時候已經上色的那幾張不染、改挑別張；都不能染就用原色）。</summary>
    private static Texture2D? HouseArt(Building kind, int seed, Color culture, int facing = 0)
    {
        // 第 08-20c 章：KayKit 烘焙的住屋（門朝貼著的道路）；帳篷沿用第五批。
        var kit = kind switch
        {
            Building.Hut => "kk_hut",
            Building.House => "kk_house",
            Building.StoneHouse => "kk_stone_house",
            _ => null
        };
        if (kit is not null && KitFacing(kit, facing, culture) is { } baked)
            return baked;
        var (prefix, count, top) = kind switch
        {
            Building.Tent => ("bld_tent", 2, 1f),
            Building.Hut => ("bld_hut", 2, 0.55f),
            Building.House => ("bld_house", 3, 0.55f),
            _ => ("bld_stone_house", 3, 0.42f)
        };
        var hash = LayoutHash(seed, (int)kind, 140);
        return (Texture2D?)PixelParts.TintedVariant(prefix, count, hash, culture, top)
            ?? (PixelParts.VariantName(prefix, count, hash) is { } plain ? PixelParts.Texture(plain) : null);
    }

    /// <summary>大建築的素材：市集、神殿、主樓、井（灰階的篷布、屋頂、旗子染文化色）。</summary>
    /// <summary>第 08-18k 章：公共建築的素材（沒有素材回傳 null，退回原本的畫法）。</summary>
    private static Texture2D? CivicArt(Building kind, Color culture, int facing = 0)
    {
        // 第 08-20c 章：有 KayKit 烘焙版本的公共建築優先用它（風車 4 幀、不分方向）。
        var kit = kind switch
        {
            Building.HallVillage => KitFacing("kk_hall_village", facing, culture),
            Building.HallTown => KitFacing("kk_hall_town", facing, culture),
            Building.Smithy => KitFacing("kk_smithy", facing, culture),
            Building.Workshop => KitFacing("kk_workshop", facing, culture),
            Building.Windmill => KitArt("kk_windmill", culture),
            Building.Watchtower => KitArt("kk_watchtower", culture),
            _ => null
        };
        return kit ?? CivicFallback(kind);
    }

    private static Texture2D? CivicFallback(Building kind) => kind switch
    {
        Building.HallVillage => PixelParts.Texture("bld_hall_village"),
        Building.HallTown => PixelParts.Texture("bld_hall_town"),
        Building.Granary => PixelParts.Texture("bld_granary"),
        Building.Smithy => PixelParts.Texture("bld_smithy"),
        Building.Workshop => PixelParts.Texture("bld_workshop"),
        Building.Kiln => PixelParts.Texture("bld_kiln"),
        Building.Windmill => PixelParts.Texture("bld_windmill"),
        Building.Watchtower => PixelParts.Texture("bld_watchtower"),
        Building.School => PixelParts.Texture("bld_school"),
        Building.Scriptorium => PixelParts.Texture("bld_scriptorium"),
        Building.Shrine => PixelParts.Texture("bld_shrine"),
        _ => null
    };

    private static Texture2D? LotArt(Building kind, Color culture, int facing = 0)
    {
        // 第 08-20c 章：KayKit 烘焙的市集、教堂（神殿）、城堡（主樓）、井。
        var kit = kind switch
        {
            Building.Market => KitFacing("kk_market", facing, culture),
            Building.Temple => KitFacing("kk_temple", facing, culture),
            Building.Keep => KitArt("kk_keep", culture),
            Building.Well => KitArt("kk_well", culture),
            _ => null
        };
        if (kit is not null)
            return kit;
        var (name, top) = kind switch
        {
            Building.Market => ("bld_market", 0.6f),
            Building.Temple => ("bld_temple", 0.4f),
            Building.Keep => ("bld_keep", 0.14f),
            Building.Well => ("bld_well", 0f),
            _ => ("", 0f)
        };
        if (name.Length == 0 || !PixelParts.IsBatch5(name))
            return null;
        return (Texture2D?)(top > 0f ? PixelParts.Tinted(name, culture, top) : null) ?? PixelParts.Texture(name);
    }

    /// <summary>想法泡泡的素材：餓＝麵包、工＝槌子、樂＝笑臉（偶爾音符）。</summary>
    private static string BubbleArt(string bubble, float hash) => bubble switch
    {
        "餓" => "fx_bubble_1",
        "工" => "fx_bubble_5",
        _ => hash < 0.3f ? "fx_bubble_10" : "fx_bubble_3"
    };

    /// <summary>小人的素材：工作中依職業（農夫、樵夫、礦工、獵人、漁夫），其他依序是男、女、小孩、老人。</summary>
    private static string UnitArt(TokenKind? work, int index) => work switch
    {
        TokenKind.Farmers => "unit_farmer",
        TokenKind.Woodcutters => "unit_woodcutter",
        TokenKind.Miners => "unit_miner",
        TokenKind.Hunters => "unit_hunter",
        TokenKind.Fishers => "unit_fisher",
        _ => (index % 7) switch
        {
            0 or 3 => "unit_man",
            1 or 4 => "unit_woman",
            2 or 5 => "unit_child",
            _ => "unit_elder"
        }
    };
}
