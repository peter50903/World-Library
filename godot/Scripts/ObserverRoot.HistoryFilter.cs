using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// #96：歷史事件搜尋與篩選（43G OBS_WHY／OBS_ENTITY）。篩選規則全部在 Core 的
/// <see cref="DemoEventFilter"/>；Godot 只收集輸入、顯示結果，不改任何事件。
/// TEMP 畫面預設：全部日期（迄日 0＝不限）、全部聚落、全部類別、無關鍵字；換世界（切換情境、讀檔）時回到預設。
/// </summary>
public partial class ObserverRoot
{
    private static readonly bool HistoryFilterSmoke = OS.HasEnvironment("WORLD_SIM_HISTORY_FILTER_SMOKE");

    private OptionButton _filterSettlement = null!;
    private OptionButton _filterKind = null!;
    private SpinBox _filterFromDay = null!;
    private SpinBox _filterToDay = null!;
    private LineEdit _filterText = null!;
    private Button _filterClear = null!;
    private Label _filterCount = null!;
    private int _filterSettlementCount = -1;

    private static readonly DemoSettlementEventKind[] FilterKinds = Enum.GetValues<DemoSettlementEventKind>();

    /// <summary>事件清單上方的篩選列（兩行）與結果筆數。</summary>
    private void BuildHistoryFilter(Container listColumn)
    {
        var row1 = new HBoxContainer();
        listColumn.AddChild(row1);
        _filterSettlement = new OptionButton { TooltipText = "只看這個聚落的事件", CustomMinimumSize = new Vector2(130, 0) };
        _filterSettlement.AddItem("全部聚落");
        _filterSettlement.SetItemMetadata(0, 0);
        _filterSettlement.ItemSelected += _ => RefreshEventList();
        row1.AddChild(_filterSettlement);

        _filterKind = new OptionButton { TooltipText = "只看這一類事件", CustomMinimumSize = new Vector2(110, 0) };
        _filterKind.AddItem("全部類別");
        foreach (var kind in FilterKinds)
            _filterKind.AddItem(DemoCausalIndex.KindText(kind));
        _filterKind.ItemSelected += _ => RefreshEventList();
        row1.AddChild(_filterKind);

        _filterClear = new Button { Text = "清除篩選" };
        _filterClear.Pressed += () =>
        {
            ResetHistoryFilter();
            RefreshEventList();
        };
        row1.AddChild(_filterClear);

        var row2 = new HBoxContainer();
        listColumn.AddChild(row2);
        row2.AddChild(new Label { Text = "第" });
        _filterFromDay = new SpinBox { MinValue = 0, MaxValue = DemoObserverWorld.TempMaxSaveDays, Step = 1, TooltipText = "起日（含）" };
        _filterFromDay.ValueChanged += _ => RefreshEventList();
        row2.AddChild(_filterFromDay);
        row2.AddChild(new Label { Text = "～" });
        _filterToDay = new SpinBox { MinValue = 0, MaxValue = DemoObserverWorld.TempMaxSaveDays, Step = 1, TooltipText = "迄日（含）；0＝不限" };
        _filterToDay.ValueChanged += _ => RefreshEventList();
        row2.AddChild(_filterToDay);
        row2.AddChild(new Label { Text = "日" });
        _filterText = new LineEdit
        {
            PlaceholderText = "搜尋聚落、類別、原因或 #編號",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ClearButtonEnabled = true
        };
        _filterText.TextChanged += _ => RefreshEventList();
        row2.AddChild(_filterText);

        _filterCount = new Label { Text = "" };
        listColumn.AddChild(_filterCount);
    }

    /// <summary>由畫面控制項組出 Core 篩選條件。</summary>
    private DemoEventFilter CurrentHistoryFilter()
    {
        var kindIndex = _filterKind.Selected;
        var toDay = (int)_filterToDay.Value;
        return new DemoEventFilter(
            FromDay: (int)_filterFromDay.Value,
            ToDay: toDay <= 0 ? int.MaxValue : toDay,
            SettlementId: _filterSettlement.Selected <= 0 ? 0 : _filterSettlement.GetItemMetadata(_filterSettlement.Selected).AsInt32(),
            Kind: kindIndex <= 0 ? null : FilterKinds[kindIndex - 1],
            Text: _filterText.Text);
    }

