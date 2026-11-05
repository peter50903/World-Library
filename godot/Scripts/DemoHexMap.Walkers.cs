using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18b 章第 4 節：近景層的小人——人數依聚落人口推算（每 10 人一個，最多 12 個），在家、田、伐木地、礦坑、廣場之間走動；
/// 頭上偶爾冒想法泡泡（餓、工作、開心）。只是表現：不進模擬、不抽世界亂數、不存檔，位置只由時間與固定雜湊決定。
/// 不做逐格尋路，直接走直線。區域層與戰略層照舊畫指示物（第 08-17 章第 5 節）。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】每幾人一個小人、每個聚落最多幾個、格半徑至少幾像素才畫；一趟來回的秒數。</summary>
    private const int TempPeoplePerWalker = 10;
    private const int TempMaxWalkers = 12;
    private const float TempWalkerMinRadius = 18f;
    private const float TempWalkerTripSeconds = 16f;

    private bool _walkersDrawn;

    /// <summary>近景層：每個畫面內聚落的小人（在建築之後、名牌之前畫）。</summary>
    private void DrawWalkers(Vector2 start, float radius)
    {
        _walkersDrawn = false;
        if (World is null || Sites is null || radius < TempWalkerMinRadius)
            return;
        var t = TerrainSurface.TempTile;
        Vector2 Screen(float tx, float ty) => TerrainSurface.TexelToScreen(new Vector2(tx * t, ty * t), start, radius);
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
            var count = (int)Math.Clamp((scale.Item1 + TempPeoplePerWalker - 1) / TempPeoplePerWalker, 1, TempMaxWalkers);
            var houses = layout.Lots.Where(l => IsResidence(l.Kind) && !l.Construction).ToList();
            var plaza = Screen(layout.Plaza.Position.X + layout.Plaza.Size.X / 2f, layout.Plaza.Position.Y + layout.Plaza.Size.Y / 2f + 0.5f);
            // 工作地：農田、伐木地、礦坑、獵場、水邊（與區域層的職業棋相同），再加廣場。
            var works = tokens.Where(k => k.SettlementId == site.Id && k.Kind != TokenKind.Residents)
                .Select(k => (k.Kind, Cell: k.Work)).ToList();
            var starving = FoodStatuses.TryGetValue(site.Id, out var food) && food == FoodStatus.Starving;
            var tunic = CultureOf.TryGetValue(site.Id, out var culture) ? CultureColor(culture) : new Color(0.55f, 0.45f, 0.35f);
            for (var i = 0; i < count; i++)
            {
                var h = LayoutHash(site.Id, i, 91);
                var door = houses.Count > 0
                    ? Screen(houses[i % houses.Count].X + houses[i % houses.Count].W / 2f, houses[i % houses.Count].Y + houses[i % houses.Count].H + 0.3f)
                    : plaza;
                Vector2 target;
                var working = works.Count > 0 && i % (works.Count + 1) < works.Count;
                if (working)
                {
                    var cell = World.GetCell(works[i % (works.Count + 1)].Cell.Column, works[i % (works.Count + 1)].Cell.Row);
                    target = start + HexCoordinates.Center(cell.AxialQ, cell.AxialR, radius)
                        + new Vector2(LayoutHash(site.Id, i, 92) - 0.5f, LayoutHash(site.Id, i, 93) - 0.5f) * radius * 1.2f;
                }
                else
                    target = plaza + new Vector2(LayoutHash(site.Id, i, 94) - 0.5f, LayoutHash(site.Id, i, 95) - 0.5f) * radius * 0.5f;
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
                people.Add((at, at.Y, () => DrawWalker(at, radius, color, frame, facing, bubble)));
            }
            _walkersDrawn = true;
        }
        foreach (var (_, _, draw) in people.OrderBy(x => x.Y))
            draw();
    }

    /// <summary>一個小人（約 8×14 圖像素）：落地陰影、兩條腿（走路時交替）、文化色上衣、頭、偶爾一個想法泡泡（固定大小）。</summary>
    private void DrawWalker(Vector2 feet, float radius, Color tunic, int frame, float facing, string? bubble)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
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
