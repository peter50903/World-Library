using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18i 章（GPT 試玩回饋 11：把一次點選轉成持續關心；參考 WorldBox 的最愛）：聚落詳情與人物頁的標題旁有「☆ 追蹤」，
/// 追蹤的對象列在右側聚落清單最上面（聚落寫人口，人物寫職位與所屬聚落；人已不在任時寫「已卸任」），點一下打開。
/// 追蹤清單只在這次遊玩中保留（換世界清空），不寫進存檔。只改介面。
/// </summary>
public partial class ObserverRoot
{
    private readonly List<(bool Person, long Id)> _tracked = new();
    private (bool Person, long Id)? _trackSubject;
    private Button? _trackButton;
    private VBoxContainer? _trackedRows;
    private string _trackedSignature = "";

    /// <summary>詳情標題列的追蹤鈕（由 <see cref="BuildDetailPanel"/> 之後呼叫）。</summary>
    private void BuildTrackButton(HBoxContainer header)
    {
        _trackButton = new Button { Text = "☆ 追蹤", Flat = true, Visible = false, FocusMode = FocusModeEnum.None, TooltipText = "加入右側的追蹤清單，之後一點就找得回來" };
        _trackButton.Pressed += () =>
        {
            if (_trackSubject is not { } subject)
                return;
            if (!_tracked.Remove(subject))
                _tracked.Insert(0, subject);
            SyncTrackButton();
            RefreshTracked();
        };
        header.AddChild(_trackButton);
        header.MoveChild(_trackButton, header.GetChildCount() - 2); // 放在關閉鈕前面
    }

    /// <summary>右側清單最上面的追蹤段（由 <see cref="BuildOutliner"/> 呼叫）。</summary>
    private void BuildTrackedSection(VBoxContainer column)
    {
        _trackedRows = new VBoxContainer { Visible = false };
        _trackedRows.AddThemeConstantOverride("separation", 2);
        column.AddChild(_trackedRows);
    }

    /// <summary>詳情打開時：記下這頁能不能追蹤（聚落或人物）。</summary>
    private void SetTrackSubject((bool Person, long Id)? subject)
    {
        _trackSubject = subject;
        SyncTrackButton();
    }

    private void SyncTrackButton()
    {
        if (_trackButton is null)
            return;
        _trackButton.Visible = _trackSubject is not null;
        _trackButton.Text = _trackSubject is { } s && _tracked.Contains(s) ? "★ 追蹤中" : "☆ 追蹤";
    }

    private void ResetTracked()
    {
        _tracked.Clear();
        _trackSubject = null;
        SyncTrackButton();
        RefreshTracked();
    }

    /// <summary>每次刷新：內容有變才重建追蹤列。</summary>
    private void RefreshTracked()
    {
        if (_trackedRows is null)
            return;
        var rows = _tracked.Select(t => (t, Text: TrackedText(t))).ToList();
        var signature = string.Join("|", rows.Select(r => r.Text));
        if (signature == _trackedSignature)
            return;
        _trackedSignature = signature;
        foreach (var child in _trackedRows.GetChildren())
        {
            _trackedRows.RemoveChild(child);
            child.QueueFree();
        }
        _trackedRows.Visible = rows.Count > 0;
        if (rows.Count == 0)
            return;
        var title = new Label { Text = $"追蹤（{rows.Count}）" };
        title.AddThemeFontSizeOverride("font_size", 18);
        _trackedRows.AddChild(title);
        foreach (var (subject, text) in rows)
        {
            var button = new Button { Text = text, Flat = true, Alignment = HorizontalAlignment.Left, ClipText = true, FocusMode = FocusModeEnum.None };
            button.Pressed += () => OpenTracked(subject);
            _trackedRows.AddChild(button);
        }
        _trackedRows.AddChild(new HSeparator());
    }

    private string TrackedText((bool Person, long Id) subject)
    {
        if (subject.Person)
        {
            foreach (var site in _world.Sites.Settlements)
                if (_world.RepresentativesOf(site.Id).FirstOrDefault(r => r.Id == subject.Id) is { } person)
                    return $"★ {DemoRepresentative.RoleName(person.Role)} {person.Name}（{site.Name}）";
            return "★ （已卸任或過世）";
        }
        return _world.Sites.Settlements.FirstOrDefault(s => s.Id == subject.Id) is { } settlement
            ? $"★ {DemoSettlementHistory.TitledName(settlement, _world.ChronicleOf(settlement.Id).Tier)}　人口 {_world.SettlementPopulation(settlement.Id)}"
                + (settlement.IsRuin ? "（遺跡）" : _world.IsStarving(settlement.Id) ? "（挨餓）" : "")
            : "★ （已消失）";
    }

    private void OpenTracked((bool Person, long Id) subject)
    {
        if (subject.Person)
        {
            var home = _world.Sites.Settlements.FirstOrDefault(s => _world.RepresentativesOf(s.Id).Any(r => r.Id == subject.Id));
            if (home is not null)
            {
                _hexMap.FocusCell(_world.Map.GetCell(home.Column, home.Row));
                ShowPersonDetail(subject.Id);
            }
            return;
        }
        if (_world.Sites.Settlements.FirstOrDefault(s => s.Id == subject.Id) is { } site)
        {
            _hexMap.FocusCell(_world.Map.GetCell(site.Column, site.Row));
            ShowDetailAt(site.Column, site.Row);
        }
    }
}
