using Godot;
using WorldSimulation.Core.World;

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
        mapPanel.AddChild(new DemoHexMap { DemoSeed = DemoSeed });

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

        var historyTitle = new Label { Text = "結算歷史｜僅顯示最近 12 筆真實 DemoDayEvent" };
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
        }
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
