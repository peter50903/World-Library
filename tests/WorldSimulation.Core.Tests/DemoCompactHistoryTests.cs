using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-14 章第 3 節：遊戲中壓縮舊週報——世界照樣運轉、指紋與存檔相同，只是不再保留全部週報。</summary>
public sealed class DemoCompactHistoryTests
{
    [Fact]
    public void CompactedWorld_MatchesFullWorld_AndKeepsOnlyRecentWeeks()
    {
        var full = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var compact = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample) { CompactHistory = true };
        for (var d = 0; d < 6 * TestCalendar.DaysPerYear; d++)
        {
            full.AdvanceOneDay();
            compact.AdvanceOneDay();
        }

        Assert.True(compact.WeekReports().Count < full.WeekReports().Count);
        Assert.True(compact.FirstRetainedWeek > 1);
        Assert.Equal(full.CommittedWeeks, compact.CommittedWeeks);
        Assert.Equal(full.StateFingerprint(), compact.StateFingerprint());
        Assert.Equal(full.ExportTempSave(), compact.ExportTempSave());
        Assert.Equal(full.YearSummaries().Select(y => y.ToString()), compact.YearSummaries().Select(y => y.ToString()));
        Assert.Equal(full.SettlementEvents().Select(e => e.ToString()), compact.SettlementEvents().Select(e => e.ToString()));

        // 壓縮後繼續跑、存讀檔都一樣。
        var loaded = DemoObserverWorld.ImportTempSave(compact.ExportTempSave());
        for (var d = 0; d < 60; d++)
        {
            full.AdvanceOneDay();
            compact.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(full.StateFingerprint(), compact.StateFingerprint());
        Assert.Equal(full.StateFingerprint(), loaded.StateFingerprint());
    }
}
