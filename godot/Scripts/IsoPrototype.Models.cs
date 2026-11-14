using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-20 章（使用者 2026-11-14：精細度要跟示意圖差不多）：原型的低多邊形模型——
/// 木構架房子（石地基、樑柱、門窗、一排排的瓦片或茅草、屋脊、煙囪）、尖木柵與走道、有平台與旗子的角樓、城門樓、
/// 十字石子主街與廣場、條紋篷布市集與貨物、輪輻水車與水花、磨坊與水槽、木橋、方田（田壟、圍籬、乾草堆）、
/// 分層針葉樹與圓冠闊葉樹、有稜角的岩石；地塊加淡淡的格線、土層剖面嵌石、海岸浪花。
/// 每個聚落、整片樹林各合成一個網格（零件很多也只算幾次繪圖）。
/// </summary>
public partial class IsoPrototypeView
{
    private static readonly Color Glass = new(0.98f, 0.82f, 0.48f);
    private static readonly Color Cobble = new(0.74f, 0.70f, 0.62f), Dirt = new(0.72f, 0.60f, 0.42f);
    private static readonly Color[] ConiferGreens = { new(0.18f, 0.40f, 0.22f), new(0.22f, 0.46f, 0.24f), new(0.16f, 0.36f, 0.20f) };
    private static readonly Color[] LeafGreens = { new(0.38f, 0.58f, 0.26f), new(0.44f, 0.62f, 0.28f), new(0.32f, 0.52f, 0.24f) };

