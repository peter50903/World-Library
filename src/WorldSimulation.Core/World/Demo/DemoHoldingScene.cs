using System.Globalization;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-33 章：領地面板的分區（使用者 2026-11-18：要寫細一點，從城牆、農田、城內建築、兵營、城堡……）。
/// 列表照這個順序；領地圖上的位置也照分區（城牆圍一圈、農田在牆外、城堡在後排正中、兵營靠城門、資源建築在城外）。
/// </summary>
public enum DemoHoldingZone
{
    /// <summary>城牆、城門與塔樓。</summary>
    Walls = 0,

    /// <summary>農田與農舍（營、村的磨坊也在田邊）。</summary>
    Fields = 1,

    /// <summary>城內建築：工坊、商店、倉庫。</summary>
    Town = 2,

    /// <summary>神殿、學府與醫療。</summary>
    Sacred = 3,

    /// <summary>兵營與訓練場。</summary>
    Military = 4,

    /// <summary>城堡與官署（市政廳、銀行、監獄）。</summary>
    Keep = 5,

    /// <summary>城外資源：木材場、採石場、礦場、港口。</summary>
    Outside = 6
}

/// <summary>
/// 第 08-33 章：一個聚落的領地面板要的資料——聚落、等級、建築（只有蓋好的、正在蓋的、毀損的；沒蓋的不列）、
/// 附近已開墾的農田塊數、附近地形、正在蓋的住宅是哪一種、居民數。
/// </summary>
public sealed record DemoHoldingScene(
    DemoSettlement Site,
    DemoSettlementTier Tier,
    DemoHolding Holding,
    int Fields,
    DemoBuildingNearby Nearby,
    DemoBuildingDef House,
    long Residents)
{
    /// <summary>正在蓋的那一棟住宅完成幾 %（0＝沒有在蓋）。</summary>
    public int HousePercent => Holding.DwellingProgress <= 0 ? 0 : Math.Clamp(Holding.DwellingProgress * 100 / Math.Max(1, House.Hours), 1, 99);

    /// <summary>已用的建築格（住宅、城牆、城門不佔格；廢墟在重建前照樣佔格，同第 08-32 章）。</summary>
    public int SlotsUsed => Holding.Buildings.Count(b => !DemoBuildings.IsWallwork(b.DefId));

    public int Slots => DemoBuildings.Slots(Tier);
}

/// <summary>第 08-33 章：領地列表的一列（同一種、完好的建築合併成「×N」）。<see cref="Percent"/>：施工中＝進度，完成＝耐久度，毀損＝0。</summary>
public sealed record DemoHoldingRow(DemoHoldingZone Zone, string DefId, string Name, DemoBuildingStatus Status, int Percent, int Count, int BuildingId);

/// <summary>第 08-33 章：領地面板的分區、名稱與列表文字（介面的領地圖與列表、聚落詳情的「領地」分頁共用）。</summary>
public static class DemoHoldingReport
{
    public static DemoHoldingZone ZoneOf(string defId, DemoSettlementTier tier) => defId switch
    {
        DemoBuildings.WallWood or DemoBuildings.WallStone or DemoBuildings.GateWood or DemoBuildings.GateStone
            or DemoBuildings.Outpost or DemoBuildings.StoneTower => DemoHoldingZone.Walls,
        "BUILD_FARM" => DemoHoldingZone.Fields,
        "BUILD_KITCHEN" when tier <= DemoSettlementTier.Village => DemoHoldingZone.Fields, // 營、村的磨坊（風車）在田邊
        "BUILD_CHURCH" or "BUILD_SCHOOL" or "BUILD_ACADEMY" or "BUILD_MAGIC_ACADEMY" or "BUILD_SCRIPTORIUM" or "BUILD_CLINIC" or "BUILD_HOSPITAL"
            => DemoHoldingZone.Sacred,
        "BUILD_BARRACK" or "BUILD_STABLE" or "BUILD_ARCHERY_RANGE" or "BUILD_SIEGE_WORKSHOP" => DemoHoldingZone.Military,
        "BUILD_CASTLE" or "BUILD_TOWN_HALL" or "BUILD_BANK" or "BUILD_PRISON" => DemoHoldingZone.Keep,
        "BUILD_LUMBERYARD" or "BUILD_QUARRY" or "BUILD_MINE" or "BUILD_PORT" => DemoHoldingZone.Outside,
        _ => DemoHoldingZone.Town
    };

    public static string ZoneName(DemoHoldingZone zone) => zone switch
    {
        DemoHoldingZone.Walls => "城牆與塔樓",
        DemoHoldingZone.Fields => "農田與農舍",
        DemoHoldingZone.Town => "城內建築",
        DemoHoldingZone.Sacred => "神殿與學府",
        DemoHoldingZone.Military => "兵營與訓練場",
        DemoHoldingZone.Keep => "城堡與官署",
        _ => "城外資源"
    };

    /// <summary>
    /// 顯示名稱：建築表的中文名稱取斜線前（「神殿/教堂」→神殿）；營、村的廚房是磨坊、鞣革場是獵屋、神殿是祭壇
    /// （與第 08-11 章等級設施的名字、領地圖上的外觀一致）。
    /// </summary>
    public static string Name(string defId, DemoSettlementTier tier)
    {
        var small = tier <= DemoSettlementTier.Village;
        return defId switch
        {
            "BUILD_KITCHEN" when small => "磨坊",
            "BUILD_TANNERY" when small => "獵屋",
            "BUILD_CHURCH" when small => "祭壇",
            _ => DemoBuildingCatalog.Contains(defId) ? DemoBuildingCatalog.Get(defId).Name.Split('/')[0] : defId
        };
    }

