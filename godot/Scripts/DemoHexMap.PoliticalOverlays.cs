using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-21 章：政治地圖上面疊的東西——河流、勢力名（CK3 式沿長軸排開的大字）、聚落徽章與名牌、怪物巢穴。
/// 依遠近分三層【TEMP】：遠看（每格不到 30 像素）只有勢力名與大勢力的首都；中距離加城鎮以上的名牌；
/// 拉近（60 像素以上）每個聚落都有名牌、勢力名淡出。名牌互相擋到時，層級高、人口多的優先。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】三層的分界（每格像素）。</summary>
    private const float TempMidCellPx = 30f, TempCloseCellPx = 60f;

    /// <summary>【TEMP】勢力名最大字級（像素）與最小（太小就不畫）。</summary>
    private const float TempLabelMaxPx = 72f, TempLabelMinPx = 11f;

    private static readonly Color TempPlateFill = new(0.09f, 0.11f, 0.20f, 0.93f);
    private static readonly Color TempPlateGold = new(0.80f, 0.66f, 0.36f);
    private static readonly Color TempPlateText = new(0.96f, 0.92f, 0.80f);
    private static readonly Color TempLairFill = new(0.24f, 0.04f, 0.05f, 0.95f);
    private static readonly Color TempLairRim = new(0.85f, 0.25f, 0.18f);

    // ── 字型 ──────────────────────────────────────────────────────────────

    private static Font? _mapFont;
    private static bool _mapFontTried;

    /// <summary>地圖用的襯線字（Noto Serif CJK TC Bold 的子集，只含地名用字；缺的字退回系統字型）。</summary>
    private static Font MapFont()
    {
        if (_mapFontTried)
            return _mapFont ?? ThemeDB.FallbackFont;
        _mapFontTried = true;
        const string path = "res://assets/fonts/map_serif_bold.otf";
        if (ResourceLoader.Exists(path) && ResourceLoader.Load<FontFile>(path) is { } font)
        {
            font.Fallbacks = new Godot.Collections.Array<Font> { ThemeDB.FallbackFont };
            _mapFont = font;
        }
        return _mapFont ?? ThemeDB.FallbackFont;
    }

    /// <summary>以 <paramref name="center"/> 為中心畫一個字（直立）。</summary>
    private void DrawGlyphCentered(Font font, Vector2 center, string text, int size, Color color, Color outline, int outlineSize)
    {
        var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
        var baseline = center + new Vector2(-width / 2f, (font.GetAscent(size) - font.GetDescent(size)) / 2f);
        if (outlineSize > 0)
            DrawStringOutline(font, baseline, text, HorizontalAlignment.Left, -1, size, outlineSize, outline);
        DrawString(font, baseline, text, HorizontalAlignment.Left, -1, size, color);
    }

    // ── 河流 ──────────────────────────────────────────────────────────────

    /// <summary>河流線段（格座標，已依扭曲對齊）；換世界才重算。</summary>
    private List<(Vector2 A, Vector2 B, bool Mouth)>? _rivers;

    private void EnsureRivers()
    {
        if (_rivers is not null || World is null)
            return;
        var world = World;
        _rivers = new List<(Vector2, Vector2, bool)>();
        bool River(int x, int y) => world.Contains(x, y) && DemoProvinceMap.IsRiver(world.GetCell(x, y));
        bool Sea(int x, int y) => world.Contains(x, y) && world.GetCell(x, y) is { Terrain: DemoTerrain.Water } w && !DemoProvinceMap.IsRiver(w);
        Vector2 At(int x, int y) => Unwarp(new Vector2(x + 0.5f, y + 0.5f));
        foreach (var cell in world.Cells)
        {
            if (!DemoProvinceMap.IsRiver(cell))
                continue;
            var (c, r) = (cell.Column, cell.Row);
            // 右、下直接相連；斜角只在兩邊都不是河時才連（不畫出三角形）。
            if (River(c + 1, r))
                _rivers.Add((At(c, r), At(c + 1, r), false));
            if (River(c, r + 1))
                _rivers.Add((At(c, r), At(c, r + 1), false));
            if (River(c + 1, r + 1) && !River(c + 1, r) && !River(c, r + 1))
                _rivers.Add((At(c, r), At(c + 1, r + 1), false));
            if (River(c - 1, r + 1) && !River(c - 1, r) && !River(c, r + 1))
                _rivers.Add((At(c, r), At(c - 1, r + 1), false));
            // 河口：流到海的那一邊畫半段。
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                if (Sea(c + dc, r + dr))
                    _rivers.Add((At(c, r), At(c, r) + new Vector2(dc, dr) * 0.6f, true));
        }
    }

    private void DrawRivers(Vector2 origin, float cellPx)
    {
        EnsureRivers();
        if (_rivers is null || _rivers.Count == 0)
            return;
        var width = Mathf.Clamp(cellPx * 0.085f, 1.1f, 4.6f);
        var color = new Color(0.22f, 0.34f, 0.45f, 0.95f);
        var core = new Color(0.36f, 0.50f, 0.60f, 0.9f);
        var view = new Rect2(Vector2.Zero, Size).Grow(cellPx);
        foreach (var (a, b, mouth) in _rivers)
        {
            var pa = origin + a * cellPx;
            var pb = origin + b * cellPx;
            if (!view.HasPoint(pa) && !view.HasPoint(pb))
                continue;
            var w = mouth ? width * 1.2f : width;
            DrawLine(pa, pb, color, w, true);
            DrawCircle(pa, w * 0.5f, color);
            if (cellPx >= TempCloseCellPx)
                DrawLine(pa, pb, core, w * 0.4f, true);
        }
    }

    // ── 勢力名（文化、信仰模式是文化名、信仰名） ───────────────────────────────

    /// <summary>一個分組的名字：每個字在格座標上的位置與字的大小（格）。</summary>
    private sealed record GroupLabel(long Group, string Text, Vector2[] Positions, float Size, int Cells);

    private List<GroupLabel> _labels = new();

    private string? GroupName(long group) => _mode switch
    {
        Mode.Realm or Mode.Terrain => Sites?.Settlements.FirstOrDefault(s => s.Id == group)?.Name is { } name
            ? (Kingdoms.Contains((int)group) ? name + "王國" : name) : null, // 第 08-24 章：王國寫「某某王國」
        Mode.Culture => CultureNames.TryGetValue((int)group, out var c) ? c : null,
        Mode.Faith => FaithNames.TryGetValue((int)group, out var f) ? f : null,
        _ => null
    };

    private void BuildGroupLabels()
    {
        _labels = new List<GroupLabel>();
        if (_provinces is null || World is null || _mode is not (Mode.Realm or Mode.Terrain or Mode.Culture or Mode.Faith))
            return;
        var byGroup = new Dictionary<long, HashSet<Vector2I>>();
        var provincesOf = new Dictionary<long, HashSet<int>>();
        for (var r = 0; r < World.Rows; r++)
            for (var c = 0; c < World.Columns; c++)
            {
                var p = _provinces.ProvinceAt(c, r);
                if (p == DemoProvinceMap.Wild)
                    continue;
                var g = _provinceGroup.GetValueOrDefault(p);
                if (g == 0)
                    continue;
                (byGroup.TryGetValue(g, out var set) ? set : byGroup[g] = new HashSet<Vector2I>()).Add(new Vector2I(c, r));
                (provincesOf.TryGetValue(g, out var ps) ? ps : provincesOf[g] = new HashSet<int>()).Add(p);
            }
        foreach (var (group, cells) in byGroup.OrderBy(kv => kv.Key))
        {
            var name = GroupName(group);
            // 只有一個省份的（獨立的小聚落）不寫勢力名：拉近時名牌就有名字，遠看不會滿地都是小字。
            if (string.IsNullOrEmpty(name) || cells.Count < 2 || provincesOf[group].Count < 2)
                continue;
            if (LayoutLabel(group, name.Replace("·", string.Empty, StringComparison.Ordinal), cells) is { } label)
                _labels.Add(label);
        }
    }

    /// <summary>
    /// 名字的排法（參考 CK3）：主軸＝各格位置的主成分方向（偏圓的取水平；偏直的由上往下），
    /// 中心＝離邊界最遠的格，沿主軸往兩邊量到邊界為止，字平均排開；字的大小取不超出寬度與每字分到的長度。
    /// </summary>
    private static GroupLabel? LayoutLabel(long group, string text, HashSet<Vector2I> cells)
    {
        var n = text.Length;
        if (n == 0)
            return null;
        double mx = 0, my = 0;
        foreach (var c in cells)
        {
            mx += c.X + 0.5;
            my += c.Y + 0.5;
        }
        mx /= cells.Count;
        my /= cells.Count;
        double sxx = 0, syy = 0, sxy = 0;
        foreach (var c in cells)
        {
            var dx = c.X + 0.5 - mx;
            var dy = c.Y + 0.5 - my;
            sxx += dx * dx;
            syy += dy * dy;
            sxy += dx * dy;
        }
        var trace = sxx + syy;
        var det = sxx * syy - sxy * sxy;
        var gap = Math.Sqrt(Math.Max(0, trace * trace / 4 - det));
        var l1 = trace / 2 + gap;
        var l2 = trace / 2 - gap;
        var angle = 0.5 * Math.Atan2(2 * sxy, sxx - syy);
        var axis = l1 < l2 * 1.4 ? new Vector2(1f, 0f) : new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        if (Math.Abs(axis.Y) > Math.Abs(axis.X) ? axis.Y < 0 : axis.X < 0)
            axis = -axis;
        var normal = new Vector2(-axis.Y, axis.X);

        // 離邊界最遠的格（四鄰有一格不是自己的就是邊界）。
        var depth = new Dictionary<Vector2I, int>();
        var queue = new Queue<Vector2I>();
        foreach (var c in cells)
            if (!cells.Contains(c + Vector2I.Right) || !cells.Contains(c + Vector2I.Left) || !cells.Contains(c + Vector2I.Up) || !cells.Contains(c + Vector2I.Down))
            {
                depth[c] = 1;
                queue.Enqueue(c);
            }
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var d in new[] { Vector2I.Right, Vector2I.Left, Vector2I.Up, Vector2I.Down })
                if (cells.Contains(c + d) && !depth.ContainsKey(c + d))
                {
                    depth[c + d] = depth[c] + 1;
                    queue.Enqueue(c + d);
                }
        }
        var centroid = new Vector2((float)mx, (float)my);
        var anchorCell = cells.OrderByDescending(c => depth.GetValueOrDefault(c))
            .ThenBy(c => (new Vector2(c.X + 0.5f, c.Y + 0.5f) - centroid).LengthSquared()).ThenBy(c => c.Y).ThenBy(c => c.X).First();
        var anchor = new Vector2(anchorCell.X + 0.5f, anchorCell.Y + 0.5f);
        // 往主軸兩邊量（容許一步出界，才跨得過細小的缺口）。
        float Extent(Vector2 dir)
        {
            float t = 0f, last = 0f;
            var misses = 0;
            while (t < 200f)
            {
                t += 0.25f;
                var p = anchor + dir * t;
                if (cells.Contains(new Vector2I(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y))))
                {
                    last = t;
                    misses = 0;
                }
                else if (++misses > 4)
                    break;
            }
            return last + 0.4f;
        }
        var ahead = Extent(axis);
        var behind = Extent(-axis);
        var width = Extent(normal) + Extent(-normal);
        var length = (ahead + behind) * 0.86f; // 兩端留一點邊
        var spacing = length / n;
        var size = Math.Min(width * 0.62f, spacing * 0.92f);
        if (spacing > size * 2.4f)
            spacing = size * 2.4f;
        var middle = anchor + axis * ((ahead - behind) / 2f);
        var positions = new Vector2[n];
        for (var i = 0; i < n; i++)
            positions[i] = middle + axis * ((i - (n - 1) / 2f) * spacing);
        return new GroupLabel(group, text, positions, size, cells.Count);
    }

    private void DrawGroupLabels(Vector2 origin, float cellPx)
    {
        if (_labels.Count == 0)
            return;
        // 遠看最清楚，拉近淡出（中距離改看名牌）。
        var fade = 1f - Mathf.SmoothStep(TempMidCellPx * 1.1f, TempCloseCellPx * 1.15f, cellPx);
        if (fade <= 0.02f)
            return;
        var font = MapFont();
        var view = new Rect2(Vector2.Zero, Size);
        foreach (var label in _labels)
        {
            var px = Math.Min(label.Size * cellPx, TempLabelMaxPx);
            if (px < TempLabelMinPx)
                continue;
            var size = (int)px;
            var fill = _mode is Mode.Realm or Mode.Terrain ? PoliticalRealmColor((int)label.Group)
                : _mode == Mode.Culture ? CultureColor((int)label.Group) : FaithColor((int)label.Group);
            var ink = new Color(0.14f, 0.10f, 0.13f, 0.92f * fade);
            var glow = new Color(fill.Lightened(0.35f), 0.45f * fade);
            var outline = Math.Max(2, size / 9);
            for (var i = 0; i < label.Positions.Length; i++)
            {
                var at = origin + label.Positions[i] * cellPx;
                if (!view.Grow(px).HasPoint(at))
                    continue;
                DrawGlyphCentered(font, at, label.Text[i].ToString(), size, ink, glow, outline);
            }
        }
    }

    // ── 聚落徽章與名牌 ─────────────────────────────────────────────────────

    private void DrawHoldings(Vector2 origin, float cellPx)
    {
        if (World is null || Sites is null || _provinces is null)
            return;
        var mapFont = MapFont();
        var view = new Rect2(Vector2.Zero, Size).Grow(40f);
        // 勢力有幾個省份（大勢力的首都遠看也畫）。
        var realmSize = new Dictionary<int, int>();
        foreach (var id in _provinceIndex.Keys)
        {
            var realm = Territory.RealmOf(id);
            realmSize[realm] = realmSize.GetValueOrDefault(realm) + 1;
        }
        var sites = Sites.Settlements
            .Where(s => !s.IsRuin && !FadedRuins.Contains(s.Id) && SettlementScale.ContainsKey(s.Id))
            .Select(s => (Site: s, Scale: SettlementScale[s.Id], Capital: Territory.RealmOf(s.Id) == s.Id && realmSize.GetValueOrDefault(s.Id) >= 2))
            .OrderByDescending(x => x.Capital).ThenByDescending(x => x.Scale.Tier).ThenByDescending(x => x.Scale.Population).ThenBy(x => x.Site.Id)
            .ToList();
        var placed = new List<Rect2>();
        foreach (var (site, scale, capital) in sites)
        {
            var tier = scale.Tier;
            var far = cellPx < TempMidCellPx;
            var close = cellPx >= TempCloseCellPx;
            // 遠看：只畫有兩個以上省份的勢力首都；中距離：城鎮以上；拉近：全部。
            if (far && !(capital && realmSize.GetValueOrDefault(site.Id) >= 3))
                continue;
            var major = capital || tier >= DemoSettlementTier.Town;
            var center = PoliticalCenter(origin, cellPx, site.Column, site.Row);
            if (!view.HasPoint(center))
                continue;
            var radius = far ? 7f : Mathf.Clamp(cellPx * (major ? 0.2f : 0.15f), major ? 8f : 5f, major ? 15f : 11f);
            if (!far && !close && !major)
            {
                // 中距離的小村：只畫小圓點。
                DrawCircle(center, 3.2f, TempPlateGold.Darkened(0.2f));
                DrawCircle(center, 2.1f, TempPlateFill);
                continue;
            }
            DrawMedallion(center, radius, tier, capital);
            _plateHits.Add((new Rect2(center - new Vector2(radius, radius), new Vector2(radius, radius) * 2f).Grow(3f), site.Id));
            if (far)
                continue;
            var name = DemoSettlementHistory.TitledName(site, tier);
            var fontSize = tier >= DemoSettlementTier.City || capital ? 15 : tier == DemoSettlementTier.Town ? 13 : 12;
            if (close)
                fontSize += 1;
            var textWidth = mapFont.GetStringSize(name, HorizontalAlignment.Left, -1, fontSize).X;
            var plate = new Rect2(center.X - textWidth / 2f - 7f, center.Y + radius + 2f, textWidth + 14f, fontSize + 8f);
            if (placed.Any(r => r.Intersects(plate)))
                continue; // 擋到更重要的名牌就不畫（徽章照畫）
            placed.Add(plate);
            _plateHits.Add((plate.Grow(2f), site.Id));
            var hover = _hoverPlate == site.Id;
            DrawRect(plate, TempPlateFill);
            DrawRect(plate, hover ? new Color(1f, 0.92f, 0.55f) : TempPlateGold, false, capital || hover ? 2f : 1f);
            // 左右兩端的金色小菱形（CK3 名牌的收尾）。
            foreach (var x in new[] { plate.Position.X, plate.End.X })
            {
                var mid = new Vector2(x, plate.Position.Y + plate.Size.Y / 2f);
                DrawColoredPolygon(new[] { mid + new Vector2(-3f, 0f), mid + new Vector2(0f, -3f), mid + new Vector2(3f, 0f), mid + new Vector2(0f, 3f) }, TempPlateGold);
            }
            DrawString(mapFont, new Vector2(plate.Position.X + 7f, plate.Position.Y + 4f + mapFont.GetAscent(fontSize)), name,
                HorizontalAlignment.Left, -1, fontSize, TempPlateText);
        }
    }

    /// <summary>聚落徽章：深藍圓底、金邊，中間是層級的圖形（帳篷、房子、塔、城堡）；勢力首都頭上一頂王冠。</summary>
    private void DrawMedallion(Vector2 c, float r, DemoSettlementTier tier, bool capital)
    {
        DrawCircle(c + new Vector2(0f, 1.5f), r + 1.5f, new Color(0f, 0f, 0f, 0.35f));
        DrawCircle(c, r + 1.2f, TempPlateGold);
        DrawCircle(c, r, TempPlateFill);
        var ink = TempPlateText;
        var s = r * 0.62f;
        switch (tier)
        {
            case DemoSettlementTier.Camp:
                DrawColoredPolygon(new[] { c + new Vector2(-s, s * 0.7f), c + new Vector2(s, s * 0.7f), c + new Vector2(0f, -s * 0.85f) }, ink);
                DrawLine(c + new Vector2(0f, s * 0.7f), c + new Vector2(0f, 0f), TempPlateFill, Math.Max(1f, s * 0.22f));
                break;
            case DemoSettlementTier.Village:
                DrawRect(new Rect2(c + new Vector2(-s * 0.7f, -s * 0.05f), new Vector2(s * 1.4f, s * 0.8f)), ink);
                DrawColoredPolygon(new[] { c + new Vector2(-s * 0.95f, 0f), c + new Vector2(s * 0.95f, 0f), c + new Vector2(0f, -s * 0.85f) }, ink);
                break;
            case DemoSettlementTier.Town:
                DrawRect(new Rect2(c + new Vector2(-s * 0.45f, -s * 0.6f), new Vector2(s * 0.9f, s * 1.4f)), ink);
                for (var i = 0; i < 3; i++)
                    DrawRect(new Rect2(c + new Vector2(-s * 0.6f + i * s * 0.45f, -s * 0.9f), new Vector2(s * 0.3f, s * 0.35f)), ink);
                break;
            default:
                // 城市、首都：兩座塔夾一道牆。
                DrawRect(new Rect2(c + new Vector2(-s * 0.95f, -s * 0.55f), new Vector2(s * 0.55f, s * 1.35f)), ink);
                DrawRect(new Rect2(c + new Vector2(s * 0.4f, -s * 0.55f), new Vector2(s * 0.55f, s * 1.35f)), ink);
                DrawRect(new Rect2(c + new Vector2(-s * 0.45f, -s * 0.1f), new Vector2(s * 0.9f, s * 0.9f)), ink);
                foreach (var x in new[] { -s * 0.95f, -s * 0.62f, s * 0.4f, s * 0.73f })
                    DrawRect(new Rect2(c + new Vector2(x, -s * 0.8f), new Vector2(s * 0.22f, s * 0.3f)), ink);
                DrawRect(new Rect2(c + new Vector2(-s * 0.15f, s * 0.35f), new Vector2(s * 0.3f, s * 0.45f)), TempPlateFill);
                break;
        }
        if (!capital)
            return;
        // 王冠。
        var top = c - new Vector2(0f, r + 2f);
        var w = r * 0.9f;
        DrawColoredPolygon(new[]
        {
            top + new Vector2(-w, 0f), top + new Vector2(w, 0f), top + new Vector2(w, -w * 0.75f), top + new Vector2(w * 0.5f, -w * 0.35f),
            top + new Vector2(0f, -w * 0.9f), top + new Vector2(-w * 0.5f, -w * 0.35f), top + new Vector2(-w, -w * 0.75f)
        }, new Color(0.95f, 0.78f, 0.30f));
        DrawPolyline(new[]
        {
            top + new Vector2(-w, 0f), top + new Vector2(w, 0f), top + new Vector2(w, -w * 0.75f), top + new Vector2(w * 0.5f, -w * 0.35f),
            top + new Vector2(0f, -w * 0.9f), top + new Vector2(-w * 0.5f, -w * 0.35f), top + new Vector2(-w, -w * 0.75f), top + new Vector2(-w, 0f)
        }, new Color(0.35f, 0.22f, 0.06f), 1f);
    }

    // ── 怪物巢穴 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 地下城：有怪物的畫暗紅圓底的骷髏與數量（遠看也畫，威脅要看得到）；沉寂的只在拉近時畫灰色。
    /// 地盤本身由著色器塗成暗紅斜紋。
    /// </summary>
    /// <summary>
    /// 第 08-23 章【TEMP，畫面】商路：中距離以上（每格 30 像素以上），從賣糧的村莊畫一條流動的金色虛線到它的市集，
    /// 市集那一端畫一個小圓點（周圍多個村莊供應一個小鎮，一眼看得出誰養活誰）。
    /// </summary>
    private void DrawTradeLinks(Vector2 origin, float cellPx)
    {
        if (TradeLinks.Count == 0 || cellPx < TempMidCellPx || _mode is not (Mode.Realm or Mode.Terrain))
            return;
        var view = new Rect2(Vector2.Zero, Size).Grow(40f);
        var width = Mathf.Clamp(cellPx * 0.05f, 1.5f, 3.5f);
        var dash = Mathf.Clamp(cellPx * 0.18f, 5f, 14f);
        var flow = (Time.GetTicksMsec() / 900f) % 1f; // 虛線慢慢往市集流
        var color = new Color(0.98f, 0.82f, 0.38f, 0.8f);
        var shadow = new Color(0.12f, 0.08f, 0.02f, 0.55f);
        foreach (var (from, to) in TradeLinks)
        {
            var a = PoliticalCenter(origin, cellPx, from.Column, from.Row);
            var b = PoliticalCenter(origin, cellPx, to.Column, to.Row);
            if (!view.HasPoint(a) && !view.HasPoint(b))
                continue;
            var length = a.DistanceTo(b);
            if (length < 1f)
                continue;
            var dir = (b - a) / length;
            // 兩端各留一點空（不壓到聚落徽章）。
            var start = a + dir * Mathf.Min(length * 0.25f, cellPx * 0.3f);
            var end = b - dir * Mathf.Min(length * 0.25f, cellPx * 0.35f);
            var shift = dir * (flow * dash * 2f);
            DrawLine(start + new Vector2(0f, 1.5f), end + new Vector2(0f, 1.5f), shadow, width + 1f, true);
            for (var t = start + shift; (t - start).Dot(dir) < (end - start).Dot(dir); t += dir * dash * 2f)
            {
                var segmentEnd = t + dir * dash;
                if ((segmentEnd - start).Dot(dir) > (end - start).Dot(dir))
                    segmentEnd = end;
                DrawLine(t, segmentEnd, color, width, true);
            }
            DrawCircle(end, width * 1.6f, color);
        }
        _animatedOnScreen = true;
    }

    private void DrawLairs(Vector2 origin, float cellPx)
    {
        if (World is null || _provinces is null)
            return;
        var font = MapFont();
        var view = new Rect2(Vector2.Zero, Size).Grow(40f);
        var active = new HashSet<DemoCellRef>();
        foreach (var nest in _provinces.Nests)
        {
            var cell = nest.Dungeon.Cell;
            active.Add(cell);
            var center = PoliticalCenter(origin, cellPx, cell.Column, cell.Row);
            if (!view.HasPoint(center))
                continue;
            var r = Mathf.Clamp(cellPx * 0.24f, 7f, 15f);
            // 第 12-2h 章：滿員的地下城隨時會爆發獸潮——外圈閃紅光。
            var full = nest.Monsters >= DemoMonsters.Cap(nest.Dungeon);
            if (full)
            {
                var pulse = 0.5f + 0.5f * Mathf.Sin(Time.GetTicksMsec() / 260f);
                DrawArc(center, r + 5f + pulse * 3f, 0f, Mathf.Tau, 32, new Color(1f, 0.25f, 0.15f, 0.55f + 0.4f * pulse), 2.5f, true);
                _animatedOnScreen = true;
            }
            DrawCircle(center + new Vector2(0f, 1.5f), r + 2f, new Color(0f, 0f, 0f, 0.4f));
            DrawCircle(center, r + 1.5f, TempLairRim);
            DrawCircle(center, r, TempLairFill);
            DrawSkull(center, r * 0.62f);
            if (cellPx < TempMidCellPx)
            {
                // 遠看：數量寫在圖示右下角。
                DrawGlyphCentered(font, center + new Vector2(r + 6f, r * 0.6f), nest.Monsters.ToString(), 12, new Color(1f, 0.85f, 0.8f),
                    new Color(0.2f, 0.02f, 0.03f, 0.9f), 3);
                continue;
            }
            var text = $"{nest.Dungeon.Name}　怪物 {nest.Monsters}（{nest.Dungeon.Layers} 級）" + (full ? "　獸潮將至" : "");
            var size = 12;
            var width = font.GetStringSize(text, HorizontalAlignment.Left, -1, size).X;
            var plate = new Rect2(center.X - width / 2f - 6f, center.Y + r + 3f, width + 12f, size + 8f);
            DrawRect(plate, new Color(0.18f, 0.03f, 0.04f, 0.92f));
            DrawRect(plate, TempLairRim, false, 1f);
            DrawString(font, new Vector2(plate.Position.X + 6f, plate.Position.Y + 4f + font.GetAscent(size)), text, HorizontalAlignment.Left, -1, size,
                new Color(1f, 0.88f, 0.84f));
        }
        if (cellPx < TempCloseCellPx)
            return;
        foreach (var cell in Dungeons)
        {
            if (active.Contains(cell) || !World.Contains(cell.Column, cell.Row))
                continue;
            var center = PoliticalCenter(origin, cellPx, cell.Column, cell.Row);
            if (!view.HasPoint(center))
                continue;
            var r = Mathf.Clamp(cellPx * 0.18f, 6f, 11f);
            DrawCircle(center, r + 1f, new Color(0.55f, 0.52f, 0.48f));
            DrawCircle(center, r, new Color(0.22f, 0.21f, 0.20f));
            DrawSkull(center, r * 0.6f, new Color(0.7f, 0.68f, 0.64f));
        }
    }

    /// <summary>簡單的骷髏：頭骨、兩個眼窩、下巴。</summary>
    private void DrawSkull(Vector2 c, float s, Color? color = null)
    {
        var bone = color ?? new Color(0.96f, 0.90f, 0.82f);
        DrawCircle(c - new Vector2(0f, s * 0.15f), s * 0.75f, bone);
        DrawRect(new Rect2(c + new Vector2(-s * 0.42f, s * 0.2f), new Vector2(s * 0.84f, s * 0.5f)), bone);
        var hole = new Color(0.12f, 0.02f, 0.03f);
        DrawCircle(c + new Vector2(-s * 0.3f, -s * 0.12f), s * 0.2f, hole);
        DrawCircle(c + new Vector2(s * 0.3f, -s * 0.12f), s * 0.2f, hole);
        DrawLine(c + new Vector2(-s * 0.15f, s * 0.5f), c + new Vector2(-s * 0.15f, s * 0.7f), hole, Math.Max(1f, s * 0.1f));
        DrawLine(c + new Vector2(s * 0.15f, s * 0.5f), c + new Vector2(s * 0.15f, s * 0.7f), hole, Math.Max(1f, s * 0.1f));
    }
}