    private static Transform3D T(Vector3 at, float yaw = 0f) => IsoMeshBuilder.At(at, yaw);

    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    /// <summary>
    /// 木構架房子：石地基、牆、四角柱與樑（兩層樓多一圈腰樑）、長邊的立柱、門、每層的窗、瓦片（或茅草）斜頂、屋脊、煙囪。
    /// 屋脊沿零件 Z 軸，門開在 +X 面。
    /// </summary>
    private void TimberHouse(IsoMeshBuilder b, int settlement, Vector3 at, float yaw, float s, Color wall, Color roof, int floors = 1,
        bool chimney = false, bool thatch = false, bool stone = false)
    {
        var t = T(at, yaw);
        float d = 0.20f * s, l = 0.28f * s, h = 0.12f * s * floors, foot = 0.03f * s, post = 0.016f * s;
        b.Box(t, V(0, foot / 2f, 0), V(d + 0.03f * s, foot, l + 0.03f * s), Stone.Darkened(0.1f));
        b.Box(t, V(0, foot + h / 2f, 0), V(d, h, l), wall);
        var frame = stone ? wall.Darkened(0.22f) : DarkWood;
        foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1f, 1f })
                b.Box(t, V(x * d / 2f, foot + h / 2f, z * l / 2f), V(post * 1.3f, h, post * 1.3f), frame);
        if (!stone)
        {
            var levels = floors == 2 ? new[] { foot + h / 2f, foot + h } : new[] { foot + h };
            foreach (var y in levels)
            {
                b.Box(t, V(d / 2f, y, 0), V(post, post, l), frame);
                b.Box(t, V(-d / 2f, y, 0), V(post, post, l), frame);
                b.Box(t, V(0, y, l / 2f), V(d, post, post), frame);
                b.Box(t, V(0, y, -l / 2f), V(d, post, post), frame);
            }
            foreach (var x in new[] { -1f, 1f })
                foreach (var z in new[] { -l / 6f, l / 6f })
                    b.Box(t, V(x * (d / 2f + 0.002f), foot + h / 2f, z), V(post * 0.7f, h, post * 0.7f), frame);
        }
        b.Box(t, V(d / 2f + 0.005f, foot + 0.045f * s, 0), V(0.01f, 0.09f * s, 0.05f * s), DarkWood.Darkened(0.25f));
        for (var f = 0; f < floors; f++)
        {
            var y = foot + h / floors * (f + 0.58f);
            foreach (var x in new[] { -1f, 1f })
                foreach (var z in new[] { -l / 3f, l / 3f })
                    b.Box(t, V(x * (d / 2f + 0.005f), y, z), V(0.01f, 0.03f * s, 0.032f * s), Glass);
            foreach (var z in new[] { -1f, 1f })
                b.Box(t, V(0, y, z * (l / 2f + 0.005f)), V(0.032f * s, 0.03f * s, 0.01f), Glass);
        }
        var rise = (thatch ? 0.17f : 0.13f) * s;
        var roofBase = foot + h;
        b.Prism(t, V(0, roofBase, 0), V(d * (thatch ? 1.5f : 1.38f), rise, l * 1.16f), roof, wall.Darkened(0.12f), thatch ? 3 : 5);
        b.Box(t, V(0, roofBase + rise, 0), V(0.024f * s, 0.02f * s, l * 1.18f), roof.Darkened(0.28f));
        if (chimney)
        {
            b.Box(t, V(d * 0.22f, roofBase + 0.08f * s, l * 0.28f), V(0.045f, 0.17f, 0.045f) * s, Stone);
            b.Box(t, V(d * 0.22f, roofBase + 0.165f * s, l * 0.28f), V(0.055f, 0.015f, 0.055f) * s, Stone.Darkened(0.2f));
        }
        var r = Math.Max(d, l) * 0.66f;
        _picks.Add((new Aabb(at - V(r, 0f, r), V(2f * r, roofBase + rise, 2f * r)), settlement));
    }

    /// <summary>木角樓：四柱、板牆、平台與欄杆、四角錐頂、勢力色的旗子。</summary>
    private void WatchTower(IsoMeshBuilder b, int settlement, Vector3 at, float height, Color flag)
    {
        var t = T(at);
        foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1f, 1f })
                b.Box(t, V(x * 0.08f, height / 2f, z * 0.08f), V(0.03f, height, 0.03f), DarkWood);
        b.Box(t, V(0, height * 0.3f, 0), V(0.17f, height * 0.6f, 0.17f), Wood);
        b.Box(t, V(0, height, 0), V(0.27f, 0.03f, 0.27f), Wood.Darkened(0.1f));
        foreach (var (x, z, w, dd) in new[] { (0f, 0.13f, 0.27f, 0.02f), (0f, -0.13f, 0.27f, 0.02f), (0.13f, 0f, 0.02f, 0.27f), (-0.13f, 0f, 0.02f, 0.27f) })
            b.Box(t, V(x, height + 0.04f, z), V(w, 0.05f, dd), Wood);
        foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1f, 1f })
                b.Box(t, V(x * 0.11f, height + 0.07f, z * 0.11f), V(0.02f, 0.1f, 0.02f), DarkWood);
        b.Frustum(t, V(0, height + 0.12f, 0), 0.22f, 0f, 0.18f, 4, Terracotta, 45f);
        b.Box(t, V(0, height + 0.38f, 0), V(0.012f, 0.16f, 0.012f), DarkWood);
        b.Box(t, V(0.045f, height + 0.42f, 0), V(0.08f, 0.05f, 0.008f), flag);
        _picks.Add((new Aabb(at - V(0.15f, 0f, 0.15f), V(0.3f, height + 0.45f, 0.3f)), settlement));
    }

    /// <summary>一段木柵：削尖的木樁（高低交錯）、內側的橫樑與走道；<paramref name="gap"/>＝城門要空出的位置。</summary>
    private void Palisade(IsoMeshBuilder b, int settlement, Vector3 from, Vector3 to, Vector3 inward, float? gap = null)
    {
        var length = from.DistanceTo(to);
        var n = (int)(length / 0.085f);
        for (var i = 0; i <= n; i++)
        {
            var p = from.Lerp(to, i / (float)n);
            if (gap is { } g && Math.Abs(p.X - g) < 0.2f && Math.Abs(from.Z - to.Z) < 0.01f)
                continue;
            var h = 0.24f + 0.04f * IsoProtoMap.Hash01(i, (int)(from.X * 10 + from.Z * 7), 5);
            var color = i % 3 == 0 ? DarkWood : Wood;
            b.Frustum(T(p), V(0, 0, 0), 0.03f, 0.03f, h, 6, color, i * 17f);
            b.Frustum(T(p), V(0, h, 0), 0.03f, 0f, 0.06f, 6, color.Lightened(0.08f), i * 17f);
        }
        var mid = (from + to) / 2f;
        var along = (to - from).Normalized();
        var yaw = Mathf.RadToDeg(Mathf.Atan2(along.X, along.Z));
        b.Box(T(mid + inward * 0.04f + V(0, 0.17f, 0), yaw), V(0.02f, 0.02f, length), DarkWood);
        b.Box(T(mid + inward * 0.09f + V(0, 0.15f, 0), yaw), V(0.07f, 0.015f, length - 0.1f), Wood.Lightened(0.08f));
        for (var k = 1; k < (int)(length / 0.5f); k++)
            b.Box(T(from.Lerp(to, k * 0.5f / length) + inward * 0.09f + V(0, 0.075f, 0)), V(0.02f, 0.15f, 0.02f), DarkWood);
        var box = new Aabb(new Vector3(Math.Min(from.X, to.X) - 0.06f, from.Y, Math.Min(from.Z, to.Z) - 0.06f),
            new Vector3(Math.Abs(to.X - from.X) + 0.12f, 0.32f, Math.Abs(to.Z - from.Z) + 0.12f));
        _picks.Add((box, settlement));
    }

    /// <summary>市集攤位：四柱、檯面、紅白條紋篷布、貨物（木箱、木桶、蔬果）。</summary>
    private void Stall(IsoMeshBuilder b, int settlement, Vector3 at, float yaw, Color stripe)
    {
        var t = T(at, yaw);
        foreach (var x in new[] { -1f, 1f })
            foreach (var z in new[] { -1f, 1f })
                b.Box(t, V(x * 0.09f, 0.075f, z * 0.11f), V(0.014f, 0.15f, 0.014f), DarkWood);
        b.Box(t, V(0.03f, 0.05f, 0), V(0.1f, 0.06f, 0.22f), Wood);
        b.Prism(t, V(0, 0.15f, 0), V(0.24f, 0.06f, 0.28f), stripe, stripe.Darkened(0.1f), 4, new Color(0.95f, 0.93f, 0.88f));
        b.Box(t, V(-0.03f, 0.1f, -0.06f), V(0.05f, 0.04f, 0.05f), Wood.Lightened(0.15f));
        b.Frustum(t, V(-0.06f, 0f, 0.07f), 0.025f, 0.025f, 0.06f, 7, DarkWood.Lightened(0.1f));
        b.Blob(t, V(0.03f, 0.095f, 0.05f), V(0.025f, 0.02f, 0.025f), new Color(0.85f, 0.35f, 0.2f), (int)(at.X * 31 + at.Z * 7));
        b.Blob(t, V(0.03f, 0.095f, -0.04f), V(0.025f, 0.02f, 0.025f), new Color(0.55f, 0.72f, 0.25f), (int)(at.X * 17 + at.Z * 3));
        _picks.Add((new Aabb(at - V(0.14f, 0f, 0.14f), V(0.28f, 0.22f, 0.28f)), settlement));
    }

    /// <summary>石井：八角石圈、兩根柱子、小斜頂。</summary>
    private static void Well(IsoMeshBuilder b, Vector3 at)
    {
        var t = T(at);
        b.Frustum(t, V(0, 0, 0), 0.07f, 0.07f, 0.06f, 8, Stone, 0f, new Color(0.2f, 0.35f, 0.5f));
        b.Box(t, V(0.06f, 0.1f, 0), V(0.015f, 0.14f, 0.015f), DarkWood);
        b.Box(t, V(-0.06f, 0.1f, 0), V(0.015f, 0.14f, 0.015f), DarkWood);
        b.Prism(t, V(0, 0.17f, 0), V(0.1f, 0.05f, 0.18f), Terracotta, null, 2);
    }

    private static void Haystack(IsoMeshBuilder b, Vector3 at)
    {
        b.Frustum(T(at), V(0, 0, 0), 0.075f, 0.07f, 0.045f, 8, new Color(0.86f, 0.70f, 0.32f));
        b.Frustum(T(at), V(0, 0.045f, 0), 0.075f, 0f, 0.09f, 8, new Color(0.90f, 0.75f, 0.36f));
    }

    private static void Conifer(IsoMeshBuilder b, Vector3 at, float s, int seed)
    {
        var t = T(at, seed * 23f);
        b.Frustum(t, V(0, 0, 0), 0.025f * s, 0.02f * s, 0.08f * s, 5, DarkWood);
        var green = ConiferGreens[Math.Abs(seed) % ConiferGreens.Length];
        b.Frustum(t, V(0, 0.06f * s, 0), 0.15f * s, 0f, 0.18f * s, 7, green);
        b.Frustum(t, V(0, 0.15f * s, 0), 0.12f * s, 0f, 0.16f * s, 7, green.Lightened(0.05f), 20f);
        b.Frustum(t, V(0, 0.23f * s, 0), 0.08f * s, 0f, 0.14f * s, 7, green.Lightened(0.1f), 40f);
    }

    private static void Broadleaf(IsoMeshBuilder b, Vector3 at, float s, int seed)
    {
        var t = T(at, seed * 41f);
        b.Frustum(t, V(0, 0, 0), 0.025f * s, 0.018f * s, 0.13f * s, 5, DarkWood);
        var green = LeafGreens[Math.Abs(seed) % LeafGreens.Length];
        b.Blob(t, V(0, 0.22f * s, 0), V(0.14f, 0.12f, 0.14f) * s, green, seed);
        b.Blob(t, V(0.07f * s, 0.17f * s, 0.05f * s), V(0.09f, 0.08f, 0.09f) * s, green.Darkened(0.05f), seed + 5);
        b.Blob(t, V(-0.06f * s, 0.18f * s, -0.04f * s), V(0.08f, 0.07f, 0.08f) * s, green.Lightened(0.05f), seed + 9);
    }

    private static void Rock(IsoMeshBuilder b, Vector3 at, float s, int seed)
        => b.Blob(T(at, seed * 13f), V(0, 0.04f * s, 0), V(0.11f, 0.08f, 0.09f) * s, new Color(0.60f, 0.59f, 0.56f), seed, 0.32f);

    /// <summary>樹林、零星的樹與灌木、丘陵與海岸的岩石（合成一個網格）。</summary>
    private void BuildTrees()
    {
        var b = new IsoMeshBuilder();
        var taken = Map.Settlements.SelectMany(s => s.Cells).ToHashSet();
        for (var c = 0; c < IsoProtoMap.Columns; c++)
            for (var r = 0; r < IsoProtoMap.Rows; r++)
            {
                var terrain = Map.TerrainAt(c, r);
                var h = Map.TopOf(c, r);
                float H(int k) => IsoProtoMap.Hash01(c, r, k);
                if (terrain == IsoTerrain.Forest)
                {
                    var count = 4 + (int)(H(5) * 3f);
                    for (var k = 0; k < count; k++)
                    {
                        var at = V(c + 0.14f + 0.72f * H(20 + k), h, r + 0.14f + 0.72f * H(40 + k));
                        var s = 0.8f + 0.5f * H(60 + k);
                        if (H(80 + k) < 0.65f)
                            Conifer(b, at, s, c * 31 + r * 7 + k);
                        else
                            Broadleaf(b, at, s, c * 31 + r * 7 + k);
                    }
                }
                else if (terrain == IsoTerrain.Grass && !taken.Contains(new Vector2I(c, r)) && Map.RealmAt(c, r) >= 0)
                {
                    // 草地上零星的樹與灌木（約三分之一的格）。
                    if (H(3) < 0.35f)
                        Broadleaf(b, V(c + 0.2f + 0.6f * H(21), h, r + 0.2f + 0.6f * H(22)), 0.8f + 0.4f * H(23), c * 13 + r);
                    if (H(4) < 0.5f)
                        b.Blob(T(V(c + 0.2f + 0.6f * H(24), h, r + 0.2f + 0.6f * H(25))), V(0, 0.04f, 0), V(0.06f, 0.05f, 0.06f), LeafGreens[(c + r) % 3].Darkened(0.08f), c * 7 + r * 3);
                }
                else if (terrain == IsoTerrain.Hill)
                {
                    for (var k = 0; k < 2 + (int)(H(6) * 2f); k++)
                        Rock(b, V(c + 0.15f + 0.7f * H(26 + k), h + 0.02f, r + 0.15f + 0.7f * H(30 + k)), 0.7f + 0.8f * H(34 + k), c * 11 + r * 5 + k);
                    if (H(8) < 0.5f)
                        Conifer(b, V(c + 0.25f + 0.5f * H(9), h + 0.04f, r + 0.25f + 0.5f * H(10)), 0.75f, c + r);
                }
                else if (terrain == IsoTerrain.Sand && (c == 1 || Map.TerrainAt(c - 1, r) == IsoTerrain.Sea) && H(11) < 0.7f)
                    Rock(b, V(c + 0.12f, h, r + 0.2f + 0.6f * H(12)), 0.8f + 0.6f * H(13), c * 3 + r * 19);
            }
        _trees.AddChild(b.Commit(VertexMaterial()));
    }

    /// <summary>
    /// 木柵城鎮：方形尖木柵（內側走道）、四角角樓、南面城門樓（兩座塔＋橋廊）、十字石子主街與中央廣場、
    /// 四個攤位與井、四個街區的房子（木構架、石屋、兩層樓；靠城門的鎮公所較大）。
    /// </summary>
    private void BuildTown(IsoSettlement town)
    {
        var b = new IsoMeshBuilder();
        var y = IsoProtoMap.LandTop;
        float x0 = 2.08f, x1 = 4.92f, z0 = 5.08f, z1 = 7.92f;
        var cx = (x0 + x1) / 2f;
        var cz = (z0 + z1) / 2f;
        // 主街與廣場：石子路面＋兩側路緣。
        b.Box(T(V(cx, y + 0.008f, cz)), V(x1 - x0, 0.016f, 0.24f), Cobble);
        b.Box(T(V(cx, y + 0.008f, cz + 0.25f)), V(0.24f, 0.016f, z1 - z0 + 0.5f), Cobble);
        foreach (var side in new[] { -0.13f, 0.13f })
        {
            b.Box(T(V(cx, y + 0.012f, cz + side)), V(x1 - x0, 0.02f, 0.02f), Cobble.Darkened(0.2f));
            b.Box(T(V(cx + side, y + 0.012f, cz + 0.25f)), V(0.02f, 0.02f, z1 - z0 + 0.5f), Cobble.Darkened(0.2f));
        }
        b.Box(T(V(cx, y + 0.01f, cz)), V(0.72f, 0.018f, 0.72f), Cobble.Lightened(0.06f));
        Well(b, V(cx, y + 0.01f, cz));
        // 木柵與城門。
        var gold = GoldColor;
        Palisade(b, town.Id, V(x0, y, z0), V(x1, y, z0), V(0, 0, 1));
        Palisade(b, town.Id, V(x0, y, z1), V(x1, y, z1), V(0, 0, -1), cx);
        Palisade(b, town.Id, V(x0, y, z0), V(x0, y, z1), V(1, 0, 0));
        Palisade(b, town.Id, V(x1, y, z0), V(x1, y, z1), V(-1, 0, 0));
        WatchTower(b, town.Id, V(x0, y, z0), 0.46f, gold);
        WatchTower(b, town.Id, V(x1, y, z0), 0.46f, gold);
        WatchTower(b, town.Id, V(x0, y, z1), 0.46f, gold);
        WatchTower(b, town.Id, V(x1, y, z1), 0.46f, gold);
        WatchTower(b, town.Id, V(cx - 0.26f, y, z1), 0.4f, gold);
        WatchTower(b, town.Id, V(cx + 0.26f, y, z1), 0.4f, gold);
        b.Box(T(V(cx, y + 0.34f, z1)), V(0.42f, 0.07f, 0.18f), Wood);
        b.Prism(T(V(cx, y + 0.375f, z1), 90f), V(0.2f, 0.08f, 0.46f), Terracotta, Wood.Darkened(0.1f), 3);
        foreach (var side in new[] { -1f, 1f })
            b.Box(T(V(cx + side * 0.1f, y + 0.08f, z1 + 0.06f), side * 25f), V(0.1f, 0.16f, 0.015f), DarkWood.Darkened(0.15f)); // 敞開的城門
        // 市集：廣場四角的攤位。
        var stalls = new[] { (-1f, -1f, 45f), (1f, -1f, -45f), (-1f, 1f, 135f), (1f, 1f, -135f) };
        for (var i = 0; i < stalls.Length; i++)
        {
            var (dx, dz, yaw) = stalls[i];
            Stall(b, town.Id, V(cx + dx * 0.28f, y + 0.018f, cz + dz * 0.28f), yaw, i % 2 == 0 ? new Color(0.82f, 0.20f, 0.18f) : new Color(0.20f, 0.42f, 0.78f));
        }
        // 房子：四個街區，每區一列面向主街、一列靠城牆；屋型輪替。
        var placed = 0;
        foreach (var (qx, qz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
            for (var i = 0; i < 4; i++)
            {
                var ox = (0.5f + (i % 2) * 0.5f) * qx;
                var oz = (0.46f + (i / 2) * 0.52f) * qz;
                var at = V(cx + ox, y, cz + oz);
                var yaw = qx > 0 ? 180f : 0f; // 門朝主街
                if (i >= 2)
                    yaw += 90f * qz;
                var hall = qx == 1 && qz == 1 && i == 0;
                var kind = (placed + 1) % 3;
                if (hall)
                    TimberHouse(b, town.Id, at, yaw, 1.45f, Plaster, new Color(0.62f, 0.24f, 0.18f), 2, chimney: true);
                else if (kind == 0)
                    TimberHouse(b, town.Id, at, yaw, 1.05f, Stone.Lightened(0.12f), new Color(0.55f, 0.30f, 0.22f), 1 + placed % 2, chimney: true, stone: true);
                else
                    TimberHouse(b, town.Id, at, yaw, 1f + 0.12f * IsoProtoMap.Hash01(placed, 1, 3), Plaster, Terracotta, 1 + (placed / 3) % 2, chimney: placed % 2 == 0);
                placed++;
            }
        // 城牆內角落的貨物與灌木。
        foreach (var (x, z) in new[] { (x0 + 0.25f, z0 + 0.25f), (x1 - 0.25f, z0 + 0.3f), (x0 + 0.3f, z1 - 0.25f) })
        {
            b.Box(T(V(x, y + 0.03f, z), 20f), V(0.06f, 0.06f, 0.06f), Wood.Lightened(0.1f));
            b.Frustum(T(V(x + 0.07f, y, z + 0.03f)), V(0, 0, 0), 0.028f, 0.028f, 0.07f, 7, DarkWood.Lightened(0.08f));
        }
        _detail.AddChild(b.Commit(VertexMaterial()));
    }

    /// <summary>
    /// 河邊農村：沿河岸的茅草屋、河上的水車（輪輻、葉片、水花）與兩層磨坊、水槽、木橋、
    /// 3×3 方田（田壟、木圍籬、乾草堆）、菜園與小路。
    /// </summary>
    private void BuildVillage(IsoSettlement village)
    {
        var b = new IsoMeshBuilder();
        var y = IsoProtoMap.LandTop;
        var spots = new[] { (8.5f, 6.4f, 90f), (8.6f, 7.6f, 90f), (9.5f, 6.55f, 0f), (9.55f, 7.7f, 180f), (10.45f, 6.35f, 0f), (10.5f, 7.55f, 180f), (9.15f, 8.55f, 90f), (10.35f, 8.6f, 0f) };
        for (var i = 0; i < spots.Length; i++)
        {
            var (x, z, yaw) = spots[i];
            TimberHouse(b, village.Id, V(x, y, z), yaw, 0.95f + 0.12f * IsoProtoMap.Hash01(i, 2, 4), new Color(0.82f, 0.72f, 0.54f), Thatch, 1, chimney: i % 3 == 0, thatch: true);
        }
        // 小路：河岸到方田、到木橋。
        b.Box(T(V(9.9f, y + 0.008f, 7.08f)), V(2.4f, 0.016f, 0.14f), Dirt);
        b.Box(T(V(8.3f, y + 0.008f, 7.5f)), V(0.6f, 0.016f, 0.12f), Dirt);
        // 菜園：兩小塊綠色田畦。
        foreach (var (x, z) in new[] { (9.9f, 8.2f), (10.75f, 8.1f) })
            for (var k = 0; k < 3; k++)
                b.Box(T(V(x, y + 0.015f, z - 0.08f + k * 0.08f)), V(0.3f, 0.03f, 0.05f), LeafGreens[k % 3]);
        Well(b, V(9.95f, y, 7.3f));
        // 兩層磨坊（石造一樓、木構二樓、紅瓦）＋往水車的木水槽。
        TimberHouse(b, village.Id, V(8.25f, y, 5.72f), 90f, 1.25f, new Color(0.78f, 0.68f, 0.52f), Terracotta, 2, chimney: false);
        b.Box(T(V(8.08f, y + 0.02f, 5.72f)), V(0.22f, 0.04f, 0.36f), Stone);
        b.Box(T(V(8.0f, IsoProtoMap.WaterLevel + 0.34f, 5.72f), 90f), V(0.07f, 0.04f, 0.2f), Wood);
        // 木橋：跨過第 7 列的河。
        b.Box(T(V(7.5f, IsoProtoMap.WaterLevel + 0.13f, 7.5f)), V(1.3f, 0.03f, 0.22f), Wood);
        foreach (var side in new[] { -0.1f, 0.1f })
        {
            b.Box(T(V(7.5f, IsoProtoMap.WaterLevel + 0.2f, 7.5f + side)), V(1.3f, 0.015f, 0.015f), DarkWood);
            for (var k = 0; k < 5; k++)
                b.Box(T(V(6.9f + k * 0.3f, IsoProtoMap.WaterLevel + 0.17f, 7.5f + side)), V(0.015f, 0.07f, 0.015f), DarkWood);
        }
        foreach (var x in new[] { 7.15f, 7.85f })
            b.Box(T(V(x, IsoProtoMap.WaterLevel + 0.03f, 7.5f)), V(0.04f, 0.2f, 0.18f), DarkWood);
        // 方田：3×3 格，每格土色底、金黃麥田、七條田壟；外圍木圍籬；田邊三堆乾草。
        for (var c = 11; c <= 13; c++)
            for (var r = 5; r <= 7; r++)
            {
                var center = V(c + 0.5f, y, r + 0.5f);
                var wheat = (c + r) % 2 == 0 ? new Color(0.90f, 0.74f, 0.27f) : new Color(0.85f, 0.68f, 0.23f);
                b.Box(T(center + V(0, 0.01f, 0)), V(0.92f, 0.02f, 0.92f), new Color(0.48f, 0.36f, 0.22f));
                b.Box(T(center + V(0, 0.035f, 0)), V(0.84f, 0.04f, 0.84f), wheat);
                for (var k = 0; k < 7; k++)
                    b.Box(T(center + V(0, 0.058f, -0.36f + k * 0.12f)), V(0.8f, 0.012f, 0.05f), wheat.Lightened(0.14f));
                _picks.Add((new Aabb(V(c + 0.05f, y, r + 0.05f), V(0.9f, 0.1f, 0.9f)), village.Id));
            }
        foreach (var (a, z) in new[] { (V(11.02f, y, 5.02f), V(13.98f, y, 5.02f)), (V(11.02f, y, 7.98f), V(13.98f, y, 7.98f)),
                     (V(11.02f, y, 5.02f), V(11.02f, y, 7.98f)), (V(13.98f, y, 5.02f), V(13.98f, y, 7.98f)) })
        {
            var mid = (a + z) / 2f;
            var length = a.DistanceTo(z);
            var yaw = Math.Abs(a.Z - z.Z) < 0.01f ? 90f : 0f;
            b.Box(T(mid + V(0, 0.1f, 0), yaw), V(0.018f, 0.018f, length), Wood);
            b.Box(T(mid + V(0, 0.06f, 0), yaw), V(0.018f, 0.018f, length), Wood);
            var n = (int)(length / 0.25f);
            for (var i = 0; i <= n; i++)
                b.Box(T(a.Lerp(z, i / (float)n) + V(0, 0.065f, 0)), V(0.025f, 0.13f, 0.025f), DarkWood);
        }
        Haystack(b, V(14.3f, y, 5.4f));
        Haystack(b, V(14.4f, y, 5.75f));
        Haystack(b, V(10.9f, y, 4.75f));
        _detail.AddChild(b.Commit(VertexMaterial()));

        // 水車（自己一個節點，繞東西向的軸轉）：雙輪圈、六根輪輻、十二片葉片、輪軸。
        _wheel = new Node3D { Position = V(7.93f, IsoProtoMap.WaterLevel + 0.14f, 5.72f) };
        _detail.AddChild(_wheel);
        var w = new IsoMeshBuilder();
        const float radius = 0.2f;
        for (var k = 0; k < 16; k++)
        {
            var spin = new Transform3D(new Basis(Vector3.Right, Mathf.Tau * k / 16f), Vector3.Zero);
            foreach (var x in new[] { -0.035f, 0.035f })
                w.Box(spin, V(x, radius, 0), V(0.012f, 0.022f, 0.082f), DarkWood);
        }
        for (var k = 0; k < 12; k++)
        {
            var spin = new Transform3D(new Basis(Vector3.Right, Mathf.Tau * k / 12f), Vector3.Zero);
            w.Box(spin, V(0, radius + 0.025f, 0), V(0.09f, 0.05f, 0.012f), Wood);
            if (k % 2 == 0)
                foreach (var x in new[] { -0.035f, 0.035f })
                    w.Box(spin, V(x, radius / 2f, 0), V(0.012f, radius, 0.014f), Wood.Darkened(0.1f));
        }
        w.Box(new Transform3D(Basis.Identity, Vector3.Zero), V(0, 0, 0), V(0.12f, 0.04f, 0.04f), DarkWood);
        _wheel.AddChild(w.Commit(VertexMaterial()));
        _picks.Add((new Aabb(V(7.78f, IsoProtoMap.WaterLevel - 0.1f, 5.45f), V(0.3f, 0.5f, 0.55f)), village.Id));
    }

    /// <summary>地塊細節：淡淡的格線（陸地與水面）、地圖邊緣土層的嵌石、水陸交界的浪花。</summary>
    private void BuildTerrainDetail()
    {
        var lines = new IsoMeshBuilder { Variation = 0f };
        var foam = new IsoMeshBuilder { Variation = 0f };
        var pebbles = new IsoMeshBuilder();
        var id = Transform3D.Identity;
        for (var c = 0; c < IsoProtoMap.Columns; c++)
            for (var r = 0; r < IsoProtoMap.Rows; r++)
            {
                var water = Map.IsWater(c, r);
                var y = water ? IsoProtoMap.WaterLevel + 0.004f : Map.TopOf(c, r) + 0.004f;
                if (Map.TerrainAt(c, r) == IsoTerrain.Hill)
                    continue; // 丘陵中央隆起，格線會被吃掉，不畫
                var line = water ? new Color(1f, 1f, 1f) : new Color(0.1f, 0.12f, 0.05f);
                const float w = 0.012f;
                lines.Quad(id, V(c, y, r), V(c + 1, y, r), V(c + 1, y, r + w), V(c, y, r + w), line, V(c + 0.5f, y - 1f, r + 0.5f));
                lines.Quad(id, V(c, y, r), V(c + w, y, r), V(c + w, y, r + 1), V(c, y, r + 1), line, V(c + 0.5f, y - 1f, r + 0.5f));
                // 浪花：水格與陸地相鄰的那一邊。
                if (water)
                    foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                    {
                        if (!IsoProtoMap.Contains(c + dc, r + dr) || Map.IsWater(c + dc, r + dr))
                            continue;
                        var fy = IsoProtoMap.WaterLevel + 0.006f;
                        const float fw = 0.07f;
                        var (a, b2) = dc switch
                        {
                            1 => (V(c + 1 - fw, fy, r), V(c + 1, fy, r + 1)),
                            -1 => (V(c, fy, r), V(c + fw, fy, r + 1)),
                            _ => dr == 1 ? (V(c, fy, r + 1 - fw), V(c + 1, fy, r + 1)) : (V(c, fy, r), V(c + 1, fy, r + fw))
                        };
                        foam.Quad(id, a, V(b2.X, fy, a.Z), b2, V(a.X, fy, b2.Z), new Color(1f, 1f, 1f), V(c + 0.5f, fy - 1f, r + 0.5f));
                    }
                // 地圖邊緣的土層剖面嵌幾顆石頭。
                if (!water && (c == IsoProtoMap.Columns - 1 || r == IsoProtoMap.Rows - 1))
                    for (var k = 0; k < 3; k++)
                    {
                        var depth = Map.TopOf(c, r) - 0.15f - 0.6f * IsoProtoMap.Hash01(c, r, 50 + k);
                        var along = 0.15f + 0.7f * IsoProtoMap.Hash01(c, r, 60 + k);
                        var at = c == IsoProtoMap.Columns - 1 ? V(c + 1.005f, depth, r + along) : V(c + along, depth, r + 1.005f);
                        pebbles.Blob(T(at), V(0, 0, 0), V(0.05f, 0.035f, 0.05f), new Color(0.52f, 0.47f, 0.40f), c * 7 + r * 3 + k, 0.3f);
                    }
            }
        var lineMaterial = VertexMaterial();
        lineMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        lineMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        lineMaterial.AlbedoColor = new Color(1f, 1f, 1f, 0.13f);
        _world.AddChild(lines.Commit(lineMaterial, shadows: false));
        var foamMaterial = VertexMaterial();
        foamMaterial.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
        foamMaterial.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        foamMaterial.AlbedoColor = new Color(1f, 1f, 1f, 0.55f);
        _world.AddChild(foam.Commit(foamMaterial, shadows: false));
        _world.AddChild(pebbles.Commit(VertexMaterial()));
    }
}
