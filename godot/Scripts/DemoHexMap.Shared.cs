using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-25 章（使用者 2026-11-18：舊的像素地圖要移除）：舊地圖的檔案移除後，政治地圖還用得到的共用部分——
/// 勢力範圍與勢力色、怪物巢穴、名牌點選、地圖符號與戰爭箭頭、音效、有東西在動時的限速重畫、色塊的滑鼠提示。只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>第 08-19 章：勢力範圍與隸屬（由觀察者介面每週注入）。</summary>
    public DemoTerritoryMap Territory { get; set; } = DemoTerritoryMap.Empty;

    /// <summary>遠看（政治地圖每格不到中距離的大小，只畫勢力名與大勢力的首都）：聚落的音效不播。</summary>
    public bool FarView => CellPx(ViewLayout().Radius) < TempMidCellPx;

    /// <summary>某勢力的代表色（政治地圖的勢力色；圖例、戰線同色）。</summary>
    public Color RealmColor(int realmId) => PoliticalRealmColor(realmId);

    /// <summary>第 12-2 章：每個地下城的怪物（由觀察者介面每次刷新時給）。</summary>
    public IReadOnlyList<DemoMonsterNest> MonsterNests { get; set; } = Array.Empty<DemoMonsterNest>();

    /// <summary>第 08-15 章：文化、信仰的名稱（滑鼠提示用；ObserverRoot 刷新時注入）。</summary>
    public IReadOnlyDictionary<int, string> CultureNames { get; set; } = new Dictionary<int, string>();
    public IReadOnlyDictionary<int, string> FaithNames { get; set; } = new Dictionary<int, string>();

    /// <summary>第 08-24 章：王國的首都（政治地圖的勢力名字寫「某某王國」；ObserverRoot 刷新時注入）。</summary>
    public IReadOnlySet<int> Kingdoms { get; set; } = new HashSet<int>();

    /// <summary>滑鼠提示：這一格的顏色代表什麼（依地圖模式）。</summary>
    public string ColorMeaning(DemoHexCell cell)
    {
        var at = new DemoCellRef(cell.Column, cell.Row);
        if (_mode is Mode.Realm or Mode.Terrain)
            return RealmMeaning(at); // 第 08-19 章
        string SiteName(int id) => Sites?.Settlements.FirstOrDefault(s => s.Id == id)?.Name ?? $"#{id}";
        if (!Lineage.TryGetValue(at, out var owner))
            return _mode == Mode.Food ? "糧食：沒有聚落" : "無人之地";
        var site = SiteName(owner.SettlementId);
        return _mode switch
        {
            Mode.Food => Sites?.SettlementAt(cell.Column, cell.Row) is { IsRuin: false } here
                ? "糧食：" + (FoodStatuses.TryGetValue(here.Id, out var s) ? s : FoodStatus.Unsettled) switch
                {
                    FoodStatus.Ample => "充足",
                    FoodStatus.Tight => "緊張",
                    FoodStatus.Starving => "飢餓",
                    _ => "尚未結算"
                } + $"（{here.Name}）"
                : $"{site}的領域（糧食模式只標聚落格）",
            Mode.Culture => $"文化：{CultureNames.GetValueOrDefault(CultureOf.GetValueOrDefault(owner.SettlementId), "未定")}（{site}）",
            Mode.Faith => FaithOf.GetValueOrDefault(owner.SettlementId) is var f and > 0
                ? $"信仰：{FaithNames.GetValueOrDefault(f, $"#{f}")}（{site}）" + (HolySites.Contains(owner.SettlementId) ? "｜聖地" : "")
                : $"無信仰（{site}）",
            Mode.Knowledge => (KnowledgeDomain.GetValueOrDefault(owner.SettlementId) is { } d ? $"擅長：{DemoPlayerDetail.DomainName(d)}，" : "")
                + $"已習得 {KnowledgeCounts.GetValueOrDefault(owner.SettlementId)} 項技術（{site}）",
            _ => $"{SiteName(owner.LineageRootId)}一系的領域（屬於 {site}）"
        };
    }

    /// <summary>勢力模式的滑鼠提示：這一格屬於哪個聚落、隸屬誰、最上層是誰。</summary>
    private string RealmMeaning(DemoCellRef at)
    {
        if (!Territory.Cells.TryGetValue(at, out var owner))
            return "無主之地";
        string Name(int id) => Sites?.Settlements.FirstOrDefault(s => s.Id == id) is { } s && SettlementScale.TryGetValue(id, out var scale)
            ? DemoSettlementHistory.TitledName(s, scale.Item2) : $"#{id}";
        return owner.RealmId == owner.SettlementId
            ? $"{Name(owner.SettlementId)}的勢力範圍"
            : $"{Name(owner.SettlementId)}的勢力範圍（隸屬於{Name(Territory.LiegeOf(owner.SettlementId) ?? owner.RealmId)}，最上層：{Name(owner.RealmId)}）";
    }

    /// <summary>
    /// 第 08-18i 章（GPT 試玩回饋 06）：滑鼠移到圖例某一項時突顯的那一群（勢力＝最上層聚落、家系＝源頭聚落、文化、信仰的編號；null＝不突顯）。
    /// 第 08-25 章：政治地圖用選取時的提亮與金邊畫出來（優先於點選選到的那一群）。
    /// </summary>
    public int? FocusGroup
    {
        get => _focusGroup;
        set
        {
            if (_focusGroup == value)
                return;
            _focusGroup = value;
            QueueRedraw();
        }
    }

    private int? _focusGroup;

    /// <summary>第 08-18i 章：這一幀畫出的名牌（範圍、聚落），點選時最先算。</summary>
    private readonly List<(Rect2 Box, int SiteId)> _plateHits = new();

    /// <summary>滑鼠所在的名牌（最後畫的在最上面）；沒有回傳 null。</summary>
    private int? PlateUnder(Vector2 position)
    {
        for (var i = _plateHits.Count - 1; i >= 0; i--)
            if (_plateHits[i].Box.HasPoint(position))
                return _plateHits[i].SiteId;
        return null;
    }

    private int? _hoverPlate;

    /// <summary>陸地格（第 0～N 格的編號）。</summary>
    private bool IsLand(int cell) => World!.Cells[cell].Terrain != DemoTerrain.Water;

    /// <summary>位置雜湊（-1～1）：只用於外形，與世界亂數無關。</summary>
    private static float Hash(int x, int y, int salt)
    {
        var v = Mathf.Sin(x * 12.9898f + y * 78.233f + salt * 37.719f) * 43758.5453f;
        return (v - Mathf.Floor(v)) * 2f - 1f;
    }

    /// <summary>某格中心在畫面上的位置。</summary>
    private Vector2 ScreenOf(Vector2 start, float radius, DemoCellRef c)
    {
        var cell = World!.GetCell(c.Column, c.Row);
        return start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
    }

    /// <summary>人數標籤：千以上寫「1.2千」、萬以上寫「3.4萬」。</summary>
    public static string TokenCount(long people) => people < 1_000 ? people.ToString()
        : people < 10_000 ? $"{people / 100 / 10f:0.#}千" : $"{people / 1_000 / 10f:0.#}萬";

    /// <summary>地圖符號：素材在就畫在 <paramref name="center"/> 正中（邊長 <paramref name="size"/> 螢幕像素、可染色）；沒有回傳 false。</summary>
    private bool DrawSymbol(string name, Vector2 center, float size, Color modulate)
    {
        if (!PixelParts.IsBatch5(name) || PixelParts.Texture(name) is not { } texture)
            return false;
        var aspect = texture.GetWidth() / (float)Math.Max(1, texture.GetHeight());
        var s = new Vector2(size * aspect, size);
        DrawTextureRect(texture, new Rect2(center - s / 2f, s), false, modulate);
        return true;
    }

    /// <summary>戰爭箭頭的箭頭端（第十一批旗箭，順著箭頭方向旋轉）；沒有素材回傳 false。</summary>
    private bool DrawArrowHead(Vector2 tip, Vector2 dir, float size, float alpha)
    {
        if (!PixelParts.IsBatch5("b11_sym_arrow_head") || PixelParts.Texture("b11_sym_arrow_head") is not { } head)
            return false;
        DrawSetTransform(tip, dir.Angle(), Vector2.One);
        DrawTextureRect(head, new Rect2(-size, -size / 2f, size, size), false, new Color(1f, 1f, 1f, alpha));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        return true;
    }

    /// <summary>無畫面模式與冒煙測試靜音（由觀察者介面設定）。</summary>
    public bool AudioMuted { get; set; }

    /// <summary>【TEMP】音效音量（dB）；同一種音效至少隔幾毫秒、全部音效至少隔幾毫秒（試玩回饋 2026-11-04 之 2：音效好吵）。</summary>
    public const float TempSfxVolumeDb = -16f;
    public const ulong TempSfxMinGapMs = 8000;
    public const ulong TempSfxAnyGapMs = 1500;

    private readonly Dictionary<string, ulong> _sfxLastMs = new();
    private ulong _sfxAnyLastMs;

    /// <summary>播一次音效（每次一個播放器，播完自行移除）；太密的略過。</summary>
    public void PlaySfx(string path)
    {
        if (AudioMuted || !IsInsideTree() || ArtAssets.Audio(path) is not { } stream)
            return;
        var now = Time.GetTicksMsec();
        if ((_sfxLastMs.TryGetValue(path, out var last) && now - last < TempSfxMinGapMs) || (_sfxAnyLastMs > 0 && now - _sfxAnyLastMs < TempSfxAnyGapMs))
            return;
        _sfxLastMs[path] = now;
        _sfxAnyLastMs = now;
        var player = new AudioStreamPlayer { Stream = stream, VolumeDb = TempSfxVolumeDb };
        AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
    }

    /// <summary>【TEMP】有東西在動時，最快幾毫秒重畫一次。</summary>
    public const ulong TempAnimateEveryMs = 66;

    private ulong _lastAnimatedMs;

    /// <summary>
    /// 第 09-1e 章：這一次重畫時，畫面內有沒有畫到「隨真實時間動」的東西——戰線（行軍虛線、軍旗）、商路。
    /// 每次重畫開頭清掉，畫到時設起來。
    /// </summary>
    private bool _animatedOnScreen;

    public override void _Process(double delta)
    {
        PerfMonitors.EndFrame(); // 第 09-1e 章：每幀結算監看值
        if (_provincesStale && Time.GetTicksMsec() - _provincesBuiltMs >= TempProvinceRefreshMs)
            QueueRedraw(); // 第 08-21 章：快轉時省份延後重算的那一次
        if (!_animatedOnScreen && !_battleAnimating)
            return;
        var now = Time.GetTicksMsec();
        if (now - _lastAnimatedMs < TempAnimateEveryMs)
            return; // 第 08-16 章：動畫限速重畫（大地圖不每幀重畫）
        _lastAnimatedMs = now;
        QueueRedraw();
    }
}
