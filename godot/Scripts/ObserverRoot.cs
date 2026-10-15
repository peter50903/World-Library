using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// Real Godot observer GUI backed by the V0-01 in-memory DemoObserverWorld.
/// Draws only DEMO hex map markers; never pretends that those hexes, regions,
/// settlements or populations were loaded from official world parameters.
/// </summary>
public partial class ObserverRoot : Control
{
    private const ulong DemoSeed = 42;
    // V0-04B: CI can opt into the TEMP famine scenario to see hunger / starvation events
    // in the real Godot UI. Normal players never set this flag.
    private static readonly bool FamineSmoke = OS.HasEnvironment("WORLD_SIM_FAMINE_SMOKE");
    // V0-06A: CI can opt into the TEMP local-famine scenario (only settlement #1 has no food)
    // to see starving people leave for a fed settlement cell by cell. Normal players never set it.
    private static readonly bool MigrationSmoke = OS.HasEnvironment("WORLD_SIM_MIGRATION_SMOKE");
    // #61: news now needs time to travel, so seed 42 may no longer migrate within the smoke window.
    // The CI-only migration smoke picks the first TEMP seed (42, then 1..30) whose Core world has a
    // batch on the road within 70 days; normal players always get DemoSeed.
    // V0-06B: CI can opt into the TEMP frontier scenario (every starting settlement has no food; migrants
    // may found a new settlement on an empty cell). The smoke picks the first TEMP seed (42, then 1..30)
    // whose Core world founds a settlement within SettleSmokeDays days. Normal players never set it.
    private static readonly bool SettleSmoke = OS.HasEnvironment("WORLD_SIM_SETTLE_SMOKE");
    // #64: true Godot mouse-input smoke, only enabled in CI.
    private static readonly bool MapInputSmoke = OS.HasEnvironment("WORLD_SIM_MAP_INPUT_SMOKE");
    private const int SettleSmokeDays = 30 * 7;
    private const int MigrationSmokeDays = 70;
    // #81: CI-only smoke for the player-visible TEMP scenario picker. Normal players never set it.
    private static readonly bool ScenarioSmoke = OS.HasEnvironment("WORLD_SIM_SCENARIO_SMOKE");
    // #87: CI-only smoke for play / pause. Normal players never set it.
    private static readonly bool PlaybackSmoke = OS.HasEnvironment("WORLD_SIM_PLAYBACK_SMOKE");

    /// <summary>
    /// #81：玩家可在畫面上選的四種既有 TEMP 生存情境（順序＝下拉選單順序）。
    /// 只對應 Core 既有的 <see cref="DemoWeeklyTempConfig"/> 示範設定，不是正式規則。
    /// </summary>
    private enum TempScenario { Normal = 0, Famine = 1, LocalFamine = 2, Frontier = 3, Foraging = 4, CrowdedForaging = 5, LivingForaging = 6 }

    // Keep legacy CI fixture expectations untouched, but make the new human-only
    // forest/river food world the default for real F5 players.
    private static readonly bool LegacyFixtureSmoke =
        OS.HasEnvironment("WORLD_SIM_UI_SMOKE")
        || OS.HasEnvironment("WORLD_SIM_SAVE_SMOKE")
        || OS.HasEnvironment("WORLD_SIM_MAP_INPUT_SMOKE")
        || OS.HasEnvironment("WORLD_SIM_SCENARIO_SMOKE")
        || OS.HasEnvironment("WORLD_SIM_PLAYBACK_SMOKE")
        || OS.HasEnvironment("WORLD_SIM_EVENT_DETAIL_SMOKE")
        || OS.HasEnvironment("WORLD_SIM_ALERT_SMOKE")
        || OS.HasEnvironment("WORLD_SIM_HISTORY_FILTER_SMOKE")
        || OS.HasEnvironment("WORLD_SIM_FOOD_SMOKE");

    private static readonly TempScenario InitialScenario = SettleSmoke ? TempScenario.Frontier
        : MigrationSmoke ? TempScenario.LocalFamine
        : FamineSmoke ? TempScenario.Famine
        : LegacyFixtureSmoke ? TempScenario.Normal : TempScenario.LivingForaging;
    // TEMP: local famine / frontier reuse the existing demo seed search (42, then 1..30) so that the
    // migration / founding is reproducibly visible; the actual seed is always shown on screen.
    private static readonly Lazy<ulong> MigrationDemoSeed = new(PickMigrationSmokeSeed);
    private static readonly Lazy<ulong> SettleDemoSeed = new(PickSettleSmokeSeed);
    private static readonly ulong WorldSeed = SeedFor(InitialScenario);
    private DemoObserverWorld _world = CreateTempWorld(InitialScenario);

    private static ulong SeedFor(TempScenario scenario) => scenario switch
    {
        TempScenario.LocalFamine => MigrationDemoSeed.Value,
        TempScenario.Frontier => SettleDemoSeed.Value,
        _ => DemoSeed
    };

    private static DemoWeeklyTempConfig ConfigFor(TempScenario scenario) => scenario switch
    {
        TempScenario.Famine => DemoWeeklyTempConfig.FamineSample,
        TempScenario.LocalFamine => DemoWeeklyTempConfig.LocalFamineSample,
        TempScenario.Frontier => DemoWeeklyTempConfig.FrontierSample,
        TempScenario.Foraging => DemoWeeklyTempConfig.ForagingSample,
        TempScenario.CrowdedForaging => DemoWeeklyTempConfig.CrowdedForagingSample,
        TempScenario.LivingForaging => DemoWeeklyTempConfig.LivingForagingSample,
        _ => DemoWeeklyTempConfig.Sample
    };

    /// <summary>每次都建立全新的 Core 示範世界；Godot 不自行計算人口、食物或路線。</summary>
    private static DemoObserverWorld CreateTempWorld(TempScenario scenario)
        => new(DemoScenario.Sample(SeedFor(scenario)), ConfigFor(scenario));

    private static string PickerLabel(TempScenario scenario) => scenario switch
    {
        TempScenario.Famine => "全面饑荒（TEMP）",
        TempScenario.LocalFamine => "局部饑荒｜觀察遷徙（TEMP）",
        TempScenario.Frontier => "開拓｜觀察新聚落（TEMP）",
        TempScenario.Foraging => "森林／河流採集生存（預設，TEMP）",
        TempScenario.CrowdedForaging => "森林周邊人口過密｜觀察缺糧（TEMP）",
        TempScenario.LivingForaging => "人類繁衍＋自然死亡（預設，TEMP）",
        _ => "舊版一般（TEMP，舊農田測試）"
    };

    /// <summary>由 Core 的情境名稱反查下拉選單位置（讀檔後同步顯示）；未知情境回傳 null。</summary>
    private static TempScenario? ScenarioOf(DemoObserverWorld world) => world.TempScenarioDisplayName switch
    {
        "一般示範" => TempScenario.Normal,
        "全面饑荒" => TempScenario.Famine,
        "局部饑荒" => TempScenario.LocalFamine,
        "開拓" => TempScenario.Frontier,
        "森林河流生存" => TempScenario.Foraging,
        "森林周邊人口過密" => TempScenario.CrowdedForaging,
        "人類繁衍與自然死亡" => TempScenario.LivingForaging,
        _ => null
    };

    private static ulong PickSettleSmokeSeed()
    {
        foreach (var seed in new[] { DemoSeed }.Concat(Enumerable.Range(1, 30).Select(i => (ulong)i)))
        {
            var probe = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.FrontierSample);
            for (var day = 0; day < SettleSmokeDays && !probe.Sites.Settlements.Any(s => s.IsFounded); day++)
                probe.AdvanceOneDay();
            if (probe.Sites.Settlements.Any(s => s.IsFounded))
                return seed;
        }

