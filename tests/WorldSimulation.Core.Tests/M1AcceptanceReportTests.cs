using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// V0-08A（#74）：表 45G M1 驗收報告。三個 TEMP 固定種子 × 四個 TEMP 情境（第 08 章 d 節加人類採集）各跑 100 年，
/// 每一項都要有結果、順序跟 45G CSV 一致、可重現；Hard 項目不能失敗，而且竄改證據時要被抓到。
/// </summary>
public sealed class M1AcceptanceReportTests
{
    private static readonly string[] HardIds = { "M1_RES", "M1_POP", "M1_NO_MAGIC_FOOD", "M1_MIG", "M1_SETTLE" };

    private static readonly Lazy<IReadOnlyList<M1CheckDefinition>> Checks = new(() =>
    {
        var path = M1CheckCatalog.FindDefaultPath();
        Assert.NotNull(path);
        return M1CheckCatalog.Load(path!);
    });

    private static readonly Lazy<IReadOnlyList<M1RunReport>> Matrix = new(() =>
        M1Preset.Default
            .SelectMany(p => M1AcceptanceCli.DefaultSeeds.Select(seed => M1Evaluator.Evaluate(M1RunCollector.Collect(seed, p, 100, TestCalendar.DaysPerYear), Checks.Value)))
            .ToList());

    private static readonly Lazy<M1RunData> LocalFamine42 = new(() => M1RunCollector.Collect(42, M1Preset.LocalFamine, 100, TestCalendar.DaysPerYear));

    private static M1CheckResult Result(M1RunReport report, string id) => report.Results.Single(r => r.Check.Id == id);

