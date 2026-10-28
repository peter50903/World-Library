using Godot;
using System.Globalization;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 05 章 a 節：觀察者介面骨架——全螢幕地圖＋疊加面板（頂部資訊列、左側詳情、右側世界控制台、
/// 右下時間控制、底部事件列）。版面由純 C# <see cref="HudLayout"/> 計算；所有文字與數字都來自 Core，
/// Godot 不計算世界、不改世界。b 節起套用 GPT 交付的介面素材（面板外框、頂部木條、時間按鈕圖示、
/// 統計圖示、點擊與重大事件音效、背景音樂）；任何素材缺少時退回純色樣式（<see cref="ArtAssets"/>）。
/// </summary>
public partial class ObserverRoot
{
    public override void _ExitTree()
    {
        foreach (var player in new[] { _clickPlayer, _alertPlayer, _musicPlayer })
            player?.Stop();
        ArtAssets.ReleaseCache();
        base._ExitTree();
    }

    private Control _hud = null!;
    private PanelContainer _consolePanel = null!;
    private PanelContainer _topBar = null!;
    private Label _topWorld = null!;
    private Label _topStats = null!;
    private Label _topDate = null!;
    private Button _consoleToggle = null!;
    private PanelContainer _detailPanel = null!;
    private Label _detailTitle = null!;
    private TabContainer _detailTabs = null!;
    private string _detailShown = string.Empty; // 第 08-11 章：目前詳情的全文（冒煙測試比對用）
    private bool _showDebugInfo; // 第 08-11 章：開發資訊頁開啟時才顯示座標、路線、供應量等數值
    private (int Column, int Row)? _detailCell;
    private int _detailWeek = -1;
    private PanelContainer _timePanel = null!;
    private Button _timePlay = null!;
    private Button _timeStep = null!;
    private Button[] _timeSpeeds = null!;
    private bool _detailOpen;
    private Label _topFood = null!;
    private Label _topBirths = null!;
    private Label _topDeaths = null!;
    private Button _musicToggle = null!;
    private AudioStreamPlayer _clickPlayer = null!;
    private AudioStreamPlayer _alertPlayer = null!;
    private AudioStreamPlayer _musicPlayer = null!;

    // 羊皮紙面板上的文字用深褐色墨水；頂部木條上用淺色（TEMP 顯示配色）。
    private static readonly Color TempInk = new(0.22f, 0.15f, 0.08f);
    private static readonly Color TempParchmentText = new(0.95f, 0.89f, 0.74f);
    private const float TempMusicVolumeDb = -16f; // TEMP：背景音樂音量
    private const float TempClickVolumeDb = -20f; // TEMP：按鍵音量（第 08-5 章：使用者 2026-10-23 嫌太大聲，原 -6 dB）

    // TEMP 顯示配色：參考視覺稿 observer_hud 的深色木框與金屬邊（不是正式美術）。
    private static readonly Color TempPanelBg = new(0.10f, 0.085f, 0.065f, 0.93f);
    private static readonly Color TempPanelBorder = new(0.56f, 0.43f, 0.24f);
    private static readonly Color TempGold = new(0.93f, 0.80f, 0.50f);

    private static StyleBoxFlat TempPanelStyle() => ArtAssets.Track(new StyleBoxFlat
    {
        BgColor = TempPanelBg,
        BorderColor = TempPanelBorder,
        BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
        ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6
    });

    /// <summary>面板外框：有 UI_PANEL_FRAME 就用九宮格羊皮紙框，否則用純色樣式。</summary>
    private static void Skin(PanelContainer panel)
        => panel.AddThemeStyleboxOverride("panel", (StyleBox?)ArtAssets.PanelStyle() ?? TempPanelStyle());

