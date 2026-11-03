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
    private const int TempPeoplePerHouse = 10;
    private const int TempPeoplePerTent = 5;
    private const int TempHousesPerCell = 3;

    public const string SpriteTent = "res://assets/sprites/tent.png";
    public const string SpriteHut = "res://assets/sprites/hut.png";
    public const string SpriteHouse = "res://assets/sprites/house.png";
    public const string SpriteStoneHouse = "res://assets/sprites/stone_house.png";
    public const string SpriteMarket = "res://assets/sprites/market.png";
    public const string SpriteTemple = "res://assets/sprites/temple.png";
    public const string SpriteKeep = "res://assets/sprites/keep.png";
    public const string SpritePalisade = "res://assets/sprites/palisade.png";
    public const string SpriteStoneWall = "res://assets/sprites/stone_wall.png";

    public enum Building { Tent, Hut, House, StoneHouse, Market, Temple, Keep }

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
        var cells = new List<DemoCellRef> { own };
        if (tier >= DemoSettlementTier.Village && World is not null)
        {
            var origin = World.GetCell(settlement.Column, settlement.Row);
            var turn = settlement.Id % 6;
            for (var k = 0; k < 6; k++)
            {
                var (dq, dr) = HexDirections[(k + turn) % 6];
                if (World.AtAxial(origin.AxialQ + dq, origin.AxialR + dr) is { } n
                    && Lineage.TryGetValue(new DemoCellRef(n.Column, n.Row), out var owner) && owner.SettlementId == settlement.Id)
                    cells.Add(new DemoCellRef(n.Column, n.Row));
            }
            // 村莊的屋子先蓋在不是田地的格（田地照樣畫農田圖塊）。
            if (tier == DemoSettlementTier.Village)
                cells = cells.Take(1).Concat(cells.Skip(1).OrderBy(c => Land.StateOf(c) == DemoLandDevelopment.Farmland ? 1 : 0)).ToList();
        }

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
        var count = tier == DemoSettlementTier.Camp
            ? (int)Math.Clamp(people / TempPeoplePerTent, 1, TempHousesPerCell)
            : (int)Math.Clamp(people / TempPeoplePerHouse, 1, TempHousesPerCell * cells.Count);
        for (var i = 0; i < count; i++)
        {
            var cell = cells[i / TempHousesPerCell];
            var slot = i % TempHousesPerCell;
            // 三個位置繞格心各差 120°，起始角依聚落與格決定；本格中央有市集等建築時屋子往外推。
            var angle = Mathf.Tau * ((settlement.Id * 7 + cell.Column * 3 + cell.Row * 5) % 12) / 12f + slot * Mathf.Tau / 3f;
            var distance = cell == own && centerTaken ? 0.55f : cell == own && count == 1 ? 0f : 0.42f;
            buildings.Add(new BuildingSpot(cell, house, Vector2.FromAngle(angle) * distance));
        }
        var walled = tier >= DemoSettlementTier.City;
        return new Footprint(walled ? cells : cells.Take(tier >= DemoSettlementTier.Village ? Math.Max(1, (count + TempHousesPerCell - 1) / TempHousesPerCell) : 1).ToList(),
            buildings, tier == DemoSettlementTier.Town, walled);
    }

    private static readonly (int Dq, int Dr)[] HexDirections = { (1, 0), (1, -1), (0, -1), (-1, 0), (-1, 1), (0, 1) };

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
                DrawWall(new[] { plan.Cells[0] }, CenterOf, radius, new Color(0.45f, 0.30f, 0.16f), 0.07f, SpritePalisade);
            if (plan.StoneWall)
                DrawWall(plan.Cells, CenterOf, radius, new Color(0.62f, 0.60f, 0.56f), 0.11f, SpriteStoneWall);
            foreach (var b in plan.Buildings)
                spots.Add((CenterOf(b.Cell) + b.Offset * radius, b.Kind));
            if (RecentDisasters.Contains(settlement.Id)) // 第 12-1 章 C3：一年內遭天災
                DrawMarker(ArtAssets.IconDisaster, CenterOf(plan.Cells[0]) + new Vector2(radius * 0.55f, -radius * 0.6f), radius * 0.7f);
        }
        foreach (var (at, kind) in spots.OrderBy(s => s.At.Y).ThenBy(s => s.At.X))
            DrawBuilding(kind, at, radius);
    }

    /// <summary>沿佔格的外緣畫牆（兩格都在牆內的邊不畫）。</summary>
    private void DrawWall(IReadOnlyList<DemoCellRef> cells, Func<DemoCellRef, Vector2> centerOf, float radius, Color color, float width, string sprite)
    {
        var texture = ArtAssets.Texture(sprite);
        var inside = cells.ToHashSet();
        foreach (var c in cells)
        {
            var cell = World!.GetCell(c.Column, c.Row);
            var center = centerOf(c);
            var hex = ScreenShape(cell, center, radius * 0.97f); // 第 08-15 章第 3 節：沿不規則邊界
            foreach (var (dq, dr) in HexDirections)
            {
                var n = World.AtAxial(cell.AxialQ + dq, cell.AxialR + dr);
                if (n is not null && inside.Contains(new DemoCellRef(n.Column, n.Row)))
                    continue;
                var offset = HexCoordinates.Center(cell.AxialQ + dq, cell.AxialR + dr, radius) - HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
                var edge = (int)Mathf.PosMod(Mathf.Round(Mathf.RadToDeg(offset.Angle()) / 60f), 6f);
                var path = EdgeOf(hex, edge);
                for (var k = 0; k + 1 < path.Length; k++)
                {
                    var (edgeStart, edgeEnd) = (path[k], path[k + 1]);
                    if (texture is not null)
                    {
                        // 素材沿邊逐段貼（牆的高度依素材比例、約為格半徑的 width×3），畫完把變換還原。
                        var length = edgeStart.DistanceTo(edgeEnd);
                        var height = radius * width * 3f;
                        DrawSetTransform(edgeStart, (edgeEnd - edgeStart).Angle(), Vector2.One);
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
        var (path, size) = kind switch
        {
            Building.Tent => (SpriteTent, 0.42f),
            Building.Hut => (SpriteHut, 0.45f),
            Building.House => (SpriteHouse, 0.48f),
            Building.StoneHouse => (SpriteStoneHouse, 0.5f),
            Building.Market => (SpriteMarket, 0.62f),
            Building.Temple => (SpriteTemple, 0.7f),
            _ => (SpriteKeep, 0.85f)
        };
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
