using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 08 章 d 節：P1-M1 正式驗收。M1_STABILITY／M1_VARIETY 的 TEMP 門檻（D1／D2）判得出全滅、爆量與無差異；
/// 四個正式案例（普通世界長期生存且出生增加、資源差衰退、饑荒、開拓）都從真實長跑找到可重現的種子；
/// 找不到時如實回報「沒有找到」；--report 寫出與主控台相同的報告檔。
/// </summary>
public sealed class M1OfficialAcceptanceTests
{
    private static readonly Lazy<IReadOnlyList<M1CheckDefinition>> Checks = new(() =>
        M1CheckCatalog.Load(M1CheckCatalog.FindDefaultPath()!));

    private static M1RunReport Evaluate(M1RunData run) => M1Evaluator.Evaluate(run, Checks.Value);

    private static M1CheckResult Result(M1RunReport report, string id) => report.Results.Single(r => r.Check.Id == id);

    private static readonly Lazy<M1RunData> Living5 = new(() =>
        M1RunCollector.Collect(5, M1Preset.Living, 100, TestCalendar.DaysPerYear));

    [Fact]
    public void RequiredItemsAreTheEightListedInTheManual()
    {
        Assert.Equal(new[] { "M1_RES", "M1_POP", "M1_NO_MAGIC_FOOD", "M1_STABILITY", "M1_MIG", "M1_SETTLE", "M1_FAMINE", "M1_HISTORY" },
            M1OfficialAcceptance.RequiredIds);
        Assert.All(M1OfficialAcceptance.RequiredIds, id => Assert.Contains(Checks.Value, c => c.Id == id));
    }

    [Fact]
    public void StabilityThresholdCatchesExtinctionAndExplosion()
    {
        var run = M1RunCollector.Collect(42, M1Preset.Normal, 3, TestCalendar.DaysPerYear);
        Assert.Equal(M1Verdict.Pass, Result(Evaluate(run), "M1_STABILITY").Verdict);

        var lastWeek = run.Weeks[^1].Week;
        var totals = run.TotalsAfterWeek.ToDictionary(kv => kv.Key, kv => kv.Value);
        totals[lastWeek] = totals[lastWeek] with { Population = 0 };
        var extinct = Result(Evaluate(run with { TotalsAfterWeek = totals }), "M1_STABILITY");
        Assert.Equal(M1Verdict.Warn, extinct.Verdict);
        Assert.Contains("全滅", extinct.Diagnosis);

        totals[lastWeek] = totals[lastWeek] with { Population = run.Initial.Population * M1Evaluator.TempStabilityExplosionFactor + 1 };
        var explosion = Result(Evaluate(run with { TotalsAfterWeek = totals }), "M1_STABILITY");
        Assert.Equal(M1Verdict.Warn, explosion.Verdict);
        Assert.Contains("爆量", explosion.Diagnosis);
    }

    [Fact]
    public void VarietyThresholdComparesResourceGroups()
    {
        var run = M1RunCollector.Collect(42, M1Preset.Normal, 3, TestCalendar.DaysPerYear);
        var kinds = run.Settlements.Select(s => s.ResourceKinds).Distinct().Count();
        Assert.True(kinds >= 2, "一般情境種子 42 應有至少兩種資源組合的聚落");
        // 全部聚落期末人口相同 → 沒有差異（警告）。
        var same = run with { Settlements = run.Settlements.Select(s => s with { FinalPopulation = 5 }).ToList() };
        var flat = Result(Evaluate(same), "M1_VARIETY");
        Assert.Equal(M1Verdict.Warn, flat.Verdict);
        // 一種資源組合有人、另一種無人 → 有差異（通過）。
        var first = run.Settlements[0].ResourceKinds;
        var split = run with
        {
            Settlements = run.Settlements.Select(s => s with { FinalPopulation = s.ResourceKinds == first ? 6 : 0 }).ToList()
        };
        Assert.Equal(M1Verdict.Pass, Result(Evaluate(split), "M1_VARIETY").Verdict);
    }

    [Fact]
    public void AllFourCasesAreFoundInRealReproducibleRuns()
    {
        var reports = new[]
        {
            Evaluate(M1RunCollector.Collect(42, M1Preset.Normal, 10, TestCalendar.DaysPerYear)),
            Evaluate(M1RunCollector.Collect(42, M1Preset.LocalFamine, 10, TestCalendar.DaysPerYear)),
            Evaluate(M1RunCollector.Collect(7, M1Preset.Frontier, 10, TestCalendar.DaysPerYear)),
            Evaluate(Living5.Value)
        };
        var cases = M1OfficialAcceptance.Cases(reports);
        Assert.Equal(4, cases.Count);
        Assert.All(cases, c => Assert.True(c.Found, $"{c.Name}：{c.Evidence}"));
        Assert.Contains("種子 5｜人類採集", cases[0].Evidence);
        Assert.Contains("100 年後仍有", cases[0].Evidence);

        // 普通世界長期生存的種子跑滿百年：仍有人、有出生，且 Hard 項目全部沒有失敗。
        var living = Evaluate(Living5.Value);
        Assert.DoesNotContain(living.Results, r => r.Verdict == M1Verdict.Fail);
        Assert.Equal(M1Verdict.Pass, Result(living, "M1_STABILITY").Verdict);
    }

    [Fact]
    public void FamineInSharedForagingUsesWorkersAndSourceStock()
    {
        // 共享採集情境：勞動力＝成人扣取水者、資源有剩看來源帳；診斷不得再引用永遠為 0 的聚落資源點。
        var famine = Result(Evaluate(Living5.Value), "M1_FAMINE");
        Assert.NotEqual(M1Verdict.Fail, famine.Verdict);
        if (famine.Diagnosis.Contains("勞動力上限", StringComparison.Ordinal))
        {
            Assert.Contains("取用的來源", famine.Diagnosis);
            Assert.DoesNotContain("資源點剩", famine.Diagnosis);
        }
    }

    [Fact]
    public void MissingCaseIsReportedHonestly()
    {
        // 人類採集種子 1 在 10 年內全滅：找不到長期生存案例，不能報「找到」。
        var cases = M1OfficialAcceptance.Cases(new[] { Evaluate(M1RunCollector.Collect(1, M1Preset.Living, 10, TestCalendar.DaysPerYear)) });
        Assert.False(cases[0].Found);
        Assert.Contains("沒有一個", cases[0].Evidence);
    }

    [Fact]
    public void ReportFileMatchesConsoleOutput()
    {
        var path = Path.Combine(Path.GetTempPath(), $"m1-report-{Guid.NewGuid():N}.txt");
        try
        {
            var console = new StringWriter();
            var code = M1AcceptanceCli.Run(new[] { "--years", "1", "--seed", "42", "--preset", "living", "--report", path }, console);
            Assert.Equal(0, code);
            var file = File.ReadAllText(path);
            Assert.Contains("種子 42｜情境：人類採集（普通世界）（living，TEMP）｜1 年", file);
            Assert.Contains("Hard 項目：", file);
            Assert.StartsWith(file, console.ToString());
            Assert.Contains("驗收報告已寫入", console.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
