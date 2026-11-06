using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18b 章第 4 節之 2：近景層的野生動物與怪物（使用者 2026-11-06：怪物、野獸也要有小人移動，或挑比較重要的顯示）。
/// 數量依模擬裡每格實際的獵物與狼（第 12-1 章 C1）：狼是威脅，每一隻都畫；獵物很多，只挑著畫（每 3 隻一頭鹿，每格最多 2 頭）。
/// 地下城門口有一隻怪物來回踱步。動物只在自己那一格裡遊走（位置只由時間與固定雜湊決定），不進模擬、不存檔。
/// 區域層、戰略層不畫動物（地下城照舊是標記）。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>第 12-1 章 C1：每格的獵物與狼（依格的索引；由觀察者介面注入）。</summary>
    public IReadOnlyList<int> Prey { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> Wolves { get; set; } = Array.Empty<int>();

    /// <summary>【TEMP】每幾隻獵物畫一頭鹿、每格最多幾頭；格半徑至少幾像素才畫；遊走一圈的秒數。</summary>
    private const int TempPreyPerDeer = 3;
    private const int TempMaxDeer = 2;
    private const float TempCreatureMinRadius = 18f;
    private const float TempRoamSeconds = 20f;

    private bool _creaturesDrawn;

    private void DrawCreatures(Vector2 start, float radius)
    {
        _creaturesDrawn = false;
        if (World is null || radius < TempCreatureMinRadius)
            return;
        var seconds = Time.GetTicksMsec() / 1000f;
        var items = new List<(float Y, Action Draw)>();
        var count = World.Cells.Count;
        for (var i = 0; i < count; i++)
        {
            var prey = i < Prey.Count ? Prey[i] : 0;
            var wolves = i < Wolves.Count ? Wolves[i] : 0;
            if (prey <= 0 && wolves <= 0)
                continue;
            var cell = World.Cells[i];
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            if (!OnScreen(center, radius * 1.5f))
                continue;
            var deer = Math.Min(TempMaxDeer, prey / TempPreyPerDeer + (prey > 0 && prey < TempPreyPerDeer ? 1 : 0));
            for (var k = 0; k < deer + wolves; k++)
            {
                var wolf = k >= deer;
                var (at, facing, moving) = Roam(center, radius, i, k, seconds, wolf ? 1.5f : 0.8f);
                var hills = cell.Terrain == DemoTerrain.Hills;
                items.Add((at.Y, () =>
                {
                    if (wolf)
                        DrawWolf(at, radius, facing, moving, seconds);
                    else
                        DrawDeer(at, radius, facing, moving, seconds, hills);
                }));
            }
            _creaturesDrawn = true;
        }
        // 地下城門口的怪物。
        foreach (var d in Dungeons)
        {
            if (!World.Contains(d.Column, d.Row))
                continue;
            var cell = World.GetCell(d.Column, d.Row);
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            if (!OnScreen(center, radius * 1.5f))
                continue;
            var pace = Mathf.Sin(seconds * 0.8f + d.Column * 1.3f + d.Row);
            var at = center + new Vector2(pace * radius * 0.45f, radius * 0.55f);
            var facing = Mathf.Cos(seconds * 0.8f + d.Column * 1.3f + d.Row) >= 0f ? 1f : -1f;
            items.Add((at.Y, () => DrawMonster(at, radius, facing, seconds)));
            _creaturesDrawn = true;
        }
        foreach (var (_, draw) in items.OrderBy(x => x.Y))
            draw();
    }

    /// <summary>在格內遊走：繞著格內一點慢慢走、偶爾停下吃草或張望（位置只看時間與固定雜湊）。</summary>
    private static (Vector2 At, float Facing, bool Moving) Roam(Vector2 center, float radius, int cell, int k, float seconds, float speed)
    {
        var h1 = LayoutHash(cell, k, 101);
        var h2 = LayoutHash(cell, k, 102);
        var h3 = LayoutHash(cell, k, 103);
        var home = center + new Vector2(h1 - 0.5f, h2 - 0.5f) * radius * 0.9f;
        var t = seconds * speed / TempRoamSeconds * Mathf.Tau + h3 * Mathf.Tau;
        // 每圈有一段停下來（吃草、張望）。
        var pause = Mathf.PosMod(t / Mathf.Tau, 1f) > 0.7f;
        var tt = pause ? Mathf.Floor(t / Mathf.Tau) * Mathf.Tau + 0.7f * Mathf.Tau : t;
        var offset = new Vector2(Mathf.Sin(tt) * radius * 0.35f, Mathf.Sin(tt * 2f + h1 * 3f) * radius * 0.18f);
        var velocity = Mathf.Cos(tt);
        return (home + offset, velocity >= 0f ? 1f : -1f, !pause);
    }

    /// <summary>一頭鹿（丘陵上是山羊）：約 10×9 圖像素，走路時腿交替。</summary>
    private void DrawDeer(Vector2 feet, float radius, float facing, bool moving, float seconds, bool goat)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        var coat = goat ? new Color(0.86f, 0.84f, 0.78f) : new Color(0.62f, 0.42f, 0.24f);
        Shadow(feet, 5f * p);
        var step = moving && (int)(seconds * 5f) % 2 == 0 ? p : 0f;
        DrawRect(new Rect2(feet.X - 4f * p, feet.Y - 4f * p + step, p, 4f * p - step), coat.Darkened(0.3f));
        DrawRect(new Rect2(feet.X + 3f * p, feet.Y - 4f * p + (p - step), p, 4f * p - (p - step)), coat.Darkened(0.3f));
        DrawRect(new Rect2(feet.X - 5f * p, feet.Y - 7f * p, 10f * p, 3f * p), coat);
        DrawRect(new Rect2(feet.X - 4f * p, feet.Y - 5f * p, 8f * p, p), goat ? coat.Darkened(0.1f) : new Color(0.90f, 0.82f, 0.66f)); // 肚子
        var headX = feet.X + facing * 5f * p - (facing < 0 ? 3f * p : 0f);
        var grazing = !moving;
        var headY = grazing ? feet.Y - 5f * p : feet.Y - 10f * p;
        DrawRect(new Rect2(headX, headY, 3f * p, 3f * p), coat);
        DrawRect(new Rect2(headX + (facing > 0 ? 2f : 0f) * p, headY + p, p, p), Ink);
        if (!grazing)
        {
            var horn = goat ? coat.Darkened(0.4f) : new Color(0.80f, 0.72f, 0.56f);
            DrawRect(new Rect2(headX + p, headY - 2f * p, p, 2f * p), horn); // 鹿角／山羊角
            DrawRect(new Rect2(headX + (facing > 0 ? 2f : -1f) * p, headY - 3f * p, p, p), horn);
        }
    }

    /// <summary>一匹狼：灰色、尾巴、豎耳，約 11×7 圖像素；走得比鹿快。</summary>
    private void DrawWolf(Vector2 feet, float radius, float facing, bool moving, float seconds)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        var fur = new Color(0.48f, 0.48f, 0.50f);
        Shadow(feet, 5.5f * p);
        var step = moving && (int)(seconds * 7f) % 2 == 0 ? p : 0f;
        DrawRect(new Rect2(feet.X - 4f * p, feet.Y - 3f * p + step, p, 3f * p - step), fur.Darkened(0.35f));
        DrawRect(new Rect2(feet.X + 3f * p, feet.Y - 3f * p + (p - step), p, 3f * p - (p - step)), fur.Darkened(0.35f));
        DrawRect(new Rect2(feet.X - 5f * p, feet.Y - 6f * p, 10f * p, 3f * p), fur);
        DrawRect(new Rect2(feet.X - 5f * p, feet.Y - 6f * p, 10f * p, p), fur.Darkened(0.2f)); // 背上深色
        var tailX = facing > 0 ? feet.X - 8f * p : feet.X + 5f * p;
        DrawRect(new Rect2(tailX, feet.Y - 6f * p, 3f * p, p), fur.Darkened(0.15f));
        var headX = facing > 0 ? feet.X + 4f * p : feet.X - 8f * p;
        DrawRect(new Rect2(headX, feet.Y - 8f * p, 4f * p, 3f * p), fur);
        DrawRect(new Rect2(headX + (facing > 0 ? 3f : -1f) * p, feet.Y - 7f * p, 2f * p, 2f * p), fur.Lightened(0.15f)); // 口鼻
        DrawRect(new Rect2(headX + p, feet.Y - 10f * p, p, 2f * p), fur.Darkened(0.2f)); // 耳朵
        DrawRect(new Rect2(headX + (facing > 0 ? 2f : 1f) * p, feet.Y - 7f * p, p, p), new Color(0.95f, 0.80f, 0.25f)); // 眼睛
    }

    /// <summary>地下城的怪物：比小人大一倍的深色身影、角、發光的眼睛，在門口來回踱步。</summary>
    private void DrawMonster(Vector2 feet, float radius, float facing, float seconds)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        var skin = new Color(0.30f, 0.22f, 0.34f);
        Shadow(feet, 8f * p);
        var bob = Mathf.Sin(seconds * 3f) > 0f ? p : 0f;
        DrawRect(new Rect2(feet.X - 5f * p, feet.Y - 6f * p, 3f * p, 6f * p), skin.Darkened(0.3f));
        DrawRect(new Rect2(feet.X + 2f * p, feet.Y - 6f * p, 3f * p, 6f * p), skin.Darkened(0.3f));
        var body = new Rect2(feet.X - 7f * p, feet.Y - 20f * p + bob, 14f * p, 14f * p);
        DrawRect(body, skin);
        DrawRect(new Rect2(body.Position.X + (facing > 0 ? 0f : 10f) * p, body.Position.Y, 4f * p, body.Size.Y), skin.Darkened(0.25f));
        DrawRect(new Rect2(feet.X - 9f * p, feet.Y - 16f * p + bob, 2f * p, 8f * p), skin.Darkened(0.1f)); // 手臂
        DrawRect(new Rect2(feet.X + 7f * p, feet.Y - 16f * p + bob, 2f * p, 8f * p), skin.Darkened(0.1f));
        DrawRect(new Rect2(feet.X - 6f * p, feet.Y - 24f * p + bob, 2f * p, 4f * p), new Color(0.85f, 0.80f, 0.70f)); // 角
        DrawRect(new Rect2(feet.X + 4f * p, feet.Y - 24f * p + bob, 2f * p, 4f * p), new Color(0.85f, 0.80f, 0.70f));
        var glow = new Color(1f, 0.35f, 0.20f, 0.7f + 0.3f * Mathf.Sin(seconds * 4f));
        DrawRect(new Rect2(feet.X + (facing > 0 ? 0f : -4f) * p, feet.Y - 17f * p + bob, 2f * p, 2f * p), glow);
        DrawRect(new Rect2(feet.X + (facing > 0 ? 3f : -1f) * p, feet.Y - 17f * p + bob, 2f * p, 2f * p), glow);
        DrawRect(body, Ink, false, Math.Max(1f, p));
    }

    private void Shadow(Vector2 feet, float width)
    {
        DrawSetTransform(feet + new Vector2(width * 0.15f, 0f), 0f, new Vector2(1f, 0.35f));
        DrawCircle(Vector2.Zero, width, new Color(0f, 0f, 0f, 0.28f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }
}
