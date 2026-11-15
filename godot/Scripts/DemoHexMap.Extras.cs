using System;
using System.Collections.Generic;
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
    private void AddBridges(List<(float Y, Action Draw)> items, SettlementPlan layout, Func<float, float, Vector2> screen, float radius)
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
            var bottom = screen(r.X + 0.5f, r.Y + 1f);
            var (n, f) = (name, flip);
            items.Add((bottom.Y - radius, () => DrawArt(n, bottom, radius, flip: f))); // 橋貼著水面：比同一列的建築先畫
        }
    }

    /// <summary>【TEMP】住屋旁小物件的出現機率（每戶最多兩件）。</summary>
    private const float TempYardPropChance = 0.55f;

    /// <summary>
    /// 住屋的院子：房子圖上方空著的幾列與屋後第一列院子，依雜湊擺一兩件小物件（村莊：乾草堆、柴堆、雞舍、狗屋、曬衣繩；
    /// 城鎮以上：柴堆、曬衣繩、水桶、花圃、長凳）。農舍屋後的菜畦後半種一排果樹（蘋果、梨、李），村莊的菜畦裡偶爾有稻草人。
    /// </summary>
    private void AddYardProps(List<(float Y, Action Draw)> items, SettlementPlan layout, DemoSettlementTier tier, Func<float, float, Vector2> screen, float radius)
    {
        if (!PixelParts.IsBatch5("b11_p_woodpile"))
            return;
        var rural = tier <= DemoSettlementTier.Village;
        var props = rural
            ? new[] { "b11_p_haystack", "b11_p_woodpile", "b11_p_coop", "b11_p_doghouse", "b11_p_clothesline" }
            : new[] { "b11_p_woodpile", "b11_p_clothesline", "b11_p_bucket", "b11_p_flowerbed", "b11_p_bench" };
        foreach (var lot in layout.Lots)
        {
            if (!IsResidence(lot.Kind) || lot.Construction || lot.Kind == Building.Tent)
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
                var bottom = screen(x, y);
                var n = name;
                items.Add((bottom.Y, () => DrawArt(n, bottom, radius)));
            }
            if (lot.Kind == Building.Farmhouse)
            {
                // 菜畦後半（屋後第 4～6 列）種一排果樹，兩格一棵。
                var kind = new[] { "b11_n_orchard_apple", "b11_n_orchard_pear", "b11_n_orchard_plum" }[(int)(LayoutHash(lot.X, lot.Y, 213) * 3) % 3];
                for (var i = 0; i + 1 < lot.W + 1; i += 2)
                {
                    var tree = screen(lot.X + i + 1f, lot.Y - 3f);
                    items.Add((tree.Y, () => DrawArt(kind, tree, radius)));
                }
            }
            else if (rural && LayoutHash(lot.X, lot.Y, 214) < 0.25f)
            {
                var crow = screen(lot.X + lot.W / 2f, lot.Y - 2f);
                items.Add((crow.Y, () => DrawArt("b11_p_scarecrow", crow, radius)));
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
