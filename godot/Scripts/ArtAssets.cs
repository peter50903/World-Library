using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 素材讀取（素材清單 assets/manifest.csv）。圖片與音訊放在 res://assets/ 底下，由 Godot 匯入；
/// 找不到或尚未匯入就回傳 null，畫面自動退回純色繪製，素材永遠不會讓遊戲無法啟動。
/// 只負責呈現，不影響世界。
/// </summary>
public static class ArtAssets
{
    // 地形圖塊（第 06 章；第 08-29 章：舊地圖的地形、天災、遷徙素材已刪）
    public const string TileGrassland = "res://assets/tiles/grassland.png";

    // 地圖標記（第 07 章）
    public const string IconVillage = "res://assets/icons/icon_settlement_village.png";
    public const string IconBirth = "res://assets/icons/icon_birth.png";
    public const string IconDeath = "res://assets/icons/icon_death.png";
    public const string IconFood = "res://assets/icons/icon_food.png";

    /// <summary>第 08-16 章：天災與事件音效（美術收件匣 2026-11-04）。</summary>
    public const string SfxFire = "res://assets/audio/sfx_fire.ogg";
    public const string SfxEarthquake = "res://assets/audio/sfx_earthquake.ogg";
    public const string SfxMeteor = "res://assets/audio/sfx_meteor.ogg";
    public const string SfxFlood = "res://assets/audio/sfx_flood.ogg";
    public const string SfxTornado = "res://assets/audio/sfx_tornado.ogg";
    public const string SfxFounding = "res://assets/audio/sfx_founding.ogg";
    public const string SfxEra = "res://assets/audio/sfx_era.ogg";

    /// <summary>第 08-11 章：技術領域圖示（技術分頁的方形小圖示；未交付時畫領域字）。</summary>
    public static string TechDomainIcon(string domain) => $"res://assets/icons/tech_{domain.ToLowerInvariant()}.png";

    /// <summary>第 08-11 章：居住地五級的地圖圖示（村莊沿用 icon_settlement_village.png）。</summary>
    public static string SettlementTierIcon(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp => "res://assets/icons/icon_settlement_camp.png",
        DemoSettlementTier.Village => IconVillage,
        DemoSettlementTier.Town => "res://assets/icons/icon_settlement_town.png",
        DemoSettlementTier.City => "res://assets/icons/icon_settlement_city.png",
        _ => "res://assets/icons/icon_settlement_capital.png"
    };

    // 介面（第 05 章）
    public const string UiPlay = "res://assets/ui/ui_icon_play.png";
    public const string UiPause = "res://assets/ui/ui_icon_pause.png";
    public const string UiStep = "res://assets/ui/ui_icon_step.png";
    public const string UiFast = "res://assets/ui/ui_icon_fast.png";
    public const string UiPanelFrame = "res://assets/ui/ui_panel_frame.png";
    public const string UiTopBar = "res://assets/ui/ui_topbar_bg.png";
    public const string UiToolbar = "res://assets/ui/ui_toolbar_bg.png";
    public const string UiScenario = "res://assets/ui/ui_icon_scenario.png";
    public const string UiSave = "res://assets/ui/ui_icon_save.png";
    public const string UiHelp = "res://assets/ui/ui_icon_help.png";
    public const string UiMapTerrain = "res://assets/ui/ui_icon_mapmode_terrain.png";
    public const string UiMapFood = "res://assets/ui/ui_icon_mapmode_food.png";
    /// <summary>第 08-10a 章 V2：家系地圖模式圖示（UI_ICON_MAPMODE_LINEAGE）。</summary>
    public const string UiMapLineage = "res://assets/ui/ui_icon_mapmode_lineage.png";
    /// <summary>第 08-10a 章 V4：知識地圖模式圖示（UI_ICON_MAPMODE_KNOWLEDGE）。</summary>
    public const string UiMapKnowledge = "res://assets/ui/ui_icon_mapmode_knowledge.png";
    /// <summary>第 08-10a 章 V5：文化地圖模式圖示（UI_ICON_MAPMODE_CULTURE；文化模式完成前不用）。</summary>
    public const string UiMapCulture = "res://assets/ui/ui_icon_mapmode_culture.png";

    /// <summary>第 08-8 章 B3：信仰模式按鈕。</summary>
    public const string UiMapFaith = "res://assets/ui/ui_icon_mapmode_faith.png";
    public const string UiChartFrame = "res://assets/ui/ui_chart_frame.png"; // 第 08 章 b 節
    public const string UiDev = "res://assets/ui/ui_icon_dev.png"; // 第 08-3 章（缺少時按鈕顯示「開發」）
    public const string UiChart = "res://assets/ui/ui_icon_chart.png"; // 第 08 章 b 節（缺少時按鈕顯示「圖表」）
    public const string UiLaws = "res://assets/ui/ui_icon_laws.png"; // 第 08-19c 章（缺少時按鈕顯示「法則」）
    public const string UiDevTools = "res://assets/ui/ui_icon_devtools.png"; // 第 05-2 章（UI_ICON_DEVTOOLS，缺少時按鈕顯示「開發工具」）