    /// <summary>回到 TEMP 預設（不觸發訊號，呼叫端自行重畫）。</summary>
    private void ResetHistoryFilter()
    {
        _filterSettlement.Select(0);
        _filterKind.Select(0);
        _filterFromDay.SetValueNoSignal(0);
        _filterToDay.SetValueNoSignal(0);
        _filterText.Text = "";
    }

    /// <summary>聚落選單跟著世界的聚落（含新建立的聚落）；保留目前選取的聚落編號。</summary>
    private void SyncFilterSettlements()
    {
        var settlements = _world.Sites.Settlements;
        if (settlements.Count == _filterSettlementCount)
            return;
        var selectedId = _filterSettlement.Selected <= 0 ? 0 : _filterSettlement.GetItemMetadata(_filterSettlement.Selected).AsInt32();
        while (_filterSettlement.ItemCount > 1)
            _filterSettlement.RemoveItem(_filterSettlement.ItemCount - 1);
        var selectIndex = 0;
        foreach (var s in settlements.OrderBy(s => s.Id))
        {
            _filterSettlement.AddItem(s.Name);
            var index = _filterSettlement.ItemCount - 1;
            _filterSettlement.SetItemMetadata(index, s.Id);
            if (s.Id == selectedId)
                selectIndex = index;
        }

        _filterSettlement.Select(selectIndex);
        _filterSettlementCount = settlements.Count;
    }

    private List<long> ListedEventIds()
        => Enumerable.Range(0, _eventList.ItemCount).Select(i => _eventList.GetItemMetadata(i).AsInt64()).ToList();

