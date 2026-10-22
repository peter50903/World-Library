namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// C07（P1.0.01-b）：觀察者點格查詢的唯讀投影（44E MOD_QUERY）。
/// 每次查詢都由世界現況重算，不快取、不改權威狀態、不消耗亂數；Godot 與 CLI 只顯示 <see cref="Lines"/>。
/// 觀察者可看世界真相（01 OBSERVER_WORLD_TRUTH），本查詢不做角色知識過濾。
/// </summary>
/// <param name="Cell">被查詢的 TEMP 六角格。</param>
/// <param name="IsMainRiver">是否為主河道格（只在共享採集情境為 true）。</param>
/// <param name="SharedSource">此格若是共享採集來源（森林／河道），為該來源；否則 null。</param>
/// <param name="FoodReachCells">共享來源的可採集範圍（畫面黃框）；非來源格為空。</param>
/// <param name="Migrants">目前站在此格的遷徙隊伍。</param>
/// <param name="SettlementId">此格聚落的 ID；沒有聚落為 null。</param>
/// <param name="Lines">資訊框完整文字，逐行；以 "\n" 串接即為畫面顯示內容。</param>
public sealed record DemoCellView(
    DemoHexCell Cell,
    bool IsMainRiver,
    DemoCatchmentFoodSource? SharedSource,
    IReadOnlyList<DemoCellRef> FoodReachCells,
    IReadOnlyList<DemoMigrationBatch> Migrants,
    int? SettlementId,
    IReadOnlyList<string> Lines)
{
    /// <summary>資訊框顯示文字（Godot Label 與 CLI `cell` 指令共用，逐字相同）。</summary>
    public string Text => string.Join("\n", Lines);
}

/// <summary>C07：點格查詢的文字組裝（TEMP 中文名稱集中在此，Godot 不再自行拼世界數字）。</summary>
public static class DemoCellQuery
{
    /// <summary>TEMP 資源點中文名稱。</summary>
    public static string ResourceName(DemoResourceKind kind) => kind switch
    {
        DemoResourceKind.Woodland => "林地（TEMP）",
        DemoResourceKind.Fishery => "漁場（TEMP）",
        DemoResourceKind.Uncultivated => "未開墾草原（農業產量 0，TEMP）",
        _ => "未定義（TEMP）"
    };

    /// <summary>第 06 章 a 節：「路線：東 野地（成本 3）、西 渡口（成本 4）…」或「路線：無（不可通行）」。</summary>
    public static string RoutesLine(DemoHexWorld map, DemoCellRef cell)
    {
        var exits = map.Routes.Exits(map, cell);
        var ford = map.Routes.Ford == cell ? "｜此格為淺灘渡口（可涉水過河）" : "";
        return exits.Count == 0
            ? $"路線：無（不可通行）{ford}"
            : $"路線：{string.Join("、", exits.Select(e => $"{e.Direction} {DemoRouteNetwork.KindName(e.Segment.Kind)}（成本 {e.Segment.Cost}）"))}{ford}";
    }

    /// <summary>第 08 章 a 節：土地開發狀態中文名稱。</summary>
    public static string LandName(DemoLandDevelopment state) => state switch
    {
        DemoLandDevelopment.Clearing => "開墾中",
        DemoLandDevelopment.Farmland => "農田",
        _ => "未開發"
    };

    /// <summary>TEMP 地形中文名稱。</summary>
    public static string TerrainName(DemoTerrain terrain) => terrain switch
    {
        DemoTerrain.Grassland => "草原（TEMP）",
        DemoTerrain.Forest => "森林（TEMP）",
        DemoTerrain.Water => "水域（TEMP）",
        _ => "未定義（TEMP）"
    };

    /// <summary>第 06-1 章：依格子區分海洋（深水）、河流、河流淺灘。</summary>
    public static string TerrainName(DemoHexCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (cell.Terrain != DemoTerrain.Water) return TerrainName(cell.Terrain);
        if (cell.Sea) return "海洋（深水，TEMP）";
        return cell.Shallow ? "河流淺灘（可涉水過河，TEMP）" : "河流（TEMP）";
    }

