using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18m 章：一條戰線（進行中的征服戰爭、獨立戰爭或內戰）。攻方從 <see cref="From"/> 出兵圍攻 <see cref="Target"/>。
/// <see cref="AttackerRealm"/>、<see cref="DefenderRealm"/>＝決定旗色的勢力（叛軍用 <see cref="RebelColor"/>）。
/// </summary>
public sealed record WarFront(int Key, string Label, DemoCellRef From, DemoCellRef Target, int TargetSettlement,
    int AttackerRealm, int DefenderRealm, long AttackerArmy, long DefenderArmy, long AttackerLosses, long DefenderLosses, int Weeks,
    Color? RebelColor = null, bool RebelsAttack = false)
{
    /// <summary>第 08-22a 章：征服戰爭的目標已被攻陷（政治地圖畫佔領斜紋）。</summary>
    public bool Occupied { get; init; }

    /// <summary>第 08-22a 章：征服戰爭的戰爭分數（−100～100；null＝不是征服戰爭，比分條照雙方死亡）。</summary>
    public int? Score { get; init; }
}

/// <summary>
/// 第 08-18m 章（試玩回饋 2026-11-11 之 8：勢力間的戰鬥表現參考十字軍之王與 Fantasy Map Simulator）：戰爭期間一直看得到——
/// ① 戰爭目標的勢力範圍罩上攻方顏色的斜線（十字軍之王的佔領斜紋）；② 從出兵地到目標的行軍路線（虛線往前流動、箭頭指向目標，FMS）；
/// ③ 雙方的軍旗棋子（旗子染勢力色、下方寫兵力，十字軍之王的軍隊棋）；④ 目標上方的交戰圖示與戰爭比分條（攻方色佔的比例＝守方死亡的比例）。
/// 軍旗與比分條是固定像素大小（任何縮放都看得清楚）。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>進行中的戰線（由觀察者介面每次刷新時給）。第 08-28 章：新的戰線記下第一次看到的時間，攻方棋子照真實時間行軍。</summary>
    public IReadOnlyList<WarFront> WarFronts
    {
        get => _warFronts;
        set
        {
            if (!ReferenceEquals(_frontWorld, World))
            {
                _frontWorld = World;
                _frontSeenMs.Clear();
                _frontsPrimed = false;
            }
            _warFronts = value;
            var now = Time.GetTicksMsec();
            foreach (var front in value)
                if (!_frontSeenMs.ContainsKey(front.Key))
                    _frontSeenMs[front.Key] = _frontsPrimed ? now : 0UL; // 讀檔或換世界時已經在打的不重播
            _frontsPrimed = true;
        }
    }

    private IReadOnlyList<WarFront> _warFronts = Array.Empty<WarFront>();
    private readonly Dictionary<int, ulong> _frontSeenMs = new();
    private object? _frontWorld;
    private bool _frontsPrimed;

    /// <summary>某條戰線的攻方行軍到了幾成（0～1；舊的戰線＝1）。</summary>
    private float MarchProgress(int key)
        => _frontSeenMs.TryGetValue(key, out var seen) && seen > 0UL ? Math.Min(1f, (Time.GetTicksMsec() - seen) / (float)TempMarchMs) : 1f;

    /// <summary>【TEMP】軍旗大小（像素）、比分條寬度、斜線間距（格半徑的倍數）。</summary>
    private const float TempBannerSize = 14f, TempScoreBarWidth = 52f;

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
            // 第 09-1e 章：行軍虛線在流動、軍旗在飄——戰線經過畫面時才持續重畫。
            var span = new Rect2(Mathf.Min(target.X, from.X), Mathf.Min(target.Y, from.Y), Mathf.Abs(target.X - from.X), Mathf.Abs(target.Y - from.Y));
            if (span.Grow(radius * 4f + 64f).Intersects(new Rect2(Vector2.Zero, Size)))
                _animatedOnScreen = true;
            // ① 佔領斜紋由政治地圖的著色器把整個省份畫成斜紋（第 08-21 章）。
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
            // ③ 軍隊棋子（第 08-28 章，仿 CK3）：攻方在路線上靠近目標處圍攻（剛開戰時從出兵地行軍過去），守方守在目標。
            var siege = length > radius ? target - line / length * Math.Min(length * 0.5f, Math.Max(radius * 0.9f, 22f)) : target + new Vector2(-24f, 0f);
            var march = MarchProgress(front.Key);
            if (march < 1f)
                _animatedOnScreen = true;
            // 第 08-30 章：征服戰爭的攻方到了就架起攻城器械（圍城棋子）。
            if (march >= 1f && front.Score is not null)
                DrawArmyToken(font, siege + new Vector2(18f, 3f), attackerColor, 0, TokenKind.Siege);
            DrawArmyToken(font, march < 1f && length > radius ? from.Lerp(siege, Ease(march)) : siege, attackerColor, front.AttackerArmy, TokenKind.Army);
            DrawArmyToken(font, target + new Vector2(length > radius ? 24f : 30f, 6f), defenderColor, front.DefenderArmy, TokenKind.Army, 0.85f);
            // ④ 交戰圖示與比分條（攻方色＝守方死亡的比例）。
            var bar = target - new Vector2(TempScoreBarWidth / 2f, TempBannerSize * 2.6f);
            DrawCircle(bar + new Vector2(TempScoreBarWidth / 2f, -9f), 9f, new Color(0.08f, 0.05f, 0.03f, 0.85f));
            var swords = font.GetStringSize("⚔", HorizontalAlignment.Left, -1, 14).X;
            DrawString(font, bar + new Vector2(TempScoreBarWidth / 2f - swords / 2f, -4f), "⚔", HorizontalAlignment.Left, -1, 14,
                new Color(1f, 0.9f, 0.6f, 0.75f + 0.25f * Mathf.Sin(seconds * 4f)));
            var total = front.AttackerLosses + front.DefenderLosses;
            // 第 08-22a 章：征服戰爭的比分條是戰爭分數（中間＝0，攻方色往右＝攻方佔上風）。
            var share = front.Score is { } warScore ? 0.5f + warScore / 200f : total <= 0 ? 0.5f : front.DefenderLosses / (float)total;
            DrawRect(new Rect2(bar - new Vector2(1f, 1f), new Vector2(TempScoreBarWidth + 2f, 7f)), new Color(0.08f, 0.05f, 0.03f, 0.9f));
            DrawRect(new Rect2(bar, new Vector2(TempScoreBarWidth * share, 5f)), attackerColor);
            DrawRect(new Rect2(bar + new Vector2(TempScoreBarWidth * share, 0f), new Vector2(TempScoreBarWidth * (1f - share), 5f)), defenderColor);
            DrawLine(bar + new Vector2(TempScoreBarWidth / 2f, -1f), bar + new Vector2(TempScoreBarWidth / 2f, 6f), new Color(1f, 1f, 1f, 0.7f), 1f);
        }
    }

    /// <summary>滑鼠所在格是戰爭目標時的說明（提示框第一行；沒有回傳空字串）。</summary>
    public string WarTip(int column, int row)
        => WarFronts.Where(f => f.Target.Column == column && f.Target.Row == row)
            .Select(f => $"⚔ {f.Label}｜第 {f.Weeks} 週" + (f.Score is { } score ? $"｜{(f.Occupied ? "已攻陷" : "圍城中")}｜戰爭分數 {score}" : "") // 第 08-22a 章
                + $"｜兵力 {TokenCount(f.AttackerArmy)} 對 {TokenCount(f.DefenderArmy)}｜攻方死 {f.AttackerLosses}、守方死 {f.DefenderLosses}")
            .FirstOrDefault() ?? "";
}
