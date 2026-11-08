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

    /// <summary>門口在這個方格的地下城（沒有資料回傳 null）。</summary>
    private DemoMonsterNest? NestAtGate(Vector2I gate)
    {
        var n = TerrainSurface.TilesPerCell;
        return MonsterNests.FirstOrDefault(nest => nest.Dungeon.Cell.Column == gate.X / n && nest.Dungeon.Cell.Row == gate.Y / n);
    }

    private void DrawMonsterRealms(Vector2 start, float radius, bool fill)
        => DrawMonsterRealms(start, radius, fill ? (_mode == Mode.Realm ? 0.42f : 0.16f) : 0f);

    /// <summary>試玩回饋 2026-11-08 之 2：勢力模式裡怪物的範圍也是一個勢力（較濃的暗紫色），地形模式淡淡一層。
    /// 第 12-2a 節：範圍是怪物實際佔據的格（Core 算好的 <see cref="DemoMonsterNest.Cells"/>），沿格邊畫外框。</summary>
    private void DrawMonsterRealms(Vector2 start, float radius, float fill)
    {
        if (World is null || MonsterNests.Count == 0)
            return;
        var font = ThemeDB.FallbackFont;
        var width = Math.Clamp(radius * 0.1f, 1.5f, 3f);
        foreach (var nest in MonsterNests)
        {
            if (nest.Cells.Count == 0)
                continue;
            var owned = nest.Cells.ToHashSet();
            var top = float.MaxValue;
            float left = float.MaxValue, right = float.MinValue;
            foreach (var at in nest.Cells)
            {
                if (!World.Contains(at.Column, at.Row))
                    continue;
                var cell = World.GetCell(at.Column, at.Row);
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                top = Math.Min(top, center.Y - radius);
                left = Math.Min(left, center.X - radius);
                right = Math.Max(right, center.X + radius);
                if (!OnScreen(center, radius))
                    continue;
                var shape = ScreenShape(cell, center, radius);
                if (fill > 0f)
                    DrawShape(center, shape, new Color(MonsterColor, fill));
                foreach (var (dq, dr, _) in DemoRouteNetwork.Orthogonal)
                    if (!owned.Contains(new DemoCellRef(at.Column + dq, at.Row + dr)))
                        DrawPolyline(EdgeOf(shape, HexCoordinates.EdgeToward(dq, dr)), new Color(MonsterColor.Darkened(0.2f), 0.9f), width);
            }
            if (top == float.MaxValue || !new Rect2(left, top, right - left, radius * 2f).Intersects(new Rect2(Vector2.Zero, Size)))
                continue;
            var label = $"{nest.Dungeon.Name}｜怪物 {nest.Monsters}";
            const int size = 12;
            var labelWidth = font.GetStringSize(label, HorizontalAlignment.Left, -1, size).X;
            var pos = new Vector2((left + right) / 2f - labelWidth / 2f, top + 16f);
            DrawRect(new Rect2(pos - new Vector2(4f, 13f), new Vector2(labelWidth + 8f, 17f)), new Color(0.12f, 0.04f, 0.14f, 0.8f));
            DrawString(font, pos, label, HorizontalAlignment.Left, -1, size, new Color(0.95f, 0.80f, 1f));
        }
    }
}
