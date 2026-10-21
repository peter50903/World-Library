using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08 章 b 節：年度圖表（工具列「圖表」開啟的疊加視窗）。
/// 年帳全部來自 Core 的 <see cref="DemoYearLedger"/>（已提交週報彙整，與 CLI 無畫面長測年報逐年相同）；
/// 點圖上某年 → 顯示該年年報；「看這一年的事件」→ 世界紀錄篩選到該年日期範圍；再點事件 → 既有事件詳情與地圖定位。
/// 只看、不改世界。
/// </summary>
public partial class ObserverRoot
{
    private static readonly bool ChartSmoke = OS.HasEnvironment("WORLD_SIM_CHART_SMOKE");

    private Button _toolChart = null!;
    private YearChart _yearChart = null!;
    private OptionButton _chartSeries = null!;
    private Label _chartYearInfo = null!;
    private Button _chartShowEvents = null!;
    private IReadOnlyList<DemoYearSummary> _chartRows = Array.Empty<DemoYearSummary>();
    private DemoObserverWorld? _chartWorld;

    private Control BuildYearChartPage()
    {
        var page = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        page.AddThemeConstantOverride("separation", 8);

        var row = new HBoxContainer();
        page.AddChild(row);
        row.AddChild(new Label { Text = "顯示：" });
        _chartSeries = new OptionButton { TooltipText = "選擇圖表內容" };
        foreach (var series in YearChart.AllSeries)
            _chartSeries.AddItem(series.Name);
        _chartSeries.ItemSelected += index => _yearChart.SeriesIndex = (int)index;
        row.AddChild(_chartSeries);
        row.AddChild(new Label
        {
            Text = "（資料＝已提交週報彙整的年帳；點長條看該年）",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        });

        var frame = new PanelContainer { CustomMinimumSize = new Vector2(0, 280) }; // TEMP 圖表高度
        frame.AddThemeStyleboxOverride("panel", (StyleBox?)ArtAssets.ChartFrameStyle() ?? TempPanelStyle());
        page.AddChild(frame);
        _yearChart = new YearChart
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Stop
        };
        _yearChart.YearSelected += OnChartYearSelected;
        frame.AddChild(_yearChart);

        _chartYearInfo = new Label
        {
            Text = "點圖上的某一年查看年報。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        page.AddChild(_chartYearInfo);
        _chartShowEvents = new Button { Text = "看這一年的事件", Disabled = true, TooltipText = "世界紀錄只列出這一年的事件；點事件看原因並定位地圖" };
        _chartShowEvents.Pressed += ShowChartYearEvents;
        page.AddChild(_chartShowEvents);
        return page;
    }

    /// <summary>重新彙整目前世界的年帳（開視窗、推進與換世界時）。</summary>
    private void RefreshYearChart()
    {
        if (_yearChart is null)
            return;
        if (!ReferenceEquals(_chartWorld, _world))
        {
            _chartWorld = _world;
            _chartShowEvents.Disabled = true;
            _chartYearInfo.Text = "點圖上的某一年查看年報。";
        }

        _chartRows = _calendar is null
            ? Array.Empty<DemoYearSummary>()
            : DemoYearLedger.FromWeeks(_world.WeekReports(), _calendar.DaysPerYear, _world.Snapshot().DaysElapsed);
        _yearChart.Rows = _chartRows;
        _yearChart.Capacity = _world.MapCapacity.People; // 第 08-1 章：理論上限線（只有自然採集世界有值）
        if (_calendar is null)
            _chartYearInfo.Text = $"曆法讀取失敗，無法分年：{_calendarError}";
        else if (_yearChart.SelectedYear == 0)
            _chartShowEvents.Disabled = true;
        else
            OnChartYearSelected(_yearChart.SelectedYear);
    }

    private void RefreshYearChartIfOpen()
    {
        if (_modalOpen == "chart")
            RefreshYearChart();
    }

    private DemoYearSummary? SelectedChartYear()
        => _chartRows.FirstOrDefault(r => r.Year == _yearChart.SelectedYear);

    private void OnChartYearSelected(int year)
    {
        if (SelectedChartYear() is not { } row || _calendar is null)
            return;
        var progress = DemoYearLedger.IsComplete(row, _calendar.DaysPerYear) ? string.Empty : "（進行中）";
        _chartYearInfo.Text = $"{row.Describe()}{progress}｜年末 {_calendar.Format(row.LastDay)}";
        _chartShowEvents.Disabled = false;
    }

    /// <summary>世界紀錄篩選到選取年份的日期範圍（其他條件清除），關閉視窗。</summary>
    private void ShowChartYearEvents()
    {
        if (SelectedChartYear() is not { } row)
            return;
        ResetHistoryFilter();
        _filterFromDay.SetValueNoSignal(row.FirstDay);
        _filterToDay.SetValueNoSignal(row.LastDay);
        RefreshEventList();
        CloseModal();
    }

