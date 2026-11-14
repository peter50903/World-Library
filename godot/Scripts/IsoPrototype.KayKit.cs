using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-20b 章：3D 等角原型改用現成的低多邊形模型（KayKit Medieval Hexagon Pack 免費版，CC0，Kay Lousberg）。
/// 使用者 2026-11-14：2D 精靈圖方向混亂、程式拼的模型粗糙，改試網路上同一風格、數量夠撐起世界的素材包。
/// 模型放在 <c>godot/assets/kaykit/</c>（glTF＋bin＋一張色票貼圖）；正方形格不變，只拿建築、樹、道具，地塊仍由程式畫。
/// 擺放規則：一格＝1 單位，模型多縮成 0.38～0.5 倍；同一聚落的房子一律門朝南（模型的 +Z），沿街道對齊、等距排列。
/// 素材不在時退回 08-20 的程式模型（總綱第 7 節：缺素材也能啟動）。
/// </summary>
public partial class IsoPrototypeView
{
    public const string KitDir = "res://assets/kaykit/";

    /// <summary>【TEMP】KayKit 模型的縮放：房子、大型建築、角樓、樹與山。</summary>
    public const float TempKitHouse = 0.38f, TempKitLarge = 0.42f, TempKitTower = 0.26f, TempKitNature = 0.5f;

    private readonly Dictionary<string, PackedScene?> _kit = new();
    private readonly List<Node3D> _kitWheels = new(), _kitFans = new();

    /// <summary>這次是否用 KayKit 模型建場景。</summary>
    public bool UsesKit { get; private set; }

    /// <summary>放了幾個 KayKit 模型。</summary>
    public int KitPlaced { get; private set; }

    public static bool KitAvailable() => ResourceLoader.Exists(KitDir + "building_home_A_blue.gltf");

    /// <summary>KayKit 的色票（貼圖是 sRGB；頂點色要先轉成線性才會一樣）。</summary>
    private static Color KitColor(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f).SrgbToLinear();

    /// <summary>KayKit 色調的地塊頂面（草地偏黃綠、沙灘淺棕、丘陵暗一點）。</summary>
    private static Color KitTerrainColor(IsoTerrain t) => t switch
    {
        IsoTerrain.Sea or IsoTerrain.River => KitColor(196, 164, 108),
        IsoTerrain.Sand => KitColor(226, 196, 138),
        IsoTerrain.Hill => KitColor(150, 168, 70),
        _ => KitColor(170, 182, 62)
    };

    /// <summary>
    /// 第 08-20b 章（使用者 2026-11-14：地塊是一整片綠色）：草地依 2×2 區塊分成亮草地、一般、深草三種色調，再加每格一點明暗。
    /// </summary>
    private static Color KitTone(int c, int r, IsoTerrain t)
    {
        if (t is not (IsoTerrain.Grass or IsoTerrain.Forest))
            return KitTerrainColor(t);
        var n = IsoProtoMap.Hash01(c / 2 + 3, r / 2, 41) * 0.6f + IsoProtoMap.Hash01(c, r, 42) * 0.4f;
        return n > 0.62f ? KitColor(186, 194, 74) : n < 0.3f ? KitColor(146, 160, 52) : KitColor(170, 182, 62);
    }

    private static readonly Color KitPath = KitColor(214, 170, 118), KitPathEdge = KitColor(184, 140, 94);

    /// <summary>土路（沿 X 或 Z 軸的直段）：淺色路面加一圈深色路緣。</summary>
    private static void Road(IsoMeshBuilder b, float x0, float z0, float x1, float z1, float width, float y = IsoProtoMap.LandTop)
    {
        var center = new Vector3((x0 + x1) / 2f, y, (z0 + z1) / 2f);
        var alongX = Math.Abs(z1 - z0) < 1e-4f;
        var length = alongX ? Math.Abs(x1 - x0) : Math.Abs(z1 - z0);
        Vector3 Size(float w, float h) => alongX ? new Vector3(length, h, w) : new Vector3(w, h, length);
        b.Box(T(center + Vector3.Up * 0.005f), Size(width + 0.04f, 0.01f), KitPathEdge);
        b.Box(T(center + Vector3.Up * 0.008f), Size(width, 0.012f), KitPath);
    }