        throw new InvalidOperationException("No TEMP seed founded a settlement for the settle smoke.");
    }
    private static ulong PickMigrationSmokeSeed()
    {
        foreach (var seed in new[] { DemoSeed }.Concat(Enumerable.Range(1, 30).Select(i => (ulong)i)))
        {
            var probe = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LocalFamineSample);
            for (var day = 0; day < MigrationSmokeDays && probe.MigrationsInTransit().Count == 0; day++)
                probe.AdvanceOneDay();
            if (probe.MigrationsInTransit().Count > 0)
                return seed;
        }

        throw new InvalidOperationException("No TEMP seed produced a migrant batch for the migration smoke.");
    }

    private DemoHexMap _hexMap = null!;
    private Label _cellInfo = null!;

    private Label _status = null!;
    private Label _notice = null!;
    private Label[] _slotLabels = null!;
    private RichTextLabel _history = null!;
    private RichTextLabel _ledger = null!;
    // #81: TEMP scenario picker + explicit confirmation before discarding an advanced world.
    private OptionButton _scenarioPicker = null!;
    private Button _startScenarioButton = null!;
    private HBoxContainer _confirmRow = null!;
    private Button _confirmScenarioButton = null!;
    private Button _cancelScenarioButton = null!;
    private TempScenario? _pendingScenario;
    // #87: play / pause. The clock only decides HOW MANY days to advance; Core advances them one by one.
    private readonly PlaybackClock _playback = new();
    private Button _playButton = null!;
    private OptionButton _speedPicker = null!;

    public override void _Ready()
    {
        var layout = new VBoxContainer();
        AddChild(layout);
        layout.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        layout.AddThemeConstantOverride("separation", 12);

        var heading = new VBoxContainer();
        heading.AddThemeConstantOverride("separation", 4);
        layout.AddChild(heading);

        var title = new Label
        {
            Text = "世界模擬遊戲  ·  觀察者模式",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 25);
        heading.AddChild(title);

        var disclaimer = new Label
        {
            Text = "【TEMP 示範】地形、聚落位置及人口／食物是演示資料，並非正式世界設定。",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        disclaimer.AddThemeColorOverride("font_color", new Color(1f, 0.80f, 0.48f));
        heading.AddChild(disclaimer);
        CreateControlsWindow(heading);

        var middle = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        middle.AddThemeConstantOverride("separation", 14);
        layout.AddChild(middle);

        var mapPanel = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        middle.AddChild(mapPanel);
        // V0-03B: inject the map and sites already generated by the observer world's
        // single RngRegistry; Godot never generates its own map.
        _hexMap = new DemoHexMap { World = _world.Map, Sites = _world.Sites,
            MainRiverTiles = _world.UsesSharedForaging ? DemoForagingWorld.MainRiverTiles(_world.Map) : Array.Empty<DemoCellRef>() };
        _hexMap.CellSelected += OnCellSelected;
        mapPanel.AddChild(_hexMap);

        // #119 separate resizable Godot Window, not an inline map-stealing sidebar.
        var sidebar = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _controlsWindow.AddChild(sidebar);
        sidebar.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        // V0-07: the added save/load buttons must remain reachable on smaller windows.
        // Scroll only the information sidebar; map wheel input stays within the map.
        var sidebarScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        sidebar.AddChild(sidebarScroll);
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        side.AddThemeConstantOverride("separation", 18);
        sidebarScroll.AddChild(side);

        var worldHeading = new Label { Text = "示範世界狀態" };
        worldHeading.AddThemeFontSizeOverride("font_size", 20);
        side.AddChild(worldHeading);

        _status = new Label();
        side.AddChild(_status);

        // #81: choose one of the existing TEMP survival scenarios and start a NEW demo world.
        var scenarioHeading = new Label { Text = "TEMP 示範情境（切換＝建立新的示範世界）" };
        scenarioHeading.AddThemeFontSizeOverride("font_size", 18);
        side.AddChild(scenarioHeading);
        _scenarioPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var scenario in Enum.GetValues<TempScenario>())
            _scenarioPicker.AddItem(PickerLabel(scenario), (int)scenario);
        _scenarioPicker.ItemSelected += OnScenarioPicked;
        side.AddChild(_scenarioPicker);
        _startScenarioButton = new Button
        {
            Text = "開始新的 TEMP 示範",
            CustomMinimumSize = new Vector2(0, 40)
        };
        _startScenarioButton.Pressed += RequestNewScenario;
        side.AddChild(_startScenarioButton);
        _confirmRow = new HBoxContainer { Visible = false };
        side.AddChild(_confirmRow);
        _confirmScenarioButton = new Button
        {
            Text = "確認切換（捨棄目前進度）",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _confirmScenarioButton.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.45f));
        _confirmScenarioButton.Pressed += ConfirmNewScenario;
        _confirmRow.AddChild(_confirmScenarioButton);
        _cancelScenarioButton = new Button { Text = "取消" };
        _cancelScenarioButton.Pressed += CancelNewScenario;
        _confirmRow.AddChild(_cancelScenarioButton);
        side.AddChild(new Label
        {
            Text = "局部饑荒／開拓沿用既有示範種子搜尋（42，再 1～30），選第一個會在示範期間發生遷徙／建立新聚落的種子；實際種子顯示在上方。存檔槽不會因切換被刪除或覆寫。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
        SyncScenarioPicker();

        // V0-04A: weekly settlement ledger computed by Core; Godot only displays it.
        var ledgerHeading = new Label { Text = "本週帳目（TEMP，每 7 日結算）" };
        ledgerHeading.AddThemeFontSizeOverride("font_size", 18);
        side.AddChild(ledgerHeading);

        _ledger = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = true,
            CustomMinimumSize = new Vector2(0, 200),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        side.AddChild(_ledger);

        _cellInfo = new Label
        {
            Text = "TEMP 六角格：請點選地圖中的一格或金色聚落查看其資料。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        side.AddChild(_cellInfo);
        BuildFoodDiagnostics(side); // #104：糧食供需診斷（只讀 Core）。

        side.AddChild(new Label
        {
            Text = "● 綠色：未開墾草原   ● 深綠：森林\n● 藍色：一般水域   ● 青色：主河道（採集情境）\n● 金色：示範聚落   ● 紅色：遷徙隊伍（每週走 1 格）",
        });
        side.AddChild(new Label
        {
            Text = "地圖操作：滾輪放大／縮小，按住滑鼠中鍵拖曳，左鍵點格查詢（TEMP）。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });

        var nextButton = new Button
        {
            Text = "下一天",
            CustomMinimumSize = new Vector2(0, 52)
        };
        nextButton.Pressed += () => Advance(1);
        side.AddChild(nextButton);

        var skipButton = new Button
        {
            Text = "快轉 30 天",
            CustomMinimumSize = new Vector2(0, 52)
        };
        skipButton.Pressed += () => Advance(30);
        side.AddChild(skipButton);

        // #87: play / pause and a TEMP observer speed (days of world time per real second).
        var playRow = new HBoxContainer();
        side.AddChild(playRow);
        _playButton = new Button
        {
            Text = "▶ 播放",
            CustomMinimumSize = new Vector2(0, 52),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        _playButton.Pressed += TogglePlayback;
        playRow.AddChild(_playButton);
        _speedPicker = new OptionButton();
        foreach (var speed in PlaybackClock.TempDaysPerSecondOptions)
            _speedPicker.AddItem($"每秒 {speed} 天（TEMP）");
        _speedPicker.ItemSelected += OnSpeedPicked;
        playRow.AddChild(_speedPicker);
        // #95: major-event alert + auto-pause during playback (TEMP major kinds).
        BuildMajorEventAlert(side);

        // V0-07C: three TEMP save slots; each row shows only the slot's header (day, seed, scenario).
        var slotHeading = new Label { Text = $"TEMP 存檔槽（{DemoSaveSlots.TempSlotCount} 槽，非正式存檔）" };
        slotHeading.AddThemeFontSizeOverride("font_size", 18);
        side.AddChild(slotHeading);
        var saveButtons = new Button[DemoSaveSlots.TempSlotCount + 1];
        var loadButtons = new Button[DemoSaveSlots.TempSlotCount + 1];
        _slotLabels = new Label[DemoSaveSlots.TempSlotCount + 1];
        for (var slot = 1; slot <= DemoSaveSlots.TempSlotCount; slot++)
        {
            var row = new HBoxContainer();
            side.AddChild(row);
            var label = new Label
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            row.AddChild(label);
            _slotLabels[slot] = label;
            var number = slot;
            saveButtons[slot] = new Button { Text = "存" };
            saveButtons[slot].Pressed += () => SaveWorld(number);
            row.AddChild(saveButtons[slot]);
            loadButtons[slot] = new Button { Text = "讀" };
            loadButtons[slot].Pressed += () => LoadWorld(number);
            row.AddChild(loadButtons[slot]);
        }
        RefreshSlots();

        _notice = new Label
        {
            Text = "世界只會在按下按鈕或按「▶ 播放」時推進；存檔、讀檔、切換情境會自動暫停。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        side.AddChild(_notice);

        var historyPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, 190)
        };
        layout.AddChild(historyPanel);

        var historyColumn = new VBoxContainer();
        historyPanel.AddChild(historyColumn);

        var historyTitle = new Label { Text = "結算歷史｜最近 12 筆日結算＋最近 10 筆聚落事件（飢餓／餓死／解除／外移／抵達／建立新聚落）" };
        historyTitle.AddThemeFontSizeOverride("font_size", 18);
        historyColumn.AddChild(historyTitle);

        _history = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 145)
        };
        // #89: text history on the left, clickable settlement events and their detail on the right.
        var historyRow = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        historyRow.AddThemeConstantOverride("separation", 12);
        historyColumn.AddChild(historyRow);
        _history.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        historyRow.AddChild(_history);
        BuildEventDetailPanel(historyRow);

        Refresh();
        if (OS.HasEnvironment("WORLD_SIM_SAVE_SMOKE"))
            VerifySaveSlotSmoke(nextButton, saveButtons, loadButtons);
        if (MapInputSmoke)
            VerifyMapInputSmoke();
        if (ScenarioSmoke)
            VerifyScenarioSwitchSmoke(nextButton, skipButton, saveButtons, loadButtons);
        if (EventDetailSmoke)
            VerifyEventDetailSmoke(nextButton);
        if (PlaybackSmoke)
            VerifyPlaybackSmoke(nextButton, saveButtons, loadButtons);
        if (AlertSmoke)
            VerifyAlertSmoke();
        if (HistoryFilterSmoke)
            VerifyHistoryFilterSmoke(nextButton, saveButtons, loadButtons);
        if (FoodSmoke)
            VerifyFoodSmoke(nextButton, loadButtons);
        if (OS.HasEnvironment("WORLD_SIM_WINDOW_SMOKE"))
            VerifyControlsWindowSmoke();
        if (OS.HasEnvironment("WORLD_SIM_HUMAN_SMOKE"))
            VerifyHumanLifecycleSmoke();
        if (ForagingSmoke)
            VerifyForagingSmoke(nextButton);

        // V0-06A CI-only smoke (separate Godot run): advance with the real button until Core has a
        // migrant batch on the road, then check the map markers, the caused history and the panel.
        if (MigrationSmoke)
        {
            for (var i = 0; i < 70 && _world.MigrationsInTransit().Count == 0; i++)
                nextButton.EmitSignal(BaseButton.SignalName.Pressed);

            var travelling = _world.MigrationsInTransit();
            var departure = _world.SettlementEvents().LastOrDefault(e => e.Kind == DemoSettlementEventKind.Emigration);
            if (travelling.Count == 0 || departure is null
                || _hexMap.Migrants.Count != travelling.Count
                || !_history.Text.Contains(departure.Describe(), StringComparison.Ordinal)
                || !_history.Text.Contains("因缺糧外移", StringComparison.Ordinal)
                || !_ledger.Text.Contains("遷出", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Godot did not show Core's migrant batch and emigration event.");
            }

            // #61: the status panel shows ONE population ledger (settled + in transit = world) and labels
            // the V0-01 daily demo population as a separate TEMP account.
            if (!_status.Text.Contains($"聚落人口合計：{_world.SettledPopulation}", StringComparison.Ordinal)
                || !_status.Text.Contains($"遷徙途中人口：{_world.PopulationInTransit}", StringComparison.Ordinal)
                || !_status.Text.Contains($"世界人口（聚落＋途中，各計一次）：{_world.WorldPopulation}", StringComparison.Ordinal)
                || !_status.Text.Contains("V0-01 每日示範帳（舊版獨立 TEMP 帳", StringComparison.Ordinal)
                || _world.WorldPopulation != _world.SettledPopulation + _world.PopulationInTransit)
            {
                throw new InvalidOperationException("Godot status panel mixed the population ledgers.");
            }

            var batch = travelling[0];
            _hexMap._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = _hexMap.CellCenter(_world.Map.GetCell(batch.Cell.Column, batch.Cell.Row))
            });
            if (!_cellInfo.Text.Contains($"遷徙隊伍 #{batch.Id}", StringComparison.Ordinal)
                || !_cellInfo.Text.Contains($"{batch.People} 人", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Godot cell panel did not show Core's migrant batch.");
            }

            GD.Print($"WORLD_SIM_MIGRATION_OK: Day={_world.Snapshot().DaysElapsed} Batches={travelling.Count} " +
                $"People={_world.PopulationInTransit} From={batch.FromSettlementId} To={batch.ToSettlementId}");
        }

        // V0-06B CI-only smoke (separate Godot run): advance with the real button until Core founds a
        // settlement, then check the new map marker, the caused history line and the clicked cell panel.
        if (SettleSmoke)
        {
            for (var i = 0; i < SettleSmokeDays && !_world.Sites.Settlements.Any(s => s.IsFounded); i++)
                nextButton.EmitSignal(BaseButton.SignalName.Pressed);

            var founded = _world.Sites.Settlements.FirstOrDefault(s => s.IsFounded);
            var foundedEvent = _world.SettlementEvents()
                .FirstOrDefault(e => e.Kind == DemoSettlementEventKind.SettlementFounded);
            if (founded is null || foundedEvent is null || foundedEvent.SettlementId != founded.Id
                || !ReferenceEquals(_hexMap.Sites, _world.Sites)
                || _hexMap.Sites?.SettlementAt(founded.Column, founded.Row) is null
                || !_history.Text.Contains(foundedEvent.Describe(), StringComparison.Ordinal)
                || !_history.Text.Contains("建立新聚落", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Godot did not show Core's newly founded settlement.");
            }

            _hexMap._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = _hexMap.CellCenter(_world.Map.GetCell(founded.Column, founded.Row))
            });
            var source = _world.Sites.Settlements.Single(s => s.Id == founded.FoundedFromSettlementId);
            // C04：新聚落 ID 由世界唯一分配器發號，必須大於開局全部 ID（開局 ID 為 1～開局下一號−1）。
            var openingMaxId = _world.Sites.Settlements.Where(s => !s.IsFounded).Select(s => (long)s.Id)
                .Concat(_world.Sites.ResourcePoints.Where(p => _world.Sites.Settlements.Single(s => s.Id == p.SettlementId).IsFounded == false).Select(p => (long)p.Id))
                .Concat(_world.Sites.PopGroups.Select(g => (long)g.Id)).Max();
            if (founded.Id <= openingMaxId || (ulong)founded.Id >= _world.EntityIdNextValue)
                throw new InvalidOperationException("Founded settlement entity ID is not newer than every opening ID.");
            if (!_cellInfo.Text.Contains($"聚落：{founded.Name}", StringComparison.Ordinal)
                || !_cellInfo.Text.Contains($"實體 ID {founded.Id}", StringComparison.Ordinal)
                || !_cellInfo.Text.Contains($"來源聚落：{source.Name}", StringComparison.Ordinal)
                || !_cellInfo.Text.Contains($"目前人口：{_world.SettlementPopulation(founded.Id)}", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Godot cell panel did not show the founded settlement.");
            }

            GD.Print($"WORLD_SIM_SETTLE_OK: Seed={WorldSeed} Day={_world.Snapshot().DaysElapsed} Id={founded.Id} " +
                $"From={founded.FoundedFromSettlementId} Founders={foundedEvent.Count} Week={founded.FoundedWeek}");
        }

        // CI-only, opt-in engine smoke: exercise the actual Godot Button.Pressed
        // signals and confirm both the Core state and visible history updated.
        // Normal users never set this flag; there is no auto-ticking in the game.
        if (OS.HasEnvironment("WORLD_SIM_UI_SMOKE"))
        {
            nextButton.EmitSignal(BaseButton.SignalName.Pressed);
            skipButton.EmitSignal(BaseButton.SignalName.Pressed);

            var state = _world.Snapshot();
            if (state.DaysElapsed != 31 || _world.History().Count != 31 ||
                !_history.Text.Contains("第 31 天", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Godot button signal did not update world and history.");
            }

            GD.Print("WORLD_SIM_UI_SMOKE_OK: Day=31 History=31");

            // V0-04A: after 31 days the panel shows Core's 4th weekly ledger, all balanced.
            var latest = _world.LatestWeek;
            if (latest is null || latest.Week != 4 || _world.WeekReports().Count != 4
                || !_ledger.Text.Contains("第 4 週帳目", StringComparison.Ordinal)
                || !_ledger.Text.Contains(latest.Settlements[0].SettlementName, StringComparison.Ordinal)
                || !latest.Settlements.All(l => l.IsBalanced)
                || _ledger.Text.Contains("不平衡", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Godot weekly ledger panel did not show Core's balanced ledger.");
            }
            GD.Print($"WORLD_SIM_WEEKLY_LEDGER_OK: Weeks={_world.WeekReports().Count} Settlements={latest.Settlements.Count}");

            // Godot runs the same input handler used by real mouse clicks;
            // selected terrain must be from Core's immutable demo cell.
            var cell = _world.Map.GetCell(2, 2);
            _hexMap._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = _hexMap.CellCenter(cell)
            });
            if (!_cellInfo.Text.Contains($"軸向座標 ({cell.AxialQ}, {cell.AxialR})", StringComparison.Ordinal)
                || !_cellInfo.Text.Contains(TerrainName(cell.Terrain), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Godot selection did not display Core's actual TEMP terrain data.");
            }
            GD.Print($"WORLD_SIM_HEX_CLICK_OK: Col={cell.Column} Row={cell.Row}");

            // V0-03B: clicking a settlement's cell shows Core's TEMP settlement,
            // resource point and population group.
            var settlement = _world.Sites.Settlements[0];
            var pop = _world.Sites.PopGroupsOf(settlement.Id)[0];
            var resource = _world.Sites.ResourcePointsOf(settlement.Id)[0];
            _hexMap._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = _hexMap.CellCenter(_world.Map.GetCell(settlement.Column, settlement.Row))
            });
            if (!_cellInfo.Text.Contains(settlement.Name, StringComparison.Ordinal)
                || !_cellInfo.Text.Contains($"人口群體 #{pop.Id}：初始 {pop.Size} 人", StringComparison.Ordinal)
                || !_cellInfo.Text.Contains($"目前人口：{_world.SettlementPopulation(settlement.Id)}", StringComparison.Ordinal)
                || !_cellInfo.Text.Contains("飢餓狀態：", StringComparison.Ordinal)
                || !_cellInfo.Text.Contains(ResourceName(resource.Kind), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Godot selection did not display Core's TEMP settlement data.");
            }
            GD.Print($"WORLD_SIM_SETTLEMENT_CLICK_OK: Settlements={_world.Sites.Settlements.Count} Id={settlement.Id}");

            // V0-04B: in the famine scenario the visible history must list Core's caused
            // hunger and starvation events, and the settlement panel must show starvation.
            if (FamineSmoke)
            {
                var events = _world.SettlementEvents();
                var hunger = events.Count(e => e.Kind == DemoSettlementEventKind.Hunger);
                var deaths = events.Where(e => e.Kind == DemoSettlementEventKind.StarvationDeath).Sum(e => e.Count);
                var anyDeath = events.LastOrDefault(e => e.Kind == DemoSettlementEventKind.StarvationDeath);
                if (hunger == 0 || deaths == 0 || anyDeath is null
                    || !_history.Text.Contains("飢餓：缺糧", StringComparison.Ordinal)
                    || !_history.Text.Contains(anyDeath.Describe(), StringComparison.Ordinal)
                    || !_ledger.Text.Contains("死亡", StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Godot history did not show Core's hunger / starvation events.");
                }

                var starving = _world.Sites.Settlements.FirstOrDefault(s => _world.IsStarving(s.Id));
                if (starving is not null)
                {
                    _hexMap._GuiInput(new InputEventMouseButton
                    {
                        ButtonIndex = MouseButton.Left,
                        Pressed = true,
                        Position = _hexMap.CellCenter(_world.Map.GetCell(starving.Column, starving.Row))
                    });
                    if (!_cellInfo.Text.Contains("飢餓狀態：飢餓中", StringComparison.Ordinal))
                        throw new InvalidOperationException("Godot settlement panel did not show starvation.");
                }

                GD.Print($"WORLD_SIM_FAMINE_OK: HungerEvents={hunger} Deaths={deaths}");
            }
        }
    }

    // #64: exercise the real Godot Control._GuiInput path, not a reimplementation of its math.
    private void VerifyMapInputSmoke()
    {
        var cell = _world.Map.GetCell(2, 2);
        var neighbor = _world.Map.GetCell(3, 2);
        var dayBefore = _world.Snapshot().DaysElapsed;
        var rngBefore = _world.Snapshot().ResourceRngDraws;
        var peopleBefore = _world.WorldPopulation;
        var foodBefore = _world.Snapshot().FoodStock;
        var sitesBefore = _world.Sites;
        var anchor = _hexMap.CellCenter(cell);
        var baselineSpan = anchor.DistanceTo(_hexMap.CellCenter(neighbor));

        _hexMap._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelUp, Pressed = true, Position = anchor
        });
        var zoomed = _hexMap.CellCenter(cell);
        if (zoomed.DistanceTo(anchor) > 0.1f
            || _hexMap.CellCenter(neighbor).DistanceTo(zoomed) <= baselineSpan * 1.1f)
            throw new InvalidOperationException("Map wheel-up did not zoom around the mouse position.");

        _hexMap._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.WheelDown, Pressed = true, Position = anchor
        });
        if (_hexMap.CellCenter(cell).DistanceTo(anchor) > 0.1f
            || Mathf.Abs(_hexMap.CellCenter(neighbor).DistanceTo(anchor) - baselineSpan) > 0.1f)
            throw new InvalidOperationException("Map wheel-down did not restore the initial view.");

        var dragDelta = new Vector2(44f, -18f);
        _hexMap._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Middle, Pressed = true, Position = anchor
        });
        _hexMap._GuiInput(new InputEventMouseMotion
        {
            ButtonMask = MouseButtonMask.Middle, Position = anchor + dragDelta, Relative = dragDelta
        });
        var afterDrag = _hexMap.CellCenter(cell);
        if (afterDrag.DistanceTo(anchor + dragDelta) > 0.1f)
            throw new InvalidOperationException("Map middle-button drag did not pan the view.");

        _hexMap._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Middle, Pressed = false, Position = afterDrag
        });
        _hexMap._GuiInput(new InputEventMouseMotion
        {
            ButtonMask = (MouseButtonMask)0, Position = afterDrag + dragDelta, Relative = dragDelta
        });
        if (_hexMap.CellCenter(cell).DistanceTo(afterDrag) > 0.1f)
            throw new InvalidOperationException("Map continued dragging after middle-button release.");

        // Verify hit testing uses the *transformed* Core cell, not its former screen coordinates.
        _hexMap._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true, Position = afterDrag
        });
        if (!_cellInfo.Text.Contains($"偏移座標 ({cell.Column}, {cell.Row})", StringComparison.Ordinal))
            throw new InvalidOperationException("Map click after panning selected the wrong Core cell.");

        // Between adjacent visible hexes is a visual gap, never an invisible selectable cell.
        _cellInfo.Text = "MAP_GAP_UNSELECTED";
        var seam = (_hexMap.CellCenter(cell) + _hexMap.CellCenter(neighbor)) * 0.5f;
        _hexMap._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left, Pressed = true, Position = seam
        });
        if (_cellInfo.Text != "MAP_GAP_UNSELECTED")
            throw new InvalidOperationException("Map selected an invisible hex gap.");

        if (_world.Snapshot().DaysElapsed != dayBefore
            || _world.Snapshot().ResourceRngDraws != rngBefore
            || _world.Snapshot().FoodStock != foodBefore
            || _world.WorldPopulation != peopleBefore
            || !ReferenceEquals(_world.Sites, sitesBefore))
            throw new InvalidOperationException("Map viewport inputs changed the simulated world.");

        GD.Print($"WORLD_SIM_MAP_INPUT_OK: Zoom=1.20 Pan=44 Day={dayBefore}");
    }

    private void SaveWorld(int slot)
    {
        PausePlayback();
        try
        {
            DemoSaveSlots.Save(_world, slot);
            _notice.Text = $"TEMP 存檔槽 {slot} 存檔成功：{DemoSaveSlots.PathFor(slot)}";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            _notice.Text = $"TEMP 存檔槽 {slot} 存檔失敗，世界未改變：{ex.Message}";
        }
        RefreshSlots();
    }

    private void LoadWorld(int slot)
    {
        PausePlayback();
        try
        {
            var restored = DemoSaveSlots.Load(slot);
            // New fully validated world; never mutate the old one on failure. The save keeps its OWN
            // scenario (#81): the picker follows the loaded world, never the other way round.
            InstallWorld(restored, "TEMP 世界讀取完成；請點選地圖格或聚落查看資料。");
            _notice.Text = $"TEMP 存檔槽 {slot} 讀檔成功：第 {restored.Snapshot().DaysElapsed} 日";
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            _notice.Text = $"TEMP 存檔槽 {slot} 讀檔失敗，世界未改變：{ex.Message}";
        }
        RefreshSlots();
    }

    // ---------- #81: TEMP scenario switching (always a NEW Core demo world) ----------

    private void OnScenarioPicked(long index)
    {
        // Picking alone never changes the world; it only cancels a pending, different confirmation.
        if (_pendingScenario is not null)
        {
            _pendingScenario = null;
            _confirmRow.Visible = false;
        }
        _notice.Text = $"已選擇「{PickerLabel((TempScenario)index)}」。按「開始新的 TEMP 示範」才會建立新世界；" +
            $"目前世界（{_world.TempScenarioDisplayName}，第 {_world.Snapshot().DaysElapsed} 日）未改變。";
    }

    private void RequestNewScenario()
    {
        PausePlayback();
        var target = (TempScenario)_scenarioPicker.Selected;
        var day = _world.Snapshot().DaysElapsed;
        if (day == 0)
        {
            StartScenario(target); // Nothing has been advanced yet, so nothing can be lost.
            return;
        }

        _pendingScenario = target;
        _confirmRow.Visible = true;
        _notice.Text = $"⚠ 目前世界（{_world.TempScenarioDisplayName}）已推進到第 {day} 日。" +
            $"切換到「{PickerLabel(target)}」會建立新的第 0 日 TEMP 示範世界，目前未存檔的進度將從畫面捨棄；" +
            "三個存檔槽不會被刪除或覆寫。請按「確認切換」或「取消」。";
    }

    private void ConfirmNewScenario()
    {
        if (_pendingScenario is not { } target)
            return;
        StartScenario(target);
    }

    private void CancelNewScenario()
    {
        _pendingScenario = null;
        _confirmRow.Visible = false;
        SyncScenarioPicker();
        _notice.Text = $"已取消切換，世界未改變（{_world.TempScenarioDisplayName}，第 {_world.Snapshot().DaysElapsed} 日）。";
    }

    private void StartScenario(TempScenario target)
    {
        DemoObserverWorld fresh;
        try
        {
            fresh = CreateTempWorld(target);
        }
        catch (InvalidOperationException ex)
        {
            _notice.Text = $"無法建立 TEMP 示範世界，目前世界未改變：{ex.Message}";
            return;
        }

        InstallWorld(fresh, "TEMP 新示範世界已建立；請點選地圖格或聚落查看資料。");
        _notice.Text = $"已建立新的 TEMP 示範世界：{PickerLabel(target)}｜實際種子 {fresh.TempWorldSeed}｜第 0 日。" +
            (fresh.TempWorldSeed == DemoSeed ? "" : "（此情境用既有示範種子搜尋，非預設種子 42）");
    }

    /// <summary>Replaces the displayed world and redraws everything from it (no residue of the old one).</summary>
    private void InstallWorld(DemoObserverWorld world, string cellInfoText)
    {
        PausePlayback();
        _world = world;
        ResetMajorEventAlert(); // #95: no stale alert from the previous world.
        _pendingScenario = null;
        _confirmRow.Visible = false;
        _hexMap.World = world.Map;
        _hexMap.Sites = world.Sites;
        _hexMap.MainRiverTiles = world.UsesSharedForaging
            ? DemoForagingWorld.MainRiverTiles(world.Map) : Array.Empty<DemoCellRef>();
        _hexMap.Migrants = world.MigrationsInTransit();
        _hexMap.FoodReachCells = Array.Empty<DemoCellRef>();
        _cellInfo.Text = cellInfoText;
        _foodSettlementId = 0; // #104：換世界不殘留前一個世界的聚落選取。
        SyncScenarioPicker();
        Refresh();
        RefreshSlots();
    }

    private void SyncScenarioPicker()
    {
        if (ScenarioOf(_world) is { } scenario)
            _scenarioPicker.Select((int)scenario);
    }

    private void PickScenario(TempScenario scenario)
    {
        _scenarioPicker.Select((int)scenario);
        _scenarioPicker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)scenario);
    }

    /// <summary>
    /// #81 CI smoke with the real Godot controls: the picker plus "start" never change an advanced world
    /// without confirmation; each confirmed switch rebuilds a day-0 world identical to a fresh Core world
    /// (no residue), shows its scenario and actual seed, and then shows migration / founding / famine.
    /// Save slots are never touched by switching; loading a slot restores the save's own scenario.
    /// </summary>
    private void VerifyScenarioSwitchSmoke(Button nextButton, Button skipButton, Button[] saveButtons, Button[] loadButtons)
    {
        void Fail(string why) => throw new InvalidOperationException("Scenario smoke: " + why);
        void Press(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);

        void CheckFresh(TempScenario scenario)
        {
            var expected = CreateTempWorld(scenario);
            if (_world.ExportTempSave() != expected.ExportTempSave() || _world.Snapshot().DaysElapsed != 0)
                Fail($"{scenario} was not rebuilt as a fresh day-0 Core world.");
            if (!_status.Text.Contains($"目前情境：【TEMP 示範】{expected.TempScenarioDisplayName}", StringComparison.Ordinal)
                || !_status.Text.Contains($"實際種子：{expected.TempWorldSeed}", StringComparison.Ordinal)
                || !_status.Text.Contains("世界日期：第 0 日", StringComparison.Ordinal)
                || _scenarioPicker.Selected != (int)scenario || _confirmRow.Visible)
                Fail($"{scenario} labels / picker not refreshed.");
            if (_history.Text != "目前沒有已結算事件。請按「下一天」開始。"
                || !_ledger.Text.StartsWith("尚未到第一次週結算", StringComparison.Ordinal)
                || _hexMap.Migrants.Count != 0
                || !ReferenceEquals(_hexMap.Sites, _world.Sites) || !ReferenceEquals(_hexMap.World, _world.Map)
                || !_cellInfo.Text.StartsWith("TEMP 新示範世界", StringComparison.Ordinal))
                Fail($"{scenario} shows residue of the previous world.");
        }

        void CheckSameAsCore(TempScenario scenario)
        {
            var replay = CreateTempWorld(scenario);
            for (var i = 0; i < _world.Snapshot().DaysElapsed; i++)
                replay.AdvanceOneDay();
            if (_world.ExportTempSave() != replay.ExportTempSave())
                Fail($"{scenario} UI world diverged from a direct Core run.");
        }

        void SwitchTo(TempScenario scenario)
        {
            PickScenario(scenario);
            Press(_startScenarioButton);
            if (_confirmRow.Visible)
                Press(_confirmScenarioButton);
            CheckFresh(scenario);
        }

        // 1. Default stays the normal TEMP scenario with seed 42.
        if (_world.TempScenarioDisplayName != "一般示範" || _world.TempWorldSeed != DemoSeed)
            Fail("default scenario is not the normal TEMP demo with seed 42.");

        // 2. Advance 7 days, save slot 1, then try to switch: no change until confirmed; cancel keeps it.
        for (var i = 0; i < 7; i++)
            Press(nextButton);
        var day7 = _world.ExportTempSave();
        Press(saveButtons[1]);
        var slot1 = System.IO.File.ReadAllBytes(DemoSaveSlots.PathFor(1));
        PickScenario(TempScenario.LocalFamine);
        if (_world.ExportTempSave() != day7)
            Fail("picking a scenario changed the world.");
        Press(_startScenarioButton);
        if (_world.ExportTempSave() != day7 || !_confirmRow.Visible
            || !_notice.Text.Contains("已推進到第 7 日", StringComparison.Ordinal))
            Fail("an advanced world was replaced without confirmation.");
        Press(_cancelScenarioButton);
        if (_world.ExportTempSave() != day7 || _confirmRow.Visible || _scenarioPicker.Selected != (int)TempScenario.Normal)
            Fail("cancel changed the world or left the confirmation open.");
        Press(_confirmScenarioButton); // Stale confirm after cancel must do nothing.
        if (_world.ExportTempSave() != day7)
            Fail("a stale confirmation replaced the world.");

        // 3. Local famine: confirmed switch → fresh day 0, then a migrant batch on the map.
        SwitchTo(TempScenario.LocalFamine);
        for (var i = 0; i < MigrationSmokeDays && _world.MigrationsInTransit().Count == 0; i++)
            Press(nextButton);
        if (_world.MigrationsInTransit().Count == 0 || _hexMap.Migrants.Count != _world.MigrationsInTransit().Count
            || !_history.Text.Contains("因缺糧外移", StringComparison.Ordinal))
            Fail("local famine did not show Core's migrant batch.");
        CheckSameAsCore(TempScenario.LocalFamine);
        var migrationDay = _world.Snapshot().DaysElapsed;

        // 4. Frontier: a founded settlement appears on the map.
        SwitchTo(TempScenario.Frontier);
        for (var i = 0; i < SettleSmokeDays && !_world.Sites.Settlements.Any(s => s.IsFounded); i++)
            Press(nextButton);
        var founded = _world.Sites.Settlements.FirstOrDefault(s => s.IsFounded);
        if (founded is null || _hexMap.Sites?.SettlementAt(founded.Column, founded.Row) is null
            || !_history.Text.Contains("建立新聚落", StringComparison.Ordinal))
            Fail("frontier did not show Core's founded settlement.");
        CheckSameAsCore(TempScenario.Frontier);
        var foundedDay = _world.Snapshot().DaysElapsed;

        // 5. Famine: caused hunger events after 30 days.
        SwitchTo(TempScenario.Famine);
        Press(skipButton);
        if (!_world.SettlementEvents().Any(e => e.Kind == DemoSettlementEventKind.Hunger)
            || !_history.Text.Contains("飢餓：缺糧", StringComparison.Ordinal))
            Fail("famine did not show Core's hunger events.");
        CheckSameAsCore(TempScenario.Famine);

        // 6. Loading slot 1 restores ITS scenario (normal, day 7), not the current famine one;
        //    an empty slot is still rejected without changing the world.
        Press(loadButtons[1]);
        if (_world.ExportTempSave() != day7 || _world.TempScenarioDisplayName != "一般示範"
            || _scenarioPicker.Selected != (int)TempScenario.Normal
            || !_status.Text.Contains("目前情境：【TEMP 示範】一般示範", StringComparison.Ordinal))
            Fail("loading slot 1 did not restore its own normal scenario.");
        var beforeEmpty = _world.ExportTempSave();
        Press(loadButtons[2]);
        if (_world.ExportTempSave() != beforeEmpty
            || !_notice.Text.StartsWith("TEMP 存檔槽 2 讀檔失敗", StringComparison.Ordinal))
            Fail("an empty slot was not rejected.");

        // 7. Back to normal: identical to a brand-new default world; slot 1 bytes never changed.
        SwitchTo(TempScenario.Normal);
        if (!System.IO.File.ReadAllBytes(DemoSaveSlots.PathFor(1)).AsSpan().SequenceEqual(slot1)
            || System.IO.File.Exists(DemoSaveSlots.PathFor(2)) || System.IO.File.Exists(DemoSaveSlots.PathFor(3)))
            Fail("switching scenarios touched a save slot.");

        GD.Print($"WORLD_SIM_SCENARIO_OK: Normal={DemoSeed} LocalFamine={SeedFor(TempScenario.LocalFamine)} " +
            $"MigrationDay={migrationDay} Frontier={SeedFor(TempScenario.Frontier)} FoundedDay={foundedDay} " +
            $"Famine={DemoSeed} ConfirmRequired=1 SlotsUnchanged=1");
    }

    private void RefreshSlots()
    {
        foreach (var info in DemoSaveSlots.ListAll())
            _slotLabels[info.Slot].Text = info.Describe();
    }

    /// <summary>
    /// V0-07C CI smoke (real Godot buttons): save slot 2 on day 9, advance, load slot 2 back to day 9,
    /// resume identically; a corrupt slot 3 is rejected with a visible reason and the world unchanged.
    /// </summary>
    private void VerifySaveSlotSmoke(Button nextButton, Button[] saveButtons, Button[] loadButtons)
    {
        for (var i = 0; i < 9; i++)
            nextButton.EmitSignal(BaseButton.SignalName.Pressed);
        var checkpoint = _world.ExportTempSave();
        saveButtons[2].EmitSignal(BaseButton.SignalName.Pressed);
        if (!_slotLabels[2].Text.StartsWith("槽 2：第 9 日", StringComparison.Ordinal))
            throw new InvalidOperationException("TEMP slot 2 label does not show the saved day.");
        nextButton.EmitSignal(BaseButton.SignalName.Pressed);
        if (_world.Snapshot().DaysElapsed != 10)
            throw new InvalidOperationException("TEMP save smoke failed to advance.");
        loadButtons[2].EmitSignal(BaseButton.SignalName.Pressed);
        if (_world.ExportTempSave() != checkpoint || _world.Snapshot().DaysElapsed != 9
            || !_status.Text.Contains("世界日期：第 9 日", StringComparison.Ordinal))
            throw new InvalidOperationException("TEMP Godot load did not restore all world state and UI.");
        var baseline = DemoObserverWorld.ImportTempSave(checkpoint);
        baseline.AdvanceOneDay();
        nextButton.EmitSignal(BaseButton.SignalName.Pressed);
        if (_world.ExportTempSave() != baseline.ExportTempSave())
            throw new InvalidOperationException("TEMP Godot resumed world diverged after load.");
        GD.Print("WORLD_SIM_SAVE_OK: SavedDay=9 RestoredDay=9 ResumedDay=10");

        System.IO.Directory.CreateDirectory(DemoSaveSlots.DefaultDirectory);
        System.IO.File.WriteAllText(DemoSaveSlots.PathFor(3), "{\"Version\":2,\"State\":");
        RefreshSlots();
        var before = _world.ExportTempSave();
        loadButtons[3].EmitSignal(BaseButton.SignalName.Pressed);
        if (_world.ExportTempSave() != before
            || !_notice.Text.StartsWith("TEMP 存檔槽 3 讀檔失敗", StringComparison.Ordinal)
            || !_slotLabels[3].Text.StartsWith("槽 3：無法辨識", StringComparison.Ordinal))
            throw new InvalidOperationException("TEMP corrupt slot 3 was not rejected visibly.");
        GD.Print("WORLD_SIM_SLOTS_OK: Slot=2 SavedDay=9 RestoredDay=9 CorruptSlot=3 Rejected=1");
    }

    private void OnCellSelected(DemoHexCell cell)
    {
        _cellInfo.Text =
            $"TEMP 六角格｜偏移座標 ({cell.Column}, {cell.Row})\n" +
            $"軸向座標 ({cell.AxialQ}, {cell.AxialR})\n" +
            $"核心地形：{TerrainName(cell.Terrain)}"
            + (_world.UsesSharedForaging && cell.Terrain == DemoTerrain.Grassland ? "（未開墾，農田糧食 0）" : "")
            + (_world.IsMainRiverCell(cell.Column, cell.Row) ? "（主河道，禁止定居，鄰格漁撈）" : "");
        _hexMap.FoodReachCells = Array.Empty<DemoCellRef>();
        if (_world.UsesSharedForaging)
        {
            var source = _world.SharedFoodSources().FirstOrDefault(s =>
                s.Column == cell.Column && s.Row == cell.Row);
            if (source is not null)
            {
                _hexMap.FoodReachCells = DemoHexFoodCatchment.EligibleCells(_world.Map, source)
                    .Select(c => new DemoCellRef(c.Column, c.Row)).ToArray();
                _cellInfo.Text += $"\n共享來源：每週供糧上限 {source.FoodAvailable}（全圈共用一次）；黃框為可採集範圍";
            }
        }
        _hexMap.QueueRedraw();

        // V0-06A: migrant batches standing on this cell (from Core; never moved by Godot).
        foreach (var batch in _world.MigrationsInTransit()
                     .Where(b => b.Cell.Column == cell.Column && b.Cell.Row == cell.Row))
        {
            _cellInfo.Text += $"\n遷徙隊伍 #{batch.Id}：{batch.People} 人，{batch.FromName}→{batch.ToName}，" +
                $"還有 {batch.StepsLeft} 格，攜帶糧食 {batch.Food}，飢餓債 {batch.HungerDebt}";
        }

        var settlement = _world.Sites.SettlementAt(cell.Column, cell.Row);
        SelectFoodSettlement(settlement?.Id ?? 0); // #104
        if (settlement is null)
            return;

        var lines = new List<string> { "", $"聚落：{settlement.Name}（實體 ID {settlement.Id}）" };
        if (settlement.IsFounded)
        {
            // V0-06B: a settlement founded by migrants (population came only from its founding batch).
            var source = _world.Sites.Settlements.FirstOrDefault(s => s.Id == settlement.FoundedFromSettlementId);
            lines.Add($"第 {settlement.FoundedWeek} 週由遷徙隊伍 #{settlement.FoundedByBatchId} 建立；" +
                $"來源聚落：{source?.Name ?? "未知"}（人口全部來自開拓隊，不憑空）");
        }
        foreach (var resource in _world.Sites.ResourcePointsOf(settlement.Id))
            lines.Add($"資源點 #{resource.Id}：{ResourceName(resource.Kind)}");
        foreach (var pop in _world.Sites.PopGroupsOf(settlement.Id))
            lines.Add($"人口群體 #{pop.Id}：初始 {pop.Size} 人（TEMP）");
        lines.Add($"目前人口：{_world.SettlementPopulation(settlement.Id)}");
        lines.Add($"糧食庫存：{_world.SettlementFood(settlement.Id)}");
        lines.Add(_world.IsStarving(settlement.Id)
            ? $"飢餓狀態：飢餓中（飢餓債 {_world.SettlementHungerDebt(settlement.Id)}，最近一週缺糧 {_world.LatestWeekShortage(settlement.Id)}）"
            : _world.SettlementPopulation(settlement.Id) == 0 ? "飢餓狀態：聚落已無人口" : "飢餓狀態：正常");
        _cellInfo.Text += string.Join("\n", lines);
    }

    private static string ResourceName(DemoResourceKind kind) => kind switch
    {
        DemoResourceKind.Farmland => "農地（TEMP）",
        DemoResourceKind.Woodland => "林地（TEMP）",
        DemoResourceKind.Fishery => "漁場（TEMP）",
        DemoResourceKind.Uncultivated => "未開墾草原（農業產量 0，TEMP）",
        _ => "未定義（TEMP）"
    };

    private static string TerrainName(DemoTerrain terrain) => terrain switch
    {
        DemoTerrain.Grassland => "草原（TEMP）",
        DemoTerrain.Forest => "森林（TEMP）",
        DemoTerrain.Water => "水域（TEMP）",
        _ => "未定義（TEMP）"
    };

    private static string LedgerText(DemoWeekReport? week)
    {
        if (week is null)
            return "尚未到第一次週結算（第 7 日）。請按「下一天」推進。";

        var lines = new List<string> { $"第 {week.Week} 週帳目（第 {(int)week.WorldTime.TotalDays} 日結算）" };
        foreach (var l in week.Settlements)
        {
            lines.Add("");
            lines.Add($"{l.SettlementName}");
            lines.Add($"  資源點存量：期初 {l.ReserveOpening} ＋再生 {l.Regenerated} －採收 {l.Harvested} ＝期末 {l.ReserveClosing}");
            lines.Add($"  糧食：期初 {l.FoodOpening} ＋生產 {l.FoodProduced} ＋隊伍帶入 {l.FoodCarriedIn} －消耗 {l.FoodConsumed} －隊伍帶出 {l.FoodCarriedOut} ＝期末 {l.FoodClosing}（缺糧 {l.FoodShortage}）");
            lines.Add($"  糧食來源：農牧 {l.FoodFromFarming} ＋採集 {l.FoodFromGathering}");
            lines.Add($"  人口：期初 {l.PopulationOpening} ＋出生 {l.Births} －死亡 {l.Deaths} ＋遷入 {l.Immigrants} －遷出 {l.Emigrants} ＝期末 {l.PopulationClosing}");
            lines.Add($"  飢餓債：期初 {l.HungerDebtOpening} ＋缺糧 {l.FoodShortage} －恢復 {l.HungerDebtRecovered} －隨死者移除 {l.HungerDebtRemovedByDeath} ＝期末 {l.HungerDebtClosing}");
            lines.Add(l.IsBalanced ? "  帳目平衡：是" : "  【錯誤】帳目不平衡！");
        }

        // V0-06A: every migrant batch's ledger for this week (departure, on the road, arrival).
        foreach (var m in week.Migrations)
        {
            lines.Add("");
            var state = m.Departed ? "出發" : m.Arrived ? "抵達" : m.PopulationClosing == 0 ? "全數死於途中" : "途中";
            lines.Add($"遷徙隊伍 #{m.BatchId}（{state}）：格 {m.CellBefore}→{m.CellAfter}");
            lines.Add($"  人口：期初 {m.PopulationOpening} －途中死亡 {m.Deaths} ＝期末 {m.PopulationClosing}；攜帶糧食 {m.FoodOpening}→{m.FoodClosing}");
            lines.Add(m.IsBalanced ? "  帳目平衡：是（不瞬移）" : "  【錯誤】隊伍帳目不平衡或瞬移！");
        }

        return string.Join("\n", lines);
    }

    private void Advance(int days)
    {
        try
        {
            DemoDayEvent? last = null;
            for (var i = 0; i < days; i++)
                last = _world.AdvanceOneDay();
            SyncAlertWatermark(); // #95: events seen by stepping manually are never alerted later.

            _notice.Text = (_playback.Playing ? $"{_playback.StatusText}；" : "")
                + $"已推進 {days} 天；第 {last!.Day} 天：收穫 {last.Harvested}，消耗 {last.Consumed}，缺糧 {last.Shortage}。";
            Refresh();
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
        {
            PausePlayback();
            _notice.Text = $"推進失敗（無狀態偽造），已暫停：{ex.Message}";
        }
    }

    // ---------- #87: play / pause ----------

    /// <summary>Each frame asks the clock how many whole days have elapsed and lets Core advance them one by one.</summary>
    public override void _Process(double delta)
    {
        var days = _playback.Tick(delta);
        if (days > 0)
            AdvancePlayback(days); // #95: stops on the day a major event appears.
    }

    private void TogglePlayback()
    {
        if (_playback.Playing)
        {
            PausePlayback();
            _notice.Text = $"已暫停（第 {_world.Snapshot().DaysElapsed} 日）。";
            return;
        }

        _playback.Play();
        _playButton.Text = "⏸ 暫停";
        _notice.Text = $"{_playback.StatusText}：世界每天仍由核心逐日結算，與按「下一天」相同。";
        Refresh();
    }

    private void PausePlayback()
    {
        if (!_playback.Playing)
            return;
        _playback.Pause();
        _playButton.Text = "▶ 播放";
        Refresh();
    }

    private void OnSpeedPicked(long index)
    {
        _playback.SetSpeed(PlaybackClock.TempDaysPerSecondOptions[(int)index]);
        if (_playback.Playing)
            Refresh();
    }

    private void VerifyPlaybackSmoke(Button nextButton, Button[] saveButtons, Button[] loadButtons)
    {
        void Fail(string why) => throw new InvalidOperationException("Playback smoke: " + why);
        void Press(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
        int Day() => _world.Snapshot().DaysElapsed;
        void Frames(int count, double delta)
        {
            for (var i = 0; i < count; i++)
                _Process(delta);
        }

        if (Day() != 0 || _playback.Playing)
            Fail("the world must start paused on day 0.");
        // #95: this smoke checks raw playback timing; major-event auto-pause has its own smoke.
        _autoPauseOnMajor.ButtonPressed = false;

        // 1. Paused: frames never advance the world.
        Frames(10, 0.5);
        if (Day() != 0)
            Fail("frames advanced a paused world.");

        // 2. Play at 5 days/s: 8 frames of 0.25 s = 10 days; identical to pressing "next day" 10 times in Core.
        _speedPicker.Select(1);
        _speedPicker.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
        Press(_playButton);
        if (!_playback.Playing || _playButton.Text != "⏸ 暫停" || !_status.Text.Contains("播放中（每秒 5 天，TEMP）", StringComparison.Ordinal))
            Fail("the play button did not start playback.");
        Frames(8, 0.25);
        var played = Day();
        var reference = CreateTempWorld(TempScenario.Normal);
        for (var i = 0; i < played; i++)
            reference.AdvanceOneDay();
        if (played != 10 || _world.ExportTempSave() != reference.ExportTempSave())
            Fail($"playback did not match Core day by day (day {played}).");

        // 3. Pause: more frames never advance.
        Press(_playButton);
        Frames(20, 0.5);
        if (_playback.Playing || Day() != played || !_status.Text.Contains("已暫停", StringComparison.Ordinal))
            Fail("pause did not stop the world.");

        // 4. Saving while playing pauses first; loading stays paused.
        Press(_playButton);
        Press(saveButtons[1]);
        var saved = Day();
        Frames(20, 0.5);
        if (_playback.Playing || Day() != saved)
            Fail("saving did not pause playback.");
        Press(nextButton);
        Press(_playButton);
        Press(loadButtons[1]);
        Frames(20, 0.5);
        if (_playback.Playing || Day() != saved)
            Fail("loading did not pause playback.");

        // 5. A long frame hitch is capped per frame (TEMP), never a giant unseen jump.
        _speedPicker.Select(2);
        _speedPicker.EmitSignal(OptionButton.SignalName.ItemSelected, 2L);
        Press(_playButton);
        _Process(60);
        var capped = Day() - saved;
        Press(_playButton);
        if (capped != PlaybackClock.TempMaxDaysPerFrame || _playback.Playing)
            Fail($"a long frame advanced {capped} days.");

        GD.Print($"WORLD_SIM_PLAYBACK_OK: Played={played} Paused={played} SaveAutoPaused=1 LoadAutoPaused=1 Capped={capped}");
    }

    private void Refresh()
    {
        var snapshot = _world.Snapshot();
        _status.Text =
            // #81: scenario and actual seed always visible; always marked TEMP, never "official".
            $"目前情境：【TEMP 示範】{_world.TempScenarioDisplayName}（非正式規則）\n" +
            $"實際種子：{_world.TempWorldSeed}\n" +
            $"世界日期：第 {snapshot.DaysElapsed} 日\n" +
            $"時間推進：{_playback.StatusText}\n" +
            // #61: one population ledger (settlements + migrants, each person counted once) ...
            $"聚落人口合計：{_world.SettledPopulation}\n" +
            $"遷徙途中人口：{_world.PopulationInTransit}\n" +
            $"世界人口（聚落＋途中，各計一次）：{_world.WorldPopulation}\n" +
            // ... kept apart from the older V0-01 daily demo ledger, which is a separate TEMP account.
            "—— V0-01 每日示範帳（舊版獨立 TEMP 帳，與上方聚落人口無關）——\n" +
            $"每日示範人口：{snapshot.Population}\n" +
            $"每日示範食物庫存：{snapshot.FoodStock}\n" +
            $"每日示範累積食物不足：{snapshot.TotalShortage}\n" +
            $"資源亂數抽樣：{snapshot.ResourceRngDraws}";

        _ledger.Text = LedgerText(_world.LatestWeek);
        _hexMap.Migrants = _world.MigrationsInTransit();
        _hexMap.Sites = _world.Sites; // V0-06B: settlements founded this week appear on the map.
        _hexMap.QueueRedraw();

        // V0-04B: merge daily events with weekly settlement events by world time
        // (same day: the daily event first, then the weekly settlement's events).
        // Shows the 12 latest daily events plus the 10 latest settlement events.
        var daily = _world.History().TakeLast(12).Select(e => (Time: e.WorldTime, Order: 0,
            Text: $"第 {e.Day} 天  |  收穫 {e.Harvested}  消耗 {e.Consumed}  缺糧 {e.Shortage}  食物結餘 {e.FoodAfter}"));
        // V0-06B: the 5 latest "settlement founded" events always stay listed, even when a busy famine
        // week pushes them out of the 10 latest settlement events.
        var allSettlementEvents = _world.SettlementEvents()
            .Select((e, i) => (Time: e.WorldTime, Order: 1 + i, Text: e.Describe(), e.Kind))
            .ToList();
        var settlementEvents = allSettlementEvents.TakeLast(10)
            .Union(allSettlementEvents.Where(x => x.Kind == DemoSettlementEventKind.SettlementFounded).TakeLast(5))
            .Select(x => (x.Time, x.Order, x.Text));
        var lines = daily.Concat(settlementEvents)
            .OrderBy(x => x.Time).ThenBy(x => x.Order)
            .Select(x => x.Text);
        _history.Text = string.Join("\n", lines);
        if (snapshot.DaysElapsed == 0)
            _history.Text = "目前沒有已結算事件。請按「下一天」開始。";
        RefreshEventList();
        RefreshFoodDiagnostics(); // #104
    }
}
