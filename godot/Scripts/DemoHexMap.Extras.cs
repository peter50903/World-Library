using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 09-1b 章：第十一批還沒用到的素材——街過水處的橋、住屋後院的小物件、農舍的果樹、地圖符號。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>
    /// 聚落裡的街經過水面（河穿過城內）時畫木橋：左右有街＝橫橋，否則直橋；橋頭（朝岸的那一端）用 end 圖，
    /// 橫橋東端左右翻轉；直橋只有一張南端的橋頭，北端用橋身。
    /// </summary>
    private void AddBridges(List<DrawItem> items, SettlementPlan layout)
    {
        if (!PixelParts.IsBatch5("b11_bridge_h_mid"))
            return;
        var roads = new HashSet<Vector2I>(layout.Roads);
        foreach (var r in layout.Roads)
        {
            if (TileLand(r.X, r.Y))
                continue;
            var horizontal = roads.Contains(r + Vector2I.Left) || roads.Contains(r + Vector2I.Right);
            string name;
            var flip = false;
            if (horizontal)
            {
                var west = TileLand(r.X - 1, r.Y);
                var east = TileLand(r.X + 1, r.Y);
                name = west || east ? "b11_bridge_h_end" : "b11_bridge_h_mid";
                flip = east && !west;
            }
            else
                name = TileLand(r.X, r.Y + 1) ? "b11_bridge_v_end" : "b11_bridge_v_mid";
            var (bx, by) = (r.X + 0.5f, r.Y + 1f);
            var (n, f) = (name, flip);
            // 橋貼著水面：比同一列的建築先畫（往前排半格＝原本的一個格半徑）。
            Put(items, by - TerrainSurface.TilesPerCell / 2f, new Rect2(bx - 2.5f, by - 4f, 5f, 5f), () => DrawArt(n, TileScreen(bx, by), _drawRadius, flip: f));
        }
    }

    /// <summary>
    /// 第 09-1c 章：連棟街屋——同一條街、彼此相接的地塊接成一整排，用第十一批的左、中、右三種（每張 3 格寬）拼起來，
    /// 排的總寬除以 3 的餘數平分在兩頭（留成小巷）。款式取這一排第一塊的。
    /// </summary>
    private void AddRowRuns(List<DrawItem> items, SettlementPlan layout, Color culture)
    {
        var rows = layout.Lots.Where(l => l.Kind == Building.RowHouse && !l.Construction).OrderBy(l => l.Y + l.H).ThenBy(l => l.X).ToList();
        var i = 0;
        while (i < rows.Count)
        {
            var first = rows[i];
            var end = first.X + first.W;
            var j = i + 1;
            while (j < rows.Count && rows[j].Y + rows[j].H == first.Y + first.H && rows[j].X == end)
                end = rows[j++].X + rows[j - 1].W;
            var total = end - first.X;
            var count = Math.Max(1, total / 3);
            var start = first.X + (total - count * 3) / 2f;
            var variant = Math.Clamp(first.Variant, 1, 2);
            for (var k = 0; k < count; k++)
            {
                var part = count == 1 ? "mid" : k == 0 ? "left" : k == count - 1 ? "right" : "mid";
                var name = $"b11_row_{part}_{variant}";
                var (sx, sy) = (start + k * 3 + 1.5f, (float)(first.Y + first.H));
                Put(items, sy, new Rect2(sx - 3.5f, first.Y - 4f, 7f, first.H + 6f), () =>
                {
                    if (((Texture2D?)PixelParts.Tinted(name, culture, 1f) ?? PixelParts.Texture(name)) is { } art)
                        DrawArt(art, TileScreen(sx, sy), _drawRadius);
                });
            }
            i = j;
        }
    }

    /// <summary>【TEMP】住屋旁小物件的出現機率（每戶最多兩件）。</summary>
    private const float TempYardPropChance = 0.55f;

    /// <summary>
    /// 住屋的院子：房子圖上方空著的幾列與屋後第一列院子，依雜湊擺一兩件小物件（村莊：乾草堆、柴堆、雞舍、狗屋、曬衣繩；
    /// 城鎮以上：柴堆、曬衣繩、水桶、花圃、長凳）。農舍屋後的菜畦後半種一排果樹（蘋果、梨、李），村莊的菜畦裡偶爾有稻草人。
    /// </summary>
    private void AddYardProps(List<DrawItem> items, SettlementPlan layout, DemoSettlementTier tier)
    {
        if (!PixelParts.IsBatch5("b11_p_woodpile"))
            return;
        var rural = tier <= DemoSettlementTier.Village;
        var props = rural
            ? new[] { "b11_p_haystack", "b11_p_woodpile", "b11_p_coop", "b11_p_doghouse", "b11_p_clothesline" }
            : new[] { "b11_p_woodpile", "b11_p_clothesline", "b11_p_bucket", "b11_p_flowerbed", "b11_p_bench" };
        foreach (var lot in layout.Lots)
        {
            if (!IsResidence(lot.Kind) || lot.Construction || lot.Kind is Building.Tent or Building.RowHouse)
                continue;
            var spriteH = SpriteSize(lot)?.H ?? lot.H;
            var free = lot.H - spriteH; // 房子圖上方空著的列（地塊比圖深）
            for (var k = 0; k < 2; k++)
            {
                var h = LayoutHash(lot.X * 3 + k, lot.Y, 211);
                if (h >= TempYardPropChance)
                    continue;
                var name = props[(int)(LayoutHash(lot.X, lot.Y + k, 212) * props.Length) % props.Length];
                var wide = name is "b11_p_clothesline" or "b11_p_bench";
                var x = k == 0 ? lot.X + 0.5f + (wide ? 0.5f : 0f) : lot.X + lot.W - 0.5f - (wide ? 0.5f : 0f);
                var y = free > 0 ? lot.Y + free : lot.Y; // 有空列擺在房子後面，否則擺在屋後院子
                var (px, py) = (x, (float)y);
                var n = name;
                Put(items, py, new Rect2(px - 3f, py - 5f, 6f, 7f), () => DrawArt(n, TileScreen(px, py), _drawRadius));
            }
            // 第 09-1c 章：村子菜畦（屋後一列）偶爾立稻草人；農舍在隔壁空地（村莊隔一塊蓋一棟）種兩棵果樹。
            if (rural && LayoutHash(lot.X, lot.Y, 214) < 0.25f)
            {
                var (cx, cy) = (lot.X + lot.W / 2f, (float)lot.Y);
                Put(items, cy, new Rect2(cx - 3f, cy - 5f, 6f, 7f), () => DrawArt("b11_p_scarecrow", TileScreen(cx, cy), _drawRadius));
            }
            if (lot.Kind == Building.Farmhouse)
            {
                var kind = new[] { "b11_n_orchard_apple", "b11_n_orchard_pear", "b11_n_orchard_plum" }[(int)(LayoutHash(lot.X, lot.Y, 213) * 3) % 3];
                var side = LayoutHash(lot.X, lot.Y, 215) < 0.5f ? -1 : 1;
                var nx = side < 0 ? lot.X - 2 : lot.X + lot.W;
                if (!layout.Lots.Any(o => o.X < nx + 2 && nx < o.X + o.W && o.Y < lot.Y + lot.H && lot.Y < o.Y + o.H))
                    for (var k = 0; k < 2; k++)
                    {
                        var (ox, oy) = (nx + 1f, lot.Y + lot.H - 2f * k);
                        Put(items, oy, new Rect2(ox - 3f, oy - 7f, 6f, 9f), () => DrawArt(kind, TileScreen(ox, oy), _drawRadius));
                    }
            }
        }
    }

    /// <summary>地圖符號：素材在就畫在 <paramref name="center"/> 正中（邊長 <paramref name="size"/> 螢幕像素、可染色）；沒有回傳 false。</summary>
    private bool DrawSymbol(string name, Vector2 center, float size, Color modulate)
    {
        if (!PixelParts.IsBatch5(name) || PixelParts.Texture(name) is not { } texture)
            return false;
        var aspect = texture.GetWidth() / (float)Math.Max(1, texture.GetHeight());
        var s = new Vector2(size * aspect, size);
        DrawTextureRect(texture, new Rect2(center - s / 2f, s), false, modulate);
        return true;
    }

    /// <summary>戰爭箭頭的箭頭端（第十一批旗箭，順著箭頭方向旋轉）；沒有素材回傳 false。</summary>
    private bool DrawArrowHead(Vector2 tip, Vector2 dir, float size, float alpha)
    {
        if (!PixelParts.IsBatch5("b11_sym_arrow_head") || PixelParts.Texture("b11_sym_arrow_head") is not { } head)
            return false;
        DrawSetTransform(tip, dir.Angle(), Vector2.One);
        DrawTextureRect(head, new Rect2(-size, -size / 2f, size, size), false, new Color(1f, 1f, 1f, alpha));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        return true;
    }
}
