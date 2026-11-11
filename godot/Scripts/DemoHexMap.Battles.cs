using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>地圖上的一場戰鬥（事件編號、種類、週、交戰地點、出發地、死亡人數）。</summary>
public sealed record BattleMark(long EventId, DemoSettlementEventKind Kind, int Week, DemoCellRef Place, DemoCellRef? From, long Deaths, int Seed, string Label = "");

/// <summary>
/// 第 08-18h 章（試玩回饋 2026-11-08 第四次之 4：外在的戰鬥事件應該要顯現出來）：怪物襲擊、出兵討伐、脫離抗爭的交戰畫在地圖上。
/// 第一次看到時照真實時間播 <see cref="TempBattleMs"/> 毫秒（快轉也看得到）：攻方從出發地走到交戰地點，接著交戰的閃光、刀光，
/// 飄出「⚔ -N」；遠看（區域、戰略層）只畫一閃一閃的 ⚔ 標記。本週與上週的戰鬥在交戰地點留一個小 ⚔。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】一場戰鬥的動畫長度（毫秒）；前段是行軍（有出發地時），後段是交戰。</summary>
    public const ulong TempBattleMs = 4000;
    private const float TempMarchShare = 0.55f;

    /// <summary>【TEMP】格半徑小於這個值時只畫 ⚔ 標記（看不清楚小人）。</summary>
    public const float TempBattleSpriteMinRadius = 14f;

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

    private static readonly Color RaidRed = new(0.78f, 0.12f, 0.10f);
    private static readonly Color SoldierTunic = new(0.55f, 0.16f, 0.12f);
    private static readonly Color DefenderTunic = new(0.20f, 0.32f, 0.58f);

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
            if (radius < TempBattleSpriteMinRadius)
            {
                // 遠看：一閃一閃的 ⚔，交戰時放大。
                var pulse = 0.6f + 0.4f * Mathf.Sin(t * Mathf.Tau * 4f);
                DrawSwords(font, place - new Vector2(0f, 6f), (int)(16 + 8 * pulse), mark.Kind, 1f);
                continue;
            }
            var from = mark.From is { } f && World.Contains(f.Column, f.Row) ? ScreenOf(start, radius, f) : place;
            var marching = mark.From is not null && from.DistanceTo(place) > radius * 0.5f;
            var clash = marching ? Mathf.Clamp((t - TempMarchShare) / (1f - TempMarchShare), 0f, 1f) : t;
            var arrive = marching ? Mathf.Clamp(t / TempMarchShare, 0f, 1f) : 1f;
            var heading = place - from;
            var facing = heading.X >= 0f ? 1f : -1f;
            // 攻方的小隊：沿路走到交戰地點旁（停在地點前半格）。
            var stop = marching ? place - heading.Normalized() * radius * 0.35f : place - new Vector2(radius * 0.45f, 0f);
            var lead = from.Lerp(stop, Mathf.SmoothStep(0f, 1f, arrive));
            var step = (int)(now / 150UL) % 4;
            var attackers = Math.Clamp(1 + (int)(mark.Deaths / 3), 2, 5);
            var side = new Vector2(-heading.Y, heading.X).Normalized() * radius * 0.18f;
            if (side == Vector2.Zero)
                side = new Vector2(0f, radius * 0.18f);
            for (var k = 0; k < attackers; k++)
            {
                var offset = side * (k - (attackers - 1) / 2f) - heading.Normalized() * radius * 0.12f * (k % 2);
                var jitter = clash > 0f ? new Vector2(Mathf.Sin(now / 90f + k) * radius * 0.05f, 0f) : Vector2.Zero;
                var feet = lead + offset + jitter;
                if (mark.Kind == DemoSettlementEventKind.MonsterRaid && PixelParts.Texture(MonsterArt(mark.Seed + k * 3)) is { } monster)
                    DrawArt(monster, feet, radius, 4, step, facing < 0f);
                else
                    DrawWalker(feet, radius, mark.Kind == DemoSettlementEventKind.MonsterRaid ? RaidRed : SoldierTunic, step % 2, facing, null,
                        mark.Kind == DemoSettlementEventKind.MonsterHunt ? "unit_hunter" : "unit_man", step);
            }
            if (clash <= 0f)
                continue;
            // 守方：討伐時是怪物、其餘是村民（站在交戰地點另一側）。
            for (var k = 0; k < 2; k++)
            {
                var feet = place + heading.Normalized() * radius * 0.15f + side * (k - 0.5f) * 1.4f;
                if (mark.Kind == DemoSettlementEventKind.MonsterHunt && PixelParts.Texture(MonsterArt(mark.Seed + k * 3)) is { } monster)
                    DrawArt(monster, feet, radius, 4, step, facing > 0f);
                else
                    DrawWalker(feet, radius, DefenderTunic, step % 2, -facing, null, "unit_man", step);
            }
            // 交戰的閃光與刀光（4 幀），中段最亮。
            var hit = place - new Vector2(0f, radius * 0.1f);
            var alpha = Mathf.Clamp(Mathf.Sin(clash * Mathf.Pi) * 1.4f, 0f, 1f);
            if (PixelParts.Texture("fx_impact") is { } impact)
                DrawArt(impact, hit + new Vector2(0f, radius * 0.4f), radius, 4, (int)(clash * 12f) % 4, alpha: alpha * 0.9f, scale: 0.6f);
            if (PixelParts.Texture("fx_beast_attack") is { } slash)
                DrawArt(slash, hit, radius, 4, (int)(clash * 16f) % 4, flip: facing < 0f, alpha: alpha);
            else
                DrawCircle(hit, radius * 0.3f * alpha, new Color(1f, 0.9f, 0.5f, 0.6f * alpha));
            // 飄字：⚔ -N（往上飄、漸淡）。
            if (mark.Deaths > 0)
            {
                var text = $"⚔ -{mark.Deaths}";
                var rise = clash * radius * 0.8f;
                var at = place - new Vector2(font.GetStringSize(text, HorizontalAlignment.Left, -1, 15).X / 2f, radius * 0.9f + rise);
                var fade = 1f - Mathf.Max(0f, clash - 0.6f) / 0.4f;
                DrawString(font, at + new Vector2(1f, 1f), text, HorizontalAlignment.Left, -1, 15, new Color(0f, 0f, 0f, 0.7f * fade));
                DrawString(font, at, text, HorizontalAlignment.Left, -1, 15, new Color(1f, 0.25f, 0.18f, fade));
            }
        }
    }

    /// <summary>第 08-18i 章：滑鼠所在格最近的戰鬥說明（提示框第一行；沒有回傳空字串）。</summary>
    public string BattleTip(int column, int row)
        => _battleMarks.Where(m => m.Week >= CurrentWeek - 1 && m.Label.Length > 0 && Math.Max(Math.Abs(m.Place.Column - column), Math.Abs(m.Place.Row - row)) <= 0)
            .OrderByDescending(m => m.EventId).Select(m => $"⚔ {m.Label}（死 {m.Deaths} 人）").FirstOrDefault() ?? "";

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
        var width = font.GetStringSize("⚔", HorizontalAlignment.Left, -1, size).X;
        DrawString(font, center + new Vector2(-width / 2f, size * 0.38f), "⚔", HorizontalAlignment.Left, -1, size, color);
    }
}
