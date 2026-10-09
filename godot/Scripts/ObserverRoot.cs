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
    private static readonly ulong WorldSeed = SettleSmoke ? PickSettleSmokeSeed()
        : MigrationSmoke ? PickMigrationSmokeSeed() : DemoSeed;
    private DemoObserverWorld _world = new(
        DemoScenario.Sample(WorldSeed),
        SettleSmoke ? DemoWeeklyTempConfig.FrontierSample
            : MigrationSmoke ? DemoWeeklyTempConfig.LocalFamineSample
            : FamineSmoke ? DemoWeeklyTempConfig.FamineSample : DemoWeeklyTempConfig.Sample);
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
            for (var day = 0; day < 70 && probe.MigrationsInTransit().Count == 0; day++)
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
    private RichTextLabel _history = null!;
    private RichTextLabel _ledger = null!;

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
        _hexMap = new DemoHexMap { World = _world.Map, Sites = _world.Sites };
        _hexMap.CellSelected += OnCellSelected;
        mapPanel.AddChild(_hexMap);

        var sidebar = new PanelContainer
        {
            CustomMinimumSize = new Vector2(350, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        middle.AddChild(sidebar);

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

        side.AddChild(new Label
        {
            Text = "● 綠色：草原   ● 深綠：森林\n● 藍色：水域   ● 金色：示範聚落\n● 紅色：遷徙隊伍（數字＝人數，每週沿相鄰格走 1 格）",
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

        var saveButton = new Button { Text = "存檔（TEMP）" };
        saveButton.Pressed += SaveWorld;
        side.AddChild(saveButton);
        var loadButton = new Button { Text = "讀檔（TEMP）" };
        loadButton.Pressed += LoadWorld;
        side.AddChild(loadButton);

        _notice = new Label
        {
            Text = "世界只會在按下按鈕時推進。",
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
        historyColumn.AddChild(_history);

        Refresh();
        if (OS.HasEnvironment("WORLD_SIM_SAVE_SMOKE"))
        {
            for (var i = 0; i < 9; i++)
                nextButton.EmitSignal(BaseButton.SignalName.Pressed);
            var checkpoint = _world.ExportTempSave();
            saveButton.EmitSignal(BaseButton.SignalName.Pressed);
            nextButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (_world.Snapshot().DaysElapsed != 10)
                throw new InvalidOperationException("TEMP save smoke failed to advance.");
            loadButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (_world.ExportTempSave() != checkpoint || _world.Snapshot().DaysElapsed != 9
                || !_status.Text.Contains("世界日期：第 9 日", StringComparison.Ordinal))
                throw new InvalidOperationException("TEMP Godot load did not restore all world state and UI.");
            var baseline = DemoObserverWorld.ImportTempSave(checkpoint);
            baseline.AdvanceOneDay();
            nextButton.EmitSignal(BaseButton.SignalName.Pressed);
            if (_world.ExportTempSave() != baseline.ExportTempSave())
                throw new InvalidOperationException("TEMP Godot resumed world diverged after load.");
            GD.Print("WORLD_SIM_SAVE_OK: SavedDay=9 RestoredDay=9 ResumedDay=10");
        }
        if (MapInputSmoke)
            VerifyMapInputSmoke();

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
            if (!_cellInfo.Text.Contains($"聚落：{founded.Name}", StringComparison.Ordinal)
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

    private void SaveWorld()
    {
        try
        {
            _world.SaveTempFile(DemoObserverWorld.DefaultTempSavePath);
            _notice.Text = $"TEMP 存檔成功：{DemoObserverWorld.DefaultTempSavePath}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            _notice.Text = $"TEMP 存檔失敗，世界未改變：{ex.Message}";
        }
    }

    private void LoadWorld()
    {
        try
        {
            var restored = DemoObserverWorld.LoadTempFile(DemoObserverWorld.DefaultTempSavePath);
            _world = restored; // New fully validated world; never mutate the old one on failure.
            _hexMap.World = restored.Map;
            _cellInfo.Text = "TEMP 世界讀取完成；請點選地圖格或聚落查看資料。";
            Refresh();
            _notice.Text = $"TEMP 讀檔成功：{DemoObserverWorld.DefaultTempSavePath}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            _notice.Text = $"TEMP 讀檔失敗，世界未改變：{ex.Message}";
        }
    }

    private void OnCellSelected(DemoHexCell cell)
    {
        _cellInfo.Text =
            $"TEMP 六角格｜偏移座標 ({cell.Column}, {cell.Row})\n" +
            $"軸向座標 ({cell.AxialQ}, {cell.AxialR})\n" +
            $"核心地形：{TerrainName(cell.Terrain)}";

        // V0-06A: migrant batches standing on this cell (from Core; never moved by Godot).
        foreach (var batch in _world.MigrationsInTransit()
                     .Where(b => b.Cell.Column == cell.Column && b.Cell.Row == cell.Row))
        {
            _cellInfo.Text += $"\n遷徙隊伍 #{batch.Id}：{batch.People} 人，{batch.FromName}→{batch.ToName}，" +
                $"還有 {batch.StepsLeft} 格，攜帶糧食 {batch.Food}，飢餓債 {batch.HungerDebt}";
        }

        var settlement = _world.Sites.SettlementAt(cell.Column, cell.Row);
        if (settlement is null)
            return;

        var lines = new List<string> { "", $"聚落：{settlement.Name}（TEMP 示範編號 {settlement.Id}）" };
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

            _notice.Text = $"已推進 {days} 天；第 {last!.Day} 天：收穫 {last.Harvested}，消耗 {last.Consumed}，缺糧 {last.Shortage}。";
            Refresh();
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
        {
            _notice.Text = $"推進失敗（無狀態偽造）：{ex.Message}";
        }
    }

    private void Refresh()
    {
        var snapshot = _world.Snapshot();
        _status.Text =
            $"世界日期：第 {snapshot.DaysElapsed} 日\n" +
            // #61: one population ledger (settlements + migrants, each person counted once) ...
            $"聚落人口合計：{_world.SettledPopulation}\n" +
            $"遷徙途中人口：{_world.PopulationInTransit}\n" +
            $"世界人口（聚落＋途中，各計一次）：{_world.WorldPopulation}\n" +
            // ... kept apart from the older V0-01 daily demo ledger, which is a separate TEMP account.
            "—— V0-01 每日示範帳（舊版獨立 TEMP 帳，與上方聚落人口無關）——\n" +
            $"每日示範人口：{snapshot.Population}\n" +
            $"每日示範食物庫存：{snapshot.FoodStock}\n" +
            $"每日示範累積食物不足：{snapshot.TotalShortage}\n" +
            $"資源亂數抽樣：{snapshot.ResourceRngDraws}\n" +
            $"亂數種子：{_world.TempWorldSeed}";

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
    }
}
