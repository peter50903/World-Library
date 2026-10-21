using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// #95：播放中發生重大事件 → 顯示可點選提示並自動暫停；點提示 → 事件詳情（#89）與地圖定位。
/// 哪些事件算「重大」正式規則尚未定案，這裡用 TEMP 清單（見 <see cref="TempMajorKinds"/>），已在問答板請使用者裁決。
/// 每筆事件最多提示一次（以事件編號水位線記錄已看過的事件）；換世界（讀檔、切換情境）時清除提示。
/// </summary>
public partial class ObserverRoot
{
    private static readonly bool AlertSmoke = OS.HasEnvironment("WORLD_SIM_ALERT_SMOKE");

    /// <summary>TEMP：會觸發提示與自動暫停的事件種類（餓死、途中餓死、外移、建立新聚落、成為遺跡）。</summary>
    private static readonly DemoSettlementEventKind[] TempMajorKinds =
    {
        DemoSettlementEventKind.StarvationDeath,
        DemoSettlementEventKind.MigrantStarvationDeath,
        DemoSettlementEventKind.Emigration,
        DemoSettlementEventKind.SettlementFounded,
        DemoSettlementEventKind.SettlementRuined // 第 07 章 c 節
    };

    private CheckBox _autoPauseOnMajor = null!;
    private Button _alertButton = null!;
    private long _alertWatermark;
    private long _alertEventId;

