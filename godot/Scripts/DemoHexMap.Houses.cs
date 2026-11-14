using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18b 章第 3 節：WorldBox 式的住屋、建地、囤糧圍欄與名牌（程式畫，第五批素材到之前）。只改畫面。
/// 住屋是俯視略帶正面：看得到牆面與屋頂；屋頂是聚落主要文化的代表色，遠看就分得出是誰的地方。
/// 像素密度與地表相同（一個圖像素＝格半徑÷<see cref="TerrainSurface.TempTexelsPerRadius"/>）。
/// </summary>
public partial class DemoHexMap
{
    private static readonly Color Ink = new(0.12f, 0.09f, 0.07f);

    /// <summary>一棟住屋：帳篷、木屋、房子、石屋（底部置中在 <paramref name="bottom"/>，地塊寬 <paramref name="lotWidth"/> 像素）。</summary>
    private void DrawHouse(Building kind, Vector2 bottom, float lotWidth, Color culture, float radius, int seed = 0, int facing = 0)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        if (HouseArt(kind, seed, culture, facing) is { } art)
        {
            // 第 08-18d 章：第五批住屋素材（屋頂染文化色）。
            GroundShadow(bottom - new Vector2(0f, p * 2f), lotWidth * 0.85f);
            DrawArt(art, bottom, radius);
            return;
        }
        // 落地陰影（光源左上：陰影往右下）。
        DrawSetTransform(bottom + new Vector2(lotWidth * 0.08f, -p * 2f), 0f, new Vector2(1f, 0.3f));
        DrawCircle(Vector2.Zero, lotWidth * 0.5f, new Color(0.08f, 0.06f, 0.04f, 0.35f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        if (kind == Building.Tent)
        {
            var canvas = new Color(0.86f, 0.78f, 0.60f).Lerp(culture, 0.35f);
            var half = lotWidth * 0.42f;
            var tip = bottom - new Vector2(0f, 20f * p);
            var tent = new[] { tip, bottom + new Vector2(half, -p), bottom + new Vector2(-half, -p) };
            DrawColoredPolygon(tent, canvas);
            DrawColoredPolygon(new[] { tip, bottom + new Vector2(half, -p), bottom + new Vector2(half * 0.1f, -p) }, canvas.Darkened(0.18f));
            DrawColoredPolygon(new[] { bottom - new Vector2(0f, 9f * p), bottom + new Vector2(3f * p, -p), bottom + new Vector2(-3f * p, -p) }, Ink);
            DrawPolyline(new[] { tent[0], tent[1], tent[2], tent[0] }, Ink, Math.Max(1f, p));
            return;
        }
        var (wallColor, wallHeight, roofHeight, roof) = kind switch
        {
            Building.Hut => (new Color(0.55f, 0.38f, 0.22f), 14f, 13f, new Color(0.78f, 0.66f, 0.36f).Lerp(culture, 0.45f)),
            Building.House => (new Color(0.86f, 0.78f, 0.62f), 16f, 15f, culture),
            _ => (new Color(0.64f, 0.62f, 0.58f), 18f, 15f, culture.Darkened(0.08f))
        };
        var width = Mathf.Round(lotWidth * 0.8f / p) * p;
        var body = new Rect2(bottom.X - width / 2f, bottom.Y - wallHeight * p, width, wallHeight * p);
        DrawRect(body, wallColor);
        // 牆面細節：木板直紋、木骨架、石塊。
        if (kind == Building.Hut)
            for (var x = body.Position.X + 4f * p; x < body.End.X - p; x += 4f * p)
                DrawRect(new Rect2(x, body.Position.Y, p, body.Size.Y), wallColor.Darkened(0.2f));
        else if (kind == Building.House)
        {
            DrawRect(new Rect2(body.Position.X, body.Position.Y + 6f * p, body.Size.X, p), new Color(0.45f, 0.30f, 0.18f));
            DrawRect(new Rect2(body.Position.X, body.Position.Y, 2f * p, body.Size.Y), new Color(0.45f, 0.30f, 0.18f));
            DrawRect(new Rect2(body.End.X - 2f * p, body.Position.Y, 2f * p, body.Size.Y), new Color(0.45f, 0.30f, 0.18f));
        }
        else
            for (var row = 0; row * 5f < wallHeight; row++)
                for (var x = body.Position.X + (row % 2) * 4f * p; x < body.End.X; x += 8f * p)
                    DrawRect(new Rect2(x, body.Position.Y + row * 5f * p, p, 5f * p), wallColor.Darkened(0.18f));
        // 右側一條陰影（光源左上）。
        DrawRect(new Rect2(body.End.X - 3f * p, body.Position.Y, 3f * p, body.Size.Y), new Color(0f, 0f, 0f, 0.18f));
        // 門與窗（房子以上有窗）。
        DrawRect(new Rect2(bottom.X - 3f * p, bottom.Y - 10f * p, 6f * p, 10f * p), new Color(0.30f, 0.18f, 0.10f));
        if (kind != Building.Hut)
            foreach (var side in new[] { -1f, 1f })
            {
                var at = new Vector2(bottom.X + side * width * 0.3f - 2.5f * p, bottom.Y - 11f * p);
                DrawRect(new Rect2(at, new Vector2(5f, 5f) * p), new Color(0.98f, 0.86f, 0.48f));
                DrawRect(new Rect2(at, new Vector2(5f, 5f) * p), Ink, false, Math.Max(1f, p * 0.8f));
            }
        DrawRect(body, Ink, false, Math.Max(1f, p));
        // 屋頂：梯形，下緣出簷、上緣是屋脊；亮面在左、暗面在右下。
        var eaves = 3f * p;
        var topY = body.Position.Y - roofHeight * p;
        var ridge = width * 0.28f;
        var roofShape = new[]
        {
            new Vector2(body.Position.X - eaves, body.Position.Y + p), new Vector2(body.End.X + eaves, body.Position.Y + p),
            new Vector2(bottom.X + ridge, topY), new Vector2(bottom.X - ridge, topY)
        };
        DrawColoredPolygon(roofShape, roof);
        DrawRect(new Rect2(body.Position.X - eaves, body.Position.Y - p, width + 2f * eaves, 2f * p), roof.Darkened(0.3f)); // 簷口
        for (var k = 1; k <= 3; k++)
        {
            var y = topY + k * roofHeight * p / 4f;
            var t = k / 4f;
            var half = ridge + (width / 2f + eaves - ridge) * t;
            DrawRect(new Rect2(bottom.X - half, y, 2f * half, p), roof.Darkened(0.14f)); // 瓦片橫紋
        }
        DrawRect(new Rect2(bottom.X - ridge, topY, 2f * ridge, 2f * p), roof.Lightened(0.25f)); // 屋脊
        DrawPolyline(new[] { roofShape[0], roofShape[1], roofShape[2], roofShape[3], roofShape[0] }, Ink, Math.Max(1f, p));
        if (kind == Building.StoneHouse)
            DrawRect(new Rect2(bottom.X + width * 0.18f, topY - 4f * p, 4f * p, 7f * p), new Color(0.50f, 0.48f, 0.45f)); // 煙囪
    }

    /// <summary>建地：木樁圍出的輪廓、繩子與一堆木料（之後才變成房子）。</summary>
    private void DrawConstruction(Vector2 bottom, float lotWidth, float lotDepth, float radius, int seed = 0)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        if (PixelParts.VariantName("bld_site", 3, LayoutHash(seed, 0, 141)) is { } site && DrawArt(site, bottom, radius))
            return; // 第 08-18d 章：第五批建地素材
        var half = lotWidth * 0.4f;
        var back = lotDepth * 0.7f;
        var stake = new Color(0.60f, 0.42f, 0.22f);
        var corners = new[]
        {
            bottom + new Vector2(-half, -back), bottom + new Vector2(half, -back), bottom + new Vector2(half, -p), bottom + new Vector2(-half, -p)
        };
        DrawRect(new Rect2(corners[0], new Vector2(2f * half, back - p)), new Color(0.55f, 0.40f, 0.24f, 0.45f)); // 整好的地
        DrawPolyline(new[] { corners[0], corners[1], corners[2], corners[3], corners[0] }, new Color(0.92f, 0.86f, 0.70f), Math.Max(1f, p * 0.8f)); // 繩子
        foreach (var c in corners)
        {
            DrawRect(new Rect2(c.X - p, c.Y - 7f * p, 2f * p, 8f * p), stake);
            DrawRect(new Rect2(c.X - p, c.Y - 7f * p, 2f * p, 8f * p), Ink, false, Math.Max(1f, p * 0.6f));
        }
        for (var k = 0; k < 3; k++) // 木料
            DrawRect(new Rect2(bottom.X - 6f * p + k * p, bottom.Y - back * 0.5f - k * 2f * p, 12f * p, 2f * p), stake.Lightened(0.1f * k));
    }

