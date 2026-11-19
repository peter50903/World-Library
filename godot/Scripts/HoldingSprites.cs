using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-40 章：建築卡片用的正面像素素材（第十一、十二批）——建築表 ID 對應的素材、工地、廢墟、城牆段與住宅。
/// 原本是第 08-33 章拼領地圖（HoldingLayout）的一部分；使用者 2026-11-19 不要拼出來的領地圖（改放等級示意圖，見 <see cref="HoldingArt"/>），
/// 拼圖的排版拿掉，只留卡片要的這些。
/// </summary>
public static class HoldingSprites
{
    private const int T = 16;

    /// <summary>素材的大小（多格動畫取一格）；缺圖時 3×3 格。</summary>
    public static Vector2 SizeOf(string sprite)
    {
        if (PixelParts.Texture(sprite) is not { } texture)
            return new Vector2(3 * T, 3 * T);
        var (width, height, _) = PixelParts.Metrics(texture);
        return new Vector2(width / FramesOf(sprite), height);
    }

    public static int FramesOf(string sprite) => sprite == "b11_windmill" ? 4 : 1; // 四格動畫（卡片只畫第一格）

    /// <summary>第 08-33 章：建築表 ID → 正面像素素材（營、村的神殿是祭壇、廚房是風車磨坊、鞣革場是獵屋；名稱見 <see cref="DemoHoldingReport.Name"/>）。</summary>
    public static string BuildingSprite(string defId, int id, DemoSettlementTier tier)
    {
        var small = tier <= DemoSettlementTier.Village;
        return defId switch
        {
            "BUILD_WORKSHOP" => "b11_workshop",
            "BUILD_KITCHEN" => small ? "b11_windmill" : "b12_kitchen",
            "BUILD_TANNERY" => small ? "b11_hunting_lodge" : "b12_tannery",
            "BUILD_CHURCH" => small ? "b11_shrine" : "b11_temple",
            "BUILD_SMITHY" => "b11_smithy",
            "BUILD_SCRIPTORIUM" => "b11_scriptorium",
            "BUILD_KILN" => "b11_kiln",
            "BUILD_SCHOOL" => "b11_school",
            "BUILD_TOWN_HALL" => small ? "b11_hall_village" : "b11_hall_town",
            "BUILD_CASTLE" => "b11_keep",
            "BUILD_GRANARY" => "b11_granary",
            "BUILD_FARM" => "b11_farmhouse_" + (id % 2 + 1),
            "BUILD_BARRACK" => "b11_barracks",
            "BUILD_OUTPOST" => "b11_watchtower",
            "BUILD_INN" => "b11_inn",
            "BUILD_WAREHOUSE" => "b11_warehouse",
            "BUILD_STABLE" => "b11_stable",
            "BUILD_ARCHERY_RANGE" => "b11_archery",
            "BUILD_SHELTER" => "b11_tent_" + (id % 2 + 1),
            "BUILD_HOUSE_BASIC" => "b11_house_" + (id % 3 + 1),
            "BUILD_HOUSE_LARGE" => "b11_stone_house_" + (id % 3 + 1),
            "BUILD_MARKET" => "b11_stall_" + (id % 2 + 1), // 第 08-40 章：市場沒有單獨的建築圖，用攤位
            _ => "b12_" + defId["BUILD_".Length..].ToLowerInvariant()
        };
    }

    /// <summary>石造（廢墟用石造殘骸）：建築表的石料多於木料。</summary>
    private static bool Stony(string defId) => DemoBuildingCatalog.Contains(defId) && DemoBuildingCatalog.Get(defId).Stone > DemoBuildingCatalog.Get(defId).Wood;

    /// <summary>工地：依原建築寬度挑小、中、大，依進度挑三個階段。</summary>
    public static string SiteSprite(float width, float progress)
    {
        var scale = width <= 56 ? "s" : width <= 88 ? "m" : "l";
        var stage = progress < 0.34f ? 1 : progress < 0.67f ? 2 : 3;
        return $"b11_site_{scale}_{stage}";
    }

    private static string RuinSprite(string defId, float width)
    {
        if (defId is DemoBuildings.Outpost or DemoBuildings.StoneTower)
            return "b12_ruin_tower";
        if (Stony(defId))
            return width >= 150 ? "b12_ruin_stone_l" : width >= 80 ? "b12_ruin_stone_m" : "b11_ruin_small";
        return width >= 80 ? "b12_ruin_wood_m" : "b12_ruin_wood_s";
    }

    /// <summary>
    /// 第 08-38 章：建築卡片的插圖（素材、蓋在前面的工地、是否一整排）：完好＝正面像素圖；施工中＝淡淡的建築＋工地；毀損＝廢墟。
    /// 城牆是一整排牆面（木柵、石牆，首都是磚牆），城門、塔樓照常。
    /// </summary>
    public static (string Sprite, string? Overlay, bool Tiled) CardArt(string defId, int buildingId, DemoBuildingStatus status, float progress,
        DemoSettlementTier tier)
    {
        if (defId is DemoBuildings.WallWood or DemoBuildings.WallStone or DemoBuildings.GateWood or DemoBuildings.GateStone)
        {
            var stone = defId is DemoBuildings.WallStone or DemoBuildings.GateStone;
            var kind = stone ? (tier >= DemoSettlementTier.Capital ? "brick" : "stone") : "palisade";
            var material = stone ? "stone" : "palisade";
            var gate = defId is DemoBuildings.GateWood or DemoBuildings.GateStone;
            var whole = gate ? $"b11_{kind}_gate_h" : $"b11_{kind}_h";
            return status switch
            {
                DemoBuildingStatus.Ruined => (gate ? $"b12_ruin_{material}_gate_h" : $"b12_ruin_{material}_h", null, !gate),
                DemoBuildingStatus.Building => (whole, gate ? SiteSprite(SizeOf(whole).X, progress) : $"b12_site_{material}_h", !gate),
                _ => (whole, null, !gate)
            };
        }
        var sprite = BuildingSprite(defId, buildingId, tier);
        var width = SizeOf(sprite).X;
        return status switch
        {
            DemoBuildingStatus.Ruined => (RuinSprite(defId, width), null, false),
            DemoBuildingStatus.Building => (sprite, SiteSprite(width, progress), false),
            _ => (sprite, null, false)
        };
    }

    /// <summary>第 08-38 章：住宅卡片的插圖（營地是帳篷；首都的大型住宅是連棟，卡片上用磚房）。</summary>
    public static string HouseCard(DemoSettlementTier tier, bool large)
    {
        var sprite = HouseSprite(tier, large, 1);
        return sprite == "ROW" ? "b11_brick_house_1" : sprite;
    }

    private static string HouseSprite(DemoSettlementTier tier, bool large, int v) => (tier, large) switch
    {
        (DemoSettlementTier.Camp, _) => "b11_tent_" + (v % 2 + 1),
        (DemoSettlementTier.Village, _) => v % 3 == 0 ? "b11_hut_" + (v % 3 + 1) : "b11_house_" + (v % 3 + 1),
        (DemoSettlementTier.Town, true) => "b11_stone_house_" + (v % 3 + 1),
        (DemoSettlementTier.Town, false) => "b11_house_" + (v % 3 + 1),
        (DemoSettlementTier.City, true) => "b11_brick_house_" + (v % 2 + 1),
        (DemoSettlementTier.City, false) => "b11_stone_house_" + (v % 3 + 1),
        (_, true) => "ROW",
        _ => "b11_brick_house_" + (v % 2 + 1)
    };
}
