using WorldSimulation.Core.Time;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>#82：測試共用的正式曆法（從程式倉庫 config/sheets/01_世界參數.csv 讀，不在測試裡另寫年長）。</summary>
internal static class TestCalendar
{
    private static readonly Lazy<WorldCalendar> Calendar = new(WorldCalendarConfig.LoadDefault);

    public static int DaysPerYear => Calendar.Value.DaysPerYear;

    /// <summary>C08：Core 曆法換算（同一份 01 表）。</summary>
    public static WorldCalendarRules Rules => Calendar.Value.Rules;
}

/// <summary>
/// #82：正式 365 日年接入。曆法從 01 表讀取並驗證；年報依實際發生的週結算匯總，
/// 週結算每 7 日一次、跨年不重置；年界前後（364、365、366、370、371 日）不漏、不重複、不跳日。
/// </summary>
public sealed class WorldCalendarTests
{
    private const string Header = "ParameterID,分類,參數名稱,值,資料型別,單位,說明,Enabled\n";

    [Fact]
    public void OfficialWorldParametersGiveA365DayYear()
    {
        var calendar = WorldCalendarConfig.LoadDefault();
        Assert.Equal(365, calendar.DaysPerYear); // Notion 02 §B 已定案：一年 365 日、不設閏年。
        Assert.EndsWith("01_世界參數.csv", calendar.Source.Replace('\\', '/'));
        Assert.Contains("WORLD_DAYS_PER_YEAR＝365", calendar.Describe());
    }

    [Theory]
    [InlineData("WORLD_DAYS_PER_YEAR,時間,一年天數,365,int,日,說明,FALSE\n", "沒有已啟用的 WORLD_DAYS_PER_YEAR")]
    [InlineData("OTHER,時間,其他,1,int,日,說明,TRUE\n", "沒有已啟用的 WORLD_DAYS_PER_YEAR")]
    [InlineData("WORLD_DAYS_PER_YEAR,時間,一年天數,三百,int,日,說明,TRUE\n", "驗證失敗")]
    [InlineData("WORLD_DAYS_PER_YEAR,時間,一年天數,365,float,日,說明,TRUE\n", "驗證失敗")]
    [InlineData("WORLD_DAYS_PER_YEAR,時間,一年天數,0,int,日,說明,TRUE\n", "必須是正整數")]
    [InlineData("WORLD_DAYS_PER_YEAR,時間,一年天數,365,enum,日,說明,TRUE\n", "資料型別必須是 int")]
    [InlineData("WORLD_DAYS_PER_YEAR,時間,一年天數,365,int,日,說明,yes\n", "Enabled 必須是 TRUE 或 FALSE")]
    [InlineData("WORLD_DAYS_PER_YEAR,時間,一年天數,365,int,日,說明,TRUE\nWORLD_DAYS_PER_YEAR,時間,一年天數,364,int,日,說明,TRUE\n", "驗證失敗")]
    public void MissingDisabledOrMalformedYearLengthFailsClearly(string rows, string expected)
    {
        var ex = Assert.Throws<WorldCalendarException>(() => WorldCalendarConfig.Parse(Header + rows, "測試.csv"));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void MissingColumnOrFileFailsClearly()
    {
        var ex = Assert.Throws<WorldCalendarException>(
            () => WorldCalendarConfig.Parse("ParameterID,值,Enabled\nWORLD_DAYS_PER_YEAR,365,TRUE\n", "測試.csv"));
        Assert.Contains("缺少欄位「資料型別」", ex.Message);
        Assert.Throws<WorldCalendarException>(() => WorldCalendarConfig.Load("/nonexistent/01_世界參數.csv"));
    }

    [Fact]
    public void YearBoundariesAre365730And36500()
    {
        var days = TestCalendar.DaysPerYear;
        var one = DemoCenturyRun.Run(42, 1, days);
        Assert.Equal(365, one.Years[0].LastDay);
        var two = DemoCenturyRun.Run(42, 2, days);
        Assert.Equal(new[] { 365, 730 }, two.Years.Select(y => y.LastDay));
        Assert.Equal(new[] { 1, 366 }, two.Years.Select(y => y.FirstDay));
        // 第 364 日的週結算屬第 1 年、第 371 日的屬第 2 年：兩年各 52 次（7…364、371…728）。
        Assert.Equal(new[] { 52, 52 }, two.Years.Select(y => y.Weeks));
        Assert.Empty(two.ConservationErrors);
    }

    [Fact]
    public void SomeYearsHave53WeeklySettlementsAndNothingIsLostAcrossYears()
    {
        var report = DemoCenturyRun.Run(7, 8, TestCalendar.DaysPerYear, DemoWeeklyTempConfig.LocalFamineSample);
        Assert.Empty(report.ConservationErrors);
        // 365＝52×7＋1：第 7 年（第 2191～2555 日）多一次週結算。
        Assert.Equal(new[] { 52, 52, 52, 52, 52, 52, 53, 52 }, report.Years.Select(y => y.Weeks));
        Assert.Equal(DemoCenturyRun.WeeklySettlementsThrough(8 * 365), report.Years.Sum(y => y.Weeks));
    }

    [Fact]
    public void WeeklySettlementAroundTheFirstYearEndIsNeitherSkippedNorRepeated()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.LocalFamineSample);
        var weekAtDay = new Dictionary<int, int>();
        for (var day = 1; day <= 371; day++)
        {
            world.AdvanceOneDay();
            weekAtDay[day] = world.LatestWeek?.Week ?? 0;
        }

        Assert.Equal(52, weekAtDay[364]); // 第 1 年最後一次週結算
        Assert.Equal(52, weekAtDay[365]); // 年末：不額外結算、不重置
        Assert.Equal(52, weekAtDay[366]); // 第 2 年第一天
        Assert.Equal(52, weekAtDay[370]);
        Assert.Equal(53, weekAtDay[371]); // 第 2 年第一次週結算，週次接續
        Assert.Equal(Enumerable.Range(1, 53), world.WeekReports().Select(w => w.Week));
        Assert.Equal(371, (int)world.WeekReports()[^1].WorldTime.TotalDays);

        // 第 1 年年報的期末＝第 365 日世界狀態＝第 52 次週結算後的狀態；第 2 年期初與之相同。
        var report = DemoCenturyRun.Run(42, 2, TestCalendar.DaysPerYear, DemoWeeklyTempConfig.LocalFamineSample);
        var y1 = report.Years[0];
        var y2 = report.Years[1];
        Assert.Equal(y1.PopulationClosing, y2.PopulationOpening);
        Assert.Equal(y1.FoodClosing, y2.FoodOpening);
        Assert.Equal(y1.ReserveClosing, y2.ReserveOpening);
        Assert.Empty(report.ConservationErrors);
    }

    [Fact]
    public void SameSeedAndCalendarGiveIdenticalYearlySummaries()
    {
        var a = DemoCenturyRun.Run(20261009, 10, TestCalendar.DaysPerYear, DemoWeeklyTempConfig.FrontierSample);
        var b = DemoCenturyRun.Run(20261009, 10, TestCalendar.DaysPerYear, DemoWeeklyTempConfig.FrontierSample);
        Assert.Equal(a.Years, b.Years);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-365)]
    public void CoreRejectsNonPositiveYearLength(int daysPerYear)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DemoCenturyRun.Run(42, 1, daysPerYear));
}
