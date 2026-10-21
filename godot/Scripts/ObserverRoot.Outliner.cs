using System.Globalization;
using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 05 章 b2：右側關注清單（十字軍之王 3 大綱清單式）。預設顯示聚落清單（名稱、人口、糧食狀況，依人口排序），
/// 點一列→地圖置中、框出該聚落並開左側詳情（Core 點格查詢）。原「世界控制台」改為同位置的第二個分頁，
/// 由頂部兩顆按鈕切換；再按目前分頁的按鈕就收合。只讀 Core，不改世界。
/// </summary>
public partial class ObserverRoot
{
    private enum RightView
    {
        None,
        Outliner,
        Console
    }

    private RightView _rightView = RightView.Outliner;
    private PanelContainer _outlinerPanel = null!;
    private Label _outlinerTitle = null!;
    private Label _overviewText = null!;
    private VBoxContainer _outlinerRows = null!;
    private Button _outlinerToggle = null!;
    private readonly List<(int SettlementId, Button Row, Control Line)> _outlinerButtons = new();
    private string _outlinerSignature = string.Empty;

    private void BuildOutliner()
    {
        _outlinerPanel = new PanelContainer();
        Skin(_outlinerPanel);
        _hud.AddChild(_outlinerPanel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        _outlinerPanel.AddChild(column);
        // 第 08-2 章：世界總覽（人口、村莊、遷徙、年齡結構、糧食、今年生死；Core DemoWorldOverview）。
        var overviewTitle = new Label { Text = "世界總覽", MouseFilter = MouseFilterEnum.Pass };
        overviewTitle.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(overviewTitle);
        _overviewText = new Label
        {
            TooltipText = "世界總覽：資料來自已提交的世界狀態與週報（今年＝目前這一年，進行中）",
            MouseFilter = MouseFilterEnum.Pass,
            ClipText = true
        };
        column.AddChild(_overviewText);
        column.AddChild(new HSeparator());
        // 不放會自動換行的說明文字：換行標籤在面板寬度定案前會撐高最小高度，讓面板超出視窗。
        _outlinerTitle = new Label
        {
            TooltipText = "點一列：地圖置中並開詳情。色塊＝糧食狀況（依最近一週聚落帳）。",
            MouseFilter = MouseFilterEnum.Pass,
            ClipText = true
        };
        _outlinerTitle.AddThemeFontSizeOverride("font_size", 20);
        column.AddChild(_outlinerTitle);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        column.AddChild(scroll);
        _outlinerRows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _outlinerRows.AddThemeConstantOverride("separation", 4);
        scroll.AddChild(_outlinerRows);
    }

    /// <summary>聚落依人口由多到少（同人口依 ID）；每次刷新只在內容有變時重建列。</summary>
    private IReadOnlyList<(int Id, string Name, long Population, FoodStatus Food)> OutlinerEntries()
        => _world.Sites.Settlements
            .Select(s => (s.Id, s.Name, _world.SettlementPopulation(s.Id),
                _hexMap.FoodStatuses.TryGetValue(s.Id, out var food) ? food : FoodStatus.Unsettled))
            .OrderByDescending(e => e.Item3)
            .ThenBy(e => e.Id)
            .ToList();

    private void RefreshOutliner()
    {
        if (_outlinerRows is null)
            return;
        _overviewText.Text = OverviewText();
        var entries = OutlinerEntries();
        var signature = string.Join("|", entries.Select(e => $"{e.Id},{e.Population},{e.Food}"));
        if (signature == _outlinerSignature)
            return;
        _outlinerSignature = signature;
        _outlinerTitle.Text = string.Create(CultureInfo.InvariantCulture,
            $"聚落清單（{entries.Count(e => !IsRuin(e.Id))} 村莊）");

        foreach (var (_, _, line) in _outlinerButtons)
        {
            _outlinerRows.RemoveChild(line);
            line.QueueFree();
        }
        _outlinerButtons.Clear();
        foreach (var entry in entries)
        {
            var line = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            line.AddThemeConstantOverride("separation", 6);
            // 糧食狀況色塊（與地圖糧食模式同一組配色）。
            line.AddChild(new ColorRect
            {
                Color = DemoHexMap.FoodStatusColor(entry.Food),
                CustomMinimumSize = new Vector2(14, 14),
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore
            });
            var row = new Button
            {
                Flat = true,
                Text = string.Create(CultureInfo.InvariantCulture,
                    $"{entry.Name}　人口 {entry.Population}　糧食 {MapFoodStatus.Label(entry.Food)}"),
                Alignment = HorizontalAlignment.Left,
                TooltipText = $"聚落 #{entry.Id}：點一下地圖置中並開詳情",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true
            };
            // 羊皮紙面板上用墨色字，飢餓用紅字；沒有面板素材時（深色底）沿用預設字色。
            if (ArtAssets.Texture(ArtAssets.UiPanelFrame) is not null)
            {
                var ink = entry.Food == FoodStatus.Starving ? new Color(0.70f, 0.12f, 0.10f) : TempInk;
                foreach (var state in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_focus_color" })
                    row.AddThemeColorOverride(state, ink);
            }
            var id = entry.Id;
            row.Pressed += () => FocusSettlement(id);
            row.Pressed += PlayClick;
            line.AddChild(row);
            _outlinerRows.AddChild(line);
            _outlinerButtons.Add((id, row, line));
        }
    }

    private bool IsRuin(int settlementId) => _world.Sites.Settlements.Any(s => s.Id == settlementId && s.IsRuin);

    /// <summary>第 08-2 章：世界總覽文字（Core 共用列）。</summary>
    private string OverviewText()
    {
        // 世界狀態只在週結算時改變：同一世界、同一週、同一年就沿用上次的文字（快轉時不重算整份年帳）。
        var daysPerYear = _calendar?.DaysPerYear ?? 365;
        var week = _world.LatestWeek?.Week ?? 0;
        var year = Math.Max(0, _world.Snapshot().DaysElapsed - 1) / daysPerYear;
        if (_overviewKey is { } cached && ReferenceEquals(cached.World, _world) && cached.Week == week && cached.Year == year)
            return _overviewCache;
        _overviewKey = (_world, week, year);
        _overviewCache = string.Join("\n", DemoWorldOverview.Of(_world, daysPerYear).Lines());
        return _overviewCache;
    }

    private (WorldSimulation.Core.World.DemoObserverWorld World, int Week, int Year)? _overviewKey;
    private string _overviewCache = string.Empty;

    private void FocusSettlement(int settlementId)
    {
        var settlement = _world.Sites.Settlements.FirstOrDefault(s => s.Id == settlementId);
        if (settlement is null)
            return; // 清單比世界舊一拍（例如剛讀檔）時不做事。
        var cell = _world.Map.GetCell(settlement.Column, settlement.Row);
        _hexMap.FocusCell(cell);
        OnCellSelected(cell);
    }

    /// <summary>頂部「聚落清單」「世界控制台」：按另一個就切換，按目前這個就收合。</summary>
    private void SetRightView(RightView view)
    {
        _rightView = _rightView == view ? RightView.None : view;
        ApplyHudLayout();
    }

    /// <summary>第 05 章 b2 冒煙：清單預設開、排序與 Core 一致、點列＝地圖置中＋Core 點格查詢，分頁切換與收合。</summary>
    private string VerifyOutlinerSmoke(Action<string> fail)
    {
        if (_rightView != RightView.Outliner || !_outlinerPanel.Visible || _consolePanel.Visible)
            fail("settlement outliner is not the default right panel.");
        RefreshOutliner();
        if (_overviewText.Text != OverviewText() || !_overviewText.Text.Contains("世界人口", StringComparison.Ordinal))
            fail("world overview does not show Core's overview lines.");
        var expected = OutlinerEntries();
        if (_outlinerButtons.Count != _world.Sites.Settlements.Count
            || !_outlinerButtons.Select(b => b.SettlementId).SequenceEqual(expected.Select(e => e.Id))
            || expected.Zip(expected.Skip(1)).Any(p => p.First.Population < p.Second.Population))
            fail("outliner rows are not Core's settlements sorted by population.");

        var (topId, topRow, _) = _outlinerButtons[0];
        topRow.EmitSignal(BaseButton.SignalName.Pressed);
        var top = _world.Sites.Settlements.First(s => s.Id == topId);
        if (!_detailPanel.Visible || _detailText.Text != _world.QueryCell(top.Column, top.Row).Text
            || _hexMap.Highlight != (top.Column, top.Row))
            fail("clicking an outliner row did not focus the map and open Core's cell query.");
        HideDetail();

        _consoleToggle.EmitSignal(BaseButton.SignalName.Pressed);
        if (_rightView != RightView.Console || !_consolePanel.Visible || _outlinerPanel.Visible)
            fail("console tab did not replace the outliner.");
        _consoleToggle.EmitSignal(BaseButton.SignalName.Pressed);
        if (_consolePanel.Visible || _outlinerPanel.Visible)
            fail("pressing the active tab did not collapse the right panel.");
        _outlinerToggle.EmitSignal(BaseButton.SignalName.Pressed);
        if (!_outlinerPanel.Visible)
            fail("outliner did not reopen.");
        return $"Outliner={_outlinerButtons.Count}";
    }
}
