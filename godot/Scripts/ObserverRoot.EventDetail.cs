using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// #89：事件詳情面板與地圖定位（43G OBS_WHY／OBS_ENTITY；43F CAUSE_PARENT／EFFECT_CHILD）。
/// 點一筆聚落事件 → 顯示事件內容、直接原因（可再點）與直接後果（反向索引），
/// 地圖置中並框出該聚落。因果全部來自 Core 的 <see cref="DemoCausalIndex"/>；Godot 只顯示、不推算。
/// </summary>
public partial class ObserverRoot
{
    private static readonly bool EventDetailSmoke = OS.HasEnvironment("WORLD_SIM_EVENT_DETAIL_SMOKE");
    private const int EventListSize = 60;

    private ItemList _eventList = null!;
    private RichTextLabel _eventDetail = null!;
    private Button _causeButton = null!;
    private VBoxContainer _effectBox = null!;
    private readonly List<Button> _effectButtons = new();
    private VBoxContainer _eventDetailColumn = null!;

    private DemoCausalIndex? _causal;
    private DemoObserverWorld? _causalWorld;
    private int _causalWeeks = -1;
    private long _selectedEventId;

    /// <summary>
    /// 建立「事件清單＋事件詳情」。第 08-3 章：放在右側「世界紀錄」分頁，上下排列；
    /// 詳情只在點選一筆事件後出現。
    /// </summary>
    private void BuildEventDetailPanel(Container parent)
    {
        var listColumn = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsStretchRatio = 2f };
        parent.AddChild(listColumn);
        var title = new Label { Text = "世界紀錄", TooltipText = "新的在上；紅色＝重大事件；點一筆看詳情、原因與後果並定位地圖" };
        title.AddThemeFontSizeOverride("font_size", 20);
        listColumn.AddChild(title);
        BuildHistoryFilter(listColumn); // #96：篩選列在清單上方。
        _eventList = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 140),
            SelectMode = ItemList.SelectModeEnum.Single
        };
        _eventList.ItemSelected += index => SelectEvent(_eventList.GetItemMetadata((int)index).AsInt64());
        listColumn.AddChild(_eventList);

        var detailColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            Visible = false
        };
        _eventDetailColumn = detailColumn;
        parent.AddChild(detailColumn);
        detailColumn.AddChild(new Label { Text = "事件詳情" });
        _eventDetail = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = true,
            FitContent = false,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 80),
            Text = "請在上方清單點選一筆聚落事件。"
        };
        detailColumn.AddChild(_eventDetail);
        _causeButton = new Button { Text = "直接原因：—", Disabled = true };
        _causeButton.Pressed += () =>
        {
            if (_causal?.Find(_selectedEventId) is { CauseEventId: > 0 } e)
                SelectEvent(e.CauseEventId);
        };
        detailColumn.AddChild(_causeButton);
        _effectBox = new VBoxContainer();
        detailColumn.AddChild(_effectBox);
    }

    /// <summary>週報有變（或換了世界）才重建因果索引；清單保留目前選取。#96：清單只顯示符合篩選的事件。</summary>
    private void RefreshEventList()
    {
        var weeks = _world.WeekReports();
        if (!ReferenceEquals(_causalWorld, _world) || _causalWeeks != weeks.Count)
        {
            if (!ReferenceEquals(_causalWorld, _world))
            {
                _selectedEventId = 0;
                ResetHistoryFilter(); // #96：換世界（切換情境、讀檔）時篩選回到預設。
                _filterSettlementCount = -1;
            }
            _causal = _world.BuildCausalIndex(); // 第 04-1 章：含讀檔前保存的較早事件
            _causalWorld = _world;
            _causalWeeks = weeks.Count;
        }

        SyncFilterSettlements();
        var events = _causal!.Events;
        var filter = CurrentHistoryFilter();
        var matched = filter.Apply(events);
        var shown = ShownEvents(matched);
        _eventList.Clear();
        foreach (var e in shown)
        {
            var major = TempMajorKinds.Contains(e.Kind);
            // 第 08-3 章：依時間列出（世界日期）。
            var index = _eventList.AddItem($"{(major ? "⚠ " : "")}{DateText(DemoEventFilter.DayOf(e))}｜{e.SettlementName} {DemoCausalIndex.KindText(e.Kind)}（#{e.EventId}）");
            _eventList.SetItemMetadata(index, e.EventId);
            // 第 05 章 b2：重大事件（TEMP 種類，同自動暫停）以醒目紅字標示。
            if (major)
                _eventList.SetItemCustomFgColor(index, new Color(0.85f, 0.20f, 0.15f));
            if (e.EventId == _selectedEventId)
                _eventList.Select(index);
        }

        _filterCount.Text = filter.IsAll
            ? $"共 {events.Count} 筆（顯示最新 {shown.Count} 筆）"
            : matched.Count == 0
                ? "沒有符合篩選條件的事件。"
                : $"符合 {matched.Count} 筆（顯示最新 {shown.Count} 筆）";

        if (_selectedEventId == 0 || _causal.Find(_selectedEventId) is null)
            ShowNoEvent(events.Count == 0 ? "目前沒有聚落事件（第一次週結算在第 7 日）。" : "請在上方清單點選一筆聚落事件。");
    }

    /// <summary>
    /// 清單顯示：符合條件的最新 60 筆＋最新 5 筆建立新聚落（與文字歷史同原則），新的在上。
    /// 輸入已是「新的在上」的篩選結果。
    /// </summary>
    private static List<DemoSettlementEvent> ShownEvents(IReadOnlyList<DemoSettlementEvent> matchedNewestFirst)
        => matchedNewestFirst.Take(EventListSize)
            .Union(matchedNewestFirst.Where(e => e.Kind == DemoSettlementEventKind.SettlementFounded).Take(5))
            .OrderByDescending(e => e.EventId)
            .ToList();

    private void ShowNoEvent(string text)
    {
        _selectedEventId = 0;
        _eventDetailColumn.Visible = false; // 第 08-3 章：沒有選取就不顯示詳情。
        _eventDetail.Text = text;
        _causeButton.Text = "直接原因：—";
        _causeButton.Disabled = true;
        ClearEffectButtons();
        _hexMap.Highlight = null;
        _hexMap.QueueRedraw();
    }

    private void ClearEffectButtons()
    {
        foreach (var button in _effectButtons)
        {
            _effectBox.RemoveChild(button);
            button.QueueFree();
        }

        _effectButtons.Clear();
    }

    /// <summary>顯示一筆事件的詳情與因果，並把地圖置中到該聚落。</summary>
    private void SelectEvent(long eventId)
    {
        var e = _causal?.Find(eventId);
        if (_causal is null || e is null)
        {
            ShowNoEvent($"找不到事件 #{eventId}。");
            return;
        }

        _selectedEventId = eventId;
        _eventDetailColumn.Visible = true;
        // 第 08-3 章：點了事件就切到右側「世界紀錄」分頁（例如從警示或圖表跳來時）。
        if (_rightView != RightView.Console)
        {
            _rightView = RightView.Console;
            ApplyHudLayout();
        }
        for (var i = 0; i < _eventList.ItemCount; i++)
        {
            if (_eventList.GetItemMetadata(i).AsInt64() == eventId)
                _eventList.Select(i);
        }

        var parent = _causal.ParentOf(e);
        var children = _causal.ChildrenOf(e);
        var lines = new List<string>
        {
            $"事件 #{e.EventId}｜第 {e.Week} 週（世界第 {(int)e.WorldTime.TotalDays} 日）",
            $"聚落：{e.SettlementName}（實體 ID {e.SettlementId}）｜種類：{DemoCausalIndex.KindText(e.Kind)}｜數量：{e.Count}",
            e.MigrationBatchId > 0 ? $"遷徙隊伍：#{e.MigrationBatchId}" : "",
            $"原因說明：{e.Cause}",
            // 第 03 章 b 節：正式史庫的同一筆事件（類型、地點、相關實體、原因邊＋根因；Core 共用格式）。
            _world.EventHistory.Find(e.EventId) is { } formal
                ? string.Join("\n", DemoEventProjection.DescribeLines(formal))
                : "⚠ 正式史庫找不到這筆事件",
            parent is null
                ? "直接原因：無（根因：本週需求大於可取得，週帳缺糧為證據）"
                : $"直接原因：#{parent.EventId} {parent.SettlementName} {DemoCausalIndex.KindText(parent.Kind)}（第 {parent.Week} 週）",
            children.Count == 0 ? "直接後果：目前沒有" : $"直接後果：{children.Count} 筆（見下方按鈕）"
        };
        if (_causal.Problems.Count > 0)
            lines.Add($"⚠ 因果證據有 {_causal.Problems.Count} 個問題：{_causal.Problems[0]}");
        _eventDetail.Text = string.Join("\n", lines.Where(l => l.Length > 0));

        _causeButton.Disabled = parent is null;
        _causeButton.Text = parent is null
            ? "直接原因：無（根因）"
            : $"← 直接原因 #{parent.EventId} {parent.SettlementName} {DemoCausalIndex.KindText(parent.Kind)}";

        ClearEffectButtons();
        foreach (var child in children)
        {
            var childId = child.EventId;
            var button = new Button
            {
                Text = $"→ 直接後果 #{childId} {child.SettlementName} {DemoCausalIndex.KindText(child.Kind)}（第 {child.Week} 週）"
            };
            button.Pressed += () => SelectEvent(childId);
            _effectBox.AddChild(button);
            _effectButtons.Add(button);
        }

        // 遷徙隊伍的途中餓死事件記在出發聚落，所以一律定位到事件的聚落。
        var settlement = _world.Sites.Settlements.FirstOrDefault(s => s.Id == e.SettlementId);
        if (settlement is null)
        {
            _hexMap.Highlight = null;
            _hexMap.QueueRedraw();
            return;
        }

        var cell = _world.Map.GetCell(settlement.Column, settlement.Row);
        _hexMap.FocusCell(cell);
        OnCellSelected(cell); // 原有點格查詢面板同步顯示這個聚落。
    }

    /// <summary>
    /// CI-only 真 Godot 冒煙：開拓情境跑到建立新聚落 → 點該事件 → 詳情顯示外移原因 → 點原因 →
    /// 再點到饑荒；每一步地圖選取格都等於該事件聚落的座標。
    /// </summary>
    private void VerifyEventDetailSmoke(Button nextButton)
    {
        void Fail(string why) => throw new InvalidOperationException("Event detail smoke: " + why);

        InstallWorld(CreateTempWorld(TempScenario.Frontier), "事件詳情冒煙測試");
        for (var i = 0; i < SettleSmokeDays && !_world.SettlementEvents().Any(e => e.Kind == DemoSettlementEventKind.SettlementFounded); i++)
            nextButton.EmitSignal(BaseButton.SignalName.Pressed);

        var founded = _world.SettlementEvents().FirstOrDefault(e => e.Kind == DemoSettlementEventKind.SettlementFounded)
            ?? throw new InvalidOperationException("Event detail smoke: no founding within the smoke window.");
        if (_causal is null || _causal.Problems.Count > 0)
            Fail("Core causal index has problems: " + string.Join("；", _causal?.Problems ?? Array.Empty<string>()));

        void CheckFocused(DemoSettlementEvent e)
        {
            var settlement = _world.Sites.Settlements.Single(s => s.Id == e.SettlementId);
            if (_hexMap.Highlight is not { } h || h.Column != settlement.Column || h.Row != settlement.Row)
                Fail($"map highlight is not event #{e.EventId}'s settlement");
            if (_hexMap.Size.X > 0 && _hexMap.Size.Y > 0
                && _hexMap.CellCenter(_world.Map.GetCell(settlement.Column, settlement.Row)).DistanceTo(_hexMap.Size * 0.5f) > 1f)
                Fail($"map is not centred on event #{e.EventId}'s settlement");
            if (!_cellInfo.Text.Contains($"聚落：{settlement.Name}", StringComparison.Ordinal))
                Fail($"cell panel does not show event #{e.EventId}'s settlement");
        }

        // ① 用真的 ItemList 訊號點「建立新聚落」。
        var row = Enumerable.Range(0, _eventList.ItemCount)
            .FirstOrDefault(i => _eventList.GetItemMetadata(i).AsInt64() == founded.EventId, -1);
        if (row < 0)
            Fail("founding event is not listed");
        _eventList.Select(row);
        _eventList.EmitSignal(ItemList.SignalName.ItemSelected, row);
        if (_selectedEventId != founded.EventId || !_eventDetail.Text.Contains(founded.Cause, StringComparison.Ordinal))
            Fail("founding detail not shown");
        var emigration = _causal!.ParentOf(founded);
        if (emigration is null || emigration.Kind != DemoSettlementEventKind.Emigration
            || !_eventDetail.Text.Contains($"直接原因：#{emigration.EventId}", StringComparison.Ordinal))
            Fail("founding detail does not name its emigration cause");
        CheckFocused(founded);

        // ② 點「直接原因」→ 外移；外移的直接後果按鈕包含這次建立。
        _causeButton.EmitSignal(BaseButton.SignalName.Pressed);
        if (_selectedEventId != emigration!.EventId || !_eventDetail.Text.Contains(emigration.Cause, StringComparison.Ordinal)
            || !_effectButtons.Any(b => b.Text.Contains($"#{founded.EventId} ", StringComparison.Ordinal)))
            Fail("emigration detail or its effects are wrong");
        CheckFocused(emigration);

        // ③ 再點「直接原因」→ 饑荒（根因，原因按鈕停用）。
        var hunger = _causal.ParentOf(emigration);
        _causeButton.EmitSignal(BaseButton.SignalName.Pressed);
        if (hunger is null || hunger.Kind != DemoSettlementEventKind.Hunger || _selectedEventId != hunger.EventId
            || !_causeButton.Disabled || !_eventDetail.Text.Contains("根因", StringComparison.Ordinal))
            Fail("hunger root not reached");
        CheckFocused(hunger!);

        // ④ 從饑荒點直接後果回到外移（EFFECT_CHILD 反向索引可往下走）。
        var back = _effectButtons.FirstOrDefault(b => b.Text.Contains($"#{emigration.EventId} ", StringComparison.Ordinal));
        if (back is null)
            Fail("hunger has no effect button back to the emigration");
        back!.EmitSignal(BaseButton.SignalName.Pressed);
        if (_selectedEventId != emigration.EventId)
            Fail("effect button did not select the emigration");

        var foundedSettlement = _world.Sites.Settlements.Single(s => s.Id == founded.SettlementId);

        // 第 03 章 b 節：建村事件在正式史庫的類型、地點、相關實體（新聚落＋隊伍＋來源聚落）與一條直接原因邊。
        var formal = _world.EventHistory.Find(founded.EventId);
        if (formal is null || formal.EventTypeId != "TEMP_EV_SETTLEMENT_FOUNDED"
            || formal.Causes.Length != 1 || formal.Causes[0].Kind != WorldSimulation.Core.History.CauseEdgeKind.Direct
            || formal.Causes[0].ParentEventId != emigration.EventId
            || (formal.LocationColumn, formal.LocationRow) != (foundedSettlement.Column, foundedSettlement.Row)
            || !formal.RelatedEntityIds.Contains(founded.SettlementId))
            Fail("formal history record of the founding is wrong");
        SelectEvent(founded.EventId);
        foreach (var line in DemoEventProjection.DescribeLines(formal!))
            if (!_eventDetail.Text.Contains(line, StringComparison.Ordinal))
                Fail($"event detail is missing the formal line: {line}");

        GD.Print($"WORLD_SIM_EVENT_DETAIL_OK: Founded=#{founded.EventId} Emigration=#{emigration.EventId} " +
            $"Hunger=#{hunger!.EventId} Cell=({foundedSettlement.Column},{foundedSettlement.Row}) " +
            $"Type={formal!.EventTypeId} Edge={formal.Causes[0].Kind}");
    }
}
