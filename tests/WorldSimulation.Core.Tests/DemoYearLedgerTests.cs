using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 08 章 b 節：年度圖表的年帳只由已提交週報彙整（<see cref="DemoYearLedger"/>），
/// 必須與無畫面長測（CLI 年報，<see cref="DemoCenturyRun"/>）逐年逐欄相同；進行中的年份也合法對帳；彙整不改世界。
/// </summary>
public sealed class DemoYearLedgerTests
{
    public static IEnumerable<object[]> Cases()
    {
        yield return new object[] { 42UL, "normal", 3 };
        yield return new object[] { 42UL, "local-famine", 3 };
        yield return new object[] { 7UL, "frontier", 5 };
        yield return new object[] { 5UL, "living", 5 };
    }

    private static DemoWeeklyTempConfig Config(string key) => key switch
    {
        "normal" => DemoWeeklyTempConfig.Sample,
        "local-famine" => DemoWeeklyTempConfig.LocalFamineSample,
        "frontier" => DemoWeeklyTempConfig.FrontierSample,
        "living" => DemoWeeklyTempConfig.LivingForagingSample,
        _ => throw new ArgumentOutOfRangeException(nameof(key))
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void YearLedgerFromWeeksEqualsHeadlessYearReport(ulong seed, string preset, int years)
    {
        var config = Config(preset);
        var report = DemoCenturyRun.Run(seed, years, TestCalendar.DaysPerYear, config);
        Assert.Empty(report.ConservationErrors);

        var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        for (var d = 0; d < years * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var ledger = DemoYearLedger.FromWeeks(world.WeekReports(), TestCalendar.DaysPerYear, world.Snapshot().DaysElapsed);

        Assert.Equal(report.Years.Count, ledger.Count);
        for (var i = 0; i < ledger.Count; i++)
        {
            Assert.Equal(report.Years[i], ledger[i]);
            Assert.Equal(report.Years[i].Describe(), ledger[i].Describe());
            Assert.True(DemoYearLedger.IsComplete(ledger[i], TestCalendar.DaysPerYear));
        }
    }

    [Fact]
    public void YearInProgressIsListedAndBalances()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(5), DemoWeeklyTempConfig.LivingForagingSample);
        var days = TestCalendar.DaysPerYear + 40;
        for (var d = 0; d < days; d++)
            world.AdvanceOneDay();
        var before = world.ExportTempSave();

        var ledger = DemoYearLedger.FromWeeks(world.WeekReports(), TestCalendar.DaysPerYear, days);
        Assert.Equal(2, ledger.Count);
        Assert.True(DemoYearLedger.IsComplete(ledger[0], TestCalendar.DaysPerYear));
        Assert.False(DemoYearLedger.IsComplete(ledger[1], TestCalendar.DaysPerYear));
        Assert.Equal(days, ledger[1].LastDay);
        Assert.Equal(TestCalendar.DaysPerYear + 1, ledger[1].FirstDay);
        Assert.All(ledger, y => Assert.Empty(y.Problems()));
        // 年與年銜接：今年期初＝去年期末；期末＝目前世界狀態。
        Assert.Equal(ledger[0].PopulationClosing, ledger[1].PopulationOpening);
        Assert.Equal(ledger[0].FoodClosing, ledger[1].FoodOpening);
        Assert.Equal(world.WorldPopulation, ledger[1].PopulationClosing);
        Assert.Equal(world.PopulationInTransit, ledger[1].PopulationInTransit);
        Assert.Equal(before, world.ExportTempSave());
    }

    [Fact]
    public void NoWeeksGivesNoYears()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.Sample);
        Assert.Empty(DemoYearLedger.FromWeeks(world.WeekReports(), TestCalendar.DaysPerYear, 0));
        for (var d = 0; d < 6; d++)
            world.AdvanceOneDay();
        Assert.Empty(DemoYearLedger.FromWeeks(world.WeekReports(), TestCalendar.DaysPerYear, 6));
    }
}
