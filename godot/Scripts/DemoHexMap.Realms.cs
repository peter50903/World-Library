using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-19 章：勢力範圍的畫面。每個聚落有自己的勢力範圍，落在更高層級範圍內的聚落隸屬於它（Core 的 <see cref="DemoTerritory"/>）。
/// <list type="bullet">
/// <item>地形模式（各層）：沿格邊畫最上層勢力的邊界線（勢力的代表色）；區域層、戰略層再罩一層淡淡的代表色（WorldBox 的國界畫法）。</item>
/// <item>勢力模式：整片塗上最上層勢力的代表色；同一勢力裡，直屬不同聚落的格之間畫細線（看得出村莊歸哪個城鎮）。</item>
/// </list>
/// 只改畫面。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>第 08-19 章：勢力範圍與隸屬（由觀察者介面每週注入）。</summary>
    public DemoTerritoryMap Territory { get; set; } = DemoTerritoryMap.Empty;

    /// <summary>某勢力的代表色（依最上層聚落編號固定挑色；同一世界不變）。</summary>
    public Color RealmColor(int realmId) => TempLineagePalette[(realmId * 5 + 3) % TempLineagePalette.Length];

    /// <summary>勢力的邊界（與勢力模式的整片著色）。<paramref name="fill"/>＝塗色的不透明度（0＝只畫邊界）。</summary>
    private void DrawRealms(Vector2 start, float radius, float fill, bool inner)
    {
        if (World is null || Territory.Cells.Count == 0)
            return;
        var width = Math.Clamp(radius * 0.12f, 1.5f, 4f);
        foreach (var (at, owner) in Territory.Cells)
        {
            var cell = World.GetCell(at.Column, at.Row);
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            if (!OnScreen(center, radius))
                continue;
            var color = RealmColor(owner.RealmId);
            var shape = ScreenShape(cell, center, radius);
            if (fill > 0f)
                DrawShape(center, shape, new Color(color, fill));
            foreach (var (dq, dr, _) in DemoRouteNetwork.Orthogonal)
            {
                var next = new DemoCellRef(at.Column + dq, at.Row + dr);
                var other = Territory.Cells.TryGetValue(next, out var o) ? o : null;
                var edge = EdgeOf(shape, HexCoordinates.EdgeToward(dq, dr));
                if (other is null || other.RealmId != owner.RealmId)
                    DrawPolyline(edge, color.Darkened(0.2f), width); // 勢力之間的邊界
                else if (inner && other.SettlementId != owner.SettlementId)
                    DrawPolyline(edge, new Color(color.Darkened(0.45f), 0.55f), Math.Max(1f, width * 0.4f)); // 同一勢力裡的直屬邊界
            }
        }
    }

    /// <summary>勢力模式的滑鼠提示：直屬哪個聚落、最上層是誰。</summary>
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
}
