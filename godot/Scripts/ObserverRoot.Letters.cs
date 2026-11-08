using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18i 章（GPT 試玩回饋 08：重大事件被日常紀錄夾住；參考 RimWorld 的事件信件）：
/// 播放中發生重大事件時，地圖右上角跳出一張小通知（圖示＋一句話），點一下定位並打開那個聚落，右鍵關掉；
/// 每張停留 <see cref="TempLetterSeconds"/> 秒（真實時間）後自動消失，最多疊 <see cref="TempMaxLetters"/> 張（新的在上）。
/// 讀檔、換世界時已經發生的事件不跳。不自動暫停（自動暫停仍在開發資訊）。只改介面。
/// </summary>
public partial class ObserverRoot
{
    /// <summary>【TEMP】通知停留秒數、最多幾張、寬度。</summary>
    private const double TempLetterSeconds = 25;
    private const int TempMaxLetters = 5;
    private const float TempLetterWidth = 300f;

    private VBoxContainer? _letters;
    private long _letterWatermark;
    private readonly List<(Button Button, double Born)> _letterCards = new();
    private readonly System.Diagnostics.Stopwatch _letterClock = System.Diagnostics.Stopwatch.StartNew();

    private void BuildLetters()
    {
        _letters = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        _letters.AddThemeConstantOverride("separation", 4);
        _hud.AddChild(_letters);
        ResetLetters();
    }

    /// <summary>換世界：清掉通知，已有的事件都算看過。</summary>
    private void ResetLetters()
    {
        _letterWatermark = _world.EventHistory.LastEventId;
        foreach (var (button, _) in _letterCards)
            button.QueueFree();
        _letterCards.Clear();
    }

    /// <summary>由 <see cref="ApplyHudLayout"/> 呼叫：放在右側面板左邊（面板收起時貼右邊），頂部列下面。</summary>
    private void PlaceLetters(HudFrame frame)
    {
        if (_letters is null)
            return;
        var right = _rightView != RightView.None ? frame.Console.X - 8 : (int)_hud.Size.X - 8;
        _letters.Position = new Vector2(right - TempLetterWidth, frame.TopBar.Y + frame.TopBar.Height + 8);
        _letters.Size = new Vector2(TempLetterWidth, 10);
    }

    /// <summary>這筆事件要不要跳通知；要的話回傳（圖示、一句話、是不是壞消息）。</summary>
    private static (string Icon, string Text, bool Bad)? LetterOf(DemoSettlementEvent e) => e.Kind switch
    {
        DemoSettlementEventKind.SettlementRuined => ("☠", $"{e.SettlementName}成為遺跡", true),
        DemoSettlementEventKind.Earthquake or DemoSettlementEventKind.Tornado or DemoSettlementEventKind.Meteorite or DemoSettlementEventKind.Flood
            => ("⚡", $"{DemoDisasters.Name(e.Kind)}襲擊{e.SettlementName}" + (e.Count > 0 ? $"，{e.Count} 人罹難" : ""), true),
        DemoSettlementEventKind.MonsterRaid when e.Count >= TempLetterRaidDeaths => ("⚔", $"怪物襲擊{e.SettlementName}，{e.Count} 人死亡", true),
        DemoSettlementEventKind.DungeonAppeared => ("⚔", $"{e.SettlementName}附近出現了地下城", true),
        DemoSettlementEventKind.SecessionArmed => ("⚔", DemoPlayerDetail.Clean(e.Cause), true),
        DemoSettlementEventKind.SecessionPeaceful => ("✊", DemoPlayerDetail.Clean(e.Cause), false),
        DemoSettlementEventKind.Independence when !e.Cause.StartsWith("隨", StringComparison.Ordinal) => ("★", DemoPlayerDetail.Clean(e.Cause), false),
        DemoSettlementEventKind.EraBegan => ("★", $"世界進入{DemoKnowledge.EraName((DemoEra)e.Count)}", false),
        DemoSettlementEventKind.CultureSplit or DemoSettlementEventKind.FaithFounded => ("✦", $"{e.SettlementName}{DemoPlayerDetail.Clean(e.Cause)}", false),
        DemoSettlementEventKind.StarvationDeath when e.Count >= TempLetterStarved => ("☠", $"{e.SettlementName}有 {e.Count} 人餓死", true),
        _ => null
    };

