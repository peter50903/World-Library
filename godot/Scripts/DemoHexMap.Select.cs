using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 試玩回饋 2026-11-07 之 2（點選的層級，照 WorldBox）：
/// <list type="bullet">
/// <item>拉遠（區域層、戰略層）：地形與勢力模式點到勢力（開勢力詳情，看得到整棵隸屬樹）；文化模式點到文化、信仰模式點到信仰；其他模式照舊點格。</item>
/// <item>點了之後鏡頭先把選到的東西移到畫面中央（勢力移到最上層的聚落）並框起來。</item>
/// </list>
/// 只改畫面與介面，不改世界。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>遠看點到勢力（最上層聚落編號）、文化、信仰時通知觀察者介面。</summary>
    public event Action<int>? RealmSelected;
    public event Action<int>? CultureSelected;
    public event Action<int>? FaithSelected;

    /// <summary>
    /// 試玩回饋 2026-11-08 第二次之 2：點選由小到大、先點到的先算（各種地圖模式、各種遠近都一樣；第 08-25 章：舊地圖的角色、建築已移除）。
    /// 第 08-31 章（使用者 2026-11-19：點選時不要圈出正方形的地塊，要以勢力範圍圈選；取消選擇地塊）：
    /// <list type="number">
    /// <item><b>名牌、聚落</b>：名牌（整條）或聚落所在的格 → 聚落，框出它的勢力範圍。</item>
    /// <item><b>怪物的地盤</b>（地形、勢力模式）→ 地下城，框出整片地盤。</item>
    /// <item><b>勢力範圍裡的空地</b>：看目前的地圖模式——勢力＝勢力、文化＝文化、信仰＝信仰、其他＝這片範圍的聚落；
    ///   在同一處再點，依序換到其他層級（聚落→勢力，文化、信仰模式再加上文化、信仰）。</item>
    /// <item>荒野與海：不選。</item>
    /// </list>
    /// 試玩回饋 2026-11-08 第四次之 1：在地圖上點選時鏡頭不動，只把選到的框起來（從清單、編年史、連結打開時才移到畫面中央）。
    /// </summary>
    private void Select(Vector2 position, DemoHexCell hit)
    {
        var at = new DemoCellRef(hit.Column, hit.Row);
        // ⓪ 第 08-18i 章：名牌（整條都算）固定開聚落。
        if (PlateUnder(position) is { } plateSite && SiteCell(plateSite) is { } plated)
        {
            _areaPick = null;
            MarkCell(plated);
            CellSelected?.Invoke(plated);
            return;
        }
        // ① 聚落
        if (VisibleSite(hit) is not null)
        {
            _areaPick = null;
            MarkCell(hit);
            CellSelected?.Invoke(hit);
            return;
        }
        // ② 怪物的地盤＝地下城（框出整片地盤）。
        if (MonsterNestAt(at) is { } nest && _mode is Mode.Terrain or Mode.Realm && World!.Contains(nest.Dungeon.Cell.Column, nest.Dungeon.Cell.Row))
        {
            _areaPick = null;
            var den = World.GetCell(nest.Dungeon.Cell.Column, nest.Dungeon.Cell.Row);
            MarkCell(den);
            _selectedNest = nest.Dungeon.EventId;
            CellSelected?.Invoke(den);
            return;
        }
        // ③ 勢力範圍：依地圖模式決定第一下選到什麼；在同一處再點，依序換到其他層級（第 05-1 章）。
        SelectArea(at);
    }

    /// <summary>上一次點到的空地格與這一格已經點了第幾下（換一格、換地圖模式或點到別的東西就重來）。</summary>
    private (DemoCellRef Cell, Mode Mode, int Clicks)? _areaPick;

    /// <summary>第 08-31 章：選到的怪物地盤（地下城的事件編號；0＝沒有），整片框起來。</summary>
    private long _selectedNest;

    /// <summary>
    /// 第 05-1 章（使用者 2026-11-17：選取地塊、勢力不順手）：空地的每一下點選依序選到這一處的各個層級，不必先換地圖模式——
    /// 所屬的聚落、所屬的勢力（聚落本身就是最上層時不重複），文化與信仰模式再加上文化、信仰。
    /// 第一下選到的照目前的地圖模式（勢力＝勢力、文化＝文化、信仰＝信仰、其他＝所屬聚落）；在同一處再點一下換下一個層級，輪完回到第一個。
    /// 第 08-31 章：不再選地塊；不在任何勢力範圍裡（荒野、海）就不選。
    /// </summary>
    private void SelectArea(DemoCellRef at)
    {
        var owner = OwnerAt(at); // 第 08-21 章：政治地圖每格陸地都屬於一個省份
        var site = owner?.SettlementId ?? (Lineage.TryGetValue(at, out var line) ? (int?)line.SettlementId : null);
        Action? settlement = site is { } s && SiteCell(s) is { } home ? () => { MarkCell(home); CellSelected?.Invoke(home); } : null;
        Action? realm = owner is { } own && RealmSelected is not null && SiteCell(own.RealmId) is { } capital
            ? () => { MarkCell(capital); RealmSelected?.Invoke(own.RealmId); _selectedRealm = own.RealmId; } : null;
        Action? culture = site is { } c && CultureOf.TryGetValue(c, out var cultureId) && CultureSelected is not null && SiteCell(c) is { } cultureHome
            ? () => { MarkCell(cultureHome); CultureSelected?.Invoke(cultureId); _selectedCulture = cultureId; } : null;
        Action? faith = site is { } f && FaithOf.TryGetValue(f, out var faithId) && FaithSelected is not null && SiteCell(f) is { } faithHome
            ? () => { MarkCell(faithHome); FaithSelected?.Invoke(faithId); _selectedFaith = faithId; } : null;
        // 第一下照地圖模式；之後由小到大。
        Action? preferred = _mode switch
        {
            Mode.Realm => realm,
            Mode.Culture => culture,
            Mode.Faith => faith,
            _ => settlement
        };
        var levels = new List<Action>();
        foreach (var next in new[] { preferred, settlement, realm, _mode == Mode.Culture ? culture : null, _mode == Mode.Faith ? faith : null })
            if (next is not null && !levels.Contains(next))
                levels.Add(next);
        if (levels.Count == 0)
        {
            _areaPick = null; // 荒野、海：不選
            return;
        }
        var clicks = _areaPick is { } last && last.Cell == at && last.Mode == _mode ? last.Clicks + 1 : 0;
        _areaPick = (at, _mode, clicks);
        levels[clicks % levels.Count]();
    }

    /// <summary>這一格在哪個地下城怪物的勢力範圍內（最近的；沒有回傳 null）。</summary>
    private DemoMonsterNest? MonsterNestAt(DemoCellRef at)
        => MonsterNests.Where(n => n.Cells.Contains(at))
            .OrderBy(n => DemoHexWorld.Distance(n.Dungeon.Cell, at)).ThenBy(n => n.Dungeon.EventId).FirstOrDefault();

    private DemoHexCell? SiteCell(int siteId)
        => Sites?.Settlements.FirstOrDefault(s => s.Id == siteId) is { } s && World!.Contains(s.Column, s.Row) ? World.GetCell(s.Column, s.Row) : null;

    /// <summary>記下選到的格（政治地圖框出它的勢力範圍；第 08-31 章起不再畫格子的方框），鏡頭不動。</summary>
    private void MarkCell(DemoHexCell cell)
    {
        Highlight = (cell.Column, cell.Row);
        QueueRedraw();
    }
}
