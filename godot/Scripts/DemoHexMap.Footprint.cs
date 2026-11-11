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
    private const float TempFootprintMinRadius = 14f;
    private const int TempPeoplePerHouse = 15;
    private const int TempPeoplePerTent = 5;
    private const int TempHousesPerCell = 3;

    /// <summary>【TEMP】各等級最多佔幾格（村莊、城鎮在一圈內，城市、首都在兩圈內）。</summary>
    private const int TempVillageCells = 3, TempTownCells = 9, TempCityCells = 20, TempCapitalCells = 30; // 第 08-18k 章：城鎮以上明顯更大

    public const string SpriteTent = "res://assets/sprites/tent.png";
    public const string SpriteHut = "res://assets/sprites/hut.png";
    public const string SpriteHouse = "res://assets/sprites/house.png";
    public const string SpriteStoneHouse = "res://assets/sprites/stone_house.png";
    public const string SpriteMarket = "res://assets/sprites/market.png";
    public const string SpriteTemple = "res://assets/sprites/temple.png";
    public const string SpriteKeep = "res://assets/sprites/keep.png";
    public const string SpritePalisade = "res://assets/sprites/palisade.png";
    public const string SpriteStoneWall = "res://assets/sprites/stone_wall.png";

    public enum Building { Tent, Hut, House, StoneHouse, Market, Temple, Keep, Well,
        // 第 08-18k 章：依等級加上的公共建築（第五批素材裡已有、原本沒用到的）
        HallVillage, HallTown, Granary, Smithy, Workshop, Kiln, Windmill, Watchtower, School, Scriptorium, Shrine }

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

    /// <summary>一口石井（程式畫，與像素地表同一像素密度：寬 9、高 8 個圖像素）。</summary>
    private void DrawWell(Vector2 bottom, float radius)
    {
        var p = radius / TerrainSurface.TempArtTexelsPerRadius; // 第四批零件的像素密度
        var left = bottom + new Vector2(-4.5f * p, -8f * p);
        void Px(float x, float y, float w, float h, Color c) => DrawRect(new Rect2(left + new Vector2(x, y) * p, new Vector2(w, h) * p), c);
        Px(0, 0, 9, 1, new Color(0.45f, 0.28f, 0.14f));   // 木頂
        Px(1, 1, 1, 3, new Color(0.40f, 0.25f, 0.12f));   // 柱
        Px(7, 1, 1, 3, new Color(0.40f, 0.25f, 0.12f));
        Px(0, 4, 9, 4, new Color(0.38f, 0.37f, 0.35f));   // 石圈
        Px(1, 4, 7, 3, new Color(0.62f, 0.60f, 0.56f));
        Px(2, 4, 5, 2, new Color(0.16f, 0.30f, 0.48f));   // 井水
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

    /// <summary>地形畫完之後畫全部聚落的建築與牆（由上而下排序，前面的屋子蓋住後面的）。</summary>
    private void DrawFootprints(Vector2 start, float radius)
    {
        if (World is null || Sites is null)
            return;
        var spots = new List<(Vector2 At, Building Kind)>();
        foreach (var settlement in Sites.Settlements)
        {
            if (settlement.IsRuin || FadedRuins.Contains(settlement.Id))
                continue;
            var ownCell = World.GetCell(settlement.Column, settlement.Row);
            if (!OnScreen(start + HexCoordinates.Center(ownCell.AxialQ, ownCell.AxialR, radius), radius * 3f))
                continue;
            var plan = FootprintOf(settlement);
            Vector2 CenterOf(DemoCellRef c)
            {
                var cell = World.GetCell(c.Column, c.Row);
                return start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            }
            if (plan.Palisade)
                DrawWall(new[] { plan.Cells[0] }, CenterOf, radius, new Color(0.45f, 0.30f, 0.16f), 0.07f, SpritePalisade, "bld_palisade");
            if (plan.StoneWall)
                DrawWall(plan.Cells, CenterOf, radius, new Color(0.62f, 0.60f, 0.56f), 0.11f, SpriteStoneWall, "bld_stone_wall");
            foreach (var b in plan.Buildings)
                spots.Add((CenterOf(b.Cell) + b.Offset * radius, b.Kind));
            // 試玩回饋 2026-11-05 之 12：不臨水的聚落（靠掘井才住得下）在村口畫一口井。
            if (!World.IsNearWater(new DemoCellRef(settlement.Column, settlement.Row)))
                DrawWell(CenterOf(plan.Cells[0]) + new Vector2(-radius * 0.5f, radius * 0.45f), radius);
            if (ShowDisasterMarks && RecentDisasters.Contains(settlement.Id)) // 第 12-1 章 C3：一年內遭天災
            {
                var mark = CenterOf(plan.Cells[0]) + new Vector2(radius * 0.55f, -radius * 0.6f);
                if (!DrawPixelPart("px_icon_disaster", mark + new Vector2(0f, radius * 0.35f), radius))
                    DrawMarker(ArtAssets.IconDisaster, mark, radius * 0.7f);
            }
        }
        foreach (var (at, kind) in spots.OrderBy(s => s.At.Y).ThenBy(s => s.At.X))
            DrawBuilding(kind, at, radius);
    }

    /// <summary>沿佔格的外緣畫牆（兩格都在牆內的邊不畫）。</summary>
    private void DrawWall(IReadOnlyList<DemoCellRef> cells, Func<DemoCellRef, Vector2> centerOf, float radius, Color color, float width, string sprite, string pixelPart)
    {
        // 像素零件城牆：原尺寸、沿邊橫向重複（不拉伸）。
        var pixel = PixelParts.Texture(pixelPart, repeat: true);
        var texture = (Texture2D?)pixel ?? ArtAssets.Texture(sprite);
        var inside = cells.ToHashSet();
        foreach (var c in cells)
        {
            var cell = World!.GetCell(c.Column, c.Row);
            var center = centerOf(c);
            var hex = ScreenShape(cell, center, radius * 0.97f); // 第 08-18b 章：沿格邊（方格）
            foreach (var (dq, dr, _) in DemoRouteNetwork.Orthogonal)
            {
                var n = World.AtAxial(cell.AxialQ + dq, cell.AxialR + dr);
                if (n is not null && inside.Contains(new DemoCellRef(n.Column, n.Row)))
                    continue;
                var path = EdgeOf(hex, HexCoordinates.EdgeToward(dq, dr));
                for (var k = 0; k + 1 < path.Length; k++)
                {
                    var (edgeStart, edgeEnd) = (path[k], path[k + 1]);
                    if (texture is not null)
                    {
                        // 素材沿邊逐段貼（牆的高度依素材比例、約為格半徑的 width×3），畫完把變換還原。
                        var length = edgeStart.DistanceTo(edgeEnd);
                        var height = pixel is not null ? PixelParts.ScreenSize(pixel, radius).Y : radius * width * 3f;
                        DrawSetTransform(edgeStart, (edgeEnd - edgeStart).Angle(), Vector2.One);
                        if (pixel is not null)
                        {
                            // 重複貼：以圖的原始像素尺寸為一段（DrawTextureRectRegion 的來源寬＝邊長換算成圖像素）。
                            var scale = radius / TerrainSurface.TempArtTexelsPerRadius;
                            DrawTextureRectRegion(pixel, new Rect2(0f, -height * 0.7f, length + 1f, height), new Rect2(0f, 0f, (length + 1f) / scale, pixel.GetHeight()));
                        }
                        else
                            DrawTextureRect(texture, new Rect2(0f, -height * 0.7f, length + 1f, height), false);
                        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
                        continue;
                    }
                    DrawLine(edgeStart, edgeEnd, color.Darkened(0.45f), radius * width + 2f);
                    DrawLine(edgeStart, edgeEnd, color, radius * width);
                }
            }
        }
    }

    private void DrawBuilding(Building kind, Vector2 at, float radius)
    {
        // 試玩回饋 2026-11-04 之 6（建築小得像地形噪點）：建築放大約 1.7 倍、數量減少（【TEMP】倍率）。
        var (path, size) = kind switch
        {
            Building.Tent => (SpriteTent, 0.72f),
            Building.Hut => (SpriteHut, 0.8f),
            Building.House => (SpriteHouse, 0.85f),
            Building.StoneHouse => (SpriteStoneHouse, 0.88f),
            Building.Market => (SpriteMarket, 1.0f),
            Building.Temple => (SpriteTemple, 1.1f),
            _ => (SpriteKeep, 1.3f)
        };
        // 第 08-16 章第 12 節：像素零件建築——與像素地表同一像素密度、最近鄰取樣、底部對齊（透視與地圖一致）。
        var px = kind switch
        {
            Building.Tent => "bld_tent", Building.Hut => "bld_hut", Building.House => "bld_house", Building.StoneHouse => "bld_stone_house",
            Building.Market => "bld_market", Building.Temple => "bld_temple", _ => "bld_keep"
        };
        if (PixelParts.Texture(px) is { } pxTexture)
        {
            // 試玩回饋 2026-11-05 之 4（像浮在空中）：建築底下畫一圈落地陰影，站在聚落的泥土空地上。
            var bottom = at + new Vector2(0f, radius * 0.3f);
            var width = PixelParts.ScreenSize(pxTexture, radius).X;
            DrawSetTransform(bottom + new Vector2(width * 0.08f, -width * 0.02f), 0f, new Vector2(1f, 0.32f));
            DrawCircle(Vector2.Zero, width * 0.55f, new Color(0.08f, 0.06f, 0.04f, 0.38f));
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            DrawPixelPart(px, bottom, radius);
            return;
        }
        if (DrawMarker(path, at, radius * size))
            return;
        // 素材到之前的色塊（【TEMP】顏色）。
        var s = radius * size;
        var outline = new Color(0.12f, 0.10f, 0.08f);
        switch (kind)
        {
            case Building.Tent:
                var tent = new[] { at + new Vector2(0, -s * 0.45f), at + new Vector2(s * 0.45f, s * 0.35f), at + new Vector2(-s * 0.45f, s * 0.35f) };
                DrawColoredPolygon(tent, new Color(0.86f, 0.78f, 0.60f));
                DrawPolyline(new[] { tent[0], tent[1], tent[2], tent[0] }, outline, 1.5f);
                break;
            case Building.Market:
                DrawRect(new Rect2(at - new Vector2(s * 0.45f, s * 0.1f), new Vector2(s * 0.9f, s * 0.4f)), new Color(0.80f, 0.68f, 0.48f));
                for (var i = 0; i < 4; i++)
                    DrawRect(new Rect2(at + new Vector2(-s * 0.5f + i * s * 0.25f, -s * 0.35f), new Vector2(s * 0.25f, s * 0.25f)),
                        i % 2 == 0 ? new Color(0.78f, 0.22f, 0.18f) : new Color(0.95f, 0.92f, 0.85f));
                break;
            case Building.Keep:
                DrawRect(new Rect2(at - new Vector2(s * 0.25f, s * 0.5f), new Vector2(s * 0.5f, s * 0.9f)), new Color(0.55f, 0.53f, 0.50f));
                DrawRect(new Rect2(at - new Vector2(s * 0.25f, s * 0.5f), new Vector2(s * 0.5f, s * 0.9f)), outline, false, 1.5f);
                for (var i = 0; i < 3; i++)
                    DrawRect(new Rect2(at + new Vector2(-s * 0.25f + i * s * 0.2f, -s * 0.62f), new Vector2(s * 0.1f, s * 0.12f)), new Color(0.55f, 0.53f, 0.50f));
                break;
            default:
                var (wall, roof) = kind switch
                {
                    Building.Hut => (new Color(0.55f, 0.38f, 0.22f), new Color(0.72f, 0.60f, 0.30f)),
                    Building.House => (new Color(0.82f, 0.70f, 0.50f), new Color(0.62f, 0.25f, 0.18f)),
                    Building.Temple => (new Color(0.93f, 0.91f, 0.84f), new Color(0.80f, 0.64f, 0.25f)),
                    _ => (new Color(0.66f, 0.64f, 0.60f), new Color(0.32f, 0.36f, 0.44f))
                };
                var body = new Rect2(at - new Vector2(s * 0.35f, s * 0.05f), new Vector2(s * 0.7f, s * 0.42f));
                DrawRect(body, wall);
                DrawRect(body, outline, false, 1.2f);
                var top = new[] { at + new Vector2(0, -s * 0.45f), at + new Vector2(s * 0.45f, -s * 0.05f), at + new Vector2(-s * 0.45f, -s * 0.05f) };
                DrawColoredPolygon(top, roof);
                DrawPolyline(new[] { top[0], top[1], top[2], top[0] }, outline, 1.2f);
                break;
        }
    }
}