    /// <summary>
    /// 只呼叫世界的唯讀成員組出點格投影。越界由 <see cref="DemoHexWorld.GetCell"/> 拋
    /// <see cref="ArgumentOutOfRangeException"/>。文字與順序照搬 C07 前 Godot `OnCellSelected`。
    /// </summary>
    internal static DemoCellView Build(DemoObserverWorld world, int column, int row)
    {
        var cell = world.Map.GetCell(column, row);
        var isMainRiver = world.IsMainRiverCell(cell.Column, cell.Row);
        var lines = new List<string>
        {
            $"TEMP 六角格｜偏移座標 ({cell.Column}, {cell.Row})",
            $"軸向座標 ({cell.AxialQ}, {cell.AxialR})",
            $"核心地形：{TerrainName(cell)}"
                + (world.UsesSharedForaging && cell.Terrain == DemoTerrain.Grassland ? "（未開墾，農田糧食 0）" : "")
                + (cell.MainWaterway ? "（主水道，禁止定居" + (world.UsesSharedForaging ? "，鄰格漁撈）" : "）") : ""),
            // 第 06 章 a 節：出入口由路線段衍生（相鄰但沒有路線段＝不可通行）。
            RoutesLine(world.Map, new DemoCellRef(cell.Column, cell.Row))
        };
        // 第 08 章 a 節：土地開發狀態（與地形分離；未開發不另列）。
        if (world.Land.ParcelAt(new DemoCellRef(cell.Column, cell.Row)) is { } parcel)
            lines.Add($"土地開發：{LandName(parcel.State)}（開發程度 {parcel.DevelopmentPercent}%；農糧產量待第 11 章農業知識）");

        DemoCatchmentFoodSource? sharedSource = null;
        IReadOnlyList<DemoCellRef> reach = Array.Empty<DemoCellRef>();
        if (world.UsesSharedForaging)
        {
            sharedSource = world.SharedFoodSources().FirstOrDefault(s =>
                s.Column == cell.Column && s.Row == cell.Row);
            if (sharedSource is not null)
            {
                reach = Array.AsReadOnly(DemoHexFoodCatchment.EligibleCells(world.Map, sharedSource)
                    .Select(c => new DemoCellRef(c.Column, c.Row)).ToArray());
                lines.Add($"共享來源：每週供糧上限 {sharedSource.FoodAvailable}（全圈共用一次）");
            }
        }

        // V0-06A：站在此格的遷徙隊伍（來自 Core；畫面不移動隊伍）。
        var migrants = world.MigrationsInTransit()
            .Where(b => b.Cell.Column == cell.Column && b.Cell.Row == cell.Row)
            .ToArray();
        foreach (var batch in migrants)
        {
            lines.Add($"遷徙隊伍 #{batch.Id}：{batch.People} 人，{batch.FromName}→{batch.ToName}，" +
                $"還有 {batch.StepsLeft} 格，攜帶糧食 {batch.Food}，飢餓債 {batch.HungerDebt}");
        }

        var settlement = world.Sites.SettlementAt(cell.Column, cell.Row);
        if (settlement is not null)
        {
            lines.Add($"聚落：{settlement.Name}（實體 ID {settlement.Id}）");
            if (settlement.IsFounded)
            {
                // V0-06B：遷徙者建立的聚落（人口只來自開拓隊）。
                var source = world.Sites.Settlements.FirstOrDefault(s => s.Id == settlement.FoundedFromSettlementId);
                lines.Add($"第 {settlement.FoundedWeek} 週由遷徙隊伍 #{settlement.FoundedByBatchId} 建立；" +
                    $"來源聚落：{source?.Name ?? "未知"}（人口全部來自開拓隊，不憑空）");
            }
            foreach (var resource in world.Sites.ResourcePointsOf(settlement.Id))
                lines.Add($"資源點 #{resource.Id}：{ResourceName(resource.Kind)}");
            foreach (var pop in world.Sites.PopGroupsOf(settlement.Id))
                lines.Add($"人口群體 #{pop.Id}：初始 {pop.Size} 人（TEMP）");
            var population = world.SettlementPopulation(settlement.Id);
            lines.Add($"目前人口：{population}");
            lines.Add($"糧食庫存：{world.SettlementFood(settlement.Id)}");
            lines.Add(world.IsStarving(settlement.Id)
                ? $"飢餓狀態：飢餓中（飢餓債 {world.SettlementHungerDebt(settlement.Id)}，最近一週缺糧 {world.LatestWeekShortage(settlement.Id)}）"
                : population == 0 ? "飢餓狀態：聚落已無人口" : "飢餓狀態：正常");
            // 第 07-1 章：人口結構與代表人物。
            if (world.PopulationStats(settlement.Id) is { } st && st.Total > 0)
                lines.Add($"人口結構：兒童 {st.Children}｜成年 {st.Adults}｜老人 {st.Elders}｜育齡伴侶約 {st.Couples} 對｜懷孕中 {st.Pregnant}");
            var day = world.Snapshot().DaysElapsed;
            foreach (var r in world.RepresentativesOf(settlement.Id))
                lines.Add($"代表人物：{DemoRepresentative.RoleName(r.Role)} {r.Name}（{(r.Sex == DemoTempSex.Female ? "女" : "男")}，{DemoCohortDemography.AgeAt(r.BirthYear, day)} 歲）");
            lines.AddRange(DemoSettlementHistory.Lines(world, settlement.Id)); // 第 08-3 章：建立年數與聚落小史
        }

        return new DemoCellView(cell, isMainRiver, sharedSource, reach,
            Array.AsReadOnly(migrants), settlement?.Id, Array.AsReadOnly(lines.ToArray()));
    }
}
