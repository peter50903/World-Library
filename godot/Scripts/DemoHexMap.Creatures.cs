using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-18b 章第 4 節之 2：近景層的野生動物與怪物。數量來自模擬裡每格實際的獵物與狼（第 12-1 章 C1），但不是一隻一隻畫——
/// 試玩回饋 2026-11-06 之 2、3（動物太多太雜、希望有習性）：每 4×4 格一個棲地，依棲地裡的總數挑著畫，而且有習性：
/// <list type="bullet">
/// <item>鹿（丘陵上是山羊）**成群**：棲地獵物夠多時一群 2～3 頭，一起慢慢遊走、一起低頭吃草；少的時候只有一頭獨行。</item>
/// <item>狼**成群行動**：棲地裡狼夠多時一群 2～3 匹，頭狼在前、其他跟在後面，在巢穴格與隔壁的獵場格之間來回巡獵；只有一兩匹時是一匹**獨狼**在巢穴附近遊蕩。</item>
/// <item>地下城門口有一隻怪物來回踱步。</item>
/// </list>
/// 動物只站在空地上（陸地、而且沒有被樹或山頭擋住；試玩回饋之 4、5：不會蓋住樹頂、不會走進水裡）。只是表現，不進模擬、不存檔。
/// 區域層、戰略層不畫動物（地下城照舊是標記）。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>第 12-1 章 C1：每格的獵物與狼（依格的索引；由觀察者介面注入）。</summary>
    public IReadOnlyList<int> Prey { get; set; } = Array.Empty<int>();
    public IReadOnlyList<int> Wolves { get; set; } = Array.Empty<int>();

    /// <summary>【TEMP】棲地大小（格）、成群的門檻（棲地裡的獵物、狼總數）、格半徑至少幾像素才畫、遊走與巡獵一圈的秒數。</summary>
    private const int TempHabitat = 4;
    private const int TempHerdPrey = 16, TempLonePrey = 6, TempPackWolves = 3;
    private const float TempCreatureMinRadius = 18f;
    private const float TempRoamSeconds = 24f, TempPatrolSeconds = 30f;

    private enum Creature { Deer, Goat, Wolf }

    /// <summary>一群（或一隻）動物：種類、幾隻、在哪幾個空地方格之間活動。</summary>
    private sealed record Group(Creature Kind, int Count, Vector2I Den, Vector2I? Hunt, int Seed);

    private bool _creaturesDrawn;

    /// <summary>試玩回饋 2026-11-07 之 5：近景不畫野生動物（地下城怪物照畫）。</summary>
    public static bool ShowAnimals { get; set; }
    private List<Group>? _groups;
    private List<(Vector2I Tile, int Seed)>? _monsters;
    private (object?, object?, object?, object?, object?) _groupKey;

    /// <summary>依棲地裡的總數挑出要畫的動物群（狼、獵物、地下城、聚落地面或地圖改變時重算）。</summary>
    private void EnsureGroups()
    {
        if (World is null || _surface is not { Active: true })
            return; // 地表還沒畫（不知道哪裡是空地），下次再算
        // 試玩回饋 2026-11-07 之 5：不畫野生動物時不必依獵物與狼重算棲地。
        var key = ((object?)World, ShowAnimals ? (object?)Prey : null, ShowAnimals ? (object?)Wolves : null, (object?)_ground, (object?)Dungeons);
        if (_groups is not null && key.Equals(_groupKey))
            return;
        _groupKey = key;
        _groups = new List<Group>();
        _monsters = new List<(Vector2I, int)>();
        for (var by = 0; ShowAnimals && by < World.Rows; by += TempHabitat)
            for (var bx = 0; bx < World.Columns; bx += TempHabitat)
            {
                int prey = 0, wolves = 0, bestPrey = -1, bestWolf = -1, preyCell = -1, wolfCell = -1;
                for (var r = by; r < Math.Min(World.Rows, by + TempHabitat); r++)
                    for (var c = bx; c < Math.Min(World.Columns, bx + TempHabitat); c++)
                    {
                        var i = r * World.Columns + c;
                        var p = i < Prey.Count ? Prey[i] : 0;
                        var w = i < Wolves.Count ? Wolves[i] : 0;
                        prey += p;
                        wolves += w;
                        if (p > bestPrey) (bestPrey, preyCell) = (p, i);
                        if (w > bestWolf) (bestWolf, wolfCell) = (w, i);
                    }
                var seed = by * 1000 + bx;
                if (!ShowAnimals)
                    continue;
                if (prey >= TempLonePrey && bestPrey > 0 && OpenTileIn(preyCell, seed) is { } pasture)
                {
                    var goat = World.Cells[preyCell].Terrain == DemoTerrain.Hills;
                    var herd = prey >= TempHerdPrey ? (prey >= TempHerdPrey * 2 ? 3 : 2) : 1; // 成群或獨行
                    _groups.Add(new Group(goat ? Creature.Goat : Creature.Deer, herd, pasture, null, seed));
                }
                if (wolves > 0 && bestWolf > 0 && OpenTileIn(wolfCell, seed + 7) is { } den)
                {
                    var pack = wolves >= TempPackWolves ? Math.Min(3, wolves) : 1; // 狼群或獨狼
                    _groups.Add(new Group(Creature.Wolf, pack, den, pack > 1 ? HuntingGround(wolfCell, den, seed) : null, seed + 7));
                }
            }
        foreach (var d in Dungeons)
            if (World.Contains(d.Column, d.Row) && OpenTileIn(d.Row * World.Columns + d.Column, d.Column * 31 + d.Row) is { } gate)
                _monsters.Add((gate, d.Column * 31 + d.Row));
    }

    /// <summary>一格裡的某個空地方格（依固定雜湊挑起點，找不到就回傳 null）。</summary>
    private Vector2I? OpenTileIn(int cell, int seed)
    {
        var n = TerrainSurface.TilesPerCell;
        var x0 = cell % World!.Columns * n;
        var y0 = cell / World.Columns * n;
        var start = (int)(LayoutHash(seed, cell, 111) * n * n);
        for (var k = 0; k < n * n; k++)
        {
            var t = (start + k * 7) % (n * n);
            var tile = new Vector2I(x0 + t % n, y0 + t / n);
            if (_surface!.OpenTile(tile.X, tile.Y))
                return tile;
        }
        return null;
    }

    /// <summary>狼群的獵場：巢穴格上下左右的陸地格裡獵物最多的一格，兩地之間一路都是空地才算。</summary>
    private Vector2I? HuntingGround(int den, Vector2I from, int seed)
    {
        var column = den % World!.Columns;
        var row = den / World.Columns;
        var best = (Prey: -1, Tile: (Vector2I?)null);
        foreach (var (dq, dr, _) in DemoRouteNetwork.Orthogonal)
        {
            if (!World.Contains(column + dq, row + dr) || World.GetCell(column + dq, row + dr).Terrain == DemoTerrain.Water)
                continue;
            var i = (row + dr) * World.Columns + column + dq;
            var prey = i < Prey.Count ? Prey[i] : 0;
            if (prey <= best.Prey || OpenTileIn(i, seed + 13) is not { } tile || !ClearPath(from, tile))
                continue;
            best = (prey, tile);
        }
        return best.Tile;
    }

    /// <summary>兩個方格之間的直線一路都是空地（不穿過水、不從樹上走過）。</summary>
    private bool ClearPath(Vector2I a, Vector2I b)
    {
        var steps = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));
        for (var k = 0; k <= steps; k++)
        {
            var t = steps == 0 ? 0f : k / (float)steps;
            if (!_surface!.OpenTile(Mathf.RoundToInt(Mathf.Lerp(a.X, b.X, t)), Mathf.RoundToInt(Mathf.Lerp(a.Y, b.Y, t))))
                return false;
        }
        return true;
    }

    /// <summary>方格裡站的位置（畫面座標，腳底）。</summary>
    private static Vector2 TileFeet(Vector2 tile, Vector2 start, float radius)
        => TerrainSurface.TexelToScreen(new Vector2((tile.X + 0.5f) * TerrainSurface.TempTile, (tile.Y + 0.85f) * TerrainSurface.TempTile), start, radius);

    private void DrawCreatures(Vector2 start, float radius)
    {
        _creaturesDrawn = false;
        if (World is null || radius < TempCreatureMinRadius)
            return;
        EnsureGroups();
        if (_groups is null)
            return;
        var seconds = Time.GetTicksMsec() / 1000f;
        var items = new List<(float Y, Action Draw)>();
        // 試玩回饋 2026-11-07 之 5：野生動物只是在畫面中遊蕩、沒有實質作用，先不畫（模擬裡的獵物與狼照舊）。
        foreach (var group in ShowAnimals ? _groups : new List<Group>())
        {
            var home = TileFeet(group.Den, start, radius);
            if (!OnScreen(home, radius * 3f))
                continue;
            _creaturesDrawn = true;
            var h = LayoutHash(group.Seed, 0, 120);
            // 群體的中心：狼群在巢穴與獵場之間巡獵（中途停一下）；其他在棲地的空地附近慢慢遊走。
            Vector2 center;
            bool moving;
            float facing;
            if (group.Hunt is { } hunt)
            {
                var phase = Mathf.PosMod(seconds / TempPatrolSeconds + h, 1f);
                var target = TileFeet(hunt, start, radius);
                (center, moving, facing) = phase switch
                {
                    < 0.3f => (home.Lerp(target, phase / 0.3f), true, Mathf.Sign(target.X - home.X + 0.01f)),
                    < 0.5f => (target, false, Mathf.Sign(target.X - home.X + 0.01f)),
                    < 0.8f => (target.Lerp(home, (phase - 0.5f) / 0.3f), true, Mathf.Sign(home.X - target.X + 0.01f)),
                    _ => (home, false, Mathf.Sign(home.X - target.X + 0.01f))
                };
            }
            else
            {
                var speed = group.Kind == Creature.Wolf ? 1.4f : 0.7f;
                var t = seconds * speed / TempRoamSeconds * Mathf.Tau + h * Mathf.Tau;
                moving = Mathf.PosMod(t / Mathf.Tau, 1f) < 0.65f; // 三成多的時間停下來吃草、張望
                var tt = moving ? t : Mathf.Floor(t / Mathf.Tau) * Mathf.Tau + 0.65f * Mathf.Tau;
                // 只在巢穴方格附近半個方格內走動（那裡一定是空地）。
                center = home + new Vector2(Mathf.Sin(tt), Mathf.Sin(tt * 2f) * 0.4f) * radius * 0.18f;
                facing = Mathf.Cos(tt) >= 0f ? 1f : -1f;
            }
            if (group.Kind == Creature.Wolf && PixelParts.Texture("ani_den") is { } den)
            {
                var denAt = home + new Vector2(radius * 0.12f, -radius * 0.04f);
                items.Add((denAt.Y - 0.5f, () => DrawArt(den, denAt, radius))); // 第 08-18d 章：狼穴
            }
            for (var k = 0; k < group.Count; k++)
            {
                // 成員：狼群排成一列跟在頭狼後面；鹿群、山羊群散在中心旁邊。
                var offset = group.Kind == Creature.Wolf
                    ? new Vector2(-facing * k * radius * 0.22f, (k % 2 == 0 ? 1f : -1f) * k * radius * 0.05f)
                    : new Vector2((LayoutHash(group.Seed, k, 121) - 0.5f) * radius * 0.35f, (LayoutHash(group.Seed, k, 122) - 0.5f) * radius * 0.18f);
                var at = center + offset;
                var kind = group.Kind;
                var face = facing;
                var walk = moving;
                var lag = k * 0.37f;
                var member = k;
                var size = group.Count;
                items.Add((at.Y, () =>
                {
                    if (kind == Creature.Wolf)
                        DrawWolf(at, radius, face, walk, seconds + lag, member, size);
                    else
                        DrawDeer(at, radius, face, walk, seconds + lag, kind == Creature.Goat, member);
                }));
            }
        }
        foreach (var (tile, seed) in _monsters!)
        {
            var gate = TileFeet(tile, start, radius);
            if (!OnScreen(gate, radius * 3f))
                continue;
            _creaturesDrawn = true;
            var pace = Mathf.Sin(seconds * 0.8f + seed);
            var at = gate + new Vector2(pace * radius * 0.15f, radius * 0.06f);
            var facing = Mathf.Cos(seconds * 0.8f + seed) >= 0f ? 1f : -1f;
            // 第 08-18d 章：地下城入口（洞窟、古代石門、墓室三種）畫在怪物後面。
            if (PixelParts.Texture(DungeonArt(seed)) is { } entrance)
                items.Add((gate.Y - 0.5f, () => DrawArt(entrance, gate, radius)));
            var kindSeed = seed;
            // 第 12-2 章：怪物數每 4 隻多畫一隻（最多 3 隻）；沉寂的地下城門口沒有怪物。
            var shown = MonstersShownAt(tile);
            for (var k = 0; k < shown; k++)
            {
                var spot = at + new Vector2((k - (shown - 1) / 2f) * radius * 0.28f, (k % 2) * radius * 0.08f);
                var lag = k * 0.9f;
                items.Add((spot.Y, () => DrawMonster(spot, radius, facing, seconds + lag, kindSeed)));
            }
        }
        foreach (var (_, draw) in items.OrderBy(x => x.Y))
            draw();
    }

    /// <summary>一頭鹿（丘陵上是山羊）：約 10×9 圖像素，走路時腿交替，停下時低頭吃草。</summary>
    private void DrawDeer(Vector2 feet, float radius, float facing, bool moving, float seconds, bool goat, int member = 0)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        var coat = goat ? new Color(0.86f, 0.84f, 0.78f) : new Color(0.62f, 0.42f, 0.24f);
        Shadow(feet, 5f * p);
        {
            // 第 08-18d 章：第五、六批動物素材——鹿群是公鹿、母鹿、小鹿；停下時公鹿與山羊低頭吃草。
            var eating = !moving && Mathf.PosMod(seconds * 0.5f, 1f) < 0.7f;
            var name = goat ? (eating ? "ani_goat_graze" : "ani_goat_walk")
                : member == 0 ? (eating ? "ani_deer_graze" : "ani_deer_walk") : member == 1 ? "ani_doe_walk" : "ani_fawn_walk";
            var frame = moving ? (int)(seconds * 6f) % 4 : eating && (goat || member == 0) ? (int)(seconds * 2f) % 4 : 0;
            if (DrawArt(name, feet, radius, 4, frame, facing < 0f))
                return;
        }
        var step = moving && (int)(seconds * 5f) % 2 == 0 ? p : 0f;
        DrawRect(new Rect2(feet.X - 4f * p, feet.Y - 4f * p + step, p, 4f * p - step), coat.Darkened(0.3f));
        DrawRect(new Rect2(feet.X + 3f * p, feet.Y - 4f * p + (p - step), p, 4f * p - (p - step)), coat.Darkened(0.3f));
        DrawRect(new Rect2(feet.X - 5f * p, feet.Y - 7f * p, 10f * p, 3f * p), coat);
        DrawRect(new Rect2(feet.X - 4f * p, feet.Y - 5f * p, 8f * p, p), goat ? coat.Darkened(0.1f) : new Color(0.90f, 0.82f, 0.66f)); // 肚子
        var headX = feet.X + facing * 5f * p - (facing < 0 ? 3f * p : 0f);
        var grazing = !moving && Mathf.PosMod(seconds * 0.5f, 1f) < 0.7f;
        var headY = grazing ? feet.Y - 5f * p : feet.Y - 10f * p;
        DrawRect(new Rect2(headX, headY, 3f * p, 3f * p), coat);
        DrawRect(new Rect2(headX + (facing > 0 ? 2f : 0f) * p, headY + p, p, p), Ink);
        if (!grazing)
        {
            var horn = goat ? coat.Darkened(0.4f) : new Color(0.80f, 0.72f, 0.56f);
            DrawRect(new Rect2(headX + p, headY - 2f * p, p, 2f * p), horn); // 鹿角／山羊角
            DrawRect(new Rect2(headX + (facing > 0 ? 2f : -1f) * p, headY - 3f * p, p, p), horn);
        }
    }

    /// <summary>一匹狼：灰色、尾巴、豎耳，約 11×7 圖像素。</summary>
    private void DrawWolf(Vector2 feet, float radius, float facing, bool moving, float seconds, int member = 0, int pack = 1)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        var fur = new Color(0.48f, 0.48f, 0.50f);
        Shadow(feet, 5.5f * p);
        {
            // 第 08-18d 章：狼群最前面是頭狼；停下時狼群趴著休息，獨狼不時仰頭嚎叫。
            var name = moving ? (member == 0 && pack > 1 ? "ani_wolf_alpha_walk" : "ani_wolf_walk")
                : pack == 1 && Mathf.PosMod(seconds * 0.15f, 1f) < 0.4f ? "ani_wolf_howl" : "ani_wolf_rest";
            var frame = moving ? (int)(seconds * 8f) % 4 : (int)(seconds * 2f) % 4;
            if (DrawArt(name, feet, radius, 4, frame, facing < 0f))
                return;
        }
        var step = moving && (int)(seconds * 7f) % 2 == 0 ? p : 0f;
        DrawRect(new Rect2(feet.X - 4f * p, feet.Y - 3f * p + step, p, 3f * p - step), fur.Darkened(0.35f));
        DrawRect(new Rect2(feet.X + 3f * p, feet.Y - 3f * p + (p - step), p, 3f * p - (p - step)), fur.Darkened(0.35f));
        DrawRect(new Rect2(feet.X - 5f * p, feet.Y - 6f * p, 10f * p, 3f * p), fur);
        DrawRect(new Rect2(feet.X - 5f * p, feet.Y - 6f * p, 10f * p, p), fur.Darkened(0.2f)); // 背上深色
        var tailX = facing > 0 ? feet.X - 8f * p : feet.X + 5f * p;
        DrawRect(new Rect2(tailX, feet.Y - 6f * p, 3f * p, p), fur.Darkened(0.15f));
        var headX = facing > 0 ? feet.X + 4f * p : feet.X - 8f * p;
        DrawRect(new Rect2(headX, feet.Y - 8f * p, 4f * p, 3f * p), fur);
        DrawRect(new Rect2(headX + (facing > 0 ? 3f : -1f) * p, feet.Y - 7f * p, 2f * p, 2f * p), fur.Lightened(0.15f)); // 口鼻
        DrawRect(new Rect2(headX + p, feet.Y - 10f * p, p, 2f * p), fur.Darkened(0.2f)); // 耳朵
        DrawRect(new Rect2(headX + (facing > 0 ? 2f : 1f) * p, feet.Y - 7f * p, p, p), new Color(0.95f, 0.80f, 0.25f)); // 眼睛
    }

    /// <summary>地下城的怪物：比小人大一倍的深色身影、角、發光的眼睛，在門口來回踱步。</summary>
    private void DrawMonster(Vector2 feet, float radius, float facing, float seconds, int seed = 0)
    {
        var p = radius / TerrainSurface.TempTexelsPerRadius;
        var skin = new Color(0.30f, 0.22f, 0.34f);
        Shadow(feet, 8f * p);
        // 第 08-18d 章：第六批怪物素材（哥布林、骷髏兵、巨魔、暗影，依地下城固定挑一種）。
        if (DrawArt(MonsterArt(seed), feet, radius, 4, (int)(seconds * 5f) % 4, facing < 0f))
            return;
        var bob = Mathf.Sin(seconds * 3f) > 0f ? p : 0f;
        DrawRect(new Rect2(feet.X - 5f * p, feet.Y - 6f * p, 3f * p, 6f * p), skin.Darkened(0.3f));
        DrawRect(new Rect2(feet.X + 2f * p, feet.Y - 6f * p, 3f * p, 6f * p), skin.Darkened(0.3f));
        var body = new Rect2(feet.X - 7f * p, feet.Y - 20f * p + bob, 14f * p, 14f * p);
        DrawRect(body, skin);
        DrawRect(new Rect2(body.Position.X + (facing > 0 ? 0f : 10f) * p, body.Position.Y, 4f * p, body.Size.Y), skin.Darkened(0.25f));
        DrawRect(new Rect2(feet.X - 9f * p, feet.Y - 16f * p + bob, 2f * p, 8f * p), skin.Darkened(0.1f)); // 手臂
        DrawRect(new Rect2(feet.X + 7f * p, feet.Y - 16f * p + bob, 2f * p, 8f * p), skin.Darkened(0.1f));
        DrawRect(new Rect2(feet.X - 6f * p, feet.Y - 24f * p + bob, 2f * p, 4f * p), new Color(0.85f, 0.80f, 0.70f)); // 角
        DrawRect(new Rect2(feet.X + 4f * p, feet.Y - 24f * p + bob, 2f * p, 4f * p), new Color(0.85f, 0.80f, 0.70f));
        var glow = new Color(1f, 0.35f, 0.20f, 0.7f + 0.3f * Mathf.Sin(seconds * 4f));
        DrawRect(new Rect2(feet.X + (facing > 0 ? 0f : -4f) * p, feet.Y - 17f * p + bob, 2f * p, 2f * p), glow);
        DrawRect(new Rect2(feet.X + (facing > 0 ? 3f : -1f) * p, feet.Y - 17f * p + bob, 2f * p, 2f * p), glow);
        DrawRect(body, Ink, false, Math.Max(1f, p));
    }

    private void Shadow(Vector2 feet, float width)
    {
        DrawSetTransform(feet + new Vector2(width * 0.15f, 0f), 0f, new Vector2(1f, 0.35f));
        DrawCircle(Vector2.Zero, width, new Color(0f, 0f, 0f, 0.28f));
        DrawSetTransform(Vector2.Zero, 0f, Vector2.One);
    }

    private static string DungeonArt(int seed) => (Math.Abs(seed) % 3) switch { 0 => "dun_cave", 1 => "dun_ruin_gate", _ => "dun_crypt" };

    private static string MonsterArt(int seed) => (Math.Abs(seed / 3) % 4) switch
    {
        0 => "mon_goblin_walk",
        1 => "mon_skeleton_walk",
        2 => "mon_troll_walk",
        _ => "mon_shadow_idle"
    };

    /// <summary>這個地下城在近景有沒有畫入口（有就不再畫地圖標記）。</summary>
    private bool DungeonDrawn(int column, int row, float radius)
        => radius >= TempCreatureMinRadius && _monsters is not null && _monsters.Any(m => m.Seed == column * 31 + row)
            && PixelParts.Texture(DungeonArt(column * 31 + row)) is not null;
}
