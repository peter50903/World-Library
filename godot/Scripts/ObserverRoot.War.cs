using Godot;
using System.Globalization;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-47 章（戰爭表現三章之三，參考 CK3 的戰爭視窗）：戰爭總覽——每場進行中的征服戰爭一塊：雙方與兵力、宣戰理由與目標、戰況、
/// 戰爭分數條與分項、會戰清單（野戰、圍城的地點、雙方死傷與勝負）、攻陷與佔領；下面是最近結束的戰爭（史書的那一段）。
/// 從功能列的「戰爭」、宣戰／攻陷／野戰的消息打開。只讀，不改世界。
/// </summary>
public partial class ObserverRoot
{
    private readonly VBoxContainer _warPage = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill };
    private int? _warWeek;

    /// <summary>【TEMP】戰爭視窗：最大寬高、每場最多列幾場會戰、列幾場最近結束的戰爭。</summary>
    private const float TempWarWindowWidth = 780f, TempWarWindowHeight = 640f;
    private const int TempWarBattleRows = 10, TempWarRecentEnded = 3;

    private void BuildWarPage() => AddModalPage("war", "戰爭", _warPage);

    /// <summary>視窗是羊皮紙底：標題用深紅褐（金色字在淺底上看不見）。</summary>
    private static readonly Color TempWarInk = new(0.42f, 0.14f, 0.08f);

    /// <summary>打開戰爭總覽（已經開著就只更新）。</summary>
    private void OpenWar()
    {
        if (_modalOpen != "war")
            ToggleModal("war");
        else
            RefreshWarWindow();
    }

    /// <summary>由 <see cref="Refresh"/> 呼叫：視窗開著、又過了一週就更新。</summary>
    private void RefreshWarIfOpen()
    {
        if (_modalOpen == "war" && _world.LatestWeek?.Week != _warWeek && !HoldingBusy())
            RefreshWarWindow();
    }

    private static string WarYear(int week) => DemoSettlementHistory.YearOfDay(Math.Max(1, week * 7)).ToString(CultureInfo.InvariantCulture);

    private Label WarLabel(string text, int size = 15, Color? color = null)
    {
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        label.AddThemeFontSizeOverride("font_size", size);
        if (color is { } c)
            label.AddThemeColorOverride("font_color", c);
        return label;
    }

    private void RefreshWarWindow()
    {
        _warWeek = _world.LatestWeek?.Week;
        foreach (var child in _warPage.GetChildren())
            child.QueueFree();
        var week = _world.CommittedWeeks;
        var reports = DemoWarReports.All(_world.SettlementEvents(), _world.Sites);
        var ongoing = reports.Where(r => r.EndWeek == 0).GroupBy(r => r.AttackerId).ToDictionary(g => g.Key, g => g.Last());
        var wars = _wars.OrderBy(w => w.StartWeek).ThenBy(w => w.AttackerId).ToList();
        _modalTitle.Text = wars.Count == 0 ? "戰爭" : $"戰爭（進行中 {wars.Count} 場）";
        if (wars.Count == 0)
            _warPage.AddChild(WarLabel("現在沒有進行中的征服戰爭。", 16));
        foreach (var w in wars)
            AddWarBlock(_warPage, w, ongoing.GetValueOrDefault(w.AttackerId), week);
        var ended = reports.Where(r => r.EndWeek > 0).TakeLast(TempWarRecentEnded).Reverse().ToList();
        if (ended.Count > 0)
        {
            _warPage.AddChild(new HSeparator());
            _warPage.AddChild(WarLabel("最近結束的戰爭", 18, TempWarInk));
            foreach (var r in ended)
                _warPage.AddChild(WarLabel($"第 {WarYear(r.StartWeek)}～{WarYear(r.EndWeek)} 年｜{r.EndText}。"
                    + DemoWarReports.Story(r, SiteName), 14));
        }
    }

    private void AddWarBlock(Container parent, DemoWar w, DemoWarReport? report, int week)
    {
        var front = _hexMap.WarFronts.FirstOrDefault(f => f.Key == w.AttackerId);
        var attackerColor = _hexMap.PoliticalRealmColor(_hexMap.Territory.RealmOf(w.AttackerId));
        var defenderColor = _hexMap.PoliticalRealmColor(_hexMap.Territory.RealmOf(w.TargetId));
        var block = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        block.AddThemeConstantOverride("separation", 4);
        parent.AddChild(block);
        block.AddChild(WarLabel($"⚔ {SiteName(w.AttackerId)} 對 {SiteName(w.DefenderId)}：征服戰爭", 19, TempWarInk));
        block.AddChild(WarLabel($"宣戰：第 {WarYear(w.StartWeek)} 年（已打 {Math.Max(0, week - w.StartWeek)} 週）｜目標：{(w.WholeRealm ? $"整個{SiteName(w.DefenderId)}" : SiteName(w.TargetId))}"
            + (w.Cause.Length > 0 ? $"｜{w.Cause}" : "")));
        if (front is not null)
            block.AddChild(WarLabel($"兵力：攻方 {front.AttackerArmy}、守方 {front.DefenderArmy}｜死傷：攻方 {w.AttackerLosses}、守方 {w.DefenderLosses}"));
        var status = _world.WarStatus(w);
        block.AddChild(WarLabel("戰況：" + (status.Stage switch
        {
            DemoWarStage.Marching => $"大軍行軍中，還要 {status.MarchLeft} 週到{SiteName(w.TargetId)}城下",
            DemoWarStage.Besieging => $"圍攻{SiteName(status.Besieged)}（{status.SiegeDone}/{status.SiegeNeed} 週）" + (w.Holdings.Count > 0 ? $"，已再佔 {w.Holdings.Count} 座" : ""),
            _ => $"已攻陷{SiteName(w.TargetId)}，佔領 {1 + w.Holdings.Count} 座，等待議和"
        })));
        // 戰爭分數條（中間＝0，攻方色往右＝攻方佔上風）與分項。
        var score = DemoRealms.WarScore(w, week);
        var bar = new HBoxContainer { CustomMinimumSize = new Vector2(0, 14), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        bar.AddThemeConstantOverride("separation", 0);
        var share = Math.Clamp(0.5f + score / 200f, 0.02f, 0.98f);
        bar.AddChild(new ColorRect { Color = attackerColor, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = share });
        bar.AddChild(new ColorRect { Color = new Color(1f, 1f, 1f, 0.8f), CustomMinimumSize = new Vector2(2, 0) });
        bar.AddChild(new ColorRect { Color = defenderColor, SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 1f - share });
        block.AddChild(bar);
        var (battle, occupy, time, holdings) = DemoRealms.WarScoreParts(w, week);
        static string Signed(int v) => v > 0 ? $"+{v}" : v.ToString(CultureInfo.InvariantCulture);
        block.AddChild(WarLabel($"戰爭分數 {Signed(score)}＝會戰 {Signed(battle)}、攻陷目標 {Signed(occupy)}、佔領時間 {Signed(time)}、又佔 {w.Holdings.Count} 座 {Signed(holdings)}"
            + $"（滿 {DemoRealms.TempWinScore} 征服；打滿兩年 {DemoRealms.TempTermsScore} 以上議和割讓，{DemoRealms.TempLoseScore} 以下敗退）", 14));
        if (report is not null && report.Battles.Count > 0)
        {
            // 野戰一場一行；圍城每週的交戰依被圍的聚落合成一行（圍了幾週、雙方死傷）。
            block.AddChild(WarLabel("會戰與圍城：", 15, TempWarInk));
            foreach (var b in report.Battles.Where(b => b.Field).Reverse().Take(TempWarBattleRows))
                block.AddChild(WarLabel($"　第 {WarYear(b.Week)} 年｜野戰｜{b.Place}｜攻方死 {b.AttackerDeaths}、守方死 {b.DefenderDeaths}｜"
                    + (b.AttackerWon ? "攻方得勝" : b.AttackerDeaths == b.DefenderDeaths ? "不分勝負" : "守方得勝"), 14, new Color(0.50f, 0.16f, 0.08f)));
            foreach (var g in report.Battles.Where(b => !b.Field).GroupBy(b => b.Besieged == 0 ? w.TargetId : b.Besieged))
                block.AddChild(WarLabel($"　第 {WarYear(g.Min(b => b.Week))}～{WarYear(g.Max(b => b.Week))} 年｜圍城｜{SiteName(g.Key)}｜{g.Count()} 週交戰"
                    + $"｜攻方死 {g.Sum(b => b.AttackerDeaths)}、守方死 {g.Sum(b => b.DefenderDeaths)}", 14));
        }
        if (report is not null && report.Occupations.Count > 0)
            block.AddChild(WarLabel("攻陷與佔領：" + string.Join("、", report.Occupations.Select(o => $"第 {WarYear(o.Week)} 年{SiteName(o.SettlementId)}")), 14));
        var look = new Button { Text = "在地圖上看戰況", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        var at = status.ArmyAt;
        look.Pressed += () =>
        {
            CloseModal();
            if (_world.Map.Contains(at.Column, at.Row))
                _hexMap.FocusCell(_world.Map.GetCell(at.Column, at.Row));
        };
        block.AddChild(look);
        block.AddChild(new HSeparator());
    }

    /// <summary>
    /// 第 08-52 章（試玩回饋 2026-11-26 之 2：看不到戰況，點選時只會出現勢力資訊）：這個聚落的勢力正在打的征服戰爭
    /// （攻方、守方或目標是它），每場一塊，跟戰爭總覽同一套內容；沒有戰爭＝null。
    /// </summary>
    private Control? WarSection(int settlementId)
    {
        if (_wars.Count == 0)
            return null;
        var top = _hexMap.Territory.RealmOf(settlementId);
        var mine = _wars.Where(w => w.TargetId == settlementId || w.AttackerId == settlementId || w.DefenderId == settlementId
            || _hexMap.Territory.RealmOf(w.AttackerId) == top || _hexMap.Territory.RealmOf(w.DefenderId) == top).ToList();
        if (mine.Count == 0)
            return null;
        var reports = DemoWarReports.All(_world.SettlementEvents(), _world.Sites).Where(r => r.EndWeek == 0)
            .GroupBy(r => r.AttackerId).ToDictionary(g => g.Key, g => g.Last());
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var w in mine)
            AddWarBlock(box, w, reports.GetValueOrDefault(w.AttackerId), _world.CommittedWeeks);
        return box;
    }

    /// <summary>第 08-52 章：在地圖上點到軍隊、圍城、比分條時，左邊的詳情直接換成這場戰爭的戰況（不另開視窗）。</summary>
    private void ShowWarDetail(int key)
    {
        if (key < 0)
        {
            // 獨立戰爭、內戰：開叛離的聚落（現況寫著戰爭）。
            if (_world.Sites.Settlements.FirstOrDefault(s => s.Id == -key) is { } rebel)
                ShowDetailAt(rebel.Column, rebel.Row);
            return;
        }
        if (_wars.FirstOrDefault(w => w.AttackerId == key) is not { } war
            || _world.Sites.Settlements.FirstOrDefault(s => s.Id == war.TargetId) is not { } target)
        {
            OpenWar();
            return;
        }
        ShowDetailAt(target.Column, target.Row);
        SelectDetailTab("戰況");
    }

    /// <summary>第 08-47 章冒煙測試：打開戰爭視窗、有內容、關掉；世界不變（由呼叫端檢查）。</summary>
    private void VerifyWarSmoke(Action<string> fail)
    {
        OpenWar();
        if (_modalOpen != "war")
            fail("戰爭視窗打不開。");
        RefreshWarWindow();
        if (_warPage.GetChildCount() == 0)
            fail("戰爭視窗是空的。");
        CloseModal();
    }
}
