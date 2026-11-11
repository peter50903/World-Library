using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18m 章：一條戰線（進行中的征服戰爭、獨立戰爭或內戰）。攻方從 <see cref="From"/> 出兵圍攻 <see cref="Target"/>。
/// <see cref="AttackerRealm"/>、<see cref="DefenderRealm"/>＝決定旗色的勢力（叛軍用 <see cref="RebelColor"/>）。
/// </summary>
public sealed record WarFront(int Key, string Label, DemoCellRef From, DemoCellRef Target, int TargetSettlement,
    int AttackerRealm, int DefenderRealm, long AttackerArmy, long DefenderArmy, long AttackerLosses, long DefenderLosses, int Weeks,
    Color? RebelColor = null, bool RebelsAttack = false);

/// <summary>
/// 第 08-18m 章（試玩回饋 2026-11-11 之 8：勢力間的戰鬥表現參考十字軍之王與 Fantasy Map Simulator）：戰爭期間一直看得到——
/// ① 戰爭目標的勢力範圍罩上攻方顏色的斜線（十字軍之王的佔領斜紋）；② 從出兵地到目標的行軍路線（虛線往前流動、箭頭指向目標，FMS）；
/// ③ 雙方的軍旗棋子（旗子染勢力色、下方寫兵力，十字軍之王的軍隊棋）；④ 目標上方的交戰圖示與戰爭比分條（攻方色佔的比例＝守方死亡的比例）。
/// 軍旗與比分條是固定像素大小（任何縮放都看得清楚）。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>進行中的戰線（由觀察者介面每次刷新時給）。</summary>
    public IReadOnlyList<WarFront> WarFronts { get; set; } = Array.Empty<WarFront>();

    /// <summary>【TEMP】軍旗大小（像素）、比分條寬度、斜線間距（格半徑的倍數）。</summary>
    private const float TempBannerSize = 14f, TempScoreBarWidth = 52f, TempHatchStep = 0.32f;

    private Color FrontColor(WarFront front, bool attacker)
        => attacker
            ? (front.RebelsAttack && front.RebelColor is { } rebel ? rebel : RealmColor(front.AttackerRealm))
            : (!front.RebelsAttack && front.RebelColor is { } rebels ? rebels : RealmColor(front.DefenderRealm));

    private void DrawWars(Vector2 start, float radius)
    {
        if (World is null || WarFronts.Count == 0)
            return;
        var font = ThemeDB.FallbackFont;
        var seconds = Time.GetTicksMsec() / 1000f;
        foreach (var front in WarFronts)
        {
            if (!World.Contains(front.Target.Column, front.Target.Row) || !World.Contains(front.From.Column, front.From.Row))
                continue;
            var attackerColor = FrontColor(front, true);
            var defenderColor = FrontColor(front, false);
            var target = ScreenOf(start, radius, front.Target);
            var from = ScreenOf(start, radius, front.From);
            // ① 佔領斜紋：目標聚落直屬的格。
            var step = Math.Max(4f, radius * TempHatchStep);
            foreach (var (at, owner) in Territory.Cells)
            {
                if (owner.SettlementId != front.TargetSettlement)
                    continue;
                var cell = World.GetCell(at.Column, at.Row);
                var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                if (!OnScreen(center, radius))
                    continue;
                Hatch(BoundsOf(ScreenShape(cell, center, radius)), step, new Color(attackerColor, 0.45f), Math.Max(1f, radius * 0.06f));
            }
            // ② 行軍路線：虛線往目標流動，末端箭頭。
            var line = target - from;
            var length = line.Length();
            if (length > radius)
            {
                var dir = line / length;
                var dash = Math.Max(6f, radius * 0.5f);
                var offset = seconds * dash * 1.5f % (dash * 2f);
                var end = length - radius * 0.6f;
                for (var d = offset - dash * 2f; d < end; d += dash * 2f)
                {
                    var a = Math.Max(0f, d);
                    var b = Math.Min(end, d + dash);
                    if (b > a)
                    {
                        DrawLine(from + dir * a, from + dir * b, new Color(0f, 0f, 0f, 0.55f), 4f);
                        DrawLine(from + dir * a, from + dir * b, attackerColor.Lightened(0.15f), 2f);
                    }
                }
                var tip = from + dir * end;
                var side = new Vector2(-dir.Y, dir.X) * 6f;
                DrawColoredPolygon(new[] { tip + dir * 8f, tip + side, tip - side }, attackerColor.Lightened(0.15f));
            }
            // ③ 軍旗：攻方在路線上靠近目標處（圍攻），守方在目標。
            var siege = length > radius ? target - line / length * Math.Min(length * 0.5f, Math.Max(radius * 0.9f, 22f)) : target + new Vector2(-24f, 0f);
            DrawBanner(font, siege, attackerColor, front.AttackerArmy);
            DrawBanner(font, target + new Vector2(length > radius ? 18f : 24f, 0f), defenderColor, front.DefenderArmy);
            // ④ 交戰圖示與比分條（攻方色＝守方死亡的比例）。
            var bar = target - new Vector2(TempScoreBarWidth / 2f, TempBannerSize * 2.6f);
            DrawCircle(bar + new Vector2(TempScoreBarWidth / 2f, -9f), 9f, new Color(0.08f, 0.05f, 0.03f, 0.85f));
            var swords = font.GetStringSize("⚔", HorizontalAlignment.Left, -1, 14).X;
            DrawString(font, bar + new Vector2(TempScoreBarWidth / 2f - swords / 2f, -4f), "⚔", HorizontalAlignment.Left, -1, 14,
                new Color(1f, 0.9f, 0.6f, 0.75f + 0.25f * Mathf.Sin(seconds * 4f)));
            var total = front.AttackerLosses + front.DefenderLosses;
            var share = total <= 0 ? 0.5f : front.DefenderLosses / (float)total;
            DrawRect(new Rect2(bar - new Vector2(1f, 1f), new Vector2(TempScoreBarWidth + 2f, 7f)), new Color(0.08f, 0.05f, 0.03f, 0.9f));
            DrawRect(new Rect2(bar, new Vector2(TempScoreBarWidth * share, 5f)), attackerColor);
            DrawRect(new Rect2(bar + new Vector2(TempScoreBarWidth * share, 0f), new Vector2(TempScoreBarWidth * (1f - share), 5f)), defenderColor);
            DrawLine(bar + new Vector2(TempScoreBarWidth / 2f, -1f), bar + new Vector2(TempScoreBarWidth / 2f, 6f), new Color(1f, 1f, 1f, 0.7f), 1f);
        }
    }

    /// <summary>軍旗棋子：旗桿、染勢力色的旗面、下方的兵力牌（固定像素大小）。</summary>
    private void DrawBanner(Font font, Vector2 foot, Color color, long army)
    {
        var s = TempBannerSize;
        DrawSetTransform(foot + new Vector2(1f, 0f), 0f, new Vector2(1f, 0.35f));
        DrawCircle(Vector2.Zero, s * 0.55f, new Color(0f, 0f, 0f, 0.35f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        var top = foot - new Vector2(0f, s * 1.9f);
        DrawLine(foot, top, new Color(0.18f, 0.12f, 0.07f), 2f);
        var wave = Mathf.Sin(Time.GetTicksMsec() / 260f + foot.X * 0.05f) * 1.5f;
        var flag = new[] { top, top + new Vector2(s, wave), top + new Vector2(s, s * 0.7f + wave), top + new Vector2(0f, s * 0.7f) };
        DrawColoredPolygon(flag, color);
        DrawPolyline(new[] { flag[0], flag[1], flag[2], flag[3], flag[0] }, new Color(0.1f, 0.07f, 0.04f), 1f);
        var text = TokenCount(army);
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, 11).X + 6f;
        var plate = new Rect2(foot + new Vector2(-width / 2f, 2f), new Vector2(width, 13f));
        DrawRect(plate, new Color(0.10f, 0.07f, 0.04f, 0.85f));
        DrawRect(plate, color, false, 1f);
        DrawString(font, plate.Position + new Vector2(3f, 10.5f), text, HorizontalAlignment.Left, -1, 11, new Color(0.98f, 0.94f, 0.82f));
    }

    private static Rect2 BoundsOf(Vector2[] points)
    {
        var box = new Rect2(points[0], Vector2.Zero);
        foreach (var p in points)
            box = box.Expand(p);
        return box;
    }

    /// <summary>在矩形裡畫往右下的斜線（方格世界的格在畫面上是方形）。</summary>
    private void Hatch(Rect2 box, float step, Color color, float width)
    {
        var x0 = box.Position.X;
        var y0 = box.Position.Y;
        var w = box.Size.X;
        var h = box.Size.Y;
        for (var t = -h; t < w; t += step)
        {
            // 線：(x0 + t + k, y0 + k)，k 在 [0, h]，夾在 [x0, x0 + w]。
            var k0 = Math.Max(0f, -t);
            var k1 = Math.Min(h, w - t);
            if (k1 <= k0)
                continue;
            DrawLine(new Vector2(x0 + t + k0, y0 + k0), new Vector2(x0 + t + k1, y0 + k1), color, width);
        }
    }

    /// <summary>滑鼠所在格是戰爭目標時的說明（提示框第一行；沒有回傳空字串）。</summary>
    public string WarTip(int column, int row)
        => WarFronts.Where(f => f.Target.Column == column && f.Target.Row == row)
            .Select(f => $"⚔ {f.Label}｜第 {f.Weeks} 週｜兵力 {TokenCount(f.AttackerArmy)} 對 {TokenCount(f.DefenderArmy)}｜攻方死 {f.AttackerLosses}、守方死 {f.DefenderLosses}")
            .FirstOrDefault() ?? "";
}
