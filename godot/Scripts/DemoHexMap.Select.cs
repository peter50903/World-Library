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
    public enum ActorKind { Leader, WorkGroup, Migrants, Monster }

    /// <summary>一個可以點的角色：種類、所屬聚落、職業（工作團體）、人數、所在或工作的格、隊伍編號、地下城事件編號。</summary>
    public sealed record MapActor(ActorKind Kind, int SettlementId, TokenKind Work, long People, DemoCellRef Cell, int BatchId = 0, long DungeonEventId = 0);

    /// <summary>近景點到角色時通知觀察者介面。</summary>
    public event Action<MapActor>? ActorSelected;

    /// <summary>這一幀畫出來的角色（腳的位置），供點選。</summary>
    private readonly List<(Vector2 Feet, MapActor Actor)> _actorHits = new();

    /// <summary>滑鼠下的角色（腳下兩個圖像素到頭上約 22 個圖像素、左右 9 個圖像素；最靠前的優先）。</summary>
    private MapActor? ActorUnder(Vector2 position, float radius)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        return _actorHits.Where(a => Mathf.Abs(position.X - a.Feet.X) <= 9f * p && position.Y <= a.Feet.Y + 2f * p
                && position.Y >= a.Feet.Y - (a.Actor.Kind == ActorKind.Monster ? 34f : 22f) * p)
            .OrderByDescending(a => a.Feet.Y).Select(a => a.Actor).FirstOrDefault();
    }

    private void Select(Vector2 position, DemoHexCell hit)
    {
        var (start, radius) = ViewLayout();
        var at = new DemoCellRef(hit.Column, hit.Row);
        var owner = Territory.Cells.TryGetValue(at, out var o) ? o : null;
        var site = owner?.SettlementId ?? (Lineage.TryGetValue(at, out var line) ? (int?)line.SettlementId : null);
        // 試玩回饋 2026-11-08 之 1：點到什麼依目前的地圖模式（不再依遠近）。
        switch (_mode)
        {
            case Mode.Realm:
                // 勢力模式：點到勢力就開勢力最上層聚落的介紹（含勢力分頁）；怪物的勢力範圍開地下城。
                if (owner is not null && RealmSelected is not null && SiteCell(owner.RealmId) is { } capital)
                {
                    FocusCell(capital);
                    RealmSelected(owner.RealmId);
                    return;
                }
                if (MonsterNestAt(at) is { } nest && World!.Contains(nest.Dungeon.Cell.Column, nest.Dungeon.Cell.Row))
                    hit = World.GetCell(nest.Dungeon.Cell.Column, nest.Dungeon.Cell.Row);
                break;
            case Mode.Culture when site is { } c && CultureOf.TryGetValue(c, out var culture) && CultureSelected is not null:
                FocusCell(hit);
                CultureSelected(culture);
                return;
            case Mode.Faith when site is { } f && FaithOf.TryGetValue(f, out var faith) && FaithSelected is not null:
                FocusCell(hit);
                FaithSelected(faith);
                return;
            case Mode.Terrain:
                // 地形模式：點地塊；近景點到人物、團體、怪物就開他們的詳情，點到建築開那個聚落。
                if (LayerFor(radius) == ViewLayer.Local)
                {
                    if (ActorUnder(position, radius) is { } actor)
                    {
                        if (World!.Contains(actor.Cell.Column, actor.Cell.Row))
                            FocusCell(World.GetCell(actor.Cell.Column, actor.Cell.Row));
                        ActorSelected?.Invoke(actor);
                        return;
                    }
                    if (SettlementUnder(position, start, radius) is { } home)
                        hit = home;
                }
                break;
            default:
                // 糧食、家系、知識模式：點到的格屬於哪個聚落就開那個聚落。
                if (site is { } s && SiteCell(s) is { } settlement)
                    hit = settlement;
                break;
        }
        FocusCell(hit);
        CellSelected?.Invoke(hit);
    }

    /// <summary>這一格在哪個地下城怪物的勢力範圍內（最近的；沒有回傳 null）。</summary>
    private DemoMonsterNest? MonsterNestAt(DemoCellRef at)
        => MonsterNests.Where(n => n.Radius > 0 && DemoHexWorld.Distance(n.Dungeon.Cell, at) <= n.Radius)
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
}
