using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 12-2 章：怪物的勢力範圍（使用者 2026-11-07：怪物出來後也要畫出自己的勢力範圍）。
/// 有怪物的地下城以自己為中心、半徑＝<see cref="DemoMonsterNest.Radius"/> 格的方形範圍：近景與區域層罩一層暗紫色、沿外框畫暗紫色邊；
/// 範圍上方標「名字 怪物 N」。沉寂的地下城沒有範圍、門口也沒有怪物。只改畫面（數量與範圍來自 Core）。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>每個地下城的怪物（由觀察者介面每次刷新時給）。</summary>
    public IReadOnlyList<DemoMonsterNest> MonsterNests { get; set; } = Array.Empty<DemoMonsterNest>();

    private static readonly Color MonsterColor = new(0.36f, 0.12f, 0.42f);

    /// <summary>近景門口要畫幾隻怪物：每 4 隻一隻、最多 3 隻；沉寂 0；沒有資料（舊畫面）畫 1 隻。</summary>
    private int MonstersShownAt(Vector2I gate)
    {
        if (World is null)
            return 1;
        var n = TerrainSurface.TilesPerCell;
        foreach (var nest in MonsterNests)
            if (nest.Dungeon.Cell.Column == gate.X / n && nest.Dungeon.Cell.Row == gate.Y / n)
                return nest.Monsters <= 0 ? 0 : Math.Clamp((nest.Monsters + 3) / 4, 1, 3);
        return 1;
    }

    private void DrawMonsterRealms(Vector2 start, float radius, bool fill)
    {
        if (World is null || MonsterNests.Count == 0)
            return;
        var font = ThemeDB.FallbackFont;
        foreach (var nest in MonsterNests)
        {
            if (nest.Radius <= 0 || !World.Contains(nest.Dungeon.Cell.Column, nest.Dungeon.Cell.Row))
                continue;
            var c = nest.Dungeon.Cell;
            var x0 = Math.Max(0, c.Column - nest.Radius);
            var y0 = Math.Max(0, c.Row - nest.Radius);
            var x1 = Math.Min(World.Columns - 1, c.Column + nest.Radius);
            var y1 = Math.Min(World.Rows - 1, c.Row + nest.Radius);
            var a = start + HexCoordinates.Center(World.GetCell(x0, y0).AxialQ, World.GetCell(x0, y0).AxialR, radius) - Vector2.One * radius;
            var b = start + HexCoordinates.Center(World.GetCell(x1, y1).AxialQ, World.GetCell(x1, y1).AxialR, radius) + Vector2.One * radius;
            var rect = new Rect2(a, b - a);
            if (!rect.Intersects(new Rect2(Vector2.Zero, Size)))
                continue;
            if (fill)
                DrawRect(rect, new Color(MonsterColor, 0.16f));
            DrawRect(rect, new Color(MonsterColor.Darkened(0.2f), 0.85f), false, Math.Clamp(radius * 0.1f, 1.5f, 3f));
            var label = $"{nest.Dungeon.Name}｜怪物 {nest.Monsters}";
            var size = 12;
            var width = font.GetStringSize(label, HorizontalAlignment.Left, -1, size).X;
            var at = new Vector2(rect.Position.X + rect.Size.X / 2f - width / 2f, rect.Position.Y + 16f);
            DrawRect(new Rect2(at - new Vector2(4f, 13f), new Vector2(width + 8f, 17f)), new Color(0.12f, 0.04f, 0.14f, 0.8f));
            DrawString(font, at, label, HorizontalAlignment.Left, -1, size, new Color(0.95f, 0.80f, 1f));
        }
    }
}
