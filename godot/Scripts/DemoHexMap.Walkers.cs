using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18b 章第 4 節：近景層的小人，在家、田、伐木地、礦坑、廣場之間走動。試玩回饋 2026-11-08 之 6：每個小人都是一個工作團體
/// （農夫、樵夫、礦工、獵人、漁夫；人多時後面跟著一兩個）或聚落的領袖，可以點開詳情；
/// 頭上偶爾冒想法泡泡（餓、工作、開心）。只是表現：不進模擬、不抽世界亂數、不存檔，位置只由時間與固定雜湊決定。
/// 不做逐格尋路，直接走直線。區域層與戰略層照舊畫指示物（第 08-17 章第 5 節）。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】團體每幾人多畫一個跟在後面的小人、格半徑至少幾像素才畫；一趟來回的秒數。</summary>
    private const long TempPeoplePerFollower = 20;
    private const float TempWalkerMinRadius = 18f;
    private const float TempWalkerTripSeconds = 16f;

    private bool _walkersDrawn;

    /// <summary>近景層：每個畫面內聚落的小人（在建築之後、名牌之前畫）。</summary>
    private void DrawWalkers(Vector2 start, float radius)
    {
        _walkersDrawn = false;
        _walkerHits.Clear();
        _actorHits.RemoveAll(a => a.Actor.Kind is ActorKind.Leader or ActorKind.WorkGroup);
        if (World is null || Sites is null || radius < TempWalkerMinRadius)
            return;
        var seconds = Time.GetTicksMsec() / 1000f;
        var tokens = Tokens();
        var people = new List<(Vector2 At, float Y, Action Draw)>();
        foreach (var site in Sites.Settlements)
        {
            if (site.IsRuin || FadedRuins.Contains(site.Id) || !_layouts.TryGetValue(site.Id, out var layout)
                || !SettlementScale.TryGetValue(site.Id, out var scale) || scale.Item1 <= 0)
                continue;
            var home = World.GetCell(site.Column, site.Row);
            var homeCenter = start + HexCoordinates.Center(home.AxialQ, home.AxialR, radius);
            if (!OnScreen(homeCenter, radius * 4f))
                continue;
            var houses = layout.Lots.Where(l => IsResidence(l.Kind) && !l.Construction).ToList();
            // 試玩回饋 2026-11-08 之 6：畫面上的角色都是有影響力的人物或團體——每個工作團體（農夫、樵夫、礦工、獵人、漁夫，
            // 與區域層的職業棋相同）一個帶頭的小人（人多時後面跟著一兩個），再加一位聚落的領袖；都可以點。
            var groups = tokens.Where(k => k.SettlementId == site.Id && k.Kind != TokenKind.Residents).ToList();
            var works = groups.Select(k => (k.Kind, Cell: k.Work)).ToList();
            var count = works.Count + 1;
            var starving = FoodStatuses.TryGetValue(site.Id, out var food) && food == FoodStatus.Starving;
            var tunic = CultureOf.TryGetValue(site.Id, out var culture) ? CultureColor(culture) : new Color(0.55f, 0.45f, 0.35f);
            for (var i = 0; i < count; i++)
            {
                var h = LayoutHash(site.Id, i, 91);
                var (doorTile, targetTile, working) = WalkerTrip(site.Id, i, layout, houses, works);
                var door = TileFeet(doorTile, start, radius);
                var target = TileFeet(targetTile, start, radius);
                // 一趟：出門走 35%、在工作地停 15%、走回 35%、在家停 15%（每人錯開時間、速度稍有不同）。
                var period = TempWalkerTripSeconds * (0.8f + 0.4f * h);
                var cycle = seconds / period + h * 7f;
                var phase = cycle - Mathf.Floor(cycle);
                var trip = (int)Mathf.Floor(cycle);
                Vector2 at;
                var walking = true;
                var heading = target - door;
                if (phase < 0.35f)
                    at = door.Lerp(target, phase / 0.35f);
                else if (phase < 0.5f)
                {
                    at = target;
                    walking = false;
                }
                else if (phase < 0.85f)
                {
                    at = target.Lerp(door, (phase - 0.5f) / 0.35f);
                    heading = -heading;
                }
                else
                {
                    at = door;
                    walking = false;
                }
                if (!OnScreen(at, radius))
                    continue;
                // 想法泡泡：每四趟大概冒一次，只在停下來的時候；缺糧的聚落冒「餓」。
                string? bubble = null;
                if (!walking && LayoutHash(site.Id, i * 31 + trip, 96) < 0.25f)
                    bubble = starving ? "餓" : phase < 0.5f && working ? "工" : "樂";
                var frame = walking ? (int)(seconds * 4f + h * 4f) % 2 : 0;
                var facing = heading.X < 0f ? -1f : 1f;
                var color = tunic;
                // 第 08-18d 章：小人素材依職業（工作中）或男女老少；走路時 4 格動畫。
                var group = i < groups.Count ? groups[i] : (Token?)null;
                var unit = group is { } g ? UnitArt(g.Kind, i) : "unit_leader";
                var step = walking ? (int)(seconds * 7f + h * 4f) % 4 : 0;
                var art = bubble is null ? null : BubbleArt(bubble, h);
                people.Add((at, at.Y, () => DrawWalker(at, radius, color, frame, facing, bubble, unit, step, art)));
                // 團體人多時，後面跟著一兩個同職業的小人（每 20 人多一個，最多三人一列）。
                var followers = group is { } crowd ? (int)Math.Min(2, crowd.People / TempPeoplePerFollower) : 0;
                for (var k = 1; k <= followers; k++)
                {
                    var behind = at - new Vector2(facing * k * radius * 0.2f, -k * radius * 0.04f);
                    var lag = (step + k) % 4;
                    people.Add((behind, behind.Y, () => DrawWalker(behind, radius, color, frame, facing, null, unit, lag, null)));
                }
                _walkerHits.Add((at, site.Id));
                _actorHits.Add((at, new MapActor(group is null ? ActorKind.Leader : ActorKind.WorkGroup, site.Id,
                    group?.Kind ?? TokenKind.Residents, group?.People ?? scale.Item1, group?.Work ?? new DemoCellRef(site.Column, site.Row))));
            }
            _walkersDrawn = true;
        }
        foreach (var (_, _, draw) in people.OrderBy(x => x.Y))
            draw();
    }

    private readonly Dictionary<(int, int), (Vector2I, Vector2I, bool)> _trips = new();
    private object? _tripKey;

    /// <summary>
    /// 一個小人的家門與目的地（方格）。試玩回饋 2026-11-06 之 4、5：兩端都站在空地上（陸地、不被樹擋住），
    /// 漁民走到水邊而不是水裡；中間的直線會穿過水或樹林時改去廣場。結果依聚落版面與職業棋快取。
    /// </summary>
    private (Vector2I Door, Vector2I Target, bool Working) WalkerTrip(int siteId, int i, SettlementPlan layout, List<Lot> houses, List<(TokenKind Kind, DemoCellRef Cell)> works)
    {
        var key = (object)(_tokenKey, _ground);
        if (!key.Equals(_tripKey))
        {
            _trips.Clear();
            _tripKey = key;
        }
        if (_trips.TryGetValue((siteId, i), out var known))
            return known;
        var plaza = new Vector2I(layout.Plaza.Position.X + layout.Plaza.Size.X / 2, layout.Plaza.End.Y);
        var door = houses.Count > 0
            ? new Vector2I(houses[i % houses.Count].X + houses[i % houses.Count].W / 2, houses[i % houses.Count].Y + houses[i % houses.Count].H)
            : plaza;
        door = NearestOpen(door, 2, false) ?? plaza;
        Vector2I? target = null;
        var working = works.Count > 0 && i % (works.Count + 1) < works.Count;
        if (working)
        {
            var (kind, cell) = works[i % (works.Count + 1)];
            var center = TerrainSurface.TileOfCell(cell.Column, cell.Row)
                + new Vector2I((int)(LayoutHash(siteId, i, 92) * 4f) - 2, (int)(LayoutHash(siteId, i, 93) * 4f) - 2);
            target = NearestOpen(center, kind == TokenKind.Fishers ? 6 : 3, kind == TokenKind.Fishers);
            if (target is { } t && !ClearPath(door, t))
                target = null;
        }
        if (target is null)
        {
            working = false;
            target = NearestOpen(plaza + new Vector2I((int)(LayoutHash(siteId, i, 94) * 3f) - 1, 0), 2, false) ?? door;
        }
        return _trips[(siteId, i)] = (door, target.Value, working);
    }

    /// <summary>離 <paramref name="from"/> 最近的空地方格（<paramref name="shore"/>＝要緊鄰水邊）；找不到回傳 null。</summary>
    private Vector2I? NearestOpen(Vector2I from, int reach, bool shore)
    {
        for (var r = 0; r <= reach; r++)
            for (var dy = -r; dy <= r; dy++)
                for (var dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                        continue;
                    var t = from + new Vector2I(dx, dy);
                    if (!_surface!.OpenTile(t.X, t.Y))
                        continue;
                    if (shore && !(_surface.WaterTile(t.X + 1, t.Y) || _surface.WaterTile(t.X - 1, t.Y) || _surface.WaterTile(t.X, t.Y + 1) || _surface.WaterTile(t.X, t.Y - 1)))
                        continue;
                    return t;
                }
        return null;
    }

    /// <summary>一個小人（約 8×14 圖像素）：落地陰影、兩條腿（走路時交替）、文化色上衣、頭、偶爾一個想法泡泡（固定大小）。</summary>
    private void DrawWalker(Vector2 feet, float radius, Color tunic, int frame, float facing, string? bubble, string? unit = null, int step = 0, string? bubbleArt = null)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        if (unit is not null && PixelParts.Tinted(unit, tunic) is { } sheet)
        {
            // 第 08-18d 章：第五批小人素材（衣服染文化色，面向右，往左走時翻轉）；泡泡用素材、至少 18 像素大。
            DrawSetTransform(feet + new Vector2(p, 0f), 0f, new Vector2(1f, 0.35f));
            DrawCircle(Vector2.Zero, 5f * p, new Color(0f, 0f, 0f, 0.3f));
            DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
            DrawArt(sheet, feet, radius, 4, step, facing < 0f);
            if (bubbleArt is not null && PixelParts.Texture(bubbleArt) is { } icon)
            {
                var scale = Math.Max(1f, 18f / PixelParts.ScreenSize(icon, radius).Y);
                DrawArt(icon, feet - new Vector2(0f, 22f * p), radius, scale: scale);
            }
            return;
        }
        DrawSetTransform(feet + new Vector2(p, 0f), 0f, new Vector2(1f, 0.35f));
        DrawCircle(Vector2.Zero, 4f * p, new Color(0f, 0f, 0f, 0.3f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
        var legs = new Color(0.30f, 0.22f, 0.16f);
        var stride = frame == 0 ? 0f : 1f;
        DrawRect(new Rect2(feet.X - 3f * p, feet.Y - 5f * p + stride * p, 2f * p, 5f * p - stride * p), legs);
        DrawRect(new Rect2(feet.X + 1f * p, feet.Y - 5f * p + (1f - stride) * p, 2f * p, 5f * p - (1f - stride) * p), legs);
        var body = new Rect2(feet.X - 4f * p, feet.Y - 11f * p, 8f * p, 6f * p);
        DrawRect(body, tunic);
        DrawRect(new Rect2(body.Position.X + (facing > 0 ? 6f : 0f) * p, body.Position.Y, 2f * p, body.Size.Y), tunic.Darkened(0.25f));
        var head = new Rect2(feet.X - 2.5f * p, feet.Y - 15f * p, 5f * p, 4f * p);
        DrawRect(head, new Color(0.92f, 0.74f, 0.58f));
        DrawRect(new Rect2(head.Position.X, head.Position.Y, head.Size.X, 1.5f * p), new Color(0.32f, 0.22f, 0.14f)); // 頭髮
        DrawRect(new Rect2(head.Position.X + (facing > 0 ? 3.5f : 0.5f) * p, head.Position.Y + 2f * p, p, p), Ink); // 眼睛（面向走的方向）
        if (bubble is null)
            return;
        var font = ThemeDB.FallbackFont;
        const int size = 11;
        var width = font.GetStringSize(bubble, HorizontalAlignment.Left, -1, size).X;
        var box = new Rect2(feet.X - width / 2f - 4f, feet.Y - 16f * p - 20f, width + 8f, 16f);
        DrawRect(box, new Color(1f, 1f, 1f, 0.92f));
        DrawRect(box, Ink, false, 1f);
        DrawColoredPolygon(new[] { new Vector2(feet.X - 3f, box.End.Y), new Vector2(feet.X + 3f, box.End.Y), new Vector2(feet.X, box.End.Y + 4f) }, new Color(1f, 1f, 1f, 0.92f));
        DrawString(font, new Vector2(box.Position.X + 4f, box.End.Y - 3f), bubble, HorizontalAlignment.Left, -1, size,
            bubble == "餓" ? new Color(0.75f, 0.15f, 0.10f) : Ink);
    }
}
