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
    /// <summary>建築素材名：第十一批優先，沒有再用第九批（都沒有回傳 null）。</summary>
    private static string? FrontName(Lot lot)
        => B11Name(lot) is { } fresh && PixelParts.IsBatch5(fresh) ? fresh : B9Name(lot);

    /// <summary>
    /// 第 09-1a 章：第十一批建築素材名。工地依地塊寬挑小（≤3）、中（4）、大（≥5）三種；市集是一列單攤（<see cref="DrawStalls"/>）。
    /// 碼頭沒有對應的一張圖（第十一批是可接長的棧道），沿用第九批。
    /// </summary>
    private static string? B11Name(Lot lot) => lot.Construction
        ? $"b11_site_{(lot.W <= 3 ? "s" : lot.W <= 4 ? "m" : "l")}_{Math.Clamp(lot.Variant, 1, 3)}"
        : lot.Kind switch
        {
            Building.Tent => $"b11_tent_{Math.Clamp(lot.Variant, 1, 2)}",
            Building.Hut => $"b11_hut_{Math.Clamp(lot.Variant, 1, 3)}",
            Building.House => $"b11_house_{Math.Clamp(lot.Variant, 1, 3)}",
            Building.StoneHouse => $"b11_stone_house_{Math.Clamp(lot.Variant, 1, 3)}",
            Building.BrickHouse => $"b11_brick_house_{Math.Clamp(lot.Variant, 1, 2)}",
            Building.HallVillage => "b11_hall_village",
            Building.HallTown => "b11_hall_town",
            Building.Market => "b11_stall_1",
            Building.Temple => "b11_temple",
            Building.Smithy => "b11_smithy",
            Building.Workshop => "b11_workshop",
            Building.Shrine => "b11_shrine",
            Building.Kiln => "b11_kiln",
            Building.Granary => "b11_granary",
            Building.School => "b11_school",
            Building.Scriptorium => "b11_scriptorium",
            Building.HuntingLodge => "b11_hunting_lodge",
            Building.FishingHut => "b11_fishing_hut",
            Building.Keep => "b11_keep",
            Building.Well => "b11_well",
            Building.Watchtower => "b11_watchtower",
            Building.Windmill => "b11_windmill",
            Building.Watermill => "b11_watermill",
            // 第 09-1b 章
            Building.Farmhouse => $"b11_farmhouse_{Math.Clamp(lot.Variant, 1, 2)}",
            Building.Inn => "b11_inn",
            Building.Warehouse => "b11_warehouse",
            Building.Stable => "b11_stable",
            Building.BellTower => "b11_bell_tower",
            Building.Barracks => "b11_barracks",
            Building.Archery => "b11_archery",
            Building.Fountain => "b11_fountain",
            Building.Statue => "b11_statue",
            Building.RowHouse => $"b11_row_mid_{Math.Clamp(lot.Variant, 1, 2)}", // 實際依整排畫左、中、右（DrawRowRuns）
            _ => null
        };

    /// <summary>第九批建築素材名（沒有對應的回傳 null）。</summary>
    private static string? B9Name(Lot lot) => lot.Construction ? $"b9_site_{Math.Clamp(lot.Variant, 1, 3)}" : lot.Kind switch
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
    /// <summary><paramref name="name"/>＝<see cref="FrontName"/> 的結果（第 09-1e 章：版面改變時組一次，不每幀組字串）。</summary>
    private bool DrawFrontLot(Lot lot, string? name, Vector2 bottom, float radius, Color culture)
    {
        if (name is null || !PixelParts.IsBatch5(name))
            return false;
        Texture2D? art = (Texture2D?)PixelParts.Tinted(name, culture, 1f) ?? PixelParts.Texture(name);
        if (art is null)
            return false;
        if (Animated(lot.Kind))
            MarkAnimated(bottom, radius); // 第 09-1e 章：風車、水車在畫面內時才持續重畫
        if (lot.Kind == Building.Watermill && DrawWatermill(art, bottom, radius, culture))
            return true;
        if (lot.Kind == Building.Market && name.StartsWith("b11_", StringComparison.Ordinal))
        {
            DrawStalls(lot, bottom, radius, culture);
            return true;
        }
        if (Animated(lot.Kind))
            DrawArt(art, bottom, radius, 4, (int)(Time.GetTicksMsec() / 220UL % 4UL));
        else
            DrawArt(art, bottom, radius);
        return true;
    }

    private static readonly string[] StallNames = { "b11_stall_1", "b11_stall_2", "b11_stall_3" };

    /// <summary>第 09-1a 章：市集＝一列單攤（第十一批每攤 2×2），三款輪流，排滿地塊寬、置中。</summary>
    private void DrawStalls(Lot lot, Vector2 bottom, float radius, Color culture)
    {
        var tile = TerrainSurface.TempTile * radius / TerrainSurface.TempTexelsPerRadius;
        var count = Math.Max(1, lot.W / 2);
        for (var i = 0; i < count; i++)
        {
            var name = StallNames[(lot.X + i) % 3];
            if (((Texture2D?)PixelParts.Tinted(name, culture, 1f) ?? PixelParts.Texture(name)) is not { } stall)
                continue;
            DrawArt(stall, bottom + new Vector2((i - (count - 1) / 2f) * 2f * tile, 0f), radius);
        }
    }

    /// <summary>【TEMP】第 08-20g 章：水磨坊每格動畫的寬（像素）、輪子從哪一欄開始。</summary>
    private const int TempMillFrameWidth = 80, TempMillWheelFrom = 48;

    /// <summary>
    /// 第 08-20g 章（使用者 2026-11-15：水車房子屋頂一樣是錯的）：第九批水磨坊的屋頂只畫了左半，第十批重畫交件前，
    /// 房子改畫一棟完整的木屋（同款染色），只從水磨坊圖裁右邊兩格的水車輪子（照樣 4 格輪播）。
    /// </summary>
    private bool DrawWatermill(Texture2D art, Vector2 bottom, float radius, Color culture)
    {
        if (art.GetWidth() != TempMillFrameWidth * 4 || PixelParts.Texture("b9_house_1") is null)
            return false;
        var house = (Texture2D?)PixelParts.Tinted("b9_house_1", culture, 1f) ?? PixelParts.Texture("b9_house_1")!;
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        var tile = TerrainSurface.TempTile * p;
        // 範圍 5 格寬：左 3 格是房子、右 2 格是輪子（下緣置中在範圍正中）。
        DrawArt(house, bottom + new Vector2(-tile, 0f), radius);
        var frame = (int)(Time.GetTicksMsec() / 220UL % 4UL);
        var wheel = TempMillFrameWidth - TempMillWheelFrom;
        var source = new Rect2(frame * TempMillFrameWidth + TempMillWheelFrom, 0f, wheel, art.GetHeight());
        DrawTextureRectRegion(art, new Rect2(bottom.X + tile * 0.5f, bottom.Y - art.GetHeight() * p, wheel * p, art.GetHeight() * p), source);
        return true;
    }

    /// <summary>
    /// 第 08-20e 章第 3 節：圍牆。上牆與左右牆在建築之前畫（在後面）；下牆在建築之後畫（在前面），它的正面往上長、蓋住牆內最南幾列。
    /// 城門：上下牆的門在主縱街的位置（寬 2～3 格，中間是路）；左右牆的門在主橫街的位置（1×3，中間一格是路）。
    /// 缺第九批素材時回傳 false。
    /// </summary>
    private bool AddFrontWall(List<DrawItem> items, SettlementPlan layout, Rect2I wall)
    {
        if (layout.WallKind < 0)
            return false;
        var key = WallNames[Math.Clamp(layout.WallKind, 0, WallNames.Length - 1)];
        // 第 09-1a 章：第十一批優先（四個角、四種端點各有一張）；沒有再用第九批（一種角樓兼端點）。
        var set = PixelParts.IsBatch5($"b11_{key}_h") ? "b11" : "b9";
        if (!PixelParts.IsBatch5($"{set}_{key}_h") || KitRepeat($"{set}_{key}_h") is not { } front)
            return false;
        Texture2D? Part(string suffix) => PixelParts.Texture($"{set}_{key}_{suffix}") ?? PixelParts.Texture($"{set}_{key}_corner") ?? PixelParts.Texture($"{set}_{key}_corner_nw");
        var side = PixelParts.Texture($"{set}_{key}_v");
        var corner = Part("corner");
        var gateH = PixelParts.Texture($"{set}_{key}_gate_h");
        var gateV = PixelParts.Texture($"{set}_{key}_gate_v");
        var frontHeight = PixelParts.Metrics(front).Height;
        var gw = GateWidth(layout.WallKind);
        var gates = layout.Gates;
        bool TopOrBottom(Vector2I g) => g.Y == wall.Position.Y || g.Y == wall.End.Y - 1;

        // 第 09-1e 章：開口、連續的牆段、兩端的角樓在版面改變時算好；回傳的動作每幀只負責畫。
        Action Row(int y)
        {
            // 這一排的城門（以主縱街為中心、寬 gw 格）。
            var openings = gates.Where(g => g.Y == y && TopOrBottom(g)).Select(g => g.X - (gw - 1) / 2).ToList();
            bool Open(int x) => openings.Any(o => x >= o && x < o + gw);
            // 第 08-20f 章：牆在水邊斷開，水本身就是邊界。
            // 第 08-20g 章（使用者 2026-11-15：遇到畸零地時城牆圖片拼接錯誤）：每一段牆只畫在連續的陸地上，斷在水邊的那一頭補一座角樓收尾。
            var runs = new List<(int From, int To)>();
            var start = -1;
            for (var x = wall.Position.X; x <= wall.End.X; x++)
            {
                var solid = x < wall.End.X && !Open(x) && TileLand(x, y);
                if (solid && start < 0)
                    start = x;
                else if (!solid && start >= 0)
                {
                    runs.Add((start, x));
                    start = -1;
                }
            }
            // 牆角，或斷在水邊（不是城門）的一頭（第十一批：角樓分四角、斷頭是端點）。
            var caps = new List<(Texture2D Art, float X)>();
            var top = y == wall.Position.Y;
            if (corner is not null)
                foreach (var (from, to) in runs)
                {
                    if (from == wall.Position.X)
                        caps.Add((Part(top ? "corner_nw" : "corner_sw")!, from + 0.5f));
                    else if (!Open(from - 1))
                        caps.Add((Part("end_w")!, from + 0.5f));
                    if (to == wall.End.X)
                        caps.Add((Part(top ? "corner_ne" : "corner_se")!, to - 0.5f));
                    else if (!Open(to))
                        caps.Add((Part("end_e")!, to - 0.5f));
                }
            return () =>
            {
                var radius = _drawRadius;
                var p = radius / TerrainSurface.TempTexelsPerRadius;
                foreach (var (from, to) in runs)
                {
                    var a = TileScreen(from, y + 1);
                    var b = TileScreen(to, y + 1);
                    var h = frontHeight * p;
                    DrawTextureRectRegion(front, new Rect2(a.X, a.Y - h, b.X - a.X, h), new Rect2(0f, 0f, (b.X - a.X) / p, frontHeight));
                }
                if (gateH is not null)
                    foreach (var opening in openings)
                        DrawArt(gateH, TileScreen(opening + gw / 2f, y + 1), radius);
                foreach (var (art, capX) in caps)
                    DrawArt(art, TileScreen(capX, y + 1), radius);
            };
        }
        // 一排縱牆：每一格畫什麼（0＝城門、1＝斷頭的角樓、2＝牆頂）在版面改變時算好。
        Action Column(int x)
        {
            var openings = gates.Where(g => g.X == x && !TopOrBottom(g)).Select(g => g.Y).ToHashSet();
            var parts = new List<(Texture2D Art, int Row, int Kind)>();
            for (var y = wall.Position.Y + 1; y < wall.End.Y - 1; y++)
            {
                if (openings.Contains(y))
                {
                    if (gateV is not null)
                        parts.Add((gateV, y, 0)); // 1×3：中間一格是路
                    continue;
                }
                if (openings.Contains(y - 1) || openings.Contains(y + 1))
                    continue; // 門柱佔的兩格
                if (!TileLand(x, y))
                    continue;
                // 第 08-20g 章：斷在水邊的一頭補角樓（上一格或下一格是水）。
                if (corner is not null && (!TileLand(x, y - 1) || !TileLand(x, y + 1)))
                    parts.Add((Part(!TileLand(x, y - 1) ? "end_n" : "end_s")!, y, 1));
                else if (side is not null)
                    parts.Add((side, y, 2));
            }
            return () =>
            {
                var radius = _drawRadius;
                var tile = TerrainSurface.TempTile * radius / TerrainSurface.TempTexelsPerRadius;
                foreach (var (art, row, kind) in parts)
                {
                    if (kind == 0)
                        DrawArt(art, TileScreen(x + 0.5f, row + 2), radius);
                    else if (kind == 1)
                        DrawArt(art, TileScreen(x + 0.5f, row + 1), radius);
                    else
                        DrawTextureRect(art, new Rect2(TileScreen(x, row), new Vector2(tile, tile)), false);
                }
            };
        }
        var left = Column(wall.Position.X);
        var right = Column(wall.End.X - 1);
        var topRow = Row(wall.Position.Y);
        var bottomRow = Row(wall.End.Y - 1);
        // 上牆與左右牆排在同一列的建築之前（在後面），下牆排在之後（在前面）。
        Put(items, wall.Position.Y + 1 - WallOrderBias, new Rect2(wall.Position.X - 3f, wall.Position.Y - 5f, wall.Size.X + 6f, wall.Size.Y + 8f), () =>
        {
            left();
            right();
            topRow();
        });
        Put(items, wall.End.Y + WallOrderBias, new Rect2(wall.Position.X - 3f, wall.End.Y - 7f, wall.Size.X + 6f, 10f), bottomRow);
        return true;
    }
}
