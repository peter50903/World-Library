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
        _simTask?.Wait(); // 關閉前等背景推進結束
        foreach (var player in new[] { _clickPlayer, _alertPlayer, _musicPlayer, _uiPlayer })
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
    private (bool Faith, int Id)? _detailBelief; // 第 08-16 章第 15 節：目前開著的是文化頁或信仰頁（null＝地點或聚落）
    private Button _detailBack = null!;
    private int? _detailRealm; // 試玩回饋 2026-11-07 之 4：目前開著的是勢力詳情（最上層聚落編號）
    private bool _detailFixed;
    private HFlowContainer _detailCrumbs = null!; // 試玩回饋 2026-11-08 之 6：開著的是團體或隊伍的詳情（每週不改成聚落頁）
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
        _consoleToggle.Visible = false; // 第 08-11 章（使用者 2026-10-28）：編年史已取代世界紀錄，按鈕不再顯示（事件清單仍保留給冒煙測試與除錯）
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
        _clickPlayer = new AudioStreamPlayer { Stream = ArtAssets.Audio(ArtAssets.UiClick), VolumeDb = TempClickVolumeDb };
        _alertPlayer = new AudioStreamPlayer { Stream = ArtAssets.Audio(ArtAssets.SfxEventAlert), VolumeDb = -4f };
        // 第 08-18d 章：先播主題曲，之後依時代輪播（沒有第五批音樂時照舊循環舊曲）。
        var music = ArtAssets.Audio(ArtAssets.MusicTheme);
        if (music is AudioStreamOggVorbis theme)
            theme.Loop = false;
        if (music is null && ArtAssets.Audio(ArtAssets.MusicObserverLoop) is { } old)
        {
            if (old is AudioStreamOggVorbis ogg)
                ogg.Loop = true;
            music = old;
        }
        _musicPlayer = new AudioStreamPlayer { Stream = music, VolumeDb = TempMusicVolumeDb };
        _musicPlayer.Finished += NextTrack;
        AddChild(_clickPlayer);
        AddChild(_alertPlayer);
        AddChild(_musicPlayer);
        WireClickSounds(_hud);
        var headless = _audioMuted = DisplayServer.GetName() == "headless";
        _hexMap.AudioMuted = headless; // 第 08-16 章：天災與事件音效
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

    private bool _tabSoundMuted;
    private AudioStreamPlayer? _uiPlayer;

    /// <summary>第 08-18d 章：介面音效（打開、關閉詳情、切換分頁；第五批 sfx2/ui_*）；無畫面模式靜音、素材缺少時不播。</summary>
    private void PlayUi(string name)
    {
        if (_audioMuted || ArtAssets.Audio(ArtAssets.Sfx2(name)) is not { } stream)
            return;
        if (_uiPlayer is null)
        {
            _uiPlayer = new AudioStreamPlayer { VolumeDb = TempClickVolumeDb };
            AddChild(_uiPlayer);
        }
        _uiPlayer.Stream = stream;
        _uiPlayer.Play();
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

    private int _track;

    /// <summary>一首播完：換這個時代的下一首（第 08-18d 章）。</summary>
    private void NextTrack()
    {
        if (_audioMuted || !_musicToggle.ButtonPressed)
            return;
        var list = ArtAssets.MusicFor(_musicEra);
        for (var k = 0; k < list.Count; k++)
        {
            if (ArtAssets.Audio(list[_track++ % list.Count]) is not { } next)
                continue;
            if (next is AudioStreamOggVorbis ogg)
                ogg.Loop = false;
            _musicPlayer.Stream = next;
            _musicPlayer.Play();
            return;
        }
        _musicPlayer.Play(); // 只有舊曲：從頭再播
    }

    /// <summary>目前播放音樂所依的時代（每次刷新由主執行緒更新）。</summary>
    private DemoEra _musicEra = DemoEra.Hope;

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
        // 第 08-16 章第 15 節：文化頁、信仰頁左上「返回」回到原本的聚落。
        _detailBack = new Button { Text = "◂ 返回", TooltipText = "回到聚落詳情", Visible = false };
        _detailBack.Pressed += () =>
        {
            if (_detailCell is { } back)
                ShowDetailAt(back.Column, back.Row);
        };
        header.AddChild(_detailBack);
        header.MoveChild(_detailBack, 0);
        header.AddChild(_detailTitle);
        var close = new Button { Text = "✕", TooltipText = "關閉（Esc）" };
        close.Pressed += HideDetail;
        header.AddChild(close);
        // 第 08-11 章：分頁籤（現況｜代表人物｜技術｜研究｜歷史；開發資訊開啟時多一頁「除錯」）。
        // 試玩回饋 2026-11-08 第二次之 2：「往上一層」的連結列（地塊→聚落→勢力、文化、信仰）。
        _detailCrumbs = new HFlowContainer();
        _detailCrumbs.AddThemeConstantOverride("h_separation", 6);
        column.AddChild(_detailCrumbs);
        _detailTabs = new TabContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddChild(_detailTabs);
        // 使用者 2026-11-01（試玩回饋四之 2）：分頁不要壓黑底——Godot 預設分頁容器自帶深灰底，蓋在羊皮紙上；改透明、分頁籤用淡色。
        _detailTabs.AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
        _detailTabs.TabChanged += _ =>
        {
            if (!_tabSoundMuted)
                PlayUi("ui_tab");
        };
        var parchment = ArtAssets.Texture(ArtAssets.UiPanelFrame) is not null;
        StyleBoxFlat TabStyle(float alpha) => ArtAssets.Track(new StyleBoxFlat
        {
            BgColor = parchment ? new Color(0.55f, 0.42f, 0.25f, alpha) : new Color(TempGold, alpha * 0.6f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3
        });
        _detailTabs.AddThemeStyleboxOverride("tab_selected", TabStyle(0.45f));
        _detailTabs.AddThemeStyleboxOverride("tab_unselected", TabStyle(0.15f));
        _detailTabs.AddThemeStyleboxOverride("tab_hovered", TabStyle(0.30f));
        _detailTabs.AddThemeStyleboxOverride("tabbar_background", new StyleBoxEmpty());
        var ink = parchment ? TempInk : TempParchmentText;
        _detailTabs.AddThemeColorOverride("font_selected_color", ink);
        _detailTabs.AddThemeColorOverride("font_unselected_color", ink with { A = 0.7f });
        _detailTabs.AddThemeColorOverride("font_hovered_color", ink);
    }

    private void ShowDetailAt(int column, int row)
    {
        var keep = _detailBelief is null && _detailRealm is null ? _detailTabs.CurrentTab : 0;
        _detailCell = (column, row);
        _detailBelief = null;
        _detailRealm = null;
        _detailFixed = false;
        var detail = _world.PlayerDetail(column, row);
        var tabs = detail.Tabs.ToList();
        // 試玩回饋 2026-11-05 之 2：遭遇天災的地塊在詳情第一頁最上面寫明狀況。
        if (tabs.Count > 0 && _hexMap.DisasterStatus(column, row) is { Length: > 0 } status)
            tabs[0] = tabs[0] with { Lines = new[] { status, "" }.Concat(tabs[0].Lines).ToList() };
        if (detail.SettlementId is { } settlementId && RealmTab(settlementId) is { } realmTab)
            tabs.Add(realmTab); // 試玩回饋 2026-11-08 之 1：聚落所屬勢力的概況與隸屬樹
        if (_showDebugInfo)
            tabs.Add(new DemoDetailTab("除錯", _world.QueryCell(column, row).Lines));
        RenderDetail(detail, tabs, keep);
        // 往上一層：地塊所屬的聚落；聚落所屬的勢力、主要文化、主要信仰。
        var crumbs = new List<(string, Action)>();
        var home = detail.SettlementId ?? (_hexMap.Territory.Cells.TryGetValue(new DemoCellRef(column, row), out var owner) ? owner.SettlementId : null);
        if (home is { } h && _world.Sites.Settlements.FirstOrDefault(s => s.Id == h) is { } site)
        {
            if (detail.SettlementId is null)
                crumbs.Add(($"聚落：{TitledName(site)}", () => ShowDetailAt(site.Column, site.Row)));
            var top = _hexMap.Territory.RealmOf(h);
            if (_hexMap.Territory.Realm.ContainsKey(h))
                crumbs.Add(($"勢力：{(_world.Sites.Settlements.FirstOrDefault(s => s.Id == top) is { } lord ? TitledName(lord) : $"#{top}")}", () => ShowRealmDetail(top)));
            if (_hexMap.CultureOf.TryGetValue(h, out var culture))
                crumbs.Add(($"文化：{_hexMap.CultureNames.GetValueOrDefault(culture, $"#{culture}")}", () => ShowBeliefDetail(false, culture)));
            if (_hexMap.FaithOf.TryGetValue(h, out var faith))
                crumbs.Add(($"信仰：{_hexMap.FaithNames.GetValueOrDefault(faith, $"#{faith}")}", () => ShowBeliefDetail(true, faith)));
        }
        SetCrumbs(crumbs);
    }

    /// <summary>第 08-16 章第 15 節：文化頁或信仰頁（十字軍之王式；從聚落現況的連結打開，「返回」回到聚落）。</summary>
    private void ShowBeliefDetail(bool faith, int id)
    {
        var keep = _detailBelief == (faith, id) ? _detailTabs.CurrentTab : 0;
        if ((faith ? _world.FaithDetail(id) : _world.CultureDetail(id)) is not { } detail)
        {
            if (_detailCell is { } back) // 換了世界、已不存在：回到原本的地點
                ShowDetailAt(back.Column, back.Row);
            return;
        }
        _detailBelief = (faith, id);
        _detailRealm = null;
        _detailFixed = false;
        RenderDetail(detail, detail.Tabs.ToList(), keep);
        SetCrumbs(Array.Empty<(string, Action)>());
    }

    /// <summary>
    /// 試玩回饋 2026-11-08 之 1：勢力模式點到勢力，開勢力最上層聚落的介紹（城市勢力就是那座城市）；
    /// 聚落詳情最後一頁「勢力」列出整個勢力的概況與隸屬樹（<see cref="RealmTab"/>）。
    /// </summary>
    private void ShowRealmDetail(int realmId)
    {
        var top = _hexMap.Territory.RealmOf(realmId);
        if (_world.Sites.Settlements.FirstOrDefault(s => s.Id == top) is not { } capital)
            return;
        ShowDetailAt(capital.Column, capital.Row);
        SelectDetailTab("勢力"); // 點勢力就直接看到勢力頁
    }

    private string TitledName(DemoSettlement site)
        => _hexMap.SettlementScale.TryGetValue(site.Id, out var scale) ? DemoSettlementHistory.TitledName(site, scale.Tier) : site.Name;

    /// <summary>詳情上方「往上一層」的連結（沒有就整列藏起來）。</summary>
    private void SetCrumbs(IReadOnlyList<(string Label, Action Go)> crumbs)
    {
        foreach (var child in _detailCrumbs.GetChildren())
        {
            _detailCrumbs.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var (label, go) in crumbs)
        {
            var button = new Button { Text = "↑ " + label, Flat = true, TooltipText = "打開上一層的詳情" };
            button.AddThemeFontSizeOverride("font_size", 13);
            button.Pressed += () => go();
            _detailCrumbs.AddChild(button);
        }
        _detailCrumbs.Visible = crumbs.Count > 0;
    }

    /// <summary>聚落所屬勢力的分頁：概況、空一行、隸屬樹（Core 的 <see cref="DemoTerritory.Detail"/>）；不在任何勢力回傳 null。</summary>
    private DemoDetailTab? RealmTab(int settlementId)
    {
        if (!_hexMap.Territory.Realm.ContainsKey(settlementId))
            return null;
        var scale = _hexMap.SettlementScale;
        var realm = DemoTerritory.Detail(_hexMap.Territory, _world.Sites, settlementId,
            id => scale.TryGetValue(id, out var s) ? s.Tier : DemoSettlementTier.Camp,
            id => scale.TryGetValue(id, out var s) ? s.Population : 0L);
        return new DemoDetailTab("勢力", realm.Tabs[0].Lines.Append("").Concat(realm.Tabs[1].Lines).ToList());
    }

    /// <summary>
    /// 試玩回饋 2026-11-08 之 6：點到地圖上的角色。領袖開聚落的「代表人物」頁；工作團體、遷徙隊伍開它們自己的詳情；怪物開地下城。
    /// </summary>
    private void OnActorSelected(DemoHexMap.MapActor actor)
    {
        var site = _world.Sites.Settlements.FirstOrDefault(s => s.Id == actor.SettlementId);
        switch (actor.Kind)
        {
            case DemoHexMap.ActorKind.Monster:
                ShowDetailAt(actor.Cell.Column, actor.Cell.Row);
                return;
            case DemoHexMap.ActorKind.Leader when site is not null:
                ShowDetailAt(site.Column, site.Row);
                SelectDetailTab("代表人物");
                return;
            case DemoHexMap.ActorKind.Migrants:
                if (_world.MigrationsInTransit().FirstOrDefault(b => b.Id == actor.BatchId) is { } batch)
                {
                    _detailBelief = null;
                    _detailRealm = null;
                    _detailFixed = true;
                    var lines = new List<string>
                    {
                        $"{(batch.Founding ? "開拓隊" : "遷徙隊伍")}：{batch.People} 人",
                        $"從{batch.FromName}出發，前往{batch.ToName}",
                        batch.Founding ? "抵達後會在那裡建立新的聚落" : "抵達後會住進那個聚落"
                    };
                    RenderDetail(new DemoPlayerDetail($"{batch.FromName}的{(batch.Founding ? "開拓隊" : "遷徙隊伍")}", new[] { new DemoDetailTab("隊伍", lines) }, null),
                        new List<DemoDetailTab> { new("隊伍", lines) }, 0);
                    var ends = new List<(string, Action)>();
                    foreach (var (label, id) in new[] { ("出發地", batch.FromSettlementId), ("目的地", batch.ToSettlementId) })
                        if (_world.Sites.Settlements.FirstOrDefault(s => s.Id == id) is { } end)
                            ends.Add(($"{label}：{TitledName(end)}", () => ShowDetailAt(end.Column, end.Row)));
                    SetCrumbs(ends);
                }
                return;
            case DemoHexMap.ActorKind.WorkGroup when site is not null:
            {
                _detailBelief = null;
                _detailRealm = null;
                _detailFixed = true;
                _detailCell = (site.Column, site.Row);
                var home = _hexMap.SettlementScale.TryGetValue(site.Id, out var scale) ? DemoSettlementHistory.TitledName(site, scale.Tier) : site.Name;
                var name = WorkGroupName(actor.Work);
                var place = DemoCellQuery.ShortName(_world.Map, _world.Sites, _world.Map.GetCell(actor.Cell.Column, actor.Cell.Row));
                var lines = new List<string>
                {
                    $"{home}的{name}：約 {actor.People} 人",
                    $"工作地：{place}",
                    WorkGroupDuty(actor.Work),
                    _world.IsStarving(site.Id) ? "聚落正在缺糧，他們的收穫攸關生死" : "聚落糧食充足"
                };
                RenderDetail(new DemoPlayerDetail($"{home}的{name}", new[] { new DemoDetailTab("團體", lines) }, site.Id),
                    new List<DemoDetailTab> { new("團體", lines) }, 0);
                SetCrumbs(new List<(string, Action)> { ($"聚落：{home}", () => ShowDetailAt(site.Column, site.Row)) });
                return;
            }
        }
    }

    private static string WorkGroupName(DemoHexMap.TokenKind kind) => kind switch
    {
        DemoHexMap.TokenKind.Farmers => "農夫",
        DemoHexMap.TokenKind.Woodcutters => "樵夫",
        DemoHexMap.TokenKind.Miners => "礦工",
        DemoHexMap.TokenKind.Hunters => "獵人",
        DemoHexMap.TokenKind.Fishers => "漁夫",
        _ => "居民"
    };

    private static string WorkGroupDuty(DemoHexMap.TokenKind kind) => kind switch
    {
        DemoHexMap.TokenKind.Farmers => "在農田耕作，是聚落主要的糧食來源",
        DemoHexMap.TokenKind.Woodcutters => "砍伐森林取得木材，砍過的林地約十年長回",
        DemoHexMap.TokenKind.Miners => "開採礦脈，挖空後只剩礦坑",
        DemoHexMap.TokenKind.Hunters => "在附近獵捕野生動物，獵物多寡影響收穫",
        DemoHexMap.TokenKind.Fishers => "在水邊捕魚",
        _ => ""
    };

    /// <summary>
    /// 第 08-18b 章第 5 節介面第二批：「人口」頁——上面是排行，下面是人口金字塔（由上到下是年老到年輕；
    /// 中間是年齡段，左邊藍色是男、右邊紅色是女，長度依最多的那一段等比例）。
    /// </summary>
    private Control PyramidView(DemoDetailTab tab)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var line in tab.Lines.TakeWhile(l => l.Length > 0))
            column.AddChild(new Label { Text = line, AutowrapMode = TextServer.AutowrapMode.WordSmart });
        column.AddChild(new Label { Text = "人口金字塔（男｜女）" });
        var max = Math.Max(1L, tab.Pyramid.Max(b => Math.Max(b.Male, b.Female)));
        const float barWidth = 110f;
        foreach (var band in tab.Pyramid.Reverse())
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            var male = new HBoxContainer { CustomMinimumSize = new Vector2(barWidth, 14), Alignment = BoxContainer.AlignmentMode.End };
            male.AddChild(new ColorRect { Color = new Color(0.30f, 0.50f, 0.80f), CustomMinimumSize = new Vector2(barWidth * band.Male / max, 12), TooltipText = $"男 {band.Male}" });
            row.AddChild(male);
            var label = new Label { Text = band.Label, CustomMinimumSize = new Vector2(78, 0), HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(label);
            row.AddChild(new ColorRect { Color = new Color(0.82f, 0.36f, 0.36f), CustomMinimumSize = new Vector2(barWidth * band.Female / max, 12), TooltipText = $"女 {band.Female}" });
            column.AddChild(row);
        }
        return column;
    }

    /// <summary>切到標題相符的分頁（沒有就不動）。</summary>
    private void SelectDetailTab(string title)
    {
        for (var i = 0; i < _detailTabs.GetTabCount(); i++)
            if (_detailTabs.GetTabTitle(i) == title)
            {
                _detailTabs.CurrentTab = i;
                return;
            }
    }

    private void RenderDetail(DemoPlayerDetail detail, List<DemoDetailTab> tabs, int keep)
    {
        foreach (var child in _detailTabs.GetChildren())
        {
            _detailTabs.RemoveChild(child);
            child.QueueFree();
        }
        _detailBack.Visible = _detailBelief is not null && _detailCell is not null;
        foreach (var tab in tabs)
        {
            var scroll = new ScrollContainer { Name = tab.Title, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            if (tab.Badges.Count > 0)
                scroll.AddChild(TechBadgeGrid(tab)); // 第 08-11 章：技術畫成 RPG 技能那樣的方形小圖示
            else if (tab.Pyramid.Any(b => b.Total > 0))
                scroll.AddChild(PyramidView(tab)); // 第 08-18b 章第 5 節：人口金字塔畫成橫條圖
            else if (tab.People.Count > 0 && PeopleList(tab) is { } peopleList)
                scroll.AddChild(peopleList); // 第 08-16 章：代表人物有人像
            else if ((tab.Emblem is not null || tab.FaithSymbol is not null || tab.CultureId > 0) && HeraldryHeader(tab) is { } header)
                scroll.AddChild(header); // 第 08-16 章：文化紋章與信仰符號
            else
                scroll.AddChild(new Label
                {
                    Text = string.Join("\n", tab.Lines),
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill
                });
            _detailTabs.AddChild(scroll);
        }
        _tabSoundMuted = true; // 程式切的分頁不播音效
        if (keep >= 0 && keep < tabs.Count)
            _detailTabs.CurrentTab = keep;
        _tabSoundMuted = false;
        _detailTitle.Text = detail.Title;
        _detailShown = detail.Text;
        if (!_detailOpen)
            PlayUi("ui_open"); // 第 08-18d 章：介面音效
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


    // ── 第 08-11 章：技術小圖示（使用者 2026-10-28：像 RPG 技能，不同圖案代表領域、數字代表等級）──
    private const int TempBadgeSize = 52;   // TEMP：圖示邊長
    private const int TempBadgeColumns = 4; // TEMP：每列幾個

    private static readonly string[] TierNumerals = { "", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ" };

    /// <summary>TEMP 顯示配色：各領域的底色（素材交付前用）。</summary>
    private static Color DomainColor(string domain) => domain switch
    {
        "10B" => new Color(0.42f, 0.55f, 0.22f), "10C" => new Color(0.45f, 0.40f, 0.36f), "10D" => new Color(0.55f, 0.45f, 0.30f),
        "10E" => new Color(0.48f, 0.33f, 0.18f), "10F" => new Color(0.55f, 0.30f, 0.42f), "10G" => new Color(0.70f, 0.48f, 0.20f),
        "10H" => new Color(0.25f, 0.55f, 0.45f), "10I" => new Color(0.50f, 0.20f, 0.18f), "10J" => new Color(0.25f, 0.35f, 0.58f),
        "10K" => new Color(0.42f, 0.25f, 0.60f), "10L" => new Color(0.62f, 0.38f, 0.28f), "10M" => new Color(0.65f, 0.55f, 0.20f),
        "10N" => new Color(0.40f, 0.35f, 0.25f), "HUNT" => new Color(0.30f, 0.45f, 0.25f), "SOCIETY" => new Color(0.35f, 0.30f, 0.50f),
        "FAITH" => new Color(0.60f, 0.52f, 0.35f), "OPENING" => new Color(0.65f, 0.32f, 0.15f),
        _ => new Color(0.4f, 0.4f, 0.4f)
    };

    /// <summary>【TEMP】詳情面板的人像、紋章大小（像素）。</summary>
    private const float TempPortraitSize = 88f;
    private const float TempEmblemSize = 56f;

    private static TextureRect Picture(Texture2D texture, float size) => new()
    {
        Texture = texture, CustomMinimumSize = new Vector2(size, size), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = TextureFilterEnum.Nearest, MouseFilter = MouseFilterEnum.Ignore
    };

    private static Label WrapLabel(string text) => new()
    {
        Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill
    };

    /// <summary>第 08-16 章：代表人物一人一列（人像＋文字）；人像部件缺少時回傳 null，改用純文字。</summary>
    private Control? PeopleList(DemoDetailTab tab)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var person in tab.People)
        {
            var fabric = person.Portrait.CultureId > 0 ? _hexMap.CultureColor(person.Portrait.CultureId) : new Color(0.55f, 0.50f, 0.42f);
            if (Heraldry.Portrait(person.Portrait, fabric) is not { } face)
                return null;
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var frame = new PanelContainer();
            frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.24f, 0.20f, 0.16f), BorderColor = new Color(0.55f, 0.43f, 0.27f),
                BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
                CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
            });
            frame.AddChild(Picture(face, TempPortraitSize));
            row.AddChild(frame);
            row.AddChild(WrapLabel(string.Join("\n", person.Lines)));
            column.AddChild(row);
        }
        return column;
    }

    /// <summary>第 08-16 章：現況分頁上方的文化紋章與信仰符號，下面接原本的文字。</summary>
    private Control? HeraldryHeader(DemoDetailTab tab)
    {
        var icons = new HBoxContainer();
        if (tab.Emblem is { } emblem && Heraldry.Emblem(emblem, _hexMap.CultureColor(emblem.CultureId)) is { } shield)
            icons.AddChild(Picture(shield, TempEmblemSize));
        if (tab.FaithSymbol is { } symbol && Heraldry.FaithSymbol(symbol, _hexMap.FaithColor(tab.FaithId)) is { } sign)
        {
            var disc = new PanelContainer();
            disc.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.12f, 0.10f, 0.09f), CornerRadiusTopLeft = 28, CornerRadiusTopRight = 28, CornerRadiusBottomLeft = 28, CornerRadiusBottomRight = 28
            });
            disc.AddChild(Picture(sign, TempEmblemSize));
            icons.AddChild(disc);
        }
        // 第 08-16 章第 15 節：聚落現況上的「查看文化」「查看信仰」連結（只在聚落頁；文化頁、信仰頁本身不放）。
        var links = new HBoxContainer();
        if (tab.CultureId > 0 && _world.Culture.Culture(tab.CultureId) is { } culture)
        {
            var id = tab.CultureId;
            var link = new Button { Text = $"文化：{culture.Name} ▸", Flat = true, TooltipText = "查看這個文化的傳統、分布與歷史" };
            link.Pressed += () => ShowBeliefDetail(false, id);
            links.AddChild(link);
        }
        if (tab.CultureId > 0 && tab.FaithId > 0 && _world.Culture.Faith.Faith(tab.FaithId) is { } faith)
        {
            var id = tab.FaithId;
            var link = new Button { Text = $"信仰：{faith.Name} ▸", Flat = true, TooltipText = "查看這個信仰的信條、信徒與歷史" };
            link.Pressed += () => ShowBeliefDetail(true, id);
            links.AddChild(link);
        }
        if (icons.GetChildCount() == 0 && links.GetChildCount() == 0)
            return null;
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (icons.GetChildCount() > 0)
            column.AddChild(icons);
        if (links.GetChildCount() > 0)
            column.AddChild(links);
        column.AddChild(WrapLabel(string.Join("\n", tab.Lines)));
        return column;
    }

    private Control TechBadgeGrid(DemoDetailTab tab)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        GridContainer Grid(IEnumerable<DemoTechBadge> badges)
        {
            var grid = new GridContainer { Columns = TempBadgeColumns };
            grid.AddThemeConstantOverride("h_separation", 6);
            grid.AddThemeConstantOverride("v_separation", 6);
            foreach (var badge in badges)
                grid.AddChild(TechBadge(badge));
            return grid;
        }
        if (tab.KeyBadgeCount <= 0)
        {
            column.AddChild(Grid(tab.Badges));
            return column;
        }
        // 第 08-16 章（知識樹收斂顯示）：關鍵技術放最上面，其餘技術預設收合。
        column.AddChild(new Label { Text = tab.KeyTitle, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill });
        column.AddChild(Grid(tab.Badges.Take(tab.KeyBadgeCount)));
        var rest = tab.Badges.Skip(tab.KeyBadgeCount).ToList();
        if (rest.Count == 0)
            return column;
        var others = Grid(rest);
        others.Visible = false;
        var toggle = new Button { Text = $"其他技術 {rest.Count} 項 ▸", Flat = true, Alignment = HorizontalAlignment.Left };
        toggle.Pressed += () =>
        {
            others.Visible = !others.Visible;
            toggle.Text = $"其他技術 {rest.Count} 項 {(others.Visible ? "▾" : "▸")}";
        };
        column.AddChild(toggle);
        column.AddChild(others);
        return column;
    }

    private static Control TechBadge(DemoTechBadge b)
    {
        var learned = b.Percent >= 100;
        // 試玩回饋 2026-11-03 之 2：滑鼠說明寫出這項技術是什麼、目前在世界中的作用、怎麼學得快、前置。
        var tooltip = (DemoTechCatalog.Contains(b.Id) ? DemoTechTooltip.Text(DemoTechCatalog.Get(b.Id)) : b.Name) + "\n"
            + (b.Opening ? "開局時就會" : learned ? $"第 {b.LearnedYear} 年習得" : $"研究中，進度 {b.Percent}%");
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(TempBadgeSize, TempBadgeSize), TooltipText = tooltip };
        var color = DomainColor(b.Domain);
        panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = learned ? color : color.Darkened(0.45f),
            BorderColor = b.Tier >= 3 ? new Color(0.95f, 0.80f, 0.35f) : new Color(0.10f, 0.08f, 0.06f),
            BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });
        var root = new Control { MouseFilter = MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(TempBadgeSize - 4, TempBadgeSize - 4) };
        panel.AddChild(root);
        // 有領域圖示素材就貼圖，沒有就畫領域字。
        if (ArtAssets.Texture(ArtAssets.TechDomainIcon(b.Domain)) is { } icon)
        {
            var art = new TextureRect { Texture = icon, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore,
                Modulate = learned ? Colors.White : new Color(1, 1, 1, 0.55f) };
            art.SetAnchorsPreset(LayoutPreset.FullRect);
            root.AddChild(art);
        }
        else
        {
            var glyph = new Label { Text = b.Glyph, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore };
            glyph.AddThemeFontSizeOverride("font_size", 24);
            glyph.AddThemeColorOverride("font_outline_color", Colors.Black);
            glyph.AddThemeConstantOverride("outline_size", 4);
            glyph.SetAnchorsPreset(LayoutPreset.FullRect);
            root.AddChild(glyph);
        }
        if (b.Tier > 0)
        {
            var tier = new Label { Text = TierNumerals[Math.Clamp(b.Tier, 0, 5)], MouseFilter = MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom };
            tier.AddThemeFontSizeOverride("font_size", 13);
            tier.AddThemeColorOverride("font_outline_color", Colors.Black);
            tier.AddThemeConstantOverride("outline_size", 3);
            tier.SetAnchorsPreset(LayoutPreset.FullRect);
            tier.OffsetRight = -2;
            root.AddChild(tier);
        }
        if (!learned)
        {
            // 研究進度：底部一條亮條。
            var bar = new ColorRect { Color = new Color(0.98f, 0.85f, 0.40f), MouseFilter = MouseFilterEnum.Ignore };
            bar.AnchorLeft = 0; bar.AnchorTop = 1; bar.AnchorBottom = 1; bar.AnchorRight = Math.Clamp(b.Percent, 0, 100) / 100f;
            bar.OffsetTop = -4;
            root.AddChild(bar);
        }
        return panel;
    }

    private void HideDetail()
    {
        if (_detailOpen)
            PlayUi("ui_close"); // 第 08-18d 章：介面音效
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
        // 試玩回饋 2026-11-08 之 3（參考 WorldBox、十字軍之王）：頂部只留時代、人口、聚落、勢力與日期；
        // 天數、糧食庫存、本週出生與死亡只在開發資訊開啟時顯示。
        _topWorld.Text = _showDebugInfo ? $"【TEMP 示範】{_world.TempScenarioDisplayName}" : DemoKnowledge.EraName(_world.Era);
        var food = _world.Sites.Settlements.Sum(s => _world.SettlementFood(s.Id));
        var realms = _hexMap.Territory.Realm.Values.Distinct().Count();
        _topStats.Text = string.Create(CultureInfo.InvariantCulture,
            $"人口 {_world.WorldPopulation}｜聚落 {_world.Sites.Settlements.Count(x => !x.IsRuin)}｜勢力 {realms}");
        _topFood.Text = string.Create(CultureInfo.InvariantCulture, $"糧食 {food}");
        _topBirths.Text = string.Create(CultureInfo.InvariantCulture, $"出生 {births}");
        _topDeaths.Text = string.Create(CultureInfo.InvariantCulture, $"死亡 {deaths}");
        foreach (var stat in new[] { _topFood, _topBirths, _topDeaths })
            if (stat.GetParent() is Control group)
                group.Visible = _showDebugInfo;
        _topDate.Text = _showDebugInfo ? $"{DateText(days)}（第 {days} 日）" : DateText(days);
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
        if (!_topDate.Text.StartsWith(DateText(7), StringComparison.Ordinal)
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
