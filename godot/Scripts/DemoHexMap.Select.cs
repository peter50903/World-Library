using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 試玩回饋 2026-11-07 之 2（點選的層級，照 WorldBox）：
/// <list type="bullet">
/// <item>拉遠（區域層、戰略層）：地形與勢力模式點到勢力（開勢力詳情，看得到整棵隸屬樹）；文化模式點到文化、信仰模式點到信仰；其他模式照舊點格。</item>
/// <item>拉近（近景）：點到小人或建築就開那個聚落的詳情，其餘照舊點格（地下城、森林、礦脈…）。</item>
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

    /// <summary>這一幀畫出來的小人（腳的位置、聚落），供點選。</summary>
    private readonly List<(Vector2 At, int SiteId)> _walkerHits = new();

    /// <summary>試玩回饋 2026-11-08 之 6：地圖上可以點的角色——聚落的領袖、工作團體、遷徙隊伍、地下城的怪物。</summary>
    public enum ActorKind { Leader, WorkGroup, Migrants, Monster, Settlement }

    /// <summary>一個可以點的角色：種類、所屬聚落、職業（工作團體）、人數、所在或工作的格、隊伍編號、地下城事件編號。</summary>
    public sealed record MapActor(ActorKind Kind, int SettlementId, TokenKind Work, long People, DemoCellRef Cell, int BatchId = 0, long DungeonEventId = 0);

    /// <summary>近景點到角色時通知觀察者介面。</summary>
    public event Action<MapActor>? ActorSelected;

    /// <summary>這一幀畫出來的角色（點得到的範圍），供點選；每次重畫地圖時清空。</summary>
    private readonly List<(Rect2 Box, MapActor Actor)> _actorHits = new();

    /// <summary>【TEMP】角色的點選範圍至少多大（螢幕像素）：小人只有十幾個像素，又在走動，點選範圍要比圖大。</summary>
    private const float TempActorMinWidth = 22f, TempActorMinHeight = 30f;

    /// <summary>登記一個站在 <paramref name="feet"/> 的角色（高 <paramref name="texels"/> 個圖像素）。</summary>
    private void AddActor(Vector2 feet, float radius, MapActor actor, float texels = 22f)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        var w = Math.Max(TempActorMinWidth, 18f * p);
        var h = Math.Max(TempActorMinHeight, (texels + 4f) * p);
        _actorHits.Add((new Rect2(feet.X - w / 2f, feet.Y + 4f - h, w, h), actor));
    }

    /// <summary>滑鼠下的角色（範圍內離中心最近的；同樣近時畫在前面的優先）。</summary>
    private MapActor? ActorUnder(Vector2 position)
        => _actorHits.Where(a => a.Box.HasPoint(position))
            .OrderBy(a => a.Box.GetCenter().DistanceSquaredTo(position)).ThenByDescending(a => a.Box.End.Y)
            .Select(a => a.Actor).FirstOrDefault();

    /// <summary>
    /// 試玩回饋 2026-11-08 第二次之 2：點選只有三層，由小到大、先點到的先算（各種地圖模式、各種遠近都一樣）：
    /// <list type="number">
    /// <item><b>角色</b>：近景的領袖、工作團體、遷徙隊伍、怪物；遠看的棋子（居民棋＝聚落、職業棋＝工作團體、遷徙棋＝隊伍）。</item>
    /// <item><b>聚落</b>：聚落所在的格；近景再加建築、廣場。</item>
    /// <item><b>區域</b>（空地）：看目前的地圖模式——地形＝地塊、勢力＝勢力（最上層聚落的「勢力」頁）、文化＝文化、信仰＝信仰、
    ///   其他＝所屬聚落；怪物的勢力範圍＝地下城。</item>
    /// </list>
    /// 詳情上方有一列「往上一層」的連結（地塊→聚落→勢力、文化、信仰），點過去不必回到地圖。
    /// 試玩回饋 2026-11-08 第四次之 1：在地圖上點選時鏡頭不動，只把選到的框起來（從清單、編年史、連結打開時才移到畫面中央）。
    /// </summary>
    private void Select(Vector2 position, DemoHexCell hit)
    {
        var (start, radius) = ViewLayout();
        var at = new DemoCellRef(hit.Column, hit.Row);
        // ⓪ 第 08-18i 章：名牌（整條都算）固定開聚落。
        if (PlateUnder(position) is { } plateSite && SiteCell(plateSite) is { } plated)
        {
            MarkCell(plated);
            CellSelected?.Invoke(plated);
            return;
        }
        // ① 角色
        if (ActorUnder(position) is { } actor)
        {
            if (World!.Contains(actor.Cell.Column, actor.Cell.Row))
                MarkCell(World.GetCell(actor.Cell.Column, actor.Cell.Row));
            if (actor.Kind == ActorKind.Settlement && SiteCell(actor.SettlementId) is { } home)
            {
                MarkCell(home);
                CellSelected?.Invoke(home);
            }
            else
                ActorSelected?.Invoke(actor);
            return;
        }
        // ② 聚落
        if (VisibleSite(hit) is not null)
        {
            MarkCell(hit);
            CellSelected?.Invoke(hit);
            return;
        }
        if (LayerFor(radius) == ViewLayer.Local && SettlementUnder(position, start, radius) is { } building)
        {
            MarkCell(building);
            CellSelected?.Invoke(building);
            return;
        }
        // ③ 區域：依地圖模式
        var owner = Territory.Cells.TryGetValue(at, out var o) ? o : null;
        var site = owner?.SettlementId ?? (Lineage.TryGetValue(at, out var line) ? (int?)line.SettlementId : null);
        if (MonsterNestAt(at) is { } nest && _mode is Mode.Terrain or Mode.Realm && World!.Contains(nest.Dungeon.Cell.Column, nest.Dungeon.Cell.Row))
            hit = World.GetCell(nest.Dungeon.Cell.Column, nest.Dungeon.Cell.Row);
        else
            switch (_mode)
            {
                case Mode.Realm when owner is not null && RealmSelected is not null && SiteCell(owner.RealmId) is { } capital:
                    MarkCell(capital);
                    RealmSelected(owner.RealmId);
                    return;
                case Mode.Culture when site is { } c && CultureOf.TryGetValue(c, out var culture) && CultureSelected is not null:
                    MarkCell(hit);
                    CultureSelected(culture);
                    return;
                case Mode.Faith when site is { } f && FaithOf.TryGetValue(f, out var faith) && FaithSelected is not null:
                    MarkCell(hit);
                    FaithSelected(faith);
                    return;
                case Mode.Food or Mode.Lineage or Mode.Knowledge when site is { } s && SiteCell(s) is { } settlement:
                    hit = settlement;
                    break;
            }
        MarkCell(hit);
        CellSelected?.Invoke(hit);
    }

    /// <summary>這一格在哪個地下城怪物的勢力範圍內（最近的；沒有回傳 null）。</summary>
    private DemoMonsterNest? MonsterNestAt(DemoCellRef at)
        => MonsterNests.Where(n => n.Cells.Contains(at))
            .OrderBy(n => DemoHexWorld.Distance(n.Dungeon.Cell, at)).ThenBy(n => n.Dungeon.EventId).FirstOrDefault();

    private DemoHexCell? SiteCell(int siteId)
        => Sites?.Settlements.FirstOrDefault(s => s.Id == siteId) is { } s && World!.Contains(s.Column, s.Row) ? World.GetCell(s.Column, s.Row) : null;

    /// <summary>近景：滑鼠下的小人或建築屬於哪個聚落（回傳聚落格；都不是回傳 null）。</summary>
    private DemoHexCell? SettlementUnder(Vector2 position, Vector2 start, float radius)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        // 建築：廣場、地塊（素材往上長出地塊，所以地塊上方兩方格也算）。
        var texel = (position - TerrainSurface.TexelToScreen(Vector2.Zero, start, radius)) / p;
        var tile = new Vector2I(Mathf.FloorToInt(texel.X / TerrainSurface.TempTile), Mathf.FloorToInt(texel.Y / TerrainSurface.TempTile));
        foreach (var (siteId, layout) in _layouts)
        {
            if (layout.Plaza.HasPoint(tile))
                return SiteCell(siteId);
            foreach (var lot in layout.Lots)
                if (new Rect2I(lot.X, lot.Y - 2, lot.W, lot.H + 2).HasPoint(tile))
                    return SiteCell(siteId);
        }
        return null;
    }

    /// <summary>框起選到的格，鏡頭不動。</summary>
    private void MarkCell(DemoHexCell cell)
    {
        Highlight = (cell.Column, cell.Row);
        QueueRedraw();
    }
}
