using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-20e 章（使用者 2026-11-15：RimWorld、2D 寶可夢式的正面視角）：第九批素材畫在正式地圖上。
/// 建築只有正面（不轉向、不翻轉），整張圖底部置中貼齊自己的方格範圍；屋頂、旗、篷布的灰階染文化色。
/// 圍牆：橫段畫正面（`_h` 左右重複）、縱段畫俯視牆頂（`_v` 一格一張）、四角是角樓、城門分橫牆門 `_gate_h` 與縱牆門 `_gate_v`。
/// 缺素材時回傳 false，呼叫端退回舊畫法。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>第九批建築素材名（沒有對應的回傳 null）。</summary>
    private static string? FrontName(Lot lot) => lot.Construction ? $"b9_site_{Math.Clamp(lot.Variant, 1, 3)}" : lot.Kind switch
    {
        Building.Tent => $"b9_tent_{Math.Clamp(lot.Variant, 1, 2)}",
        Building.Hut => $"b9_hut_{Math.Clamp(lot.Variant, 1, 2)}",
        Building.House => $"b9_house_{Math.Clamp(lot.Variant, 1, 3)}",
        Building.StoneHouse => $"b9_stone_house_{Math.Clamp(lot.Variant, 1, 3)}",
        Building.BrickHouse => $"b9_brick_house_{Math.Clamp(lot.Variant, 1, 2)}",
        Building.HallVillage => "b9_hall_village",
        Building.HallTown => "b9_hall_town",
        Building.Market => "b9_market",
        Building.Temple => "b9_temple",
        Building.Smithy => "b9_smithy",
        Building.Workshop => "b9_workshop",
        Building.Shrine => "b9_shrine",
        Building.Kiln => "b9_kiln",
        Building.Granary => "b9_granary",
        Building.School => "b9_school",
        Building.Scriptorium => "b9_scriptorium",
        Building.HuntingLodge => "b9_hunting_lodge",
        Building.FishingHut => "b9_fishing_hut",
        Building.Keep => "b9_keep",
        Building.Well => "b9_well",
        Building.Watchtower => "b9_watchtower",
        Building.Windmill => "b9_windmill",
        Building.Dock => "b9_dock",
        Building.Watermill => "b9_watermill",
        _ => null
    };

    /// <summary>
    /// 第 08-20f 章（使用者 2026-11-15：先建立物件的比例尺）：建築佔的方格直接取素材的寬高÷16（動畫取單格），
    /// 素材依比例尺重畫後版面自動跟著變大變小；沒有素材回傳 null（用 <see cref="LotSize"/> 的表）。
    /// </summary>
    private static (int W, int H)? SpriteSize(Lot lot)
    {
        if (FrontName(lot) is not { } name || !PixelParts.IsBatch5(name) || PixelParts.Get(name) is not { } sprite)
            return null;
        var frames = Animated(lot.Kind) ? 4 : 1;
        return (Math.Max(1, sprite.Width / frames / TerrainSurface.TempTile), Math.Max(1, sprite.Height / TerrainSurface.TempTile));
    }

    /// <summary>這個方格是不是陸地（圍牆只建在陸地上）。</summary>
    private bool TileLand(int x, int y)
        => _surface is { } surface && surface.TileOwner(x, y) is var o and >= 0 && IsLand(o);

    private static bool Animated(Building kind) => kind is Building.Windmill or Building.Watermill;

    /// <summary>畫一棟第九批建築：底部置中在範圍下緣，灰階部分染文化色；動畫素材（風車、水車）4 格輪播。</summary>
    private bool DrawFrontLot(Lot lot, Vector2 bottom, float radius, Color culture)
    {
        if (FrontName(lot) is not { } name || !PixelParts.IsBatch5(name))
            return false;
        Texture2D? art = (Texture2D?)PixelParts.Tinted(name, culture, 1f) ?? PixelParts.Texture(name);
        if (art is null)
            return false;
        if (Animated(lot.Kind))
            DrawArt(art, bottom, radius, 4, (int)(Time.GetTicksMsec() / 220UL % 4UL));
        else
            DrawArt(art, bottom, radius);
        return true;
    }

    /// <summary>
    /// 第 08-20e 章第 3 節：圍牆。上牆與左右牆在建築之前畫（在後面）；下牆在建築之後畫（在前面），它的正面往上長、蓋住牆內最南幾列。
    /// 城門：上下牆的門在主縱街的位置（寬 2～3 格，中間是路）；左右牆的門在主橫街的位置（1×3，中間一格是路）。
    /// 缺第九批素材時回傳 false。
    /// </summary>
    private bool AddFrontWall(List<(float Y, Action Draw)> items, SettlementPlan layout, Rect2I wall, Func<float, float, Vector2> screen, float radius)
    {
        if (layout.WallKind < 0)
            return false;
        var key = WallNames[Math.Clamp(layout.WallKind, 0, WallNames.Length - 1)];
        if (!PixelParts.IsBatch5($"b9_{key}_h") || KitRepeat($"b9_{key}_h") is not { } front)
            return false;
        var side = PixelParts.Texture($"b9_{key}_v");
        var corner = PixelParts.Texture($"b9_{key}_corner");
        var gateH = PixelParts.Texture($"b9_{key}_gate_h");
        var gateV = PixelParts.Texture($"b9_{key}_gate_v");
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        var tile = TerrainSurface.TempTile * p;
        var gw = GateWidth(layout.WallKind);
        var gates = layout.Gates;
        bool TopOrBottom(Vector2I g) => g.Y == wall.Position.Y || g.Y == wall.End.Y - 1;

        void Row(int y)
        {
            // 這一排的城門（以主縱街為中心、寬 gw 格）。
            var openings = gates.Where(g => g.Y == y && TopOrBottom(g)).Select(g => g.X - (gw - 1) / 2).ToList();
            bool Open(int x) => openings.Any(o => x >= o && x < o + gw);
            // 第 08-20f 章（使用者 2026-11-15：城牆不應該建在水上）：牆在水邊斷開，水本身就是邊界。
            var x0 = wall.Position.X;
            for (var x = wall.Position.X; x <= wall.End.X; x++)
            {
                if (x < wall.End.X && !Open(x) && TileLand(x, y))
                    continue;
                if (x < wall.End.X && !Open(x) && x0 == x)
                {
                    x0 = x + 1; // 連續的水格：往後移
                    continue;
                }
                var a = screen(x0, y + 1);
                var b = screen(x, y + 1);
                if (b.X - a.X > 0.5f)
                {
                    var h = front.GetHeight() * p;
                    DrawTextureRectRegion(front, new Rect2(a.X, a.Y - h, b.X - a.X, h), new Rect2(0f, 0f, (b.X - a.X) / p, front.GetHeight()));
                }
                x0 = x + 1;
            }
            foreach (var start in openings)
                if (gateH is not null)
                    DrawArt(gateH, screen(start + gw / 2f, y + 1), radius);
            if (corner is null)
                return;
            if (TileLand(wall.Position.X, y))
                DrawArt(corner, screen(wall.Position.X + 0.5f, y + 1), radius);
            if (TileLand(wall.End.X - 1, y))
                DrawArt(corner, screen(wall.End.X - 0.5f, y + 1), radius);
        }
        void Column(int x)
        {
            var openings = gates.Where(g => g.X == x && !TopOrBottom(g)).Select(g => g.Y).ToHashSet();
            for (var y = wall.Position.Y + 1; y < wall.End.Y - 1; y++)
            {
                if (openings.Contains(y))
                {
                    if (gateV is not null)
                        DrawArt(gateV, screen(x + 0.5f, y + 2), radius); // 1×3：中間一格是路
                    continue;
                }
                if (openings.Contains(y - 1) || openings.Contains(y + 1))
                    continue; // 門柱佔的兩格
                if (side is not null && TileLand(x, y))
                    DrawTextureRect(side, new Rect2(screen(x, y), new Vector2(tile, tile)), false);
            }
        }
        items.Add((screen(0, wall.Position.Y + 1).Y - 0.01f, () =>
        {
            Column(wall.Position.X);
            Column(wall.End.X - 1);
            Row(wall.Position.Y);
        }));
        items.Add((screen(0, wall.End.Y).Y + 0.01f, () => Row(wall.End.Y - 1)));
        return true;
    }
}