    // 音訊
    public const string SfxClick = "res://assets/audio/sfx_click.ogg";
    public const string SfxEventAlert = "res://assets/audio/sfx_event_alert.ogg";
    public const string MusicObserverLoop = "res://assets/audio/music_observer_loop.ogg";

    /// <summary>第 08-18d 章：第五、六批音樂與音效（res://assets/audio/music2、sfx2）。</summary>
    public static string Sfx2(string name) => $"res://assets/audio/sfx2/{name}.ogg";
    public static string Music2(string name) => $"res://assets/audio/music2/{name}.ogg";

    /// <summary>新的音效有就用新的，沒有退回舊的。</summary>
    public static string Prefer(string fresh, string old) => ResourceLoader.Exists(fresh) ? fresh : old;

    /// <summary>第 08-18d 章：各時代的背景音樂（一首播完換下一首；開頭先播主題曲）。</summary>
    public static IReadOnlyList<string> MusicFor(DemoEra era) => era switch
    {
        DemoEra.Hope => new[] { Music2("music_hope_1"), Music2("music_hope_2"), Music2("music_wonder") },
        DemoEra.Pioneer => new[] { Music2("music_pioneer_1"), Music2("music_pioneer_2"), Music2("music_chronicle") },
        _ => new[] { Music2("music_conquest_1"), Music2("music_conquest_2"), Music2("music_tension") }
    };

    public static string MusicTheme => Music2("music_theme");

    /// <summary>事件音效（第五批；缺少時退回第四批）。</summary>
    public static string EventSfx(DemoSettlementEventKind kind) => kind switch
    {
        DemoSettlementEventKind.Earthquake => Prefer(Sfx2("sfx_quake"), SfxEarthquake),
        DemoSettlementEventKind.Meteorite => Prefer(Sfx2("sfx_meteor"), SfxMeteor),
        DemoSettlementEventKind.Flood => Prefer(Sfx2("sfx_flood"), SfxFlood),
        DemoSettlementEventKind.Tornado => Prefer(Sfx2("sfx_tornado"), SfxTornado),
        DemoSettlementEventKind.SettlementFounded => Prefer(Sfx2("sfx_found_village"), SfxFounding),
        DemoSettlementEventKind.EraBegan => Prefer(Sfx2("sfx_era"), SfxEra),
        DemoSettlementEventKind.SettlementRuined => Sfx2("sfx_ruin"),
        DemoSettlementEventKind.CultureSplit => Sfx2("sfx_culture_split"),
        DemoSettlementEventKind.FaithFounded => Sfx2("sfx_faith"),
        DemoSettlementEventKind.KnowledgeLearned => Sfx2("sfx_tech"),
        DemoSettlementEventKind.BeastAttack => Sfx2("sfx_beast"),
        DemoSettlementEventKind.MonsterRaid or DemoSettlementEventKind.MonsterHunt or DemoSettlementEventKind.MonsterSwarm => Prefer(Sfx2("sfx_monster"), Sfx2("sfx_beast")), // 第 08-18h 章
        DemoSettlementEventKind.SecessionBattle or DemoSettlementEventKind.ConquestBattle => Prefer(Sfx2("sfx_war"), Sfx2("sfx_beast")), // 第 08-19b 章
        DemoSettlementEventKind.VassalJoined or DemoSettlementEventKind.Independence or DemoSettlementEventKind.Conquered => Sfx2("sfx_vassal"),
        _ => Prefer(Sfx2("sfx_tornado"), SfxTornado)
    };

    public static string FireLoop => Prefer(Sfx2("amb_fire"), SfxFire);
    public static string UiClick => Prefer(Sfx2("ui_click"), SfxClick);

    /// <summary>實際用到的第五、六批音訊（冒煙測試確認讀得到）。</summary>
    private static IEnumerable<string> Batch5Audio => new[] { DemoEra.Hope, DemoEra.Pioneer, (DemoEra)2 }.SelectMany(MusicFor).Append(MusicTheme)
        .Concat(new[] { "sfx_quake", "sfx_meteor", "sfx_flood", "sfx_tornado", "sfx_found_village", "sfx_era", "sfx_ruin", "sfx_culture_split",
            "sfx_faith", "sfx_tech", "sfx_beast", "amb_fire", "ui_click", "ui_open", "ui_close", "ui_tab" }.Select(Sfx2));