    /// <summary>房子＋門前小徑接到街（門在模型的 +Z；朝南 yaw 0、朝北 yaw 180）。</summary>
    private void KitHouse(IsoMeshBuilder roads, string name, float x, float z, float yaw, float streetZ, int settlement)
    {
        Kit(_detail, name, V(x, IsoProtoMap.LandTop, z), yaw, TempKitHouse, settlement: settlement);
        var front = yaw == 0f ? z + 0.17f : z - 0.17f;
        if (Math.Abs(streetZ - front) > 0.02f)
            Road(roads, x, front, x, streetZ, 0.07f);
    }

    /// <summary>放一個 KayKit 模型；<paramref name="settlement"/> 大於 0 時加入點選範圍。找不到模型回傳 null。</summary>
    private Node3D? Kit(Node3D parent, string name, Vector3 at, float yaw = 0f, float scale = 1f, Vector3? scale3 = null, int settlement = 0)
    {
        if (!_kit.TryGetValue(name, out var scene))
        {
            var path = KitDir + name + ".gltf";
            scene = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
            _kit[name] = scene;
        }
        if (scene?.Instantiate() is not Node3D node)
            return null;
        node.Position = at;
        node.RotationDegrees = new Vector3(0f, yaw, 0f);
        node.Scale = scale3 ?? Vector3.One * scale;
        parent.AddChild(node);
        KitPlaced++;
        var box = new Aabb();
        var first = true;
        foreach (var child in node.FindChildren("*", "Node3D", true, false))
        {
            if (child is not Node3D part)
                continue;
            // 會動的零件：水車輪、風車扇葉。
            if (part.Name.ToString().Contains("_wheel"))
                _kitWheels.Add(part);
            else if (part.Name.ToString().Contains("_fan"))
                _kitFans.Add(part);
            if (part is MeshInstance3D mesh && mesh.Mesh is not null)
            {
                var world = mesh.GlobalTransform * mesh.Mesh.GetAabb();
                box = first ? world : box.Merge(world);
                first = false;
            }
        }
        if (settlement > 0 && !first)
            _picks.Add((box, settlement));
        return node;
    }

    /// <summary>水車輪、風車扇葉慢慢轉。</summary>
    private void SpinKit(float delta)
    {
        foreach (var wheel in _kitWheels)
            wheel.RotateObjectLocal(Vector3.Right, delta * 1.2f);
        foreach (var fan in _kitFans)
            fan.RotateObjectLocal(Vector3.Back, delta * 0.8f);
    }

