using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-56 章（試玩回饋 2026-11-26 第二次之 2：近距離看地圖時，地形、河流醜到不行）：近看的地形點綴，參考 CK3 細節層——
/// 森林裡站著一棵棵樹（第十二批的橡樹、松樹），丘陵是山丘與山（第十四批，交件前不畫、照舊由著色器畫山形），草原偶爾有灌木（第十四批）。
/// 每格依座標雜湊決定位置與大小（每次都一樣），由上往下畫（前面的擋後面的）；聚落、河流所在的格不畫。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】每格幾像素開始畫樹（之下淡入）、樹的高度（格）、山的高度（格）。</summary>
    private const float TempTreeCellPx = 44f, TempTreeFadePx = 16f, TempTreeHeight = 0.36f, TempMountainHeight = 0.62f;

    /// <summary>第 08-56 章：第十四批的山、丘、灌木素材都到了沒（到了就不再由著色器畫山形）。</summary>
    private bool CloseupArt => PixelParts.Texture("b14_mtn_1") is not null && PixelParts.Texture("b14_hill_1") is not null;

    private static float Hash01(int x, int y, int k)
    {
        unchecked
        {
            var h = x * 73856093 ^ y * 19349663 ^ k * 83492791;
            h ^= h >> 13;
            h *= 1274126177;
            return (h & 0x7fffffff) / (float)int.MaxValue;
        }
    }

    private readonly List<(float Y, CanvasTexture Texture, Rect2 Rect, float Shadow)> _closeSprites = new();

    private void DrawCloseTerrain(Vector2 origin, float cellPx)
    {
        if (World is null || cellPx < TempTreeCellPx - TempTreeFadePx || _mode == Mode.Food)
            return;
        var fade = Mathf.Clamp((cellPx - (TempTreeCellPx - TempTreeFadePx)) / TempTreeFadePx, 0f, 1f);
        var trees = new[] { PixelParts.Texture("b12_n_tree_oak_1"), PixelParts.Texture("b12_n_tree_oak_2"), PixelParts.Texture("b12_n_tree_pine_1"), PixelParts.Texture("b12_n_tree_pine_2") }
            .Where(t => t is not null).Select(t => t!).ToArray();
        var mountains = new[] { PixelParts.Texture("b14_mtn_1"), PixelParts.Texture("b14_mtn_2"), PixelParts.Texture("b14_mtn_3") }.Where(t => t is not null).Select(t => t!).ToArray();
        var hills = new[] { PixelParts.Texture("b14_hill_1"), PixelParts.Texture("b14_hill_2") }.Where(t => t is not null).Select(t => t!).ToArray();
        var bushes = new[] { PixelParts.Texture("b14_bush_1"), PixelParts.Texture("b14_bush_2") }.Where(t => t is not null).Select(t => t!).ToArray();
        var c0 = Math.Max(0, (int)MathF.Floor(-origin.X / cellPx) - 1);
        var r0 = Math.Max(0, (int)MathF.Floor(-origin.Y / cellPx) - 1);
        var c1 = Math.Min(World.Columns - 1, (int)MathF.Ceiling((Size.X - origin.X) / cellPx) + 1);
        var r1 = Math.Min(World.Rows - 1, (int)MathF.Ceiling((Size.Y - origin.Y) / cellPx) + 1);
        _closeSprites.Clear();
        void Put(CanvasTexture texture, Vector2 foot, float height)
        {
            var size = new Vector2(height * texture.GetWidth() / Math.Max(1, texture.GetHeight()), height);
            _closeSprites.Add((foot.Y, texture, new Rect2(foot - new Vector2(size.X / 2f, size.Y), size), size.X * 0.42f));
        }
        bool Hill(int c, int r) => World.Contains(c, r) && World.GetCell(c, r).Terrain == DemoTerrain.Hills;
        for (var r = r0; r <= r1; r++)
            for (var c = c0; c <= c1; c++)
            {
                var cell = World.GetCell(c, r);
                if (cell.Terrain == DemoTerrain.Water || DemoProvinceMap.IsRiver(cell) || Sites?.SettlementAt(c, r) is { IsRuin: false })
                    continue;
                var corner = origin + new Vector2(c, r) * cellPx;
                switch (cell.Terrain)
                {
                    case DemoTerrain.Forest when trees.Length > 0:
                    {
                        // 森林：每格 2×2 到 3×3 棵（越近越多），位置錯開一點。
                        var per = cellPx >= 80f ? 3 : 2;
                        for (var j = 0; j < per; j++)
                            for (var i = 0; i < per; i++)
                            {
                                var k = j * per + i;
                                var jitter = new Vector2(Hash01(c, r, k) - 0.5f, Hash01(c, r, k + 17) - 0.5f) * (0.8f / per);
                                var foot = corner + (new Vector2(i + 0.5f, j + 0.8f) / per + jitter) * cellPx;
                                var pine = Hash01(c, r, k + 31) < (Hill(c - 1, r) || Hill(c + 1, r) ? 0.75f : 0.35f);
                                var pick = trees[Math.Min(trees.Length - 1, (pine ? 2 : 0) + (Hash01(c, r, k + 47) < 0.5f ? 0 : 1))];
                                Put(pick, foot, cellPx * TempTreeHeight * (0.85f + 0.3f * Hash01(c, r, k + 59)));
                            }
                        break;
                    }
                    case DemoTerrain.Hills:
                    {
                        // 丘陵：四周大多是丘陵＝山（一格一座大的），邊上＝兩座山丘（素材交件前由著色器畫山形）。
                        var around = 0;
                        for (var dr = -1; dr <= 1; dr++)
                            for (var dc = -1; dc <= 1; dc++)
                                if ((dr != 0 || dc != 0) && Hill(c + dc, r + dr))
                                    around++;
                        if (around >= 6 && mountains.Length > 0)
                        {
                            var pick = mountains[(int)(Hash01(c, r, 3) * mountains.Length) % mountains.Length];
                            var foot = corner + new Vector2(0.5f + (Hash01(c, r, 5) - 0.5f) * 0.25f, 0.92f) * cellPx;
                            Put(pick, foot, cellPx * TempMountainHeight * (0.85f + 0.3f * Hash01(c, r, 7)));
                        }
                        else if (hills.Length > 0)
                            for (var k = 0; k < 2; k++)
                            {
                                var pick = hills[(int)(Hash01(c, r, 11 + k) * hills.Length) % hills.Length];
                                var foot = corner + new Vector2(0.3f + 0.4f * k + (Hash01(c, r, 13 + k) - 0.5f) * 0.2f, 0.55f + 0.35f * Hash01(c, r, 15 + k)) * cellPx;
                                Put(pick, foot, cellPx * 0.26f);
                            }
                        break;
                    }
                    case DemoTerrain.Grassland when bushes.Length > 0 && Hash01(c, r, 21) < 0.35f:
                    {
                        var pick = bushes[(int)(Hash01(c, r, 23) * bushes.Length) % bushes.Length];
                        var foot = corner + new Vector2(0.2f + 0.6f * Hash01(c, r, 25), 0.3f + 0.6f * Hash01(c, r, 27)) * cellPx;
                        Put(pick, foot, cellPx * 0.14f);
                        break;
                    }
                }
            }
        // 由上往下畫：先畫影子，再畫素材（前面的擋後面的）。
        _closeSprites.Sort((a, b) => a.Y.CompareTo(b.Y));
        var tint = new Color(1f, 1f, 1f, fade);
        foreach (var (_, texture, rect, shadow) in _closeSprites)
        {
            DrawSetTransform(new Vector2(rect.Position.X + rect.Size.X / 2f + 2f, rect.End.Y - 1f), 0f, new Vector2(1f, 0.32f));
            DrawCircle(Vector2.Zero, shadow, new Color(0f, 0f, 0f, 0.28f * fade));
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            DrawTextureRect(texture, rect, false, tint);
        }
    }
}