    /// <summary>
    /// CI-only 真 Godot 冒煙：局部饑荒世界推進 2 年又 100 日；工具列按鈕開圖表；圖表年帳與 Core 無畫面長測年報逐年相同、
    /// 進行中的第 3 年也列出；四組系列的長條數值＝年帳欄位；真的滑鼠點長條選年；「看這一年的事件」讓世界紀錄
    /// 只列該年事件（與 Core 篩選逐筆相同）；點事件開詳情並定位地圖；全程不改 Core 世界。
    /// </summary>
    private void VerifyYearChartSmoke()
    {
        void Fail(string why) => throw new InvalidOperationException("Year chart smoke: " + why);
        if (_calendar is null)
            Fail($"calendar not loaded: {_calendarError}");
        var daysPerYear = _calendar!.DaysPerYear;
        const ulong seed = 42;
        var config = DemoWeeklyTempConfig.LocalFamineSample;
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        var days = 2 * daysPerYear + 100;
        for (var d = 0; d < days; d++)
            world.AdvanceOneDay();
        InstallWorld(world, "年度圖表冒煙測試");
        var coreBefore = _world.ExportTempSave();

        _toolChart.EmitSignal(BaseButton.SignalName.Pressed);
        if (_modalOpen != "chart" || !_modalPages["chart"].Visible)
            Fail("toolbar chart button did not open the chart window");

        var cli = DemoCenturyRun.Run(seed, 2, daysPerYear, config).Years;
        var rows = _yearChart.Rows;
        if (rows.Count != 3 || !rows.Take(2).SequenceEqual(cli)
            || !rows.Take(2).Select(r => r.Describe()).SequenceEqual(cli.Select(r => r.Describe())))
            Fail($"chart years differ from the headless year report ({rows.Count} rows)");
        if (DemoYearLedger.IsComplete(rows[2], daysPerYear) || rows[2].LastDay != days || rows[2].Problems().Count != 0)
            Fail("the year in progress is not listed correctly");

        for (var s = 0; s < YearChart.AllSeries.Count; s++)
        {
            _chartSeries.Select(s);
            _chartSeries.EmitSignal(OptionButton.SignalName.ItemSelected, s);
            var expected = rows.Select(r => YearChart.AllSeries[s].Bars.Select(b => b.Value(r)).ToArray()).ToList();
            var shown = _yearChart.Values();
            if (_yearChart.SeriesIndex != s || shown.Count != expected.Count || shown.Zip(expected).Any(p => !p.First.SequenceEqual(p.Second)))
                Fail($"series {YearChart.AllSeries[s].Name} does not show the ledger values");
        }

        // 選一個有事件的年份，用真的滑鼠事件點它的長條。冒煙在 _Ready 內同步執行、容器尚未排版，先給圖表 TEMP 尺寸。
        _yearChart.Size = new Vector2(560, 260);
        var target = rows.FirstOrDefault(r => _world.SettlementEvents().Any(e =>
            DemoEventFilter.DayOf(e) >= r.FirstDay && DemoEventFilter.DayOf(e) <= r.LastDay))
            ?? throw new InvalidOperationException("Year chart smoke: no year has events");
        var click = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = new Vector2(_yearChart.CenterXOf(target.Year), _yearChart.Size.Y / 2)
        };
        _yearChart._GuiInput(click);
        if (_yearChart.SelectedYear != target.Year || !_chartYearInfo.Text.Contains(target.Describe(), StringComparison.Ordinal)
            || _chartShowEvents.Disabled)
            Fail($"clicking year {target.Year} did not select it (selected {_yearChart.SelectedYear}, chart width {_yearChart.Size.X})");

        _chartShowEvents.EmitSignal(BaseButton.SignalName.Pressed);
        var filter = new DemoEventFilter(FromDay: target.FirstDay, ToDay: target.LastDay);
        if (_modalOpen is not null || CurrentHistoryFilter() != filter)
            Fail($"show events did not filter the history to year {target.Year}: {CurrentHistoryFilter()}");
        var expectedIds = ShownEvents(filter.Apply(_world.SettlementEvents())).Select(e => e.EventId).ToList();
        var listed = ListedEventIds();
        if (listed.Count == 0 || !listed.SequenceEqual(expectedIds))
            Fail($"history list differs from Core filter for year {target.Year} ({listed.Count} vs {expectedIds.Count})");

        _eventList.Select(0);
        _eventList.EmitSignal(ItemList.SignalName.ItemSelected, 0);
        var e = _causal!.Find(listed[0])!;
        var place = _world.Sites.Settlements.Single(s => s.Id == e.SettlementId);
        if (_selectedEventId != e.EventId || _hexMap.Highlight is not { } h || h.Column != place.Column || h.Row != place.Row)
            Fail("clicking an event of the year did not open its detail and focus the map");
        if (_world.ExportTempSave() != coreBefore)
            Fail("the chart changed the Core world");

        GD.Print($"WORLD_SIM_CHART_OK: Years={rows.Count} Complete=2 CliEqual=1 Series={YearChart.AllSeries.Count} " +
            $"ClickYear={target.Year} Events={listed.Count} Focus=1 CoreUnchanged=1");
        ResetHistoryFilter();
        RefreshEventList();
    }
}
