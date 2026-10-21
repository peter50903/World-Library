using System.Text.Json;
using WorldSimulation.Core.Config;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 07 章 c 節：TEMP 聚落參數（config/demo/settlement-temp.json）。</summary>
public sealed record DemoSettlementTempSettings(int SchemaVersion, string Status, int RuinAfterEmptyWeeks)
{
    public static DemoSettlementTempSettings Current { get; } = Load();

    private static DemoSettlementTempSettings Load()
    {
        var settings = JsonSerializer.Deserialize<DemoSettlementTempSettings>(OfficialConfig.ReadDemoText("settlement-temp.json"))
            ?? throw new InvalidOperationException("config/demo/settlement-temp.json 讀不到。");
        if (settings.SchemaVersion != 1 || settings.Status != "TEMP" || settings.RuinAfterEmptyWeeks < 1)
            throw new InvalidOperationException("config/demo/settlement-temp.json 內容不合法。");
        return settings;
    }

    /// <summary>
    /// 聚落在第 <paramref name="week"/> 週是否滿足成為遺跡的條件：本週期末人口 0，且前面連續 RuinAfterEmptyWeeks－1 週
    /// 已發布週帳的期末人口也都是 0（聚落當時必須已存在）。純函式，只讀已發布週報。
    /// </summary>
    public bool EmptyLongEnough(int settlementId, int week, long closingThisWeek, IReadOnlyList<DemoWeekReport> published)
    {
        if (closingThisWeek != 0) return false;
        for (var w = week - 1; w > week - RuinAfterEmptyWeeks; w--)
        {
            if (w < 1) return false;
            var report = published.Count >= w && published[w - 1].Week == w ? published[w - 1] : published.FirstOrDefault(r => r.Week == w);
            var ledger = report?.Settlements.FirstOrDefault(l => l.SettlementId == settlementId);
            if (ledger is null || ledger.PopulationClosing != 0) return false;
        }
        return true;
    }
}
