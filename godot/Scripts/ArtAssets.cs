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
    // 地形圖塊（尖頂六角，第 06 章）
    public const string TileGrassland = "res://assets/tiles/grassland.png";
    public const string TileForest = "res://assets/tiles/forest.png";
    public const string TileHills = "res://assets/tiles/hills.png"; // 第 06-3 章
    public const string OverlayOreVein = "res://assets/tiles/overlay_ore_vein.png"; // 第 06-3 章
    public const string TileWater = "res://assets/tiles/water.png";
    public const string TileRiver = "res://assets/tiles/river.png";
    // 第 06-1 章：河流淺灘（渡口）；缺少時用河流圖塊調淡＋踏石。water.png 是海洋（深海圖塊）。
    public const string TileRiverShallow = "res://assets/tiles/river_shallow.png";
    public const string TileFarmland = "res://assets/tiles/farmland.png"; // 第 08 章 a 節
    public const string TileFarmlandClearing = "res://assets/tiles/farmland_clearing.png";

    // 地圖標記（第 07 章）
    public const string IconVillage = "res://assets/icons/icon_settlement_village.png";
    public const string IconNewSettlement = "res://assets/icons/icon_settlement_new.png";
    public const string IconMigrants = "res://assets/icons/icon_migrant_band.png";
    public const string IconBirth = "res://assets/icons/icon_birth.png";
    public const string IconDeath = "res://assets/icons/icon_death.png";
    public const string IconFood = "res://assets/icons/icon_food.png";
    public const string IconRuin = "res://assets/icons/icon_ruin.png"; // 第 07 章 c 節
    public const string IconDisaster = "res://assets/icons/icon_disaster.png"; // 第 12-1 章 C3

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

    /// <summary>第 08-8 章 B3：信仰模式按鈕與聖地標記。</summary>
    public const string UiMapFaith = "res://assets/ui/ui_icon_mapmode_faith.png";
    public const string IconHolySite = "res://assets/icons/icon_holy_site.png";
    /// <summary>第 06-2 章：格線開關圖示（UI_ICON_GRID）。</summary>
    public const string UiGrid = "res://assets/ui/ui_icon_grid.png";
    public const string UiChartFrame = "res://assets/ui/ui_chart_frame.png"; // 第 08 章 b 節
    public const string UiDev = "res://assets/ui/ui_icon_dev.png"; // 第 08-3 章（缺少時按鈕顯示「開發」）
    public const string UiChart = "res://assets/ui/ui_icon_chart.png"; // 第 08 章 b 節（缺少時按鈕顯示「圖表」）

    // 音訊
    public const string SfxClick = "res://assets/audio/sfx_click.ogg";
    public const string SfxEventAlert = "res://assets/audio/sfx_event_alert.ogg";
    public const string MusicObserverLoop = "res://assets/audio/music_observer_loop.ogg";

    /// <summary>本版畫面實際使用的全部素材（冒煙測試逐一確認讀得到）。</summary>
    public static readonly string[] Used =
    {
        TileGrassland, TileForest, TileWater, TileRiver, TileRiverShallow, TileFarmland, TileFarmlandClearing,
        IconVillage, IconNewSettlement, IconMigrants, IconBirth, IconDeath, IconFood, IconRuin,
        UiPlay, UiPause, UiStep, UiFast, UiPanelFrame, UiTopBar,
        UiToolbar, UiScenario, UiSave, UiHelp, UiMapTerrain, UiMapFood, UiChartFrame, UiChart, UiDev,
        UiMapLineage, UiMapKnowledge, UiGrid, // 美術收件匣 2026-10-28
        UiMapCulture, // 第 08-8 章 B1：文化模式
        UiMapFaith, IconHolySite, // 第 08-8 章 B3：信仰模式與聖地（美術收件匣 2026-10-30）
        TileHills, OverlayOreVein, // 第 06-3 章：丘陵與礦脈
        IconDisaster, // 第 12-1 章 C3：天災標記
        SfxClick, SfxEventAlert, MusicObserverLoop
    };

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
