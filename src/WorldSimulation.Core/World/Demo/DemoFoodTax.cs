namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-19g 章（使用者 2026-11-15：上級聚集地可以向下級聚集地徵收糧食稅金，如鎮可以向村徵集）：
/// 每週結算的最後，隸屬於上級的聚落把期末存糧超過「留底」的部分繳 <see cref="TempTaxPercent"/>% 給直屬上級。
/// <list type="bullet">
/// <item>留底＝期末人口 × 每人每週糧食 × <see cref="TempKeepWeeks"/> 週：下級不會因繳稅而挨餓。</item>
/// <item>只繳給直屬上級（村→鎮、鎮→城）；上級收到的糧照常存進自己的糧庫（下一週才再往上繳）。</item>
/// <item>上級已是遺跡或沒人時不收。全世界的糧食總量不變（只是移轉），週帳多兩欄：繳稅、收稅。</item>
/// </list>
/// </summary>
public static class DemoFoodTax
{
    /// <summary>【TEMP】糧稅率（%）。</summary>
    public const long TempTaxPercent = 10;

    /// <summary>【TEMP】下級留底的週數（不繳稅的存糧）。</summary>
    public const long TempKeepWeeks = 8;

    /// <summary>這一週該繳的稅（無條件捨去）。</summary>
    public static long Due(long foodClosing, long populationClosing, long foodPerPersonPerWeek)
    {
        var keep = checked(populationClosing * foodPerPersonPerWeek * TempKeepWeeks);
        return foodClosing <= keep ? 0 : checked(foodClosing - keep) * TempTaxPercent / 100;
    }

    /// <summary>依期末存糧收稅：改寫週帳（繳稅、收稅、期末存糧）與下週的期初存糧。</summary>
    public static void Collect(List<DemoSettlementWeekLedger> ledgers, IDictionary<int, long> nextFood,
        Func<int, int?> liegeOf, long foodPerPersonPerWeek)
    {
        ArgumentNullException.ThrowIfNull(ledgers);
        ArgumentNullException.ThrowIfNull(nextFood);
        ArgumentNullException.ThrowIfNull(liegeOf);
        var index = new Dictionary<int, int>();
        for (var i = 0; i < ledgers.Count; i++)
            index[ledgers[i].SettlementId] = i;
        var due = new List<(int Vassal, int Liege, long Food)>();
        foreach (var ledger in ledgers.OrderBy(l => l.SettlementId))
        {
            if (liegeOf(ledger.SettlementId) is not { } liege || liege == ledger.SettlementId
                || !index.TryGetValue(liege, out var at) || ledgers[at].PopulationClosing <= 0)
                continue;
            var tax = Due(ledger.FoodClosing, ledger.PopulationClosing, foodPerPersonPerWeek);
            if (tax > 0)
                due.Add((ledger.SettlementId, liege, tax));
        }
        foreach (var (vassal, liege, food) in due)
        {
            var v = index[vassal];
            ledgers[v] = ledgers[v] with { FoodTaxPaid = checked(ledgers[v].FoodTaxPaid + food), FoodClosing = ledgers[v].FoodClosing - food };
            nextFood[vassal] = ledgers[v].FoodClosing;
            var l = index[liege];
            ledgers[l] = ledgers[l] with { FoodTaxReceived = checked(ledgers[l].FoodTaxReceived + food), FoodClosing = checked(ledgers[l].FoodClosing + food) };
            nextFood[liege] = ledgers[l].FoodClosing;
        }
    }
}
