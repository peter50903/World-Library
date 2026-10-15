using WorldSimulation.Core.Config;
using WorldSimulation.Core.Time;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// C08（P1.0.02）：世界曆法在 Core，月份天數從 01 表 WORLD_MONTH_DAYS 讀；公曆平年、無閏年（定案）。
/// 正例用真實 01 表；反例用最小測試表，訊息須含對應參數 ID。
/// </summary>
public sealed class WorldCalendarRulesTests
{
    private const string Header = "ParameterID,分類,參數名稱,值,資料型別,單位,說明,Enabled\n";
    private const string Days = "WORLD_DAYS_PER_YEAR,時間,一年天數,365,int,日,說明,TRUE\n";
    private const string Months = "WORLD_MONTHS_PER_YEAR,時間,一年月份,12,int,月,說明,TRUE\n";
    private const string Feb = "WORLD_FEB_DAYS,時間,二月天數,28,int,日,說明,TRUE\n";
    private const string MonthDays = "WORLD_MONTH_DAYS,時間,各月天數,31|28|31|30|31|30|31|31|30|31|30|31,enum_list,日,說明,TRUE\n";

    private static WorldCalendarRules Rules => TestCalendar.Rules;

    private static WorldCalendarRules FromText(string rows)
    {
        var result = ConfigRegistry.ValidateAndCreate(ParameterSheet.Parse(Header + rows, "測試.csv"));
        if (!result.IsValid)
            throw new WorldCalendarException($"驗證失敗：{result.Errors[0].ParameterId}：{result.Errors[0].Reason}");
        return WorldCalendarRules.FromRegistry(result.Registry!, "測試.csv");
    }

    [Theory]
    [InlineData(1, "第 1 年 1 月 1 日")]
    [InlineData(31, "第 1 年 1 月 31 日")]
    [InlineData(32, "第 1 年 2 月 1 日")]
    [InlineData(59, "第 1 年 2 月 28 日")]
    [InlineData(60, "第 1 年 3 月 1 日")]
    [InlineData(181, "第 1 年 6 月 30 日")]
    [InlineData(334, "第 1 年 11 月 30 日")]
    [InlineData(365, "第 1 年 12 月 31 日")]
    [InlineData(366, "第 2 年 1 月 1 日")]
    [InlineData(730, "第 2 年 12 月 31 日")]
    [InlineData(36500, "第 100 年 12 月 31 日")]
    public void OfficialSheetFormatsCommonYearDates(int day, string expected)
        => Assert.Equal(expected, Rules.Format(day));

    [Fact]
    public void OfficialSheetHasCommonYearMonthsAndDescribesThem()
    {
        Assert.Equal(new[] { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 }, Rules.MonthDays);
        Assert.Equal(365, Rules.DaysPerYear);
        Assert.Contains("各月 31/28/31/30/31/30/31/31/30/31/30/31（WORLD_MONTH_DAYS）", WorldCalendarConfig.LoadDefault().Describe());
        Assert.Equal("開局（第 1 年 1 月 1 日之前）", Rules.Format(0));
    }

    [Fact]
    public void EveryDayOfTwoYearsIsCountedExactlyOnce()
    {
        var previous = new WorldDate(0, 12, 31, 0);
        var daysInMonth = new Dictionary<(int Year, int Month), int>();
        for (var d = 1; d <= 730; d++)
        {
            var date = Rules.ToDate(d);
            // 嚴格遞增、無重複無跳號：同月日加 1，或換到下一月 1 日，或換到下一年 1 月 1 日。
            var sameMonth = date.Year == previous.Year && date.Month == previous.Month && date.Day == previous.Day + 1;
            var nextMonth = date.Year == previous.Year && date.Month == previous.Month + 1 && date.Day == 1;
            var nextYear = date.Year == previous.Year + 1 && date.Month == 1 && date.Day == 1;
            Assert.True(sameMonth || nextMonth || nextYear, $"第 {d} 日 {date} 接在 {previous} 後面不連續");
            Assert.Equal((d - 1) % 365 + 1, date.DayOfYear);
            daysInMonth[(date.Year, date.Month)] = daysInMonth.GetValueOrDefault((date.Year, date.Month)) + 1;
            previous = date;
        }

        foreach (var year in new[] { 1, 2 })
        {
            Assert.Equal(12, daysInMonth.Keys.Count(k => k.Year == year));
            for (var m = 1; m <= 12; m++)
                Assert.Equal(Rules.MonthDays[m - 1], daysInMonth[(year, m)]);
        }
    }

