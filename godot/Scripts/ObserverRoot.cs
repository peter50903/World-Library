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
    private readonly DemoObserverWorld _world = new(DemoScenario.Sample(DemoSeed));
    private readonly DemoHexWorld _mapWorld = DemoHexWorld.Generate(DemoSeed);
    private DemoHexMap _hexMap = null!;
    private Label _cellInfo = null!;

    private Label _status = null!;
    private Label _notice = null!;
    private RichTextLabel _history = null!;

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
        _hexMap = new DemoHexMap { World = _mapWorld };
        _hexMap.CellSelected += OnCellSelected;
        mapPanel.AddChild(_hexMap);

        var sidebar = new PanelContainer
        {
            CustomMinimumSize = new Vector2(350, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        middle.AddChild(sidebar);

        var side = new VBoxContainer();
        side.AddThemeConstantOverride("separation", 18);
        sidebar.AddChild(side);

        var worldHeading = new Label { Text = "示範世界狀態" };
        worldHeading.AddThemeFontSizeOverride("font_size", 20);
        side.AddChild(worldHeading);

        _status = new Label();
        side.AddChild(_status);

        _cellInfo = new Label
        {
            Text = "TEMP 六角格：請點選地圖中的一格查看其資料。",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        side.AddChild(_cellInfo);

        side.AddChild(new Label
        {
            Text = "● 綠色：草原   ● 深綠：森林\n● 藍色：水域   ● 金色：示範聚落",
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

        var historyTitle = new Label { Text = "結算歷史｜最近 12 筆示範日結算事件" };
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

            // Godot runs the same input handler used by real mouse clicks;
            // selected terrain must be from Core's immutable demo cell.
            var cell = _mapWorld.GetCell(2, 2);
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
        }
    }

    private void OnCellSelected(DemoHexCell cell)
    {
        _cellInfo.Text =
            $"TEMP 六角格｜偏移座標 ({cell.Column}, {cell.Row})\n" +
            $"軸向座標 ({cell.AxialQ}, {cell.AxialR})\n" +
            $"核心地形：{TerrainName(cell.Terrain)}";
    }

    private static string TerrainName(DemoTerrain terrain) => terrain switch
    {
        DemoTerrain.Grassland => "草原（TEMP）",
        DemoTerrain.Forest => "森林（TEMP）",
        DemoTerrain.Water => "水域（TEMP）",
        _ => "未定義（TEMP）"
    };

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
            $"人口：{snapshot.Population}\n" +
            $"食物庫存：{snapshot.FoodStock}\n" +
            $"累積食物不足：{snapshot.TotalShortage}\n" +
            $"資源亂數抽樣：{snapshot.ResourceRngDraws}\n" +
            $"亂數種子：{DemoSeed}";

        var recent = _world.History().TakeLast(12);
        var lines = recent.Select(e =>
            $"第 {e.Day} 天  |  收穫 {e.Harvested}  消耗 {e.Consumed}  缺糧 {e.Shortage}  食物結餘 {e.FoodAfter}");
        _history.Text = string.Join("\n", lines);
        if (snapshot.DaysElapsed == 0)
            _history.Text = "目前沒有已結算事件。請按「下一天」開始。";
    }
}
