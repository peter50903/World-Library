using Godot;

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
    public const string TileWater = "res://assets/tiles/water.png";
    public const string TileRiver = "res://assets/tiles/river.png";

    // 地圖標記（第 07 章）
    public const string IconVillage = "res://assets/icons/icon_settlement_village.png";
    public const string IconNewSettlement = "res://assets/icons/icon_settlement_new.png";
    public const string IconMigrants = "res://assets/icons/icon_migrant_band.png";
    public const string IconBirth = "res://assets/icons/icon_birth.png";
    public const string IconDeath = "res://assets/icons/icon_death.png";
    public const string IconFood = "res://assets/icons/icon_food.png";

    // 介面（第 05 章）
    public const string UiPlay = "res://assets/ui/ui_icon_play.png";
    public const string UiPause = "res://assets/ui/ui_icon_pause.png";
    public const string UiStep = "res://assets/ui/ui_icon_step.png";
    public const string UiFast = "res://assets/ui/ui_icon_fast.png";
    public const string UiPanelFrame = "res://assets/ui/ui_panel_frame.png";
    public const string UiTopBar = "res://assets/ui/ui_topbar_bg.png";

    // 音訊
    public const string SfxClick = "res://assets/audio/sfx_click.ogg";
    public const string SfxEventAlert = "res://assets/audio/sfx_event_alert.ogg";
    public const string MusicObserverLoop = "res://assets/audio/music_observer_loop.ogg";

    /// <summary>本版畫面實際使用的全部素材（冒煙測試逐一確認讀得到）。</summary>
    public static readonly string[] Used =
    {
        TileGrassland, TileForest, TileWater, TileRiver,
        IconVillage, IconNewSettlement, IconMigrants, IconBirth, IconDeath, IconFood,
        UiPlay, UiPause, UiStep, UiFast, UiPanelFrame, UiTopBar,
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

    /// <summary>離開場景時放掉靜態快取，避免 Godot 結束時回報「resources still in use at exit」。</summary>
    public static void ReleaseCache() => Cache.Clear();

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
        return new StyleBoxTexture
        {
            Texture = frame,
            TextureMarginLeft = 18, TextureMarginRight = 18, TextureMarginTop = 18, TextureMarginBottom = 18, // TEMP：依素材邊框寬度
            ContentMarginLeft = 16, ContentMarginRight = 16, ContentMarginTop = 14, ContentMarginBottom = 14,
            AxisStretchHorizontal = StyleBoxTexture.AxisStretchMode.TileFit,
            AxisStretchVertical = StyleBoxTexture.AxisStretchMode.TileFit
        };
    }

    /// <summary>頂部資訊列木條（UI_TOPBAR_BG）；左右端蓋不拉伸。</summary>
    public static StyleBoxTexture? TopBarStyle()
    {
        if (Texture(UiTopBar) is not { } bar)
            return null;
        return new StyleBoxTexture
        {
            Texture = bar,
            TextureMarginLeft = 18, TextureMarginRight = 18, TextureMarginTop = 6, TextureMarginBottom = 6, // TEMP
            ContentMarginLeft = 26, ContentMarginRight = 26, ContentMarginTop = 4, ContentMarginBottom = 4
        };
    }
}