    /// <summary>
    /// CI-only 真 Godot 冒煙：用真的選單、數字框、文字框篩選開拓情境的事件；結果與 Core 篩選逐筆相同、
    /// 新的在上、重複套用順序不變、空結果有提示、點結果會開詳情並定位地圖；篩選不改 Core 世界；
    /// 讀檔與切換情境後篩選回到預設。
    /// </summary>
    private void VerifyHistoryFilterSmoke(Button nextButton, Button[] saveButtons, Button[] loadButtons)
    {
        void Fail(string why) => throw new InvalidOperationException("History filter smoke: " + why);

        InstallWorld(CreateTempWorld(TempScenario.Frontier), "歷史篩選冒煙測試");
        for (var i = 0; i < SettleSmokeDays && !_world.SettlementEvents().Any(e => e.Kind == DemoSettlementEventKind.SettlementFounded); i++)
            nextButton.EmitSignal(BaseButton.SignalName.Pressed);
        var founded = _world.SettlementEvents().FirstOrDefault(e => e.Kind == DemoSettlementEventKind.SettlementFounded)
            ?? throw new InvalidOperationException("History filter smoke: no founding within the smoke window.");
        var coreBefore = _world.ExportTempSave();

        List<long> Expected(DemoEventFilter filter)
            => ShownEvents(filter.Apply(_world.SettlementEvents())).Select(e => e.EventId).ToList();

        void Check(string step, DemoEventFilter expected)
        {
            if (CurrentHistoryFilter() != expected)
                Fail($"{step}: controls produced {CurrentHistoryFilter()} instead of {expected}");
            var listed = ListedEventIds();
            if (!listed.SequenceEqual(Expected(expected)))
                Fail($"{step}: list differs from Core filter ({listed.Count} vs {Expected(expected).Count})");
            if (_world.ExportTempSave() != coreBefore)
                Fail($"{step}: filtering changed the Core world");
        }

        // ① 類別：外移（真的 OptionButton 訊號）。
        var emigrationIndex = Array.IndexOf(FilterKinds, DemoSettlementEventKind.Emigration) + 1;
        _filterKind.Select(emigrationIndex);
        _filterKind.EmitSignal(OptionButton.SignalName.ItemSelected, emigrationIndex);
        Check("kind", new DemoEventFilter(Kind: DemoSettlementEventKind.Emigration));
        var kindCount = _eventList.ItemCount;
        if (kindCount == 0)
            Fail("no emigration listed");

        // ② 再加聚落：新聚落的來源聚落。
        var sourceIndex = Enumerable.Range(0, _filterSettlement.ItemCount)
            .First(i => i > 0 && _filterSettlement.GetItemMetadata(i).AsInt32() == _causal!.ParentOf(founded)!.SettlementId);
        _filterSettlement.Select(sourceIndex);
        _filterSettlement.EmitSignal(OptionButton.SignalName.ItemSelected, sourceIndex);
        var sourceId = _filterSettlement.GetItemMetadata(sourceIndex).AsInt32();
        Check("settlement", new DemoEventFilter(SettlementId: sourceId, Kind: DemoSettlementEventKind.Emigration));
        var settlementCount = _eventList.ItemCount;
        if (settlementCount == 0)
            Fail("source settlement has no emigration listed");

        // ③ 重複套用：同一條件再觸發一次，順序完全相同（決定性）。
        var again = ListedEventIds();
        _filterKind.EmitSignal(OptionButton.SignalName.ItemSelected, emigrationIndex);
        if (!again.SequenceEqual(ListedEventIds()))
            Fail("re-applying the same filter changed the order");

        // ④ 清除 → 以「#編號」搜尋建立新聚落，點結果開詳情並定位。
        _filterClear.EmitSignal(BaseButton.SignalName.Pressed);
        Check("clear", DemoEventFilter.All);
        _filterText.Text = $"#{founded.EventId}";
        _filterText.EmitSignal(LineEdit.SignalName.TextChanged, _filterText.Text);
        Check("search", new DemoEventFilter(Text: $"#{founded.EventId}"));
        if (_eventList.ItemCount != 1)
            Fail("search by #id did not give exactly one result");
        _eventList.Select(0);
        _eventList.EmitSignal(ItemList.SignalName.ItemSelected, 0);
        var settlement = _world.Sites.Settlements.Single(s => s.Id == founded.SettlementId);
        if (_selectedEventId != founded.EventId || !_eventDetail.Text.Contains(founded.Cause, StringComparison.Ordinal)
            || _hexMap.Highlight is not { } h || h.Column != settlement.Column || h.Row != settlement.Row)
            Fail("clicking the search result did not open its detail and focus the map");

        // ⑤ 日期：只看建立新聚落那一天。
        _filterText.Text = "";
        _filterText.EmitSignal(LineEdit.SignalName.TextChanged, "");
        var day = DemoEventFilter.DayOf(founded);
        _filterFromDay.Value = day; // 真的 SpinBox：會發出 ValueChanged。
        _filterToDay.Value = day;
        Check("day", new DemoEventFilter(FromDay: day, ToDay: day));
        if (!ListedEventIds().Contains(founded.EventId))
            Fail("day filter lost the founding event");

        // ⑥ 空結果：有提示、不是錯誤。
        _filterText.Text = "不存在的關鍵字zzz";
        _filterText.EmitSignal(LineEdit.SignalName.TextChanged, _filterText.Text);
        if (_eventList.ItemCount != 0 || !_filterCount.Text.Contains("沒有符合", StringComparison.Ordinal))
            Fail("empty result is not shown as empty");

        // ⑦ 存檔 → 設篩選 → 讀檔：讀入的世界相同、篩選回到預設。
        saveButtons[0].EmitSignal(BaseButton.SignalName.Pressed);
        _filterKind.Select(emigrationIndex);
        _filterKind.EmitSignal(OptionButton.SignalName.ItemSelected, emigrationIndex);
        loadButtons[0].EmitSignal(BaseButton.SignalName.Pressed);
        if (_world.ExportTempSave() != coreBefore)
            Fail("loaded world differs from the saved one");
        Check("after load", DemoEventFilter.All);

        // ⑧ 切換情境（新世界）：篩選回到預設、清單是新世界的事件。
        _filterKind.Select(emigrationIndex);
        _filterKind.EmitSignal(OptionButton.SignalName.ItemSelected, emigrationIndex);
        InstallWorld(CreateTempWorld(TempScenario.Normal), "冒煙測試結束");
        coreBefore = _world.ExportTempSave();
        Check("after switch", DemoEventFilter.All);
        if (_eventList.ItemCount != 0 || _filterSettlement.ItemCount != 1 + _world.Sites.Settlements.Count)
            Fail("switched world still shows old events or settlements");

        GD.Print($"WORLD_SIM_HISTORY_FILTER_OK: Kind={kindCount} Settlement={settlementCount} Search=#{founded.EventId} " +
            $"Day={day} Empty=1 Deterministic=1 LoadReset=1 SwitchReset=1 CoreUnchanged=1");
    }
}
