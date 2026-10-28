using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 05 章 b 節：地圖模式（地形／糧食狀況）與工具列（情境、存讀檔、說明）。
/// 第 08-3 章：兩者併成左下一條功能列，與右下時間控制並排置底；另加「開發」視窗（原世界控制台的開發用資訊）。
/// 工具列按鈕開置中的疊加視窗（modal，不是作業系統視窗）；Esc 或 ✕ 關閉。只改畫面，不改世界。
/// 圖示是 GPT 交付素材（使用者選定保留圖上的英文字），按鈕提示是繁中；素材缺少時改顯示繁中文字。
/// </summary>
public partial class ObserverRoot
{
    private const int TempToolButtonSize = 64; // TEMP：工具列與地圖模式圖示按鈕邊長

    private Button _mapTerrainButton = null!;
    private Button _mapFoodButton = null!;
    private RichTextLabel _foodLegend = null!;
    private PanelContainer _toolbarPanel = null!;
    private Button _toolScenario = null!;
    private Button _toolSave = null!;
    private Button _toolHelp = null!;
    private Button _toolDev = null!;
    private Control _modalBackdrop = null!;
    private PanelContainer _modalPanel = null!;
    private Label _modalTitle = null!;
    private VBoxContainer _modalPageHost = null!;
    private readonly Dictionary<string, Control> _modalPages = new();
    private string? _modalOpen;

    private static void SkinBar(PanelContainer panel)
        => panel.AddThemeStyleboxOverride("panel", (StyleBox?)ArtAssets.ToolbarStyle() ?? TempPanelStyle());

    private static Button ToolButton(string iconPath, string fallbackText, string tooltip)
    {
        var button = new Button
        {
            TooltipText = tooltip,
            CustomMinimumSize = new Vector2(TempToolButtonSize, TempToolButtonSize),
            FocusMode = Control.FocusModeEnum.None
        };
        SetButtonIcon(button, iconPath, fallbackText);
        button.Flat = button.Icon is not null; // 有圖示就讓木板底圖透出來。
        return button;
    }