    /// <summary>羊皮紙面板內的預設文字顏色（只在面板素材存在時套用）。</summary>
    private void ApplyHudTheme()
    {
        if (ArtAssets.Texture(ArtAssets.UiPanelFrame) is null)
            return;
        var theme = ArtAssets.Track(new Theme());
        theme.SetColor("font_color", "Label", TempInk);
        theme.SetColor("default_color", "RichTextLabel", TempInk);
        theme.SetColor("font_color", "CheckBox", TempInk);
        theme.SetColor("font_hover_color", "CheckBox", TempInk);
        theme.SetColor("font_pressed_color", "CheckBox", TempInk);
        // 第 08-5 章：滑鼠提示是深色底，不能沿用羊皮紙的深色字（使用者 2026-10-23：功能列提示看不清楚）。
        theme.SetColor("font_color", "TooltipLabel", TempParchmentText);
        theme.SetStylebox("panel", "TooltipPanel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.08f, 0.06f, 0.95f),
            BorderColor = new Color(0.55f, 0.45f, 0.30f),
            BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4
        });
        _hud.Theme = theme;
    }

    private static Label LightLabel(string text = "")
    {
        var label = new Label { Text = text, VerticalAlignment = VerticalAlignment.Center };
        label.AddThemeColorOverride("font_color", TempParchmentText);
        return label;
    }

    private static TextureRect? SmallIcon(string path)
        => ArtAssets.Texture(path) is { } texture
            ? new TextureRect
            {
                Texture = texture,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(26, 26),
                MouseFilter = Control.MouseFilterEnum.Ignore
            }
            : null;

    /// <summary>按鈕圖示：有素材就用圖示（文字移到提示），沒有就保留文字。</summary>
    private static void SetButtonIcon(Button button, string path, string fallbackText)
    {
        if (ArtAssets.Texture(path) is { } texture)
        {
            button.Icon = texture;
            button.ExpandIcon = true;
            button.IconAlignment = HorizontalAlignment.Center;
            button.Text = "";
        }
        else
        {
            button.Icon = null;
            button.Text = fallbackText;
        }
    }

    private void BuildTopBar()
    {
        ApplyHudTheme();
        _topBar = new PanelContainer();
        _topBar.AddThemeStyleboxOverride("panel", (StyleBox?)ArtAssets.TopBarStyle() ?? TempPanelStyle());
        _hud.AddChild(_topBar);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        _topBar.AddChild(row);

        _topWorld = new Label { VerticalAlignment = VerticalAlignment.Center };
        _topWorld.AddThemeColorOverride("font_color", TempGold);
        row.AddChild(_topWorld);
        _topStats = LightLabel();
        _topStats.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _topStats.HorizontalAlignment = HorizontalAlignment.Center;
        _topStats.ClipText = true;
        row.AddChild(_topStats);

        // 糧食／本週出生／本週死亡：圖示＋數字（素材缺少時只有文字）。
        _topFood = AddStat(row, ArtAssets.IconFood, "全世界聚落糧食庫存");
        _topBirths = AddStat(row, ArtAssets.IconBirth, "本週出生");
        _topDeaths = AddStat(row, ArtAssets.IconDeath, "本週死亡（餓死＋自然死亡＋途中死亡）");

        _topDate = new Label { VerticalAlignment = VerticalAlignment.Center };
        _topDate.AddThemeColorOverride("font_color", TempGold);
        row.AddChild(_topDate);
        _musicToggle = new Button { Text = "♪", TooltipText = "背景音樂 開／關" , ToggleMode = true };
        _musicToggle.Toggled += OnMusicToggled;
        row.AddChild(_musicToggle);
        // 第 05 章 b2：右側分頁——聚落清單（預設）／世界控制台；按目前分頁就收合。
        _outlinerToggle = new Button { Text = "聚落清單", ToggleMode = true, TooltipText = "右側聚落清單（再按一次收合）" };
        _outlinerToggle.Pressed += () => SetRightView(RightView.Outliner);
        row.AddChild(_outlinerToggle);
        _consoleToggle = new Button { Text = "世界紀錄", ToggleMode = true, TooltipText = "右側世界紀錄：事件依時間列出，點一筆看詳情、原因與後果並定位地圖（再按一次收合）" };
        _consoleToggle.Pressed += () => SetRightView(RightView.Console);
        row.AddChild(_consoleToggle);
        // 第 08-10b 章：右側第三分頁「編年史」。
        _chronicleToggle = new Button { Text = "編年史", ToggleMode = true, TooltipText = "右側編年史：依年份列出世界的重大事件，點聚落名稱定位地圖（再按一次收合）" };
        _chronicleToggle.Pressed += () => SetRightView(RightView.Chronicle);
        row.AddChild(_chronicleToggle);
    }

    private static Label AddStat(HBoxContainer row, string iconPath, string tooltip)
    {
        var group = new HBoxContainer { TooltipText = tooltip };
        group.AddThemeConstantOverride("separation", 4);
        if (SmallIcon(iconPath) is { } icon)
            group.AddChild(icon);
        var value = LightLabel();
        value.TooltipText = tooltip;
        group.AddChild(value);
        row.AddChild(group);
        return value;
    }

    /// <summary>
    /// 音效與背景音樂播放器。點擊音效接到疊加面板裡所有按鈕；重大事件提示播放警示音。
    /// 背景音樂預設開啟（無畫面的 CI 冒煙不播放），可用頂部「♪」關閉。
    /// </summary>
    /// <summary>無畫面（冒煙、CI）不播任何音效：播放中的音效在結束時會被回報為未釋放資源。</summary>
    private bool _audioMuted;

    private void BuildAudio()
    {
        _clickPlayer = new AudioStreamPlayer { Stream = ArtAssets.Audio(ArtAssets.SfxClick), VolumeDb = TempClickVolumeDb };
        _alertPlayer = new AudioStreamPlayer { Stream = ArtAssets.Audio(ArtAssets.SfxEventAlert), VolumeDb = -4f };
        var music = ArtAssets.Audio(ArtAssets.MusicObserverLoop);
        if (music is AudioStreamOggVorbis ogg)
            ogg.Loop = true;
        _musicPlayer = new AudioStreamPlayer { Stream = music, VolumeDb = TempMusicVolumeDb };
        AddChild(_clickPlayer);
        AddChild(_alertPlayer);
        AddChild(_musicPlayer);
        WireClickSounds(_hud);
        var headless = _audioMuted = DisplayServer.GetName() == "headless";
        _musicToggle.Disabled = music is null;
        _musicToggle.SetPressedNoSignal(music is not null && !headless);
        if (_musicToggle.ButtonPressed)
            _musicPlayer.Play();
    }

    private void WireClickSounds(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is BaseButton button && !ReferenceEquals(button, _musicToggle))
                button.Pressed += PlayClick;
            WireClickSounds(child);
        }
    }

    private void PlayClick()
    {
        if (!_audioMuted && _clickPlayer.Stream is not null)
            _clickPlayer.Play();
    }

    /// <summary>重大事件自動暫停時呼叫（#95）。</summary>
    private void PlayAlert()
    {
        if (!_audioMuted && _alertPlayer.Stream is not null)
            _alertPlayer.Play();
    }

    private void OnMusicToggled(bool on)
    {
        if (on && _musicPlayer.Stream is not null)
            _musicPlayer.Play();
        else
            _musicPlayer.Stop();
    }

    private void BuildDetailPanel()
    {
        _detailPanel = new PanelContainer { Visible = false };
        Skin(_detailPanel);
        _hud.AddChild(_detailPanel);
        var column = new VBoxContainer();
        _detailPanel.AddChild(column);
        var header = new HBoxContainer();
        column.AddChild(header);
        _detailTitle = new Label { Text = "詳情", SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        _detailTitle.AddThemeFontSizeOverride("font_size", 18);
        header.AddChild(_detailTitle);
        var close = new Button { Text = "✕", TooltipText = "關閉（Esc）" };
        close.Pressed += HideDetail;
        header.AddChild(close);
        // 第 08-11 章：分頁籤（現況｜代表人物｜技術｜研究｜歷史；開發資訊開啟時多一頁「除錯」）。
        _detailTabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(_detailTabs);
    }

    private void ShowDetailAt(int column, int row)
    {
        _detailCell = (column, row);
        var detail = _world.PlayerDetail(column, row);
        var keep = _detailTabs.CurrentTab;
        foreach (var child in _detailTabs.GetChildren())
        {
            _detailTabs.RemoveChild(child);
            child.QueueFree();
        }
        var tabs = detail.Tabs.ToList();
        if (_showDebugInfo)
            tabs.Add(new DemoDetailTab("除錯", _world.QueryCell(column, row).Lines));
        foreach (var tab in tabs)
        {
            var scroll = new ScrollContainer { Name = tab.Title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            scroll.AddChild(new Label
            {
                Text = string.Join("\n", tab.Lines),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            });
            _detailTabs.AddChild(scroll);
        }
        if (keep >= 0 && keep < tabs.Count)
            _detailTabs.CurrentTab = keep;
        _detailTitle.Text = detail.Title;
        _detailShown = detail.Text;
        _detailOpen = true;
        ApplyHudLayout();
    }

    private void BuildTimeControls()
    {
        _timePanel = new PanelContainer();
        Skin(_timePanel);
        _hud.AddChild(_timePanel);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 6);
        _timePanel.AddChild(row);

        _timePlay = new Button { TooltipText = "播放／暫停（空白鍵）", CustomMinimumSize = new Vector2(44, 36) };
        SetButtonIcon(_timePlay, ArtAssets.UiPlay, "▶");
        _timePlay.Pressed += TogglePlayback;
        row.AddChild(_timePlay);
        _timeStep = new Button { TooltipText = "推進一天", CustomMinimumSize = new Vector2(44, 36) };
        SetButtonIcon(_timeStep, ArtAssets.UiStep, "+1 日");
        _timeStep.Pressed += () => Advance(1);
        row.AddChild(_timeStep);

        var options = PlaybackClock.TempDaysPerSecondOptions;
        _timeSpeeds = new Button[options.Count];
        for (var i = 0; i < options.Count; i++)
        {
            var index = i;
            _timeSpeeds[i] = new Button
            {
                Text = $"{i + 1}",
                ToggleMode = true,
                CustomMinimumSize = new Vector2(40, 36),
                TooltipText = $"速度 {i + 1} 檔：每秒 {options[i]} 天（TEMP；數字鍵 {i + 1}）"
            };
            if (ArtAssets.Texture(ArtAssets.UiFast) is { } fast)
            {
                _timeSpeeds[i].Icon = fast;
                _timeSpeeds[i].ExpandIcon = true;
            }
            else
            {
                _timeSpeeds[i].Text = $"{i + 1}檔";
            }
            _timeSpeeds[i].Pressed += () => SelectSpeed(index);
            row.AddChild(_timeSpeeds[i]);
        }
    }

    /// <summary>右下速度鈕與控制台下拉選單同步（兩者呼叫同一個 <see cref="OnSpeedPicked"/>）。</summary>
    private void SelectSpeed(int index)
    {
        _speedPicker.Select(index);
        _speedPicker.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
        RefreshHud();
    }


    private void HideDetail()
    {
        _detailOpen = false;
        if (_hexMap is not null)
            _hexMap.Highlight = null; // 第 08-5 章：關閉詳情時一併取消地圖上的框
        ApplyHudLayout();
    }

    /// <summary>依目前視窗大小重排所有疊加面板；只改畫面，不碰世界。</summary>
    private void ApplyHudLayout()
    {
        var size = _hud.Size;
        var frame = HudLayout.Compute((int)size.X, (int)size.Y, _detailOpen, _rightView != RightView.None);
        Place(_topBar, frame.TopBar, true);
        Place(_detailPanel, frame.Detail, _detailOpen);
        Place(_consolePanel, frame.Console, _rightView == RightView.Console);
        if (_outlinerPanel is not null)
            Place(_outlinerPanel, frame.Console, _rightView == RightView.Outliner);
        if (_chroniclePanel is not null)
            Place(_chroniclePanel, frame.Console, _rightView == RightView.Chronicle);
        Place(_timePanel, frame.Time, true);
        PlaceToolbarRow(frame);
        _consoleToggle.SetPressedNoSignal(_rightView == RightView.Console);
        _outlinerToggle.SetPressedNoSignal(_rightView == RightView.Outliner);
        _chronicleToggle?.SetPressedNoSignal(_rightView == RightView.Chronicle);
    }

    private static void Place(Control control, HudRect rect, bool visible)
    {
        control.Visible = visible && rect.Width > 0 && rect.Height > 0;
        control.Position = new Vector2(rect.X, rect.Y);
        control.Size = new Vector2(Math.Max(rect.Width, 1), Math.Max(rect.Height, 1));
    }

    /// <summary>頂部資訊列與時間控制的文字（每次 <see cref="Refresh"/> 呼叫；資料全部讀 Core）。</summary>
    private void RefreshHud()
    {
        if (_topBar is null)
            return; // 版面尚未建好（_Ready 前）。
        var days = _world.Snapshot().DaysElapsed;
        var week = _world.LatestWeek;
        var births = week?.Settlements.Sum(l => l.Births) ?? 0;
        var deaths = week?.Settlements.Sum(l => l.Deaths) ?? 0;
        _topWorld.Text = $"【TEMP 示範】{_world.TempScenarioDisplayName}";
        var food = _world.Sites.Settlements.Sum(s => _world.SettlementFood(s.Id));
        _topStats.Text = string.Create(CultureInfo.InvariantCulture,
            $"人口 {_world.WorldPopulation}｜村莊 {_world.Sites.Settlements.Count(x => !x.IsRuin)}｜遷徙途中 {_world.PopulationInTransit}");
        _topFood.Text = string.Create(CultureInfo.InvariantCulture, $"糧食 {food}");
        _topBirths.Text = string.Create(CultureInfo.InvariantCulture, $"出生 {births}");
        _topDeaths.Text = string.Create(CultureInfo.InvariantCulture, $"死亡 {deaths}");
        _topDate.Text = $"{DateText(days)}（第 {days} 日）";
        if (_timePlay is null)
            return;
        SetButtonIcon(_timePlay, _playback.Playing ? ArtAssets.UiPause : ArtAssets.UiPlay, _playback.Playing ? "⏸" : "▶");
        for (var i = 0; i < _timeSpeeds.Length; i++)
            _timeSpeeds[i].ButtonPressed = PlaybackClock.TempDaysPerSecondOptions[i] == _playback.DaysPerSecond;
    }

    private bool PlayButtonShowsPause()
        => _timePlay.Icon is { } icon ? ReferenceEquals(icon, ArtAssets.Texture(ArtAssets.UiPause)) : _timePlay.Text == "⏸";

    /// <summary>空白鍵播放／暫停、數字鍵切換速度、Esc 關閉詳情（輸入框取得焦點時不會收到）。</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_timeSpeeds is null || @event is not InputEventKey { Pressed: true, Echo: false } key)
            return; // 正式設定讀取失敗時沒有介面。
        if (CloseModalOnEscape(key))
        {
            GetViewport().SetInputAsHandled();
            return;
        }
        if (key.Keycode == Key.Space)
        {
            TogglePlayback();
            RefreshHud();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode == Key.Escape && _detailOpen)
        {
            HideDetail();
            GetViewport().SetInputAsHandled();
        }
        else if (key.Keycode >= Key.Key1 && key.Keycode < Key.Key1 + _timeSpeeds.Length)
        {
            SelectSpeed((int)(key.Keycode - Key.Key1));
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// 第 05 章 CI 冒煙：兩種視窗尺寸下各面板可見且在視窗內；點聚落→左側詳情＝Core 點格查詢文字；
    /// 空白鍵切換播放、數字鍵切換速度、Esc 關閉詳情；收合控制台；以上操作都不改世界。
    /// </summary>
    private void VerifyHudSmoke(Button nextButton)
    {
        void Fail(string why) => throw new InvalidOperationException("HUD smoke: " + why);
        void PressKey(Godot.Key code)
        {
            var ev = new InputEventKey { Keycode = code, Pressed = true };
            _UnhandledInput(ev);
        }

        var resized = 0;
        foreach (var (w, h) in new[] { (1280, 720), (1920, 1080) })
        {
            _hud.Size = new Vector2(w, h);
            ApplyHudLayout();
            foreach (var (name, control) in new (string, Control)[]
                     { ("TopBar", _topBar), ("Outliner", _outlinerPanel), ("Time", _timePanel),
                       ("Toolbar", _toolbarPanel) })
            {
                var r = new Rect2(control.Position, control.Size);
                if (!control.Visible || r.Position.X < 0 || r.Position.Y < 0 || r.End.X > w || r.End.Y > h)
                    Fail($"{name} not visible inside {w}x{h}: {r}");
            }
            resized++;
        }

        for (var i = 0; i < 7; i++)
            nextButton.EmitSignal(BaseButton.SignalName.Pressed);
        var before = _world.ExportTempSave();
        if (!_topDate.Text.Contains("第 7 日", StringComparison.Ordinal)
            || !_topStats.Text.Contains($"人口 {_world.WorldPopulation}", StringComparison.Ordinal))
            Fail($"top bar does not show Core's day/population: {_topDate.Text} / {_topStats.Text}");

        // 點聚落 → 左側詳情面板＝Core 點格查詢文字。
        var settlement = _world.Sites.Settlements[0];
        _hexMap._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = _hexMap.CellCenter(_world.Map.GetCell(settlement.Column, settlement.Row))
        });
        if (!_detailPanel.Visible || _detailShown != _world.PlayerDetail(settlement.Column, settlement.Row).Text)
            Fail("detail panel did not open with Core's cell query text.");
        PressKey(Godot.Key.Escape);
        if (_detailPanel.Visible)
            Fail("Esc did not close the detail panel.");

        // 鍵盤：空白鍵播放→暫停；數字鍵 2 選第二檔。
        PressKey(Godot.Key.Space);
        if (!_playback.Playing || !PlayButtonShowsPause())
            Fail("Space did not start playback.");
        PressKey(Godot.Key.Space);
        if (_playback.Playing)
            Fail("Space did not pause playback.");
        PressKey(Godot.Key.Key2);
        if (_playback.DaysPerSecond != PlaybackClock.TempDaysPerSecondOptions[1] || !_timeSpeeds[1].ButtonPressed
            || _speedPicker.Selected != 1)
            Fail("key 2 did not select the second speed.");
        PressKey(Godot.Key.Key5);
        if (_playback.DaysPerSecond != PlaybackClock.TempDaysPerSecondOptions[4] || !_timeSpeeds[4].ButtonPressed
            || _speedPicker.Selected != 4 || _timeSpeeds.Length != 5)
            Fail("key 5 did not select the fifth speed.");

        // b2：右側聚落清單（預設）、點列定位、分頁切換與收合。
        var outliner = VerifyOutlinerSmoke(Fail);

        var toolbar = VerifyToolbarSmoke(Fail, PressKey);

        if (_world.ExportTempSave() != before)
            Fail("HUD operations changed the world.");

        // b 節：本版用到的全部素材都必須讀得到（CI 已匯入專案），而且真的掛上畫面。
        var art = ArtAssets.LoadedUsedCount();
        if (art != ArtAssets.Used.Length)
            Fail($"only {art}/{ArtAssets.Used.Length} art assets loaded: "
                + string.Join(", ", ArtAssets.Used.Where(p => (p.EndsWith(".ogg", StringComparison.Ordinal)
                    ? ArtAssets.Audio(p) : (Resource?)ArtAssets.Texture(p)) is null)));
        if (_timeStep.Icon is null || _timeSpeeds[0].Icon is null
            || _consolePanel.GetThemeStylebox("panel") is not StyleBoxTexture
            || _outlinerPanel.GetThemeStylebox("panel") is not StyleBoxTexture
            || _topBar.GetThemeStylebox("panel") is not StyleBoxTexture
            || _clickPlayer.Stream is null || _alertPlayer.Stream is null || _musicPlayer.Stream is null
            || !_hexMap.UsesTerrainArt || !DemoHexMap.HasLandArt)
            Fail("art assets were loaded but not applied to the HUD/map.");
        if (!_topFood.Text.StartsWith("糧食 ", StringComparison.Ordinal))
            Fail("food stat missing from the top bar.");

        GD.Print($"WORLD_SIM_HUD_OK: TopBar=1 Time=1 Detail=1 Record=1 Keys=1 Resize={resized} Art={art} {toolbar} {outliner} Speeds={_timeSpeeds.Length}");
    }
}
