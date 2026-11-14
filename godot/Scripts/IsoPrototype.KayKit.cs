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

    /// <summary>樹林、零星的樹、岩石、丘陵與山（KayKit 自然物）。</summary>
    private void BuildKitNature()
    {
        var taken = Map.Settlements.SelectMany(s => s.Cells).ToHashSet();
        string[] forests = { "trees_A_medium", "trees_B_medium", "trees_A_large", "trees_B_large" };
        for (var c = 0; c < IsoProtoMap.Columns; c++)
            for (var r = 0; r < IsoProtoMap.Rows; r++)
            {
                float H(int k) => IsoProtoMap.Hash01(c, r, k);
                var h = Map.TopOf(c, r);
                var yaw = 90f * (int)(H(2) * 4f); // 自然物才轉方向
                switch (Map.TerrainAt(c, r))
                {
                    case IsoTerrain.Forest:
                        Kit(_trees, forests[(int)(H(1) * forests.Length)], V(c + 0.5f, h, r + 0.5f), yaw, TempKitNature);
                        break;
                    case IsoTerrain.Hill when H(5) < 0.6f:
                        Kit(_trees, H(6) < 0.6f ? "hills_A_trees" : "mountain_A_grass_trees", V(c + 0.5f, h, r + 0.5f), yaw, TempKitNature);
                        break;
                    case IsoTerrain.Grass when !taken.Contains(new Vector2I(c, r)):
                        if (H(9) < 0.18f)
                            Kit(_trees, H(3) < 0.5f ? "tree_single_A" : "tree_single_B", V(c + 0.3f + 0.4f * H(4), h, r + 0.3f + 0.4f * H(7)), 0f, 0.45f);
                        else if (H(9) > 0.9f)
                            Kit(_trees, "rock_single_C", V(c + 0.5f, h, r + 0.5f), yaw, 0.6f);
                        break;
                    case IsoTerrain.Sand when c == 1 && H(11) < 0.5f:
                        Kit(_trees, "rock_single_A", V(c + 0.3f, h, r + 0.3f + 0.4f * H(12)), yaw, 0.8f);
                        break;
                }
            }
        // 伐木場（金柵鎮的工作地）。
        var y = IsoProtoMap.LandTop;
        Kit(_trees, "building_lumbermill_yellow", V(6.45f, y, 4.45f), 0f, TempKitLarge);
        Kit(_trees, "resource_lumber", V(5.95f, y, 4.7f), 0f, 0.5f);
    }

    /// <summary>
    /// 金柵鎮（金色勢力＝黃色版模型）：十字土路與中央廣場、廣場上的市集、木柵（每邊 5 段，南邊中段是柵門）、四角角樓、柵門兩側旗子；
    /// 四個街區：東北是教堂（鎮公所），其他三區各 2×2 棟房子，全部門朝南、等距對齊。
    /// </summary>
    private void BuildKitTown(IsoSettlement town)
    {
        var y = IsoProtoMap.LandTop;
        float x0 = 2.12f, x1 = 4.88f, z0 = 5.12f, z1 = 7.88f;
        var cx = (x0 + x1) / 2f;
        var cz = (z0 + z1) / 2f;
        var path = KitColor(214, 170, 118);
        var b = new IsoMeshBuilder { Variation = 0f };
        b.Box(T(V(cx, y + 0.004f, cz)), V(x1 - x0, 0.008f, 0.22f), path);
        b.Box(T(V(cx, y + 0.004f, (z0 + z1 + 0.4f) / 2f)), V(0.22f, 0.008f, z1 - z0 + 0.4f), path);
        b.Box(T(V(cx, y + 0.005f, cz)), V(0.84f, 0.01f, 0.84f), path);
        _detail.AddChild(b.Commit(VertexMaterial()));

        Kit(_detail, "building_market_yellow", V(cx, y, cz), 0f, 0.4f, settlement: town.Id);
        // 木柵：模型的柵欄在本地 x＝-1、沿 z 長 1.16；縮放 0.5 後補回 0.5 的位移，長度拉成一段的長度。
        const int segments = 5;
        var length = (x1 - x0) / segments;
        var stretch = new Vector3(0.5f, 0.5f, length / 1.16f);
        for (var i = 0; i < segments; i++)
        {
            var a = x0 + length * (i + 0.5f);
            var along = z0 + (a - x0);
            Kit(_detail, "fence_wood_straight", V(a, y, z0 - 0.5f), 90f, scale3: stretch, settlement: town.Id);
            Kit(_detail, i == segments / 2 ? "fence_wood_straight_gate" : "fence_wood_straight", V(a, y, z1 - 0.5f), 90f, scale3: stretch, settlement: town.Id);
            Kit(_detail, "fence_wood_straight", V(x0 + 0.5f, y, along), 0f, scale3: stretch, settlement: town.Id);
            Kit(_detail, "fence_wood_straight", V(x1 + 0.5f, y, along), 0f, scale3: stretch, settlement: town.Id);
        }
        foreach (var (px, pz) in new[] { (x0, z0), (x1, z0), (x0, z1), (x1, z1) })
            Kit(_detail, "building_tower_A_yellow", V(px, y, pz), 0f, TempKitTower, settlement: town.Id);
        Kit(_detail, "flag_yellow", V(cx - 0.22f, y, z1 + 0.05f), 0f, 0.9f);
        Kit(_detail, "flag_yellow", V(cx + 0.22f, y, z1 + 0.05f), 0f, 0.9f);

        string[] homes = { "building_home_A_yellow", "building_home_B_yellow", "building_home_B_yellow", "building_home_A_yellow" };
        var n = 0;
        foreach (var (qx, qz) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
        {
            if (qx == 1 && qz == -1)
            {
                Kit(_detail, "building_church_yellow", V(cx + 0.72f, y, cz - 0.72f), 0f, 0.5f, settlement: town.Id);
                continue;
            }
            for (var i = 0; i < 4; i++, n++)
                Kit(_detail, homes[n % homes.Length], V(cx + (0.42f + (i % 2) * 0.52f) * qx, y, cz + (0.42f + (i / 2) * 0.52f) * qz), 0f, TempKitHouse, settlement: town.Id);
        }
        Kit(_detail, "barrel", V(x0 + 0.18f, y, z0 + 0.2f), 0f, 0.6f);
        Kit(_detail, "crate_A_big", V(x0 + 0.3f, y, z0 + 0.18f), 0f, 0.6f);
    }

    /// <summary>
    /// 河灣村（藍色勢力＝藍色版模型）：河上的石橋接一條往東到方田的土路；路北一排三棟、路南一排三棟、再南兩棟，全部門朝南；
    /// 河岸的水磨坊（輪子在河上、會轉）、井、3×3 方田（田壟）與田邊的風車（扇葉會轉）。
    /// </summary>
    private void BuildKitVillage(IsoSettlement village)
    {
        var y = IsoProtoMap.LandTop;
        var b = new IsoMeshBuilder { Variation = 0f };
        b.Box(T(V(9f, y + 0.004f, 7.08f)), V(4f, 0.008f, 0.16f), KitColor(214, 170, 118));
        var grain = KitColor(226, 176, 83);
        var furrow = KitColor(176, 126, 60);
        for (var c = 11; c <= 13; c++)
            for (var r = 5; r <= 7; r++)
            {
                b.Box(T(V(c + 0.5f, y + 0.01f, r + 0.5f)), V(0.88f, 0.02f, 0.88f), grain);
                for (var j = 0; j < 5; j++)
                    b.Box(T(V(c + 0.5f, y + 0.025f, r + 0.15f + j * 0.175f)), V(0.82f, 0.012f, 0.04f), furrow);
                _picks.Add((new Aabb(V(c + 0.06f, y, r + 0.06f), V(0.88f, 0.05f, 0.88f)), village.Id));
            }
        _detail.AddChild(b.Commit(VertexMaterial()));

        Kit(_detail, "building_bridge_A", V(7.5f, y - 0.05f, 7.08f), 90f, 0.5f);
        foreach (var (x, z, art) in new[]
                 {
                     (8.9f, 6.55f, "building_home_A_blue"), (9.55f, 6.55f, "building_home_B_blue"), (10.2f, 6.55f, "building_home_A_blue"),
                     (8.9f, 7.62f, "building_home_B_blue"), (9.55f, 7.62f, "building_home_A_blue"), (10.2f, 7.62f, "building_home_B_blue"),
                     (9.2f, 8.55f, "building_home_A_blue"), (9.85f, 8.55f, "building_home_A_blue")
                 })
            Kit(_detail, art, V(x, y, z), 0f, TempKitHouse, settlement: village.Id);
        Kit(_detail, "building_well_blue", V(10.75f, y, 7.6f), 0f, TempKitHouse, settlement: village.Id);
        // 水磨坊：模型的輪子在 +X 側，轉 180 度讓輪子落在西邊的河上。
        Kit(_detail, "building_watermill_blue", V(8.22f, y, 6.4f), 180f, TempKitLarge, settlement: village.Id);
        Kit(_detail, "building_windmill_blue", V(13.5f, y, 4.5f), 0f, TempKitLarge, settlement: village.Id);
        Kit(_detail, "flag_blue", V(8.6f, y, 7.1f), 0f, 0.9f);
        Kit(_detail, "wheelbarrow", V(10.9f, y, 6.6f), 0f, 0.6f);
        Kit(_detail, "sack", V(11f, y, 6.75f), 0f, 0.8f);
    }
}
