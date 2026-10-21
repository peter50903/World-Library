namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-1 章：自然採集世界（人類採集）的「接近承載上限」判斷。結算、生育條件與因果驗證共用這一份。
/// 餘量＝本週聚落實際取用過的自然來源，分配完後還剩下的可採量合計（來源帳 Unharvested）。
/// <list type="bullet">
/// <item>生育：本週沒缺糧、沒飢餓債，而且餘量 ≥ 需求的 <see cref="TempBirthSlackPercent"/>%（糧食寬裕才受孕）——人口逼近上限時出生自然放慢，不會衝過頭再崩潰。</item>
/// <item>擁擠外移：本週沒缺糧、倖存人口 ≥ <see cref="TempCrowdingMinPopulation"/>、餘量 ＜ 需求的 <see cref="TempCrowdingSlackPercent"/>%——
/// 在挨餓之前就派一戶（必須含一對伴侶）去開拓新聚落；這是根因事件，證據是本週來源帳與聚落帳。</item>
/// </list>
/// 三個門檻都是【TEMP】（Claude 定案，登記於 docs/TEMP_待裁決清單.md）。
/// </summary>
public static class DemoCrowding
{
    /// <summary>【TEMP】受孕需要的來源餘量（需求的百分比）。</summary>
    public const long TempBirthSlackPercent = 25;

    /// <summary>【TEMP】低於這個餘量（需求的百分比）視為擁擠。</summary>
    public const long TempCrowdingSlackPercent = 10;

    /// <summary>【TEMP】擁擠外移需要的最少倖存人口（避免分出後兩邊都太小、無法繁衍）。</summary>
    public const long TempCrowdingMinPopulation = 8;

    /// <summary>聚落本週取用過的來源在分配後的剩餘可採量合計。</summary>
    public static long Slack(IReadOnlyList<DemoSourceWeekLedger> sources, int settlementId)
    {
        ArgumentNullException.ThrowIfNull(sources);
        return sources.Where(s => (s.Shares ?? Array.Empty<DemoSourceShare>()).Any(x => x.SettlementId == settlementId && x.Food > 0))
            .Sum(s => s.Unharvested);
    }

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
                ledger.PopulationOpening - ledger.StarvationDeaths, Slack(report.Sources, ledger.SettlementId));
    }
}