    /// <summary>本版畫面實際使用的全部素材（冒煙測試逐一確認讀得到；第 08-29 章：tools/asset_usage.py --check 另外確認登記的都有人用）。</summary>
    public static readonly string[] Used =
    new[]
    {
        TileGrassland, IconVillage, IconBirth, IconDeath, IconFood, UiPlay, UiPause, UiStep, UiFast, UiPanelFrame, UiTopBar,
        UiToolbar, UiScenario, UiSave, UiHelp, UiMapTerrain, UiMapFood, UiChartFrame, UiChart, UiDev,
        UiMapLineage, UiMapKnowledge, // 美術收件匣 2026-10-28
        UiMapCulture, // 第 08-8 章 B1：文化模式
        UiMapFaith, // 第 08-8 章 B3：信仰模式（美術收件匣 2026-10-30）
        SfxFire, SfxEarthquake, SfxMeteor, SfxFlood, SfxTornado, SfxFounding, SfxEra, // 第 08-16 章：天災與事件音效
        SfxClick, SfxEventAlert, MusicObserverLoop
    }.Concat(PixelParts.Batch5.Select(PixelParts.Path)) // 第 08-18d 章：像素素材（第 08-29 章：只留用到與保留的）
        .Concat(Batch5Audio)
        .Concat(Heraldry.AllParts()) // 第 08-16 章：人像、紋章、信仰符號部件
        .Concat(new[] { ObserverRoot.EraArt(DemoEra.Hope), ObserverRoot.EraArt(DemoEra.Pioneer), ObserverRoot.EraArt((DemoEra)2) }) // 第 08-16 章：時代插圖
        .ToArray();

    private static readonly Dictionary<string, Resource?> Cache = new();

    private static T? Load<T>(string path) where T : Resource
    {
        if (!Cache.TryGetValue(path, out var resource))
        {
            resource = ResourceLoader.Exists(path) ? ResourceLoader.Load(path) : null;
            Cache[path] = resource;
        }
        return resource as T;
    }

    private static readonly List<Resource> Created = new();

    /// <summary>登記由程式新建的資源（樣式、主題），離開場景時一併釋放。</summary>
    public static T Track<T>(T resource) where T : Resource
    {
        Created.Add(resource);
        return resource;
    }

    /// <summary>
    /// 離開場景時釋放 C# 端持有的資源參照。Godot .NET 的包裝物件在被 Dispose 前會一直佔一個參照，
    /// 只清字典不夠，結束時會回報「resources still in use at exit」。節點自己的參照不受影響。
    /// </summary>
    public static void ReleaseCache()
    {
        PixelParts.ReleaseCache();
        HoldingView.ReleaseCache(); // 第 08-33 章：領地圖的排版快取
        foreach (var resource in Cache.Values.Where(r => r is not null).Concat(Created))
            resource!.Dispose();
        Cache.Clear();
        Created.Clear();
    }

    public static Texture2D? Texture(string path) => Load<Texture2D>(path);

    public static AudioStream? Audio(string path) => Load<AudioStream>(path);

    /// <summary>讀得到的已使用素材數量（冒煙測試用）。</summary>
    public static int LoadedUsedCount()
        => Used.Count(p => p.EndsWith(".ogg", StringComparison.Ordinal) ? Audio(p) is not null : Texture(p) is not null);

    /// <summary>九宮格面板（UI_PANEL_FRAME）；素材缺少時回傳 null，由呼叫端改用純色樣式。</summary>
    public static StyleBoxTexture? PanelStyle()
    {
        if (Texture(UiPanelFrame) is not { } frame)
            return null;
        return Track(new StyleBoxTexture
        {
            Texture = frame,
            TextureMarginLeft = 18, TextureMarginRight = 18, TextureMarginTop = 18, TextureMarginBottom = 18, // TEMP：依素材邊框寬度
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 14, ContentMarginBottom = 14,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.TileFit,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.TileFit
        });
    }

    /// <summary>底部工具列／地圖模式列木板（UI_TOOLBAR_BG）；左右端蓋不拉伸。</summary>
    public static StyleBoxTexture? ToolbarStyle()
    {
        if (Texture(UiToolbar) is not { } board)
            return null;
        return Track(new StyleBoxTexture
        {
            Texture = board,
            TextureMarginLeft = 24, TextureMarginRight = 24, TextureMarginTop = 10, TextureMarginBottom = 10, // TEMP：依素材邊框
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 4, ContentMarginBottom = 4
        });
    }

    /// <summary>第 08 章 b 節：年度圖表外框（UI_CHART_FRAME）；中央留空給長條圖。</summary>
    public static StyleBoxTexture? ChartFrameStyle()
    {
        if (Texture(UiChartFrame) is not { } frame)
            return null;
        return Track(new StyleBoxTexture
        {
            Texture = frame,
            TextureMarginLeft = 44, TextureMarginRight = 44, TextureMarginTop = 40, TextureMarginBottom = 40, // TEMP：依素材包角
            ContentMarginLeft = 30, ContentMarginRight = 30, ContentMarginTop = 28, ContentMarginBottom = 28
        });
    }

    /// <summary>頂部資訊列木條（UI_TOPBAR_BG）；左右端蓋不拉伸。</summary>
    public static StyleBoxTexture? TopBarStyle()
    {
        if (Texture(UiTopBar) is not { } bar)
            return null;
        return Track(new StyleBoxTexture
        {
            Texture = bar,
            TextureMarginLeft = 18, TextureMarginRight = 18, TextureMarginTop = 6, TextureMarginBottom = 6, // TEMP
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 4, ContentMarginBottom = 4
        });
    }
}