    /// <summary>
    /// 樹林、零星的樹、岩石、丘陵與山（KayKit 自然物）；草地上的草叢與小花（程式畫的小三角，合成一個網格）。
    /// </summary>
    private void BuildKitNature()
    {
        var flat = Map.Settlements.SelectMany(s => s.Cells).Concat(IsoProtoMap.RoadCells).ToHashSet();
        string[] forests = { "trees_A_medium", "trees_B_medium", "trees_A_large", "trees_B_large" };
        var tuft = KitColor(120, 140, 40);
        Color[] flowers = { KitColor(240, 236, 210), KitColor(232, 200, 70), KitColor(214, 110, 90) };
        var deco = new IsoMeshBuilder { Variation = 0f };
        var id = Transform3D.Identity;
        for (var c = 0; c < IsoProtoMap.Columns; c++)
            for (var r = 0; r < IsoProtoMap.Rows; r++)
            {
                float H(int k) => IsoProtoMap.Hash01(c, r, k);
                var h = Map.TopOf(c, r);
                var yaw = 90f * (int)(H(2) * 4f); // 自然物才轉方向
                var open = !flat.Contains(new Vector2I(c, r));
                switch (Map.TerrainAt(c, r))
                {
                    case IsoTerrain.Forest:
                        Kit(_trees, forests[(int)(H(1) * forests.Length)], V(c + 0.5f, h, r + 0.5f), yaw, TempKitNature);
                        break;
                    case IsoTerrain.Hill when H(5) < 0.6f:
                        Kit(_trees, H(6) < 0.6f ? "hills_A_trees" : "mountain_A_grass_trees", V(c + 0.5f, h, r + 0.5f), yaw, TempKitNature);
                        break;
                    case IsoTerrain.Grass when open:
                        if (H(9) < 0.2f)
                            Kit(_trees, H(3) < 0.5f ? "tree_single_A" : "tree_single_B", V(c + 0.3f + 0.4f * H(4), h, r + 0.3f + 0.4f * H(7)), 0f, 0.45f);
                        else if (H(9) > 0.88f)
                            Kit(_trees, "rock_single_C", V(c + 0.5f, h, r + 0.5f), yaw, 0.6f);
                        for (var k = 0; k < 5; k++)
                        {
                            if (H(100 + k) >= 0.55f)
                                continue;
                            var x = c + 0.1f + 0.8f * H(110 + k);
                            var z = r + 0.1f + 0.8f * H(120 + k);
                            var tall = 0.05f + 0.04f * H(130 + k);
                            deco.Tri(id, V(x - 0.03f, h, z), V(x + 0.03f, h, z + 0.01f), V(x, h + tall, z), tuft, V(x, h - 1f, z - 0.5f));
                            deco.Tri(id, V(x, h, z - 0.03f), V(x, h, z + 0.03f), V(x + 0.005f, h + tall * 0.8f, z), tuft, V(x - 0.5f, h - 1f, z));
                        }
                        if (H(140) < 0.35f)
                        {
                            var color = flowers[(int)(H(143) * flowers.Length)];
                            for (var q = 0; q < 3; q++)
                                deco.Box(T(V(c + 0.2f + 0.6f * H(141) + 0.05f * Mathf.Cos(q * 2.1f), h + 0.008f, r + 0.2f + 0.6f * H(142) + 0.05f * Mathf.Sin(q * 2.1f))), V(0.024f, 0.012f, 0.024f), color);
                        }
                        break;
                    case IsoTerrain.Sand when c == 1 && H(11) < 0.5f:
                        Kit(_trees, "rock_single_A", V(c + 0.3f, h, r + 0.3f + 0.4f * H(12)), yaw, 0.8f);
                        break;
                }
            }
        _trees.AddChild(deco.Commit(VertexMaterial(), shadows: false));
    }

    /// <summary>
    /// 道路（第 08-20b 章，使用者 2026-11-14：橋沒接好、建築沒有連接性）：城鎮東門往東、南轉、過石橋接河灣村主街，一路到方田；
    /// 伐木場有支路接到這條路。
    /// </summary>
    private void BuildKitRoads(IsoMeshBuilder roads)
    {
        Road(roads, 4.88f, 6.5f, 6.61f, 6.5f, 0.22f);
        Road(roads, 6.5f, 6.39f, 6.5f, 7.61f, 0.22f);
        Road(roads, 6.39f, 7.5f, 7f, 7.5f, 0.22f);
        Kit(_detail, "building_bridge_A", V(7.5f, IsoProtoMap.LandTop, 7.5f), 90f, scale3: new Vector3(0.32f, 0.5f, 0.53f));
        Road(roads, 8f, 7.5f, 11f, 7.5f, 0.22f);
        Kit(_trees, "building_lumbermill_yellow", V(6f, IsoProtoMap.LandTop, 4.45f), 0f, TempKitLarge);
        Kit(_trees, "resource_lumber", V(5.55f, IsoProtoMap.LandTop, 4.75f), 0f, 0.5f);
        Road(roads, 6f, 4.65f, 6f, 6.39f, 0.12f);
    }