    private void BuildMajorEventAlert(Container side)
    {
        _autoPauseOnMajor = new CheckBox
        {
            Text = "重大事件自動暫停（TEMP）",
            ButtonPressed = true,
            TooltipText = "播放中發生餓死、途中餓死、外移或建立新聚落時自動暫停並提示（TEMP 清單，待使用者裁決）"
        };
        side.AddChild(_autoPauseOnMajor);
        _alertButton = new Button
        {
            Visible = false,
            ClipText = true,
            CustomMinimumSize = new Vector2(0, 52)
        };
        _alertButton.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.3f));
        _alertButton.Pressed += OpenAlertedEvent;
        side.AddChild(_alertButton);
    }

    /// <summary>世界中最後一筆聚落事件的編號（沒有事件為 0）。</summary>
    private long LatestEventId()
    {
        var weeks = _world.WeekReports();
        for (var i = weeks.Count - 1; i >= 0; i--)
        {
            if (weeks[i].Events.Count > 0)
                return weeks[i].Events[^1].EventId;
        }

        return 0;
    }

    /// <summary>手動推進或換世界後：目前已有的事件都算「看過」，不會之後才補提示。</summary>
    private void SyncAlertWatermark() => _alertWatermark = LatestEventId();

    /// <summary>換世界（讀檔、切換情境、新示範）時清掉舊提示，不殘留。</summary>
    private void ResetMajorEventAlert()
    {
        _alertEventId = 0;
        _alertButton.Visible = false;
        _alertButton.Text = "";
        SyncAlertWatermark();
    }

    /// <summary>本次推進新出現（編號高於水位線）的第一筆重大事件；同時把水位線推到最新。</summary>
    private DemoSettlementEvent? TakeNewMajorEvent()
    {
        var latest = _world.LatestWeek;
        DemoSettlementEvent? found = null;
        if (latest is not null)
        {
            found = latest.Events.FirstOrDefault(e => e.EventId > _alertWatermark && TempMajorKinds.Contains(e.Kind));
        }

        SyncAlertWatermark();
        return found;
    }

    /// <summary>播放用的推進：逐日交給 Core；遇到重大事件就停在那一天、暫停並顯示提示。</summary>
    private void AdvancePlayback(int days)
    {
        try
        {
            DemoDayAdvance? last = null;
            DemoSettlementEvent? alert = null;
            var advanced = 0;
            for (var i = 0; i < days; i++)
            {
                last = _world.AdvanceOneDay();
                advanced++;
                var major = TakeNewMajorEvent();
                if (major is not null && _autoPauseOnMajor.ButtonPressed)
                {
                    alert = major;
                    break;
                }
            }

            if (alert is null)
            {
                _notice.Text = $"{_playback.StatusText}；已推進 {advanced} 天（第 {last!.Day} 天）；{WeeklySummaryText()}";
                Refresh();
                return;
            }

            _alertEventId = alert.EventId;
            _alertButton.Text = $"⚠ 重大事件 #{alert.EventId} {alert.SettlementName} {DemoCausalIndex.KindText(alert.Kind)}（點此查看）";
            _alertButton.TooltipText = alert.Describe();
            _alertButton.Visible = true;
            PausePlayback(); // 會 Refresh。
            PlayAlert(); // b 節：重大事件警示音。
            _notice.Text = $"第 {last!.Day} 日發生重大事件 #{alert.EventId}（{DemoCausalIndex.KindText(alert.Kind)}），已自動暫停。";
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
        {
            PausePlayback();
            _notice.Text = $"推進失敗（無狀態偽造），已暫停：{ex.Message}";
        }
    }

    private void OpenAlertedEvent()
    {
        if (_alertEventId == 0)
            return;
        RefreshEventList(); // 確保因果索引已包含這筆事件。
        SelectEvent(_alertEventId);
        _alertButton.Visible = false;
        _alertEventId = 0; // 一則提示只用一次。
    }

    /// <summary>
    /// CI-only 真 Godot 冒煙：開拓情境以真按鈕播放 → 第一筆重大事件出現的那一天自動暫停（日期與 Core 獨立重算相同），
    /// 再等多幀日期不變；點提示 → 詳情與地圖定位到該事件聚落；繼續播放的下一則提示是不同、較新的事件；
    /// 切換情境後提示消失。
    /// </summary>
    private void VerifyAlertSmoke()
    {
        void Fail(string why) => throw new InvalidOperationException("Alert smoke: " + why);
        void Press(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        int Day() => _world.Snapshot().DaysElapsed;

        // Core 獨立重算：第一筆重大事件的編號與發生日。
        var probe = CreateTempWorld(TempScenario.Frontier);
        long expectedId = 0;
        var expectedDay = 0;
        for (var day = 1; day <= SettleSmokeDays && expectedId == 0; day++)
        {
            probe.AdvanceOneDay();
            var major = probe.LatestWeek?.Events.FirstOrDefault(e => TempMajorKinds.Contains(e.Kind));
            if (major is not null)
            {
                expectedId = major.EventId;
                expectedDay = day;
            }
        }

        if (expectedId == 0)
            Fail("Core produced no major event within the smoke window.");

        InstallWorld(CreateTempWorld(TempScenario.Frontier), "重大事件提示冒煙測試");
        _speedPicker.Select(2);
        _speedPicker.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
        Press(_playButton);
        for (var frame = 0; frame < 40 && _playback.Playing; frame++)
            _Process(1.0);

        if (_playback.Playing || !_alertButton.Visible || _alertEventId != expectedId || Day() != expectedDay)
            Fail($"expected auto-pause on event #{expectedId} at day {expectedDay}, got #{_alertEventId} at day {Day()}.");

        for (var frame = 0; frame < 10; frame++)
            _Process(1.0);
        if (Day() != expectedDay)
            Fail("the world advanced while paused by an alert.");

        // 點提示 → 事件詳情與地圖定位。
        if (!_alertButton.Text.Contains($"#{expectedId} ", StringComparison.Ordinal))
            Fail("alert text does not name the event.");
        Press(_alertButton);
        var alerted = _causal?.Find(expectedId);
        var settlement = alerted is null ? null : _world.Sites.Settlements.FirstOrDefault(s => s.Id == alerted.SettlementId);
        if (_selectedEventId != expectedId || settlement is null || _alertButton.Visible
            || _hexMap.Highlight is not { } h || h.Column != settlement.Column || h.Row != settlement.Row)
            Fail("clicking the alert did not open the event detail and focus its settlement.");

        // 繼續播放：下一則提示必須是較新的另一筆事件（同一筆只提示一次）。
        Press(_playButton);
        for (var frame = 0; frame < 80 && _playback.Playing; frame++)
            _Process(1.0);
        var secondId = _alertEventId;
        if (_playback.Playing)
            Press(_playButton);
        if (secondId != 0 && secondId <= expectedId)
            Fail($"event #{expectedId} was alerted twice (second alert #{secondId}).");

        // 換世界不殘留舊提示。
        InstallWorld(CreateTempWorld(TempScenario.Normal), "冒煙測試結束");
        if (_alertButton.Visible || _alertEventId != 0)
            Fail("the old alert survived a world switch.");

        GD.Print($"WORLD_SIM_ALERT_OK: First=#{expectedId} Day={expectedDay} Second=#{secondId} Cell=({settlement!.Column},{settlement.Row}) Cleared=1");
    }
}
