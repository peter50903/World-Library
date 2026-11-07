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

    private void Select(Vector2 position, DemoHexCell hit)
    {
        var (start, radius) = ViewLayout();
        var at = new DemoCellRef(hit.Column, hit.Row);
        if (LayerFor(radius) != ViewLayer.Local)
        {
            var owner = Territory.Cells.TryGetValue(at, out var o) ? o : null;
            if (_mode is Mode.Terrain or Mode.Realm && owner is not null && RealmSelected is not null && SiteCell(owner.RealmId) is { } capital)
            {
                FocusCell(capital);
                RealmSelected(owner.RealmId);
                return;
            }
            var site = owner?.SettlementId ?? (Lineage.TryGetValue(at, out var line) ? (int?)line.SettlementId : null);
            if (_mode == Mode.Culture && site is { } c && CultureOf.TryGetValue(c, out var culture) && CultureSelected is not null)
            {
                FocusCell(hit);
                CultureSelected(culture);
                return;
            }
            if (_mode == Mode.Faith && site is { } f && FaithOf.TryGetValue(f, out var faith) && FaithSelected is not null)
            {
                FocusCell(hit);
                FaithSelected(faith);
                return;
            }
        }
        else if (SettlementUnder(position, start, radius) is { } home)
            hit = home;
        FocusCell(hit);
        CellSelected?.Invoke(hit);
    }

    private DemoHexCell? SiteCell(int siteId)
        => Sites?.Settlements.FirstOrDefault(s => s.Id == siteId) is { } s && World!.Contains(s.Column, s.Row) ? World.GetCell(s.Column, s.Row) : null;

    /// <summary>近景：滑鼠下的小人或建築屬於哪個聚落（回傳聚落格；都不是回傳 null）。</summary>
    private DemoHexCell? SettlementUnder(Vector2 position, Vector2 start, float radius)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        foreach (var (feet, siteId) in _walkerHits)
            if (Mathf.Abs(position.X - feet.X) <= 8f * p && position.Y <= feet.Y + 2f * p && position.Y >= feet.Y - 20f * p)
                return SiteCell(siteId);
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