    [Fact]
    public void WeeklySettlementsBelongToTheYearOfTheirDay()
    {
        Assert.Equal((1, 1), Rules.WeeklySettlementInYear(1));
        Assert.Equal((1, 52), Rules.WeeklySettlementInYear(52));
        Assert.Equal((2, 1), Rules.WeeklySettlementInYear(53));

        // 與 SomeYearsHave53WeeklySettlementsAndNothingIsLostAcrossYears 一致：前 8 年 52×6、53、52。
        var perYear = Enumerable.Range(1, DemoCenturyRun.WeeklySettlementsThrough(8 * 365))
            .Select(w => Rules.WeeklySettlementInYear(w))
            .GroupBy(x => x.Year)
            .Select(g => g.Count());
        Assert.Equal(new[] { 52, 52, 52, 52, 52, 52, 53, 52 }, perYear);
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules.WeeklySettlementInYear(0));
    }

    [Fact]
    public void YearlyReportsEndOnDecember31()
    {
        var report = DemoCenturyRun.Run(42, 2, Rules.DaysPerYear);
        Assert.Equal(new[] { "第 1 年 12 月 31 日", "第 2 年 12 月 31 日" }, report.Years.Select(y => Rules.Format(y.LastDay)));
    }

    [Fact]
    public void SameSeedGivesTheSameDatedYearLines()
    {
        string Lines() => string.Join("\n", DemoCenturyRun.Run(42, 3, Rules.DaysPerYear).Years
            .Select(y => $"{y.Describe()}｜年末 {Rules.Format(y.LastDay)}"));
        Assert.Equal(Lines(), Lines());
    }

    [Fact]
    public void ConvertingDatesNeverChangesTheWorld()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.LocalFamineSample);
        for (var i = 0; i < 40; i++)
            world.AdvanceOneDay();
        var before = world.StateFingerprint();
        var day = (int)world.Snapshot().WorldTime.TotalDays;
        for (var i = 0; i < 1000; i++)
        {
            Rules.ToDate(day);
            Rules.Format(day);
        }

        Assert.Equal(before, world.StateFingerprint());
        Assert.Equal("第 1 年 2 月 9 日", Rules.Format(day));
    }

    [Fact]
    public void SavedAndLoadedWorldShowsTheSameDate()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.Sample);
        for (var i = 0; i < 9; i++)
            world.AdvanceOneDay();
        var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(
            Rules.Format((int)world.Snapshot().WorldTime.TotalDays),
            Rules.Format((int)loaded.Snapshot().WorldTime.TotalDays));
        Assert.Equal("第 1 年 1 月 9 日", Rules.Format((int)loaded.Snapshot().WorldTime.TotalDays));
    }

    [Fact]
    public void NegativeDaysAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules.ToDate(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules.ToDate(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rules.Format(-1));
    }

    [Fact]
    public void MinimalSheetWithAllFourParametersWorks()
        => Assert.Equal("第 1 年 3 月 1 日", FromText(Days + Months + Feb + MonthDays).Format(60));

    public static IEnumerable<object[]> BadMonthSheets() => new[]
    {
        new object[] { Days + Months + Feb, "WORLD_MONTH_DAYS" },
        new object[] { Days + Months + Feb + "WORLD_MONTH_DAYS,時間,各月天數,31|28|31|30|31|30|31|31|30|31|30,enum_list,日,說明,TRUE\n", "WORLD_MONTH_DAYS" },
        new object[] { Days + Months + Feb + "WORLD_MONTH_DAYS,時間,各月天數,31|28|31|30|31|30|31|31|30|31|30|32,enum_list,日,說明,TRUE\n", "WORLD_MONTH_DAYS" },
        new object[] { Days + Months + Feb + "WORLD_MONTH_DAYS,時間,各月天數,31|29|31|30|31|30|31|31|30|31|30|30,enum_list,日,說明,TRUE\n", "WORLD_FEB_DAYS" },
        new object[] { Days + Months + Feb + "WORLD_MONTH_DAYS,時間,各月天數,31|28|31|30|31|30|31|31|30|31|61|0,enum_list,日,說明,TRUE\n", "WORLD_MONTH_DAYS" },
        new object[] { Days + Months + Feb + "WORLD_MONTH_DAYS,時間,各月天數,31|28|31|30|31|30|31|31|30|31|三十|31,enum_list,日,說明,TRUE\n", "WORLD_MONTH_DAYS" },
        new object[] { Days + Months + Feb + "WORLD_MONTH_DAYS,時間,各月天數,31|28|31|30|31|30|31|31|30|31|30|31,enum_list,日,說明,FALSE\n", "WORLD_MONTH_DAYS" },
        new object[] { Days + Feb + MonthDays, "WORLD_MONTHS_PER_YEAR" },
        new object[] { Days + Months + MonthDays, "WORLD_FEB_DAYS" },
    };

    [Theory]
    [MemberData(nameof(BadMonthSheets))]
    public void InconsistentMonthSettingsFailWithTheirId(string rows, string id)
    {
        var core = Assert.Throws<WorldCalendarException>(() => FromText(rows));
        Assert.Contains(id, core.Message);
        var cli = Assert.Throws<WorldCalendarException>(() => WorldCalendarConfig.Parse(Header + rows, "測試.csv"));
        Assert.Contains(id, cli.Message);
    }

    [Fact]
    public void M1CatalogCsvStillReportsUnclosedQuotesInItsOwnWords()
    {
        var ex = Assert.Throws<M1CatalogException>(() => M1CheckCatalog.ParseCsv("a,\"b\n", "清單.csv"));
        Assert.Equal("M1 驗收清單有未關閉的引號：清單.csv", ex.Message);
        Assert.Equal(new[] { "a", "b,c" }, M1CheckCatalog.ParseCsv("a,\"b,c\"\r\n", "清單.csv")[0]);
    }
}
