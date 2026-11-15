using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-13 章第 4 節（試玩回饋三之 4）：聚落外觀依等級長大——只改顯示，不改規則。
/// 營地＝本格帳篷；村莊＝本格＋相鄰領域格的木屋；城鎮＝屋舍＋本格木柵欄＋市集；城市＝石屋＋外圈石城牆＋市集（有信仰加神殿）；首都再加城堡主樓。
/// 建築位置依聚落與格決定（同一世界每次畫的位置相同）；有 SPRITE_* 素材就貼圖，沒有就畫簡單色塊。
/// 格太小（遠看）時改回單一聚落圖示（<see cref="UseFootprints"/>）。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】每格半徑小於此（像素）時不畫建築、改畫單一圖示；每幾人一棟屋（營地每幾人一頂帳篷）；每格最多幾棟。</summary>
    private const float TempFootprintMinRadius = 14f * TerrainSurface.TempZoomScale; // 第 08-20g 章：世界放大後照比例換算
    private const int TempPeoplePerHouse = 15;
    private const int TempPeoplePerTent = 5;
    private const int TempHousesPerCell = 3;

    /// <summary>【TEMP】各等級最多佔幾格（村莊、城鎮在一圈內，城市、首都在兩圈內）。</summary>
    private const int TempVillageCells = 3, TempTownCells = 9, TempCityCells = 20, TempCapitalCells = 30; // 第 08-18k 章：城鎮以上明顯更大

    public enum Building { Tent, Hut, House, StoneHouse, Market, Temple, Keep, Well,
        // 第 08-18k 章：依等級加上的公共建築（第五批素材裡已有、原本沒用到的）
        HallVillage, HallTown, Granary, Smithy, Workshop, Kiln, Windmill, Watchtower, School, Scriptorium, Shrine,
        // 第 08-20e 章：第九批的首都磚屋、獵人小屋、漁夫高腳屋、碼頭、水磨坊
        BrickHouse, HuntingLodge, FishingHut, Dock, Watermill,
        // 第 09-1b 章：第十一批新增的農舍、旅店、倉庫、馬廄、鐘樓、兵營、靶場、噴泉、雕像
        Farmhouse, Inn, Warehouse, Stable, BellTower, Barracks, Archery, Fountain, Statue }

    /// <summary>一棟建築：在哪一格、什麼、相對格中心的位置（格半徑的倍數）。</summary>
    public readonly record struct BuildingSpot(DemoCellRef Cell, Building Kind, Vector2 Offset);

    /// <summary>聚落的外觀：佔哪些格、有哪些建築、牆（木柵＝只圍本格；石牆＝圍住全部佔格）。</summary>
    public sealed record Footprint(IReadOnlyList<DemoCellRef> Cells, IReadOnlyList<BuildingSpot> Buildings, bool Palisade, bool StoneWall);

    private bool UseFootprints(float radius) => radius >= TempFootprintMinRadius;

    /// <summary>依目前注入的人口、等級、領域（家系地圖）與信仰算出聚落外觀（只讀，供繪圖與冒煙測試）。</summary>
    public Footprint FootprintOf(DemoSettlement settlement)
    {
        var own = new DemoCellRef(settlement.Column, settlement.Row);
        var (people, tier) = SettlementScale.TryGetValue(settlement.Id, out var scale) ? scale : (0L, DemoSettlementTier.Camp);
        var cells = FootprintCells(settlement, tier);
        return FootprintFrom(settlement, own, people, tier, cells);
    }

    /// <summary>聚落佔哪些格（本格在第一個）；第 08-17 章的聚落版面也只蓋在這些格裡。</summary>
    public List<DemoCellRef> FootprintCells(DemoSettlement settlement, DemoSettlementTier tier)
    {
        var own = new DemoCellRef(settlement.Column, settlement.Row);
        // 試玩回饋 2026-11-05（城市看起來跟村莊差不多大）：佔地依等級擴大，不再只限家系領域的鄰格——
        // 村莊最多 3 格、城鎮 5 格（一圈內）、城市 10 格、首都 16 格（兩圈內）；只蓋在陸地、不壓到別的聚落與別人的領域，
        // 近的先、田地最後（田照樣畫田）。
        var cells = new List<DemoCellRef> { own };
        var (maxCells, ring) = tier switch
        {
            DemoSettlementTier.Village => (TempVillageCells, 1),
            DemoSettlementTier.Town => (TempTownCells, 1),
            DemoSettlementTier.City => (TempCityCells, 2),
            DemoSettlementTier.Capital => (TempCapitalCells, 2),
            _ => (1, 0)
        };
        if (ring > 0 && World is not null)
        {
            var origin = World.GetCell(settlement.Column, settlement.Row);
            var turn = settlement.Id % 6;
            var around = new List<(DemoCellRef Cell, int Distance, int Order, bool Farm)>();
            for (var dq = -ring; dq <= ring; dq++)
                for (var dr = -ring; dr <= ring; dr++)
                {
                    var d = Math.Max(Math.Abs(dq), Math.Abs(dr)); // 第 08-18a 章：切比雪夫距離
                    if (d == 0 || d > ring || World.AtAxial(origin.AxialQ + dq, origin.AxialR + dr) is not { } n || n.Terrain == DemoTerrain.Water)
                        continue;
                    var at = new DemoCellRef(n.Column, n.Row);
                    if (Sites?.SettlementAt(n.Column, n.Row) is { } other && other.Id != settlement.Id)
                        continue;
                    if (Lineage.TryGetValue(at, out var owner) && owner.SettlementId != settlement.Id)
                        continue;
                    around.Add((at, d, ((dq + 3) * 7 + dr + 3 + turn * 11) % 49, Land.StateOf(at) == DemoLandDevelopment.Farmland));
                }
            cells.AddRange(around.OrderBy(c => c.Distance).ThenBy(c => c.Farm ? 1 : 0).ThenBy(c => c.Order)
                .Take(maxCells - 1).Select(c => c.Cell));
        }
        return cells;
    }

    private Footprint FootprintFrom(DemoSettlement settlement, DemoCellRef own, long people, DemoSettlementTier tier, List<DemoCellRef> cells)
    {

        var buildings = new List<BuildingSpot>();
        var centerTaken = false;
        void Center(Building kind, Vector2 offset)
        {
            buildings.Add(new BuildingSpot(own, kind, offset));
            centerTaken = true;
        }
        if (tier >= DemoSettlementTier.Town)
            Center(Building.Market, new Vector2(0f, 0.05f));
        if (tier >= DemoSettlementTier.City && FaithOf.ContainsKey(settlement.Id))
            buildings.Add(new BuildingSpot(cells.Count > 1 ? cells[1] : own, Building.Temple, Vector2.Zero));
        if (tier == DemoSettlementTier.Capital)
            Center(Building.Keep, new Vector2(0f, -0.15f));

        var house = tier switch
        {
            DemoSettlementTier.Camp => Building.Tent,
            DemoSettlementTier.Village => Building.Hut,
            DemoSettlementTier.Town => Building.House,
            _ => Building.StoneHouse
        };
        // 每格屋數也依等級增加（城市更密）。
        var perCell = tier switch
        {
            DemoSettlementTier.Camp or DemoSettlementTier.Village => TempHousesPerCell - 1,
            DemoSettlementTier.Town => TempHousesPerCell,
            _ => TempHousesPerCell + 1
        };
        var count = tier == DemoSettlementTier.Camp
            ? (int)Math.Clamp(people / TempPeoplePerTent, 1, TempHousesPerCell)
            : (int)Math.Clamp(people / TempPeoplePerHouse, 1, perCell * cells.Count);
        if (tier == DemoSettlementTier.Camp)
            perCell = TempHousesPerCell;
        for (var i = 0; i < count; i++)
        {
            var cell = cells[i / perCell];
            var slot = i % perCell;
            // 位置繞格心平均分開，起始角依聚落與格決定；本格中央有市集等建築時屋子往外推。
            var angle = Mathf.Tau * ((settlement.Id * 7 + cell.Column * 3 + cell.Row * 5) % 12) / 12f + slot * Mathf.Tau / perCell;
            var distance = cell == own && centerTaken ? 0.62f : cell == own && count == 1 ? 0f : 0.38f;
            buildings.Add(new BuildingSpot(cell, house, Vector2.FromAngle(angle) * distance));
        }
        var walled = tier >= DemoSettlementTier.City;
        return new Footprint(walled ? cells : cells.Take(tier >= DemoSettlementTier.Village ? Math.Max(1, (count + perCell - 1) / perCell) : 1).ToList(),
            buildings, tier == DemoSettlementTier.Town, walled);
    }

    /// <summary>畫一個像素零件：底部置中在 <paramref name="bottom"/>，大小依像素地表的像素密度；素材缺少回傳 false。</summary>
    private bool DrawPixelPart(string name, Vector2 bottom, float radius)
    {
        if (PixelParts.Texture(name) is not { } texture)
            return false;
        var size = PixelParts.ScreenSize(texture, radius);
        DrawTextureRect(texture, new Rect2(bottom - new Vector2(size.X / 2f, size.Y), size), false);
        return true;
    }
}