    private void AddMapModeButtons(HBoxContainer row)
    {

        _mapTerrainButton = ToolButton(ArtAssets.UiMapTerrain, "地形", "地圖模式：地形（預設）");
        _mapTerrainButton.Pressed += () => SetMapMode(DemoHexMap.Mode.Terrain);
        row.AddChild(_mapTerrainButton);
        _mapFoodButton = ToolButton(ArtAssets.UiMapFood, "糧食",
            $"地圖模式：糧食狀況（依最近一週聚落帳；綠＝充足、黃＝緊張：存糧不足 {MapFoodStatus.TempTightWeeks} 週〔TEMP〕、紅＝飢餓、灰＝尚未結算）");
        _mapFoodButton.Pressed += () => SetMapMode(DemoHexMap.Mode.Food);
        row.AddChild(_mapFoodButton);

        _foodLegend = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            MouseFilter = MouseFilterEnum.Ignore,
            Visible = false
        };
        var legend = string.Join("  ", new[] { FoodStatus.Ample, FoodStatus.Tight, FoodStatus.Starving, FoodStatus.Unsettled }
            .Select(s => $"[color=#{DemoHexMap.FoodStatusColor(s).ToHtml(false)}]■[/color] {MapFoodStatus.Label(s)}"));
        _foodLegend.Text = $"[outline_size=4][outline_color=#000000]{legend}[/outline_color][/outline_size]";
        _hud.AddChild(_foodLegend);
        SetMapMode(DemoHexMap.Mode.Terrain);
    }

    private void SetMapMode(DemoHexMap.Mode mode)
    {
        _hexMap.MapMode = mode;
        // 目前模式全亮，另一顆變暗（扁平圖示按鈕沒有按下底色）。
        _mapTerrainButton.SelfModulate = mode == DemoHexMap.Mode.Terrain ? Colors.White : new Color(1, 1, 1, 0.5f);
        _mapFoodButton.SelfModulate = mode == DemoHexMap.Mode.Food ? Colors.White : new Color(1, 1, 1, 0.5f);
        if (_foodLegend is not null)
            ApplyHudLayout();
    }

    private void BuildToolbar(Control scenarioPage, Control savePage, Control devPage)
    {
        _toolbarPanel = new PanelContainer();
        SkinBar(_toolbarPanel);
        _hud.AddChild(_toolbarPanel);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 8);
        _toolbarPanel.AddChild(row);
        AddMapModeButtons(row); // 第 08-3 章：地圖模式併入同一條功能列。第 08-5 章：不畫分隔線（使用者 2026-10-23）。

        _toolScenario = ToolButton(ArtAssets.UiScenario, "情境", "情境與新世界（世界種子、歷史種子、TEMP 示範情境）");
        _toolScenario.Pressed += () => ToggleModal("scenario");
        row.AddChild(_toolScenario);
        _toolSave = ToolButton(ArtAssets.UiSave, "存讀檔", "存檔／讀檔（TEMP 三個存檔槽）");
        _toolSave.Pressed += () => ToggleModal("save");
        row.AddChild(_toolSave);
        _toolHelp = ToolButton(ArtAssets.UiHelp, "說明", "說明與快捷鍵");
        _toolHelp.Pressed += () => ToggleModal("help");
        row.AddChild(_toolHelp);
        // 第 08 章 b 節：年度圖表（圖示 UI_ICON_CHART 尚待製作，缺少時顯示文字）。
        _toolChart = ToolButton(ArtAssets.UiChart, "圖表", "年度圖表：人口、出生死亡、遷徙、糧食採收與缺口（點某年看該年事件）");
        _toolChart.Pressed += () => ToggleModal("chart");
        row.AddChild(_toolChart);
        // 第 08-3 章：開發資訊（原右側世界控制台：狀態、帳目、診斷、逐日推進、週結算文字）。
        _toolDev = ToolButton(ArtAssets.UiDev, "開發", "開發資訊：世界狀態、本週帳目、糧食診斷、逐日推進與週結算文字（開發用）");
        _toolDev.Pressed += () => ToggleModal("dev");
        row.AddChild(_toolDev);

        BuildModalHost();
        AddModalPage("scenario", "情境與新世界", scenarioPage);
        AddModalPage("save", "存檔／讀檔", savePage);
        AddModalPage("chart", "年度圖表", BuildYearChartPage());
        AddModalPage("dev", "開發資訊", devPage);
        AddModalPage("help", "說明", new Label
        {
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Text = string.Join("\n", new[]
            {
                "【快捷鍵】",
                "空白鍵：播放／暫停",
                $"數字鍵 1～{PlaybackClock.TempDaysPerSecondOptions.Count}：切換速度（每秒 {string.Join("／", PlaybackClock.TempDaysPerSecondOptions)} 天，TEMP）",
                "Esc：關閉視窗或左側詳情",
                "",
                "【地圖】",
                "滾輪放大／縮小，按住滑鼠中鍵拖曳，左鍵點格或聚落開左側詳情。",
                "左下地圖模式：地形（預設）、糧食狀況（綠＝充足、黃＝緊張、紅＝飢餓、灰＝尚未結算；依最近一週聚落帳）。",
                "",
                "【介面】",
                "頂部：世界、人口、糧食、本週出生／死亡、日期；♪ 開關背景音樂；右上「聚落清單」「世界紀錄」「編年史」切換右側面板，再按一次收合。",
                "右側聚落清單：上方世界總覽；聚落依人口排序，色塊＝糧食狀況；點一列地圖置中並開詳情（含聚落小史）。",
                "右側世界紀錄：事件依時間列出（新的在上），紅色＝重大事件；點一筆才顯示詳情、原因與後果，並定位地圖。",
                "右側編年史：依年份列出重大事件（新的在上）；勾「一般事件」或用「類型」選單篩選；點聚落名稱定位地圖；較早的年份每 10 年折疊，點一下展開；點「本年出生…」列出各聚落。",
                "右下：時間控制（播放、+1 日、5 檔速度）。左下功能列：地圖模式（地形／糧食）、情境與新世界、存讀檔、說明、年度圖表、開發資訊。",
                "",
                "所有人口、糧食與門檻都是 TEMP 示範值；介面操作不會改變世界。"
            })
        });
    }

    private void BuildModalHost()
    {
        // 半透明遮罩擋住地圖點擊；視窗置中。
        _modalBackdrop = new ColorRect { Color = new Color(0, 0, 0, 0.45f), Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _hud.AddChild(_modalBackdrop);
        _modalBackdrop.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _modalBackdrop.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
                CloseModal();
        };
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        _modalBackdrop.AddChild(center);
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _modalPanel = new PanelContainer();
        Skin(_modalPanel);
        center.AddChild(_modalPanel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 10);
        _modalPanel.AddChild(column);
        var header = new HBoxContainer();
        column.AddChild(header);
        _modalTitle = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _modalTitle.AddThemeFontSizeOverride("font_size", 20);
        header.AddChild(_modalTitle);
        var close = new Button { Text = "✕", TooltipText = "關閉（Esc）" };
        close.Pressed += CloseModal;
        header.AddChild(close);
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        column.AddChild(scroll);
        _modalPageHost = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_modalPageHost);
    }

    private void AddModalPage(string key, string title, Control page)
    {
        page.Visible = false;
        page.SetMeta("modal_title", title);
        _modalPageHost.AddChild(page);
        _modalPages[key] = page;
    }

    private void ToggleModal(string key)
    {
        if (_modalOpen == key)
        {
            CloseModal();
            return;
        }
        _modalOpen = key;
        foreach (var (name, page) in _modalPages)
            page.Visible = name == key;
        _modalTitle.Text = (string)_modalPages[key].GetMeta("modal_title");
        _modalBackdrop.Visible = true;
        SizeModal();
        if (key == "save")
            RefreshSlots();
        if (key == "chart")
            RefreshYearChart();
    }

    private void CloseModal()
    {
        _modalOpen = null;
        _modalBackdrop.Visible = false;
    }

    private void SizeModal()
    {
        if (_modalBackdrop is null || !_modalBackdrop.Visible)
            return;
        var size = _hud.Size;
        _modalPanel.CustomMinimumSize = new Vector2(Math.Min(640f, size.X - 32f), Math.Min(520f, size.Y - 96f)); // TEMP
    }

    /// <summary>由 <see cref="ApplyHudLayout"/> 呼叫：放工具列、地圖模式列與糧食圖例。</summary>
    private void PlaceToolbarRow(HudFrame frame)
    {
        if (_toolbarPanel is null)
            return;
        Place(_toolbarPanel, frame.Toolbar, true);
        // 圖例放在功能列正上方（糧食模式才顯示）。
        var showLegend = _hexMap.MapMode == DemoHexMap.Mode.Food;
        Place(_foodLegend, new HudRect(frame.Toolbar.X, Math.Max(0, frame.Toolbar.Y - 32), Math.Max(frame.Toolbar.Width, 1), 28), showLegend);
        SizeModal();
    }

    private bool CloseModalOnEscape(InputEventKey key)
    {
        if (_modalOpen is null || key.Keycode != Key.Escape)
            return false;
        CloseModal();
        return true;
    }

    /// <summary>第 05 章 b 節冒煙：工具列三個視窗、地圖模式切換、圖示套用；全部不改世界。</summary>
    private string VerifyToolbarSmoke(Action<string> fail, Action<Key> pressKey)
    {
        var modals = 0;
        foreach (var (button, key) in new[] { (_toolScenario, "scenario"), (_toolSave, "save"), (_toolHelp, "help") })
        {
            button.EmitSignal(BaseButton.SignalName.Pressed);
            if (!_modalBackdrop.Visible || _modalOpen != key || !_modalPages[key].Visible
                || _modalPages.Any(p => p.Key != key && p.Value.Visible))
                fail($"toolbar button did not open the {key} window.");
            pressKey(Key.Escape);
            if (_modalBackdrop.Visible || _modalOpen is not null)
                fail($"Esc did not close the {key} window.");
            modals++;
        }
        // 情境視窗裡就是原本的情境選單與種子輸入；存讀檔視窗裡就是存檔槽。
        if (!_modalPages["scenario"].IsAncestorOf(_scenarioPicker) || !_modalPages["scenario"].IsAncestorOf(_worldSeedInput)
            || !_modalPages["save"].IsAncestorOf(_slotLabels[1]) || !_modalPages["dev"].IsAncestorOf(_status)
            || !_modalPages["dev"].IsAncestorOf(_history) || !_toolbarPanel.IsAncestorOf(_mapFoodButton))
            fail("scenario/save controls are not inside their toolbar windows.");

        _mapFoodButton.EmitSignal(BaseButton.SignalName.Pressed);
        var week = _world.LatestWeek;
        if (_hexMap.MapMode != DemoHexMap.Mode.Food || week is null
            || _hexMap.FoodStatuses.Count != week.Settlements.Count
            || week.Settlements.Any(l => _hexMap.FoodStatuses[l.SettlementId] != MapFoodStatus.Classify(l)))
            fail("food map mode does not show Core's latest weekly ledger.");
        _mapTerrainButton.EmitSignal(BaseButton.SignalName.Pressed);
        if (_hexMap.MapMode != DemoHexMap.Mode.Terrain)
            fail("terrain map mode did not come back.");

        if (new[] { _toolScenario, _toolSave, _toolHelp, _mapTerrainButton, _mapFoodButton }.Any(b => b.Icon is null)
            || _toolbarPanel.GetThemeStylebox("panel") is not StyleBoxTexture)
            fail("toolbar / map mode art was not applied.");
        return $"Toolbar=1 MapMode=2 Modal={modals}";
    }
}