    [Fact]
    public void CatalogComesFromThe45GCsvInSourceOrderAndOnlyEnabledRows()
    {
        var path = M1CheckCatalog.FindDefaultPath()!;
        var csvIds = File.ReadAllLines(path).Skip(1)
            .Where(l => l.Trim().Length > 0)
            .Select(l => l.Split(','))
            .Where(f => f[^1].Trim().Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            .Select(f => f[0])
            .ToList();

        Assert.Equal(csvIds, Checks.Value.Select(c => c.Id).ToList());
        Assert.Equal(10, Checks.Value.Count);
        Assert.Equal(HardIds.OrderBy(x => x), Checks.Value.Where(c => c.Category == M1Category.Hard).Select(c => c.Id).OrderBy(x => x));
        Assert.Equal("資源守恆", Checks.Value[0].Name);
    }

    [Fact]
    public void ThreeSeedsTimesFivePresetsReportEveryCheckInOrderWithNoHardFailure()
    {
        var expectedOrder = Checks.Value.Select(c => c.Id).ToList();
        Assert.Equal(15, Matrix.Value.Count); // 第 06-2 章：加勞力饑荒情境
        foreach (var report in Matrix.Value)
        {
            Assert.Equal(expectedOrder, report.Results.Select(r => r.Check.Id).ToList());
            Assert.All(report.Results, r => Assert.False(string.IsNullOrWhiteSpace(r.Diagnosis)));
            var failures = report.Results.Where(r => r.Verdict == M1Verdict.Fail).Select(r => $"{r.Check.Id}: {r.Diagnosis}");
            Assert.Empty(failures);
            Assert.Equal(DemoCenturyRun.WeeklySettlementsThrough(100 * TestCalendar.DaysPerYear), report.Run.Weeks.Count);
        }

        // 每個 Hard 項目在矩陣中至少有一次實際量測到而通過（不是只因沒發生而沒失敗）。
        foreach (var id in HardIds)
            Assert.Equal(M1Verdict.Pass, M1Evaluator.Aggregate(Matrix.Value.Select(r => Result(r, id).Verdict)));
    }

    [Fact]
    public void WarningsUseTempThresholdsAndMissingFeaturesAreNotReportedAsPass()
    {
        foreach (var report in Matrix.Value)
        {
            // 第 08 章 d 節 TEMP 門檻（D1）：期末仍有人且未爆量才通過；全滅＝警告。
            var final = report.Run.TotalsAfterWeek[report.Run.Weeks[^1].Week].Population;
            var stability = Result(report, "M1_STABILITY");
            Assert.Equal(final > 0 ? M1Verdict.Pass : M1Verdict.Warn, stability.Verdict);
            Assert.Contains("【TEMP 門檻 D1】", stability.Diagnosis);
            var variety = Result(report, "M1_VARIETY");
            Assert.NotEqual(M1Verdict.Fail, variety.Verdict);
            Assert.Contains("【TEMP 門檻 D2】", variety.Diagnosis);
            // 聚落內分組取用尚未實作。
            Assert.Equal(M1Verdict.Unimplemented, Result(report, "M1_ACCESS").Verdict);
        }

        // 一般情境沒有開拓：M1_SETTLE 不能報「建立成功」或通過。
        foreach (var report in Matrix.Value.Where(r => r.Run.Preset == M1Preset.Normal))
        {
            var settle = Result(report, "M1_SETTLE");
            Assert.Equal(M1Verdict.Warn, settle.Verdict);
            Assert.Contains("沒有開拓", settle.Diagnosis);
        }
    }

    [Fact]
    public void SameSeedReproducesTheSameReport()
    {
        var a = M1Evaluator.Evaluate(M1RunCollector.Collect(7, M1Preset.Frontier, 100, TestCalendar.DaysPerYear), Checks.Value);
        var b = Matrix.Value.Single(r => r.Run.Seed == 7 && r.Run.Preset == M1Preset.Frontier);
        Assert.Equal(Render(a), Render(b));
    }

    [Fact]
    public void TamperedFoodLedgerFailsM1ResAndCliExitCodeIsOne()
    {
        var run = LocalFamine42.Value;
        var weeks = run.Weeks.ToList();
        var target = weeks[10];
        var ledgers = target.Settlements.ToList();
        ledgers[0] = ledgers[0] with { FoodClosing = ledgers[0].FoodClosing + 1 };
        weeks[10] = target with { Settlements = ledgers };

        var report = M1Evaluator.Evaluate(run with { Weeks = weeks }, Checks.Value);
        var res = Result(report, "M1_RES");
        Assert.Equal(M1Verdict.Fail, res.Verdict);
        Assert.Contains("第 1 年第 11 週", res.Diagnosis);
        Assert.True(report.HasHardFailure);
    }

    [Fact]
    public void TeleportingMigrantBatchFailsM1Mig()
    {
        // 取矩陣中第一組真的有隊伍移動的長跑（不是每個種子都會遷徙）。
        static bool Moving(DemoMigrationWeekLedger m) => !m.Departed && m.CellBefore != m.CellAfter;
        var run = Matrix.Value.Select(r => r.Run).FirstOrDefault(r => r.Weeks.Any(w => w.Migrations.Any(Moving)));
        Assert.True(run is not null, "預設矩陣應該至少有一組出現移動中的遷徙隊伍");
        var weeks = run!.Weeks.ToList();
        var index = weeks.FindIndex(w => w.Migrations.Any(Moving));
        var migrations = weeks[index].Migrations.ToList();
        var i = migrations.FindIndex(Moving);
        var m0 = migrations[i];
        migrations[i] = m0 with { CellAfter = new DemoCellRef(m0.CellBefore.Column + 5, m0.CellBefore.Row + 5) };
        weeks[index] = weeks[index] with { Migrations = migrations };

        var report = M1Evaluator.Evaluate(run with { Weeks = weeks }, Checks.Value);
        Assert.Equal(M1Verdict.Fail, Result(report, "M1_MIG").Verdict);
        Assert.Contains("瞬移", Result(report, "M1_MIG").Diagnosis);
    }

    [Fact]
    public void SettlementAppearingWithoutFoundingEventFailsM1Settle()
    {
        var run = M1RunCollector.Collect(42, M1Preset.Normal, 1, TestCalendar.DaysPerYear);
        var weeks = run.Weeks.ToList();
        var ledgers = weeks[3].Settlements.ToList();
        ledgers.Add(ledgers[0] with { SettlementId = 99, SettlementName = "幽靈聚落" });
        weeks[3] = weeks[3] with { Settlements = ledgers };

        var report = M1Evaluator.Evaluate(run with { Weeks = weeks }, Checks.Value);
        Assert.Equal(M1Verdict.Fail, Result(report, "M1_SETTLE").Verdict);
        Assert.Contains("憑空出現", Result(report, "M1_SETTLE").Diagnosis);
    }

    [Fact]
    public void WithoutMigrationM1MigIsWarningNotPass()
    {
        // 局部饑荒但關閉遷徙（測試用 TEMP 設定）：有缺糧、沒有任何遷徙隊伍。
        var noMigration = new M1Preset("test-no-migration", "測試：關閉遷徙",
            DemoWeeklyTempConfig.LocalFamineSample with { MigrationEnabled = false });
        var run = M1RunCollector.Collect(42, noMigration, 2, TestCalendar.DaysPerYear);
        Assert.All(run.Weeks, w => Assert.Empty(w.Migrations));
        var report = M1Evaluator.Evaluate(run, Checks.Value);
        Assert.Equal(M1Verdict.Warn, Result(report, "M1_MIG").Verdict);
        Assert.Contains("沒有遷徙", Result(report, "M1_MIG").Diagnosis);
    }

    /// <summary>第 06-2 章：勞力饑荒情境（存量充足、人手不夠）每個預設種子都量測得到危機傳導，正式驗收不再靠運氣。</summary>
    [Fact]
    public void LaborFamineMeasuresTheCrisisChainOnEveryDefaultSeed()
    {
        var famine = Matrix.Value.Where(r => r.Run.Preset == M1Preset.LaborFamine).ToList();
        Assert.Equal(M1AcceptanceCli.DefaultSeeds.Count, famine.Count);
        Assert.All(famine, r =>
        {
            var result = Result(r, "M1_FAMINE");
            Assert.True(result.Verdict == M1Verdict.Pass, $"種子 {r.Run.Seed}：{result.Diagnosis}");
        });
    }

    [Fact]
    public void FamineWithoutMeasuredLaborBoundHarvestDropIsWarningNotPass()
    {
        // 反例：把每一週的資源點剩餘都改成 0（產量看起來受存量限制），
        // 就算人口因餓死／外移下降，也不能從人口前後推斷「產能隨之改變」而判通過。
        var run = LocalFamine42.Value;
        var weeks = run.Weeks
            .Select(w => w with { Settlements = w.Settlements.Select(l => l with { ReserveClosing = 0 }).ToList() })
            .ToList();

        var famine = Result(M1Evaluator.Evaluate(run with { Weeks = weeks }, Checks.Value), "M1_FAMINE");
        Assert.Equal(M1Verdict.Warn, famine.Verdict);
        Assert.Contains("量測不到生產力", famine.Diagnosis);
        Assert.DoesNotContain("產量受勞力限制）", famine.Diagnosis);
    }

    /// <summary>
    /// 在 LocalFamine42 上挑第一個「缺糧後餓死／外移」且有下一週帳的效果，只留下這一組前後兩週可量測：
    /// 其他週的資源點剩餘一律改 0（不可能被當成受勞力限制），下一週人口少 1、採收＝新上限、資源點有剩。
    /// <paramref name="beforeHarvest"/> 給前週（效果當週）採收；null 表示前週採收＝舊上限。
    /// </summary>
    private static M1RunData SingleMeasurableDrop(Func<long, long>? beforeHarvest)
    {
        var run = LocalFamine42.Value;
        var yieldPerWorker = run.Preset.Config.YieldPerWorkerPerWeek;
        Assert.True(yieldPerWorker > 0, "示範設定的 TEMP 每人每週產量應大於 0");
        var events = run.Weeks.SelectMany(w => w.Events).ToList();
        var ledgerOf = run.Weeks.SelectMany(w => w.Settlements.Select(l => (w.Week, l))).ToDictionary(x => (x.Week, x.l.SettlementId), x => x.l);
        var effect = events.FirstOrDefault(e =>
            (e.Kind == DemoSettlementEventKind.StarvationDeath || e.Kind == DemoSettlementEventKind.Emigration)
            && events.Any(h => h.Kind == DemoSettlementEventKind.Hunger && h.SettlementId == e.SettlementId && h.Week <= e.Week)
            && ledgerOf.TryGetValue((e.Week, e.SettlementId), out var d) && d.PopulationOpening >= 2
            && ledgerOf.ContainsKey((e.Week + 1, e.SettlementId)));
        Assert.True(effect is not null, "局部饑荒種子 42 應該有缺糧後的餓死或外移，且下一週仍有帳");

        var hit = effect!;
        var key = hit.SettlementId;
        var weeks = run.Weeks.Select(w => w with
        {
            Settlements = w.Settlements.Select(l =>
            {
                var during = l.SettlementId == key && w.Week == hit.Week;
                var next = l.SettlementId == key && w.Week == hit.Week + 1;
                if (!during && !next)
                    return l with { ReserveClosing = 0 };
                var populationBefore = ledgerOf[(hit.Week, key)].PopulationOpening;
                if (during)
                {
                    var cap = populationBefore * yieldPerWorker;
                    return l with { FoodProduced = beforeHarvest?.Invoke(cap) ?? cap, ReserveClosing = 1 };
                }

                var after = populationBefore - 1;
                return l with { PopulationOpening = after, FoodProduced = after * yieldPerWorker, ReserveClosing = 1 };
            }).ToList()
        }).ToList();
        return run with { Weeks = weeks };
    }

    [Fact]
    public void FamineHarvestRisingFromZeroToNewCapIsNotAMeasuredDrop()
    {
        // 反例（GPT 審查 #87）：前週採收 0、下週採收＝新上限 N。只看下週會誤判「減產」通過；
        // 前週沒有受舊上限限制、採收也沒有下降，必須是警告而且不能出現受勞力限制的傳導。
        var famine = Result(M1Evaluator.Evaluate(SingleMeasurableDrop(_ => 0), Checks.Value), "M1_FAMINE");
        Assert.Equal(M1Verdict.Warn, famine.Verdict);
        Assert.Contains("量測不到生產力", famine.Diagnosis);
        Assert.DoesNotContain("產量受勞力限制）", famine.Diagnosis);
    }

    [Fact]
    public void FamineHarvestBelowOldCapBeforeTheDropIsNotAMeasuredDrop()
    {
        // 反例：前週採收低於舊上限（受存量等其他因素限制），即使下週＝新上限也不算勞動力傳導。
        var famine = Result(M1Evaluator.Evaluate(SingleMeasurableDrop(cap => cap - 1), Checks.Value), "M1_FAMINE");
        Assert.Equal(M1Verdict.Warn, famine.Verdict);
        Assert.DoesNotContain("產量受勞力限制）", famine.Diagnosis);
    }

    [Fact]
    public void FamineLaborBoundHarvestDropIsMeasured()
    {
        // 正例：前週採收＝舊上限且資源點有剩，下週人口少 1、採收＝新上限且資源點有剩 → 量測到真的減產。
        var famine = Result(M1Evaluator.Evaluate(SingleMeasurableDrop(null), Checks.Value), "M1_FAMINE");
        Assert.Contains("＝前週舊上限", famine.Diagnosis);
        Assert.Contains("前後兩週產量受勞力限制）", famine.Diagnosis);
        Assert.DoesNotContain("量測不到生產力", famine.Diagnosis);
    }

    [Fact]
    public void HarvestAboveLaborCapacityIsReportedByM1Famine()
    {
        // 反例：證據中的 TEMP 每人每週產量改成 0 → 任何採收都超過勞動力上限，必須被指出（Behavior Check → 警告）。
        // 第 06-1 章：需要第一個聚落以外有實際採收的種子（地圖改變後種子 42 可能全是未開墾草原）。
        var run = M1RunCollector.Collect(TestSeeds.NormalWithWoodlandAfterFirst, M1Preset.LocalFamine, 10, TestCalendar.DaysPerYear);
        var zeroYield = run.Preset with { Config = run.Preset.Config with { YieldPerWorkerPerWeek = 0 } };

        var famine = Result(M1Evaluator.Evaluate(run with { Preset = zeroYield }, Checks.Value), "M1_FAMINE");
        Assert.Equal(M1Verdict.Warn, famine.Verdict);
        Assert.Contains("超過勞動力上限", famine.Diagnosis);
    }

    [Fact]
    public void FaminePassAlwaysCitesMeasuredHarvestAndCoverageLimitsAreLabelled()
    {
        foreach (var report in Matrix.Value)
        {
            var famine = Result(report, "M1_FAMINE");
            if (famine.Verdict == M1Verdict.Pass)
            {
                Assert.Contains("實際採收", famine.Diagnosis);
                Assert.Contains("＝前週舊上限", famine.Diagnosis);
                Assert.Contains("前後兩週產量受勞力限制", famine.Diagnosis);
            }

            // 出生、具體化尚未實作；43F 只做了 M1 示範切片（#88）：通過時也必須標明 TEMP 覆蓋限制。
            Assert.Contains("【覆蓋限制 TEMP】", Result(report, "M1_POP").Diagnosis);
            Assert.Contains("具體化", Result(report, "M1_POP").Diagnosis);
            var history = Result(report, "M1_HISTORY");
            if (history.Verdict == M1Verdict.Pass)
            {
                Assert.Contains("【覆蓋限制 TEMP】", history.Diagnosis);
                Assert.Contains("完整 43F 歷史核心尚未實作", history.Diagnosis);
                Assert.Contains("事件 #", history.Diagnosis);
            }
        }
    }

    [Fact]
    public void NonHardViolationIsOnlyAWarning()
    {
        Assert.Equal(M1Verdict.Fail, M1Evaluator.ToVerdict(M1Raw.Violation, M1Category.Hard));
        Assert.Equal(M1Verdict.Warn, M1Evaluator.ToVerdict(M1Raw.Violation, M1Category.Warning));
        Assert.Equal(M1Verdict.Warn, M1Evaluator.ToVerdict(M1Raw.Violation, M1Category.BehaviorCheck));
        Assert.Equal(M1Verdict.Fail, M1Evaluator.Aggregate(new[] { M1Verdict.Pass, M1Verdict.Fail }));
        Assert.Equal(M1Verdict.Pass, M1Evaluator.Aggregate(new[] { M1Verdict.Warn, M1Verdict.Pass }));
        Assert.Equal(M1Verdict.Warn, M1Evaluator.Aggregate(new[] { M1Verdict.Warn, M1Verdict.Unimplemented }));
    }

    [Theory]
    [InlineData("CheckID,中文名稱,100年M1通過條件,Hard/Warning,主要診斷,Enabled\nM1_RES,資源守恆,x,Hard,y,TRUE\nM1_RES,重複,x,Hard,y,TRUE\n", "重複")]
    [InlineData("CheckID,中文名稱,Hard/Warning,Enabled\nM1_RES,資源守恆,Hard,TRUE\n", "缺少欄位")]
    [InlineData("CheckID,中文名稱,100年M1通過條件,Hard/Warning,主要診斷,Enabled\nM1_RES,資源守恆,x,Hard,y,FALSE\n", "沒有任何 Enabled=TRUE")]
    [InlineData("CheckID,中文名稱,100年M1通過條件,Hard/Warning,主要診斷,Enabled\nM1_RES,資源守恆,x,Fatal,y,TRUE\n", "分類")]
    [InlineData("CheckID,中文名稱,100年M1通過條件,Hard/Warning,主要診斷,Enabled\n,資源守恆,x,Hard,y,TRUE\n", "缺少 CheckID")]
    public void BrokenCatalogIsRejectedInChinese(string csv, string expected)
    {
        var ex = Assert.Throws<M1CatalogException>(() => M1CheckCatalog.Parse(csv, "測試"));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void DisabledAndUnknownChecksAreHandledFromTheCsv()
    {
        var checks = M1CheckCatalog.Parse(
            "CheckID,中文名稱,100年M1通過條件,Hard/Warning,主要診斷,Enabled\n"
            + "M1_RES,資源守恆,x,Hard,y,FALSE\n"
            + "M1_NEW,\"新項目，含逗號\",x,Warning,y,TRUE\n", "測試");
        var only = Assert.Single(checks);
        Assert.Equal("新項目，含逗號", only.Name);
        var report = M1Evaluator.Evaluate(M1RunCollector.Collect(42, M1Preset.Normal, 1, TestCalendar.DaysPerYear), checks);
        Assert.Equal(M1Verdict.Unimplemented, Assert.Single(report.Results).Verdict);
    }

    [Theory]
    [InlineData(new[] { "--years", "0" }, "--years 必須是")]
    [InlineData(new[] { "--years", "-1" }, "--years 必須是")]
    [InlineData(new[] { "--years", "1001" }, "--years 必須是")]
    [InlineData(new[] { "--years" }, "缺少值")]
    [InlineData(new[] { "--preset", "paradise" }, "--preset 必須是")]
    [InlineData(new[] { "--seed", "abc" }, "--seed 必須是")]
    [InlineData(new[] { "--color", "red" }, "不認得的參數")]
    [InlineData(new[] { "--checks", "/nonexistent/45G.csv" }, "找不到 M1 驗收清單")]
    [InlineData(new[] { "--world-params", "/nonexistent/01.csv" }, "找不到世界參數表")]
    [InlineData(new[] { "--world-params" }, "缺少值")]
    public void BadArgumentsExitWithTwoAndChineseMessage(string[] args, string expected)
    {
        var output = new StringWriter();
        Assert.Equal(2, M1AcceptanceCli.Run(args, output));
        Assert.Contains(expected, output.ToString());
        Assert.Contains("錯誤：", output.ToString());
    }

    [Fact]
    public void CliPrintsEveryCheckIdForOneYearRun()
    {
        var output = new StringWriter();
        var code = M1AcceptanceCli.Run(new[] { "--years", "1", "--seed", "42", "--preset", "frontier" }, output);
        var text = output.ToString();
        Assert.Equal(0, code);
        foreach (var check in Checks.Value)
            Assert.Contains($"  {check.Id}｜{check.Name}｜{check.CategoryText}｜", text);
        Assert.Contains("種子 42｜情境：開拓（frontier，TEMP）｜1 年", text);
        // #82：報告標頭寫出年界來源（正式設定 01 表），與 --demo --years 相同。
        Assert.Contains($"WORLD_DAYS_PER_YEAR＝{TestCalendar.DaysPerYear}", text);
        Assert.Contains("Hard 項目：", text);
    }

    private static string Render(M1RunReport report)
    {
        var writer = new StringWriter();
        M1AcceptanceCli.WriteRun(report, writer);
        return writer.ToString();
    }
}
