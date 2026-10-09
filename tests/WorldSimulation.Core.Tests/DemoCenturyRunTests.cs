using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// V0-05：百年無畫面長測。3 個固定種子各跑 100 個 TEMP 年（每年 52 週＝364 日），
/// 任何守恆錯誤（資源帳、人口帳、無魔法糧食、死亡無因）即失敗；年度摘要要看得出種子之間的差異。
/// </summary>
public sealed class DemoCenturyRunTests
{
    private static readonly ulong[] Seeds = { 42UL, 7UL, 20261009UL };

    private static void AssertCenturyIsConserved(DemoCenturyReport report, int years)
    {
        Assert.Empty(report.ConservationErrors);
        Assert.Equal(years, report.Years.Count);

        DemoYearSummary? previous = null;
        foreach (var y in report.Years)
        {
            Assert.Empty(y.Problems());
            Assert.Equal(DemoCenturyRun.TempWeeksPerYear, y.Weeks);
            Assert.Equal(y.Year * DemoCenturyRun.TempDaysPerYear, y.LastDay);

            // M1_POP：期初＋出生－死亡＝期末；死亡全部由帶原因的餓死事件對帳。
            Assert.Equal(y.PopulationClosing, y.PopulationOpening + y.Births - y.Deaths);
            Assert.Equal(y.Deaths, y.StarvationDeathsInEvents);

            // M1_RES：資源存量與糧食各自守恆。
            Assert.Equal(y.ReserveClosing, y.ReserveOpening + y.Regenerated - y.Harvested);
            Assert.Equal(y.FoodClosing, y.FoodOpening + y.FoodProduced - y.FoodConsumed);

            // M1_NO_MAGIC_FOOD：糧食只來自實際採收。
            Assert.Equal(y.Harvested, y.FoodProduced);

            Assert.True(y.PopulationClosing >= 0 && y.FoodClosing >= 0 && y.ReserveClosing >= 0);

            if (previous is not null)
            {
                // 年與年之間無縫銜接：上一年期末＝下一年期初。
                Assert.Equal(previous.PopulationClosing, y.PopulationOpening);
                Assert.Equal(previous.FoodClosing, y.FoodOpening);
                Assert.Equal(previous.ReserveClosing, y.ReserveOpening);
            }

            previous = y;
        }
    }

    [Fact]
    public void ThreeFixedSeedsRunOneHundredYearsWithZeroConservationErrorsAndDistinctSummaries()
    {
        var reports = Seeds.Select(seed => DemoCenturyRun.Run(seed, 100)).ToList();

        foreach (var report in reports)
            AssertCenturyIsConserved(report, 100);

        // 年度摘要看得出彼此差異：任兩個種子的 100 行摘要不完全相同。
        var texts = reports.Select(r => string.Join("\n", r.Years.Select(y => y.Describe()))).ToList();
        Assert.Equal(texts.Count, texts.Distinct().Count());

        // 每一行摘要都是玩家可讀的繁體中文，且含人口、糧食、死亡、事件數。
        foreach (var line in reports[0].Years.Select(y => y.Describe()))
        {
            Assert.Contains("人口", line);
            Assert.Contains("糧食", line);
            Assert.Contains("死亡", line);
            Assert.Contains("事件", line);
        }

        Assert.StartsWith("第 100 年（至第 36400 日）", reports[0].Years[^1].Describe());
    }

    [Fact]
    public void SameSeedGivesIdenticalCentury()
    {
        var a = DemoCenturyRun.Run(42, 100);
        var b = DemoCenturyRun.Run(42, 100);
        Assert.Equal(a.Years, b.Years); // record 值相等：每年每個欄位都一樣。
    }

    [Fact]
    public void FamineCenturyStaysConservedAndNeverRescuesTheDead()
    {
        // SURV_NO_RESCUE 的長期版本：沒有任何糧食來源時，人口只會下降到 0，之後保持 0，
        // 整整 100 年都不會生成糧食或人口，也不會出現守恆錯誤。
        var report = DemoCenturyRun.Run(42, 100, DemoWeeklyTempConfig.FamineSample);
        AssertCenturyIsConserved(report, 100);

        Assert.True(report.Years[0].Deaths > 0);
        Assert.True(report.Years[0].StarvationEvents > 0);
        Assert.All(report.Years, y =>
        {
            Assert.Equal(0, y.Births);
            Assert.Equal(0, y.FoodProduced);
            Assert.Equal(0, y.Regenerated);
        });

        var extinctFrom = report.Years.First(y => y.PopulationClosing == 0).Year;
        Assert.All(report.Years.Where(y => y.Year > extinctFrom), y =>
        {
            Assert.Equal(0, y.PopulationClosing);
            Assert.Equal(0, y.InhabitedSettlements);
            Assert.Equal(0, y.SettlementEvents);
        });
    }

    [Fact]
    public void YearSummaryReportsEveryKindOfImbalance()
    {
        var ok = new DemoYearSummary(
            Year: 1, LastDay: 364, Weeks: 52,
            PopulationOpening: 10, Births: 0, Deaths: 2, PopulationClosing: 8,
            FoodOpening: 5, FoodProduced: 30, FoodConsumed: 20, FoodShortage: 3, FoodClosing: 15,
            ReserveOpening: 100, Regenerated: 40, Harvested: 30, ReserveClosing: 110,
            HungerEvents: 1, StarvationEvents: 1, StarvationDeathsInEvents: 2, RecoveryEvents: 0,
            InhabitedSettlements: 2, DailyDemoHarvest: 0);
        Assert.Empty(ok.Problems());

        Assert.Contains((ok with { PopulationClosing = 9 }).Problems(), p => p.Contains("人口帳"));
        Assert.Contains((ok with { FoodClosing = 16 }).Problems(), p => p.Contains("糧食帳"));
        Assert.Contains((ok with { ReserveClosing = 111 }).Problems(), p => p.Contains("資源存量帳"));
        Assert.Contains((ok with { FoodProduced = 31, FoodClosing = 16 }).Problems(), p => p.Contains("魔法糧食"));
        Assert.Contains((ok with { StarvationDeathsInEvents = 1 }).Problems(), p => p.Contains("帶原因"));
        Assert.Contains((ok with { Weeks = 51 }).Problems(), p => p.Contains("週結算次數"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(DemoCenturyRun.MaxYears + 1)]
    public void RejectsOutOfRangeYearCounts(int years)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DemoCenturyRun.Run(42, years));

    [Fact]
    public void OnYearCallbackSeesEachYearInOrder()
    {
        var seen = new List<int>();
        var report = DemoCenturyRun.Run(7, 3, onYear: y => seen.Add(y.Year));
        Assert.Equal(new[] { 1, 2, 3 }, seen);
        Assert.Equal(seen, report.Years.Select(y => y.Year));
    }
}
