using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>地圖上的一場戰鬥（事件編號、種類、週、交戰地點、出發地、死亡人數）。</summary>
public sealed record BattleMark(long EventId, DemoSettlementEventKind Kind, int Week, DemoCellRef Place, DemoCellRef? From, long Deaths, int Seed, string Label = "",
    long Strength = 0); // 第 08-28 章：棋子上的數量（怪物數、出征的士兵數；0＝不寫）

/// <summary>
/// 第 08-18h 章（試玩回饋 2026-11-08 第四次之 4：外在的戰鬥事件應該要顯現出來）：怪物襲擊、出兵討伐、脫離抗爭的交戰畫在地圖上。
/// 第一次看到時照真實時間播 <see cref="TempBattleMs"/> 毫秒（快轉也看得到）：紅色箭頭從出兵地指向戰場、一閃一閃的 ⚔ 與死亡人數。
/// 本週與上週的戰鬥在交戰地點留一個小 ⚔。第 09-2 章移除小人交戰動畫（第 08-18j 章）。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】一場戰鬥的動畫長度（毫秒）。</summary>
    public const ulong TempBattleMs = 4000;

    private IReadOnlyList<BattleMark> _battleMarks = Array.Empty<BattleMark>();
    private readonly HashSet<long> _seenBattles = new();
    private bool _battleMarksPrimed;
    private object? _battleWorld;
    private readonly List<(BattleMark Mark, ulong StartMs)> _battles = new();

    /// <summary>有戰鬥動畫在播（要持續重畫）。</summary>
    private bool _battleAnimating => _battles.Count > 0;

    /// <summary>近兩週的戰鬥（由觀察者介面每次刷新時給；要在 <see cref="CurrentWeek"/> 之後設定）。讀檔看到的舊戰鬥不播。</summary>
    public IReadOnlyList<BattleMark> BattleMarks
    {
        get => _battleMarks;
        set
        {
            if (!ReferenceEquals(_battleWorld, World))
            {
                _battleWorld = World;
                _seenBattles.Clear();
                _battles.Clear();
                _battleMarksPrimed = false;
            }
            _battleMarks = value;
            var now = Time.GetTicksMsec();
            var sounded = false;
            foreach (var mark in value)
            {
                if (!_seenBattles.Add(mark.EventId) || !_battleMarksPrimed || mark.Week < CurrentWeek - 1)
                    continue;
                // 同時太多場就只播最近的幾場（每場錯開一點開始）。
                if (_battles.Count >= TempMaxBattles)
                    continue;
                _battles.Add((mark, now + (ulong)(_battles.Count % 4) * 250UL));
                if (!sounded)
                {
                    PlaySfx(ArtAssets.EventSfx(mark.Kind));
                    sounded = true;
                }
            }
            _battleMarksPrimed = true;
        }
    }

    /// <summary>【TEMP】同時最多播幾場戰鬥。</summary>
    private const int TempMaxBattles = 8;

    /// <summary>戰鬥的動畫與標記（畫在地形、聚落與小人上面）。</summary>
    private void DrawBattles(Vector2 start, float radius)
    {
        if (World is null)
            return;
        var now = Time.GetTicksMsec();
        _battles.RemoveAll(x => now > x.StartMs && now - x.StartMs > TempBattleMs);
        var font = ThemeDB.FallbackFont;
        var playing = _battles.Select(x => x.Mark.EventId).ToHashSet();
        // 近兩週戰鬥的小標記（動畫播完後留著，看得出哪裡在打仗）。
        foreach (var mark in _battleMarks.Where(m => m.Week >= CurrentWeek - 1 && !playing.Contains(m.EventId)))
        {
            if (!World.Contains(mark.Place.Column, mark.Place.Row))
                continue;
            var at = ScreenOf(start, radius, mark.Place) - new Vector2(0f, radius * 0.9f);
            if (!OnScreen(at, radius))
                continue;
            DrawSwords(font, at, 13, mark.Kind, 0.85f);
        }
        foreach (var (mark, startMs) in _battles)
        {
            if (now < startMs || !World.Contains(mark.Place.Column, mark.Place.Row))
                continue;
            var t = (now - startMs) / (float)TempBattleMs;
            var place = ScreenOf(start, radius, mark.Place);
            if (!OnScreen(place, radius * 4f))
                continue;
            // 第 08-20f 章：只畫符號——紅色箭頭從出兵的地方指向戰場，戰場上一閃一閃的 ⚔ 與死亡人數（第 09-2 章移除小人交戰動畫）。
            var origin = mark.From is { } src && World.Contains(src.Column, src.Row) ? ScreenOf(start, radius, src) : (Vector2?)null;
            if (origin is { } o && o.DistanceTo(place) > radius * 0.5f)
            {
                DrawWarArrow(o, place, radius, Math.Min(1f, t * 2.5f));
                // 第 08-28 章：怪物（襲擊、獸潮）或士兵（討伐、戰爭）的棋子沿著箭頭走到戰場，到了就停在戰場旁。
                var kind = mark.Kind switch
                {
                    DemoSettlementEventKind.MonsterSwarm => TokenKind.Swarm,
                    DemoSettlementEventKind.MonsterRaid => TokenKind.Monster,
                    _ => TokenKind.Army
                };
                DrawArmyToken(font, o.Lerp(place, Ease(Math.Min(1f, t * 1.8f))) + new Vector2(radius * 0.35f, radius * 0.25f),
                    TempTroopColor, mark.Strength, kind, kind == TokenKind.Swarm ? 1.2f : 1f);
            }
            var beat = 0.6f + 0.4f * Mathf.Sin(t * Mathf.Tau * 4f);
            DrawSwords(font, place - new Vector2(0f, radius * 0.6f), (int)(16 + 8 * beat), mark.Kind, 1f);
            if (mark.Deaths > 0)
                DrawString(font, place + new Vector2(radius * 0.3f, -radius * 0.2f), $"-{mark.Deaths}", HorizontalAlignment.Left, -1, 15, new Color(1f, 0.3f, 0.2f));
        }
    }

    /// <summary>⚔ 標記（深色底圓＋字）；怪物相關是紅色，脫離抗爭是金色，征服戰爭是鋼藍色（第 08-19e 章）。</summary>
    private void DrawSwords(Font font, Vector2 center, int size, DemoSettlementEventKind kind, float alpha)
    {
        var color = kind switch
        {
            DemoSettlementEventKind.SecessionBattle => new Color(1f, 0.82f, 0.30f, alpha),
            DemoSettlementEventKind.ConquestBattle => new Color(0.55f, 0.78f, 1f, alpha),
            _ => new Color(1f, 0.35f, 0.25f, alpha)
        };
        DrawCircle(center, size * 0.75f, new Color(0.08f, 0.05f, 0.03f, 0.75f * alpha));
        // 第 09-1b 章：第十一批交叉雙劍（底圓的顏色照舊分戰爭種類），缺素材才用字。
        if (DrawSymbol("b11_sym_battle", center, size * 1.3f, new Color(1f, 1f, 1f, alpha)))
        {
            DrawArc(center, size * 0.75f, 0f, Mathf.Tau, 24, color, Math.Max(1.5f, size * 0.08f));
            return;
        }
        var width = font.GetStringSize("⚔", HorizontalAlignment.Left, -1, size).X;
        DrawString(font, center + new Vector2(-width / 2f, size * 0.38f), "⚔", HorizontalAlignment.Left, -1, size, color);
    }

    /// <summary>第 08-20f 章：戰爭箭頭（紅色粗線＋箭頭，<paramref name="grow"/>＝畫到幾成，動畫一開始從出發點長出去）。</summary>
    private void DrawWarArrow(Vector2 from, Vector2 to, float radius, float grow)
    {
        var dir = (to - from).Normalized();
        var tip = from + (to - from) * grow - dir * radius * 0.35f;
        var width = Math.Max(3f, radius * 0.12f);
        var red = new Color(0.82f, 0.10f, 0.08f, 0.9f);
        DrawLine(from, tip, new Color(0f, 0f, 0f, 0.5f), width + 3f);
        DrawLine(from, tip, red, width);
        var side = new Vector2(-dir.Y, dir.X) * width * 2f;
        if (DrawArrowHead(tip + dir * width * 3f, dir, Math.Max(radius * 0.6f, 20f), red.A)) // 第 09-1b 章：旗箭箭頭
            return;
        var head = new[] { tip + dir * width * 3f, tip + side, tip - side };
        DrawColoredPolygon(head, red);
        DrawPolyline(new[] { head[0], head[1], head[2], head[0] }, new Color(0f, 0f, 0f, 0.6f), 1.5f);
    }

    /// <summary>第 08-18i 章：滑鼠所在格最近的戰鬥說明（提示框第一行；沒有回傳空字串）。</summary>
    public string BattleTip(int column, int row)
        => _battleMarks.Where(m => m.Week >= CurrentWeek - 1 && m.Label.Length > 0 && m.Place.Column == column && m.Place.Row == row)
            .OrderByDescending(m => m.EventId).Select(m => $"⚔ {m.Label}（死 {m.Deaths} 人）").FirstOrDefault() ?? "";
}
