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

    /// <summary>【TEMP】勢力名最大字級（像素）與最小（太小就不畫）。第 08-41 章：72→56，大勢力的名字不再壓滿整片。</summary>
    private const float TempLabelMaxPx = 56f, TempLabelMinPx = 11f;

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

    /// <summary>
    /// 第 08-56 章（試玩回饋 2026-11-26 第二次之 2：近看時河流醜到不行——原本是格中心之間的直線段，一折一折、粗細都一樣）：
    /// 河流改成一條條平滑的曲線——相連的河格串成河段，角切圓（Chaikin 三次），再沿河道加一點蜿蜒；
    /// 越靠近出海口越寬（沿河道離河口幾格）。每點記寬度比例。換世界才重算。
    /// </summary>
    private List<(Vector2[] Points, float[] Widths)>? _rivers;

    /// <summary>【TEMP】河流：最窄與最寬的比例、幾格之後就是最窄、蜿蜒幅度（格）、切圓次數。</summary>
    private const float TempRiverNarrow = 0.45f, TempRiverFullSteps = 24f, TempRiverMeander = 0.16f;
    private const int TempRiverSmooth = 3;

    private static float RiverNoise(float x, int seed)
    {
        static float H(int i, int s) => ((i * 374761393 + s * 668265263) & 0x7fffffff) % 10007 / 10007f;
        var i0 = (int)MathF.Floor(x);
        var f = x - i0;
        var u = f * f * (3f - 2f * f);
        return Mathf.Lerp(H(i0, seed), H(i0 + 1, seed), u) - 0.5f;
    }

    private void EnsureRivers()
    {
        if (_rivers is not null || World is null)
            return;
        var world = World;
        _rivers = new List<(Vector2[], float[])>();
        bool River(int x, int y) => world.Contains(x, y) && DemoProvinceMap.IsRiver(world.GetCell(x, y));
        bool Sea(int x, int y) => world.Contains(x, y) && world.GetCell(x, y) is { Terrain: DemoTerrain.Water } w && !DemoProvinceMap.IsRiver(w);
        Vector2 At(Vector2I c) => Unwarp(new Vector2(c.X + 0.5f, c.Y + 0.5f));
        // 河格之間的相連（右、下直接相連；斜角只在兩邊都不是河時才連，同原本）。
        var links = new Dictionary<Vector2I, List<Vector2I>>();
        void Link(Vector2I a, Vector2I b)
        {
            (links.TryGetValue(a, out var la) ? la : links[a] = new List<Vector2I>()).Add(b);
            (links.TryGetValue(b, out var lb) ? lb : links[b] = new List<Vector2I>()).Add(a);
        }
        foreach (var cell in world.Cells.Where(DemoProvinceMap.IsRiver))
        {
            var (c, r) = (cell.Column, cell.Row);
            var here = new Vector2I(c, r);
            if (!links.ContainsKey(here))
                links[here] = new List<Vector2I>();
            if (River(c + 1, r))
                Link(here, new Vector2I(c + 1, r));
            if (River(c, r + 1))
                Link(here, new Vector2I(c, r + 1));
            if (River(c + 1, r + 1) && !River(c + 1, r) && !River(c, r + 1))
                Link(here, new Vector2I(c + 1, r + 1));
            if (River(c - 1, r + 1) && !River(c - 1, r) && !River(c, r + 1))
                Link(here, new Vector2I(c - 1, r + 1));
        }
        if (links.Count == 0)
            return;
        // 河口：靠海的河格往海那邊伸出去；沿河道算離河口幾格（越近越寬）。
        var mouths = new Dictionary<Vector2I, Vector2>();
        foreach (var cell in links.Keys)
            foreach (var d in new[] { new Vector2I(1, 0), new Vector2I(-1, 0), new Vector2I(0, 1), new Vector2I(0, -1) })
                if (Sea(cell.X + d.X, cell.Y + d.Y) && !mouths.ContainsKey(cell))
                    mouths[cell] = At(cell) + new Vector2(d.X, d.Y) * 0.7f;
        var steps = new Dictionary<Vector2I, int>();
        var queue = new Queue<Vector2I>();
        foreach (var m in mouths.Keys)
        {
            steps[m] = 0;
            queue.Enqueue(m);
        }
        while (queue.Count > 0)
        {
            var at = queue.Dequeue();
            foreach (var next in links[at].Where(n => !steps.ContainsKey(n)))
            {
                steps[next] = steps[at] + 1;
                queue.Enqueue(next);
            }
        }
        // 第 08-56 章：河格連成一大片時（沼澤、河網）會畫出一格一格的方圈——只留一棵樹（從河口往上游走到的最短路），
        // 再剪掉從分岔點長出來不到 3 格的短枝，留下主河道。
        var tree = new Dictionary<Vector2I, List<Vector2I>>();
        foreach (var cell in links.Keys)
            tree[cell] = new List<Vector2I>();
        var seen = new HashSet<Vector2I>();
        foreach (var root in mouths.Keys.OrderBy(k => k.Y).ThenBy(k => k.X).Concat(links.Keys.OrderBy(k => k.Y).ThenBy(k => k.X)))
        {
            if (!seen.Add(root))
                continue;
            var walk = new Queue<Vector2I>();
            walk.Enqueue(root);
            while (walk.Count > 0)
            {
                var at = walk.Dequeue();
                foreach (var next in links[at].OrderBy(n => Math.Abs(n.X - at.X) + Math.Abs(n.Y - at.Y)).ThenBy(n => n.Y).ThenBy(n => n.X))
                    if (seen.Add(next))
                    {
                        tree[at].Add(next);
                        tree[next].Add(at);
                        walk.Enqueue(next);
                    }
            }
        }
        for (var pass = 0; pass < 2; pass++)
            foreach (var leaf in tree.Keys.Where(k => tree[k].Count == 1 && !mouths.ContainsKey(k)).ToList())
            {
                var spur = new List<Vector2I> { leaf };
                var (prev, cur) = (leaf, tree[leaf][0]);
                while (tree[cur].Count == 2 && spur.Count < 3)
                {
                    spur.Add(cur);
                    var next = tree[cur][0] == prev ? tree[cur][1] : tree[cur][0];
                    (prev, cur) = (cur, next);
                }
                if (tree[cur].Count >= 3 && spur.Count < 3)
                    foreach (var dead in spur)
                    {
                        foreach (var n in tree[dead])
                            tree[n].Remove(dead);
                        tree[dead].Clear();
                    }
            }
        links = tree.Where(kv => kv.Value.Count > 0 || mouths.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        float WidthOf(Vector2I cell) => Mathf.Lerp(1f, TempRiverNarrow, Mathf.Clamp(steps.GetValueOrDefault(cell, (int)TempRiverFullSteps) / TempRiverFullSteps, 0f, 1f));
        // 串成河段：從分岔點、端點出發走到下一個分岔點或端點；剩下的是環。
        var used = new HashSet<(Vector2I, Vector2I)>();
        bool Take(Vector2I a, Vector2I b) => used.Add((a, b)) && used.Add((b, a));
        var chains = new List<List<Vector2I>>();
        foreach (var start in links.Keys.Where(k => links[k].Count != 2).OrderBy(k => k.Y).ThenBy(k => k.X))
            foreach (var first in links[start])
            {
                if (!Take(start, first))
                    continue;
                var chain = new List<Vector2I> { start, first };
                var prev = start;
                var cur = first;
                while (links[cur].Count == 2)
                {
                    var next = links[cur][0] == prev ? links[cur][1] : links[cur][0];
                    if (!Take(cur, next))
                        break;
                    chain.Add(next);
                    (prev, cur) = (cur, next);
                }
                chains.Add(chain);
            }
        foreach (var start in links.Keys.OrderBy(k => k.Y).ThenBy(k => k.X))
            foreach (var first in links[start])
            {
                if (!Take(start, first))
                    continue;
                var chain = new List<Vector2I> { start, first };
                var (prev, cur) = (start, first);
                while (true)
                {
                    Vector2I? next = null;
                    foreach (var n in links[cur])
                        if (n != prev && !used.Contains((cur, n)))
                        {
                            next = n;
                            break;
                        }
                    if (next is not { } step || !Take(cur, step))
                        break;
                    chain.Add(step);
                    (prev, cur) = (cur, step);
                }
                chains.Add(chain);
            }
        // 單獨一格的河（沒有相連）：只畫河口那一小段。
        foreach (var lone in links.Keys.Where(k => links[k].Count == 0 && mouths.ContainsKey(k)))
            chains.Add(new List<Vector2I> { lone });
        var seed = 0;
        foreach (var chain in chains)
        {
            seed++;
            var points = chain.Select(At).ToList();
            var widths = chain.Select(WidthOf).ToList();
            if (mouths.TryGetValue(chain[^1], out var tail))
            {
                points.Add(tail);
                widths.Add(widths[^1] * 1.15f);
            }
            if (mouths.TryGetValue(chain[0], out var head))
            {
                points.Insert(0, head);
                widths.Insert(0, widths[0] * 1.15f);
            }
            if (points.Count < 2)
                continue;
            // 角切圓（端點不動）。
            for (var k = 0; k < TempRiverSmooth; k++)
            {
                var p2 = new List<Vector2> { points[0] };
                var w2 = new List<float> { widths[0] };
                for (var i = 0; i + 1 < points.Count; i++)
                {
                    p2.Add(points[i].Lerp(points[i + 1], 0.25f));
                    p2.Add(points[i].Lerp(points[i + 1], 0.75f));
                    w2.Add(Mathf.Lerp(widths[i], widths[i + 1], 0.25f));
                    w2.Add(Mathf.Lerp(widths[i], widths[i + 1], 0.75f));
                }
                p2.Add(points[^1]);
                w2.Add(widths[^1]);
                (points, widths) = (p2, w2);
            }
            // 蜿蜒：沿河道走過的長度取雜訊，往法線方向偏一點（兩端不動）。
            var walked = 0f;
            var bent = new Vector2[points.Count];
            for (var i = 0; i < points.Count; i++)
            {
                if (i > 0)
                    walked += points[i].DistanceTo(points[i - 1]);
                var dir = (points[Math.Min(i + 1, points.Count - 1)] - points[Math.Max(i - 1, 0)]).Normalized();
                var normal = new Vector2(-dir.Y, dir.X);
                var fade = Mathf.Min(1f, Mathf.Min(i, points.Count - 1 - i) / 6f);
                bent[i] = points[i] + normal * RiverNoise(walked * 1.3f, seed) * 2f * TempRiverMeander * fade;
            }
            _rivers.Add((bent, widths.ToArray()));
        }
    }

    /// <summary>
    /// 第 08-56 章：河流三層——深色河岸、藍色河水、拉近時河心一道亮光；寬度照河段每點的比例分段畫（同寬的一段用一條折線）。
    /// </summary>
    private void DrawRivers(Vector2 origin, float cellPx)
    {
        EnsureRivers();
        if (_rivers is null || _rivers.Count == 0)
            return;
        var full = Mathf.Clamp(cellPx * 0.16f, 1.6f, 15f);
        var bank = new Color(0.20f, 0.27f, 0.25f, 0.85f);
        var water = new Color(0.33f, 0.56f, 0.70f, 1f);
        var shine = new Color(0.66f, 0.84f, 0.92f, 0.75f);
        var close = Mathf.Clamp((cellPx - TempMidCellPx) / (TempCloseCellPx - TempMidCellPx), 0f, 1f);
        var view = new Rect2(Vector2.Zero, Size).Grow(cellPx * 2f);
        foreach (var (points, widths) in _rivers)
        {
            var screen = new Vector2[points.Length];
            var any = false;
            for (var i = 0; i < points.Length; i++)
            {
                screen[i] = origin + points[i] * cellPx;
                any |= view.HasPoint(screen[i]);
            }
            if (!any)
                continue;
            // 依寬度分段（量化成 6 階），每段一條折線；相鄰兩段共用一點，接縫看不出來。
            var startAt = 0;
            for (var i = 1; i <= screen.Length; i++)
            {
                var band = (int)MathF.Round(widths[startAt] * 6f);
                if (i < screen.Length && (int)MathF.Round(widths[i] * 6f) == band && i - startAt < 64)
                    continue;
                var end = Math.Min(i, screen.Length - 1);
                if (end > startAt)
                {
                    var run = screen[startAt..(end + 1)];
                    var w = full * band / 6f;
                    DrawPolyline(run, bank, w + Mathf.Max(1.2f, w * 0.45f), true);
                    DrawPolyline(run, water, w, true);
                    if (close > 0.01f && w > 3f)
                        DrawPolyline(run, new Color(shine, shine.A * close), Mathf.Max(1f, w * 0.28f), true);
                }
                startAt = end;
            }
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
            // 第 08-41 章：紙地圖的墨色字＋淡淡的紙色暈邊（CK3 遠看的地名），不再用勢力色的亮邊。
            var ink = new Color(0.17f, 0.11f, 0.08f, 0.88f * fade);
            var glow = new Color(new Color(0.94f, 0.89f, 0.76f).Lerp(fill, 0.25f), 0.42f * fade);
            var outline = Math.Max(2, size / 12);
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

    /// <summary>
    /// 第 08-42 章：航線——沿海路畫的虛線（暢通＝淡色、慢慢流動；封鎖＝暗紅，在第一格被怪物佔的海畫一個叉）。
    /// 路徑用兩次切角（Chaikin）磨圓，不是一格一格的折線。
    /// </summary>
    private void DrawSeaLanes(Vector2 origin, float cellPx)
    {
        if (SeaLanes.Count == 0 || _mode is not (Mode.Realm or Mode.Terrain) || Sites is null)
            return;
        var view = new Rect2(Vector2.Zero, Size).Grow(60f);
        var width = Mathf.Clamp(cellPx * 0.045f, 1.2f, 3f);
        var dash = Mathf.Clamp(cellPx * 0.22f, 4f, 14f);
        var flow = (Time.GetTicksMsec() / 1400f) % 1f;
        var open = new Color(0.96f, 0.94f, 0.86f, 0.85f);
        var closed = new Color(0.78f, 0.16f, 0.12f, 0.9f);
        var shadow = new Color(0.05f, 0.08f, 0.12f, 0.45f);
        var byId = Sites.Settlements.ToDictionary(s => s.Id);
        foreach (var lane in SeaLanes)
        {
            if (!byId.TryGetValue(lane.From, out var a) || !byId.TryGetValue(lane.To, out var b) || lane.Path.Count == 0)
                continue;
            var points = new List<Vector2> { PoliticalCenter(origin, cellPx, a.Column, a.Row) };
            points.AddRange(lane.Path.Select(c => PoliticalCenter(origin, cellPx, c.Column, c.Row)));
            points.Add(PoliticalCenter(origin, cellPx, b.Column, b.Row));
            var box = new Rect2(points[0], Vector2.Zero);
            foreach (var p in points)
                box = box.Expand(p);
            if (!view.Intersects(box))
                continue;
            for (var pass = 0; pass < 2; pass++)
            {
                var smooth = new List<Vector2> { points[0] };
                for (var i = 0; i + 1 < points.Count; i++)
                {
                    smooth.Add(points[i].Lerp(points[i + 1], 0.25f));
                    smooth.Add(points[i].Lerp(points[i + 1], 0.75f));
                }
                smooth.Add(points[^1]);
                points = smooth;
            }
            var color = lane.Blocked ? closed : open;
            // 兩端留一點空（不壓到港口的徽章），沿路依長度畫虛線。
            var total = 0f;
            for (var i = 0; i + 1 < points.Count; i++)
                total += points[i].DistanceTo(points[i + 1]);
            var skip = Mathf.Min(total * 0.2f, cellPx * 0.45f);
            var phase = lane.Blocked ? 0f : flow * dash * 2f;
            var walked = 0f;
            for (var i = 0; i + 1 < points.Count; i++)
            {
                var p0 = points[i];
                var p1 = points[i + 1];
                var len = p0.DistanceTo(p1);
                if (len < 0.01f)
                    continue;
                // 這一小段落在第幾個虛線週期：只畫「實」的那一半。
                for (var t = 0f; t < len; )
                {
                    var at = walked + t;
                    var cycle = (at - phase) % (dash * 2f);
                    if (cycle < 0f)
                        cycle += dash * 2f;
                    var step = cycle < dash ? Mathf.Min(dash - cycle, len - t) : Mathf.Min(dash * 2f - cycle, len - t);
                    if (cycle < dash && at > skip && at < total - skip)
                    {
                        var q0 = p0.Lerp(p1, t / len);
                        var q1 = p0.Lerp(p1, (t + step) / len);
                        DrawLine(q0 + new Vector2(0f, 1.2f), q1 + new Vector2(0f, 1.2f), shadow, width + 1f, true);
                        DrawLine(q0, q1, color, width, true);
                    }
                    t += Mathf.Max(step, 0.5f);
                }
                walked += len;
            }
            if (lane.Blocked && lane.BlockedAt >= 0 && lane.BlockedAt < lane.Path.Count)
            {
                var c = PoliticalCenter(origin, cellPx, lane.Path[lane.BlockedAt].Column, lane.Path[lane.BlockedAt].Row);
                var r = Mathf.Clamp(cellPx * 0.22f, 4f, 10f);
                DrawCircle(c, r * 1.25f, new Color(0.10f, 0.02f, 0.02f, 0.75f));
                DrawLine(c - new Vector2(r, r) * 0.7f, c + new Vector2(r, r) * 0.7f, closed.Lightened(0.25f), Mathf.Max(1.5f, width * 1.2f), true);
                DrawLine(c + new Vector2(-r, r) * 0.7f, c + new Vector2(r, -r) * 0.7f, closed.Lightened(0.25f), Mathf.Max(1.5f, width * 1.2f), true);
            }
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
            // 第 08-30 章：地下城標記換成第十二批（暗色圓座上的岩洞口）；缺素材時退回紅底骷髏。
            if (PixelParts.Texture("b12_map_lair") is { } lair)
                DrawTextureRect(lair, new Rect2(center - new Vector2(r, r) * 1.3f, new Vector2(r, r) * 2.6f), false);
            else
            {
                DrawCircle(center, r + 1.5f, TempLairRim);
                DrawCircle(center, r, TempLairFill);
                DrawSkull(center, r * 0.62f);
            }
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
            // 沉寂的地下城：同一個標記，灰暗半透明。
            if (PixelParts.Texture("b12_map_lair") is { } quiet)
                DrawTextureRect(quiet, new Rect2(center - new Vector2(r, r) * 1.3f, new Vector2(r, r) * 2.6f), false, new Color(0.62f, 0.6f, 0.58f, 0.8f));
            else
            {
                DrawCircle(center, r + 1f, new Color(0.55f, 0.52f, 0.48f));
                DrawCircle(center, r, new Color(0.22f, 0.21f, 0.20f));
                DrawSkull(center, r * 0.6f, new Color(0.7f, 0.68f, 0.64f));
            }
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
