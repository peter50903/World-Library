using Godot;
using System.Globalization;
using WorldSimulation.Core.Config;
using WorldSimulation.Core.Time;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// Real Godot observer GUI backed by the in-memory DemoObserverWorld (C05: weekly settlement only).
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
    // 第 02 章 a 節：CI-only smoke for the world / history seed inputs. Normal players never set it.
    private static readonly bool RecipeSmoke = OS.HasEnvironment("WORLD_SIM_RECIPE_SMOKE");

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
        || OS.HasEnvironment("WORLD_SIM_RECIPE_SMOKE")
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

    /// <summary>第 02 章 a 節：玩家輸入的世界配方＋目前選的 TEMP 情境。</summary>
    private static DemoObserverWorld CreateTempWorld(TempScenario scenario, WorldRecipe recipe)
        => new(recipe, ConfigFor(scenario));

    private static string PickerLabel(TempScenario scenario) => scenario switch
    {
        TempScenario.Famine => "全面饑荒（TEMP）",
        TempScenario.LocalFamine => "局部饑荒｜觀察遷徙（TEMP）",
        TempScenario.Frontier => "開拓｜觀察新聚落（TEMP）",
        TempScenario.Foraging => "森林／河流採集生存（預設，TEMP）",
        TempScenario.CrowdedForaging => "森林周邊人口過密｜觀察缺糧（TEMP）",
        TempScenario.LivingForaging => "希望時代：人類採集（預設，TEMP）",
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
        "希望時代：人類採集" => TempScenario.LivingForaging,
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
    private Button[] _verifyButtons = null!;
    private RichTextLabel _history = null!;
    private RichTextLabel _ledger = null!;
    // #81: TEMP scenario picker + explicit confirmation before discarding an advanced world.
    private OptionButton _scenarioPicker = null!;
    private Button _startScenarioButton = null!;
    private HBoxContainer _confirmRow = null!;
    private Button _confirmScenarioButton = null!;
    private Button _cancelScenarioButton = null!;
    private TempScenario? _pendingScenario;
    // 第 02 章 a 節：世界種子／歷史種子輸入；確認列待確認的配方（null＝情境切換沿用預設種子）。
    private LineEdit _worldSeedInput = null!;
    private LineEdit _historySeedInput = null!;
    private OptionButton _mapSizePicker = null!; // 第 07-1 章 c 節
    private Button _seedStartButton = null!;
    private WorldRecipe? _pendingRecipe;
    // #87: play / pause. The clock only decides HOW MANY days to advance; Core advances them one by one.
    private readonly PlaybackClock _playback = new();
    private Button _playButton = null!;
    private OptionButton _speedPicker = null!;

    // C08：世界曆法（01 表）由 Core 換算；Godot 只在邊界讀檔一次。讀不到時保留錯誤，不給預設值。
    private WorldCalendarRules? _calendar;
    private string? _calendarError;

    private string DateText(int day)
        => _calendar is not null ? _calendar.Format(day) : $"曆法讀取失敗：{_calendarError}";

    public override void _Ready()
    {
        // 第 02 章 b 節：先載入並驗證正式設定（01＋29）；失敗就只顯示錯誤，不建立任何介面、不推進世界。
        var officialConfig = OfficialConfig.Load();
        if (!officialConfig.IsValid)
        {
            var error = new Label
            {
                Text = officialConfig.ErrorText,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            AddChild(error);
            error.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
            GD.PrintErr(officialConfig.ErrorText);
            return;
        }

        _calendar = WorldCalendarFile.TryLoad(out _calendarError);
        // 第 05 章：地圖永遠全螢幕；所有介面都是疊在地圖上的面板（不另開作業系統視窗）。
        var mapPanel = new PanelContainer();
        AddChild(mapPanel);
        mapPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        // V0-03B: inject the map and sites already generated by the observer world's
        // single RngRegistry; Godot never generates its own map.
        _hexMap = new DemoHexMap { World = _world.Map, Sites = _world.Sites,
            MainRiverTiles = _world.UsesSharedForaging ? DemoForagingWorld.MainRiverTiles(_world.Map) : Array.Empty<DemoCellRef>() };
        _hexMap.CellSelected += OnCellSelected;
        mapPanel.AddChild(_hexMap);

        _hud = new Control { MouseFilter = MouseFilterEnum.Ignore };
        AddChild(_hud);
        _hud.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        BuildTopBar();

        // 第 08-3 章：右側第二分頁改為「世界紀錄」（事件依時間列出，點一筆才顯示詳情）；
        // 原「世界控制台」的開發用資訊移到功能列「開發」視窗（疊加 modal）。
        _consolePanel = new PanelContainer();
        Skin(_consolePanel);
        _hud.AddChild(_consolePanel);

        // V0-07: the added save/load buttons must remain reachable on smaller windows.
        // Scroll only the information page; map wheel input stays within the map.
        var sidebarScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(600, 420) // TEMP：開發資訊視窗內容區
        };
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        side.AddThemeConstantOverride("separation", 18);
        sidebarScroll.AddChild(side);

        var worldHeading = new Label { Text = "示範世界狀態" };
        worldHeading.AddThemeFontSizeOverride("font_size", 20);
        side.AddChild(worldHeading);

        _status = new Label();
        side.AddChild(_status);

        // #81: choose one of the existing TEMP survival scenarios and start a NEW demo world.
        // 第 05 章 b 節：情境與種子移到底部工具列「情境」視窗（疊加 modal）。
        var scenarioBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scenarioBox.AddThemeConstantOverride("separation", 10);
        var scenarioHeading = new Label { Text = "TEMP 示範情境（切換＝建立新的示範世界）" };
        scenarioHeading.AddThemeFontSizeOverride("font_size", 18);
        scenarioBox.AddChild(scenarioHeading);
        _scenarioPicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var scenario in Enum.GetValues<TempScenario>())
            _scenarioPicker.AddItem(PickerLabel(scenario), (int)scenario);
        _scenarioPicker.ItemSelected += OnScenarioPicked;
        scenarioBox.AddChild(_scenarioPicker);
        // 第 02 章 a 節：世界種子＋歷史種子（留空＝由世界種子衍生），搭配上方情境建立新世界。
        var seedRow = new HBoxContainer();
        scenarioBox.AddChild(seedRow);
        seedRow.AddChild(new Label { Text = "世界種子" });
        _worldSeedInput = new LineEdit { Text = _world.TempWorldSeed.ToString(CultureInfo.InvariantCulture), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        seedRow.AddChild(_worldSeedInput);
        seedRow.AddChild(new Label { Text = "歷史種子" });
        _historySeedInput = new LineEdit { PlaceholderText = "留空＝由世界種子衍生", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        seedRow.AddChild(_historySeedInput);
        // 第 07-1 章 c 節：建立世界時選地圖大小（只套用在「以此種子建立世界」）。
        var sizeRow = new HBoxContainer();
        scenarioBox.AddChild(sizeRow);
        sizeRow.AddChild(new Label { Text = "地圖大小" });
        _mapSizePicker = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var size in Enum.GetValues<DemoMapSize>())
        {
            var (columns, rows) = DemoMapSizes.Dimensions(size);
            _mapSizePicker.AddItem($"{DemoMapSizes.Name(size)}（{columns}×{rows} 格）", (int)size);
        }
        sizeRow.AddChild(_mapSizePicker);
        _seedStartButton = new Button { Text = "以此種子建立世界", CustomMinimumSize = new Vector2(0, 36) };
        _seedStartButton.Pressed += RequestSeedWorld;
        scenarioBox.AddChild(_seedStartButton);
        _startScenarioButton = new Button
        {
            Text = "開始新的 TEMP 示範",
            CustomMinimumSize = new Vector2(0, 40)
        };
        _startScenarioButton.Pressed += RequestNewScenario;
        scenarioBox.AddChild(_startScenarioButton);
        _confirmRow = new HBoxContainer { Visible = false };
        scenarioBox.AddChild(_confirmRow);
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
        scenarioBox.AddChild(new Label
        {
            Text = "局部饑荒／開拓沿用既有示範種子搜尋（42，再 1～30），選第一個會在示範期間發生遷徙／建立新聚落的種子；實際種子顯示在上方（「世界種子」）。存檔槽不會因切換被刪除或覆寫。",
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

        // 第 08-11 章：座標、路線、供應量等數值只給開發者看（詳情多一頁「除錯」）。
        var debugToggle = new CheckBox { Text = "詳情顯示除錯資訊（座標、路線、糧食供應）", ButtonPressed = _showDebugInfo };
        debugToggle.Toggled += on =>
        {
            _showDebugInfo = on;
            if (_detailOpen && _detailCell is { } c)
                ShowDetailAt(c.Column, c.Row);
        };
        side.AddChild(debugToggle);

        _cellInfo = new Label
        {
            Text = "TEMP 六角格：請點選地圖中的一格或金色聚落查看其資料。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        side.AddChild(_cellInfo);
        BuildFoodDiagnostics(side); // #104：糧食供需診斷（只讀 Core）。

        side.AddChild(new Label
        {
            Text = ArtAssets.Texture(ArtAssets.TileGrassland) is not null
                ? "地圖：草原、森林、海洋、主河道（採集情境）各有地形圖塊；村莊圖示＝聚落，帳篷圖示＝遷徙者建立的新聚落，行旅圖示＝遷徙隊伍（數字＝人數，每週走 1 格）。"
                : "● 綠色：未開墾草原   ● 深綠：森林\n● 藍色：海洋   ● 青色：河流（淺色＝淺灘渡口）\n● 金色：示範聚落   ● 紅色：遷徙隊伍（每週走 1 格）",
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
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
        // 第 05 章 b 節：存檔槽移到底部工具列「存讀檔」視窗。
        var saveBox = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        saveBox.AddThemeConstantOverride("separation", 10);
        var slotHeading = new Label { Text = $"TEMP 存檔槽（{DemoSaveSlots.TempSlotCount} 槽，非正式存檔）" };
        slotHeading.AddThemeFontSizeOverride("font_size", 18);
        saveBox.AddChild(slotHeading);
        var saveButtons = new Button[DemoSaveSlots.TempSlotCount + 1];
        var loadButtons = new Button[DemoSaveSlots.TempSlotCount + 1];
        _slotLabels = new Label[DemoSaveSlots.TempSlotCount + 1];
        _verifyButtons = new Button[DemoSaveSlots.TempSlotCount + 1];
        for (var slot = 1; slot <= DemoSaveSlots.TempSlotCount; slot++)
        {
            var row = new HBoxContainer();
            saveBox.AddChild(row);
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
            // 第 04 章 b 節：選用完整驗證（從第 0 日重跑逐週比對；不改世界）。
            _verifyButtons[slot] = new Button { Text = "驗", TooltipText = $"完整驗證槽 {number}：從第 0 日重跑到存檔日逐週比對" };
            _verifyButtons[slot].Pressed += () => VerifySlot(number);
            row.AddChild(_verifyButtons[slot]);
        }
        RefreshSlots();

        _notice = new Label
        {
            Text = "世界只會在按下按鈕或按「▶ 播放」時推進；存檔、讀檔、切換情境會自動暫停。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        side.AddChild(_notice);

        // 第 08-3 章：週結算摘要文字（開發用）放在開發資訊視窗底部。
        var historyTitle = new Label { Text = "最近週結算與聚落事件（文字）" };
        historyTitle.AddThemeFontSizeOverride("font_size", 18);
        side.AddChild(historyTitle);
        _history = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 240)
        };
        side.AddChild(_history);

        // 第 08-3 章：右側「世界紀錄」分頁＝篩選＋事件清單（依時間，新的在上）＋點選後才出現的事件詳情。
        var recordColumn = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        recordColumn.AddThemeConstantOverride("separation", 6);
        _consolePanel.AddChild(recordColumn);
        BuildEventDetailPanel(recordColumn);

        BuildOutliner();
        BuildChronicle(); // 第 08-10b 章
        BuildDetailPanel();
        BuildTimeControls();
        BuildToolbar(scenarioBox, saveBox, sidebarScroll);
        BuildAudio();
        _hud.Resized += ApplyHudLayout;
        ApplyHudLayout();

        Refresh();
        if (OS.HasEnvironment("WORLD_SIM_SAVE_SMOKE"))
            VerifySaveSlotSmoke(nextButton, saveButtons, loadButtons);
        if (MapInputSmoke)
            VerifyMapInputSmoke();
        if (ScenarioSmoke)
            VerifyScenarioSwitchSmoke(nextButton, skipButton, saveButtons, loadButtons);
        if (RecipeSmoke)
            VerifyRecipeSmoke(nextButton);
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
        if (OS.HasEnvironment("WORLD_SIM_HUD_SMOKE"))
            VerifyHudSmoke(nextButton);
        if (OS.HasEnvironment("WORLD_SIM_HUMAN_SMOKE"))
            VerifyHumanLifecycleSmoke();
        if (ForagingSmoke)
            VerifyForagingSmoke(nextButton);
        if (ChartSmoke)
            VerifyYearChartSmoke();
        if (ChronicleSmoke)
            VerifyChronicleSmoke();

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

            // #61: the status panel shows ONE population ledger (settled + in transit = world);
            // C05: the old V0-01 daily demo account is retired and must not appear.
            if (!_status.Text.Contains($"聚落人口合計：{_world.SettledPopulation}", StringComparison.Ordinal)
                || !_status.Text.Contains($"遷徙途中人口：{_world.PopulationInTransit}", StringComparison.Ordinal)
                || !_status.Text.Contains($"世界人口（聚落＋途中，各計一次）：{_world.WorldPopulation}", StringComparison.Ordinal)
                || _status.Text.Contains("V0-01", StringComparison.Ordinal)
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
            // C05：舊每日糧食線已退役；歷史欄改列週結算摘要。
            if (state.DaysElapsed != 31 || _world.WeekReports().Count != 4 ||
                !_history.Text.Contains("第 4 週結算", StringComparison.Ordinal) ||
                _status.Text.Contains("V0-01", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Godot button signal did not update world and history.");
            }

            GD.Print("WORLD_SIM_UI_SMOKE_OK: Day=31 Weeks=4");

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

            // C08：狀態欄與週帳標頭的年月日來自 Core 曆法（01 表 WORLD_MONTH_DAYS），第 31 日＝第 1 年 1 月 31 日。
            if (_calendar is null
                || !_status.Text.Contains("世界日期：第 31 日（第 1 年 1 月 31 日）", StringComparison.Ordinal)
                || !_ledger.Text.Contains("第 4 週帳目（第 1 年 1 月 28 日結算，世界第 28 日）", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Godot calendar display is wrong: {_calendarError}");
            }
            GD.Print("WORLD_SIM_CALENDAR_OK: Day=31 Date=1-1-31 Week4=1-1-28");

            // 第 02 章 b 節：狀態欄顯示正式設定載入結果與 64 碼設定雜湊的前 16 碼（CI 與 CLI 的 Hash16 比對）。
            var config = OfficialConfig.Current;
            if (!_status.Text.Contains("正式設定：01 已載入", StringComparison.Ordinal)
                || !_status.Text.Contains(config.ConfigHash[..16], StringComparison.Ordinal)
                || _world.Recipe.ConfigHash != config.ConfigHash)
            {
                throw new InvalidOperationException("Godot status does not show the official config and its hash.");
            }
            GD.Print($"WORLD_SIM_CONFIG_OK: Sheets={config.SheetCount} Hash16={config.ConfigHash[..16]}");

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
                || !_cellInfo.Text.Contains(DemoCellQuery.TerrainName(cell), StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Godot selection did not display Core's actual TEMP terrain data.");
            }
            GD.Print($"WORLD_SIM_HEX_CLICK_OK: Col={cell.Column} Row={cell.Row}");

            // V0-03B: clicking a settlement's cell shows Core's TEMP settlement,
            // resource point and population group.
            var settlement = _world.Sites.Settlements[0];
            var pop = _world.Sites.PopGroupsOf(settlement.Id)[0];
            var resource = _world.Sites.ResourcePointsOf(settlement.Id)[0];
            var saveBeforeClick = _world.ExportTempSave(); // C07：點格查詢不得改世界。
            _hexMap._GuiInput(new InputEventMouseButton
            {
                ButtonIndex = MouseButton.Left,
                Pressed = true,
                Position = _hexMap.CellCenter(_world.Map.GetCell(settlement.Column, settlement.Row))
            });
            if (_cellInfo.Text != string.Join("\n", _world.QueryCell(settlement.Column, settlement.Row).Lines)
                || _world.ExportTempSave() != saveBeforeClick)
            {
                throw new InvalidOperationException("C07: Godot cell panel is not Core QueryCell text, or the click changed the world.");
            }
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
        var rngBefore = _world.RngState(); // C05：十條流全部比對（UI 不碰世界亂數，44E MOD_RNG）。
        var peopleBefore = _world.WorldPopulation;
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
            || !_world.RngState().SequenceEqual(rngBefore)
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
            _notice.Text = $"TEMP 存檔槽 {slot} 讀檔成功：第 {restored.Snapshot().DaysElapsed} 日"
                + (restored.LoadWarning is { } warning ? $"\n{warning}" : ""); // 第 04 章 b 節：程式版本不同只警告
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            _notice.Text = $"TEMP 存檔槽 {slot} 讀檔失敗，世界未改變：{ex.Message}";
        }
        RefreshSlots();
    }

    /// <summary>第 04 章 b 節：完整驗證一個存檔槽（選用工具；不改世界）。</summary>
    private void VerifySlot(int slot)
    {
        PausePlayback();
        _notice.Text = DemoSaveSlots.Verify(slot).Describe(slot);
    }

    // ---------- #81: TEMP scenario switching (always a NEW Core demo world) ----------

    private void OnScenarioPicked(long index)
    {
        // Picking alone never changes the world; it only cancels a pending, different confirmation.
        if (_pendingScenario is not null)
        {
            _pendingScenario = null;
            _pendingRecipe = null;
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
        if (_pendingRecipe is { } recipe)
            StartRecipe(target, recipe);
        else
            StartScenario(target);
    }

    /// <summary>第 02 章 a 節：依輸入的世界／歷史種子與目前情境建立新世界；格式錯誤時世界不變。</summary>
    private void RequestSeedWorld()
    {
        PausePlayback();
        if (!TryReadRecipe(out var recipe))
        {
            _notice.Text = "種子必須是 0～18446744073709551615 的整數，世界未改變。";
            return;
        }
        var target = (TempScenario)_scenarioPicker.Selected;
        var day = _world.Snapshot().DaysElapsed;
        if (day == 0)
        {
            StartRecipe(target, recipe);
            return;
        }

        _pendingScenario = target;
        _pendingRecipe = recipe;
        _confirmRow.Visible = true;
        _notice.Text = $"⚠ 目前世界（{_world.TempScenarioDisplayName}）已推進到第 {day} 日。" +
            $"以世界種子 {recipe.WorldSeed}、歷史種子 {(recipe.HistorySeedDerived ? "衍生" : recipe.HistorySeed.ToString(CultureInfo.InvariantCulture))} 建立「{PickerLabel(target)}」會捨棄目前未存檔的進度；" +
            "三個存檔槽不會被刪除或覆寫。請按「確認切換」或「取消」。";
    }

    private bool TryReadRecipe(out WorldRecipe recipe)
    {
        recipe = null!;
        if (!ulong.TryParse(_worldSeedInput.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var worldSeed))
            return false;
        ulong? history = null;
        var historyText = _historySeedInput.Text.Trim();
        if (historyText.Length > 0)
        {
            if (!ulong.TryParse(historyText, NumberStyles.None, CultureInfo.InvariantCulture, out var h))
                return false;
            history = h;
        }
        recipe = WorldRecipe.ForSeeds(worldSeed, history, (DemoMapSize)_mapSizePicker.GetSelectedId());
        return true;
    }

    private void StartRecipe(TempScenario target, WorldRecipe recipe)
    {
        DemoObserverWorld fresh;
        try
        {
            fresh = CreateTempWorld(target, recipe);
        }
        catch (InvalidOperationException ex)
        {
            _notice.Text = $"無法建立世界，目前世界未改變：{ex.Message}";
            return;
        }

        InstallWorld(fresh, "TEMP 新示範世界已建立；請點選地圖格或聚落查看資料。");
        _notice.Text = $"已建立新世界：{PickerLabel(target)}｜{fresh.Recipe.Describe()}｜第 0 日。";
    }

    private void CancelNewScenario()
    {
        _pendingScenario = null;
        _pendingRecipe = null;
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
        _pendingRecipe = null;
        _confirmRow.Visible = false;
        HideDetail(); // 第 05 章：換世界不殘留前一個世界的詳情。
        _worldSeedInput.Text = world.Recipe.WorldSeed.ToString(CultureInfo.InvariantCulture);
        _historySeedInput.Text = world.Recipe.HistorySeedInput?.ToString(CultureInfo.InvariantCulture) ?? "";
        _mapSizePicker.Select(_mapSizePicker.GetItemIndex((int)world.Recipe.MapSize));
        _hexMap.World = world.Map;
        _hexMap.Sites = world.Sites;
        _hexMap.Land = world.Land; // 第 08 章 a 節
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
                || !_status.Text.Contains($"世界種子：{expected.TempWorldSeed}", StringComparison.Ordinal)
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
        // 第 08-2 章：遷徙圖示畫在所在六角格的正中央。
        var migrantCell = _world.Map.GetCell(_world.MigrationsInTransit()[0].Cell.Column, _world.MigrationsInTransit()[0].Cell.Row);
        if (_hexMap.MigrantCenter(migrantCell) != _hexMap.CellCenter(migrantCell))
            Fail("the migrant marker is not centred on its hex.");
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
        System.IO.File.WriteAllText(DemoSaveSlots.PathFor(3), "{\"Version\":3,\"State\":");
        RefreshSlots();
        var before = _world.ExportTempSave();
        loadButtons[3].EmitSignal(BaseButton.SignalName.Pressed);
        if (_world.ExportTempSave() != before
            || !_notice.Text.StartsWith("TEMP 存檔槽 3 讀檔失敗", StringComparison.Ordinal)
            || !_slotLabels[3].Text.StartsWith("槽 3：無法辨識", StringComparison.Ordinal))
            throw new InvalidOperationException("TEMP corrupt slot 3 was not rejected visibly.");
        GD.Print("WORLD_SIM_SLOTS_OK: Slot=2 SavedDay=9 RestoredDay=9 CorruptSlot=3 Rejected=1");

        // 第 04 章 b 節：存檔標頭——可讀槽標「可讀」；正式設定雜湊不符的檔案在槽位標「不相容」且讀檔被拒；完整驗證一致。
        if (!_slotLabels[2].Text.EndsWith("｜可讀", StringComparison.Ordinal)
            || !_slotLabels[2].Text.Contains("｜世界種子 42｜", StringComparison.Ordinal))
            throw new InvalidOperationException($"slot 2 header is not shown as compatible: {_slotLabels[2].Text}");
        var mismatched = System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(DemoSaveSlots.PathFor(2)))!;
        var otherHash = new string('0', 64);
        mismatched["Manifest"]!["ConfigHash"] = otherHash;
        mismatched["Manifest"]!["Recipe"]!["ConfigHash"] = otherHash;
        System.IO.File.WriteAllText(DemoSaveSlots.PathFor(1), mismatched.ToJsonString());
        RefreshSlots();
        var beforeMismatch = _world.ExportTempSave();
        loadButtons[1].EmitSignal(BaseButton.SignalName.Pressed);
        if (_world.ExportTempSave() != beforeMismatch
            || !_notice.Text.Contains("正式設定不符", StringComparison.Ordinal)
            || !_slotLabels[1].Text.Contains("｜不相容（正式設定不符", StringComparison.Ordinal))
            throw new InvalidOperationException($"config-mismatched slot 1 was not rejected visibly: {_slotLabels[1].Text} / {_notice.Text}");
        _verifyButtons[2].EmitSignal(BaseButton.SignalName.Pressed);
        if (!_notice.Text.StartsWith("完整驗證槽 2：一致", StringComparison.Ordinal) || _world.ExportTempSave() != beforeMismatch)
            throw new InvalidOperationException($"full replay verification of slot 2 failed: {_notice.Text}");
        GD.Print("WORLD_SIM_SAVE_HEADER_OK: Compatible=1 ConfigMismatchRejected=1 ReplayVerified=1");
    }

    // C07：資訊框文字全部來自 Core 唯讀點格查詢；Godot 只放字串、更新黃框與糧食面板選取。
    private void OnCellSelected(DemoHexCell cell)
    {
        var view = _world.QueryCell(cell.Column, cell.Row);
        _cellInfo.Text = view.Text;
        _hexMap.FoodReachCells = view.FoodReachCells;
        SelectFoodSettlement(view.SettlementId ?? 0); // #104
        _hexMap.QueueRedraw();
        ShowDetailAt(cell.Column, cell.Row); // 第 08-11 章：左側詳情改為玩家用分頁（數值資訊只在開發資訊開啟時）。
    }

    private static string ResourceName(DemoResourceKind kind) => DemoCellQuery.ResourceName(kind);

    private static string TerrainName(DemoTerrain terrain) => DemoCellQuery.TerrainName(terrain);

    private string LedgerText(DemoWeekReport? week)
    {
        if (week is null)
            return "尚未到第一次週結算（第 7 日）。請按「下一天」推進。";

        var settledDay = (int)week.WorldTime.TotalDays;
        var lines = new List<string> { $"第 {week.Week} 週帳目（{DateText(settledDay)}結算，世界第 {settledDay} 日）" };
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

    /// <summary>C05：通知列用的最近一次週結算摘要（同一句由 Core 產生）。</summary>
    private string WeeklySummaryText()
        => _world.LatestWeek is { } week ? week.Summary() : "第 7 天才有第一次週結算。";

    private void Advance(int days)
    {
        try
        {
            DemoDayAdvance? last = null;
            for (var i = 0; i < days; i++)
                last = _world.AdvanceOneDay();
            SyncAlertWatermark(); // #95: events seen by stepping manually are never alerted later.

            _notice.Text = (_playback.Playing ? $"{_playback.StatusText}；" : "")
                + $"已推進 {days} 天（第 {last!.Day} 天）；{WeeklySummaryText()}";
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
        // 第 08-11 章：詳情開著時每週結算後更新一次（不在每幀重建分頁）。
        if (_detailOpen && _detailCell is { } open && open.Column < _world.Map.Columns && open.Row < _world.Map.Rows
            && _world.LatestWeek?.Week is { } week && week != _detailWeek)
        {
            _detailWeek = week;
            ShowDetailAt(open.Column, open.Row);
        }
        _status.Text =
            // #81: scenario and actual seed always visible; always marked TEMP, never "official".
            $"目前情境：【TEMP 示範】{_world.TempScenarioDisplayName}（非正式規則）\n" +
            $"{_world.Recipe.DescribeLines()}\n" +
            $"{OfficialConfig.Current.Describe()}\n" +
            $"世界日期：第 {(int)snapshot.WorldTime.TotalDays} 日（{DateText((int)snapshot.WorldTime.TotalDays)}）\n" +
            $"時間推進：{_playback.StatusText}\n" +
            // #61: one population ledger (settlements + migrants, each person counted once) ...
            $"聚落人口合計：{_world.SettledPopulation}\n" +
            $"遷徙途中人口：{_world.PopulationInTransit}\n" +
            $"世界人口（聚落＋途中，各計一次）：{_world.WorldPopulation}";

        RefreshHud(); // 第 05 章：頂部資訊列與時間控制。
        _ledger.Text = LedgerText(_world.LatestWeek);
        _hexMap.Migrants = _world.MigrationsInTransit();
        _hexMap.WeekFraction = (snapshot.DaysElapsed % 7) / 7f; // 第 06-2 章：遷徙隊伍平滑移動
        _hexMap.Sites = _world.Sites; // V0-06B: settlements founded this week appear on the map.
        _hexMap.Land = _world.Land;
        _hexMap.FoodStatuses = MapFoodStatus.ForWeek(_world.LatestWeek?.Settlements); // 第 05 章 b 節：糧食狀況地圖模式。
        // 第 08-10a 章 V2：家系地圖與聚落圖示大小（人口、是否為村莊）。
        _hexMap.Lineage = DemoLineageMap.Compute(_world.Map, _world.Sites);
        _hexMap.LineageRoots = DemoLineageMap.Roots(_world.Sites);
        _hexMap.KnowledgeCounts = _world.Sites.Settlements.ToDictionary(s => s.Id, s => _world.Knowledge.KnownBy(s.Id).Count); // 第 08-10a 章 V4
        _hexMap.FadedRuins = DemoRuins.Faded(_world); // 第 08-11 章：不重要的遺跡還原成原本的土地
        _hexMap.RecentDisasters = _world.WeekReports().Where(w => w.Week > _world.CommittedWeeks - DemoWildlife.TempYearWeeks)
            .SelectMany(w => w.Events).Where(e => DemoDisasters.IsDisaster(e.Kind)).Select(e => e.SettlementId).ToHashSet(); // 第 12-1 章 C3
        _hexMap.MeteoriteOre = _world.MeteoriteOre.ToHashSet(); // 第 12-1 章
        _hexMap.CultureOf = _world.Sites.Settlements.Where(s => _world.Culture.MainCultureOf(s.Id) is not null)
            .ToDictionary(s => s.Id, s => _world.Culture.MainCultureOf(s.Id)!.Value); // 第 08-8 章 B1
        _hexMap.CultureOrder = _world.Culture.Cultures.Select(c => c.Id).ToList();
        // 第 08-8 章 B3：信仰模式。
        _hexMap.FaithOf = _world.Sites.Settlements.Where(s => _world.Culture.Faith.MainFaithOf(s.Id) is not null)
            .ToDictionary(s => s.Id, s => _world.Culture.Faith.MainFaithOf(s.Id)!.Value);
        _hexMap.FaithOrder = _world.Culture.Faith.Faiths.Select(f => f.Id).ToList();
        _hexMap.HolySites = _world.Culture.Faith.Faiths.Where(f => f.ExtinctWeek == 0).Select(f => f.FounderSettlementId).ToHashSet();
        _hexMap.SettlementScale = _world.Sites.Settlements.ToDictionary(s => s.Id,
            s => (_world.SettlementPopulation(s.Id), _world.ChronicleOf(s.Id).Tier)); // 第 08-11 章：五級
        RefreshOutliner(); // 第 05 章 b2：右側聚落清單。
        RefreshChronicle(); // 第 08-10b 章：編年史（分頁開著才重算）。
        RefreshYearChartIfOpen(); // 第 08 章 b 節：圖表開著時跟著推進更新。
        _hexMap.QueueRedraw();

        // C05：舊每日糧食線退役；改為最近 12 週的週結算摘要（Core DemoWeekReport.Summary()），
        // 與聚落事件依世界時間合併（同一天：週摘要在前，再列該週事件）。
        var daily = _world.WeekReports().TakeLast(12).Select(w => (Time: w.WorldTime, Order: 0,
            Text: w.Summary()));
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
