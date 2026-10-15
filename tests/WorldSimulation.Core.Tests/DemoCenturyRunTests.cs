using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// V0-05：百年無畫面長測。3 個固定種子各跑 100 年；#82 起一年日數讀正式設定 01 表（365 日），
/// 週結算每 7 日一次、跨年不重置，每年 52 或 53 次。
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
            Assert.Equal(y.ExpectedWeeks, y.Weeks);
            Assert.InRange(y.Weeks, 52, 53);
            Assert.Equal(y.Year * TestCalendar.DaysPerYear, y.LastDay);
            Assert.Equal((y.Year - 1) * TestCalendar.DaysPerYear + 1, y.FirstDay);

            // M1_POP：期初＋出生－死亡＝期末；死亡按原因分類，各自由帶原因事件對帳（C02）。
            Assert.Equal(y.PopulationClosing, y.PopulationOpening + y.Births - y.Deaths);
            Assert.Equal(y.Deaths - y.NaturalDeaths, y.StarvationDeathsInEvents);
            Assert.Equal(y.NaturalDeaths, y.NaturalDeathEvents);
            Assert.Equal(y.Births, y.BirthEvents);

            // M1_RES：資源存量與糧食各自守恆。
            Assert.Equal(y.ReserveClosing, y.ReserveOpening + y.Regenerated - y.Harvested);
            Assert.Equal(y.FoodClosing, y.FoodOpening + y.FoodProduced - y.FoodConsumed);

            // M1_NO_MAGIC_FOOD：糧食只來自實際採收。
            Assert.Equal(y.Harvested, y.FoodProduced);

            Assert.True(y.PopulationClosing >= 0 && y.FoodClosing >= 0 && y.ReserveClosing >= 0);

            if (previous is not null)
            {
                // 年界不跳日、不重複。
                Assert.Equal(previous.LastDay + 1, y.FirstDay);
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
        var reports = Seeds.Select(seed => DemoCenturyRun.Run(seed, 100, TestCalendar.DaysPerYear)).ToList();

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

        Assert.StartsWith($"第 100 年（至第 {100 * TestCalendar.DaysPerYear} 日）｜週結算", reports[0].Years[^1].Describe());
        // 100 年的週結算總數＝第 1～36500 日之間 7 的倍數日數，沒有漏算或重複。
        foreach (var report in reports)
            Assert.Equal(DemoCenturyRun.WeeklySettlementsThrough(100 * TestCalendar.DaysPerYear), report.Years.Sum(y => y.Weeks));
    }

    [Fact]
    public void SameSeedGivesIdenticalCentury()
    {
        var a = DemoCenturyRun.Run(42, 100, TestCalendar.DaysPerYear);
        var b = DemoCenturyRun.Run(42, 100, TestCalendar.DaysPerYear);
        Assert.Equal(a.Years, b.Years); // record 值相等：每年每個欄位都一樣。
    }

    [Fact]
    public void FamineCenturyStaysConservedAndNeverRescuesTheDead()
    {
        // SURV_NO_RESCUE 的長期版本：沒有任何糧食來源時，人口只會下降到 0，之後保持 0，
        // 整整 100 年都不會生成糧食或人口，也不會出現守恆錯誤。
        var report = DemoCenturyRun.Run(42, 100, TestCalendar.DaysPerYear, DemoWeeklyTempConfig.FamineSample);
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
            InhabitedSettlements: 2);
        Assert.Empty(ok.Problems());

        Assert.Contains((ok with { PopulationClosing = 9 }).Problems(), p => p.Contains("人口帳"));
        Assert.Contains((ok with { FoodClosing = 16 }).Problems(), p => p.Contains("糧食帳"));
        Assert.Contains((ok with { ReserveClosing = 111 }).Problems(), p => p.Contains("資源存量帳"));
        Assert.Contains((ok with { FoodProduced = 31, FoodClosing = 16 }).Problems(), p => p.Contains("魔法糧食"));
        Assert.Contains((ok with { StarvationDeathsInEvents = 1 }).Problems(), p => p.Contains("帶原因"));
        Assert.Contains((ok with { Weeks = 51 }).Problems(), p => p.Contains("週結算次數"));

        // #82：365 日年第 7 年（第 2191～2555 日）含 53 次週結算；固定寫 52 週會被抓到。
        var year7 = ok with { Year = 7, FirstDay = 2191, LastDay = 2555, Weeks = 53 };
        Assert.Equal(53, year7.ExpectedWeeks);
        Assert.Empty(year7.Problems());
        Assert.Contains((year7 with { Weeks = 52 }).Problems(), p => p.Contains("週結算次數"));
        Assert.Contains((ok with { FirstDay = 400 }).Problems(), p => p.Contains("日期範圍"));
    }

    [Fact]
    public void WeekLedgerStarvationDeathsExcludesNaturalDeaths()
    {
        // C02：週帳的餓死數＝全部死亡－自然死亡；下游一律讀這個屬性。
        var ledger = new DemoSettlementWeekLedger(
            Week: 1, SettlementId: 1, SettlementName: "測試聚落",
            ReserveOpening: 0, Regenerated: 0, Harvested: 0, ReserveClosing: 0,
            FoodOpening: 0, FoodProduced: 0, FoodConsumed: 0, FoodClosing: 0, FoodShortage: 0,
            PopulationOpening: 10, Births: 0, Deaths: 5, PopulationClosing: 5,
            NaturalDeaths: 2);
        Assert.Equal(3, ledger.StarvationDeaths);
        Assert.Equal(5, (ledger with { NaturalDeaths = 0 }).StarvationDeaths);
    }

    [Fact]
    public void YearSummaryReconcilesBirthsStarvationAndNaturalDeathsSeparately()
    {
        // C02：死亡 5＝餓死 3（事件 3）＋自然死亡 2（事件 2）；出生 1 有 1 筆出生事件 → 無錯誤。
        var ok = new DemoYearSummary(
            Year: 1, LastDay: 364, Weeks: 52,
            PopulationOpening: 10, Births: 1, Deaths: 5, PopulationClosing: 6,
            FoodOpening: 0, FoodProduced: 0, FoodConsumed: 0, FoodShortage: 0, FoodClosing: 0,
            ReserveOpening: 0, Regenerated: 0, Harvested: 0, ReserveClosing: 0,
            HungerEvents: 0, StarvationEvents: 3, StarvationDeathsInEvents: 3, RecoveryEvents: 0,
            InhabitedSettlements: 1,
            NaturalDeaths: 2, BirthEvents: 1, NaturalDeathEvents: 2);
        Assert.Empty(ok.Problems());
        Assert.Equal(3, ok.StarvationDeaths);
        Assert.Contains("死亡 5（餓死 3、自然死亡 2）", ok.Describe());

        // 自然死亡沒記在帳上：全部死亡被當餓死，與餓死事件 3 人對不上。
        var noNatural = (ok with { NaturalDeaths = 0 }).Problems();
        Assert.Contains(noNatural, p => p.Contains("帶原因的餓死事件"));
        Assert.Contains(noNatural, p => p.Contains("帶原因的自然死亡事件"));

        // 出生沒有出生事件：出生無來源。
        Assert.Contains((ok with { BirthEvents = 0 }).Problems(), p => p.Contains("帶原因的出生事件"));
        // 自然死亡事件少一筆。
        Assert.Contains((ok with { NaturalDeathEvents = 1 }).Problems(), p => p.Contains("帶原因的自然死亡事件"));
    }

    [Fact]
    public void LivingHumanRunReconcilesBirthsAndDeathsByCauseWithZeroConservationErrors()
    {
        // C02：人類繁衍情境的年度對帳把出生、餓死、自然死亡分開對事件，不再把合法出生誤報為錯誤。
        // 實測（CI，2026-10-15）：種子 42 跑 45 年＝出生 15、餓死 27、自然死亡 0，45 年底人口 0；
        // 種子 7／1／2／3 跑 70 年同樣在 45 年內因餓死滅絕、自然死亡 0（TEMP 自然死亡風險 60 歲起，
        // 初始 18～22 歲的人口活不到 60 歲）。所以本情境量測不到自然死亡；自然死亡的對帳
        // 由 YearSummaryReconcilesBirthsStarvationAndNaturalDeathsSeparately（年度）與
        // M1HumanLifecycleCoverageTests（M1_POP）覆蓋，這裡仍逐年檢查自然死亡＝帶原因事件。
        // 依問答板 #25 使用者裁決 A（2026-10-15）：「累計自然死亡 >0」延到 P1.1.04（餓死改隨機抽取）
        // 或正式人口設定接入後補驗；屆時在此加回 NaturalDeaths > 0 斷言。
        const int years = 45;
        var report = DemoCenturyRun.Run(42, years, TestCalendar.DaysPerYear, DemoWeeklyTempConfig.LivingForagingSample);
        AssertCenturyIsConserved(report, years);
        Assert.True(report.Years.Sum(y => y.Births) > 0, "人類繁衍情境 45 年內應有出生。");
        Assert.All(report.Years, y => Assert.Equal(y.NaturalDeaths, y.NaturalDeathEvents));
        Assert.Equal(report.Years.Sum(y => y.Births), report.Years.Sum(y => y.BirthEvents));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(DemoCenturyRun.MaxYears + 1)]
    public void RejectsOutOfRangeYearCounts(int years)
        => Assert.Throws<ArgumentOutOfRangeException>(() => DemoCenturyRun.Run(42, years, TestCalendar.DaysPerYear));

    [Fact]
    public void OnYearCallbackSeesEachYearInOrder()
    {
        var seen = new List<int>();
        var report = DemoCenturyRun.Run(7, 3, TestCalendar.DaysPerYear, onYear: y => seen.Add(y.Year));
        Assert.Equal(new[] { 1, 2, 3 }, seen);
        Assert.Equal(seen, report.Years.Select(y => y.Year));
    }
}