    /// <summary>
    /// 金柵鎮（金色勢力＝黃色版模型）：十字主街、城牆內側一圈環路、中央石板廣場與市集；木柵每邊 5 段、東邊中段是柵門（朝河灣村），四角角樓。
    /// 四個街區：東北是教堂（鎮公所），其他三區各 2×2 棟——內排面向主街、外排面向環路（背對背），每棟有門前小徑。
    /// </summary>
    private void BuildKitTown(IsoSettlement town, IsoMeshBuilder roads)
    {
        var y = IsoProtoMap.LandTop;
        float x0 = 2.12f, x1 = 4.88f, z0 = 5.12f, z1 = 7.88f;
        var cx = (x0 + x1) / 2f;
        var cz = (z0 + z1) / 2f;
        const float ring = 1.2f;
        Road(roads, x0, cz, x1, cz, 0.22f);
        Road(roads, cx, z0, cx, z1, 0.22f);
        Road(roads, cx - ring, cz - ring, cx + ring, cz - ring, 0.12f);
        Road(roads, cx - ring, cz + ring, cx + ring, cz + ring, 0.12f);
        Road(roads, cx - ring, cz - ring, cx - ring, cz + ring, 0.12f);
        Road(roads, cx + ring, cz - ring, cx + ring, cz + ring, 0.12f);
        roads.Box(T(V(cx, y + 0.01f, cz)), V(0.8f, 0.016f, 0.8f), KitColor(200, 190, 170));
        Kit(_detail, "building_market_yellow", V(cx, y, cz), 0f, 0.36f, settlement: town.Id);
        Kit(_detail, "building_well_yellow", V(cx - 0.3f, y, cz + 0.3f), 0f, 0.3f, settlement: town.Id);

        // 木柵：模型的柵欄在本地 x＝-1、沿 z 長 1.16；縮放 0.5 後補回 0.5 的位移，長度拉成一段的長度。
        const int segments = 5;
        var length = (x1 - x0) / segments;
        var stretch = new Vector3(0.5f, 0.5f, length / 1.16f);
        for (var i = 0; i < segments; i++)
        {
            var a = x0 + length * (i + 0.5f);
            var along = z0 + (a - x0);
            Kit(_detail, "fence_wood_straight", V(a, y, z0 - 0.5f), 90f, scale3: stretch, settlement: town.Id);
            Kit(_detail, "fence_wood_straight", V(a, y, z1 - 0.5f), 90f, scale3: stretch, settlement: town.Id);
            Kit(_detail, "fence_wood_straight", V(x0 + 0.5f, y, along), 0f, scale3: stretch, settlement: town.Id);
            Kit(_detail, i == segments / 2 ? "fence_wood_straight_gate" : "fence_wood_straight", V(x1 + 0.5f, y, along), 0f, scale3: stretch, settlement: town.Id);
        }
        foreach (var (px, pz) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1) })
            Kit(_detail, "building_tower_A_yellow", V(px, y, pz), 0f, TempKitTower, settlement: town.Id);
        Kit(_detail, "flag_yellow", V(x1 + 0.06f, y, cz - 0.24f), 0f, 0.9f);
        Kit(_detail, "flag_yellow", V(x1 + 0.06f, y, cz + 0.24f), 0f, 0.9f);

        string[] homes = { "building_home_A_yellow", "building_home_B_yellow" };
        var n = 0;
        foreach (var (qx, qz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            if (qx == 1 && qz == -1)
            {
                Kit(_detail, "building_church_yellow", V(cx + 0.68f, y, cz - 0.66f), 0f, 0.46f, settlement: town.Id);
                Road(roads, cx + 0.68f, cz - 0.4f, cx + 0.68f, cz, 0.08f);
                continue;
            }
            for (var i = 0; i < 4; i++, n++)
            {
                var inner = i / 2 == 0;
                var southward = (qz < 0) == inner; // 內排面向主街、外排面向環路
                KitHouse(roads, homes[n % homes.Length], cx + (0.4f + (i % 2) * 0.48f) * qx, cz + (inner ? 0.42f : 0.92f) * qz,
                    southward ? 0f : 180f, inner ? cz : cz + ring * qz, town.Id);
            }
        }
        Kit(_detail, "barrel", V(x0 + 0.2f, y, z0 + 0.2f), 0f, 0.6f);
        Kit(_detail, "crate_A_big", V(x0 + 0.32f, y, z0 + 0.18f), 0f, 0.6f);
    }

    /// <summary>
    /// 河灣村（藍色勢力＝藍色版模型）：過橋的主街往東到方田；主街北排三棟門朝南、南排三棟門朝北，每棟有門前小徑；
    /// 往南的小巷兩側各一棟（門朝小巷）；河北段東岸的水磨坊（輪子在河上、會轉）有小路接主街；井、雜物、3×3 方田與風車（扇葉會轉）。
    /// </summary>
    private void BuildKitVillage(IsoSettlement village, IsoMeshBuilder roads)
    {
        var y = IsoProtoMap.LandTop;
        const float street = 7.5f;
        foreach (var (x, art) in new[] { (8.75f, "building_home_A_blue"), (9.45f, "building_home_B_blue"), (10.15f, "building_home_A_blue") })
            KitHouse(roads, art, x, 7f, 0f, street, village.Id);
        foreach (var (x, art) in new[] { (8.75f, "building_home_B_blue"), (9.45f, "building_home_A_blue"), (10.15f, "building_home_B_blue") })
            KitHouse(roads, art, x, 8.05f, 180f, street, village.Id);
        Road(roads, 9.8f, street, 9.8f, 8.9f, 0.1f);
        Kit(_detail, "building_home_A_blue", V(9.45f, y, 8.65f), 90f, TempKitHouse, settlement: village.Id);
        Kit(_detail, "building_home_A_blue", V(10.15f, y, 8.65f), -90f, TempKitHouse, settlement: village.Id);
        Kit(_detail, "building_well_blue", V(10.65f, y, street), 0f, 0.34f, settlement: village.Id);
        // 水磨坊：模型的輪子在 +X 側，轉 180 度讓輪子落在西邊的河上。
        Kit(_detail, "building_watermill_blue", V(9.05f, Map.TopOf(9, 5), 5.55f), 180f, TempKitLarge, settlement: village.Id);
        Road(roads, 9.1f, 5.8f, 9.1f, street, 0.1f);
        Kit(_detail, "wheelbarrow", V(10.55f, y, 6.85f), 0f, 0.6f);
        Kit(_detail, "sack", V(10.62f, y, 7f), 0f, 0.8f);
        Kit(_detail, "barrel", V(8.45f, y, 8.25f), 0f, 0.6f);
        Kit(_detail, "crate_A_big", V(10.45f, y, 8.25f), 0f, 0.6f);
        Kit(_detail, "flag_blue", V(8.12f, y, 7.25f), 0f, 0.9f);

        var grain = KitColor(226, 176, 83);
        var furrow = KitColor(176, 126, 60);
        for (var c = 11; c <= 13; c++)
            for (var r = 5; r <= 7; r++)
            {
                roads.Box(T(V(c + 0.5f, y + 0.01f, r + 0.5f)), V(0.88f, 0.02f, 0.88f), grain);
                for (var j = 0; j < 5; j++)
                    roads.Box(T(V(c + 0.5f, y + 0.025f, r + 0.15f + j * 0.175f)), V(0.82f, 0.012f, 0.04f), furrow);
                _picks.Add((new Aabb(V(c + 0.06f, y, r + 0.06f), V(0.88f, 0.05f, 0.88f)), village.Id));
            }
        Kit(_detail, "building_windmill_blue", V(13.5f, y, 4.5f), 0f, TempKitLarge, settlement: village.Id);
        Road(roads, 13.5f, 4.75f, 13.5f, 5f, 0.08f);
    }
}