    /// <summary>【TEMP】怪物襲擊死幾人以上、一週餓死幾人以上才跳通知（太小的事不打擾）。</summary>
    private const long TempLetterRaidDeaths = 3, TempLetterStarved = 5;

    /// <summary>每次刷新：新事件裡值得通知的跳出來；過期的收掉。</summary>
    private void RefreshLetters()
    {
        if (_letters is null)
            return;
        var now = _letterClock.Elapsed.TotalSeconds;
        foreach (var expired in _letterCards.Where(x => now - x.Born > TempLetterSeconds).ToList())
            DropLetter(expired.Button);
        var latest = _world.EventHistory.LastEventId;
        if (latest <= _letterWatermark)
            return;
        var fresh = _world.WeekReports().Where(w => w.Week >= _world.CommittedWeeks - 2).SelectMany(w => w.Events)
            .Where(e => e.EventId > _letterWatermark).OrderBy(e => e.EventId).ToList();
        _letterWatermark = latest;
        foreach (var e in fresh)
            if (LetterOf(e) is { } letter)
                AddLetter(e, letter.Icon, letter.Text, letter.Bad, now);
    }

    private void AddLetter(DemoSettlementEvent e, string icon, string text, bool bad, double now)
    {
        var button = new Button
        {
            Text = $"{icon} {text}",
            Alignment = HorizontalAlignment.Left,
            ClipText = true,
            CustomMinimumSize = new Vector2(TempLetterWidth, 30),
            FocusMode = FocusModeEnum.None,
            TooltipText = $"第 {DemoSettlementHistory.YearOfDay(Math.Max(1, (int)e.WorldTime.TotalDays))} 年：{DemoPlayerDetail.Clean(e.Cause)}\n左鍵：定位並打開　右鍵：關掉"
        };
        var tint = bad ? new Color(0.45f, 0.10f, 0.07f, 0.92f) : new Color(0.30f, 0.22f, 0.08f, 0.92f);
        button.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = tint, BorderColor = TempGold, BorderWidthLeft = 3, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 8, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 4
        });
        button.AddThemeStyleboxOverride("hover", new StyleBoxFlat
        {
            BgColor = tint.Lightened(0.15f), BorderColor = TempGold, BorderWidthLeft = 3, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 8, ContentMarginRight = 6, ContentMarginTop = 4, ContentMarginBottom = 4
        });
        button.AddThemeColorOverride("font_color", new Color(1f, 0.95f, 0.85f));
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        var settlementId = e.SettlementId;
        button.GuiInput += input =>
        {
            if (input is not InputEventMouseButton { Pressed: true } click)
                return;
            if (click.ButtonIndex == MouseButton.Left && _world.Sites.Settlements.FirstOrDefault(s => s.Id == settlementId) is { } site)
            {
                _hexMap.FocusCell(_world.Map.GetCell(site.Column, site.Row));
                ShowDetailAt(site.Column, site.Row);
            }
            if (click.ButtonIndex is MouseButton.Left or MouseButton.Right)
                DropLetter(button);
        };
        _letters!.AddChild(button);
        _letters.MoveChild(button, 0); // 新的在上
        _letterCards.Add((button, now));
        while (_letterCards.Count > TempMaxLetters)
            DropLetter(_letterCards.OrderBy(x => x.Born).First().Button);
    }

    private void DropLetter(Button button)
    {
        _letterCards.RemoveAll(x => x.Button == button);
        button.QueueFree();
    }
}
