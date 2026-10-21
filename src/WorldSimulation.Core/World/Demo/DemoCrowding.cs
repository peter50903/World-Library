namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-1 章：自然採集世界（人類採集）的「接近承載上限」判斷。結算、生育條件與因果驗證共用這一份。
/// 餘量＝聚落可及範圍內全部自然來源的每週可持續供應，減去本週所有聚落從這些來源採走的量（可為負）；
/// 結算時算好記在聚落週帳 <see cref="DemoSettlementWeekLedger.SourceSlack"/>，讀檔依地圖重算核對。
/// 不用累積存量（未採部分會累積到 TEMP 4 週），否則人口已超過再生量時仍會誤判寬裕、繼續生育，等存量見底才崩潰。
/// <list type="bullet">
/// <item>生育：本週沒缺糧、沒飢餓債，而且餘量 ≥ 需求的 <see cref="TempBirthSlackPercent"/>%（糧食寬裕才受孕）——人口逼近上限時出生自然放慢，不會衝過頭再崩潰。</item>
/// <item>擁擠外移：本週沒缺糧、倖存人口 ≥ <see cref="TempCrowdingMinPopulation"/>、餘量 ＜ 需求的 <see cref="TempCrowdingSlackPercent"/>%——
/// 在挨餓之前就派一戶（必須含一對伴侶）去開拓新聚落；這是根因事件，證據是本週來源帳與聚落帳。</item>
/// </list>
/// 三個門檻都是【TEMP】（Claude 定案，登記於 docs/TEMP_待裁決清單.md）。
/// </summary>
public static class DemoCrowding
{
    /// <summary>【TEMP】受孕需要的來源餘量（需求的百分比）。0＝只要不吃老本（可持續供應 ≥ 本週採收）就可受孕。
    /// 25% 時相鄰聚落共用來源，餘量常不足，吃飽也長期不生育而老化衰退（診斷種子 2）。</summary>
    public const long TempBirthSlackPercent = 0;

    /// <summary>【TEMP】低於這個餘量（需求的百分比）視為擁擠。</summary>
    public const long TempCrowdingSlackPercent = 10;

    /// <summary>【TEMP】擁擠外移需要的最少倖存人口（避免分出後兩邊都太小、無法繁衍）。</summary>
    public const long TempCrowdingMinPopulation = 8;

    /// <summary>【TEMP】存糧目標：需求的週數（第 08-1 章，人類採集世界）。</summary>
    public const long TempStoreTargetWeeks = 4;

    /// <summary>【TEMP】每週為補存糧多採的上限（需求的百分比）。</summary>
    public const long TempStoreTopUpPercent = 50;

    /// <summary>各聚落（非遺跡）可及範圍內全部來源：每週可持續供應 − 本週全部採收（可為負＝正在吃老本）。</summary>
    public static Dictionary<int, long> SlackInReach(DemoHexWorld map, DemoWorldSites sites, IReadOnlyList<DemoSourceWeekLedger> sources)
        => SlackInReach(map, sites.Settlements.Where(s => !s.IsRuin), sources);

    /// <summary>
    /// 第 08-1 章：某一格可及範圍內全部來源的可持續餘量（每週供應 − 本週全部採收）。開拓選址用：
    /// 只選餘量養得起預計開拓隊的空地，並挑餘量最大者，避免新聚落擠在舊聚落旁搶同一批來源。
    /// </summary>
    public static Dictionary<DemoCellRef, long> SpareMap(DemoHexWorld map, IReadOnlyList<DemoSourceWeekLedger> sources)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(sources);
        var spare = new Dictionary<DemoCellRef, long>();
        foreach (var source in sources)
        {
            var add = DemoForagingWorld.Settings.WeeklySupply(source.Kind) - source.Harvested;
            foreach (var c in DemoHexFoodCatchment.EligibleCells(map,
                         new DemoCatchmentFoodSource(source.SourceId, source.Column, source.Row, source.Kind, source.FoodAvailable)))
            {
                var key = new DemoCellRef(c.Column, c.Row);
                spare[key] = checked(spare.GetValueOrDefault(key) + add);
            }
        }
        return spare;
    }

    /// <summary>【TEMP】擁擠分家分出的人數上限＝倖存人口 ÷ 這個除數（缺糧外移仍是一半）。</summary>
    public const long TempCrowdingShareDivisor = 3;

    /// <summary>同上，指定本週開始時存在且不是遺跡的聚落（讀檔重算用）。</summary>
    public static Dictionary<int, long> SlackInReach(DemoHexWorld map, IEnumerable<DemoSettlement> active, IReadOnlyList<DemoSourceWeekLedger> sources)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(sources);
        var settlements = active.ToList();
        var result = settlements.ToDictionary(s => s.Id, _ => 0L);
        foreach (var source in sources)
        {
            var spare = DemoForagingWorld.Settings.WeeklySupply(source.Kind) - source.Harvested;
            var cells = DemoHexFoodCatchment.EligibleCells(map,
                new DemoCatchmentFoodSource(source.SourceId, source.Column, source.Row, source.Kind, source.FoodAvailable));
            foreach (var settlement in settlements.Where(s => cells.Any(c => c.Column == s.Column && c.Row == s.Row)))
                result[settlement.Id] = checked(result[settlement.Id] + spare);
        }
        return result;
    }

    /// <summary>
    /// 第 08-1 章【TEMP 規則】存糧：人類採集世界的聚落每週向來源要求「本週需求＋補存糧」，
    /// 補存糧＝min(需求×50%, 需求×4 週－期初存糧)，不為負。出發的隊伍依人數比例帶走存糧當路糧。
    /// 其他世界只要求本週需求。
    /// </summary>
    public static long Claim(bool storesFood, long need, long foodOpening)
        => !storesFood ? need
            : checked(need + Math.Max(0, Math.Min(need * TempStoreTopUpPercent / 100, need * TempStoreTargetWeeks - foodOpening)));

    /// <summary>糧食寬裕（可以受孕）：沒缺糧，且來源餘量 ≥ 需求 × TEMP 百分比。非共享採集世界沒有來源帳，只看缺糧。</summary>
    public static bool Ample(bool shared, long shortage, long need, long slack)
        => shortage == 0 && (!shared || checked(slack * 100) >= checked(need * TempBirthSlackPercent));

    /// <summary>擁擠（在挨餓前分出一戶開拓）：沒缺糧、人口夠多，但來源餘量 ＜ 需求 × TEMP 百分比。</summary>
    public static bool Crowded(long shortage, long need, long survivors, long slack)
        => shortage == 0 && need > 0 && survivors >= TempCrowdingMinPopulation
        && checked(slack * 100) < checked(need * TempCrowdingSlackPercent);

    /// <summary>從已發布週報判斷某聚落本週是否擁擠（因果驗證用；倖存＝期初－餓死）。</summary>
    public static bool Crowded(DemoWeekReport report, DemoSettlementWeekLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(ledger);
        return report.Sources.Count > 0
            && Crowded(ledger.FoodShortage, ledger.FoodConsumed + ledger.FoodShortage,
                ledger.PopulationOpening - ledger.StarvationDeaths, ledger.SourceSlack);
    }
}