    /// <summary>狀態文字：完成（受損時加耐久度）、施工中幾 %、毀損。</summary>
    public static string StateText(DemoBuildingStatus status, int percent) => status switch
    {
        DemoBuildingStatus.Building => $"施工中 {percent}%",
        DemoBuildingStatus.Ruined => "毀損（耐久度歸零）",
        _ when percent < 100 => $"受損（耐久 {percent}%）",
        _ => "完成"
    };

    /// <summary>每棟建築一列，依分區、狀態（完成、施工中、毀損）、名稱排；同一種完好的建築合併。</summary>
    public static IReadOnlyList<DemoHoldingRow> Rows(DemoHoldingScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var rows = new List<DemoHoldingRow>();
        foreach (var b in scene.Holding.Buildings.OrderBy(b => b.Id))
        {
            var percent = b.Status switch
            {
                DemoBuildingStatus.Building => Math.Clamp(b.Progress * 100 / Math.Max(1, b.Def.Hours), 0, 99),
                DemoBuildingStatus.Ruined => 0,
                _ => Math.Clamp(b.Durability / 10, 1, 100)
            };
            var row = new DemoHoldingRow(ZoneOf(b.DefId, scene.Tier), b.DefId, Name(b.DefId, scene.Tier), b.Status, percent, 1, b.Id);
            var same = row.Status == DemoBuildingStatus.Standing && row.Percent == 100
                ? rows.FindIndex(r => r.DefId == row.DefId && r.Status == DemoBuildingStatus.Standing && r.Percent == 100)
                : -1;
            if (same >= 0)
                rows[same] = rows[same] with { Count = rows[same].Count + 1 };
            else
                rows.Add(row);
        }
        return rows.OrderBy(r => r.Zone).ThenBy(r => r.Status switch
            {
                DemoBuildingStatus.Standing => 0,
                DemoBuildingStatus.Building => 1,
                _ => 2
            })
            .ThenBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.BuildingId).ToList();
    }

    /// <summary>住宅一行：棟數、住得下幾人、居民幾人、毀損與正在蓋的。</summary>
    public static string HousingLine(DemoHoldingScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var h = scene.Holding;
        var kind = scene.Tier == DemoSettlementTier.Camp && h.LargeDwellings == 0 ? "棚屋" : "住宅";
        var text = string.Create(CultureInfo.InvariantCulture,
            $"{kind} {h.Dwellings + h.LargeDwellings} 棟" + (h.LargeDwellings > 0 ? $"（大型 {h.LargeDwellings} 棟）" : "")
            + $"，住得下 {h.Housing} 人／居民 {scene.Residents} 人");
        if (scene.Residents > h.Housing)
            text += "，不夠住";
        if (h.RuinedDwellings > 0)
            text += string.Create(CultureInfo.InvariantCulture, $"；毀損 {h.RuinedDwellings} 棟");
        if (scene.HousePercent > 0)
            text += string.Create(CultureInfo.InvariantCulture, $"；正在蓋 1 棟（{scene.HousePercent}%）");
        return text;
    }

    /// <summary>聚落詳情「領地」分頁的文字（介面另畫成領地圖與有圖示的列表）。</summary>
    public static IReadOnlyList<string> Lines(DemoHoldingScene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var h = scene.Holding;
        var inv = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            string.Create(inv, $"建築格 {scene.SlotsUsed}/{scene.Slots}（{DemoSettlementHistory.TierName(scene.Tier)}；住宅、城牆、城門不佔格），同時施工最多 {DemoBuildings.Projects(scene.Tier)} 項"),
            HousingLine(scene),
            string.Create(inv, $"建材庫存：木 {h.Wood}、石 {h.Stone}、鐵 {h.Iron}")
        };
        var near = new List<string>();
        if (scene.Nearby.Forest) near.Add("森林");
        if (scene.Nearby.Hills) near.Add("丘陵");
        if (scene.Nearby.Ore) near.Add("礦脈");
        if (scene.Nearby.Coast) near.Add("海岸");
        if (near.Count > 0)
            lines.Add("附近：" + string.Join("、", near));
        var rows = Rows(scene);
        foreach (var zone in Enum.GetValues<DemoHoldingZone>())
        {
            var inZone = rows.Where(r => r.Zone == zone).ToList();
            if (zone == DemoHoldingZone.Fields && scene.Fields > 0)
            {
                lines.Add($"── {ZoneName(zone)} ──");
                lines.Add(string.Create(inv, $"・附近已開墾的農田 {scene.Fields} 塊"));
            }
            else if (inZone.Count > 0)
                lines.Add($"── {ZoneName(zone)} ──");
            foreach (var r in inZone)
                lines.Add($"・{r.Name}" + (r.Count > 1 ? string.Create(inv, $" ×{r.Count}") : "") + $"：{StateText(r.Status, r.Percent)}");
        }
        if (rows.Count == 0)
            lines.Add("（住宅以外還沒有任何建築）");
        return lines;
    }
}
