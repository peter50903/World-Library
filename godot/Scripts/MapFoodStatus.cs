using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>第 05 章 b 節：「糧食狀況」地圖模式的聚落分級（只讀 Core 最近一週聚落帳）。</summary>
public enum FoodStatus
{
    /// <summary>還沒有任何週結算。</summary>
    Unsettled,
    Ample,
    Tight,
    Starving
}

/// <summary>
/// 第 05 章 b 節：糧食狀況分級。純 C#，不引用 Godot，可單元測試；不改世界。
/// 飢餓＝本週缺糧或仍有飢餓債；緊張＝期末糧食撐不到 TEMP 週數；其餘充足。門檻是 TEMP 顯示參數。
/// </summary>
public static class MapFoodStatus
{
    /// <summary>TEMP：期末糧食少於「本週消耗 × 這個週數」視為緊張。</summary>
    public const int TempTightWeeks = 2;

    public static FoodStatus Classify(DemoSettlementWeekLedger? ledger)
    {
        if (ledger is null)
            return FoodStatus.Unsettled;
        if (ledger.FoodShortage > 0 || ledger.HungerDebtClosing > 0)
            return FoodStatus.Starving;
        if (ledger.FoodConsumed > 0 && ledger.FoodClosing < ledger.FoodConsumed * TempTightWeeks)
            return FoodStatus.Tight;
        return FoodStatus.Ample;
    }

    /// <summary>聚落 ID → 分級；沒有帳的聚落（例如本週剛建立）不列入，畫面視為未結算。</summary>
    public static IReadOnlyDictionary<int, FoodStatus> ForWeek(IEnumerable<DemoSettlementWeekLedger>? ledgers)
    {
        var result = new Dictionary<int, FoodStatus>();
        if (ledgers is null)
            return result;
        foreach (var ledger in ledgers)
            result[ledger.SettlementId] = Classify(ledger);
        return result;
    }

    public static string Label(FoodStatus status) => status switch
    {
        FoodStatus.Ample => "充足",
        FoodStatus.Tight => "緊張",
        FoodStatus.Starving => "飢餓",
        _ => "尚未結算"
    };
}