    /// <summary>囤糧圍欄：一圈矮木欄圍著幾個木箱與糧袋（營地起就有，放在廣場角落）。</summary>
    private void DrawStockpile(Vector2 bottom, float radius, bool art = false)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        if (art && PixelParts.IsBatch5("bld_stockpile") && PixelParts.Texture("bld_stockpile") is { } stockpile)
        {
            GroundShadow(bottom - new Vector2(0f, p), PixelParts.ScreenSize(stockpile, radius).X * 0.9f, 0.25f);
            DrawArt(stockpile, bottom, radius);
            return;
        }
        var fence = new Color(0.52f, 0.36f, 0.20f);
        var box = new Rect2(bottom.X - 8f * p, bottom.Y - 9f * p, 16f * p, 8f * p);
        DrawRect(new Rect2(box.Position.X + 2f * p, box.Position.Y + 2f * p, 5f * p, 4f * p), new Color(0.70f, 0.52f, 0.28f)); // 木箱
        DrawRect(new Rect2(box.Position.X + 8f * p, box.Position.Y + 3f * p, 5f * p, 4f * p), new Color(0.86f, 0.80f, 0.60f)); // 糧袋
        DrawRect(new Rect2(box.Position.X + 3f * p, box.Position.Y, 4f * p, 3f * p), new Color(0.86f, 0.80f, 0.60f));
        DrawRect(box, fence, false, Math.Max(1f, p * 1.5f));
        for (var x = box.Position.X; x <= box.End.X + 0.1f; x += 4f * p)
            DrawRect(new Rect2(x - p * 0.5f, box.End.Y - 4f * p, p, 4f * p), fence.Darkened(0.2f)); // 欄柱
    }

    /// <summary>【TEMP】名牌：固定大小（不隨縮放）；區域層只標城鎮以上。</summary>
    private const int TempPlateFont = 13;
    private const float TempPlateEmblem = 16f;

    /// <summary>聚落名牌：紋章、名字、人口，畫在聚落上方，大小固定（WorldBox 式）。</summary>
    /// <summary>第 08-18i 章（GPT 試玩回饋 01：點名牌卻選到底下的草原）：這一幀畫出的名牌（範圍、聚落），點選時最先算。</summary>
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

    /// <summary>試玩回饋 2026-11-07 之 4：<paramref name="realmsOnly"/>＝遠看時只標各勢力最上層的聚落（人數是整個勢力的人口），
    /// 近看時每個聚落標自己的名字（不再寫「屬某某鎮」，隸屬看詳情）。</summary>
    private void DrawNameplates(Vector2 start, float radius, DemoSettlementTier minimum = DemoSettlementTier.Camp, bool realmsOnly = false)
    {
        if (World is null || Sites is null)
            return;
        var font = ThemeDB.FallbackFont;
        Dictionary<int, long>? realmPeople = null;
        if (realmsOnly)
        {
            realmPeople = new Dictionary<int, long>();
            foreach (var (id, (people, _)) in SettlementScale)
            {
                var top = Territory.RealmOf(id);
                realmPeople[top] = realmPeople.GetValueOrDefault(top) + people;
            }
        }
        foreach (var site in Sites.Settlements)
        {
            if (site.IsRuin || FadedRuins.Contains(site.Id) || !SettlementScale.TryGetValue(site.Id, out var scale) || scale.Item1 <= 0 || scale.Item2 < minimum)
                continue;
            if (realmsOnly && Territory.LiegeOf(site.Id) is not null)
                continue;
            var cell = World.GetCell(site.Column, site.Row);
            var center = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius);
            if (!OnScreen(center, radius * 2f))
                continue;
            var name = DemoSettlementHistory.TitledName(site, scale.Item2); // 試玩回饋 2026-11-06 之 9：名字帶層級
            var people = TokenCount(realmPeople?.GetValueOrDefault(site.Id, scale.Item1) ?? scale.Item1);
            var nameWidth = font.GetStringSize(name, HorizontalAlignment.Left, -1, TempPlateFont).X;
            var countWidth = font.GetStringSize(people, HorizontalAlignment.Left, -1, TempPlateFont - 2).X;
            // 第 08-18d 章：名牌底板（ui_nameplate 九宮格）、名字前的層級徽章、隸屬時人數後的隸屬徽章。
            var plate = PlateStyle();
            var badge = PixelParts.Texture(TierBadge(scale.Item2));
            CanvasTexture? vassal = null; // 試玩回饋 2026-11-07 之 4：名牌不再標隸屬
            var extra = (badge is not null ? TempPlateEmblem + 3f : 0f) + (vassal is not null ? TempPlateEmblem + 2f : 0f) + (plate is not null ? 10f : 0f);
            var width = TempPlateEmblem + 6f + nameWidth + 8f + countWidth + 10f + extra;
            var height = 20f;
            var anchor = center - new Vector2(0f, radius * (scale.Item2 >= DemoSettlementTier.City ? 1.25f : 1.05f) + height);
            var rect = new Rect2(anchor.X - width / 2f, anchor.Y, width, height);
            var culture = CultureOf.TryGetValue(site.Id, out var cultureId) ? CultureColor(cultureId) : new Color(0.5f, 0.5f, 0.5f);
            _plateHits.Add((rect.Grow(2f), site.Id)); // 第 08-18i 章：整條名牌都可以點（固定開聚落）
            if (_hoverPlate == site.Id)
                DrawRect(rect.Grow(2f), new Color(1f, 0.92f, 0.55f, 0.95f), false, 2f);
            if (plate is not null)
            {
                DrawStyleBox(plate, rect);
                DrawRect(new Rect2(rect.Position + new Vector2(8f, 3f), new Vector2(2f, height - 6f)), culture);
                rect = new Rect2(rect.Position + new Vector2(6f, 0f), rect.Size - new Vector2(10f, 0f));
            }
            else
            {
                DrawRect(rect, new Color(0.08f, 0.06f, 0.05f, 0.78f));
                DrawRect(new Rect2(rect.Position, new Vector2(3f, height)), culture);
                DrawRect(rect, culture.Darkened(0.3f), false, 1f);
            }
            var emblemAt = new Rect2(rect.Position + new Vector2(5f, 2f), new Vector2(TempPlateEmblem, TempPlateEmblem));
            if (CultureEmblems.TryGetValue(cultureId, out var emblem) && Heraldry.Emblem(emblem, culture) is { } shield)
                DrawTextureRect(shield, emblemAt, false);
            else
                DrawRect(emblemAt, culture);
            var x = emblemAt.End.X + 5f;
            if (badge is not null)
            {
                DrawTextureRect(badge, new Rect2(new Vector2(x - 2f, rect.Position.Y + 2f), new Vector2(TempPlateEmblem, TempPlateEmblem)), false);
                x += TempPlateEmblem + 3f;
            }
            DrawString(font, new Vector2(x, rect.Position.Y + 15f), name, HorizontalAlignment.Left, -1, TempPlateFont, Colors.White);
            var countX = x + nameWidth + 8f;
            if (vassal is not null)
            {
                var split = people.IndexOf('｜');
                var head = split >= 0 ? people[..split] : people;
                var tail = split >= 0 ? people[(split + 1)..] : "";
                var headWidth = font.GetStringSize(head, HorizontalAlignment.Left, -1, TempPlateFont - 2).X;
                DrawString(font, new Vector2(countX, rect.Position.Y + 15f), head, HorizontalAlignment.Left, -1, TempPlateFont - 2, new Color(0.95f, 0.85f, 0.55f));
                var iconX = countX + headWidth + 3f;
                DrawTextureRect(vassal, new Rect2(new Vector2(iconX, rect.Position.Y + 2f), new Vector2(TempPlateEmblem, TempPlateEmblem)), false);
                DrawString(font, new Vector2(iconX + TempPlateEmblem + 2f, rect.Position.Y + 15f), tail, HorizontalAlignment.Left, -1, TempPlateFont - 2,
                    new Color(0.95f, 0.85f, 0.55f));
            }
            else
                DrawString(font, new Vector2(countX, rect.Position.Y + 15f), people, HorizontalAlignment.Left, -1, TempPlateFont - 2,
                    new Color(0.95f, 0.85f, 0.55f));
        }
    }

    private StyleBoxTexture? _plateStyle;
    private bool _plateTried;

    /// <summary>第 08-18d 章：名牌底板（ui_nameplate 的不透明部分，左右尖角不拉伸）；沒有素材回傳 null。</summary>
    private StyleBoxTexture? PlateStyle()
    {
        if (_plateTried)
            return _plateStyle;
        _plateTried = true;
        if (PixelParts.IsBatch5("ui_nameplate") && PixelParts.Texture("ui_nameplate") is { } texture)
            _plateStyle = ArtAssets.Track(new StyleBoxTexture
            {
                Texture = texture,
                RegionRect = new Rect2(4f, 7f, 120f, 18f), // 【TEMP】素材不透明部分
                TextureMarginLeft = 12, TextureMarginRight = 12, TextureMarginTop = 6, TextureMarginBottom = 6
            });
        return _plateStyle;
    }

    /// <summary>第 08-18d 章：名牌上的層級徽章（營、村、鎮、城、都）。</summary>
    private static string TierBadge(DemoSettlementTier tier) => tier switch
    {
        DemoSettlementTier.Camp => "tier_camp",
        DemoSettlementTier.Village => "tier_village",
        DemoSettlementTier.Town => "tier_town",
        DemoSettlementTier.City => "tier_city",
        _ => "tier_capital"
    };
}
