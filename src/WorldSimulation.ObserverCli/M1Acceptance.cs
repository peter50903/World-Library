using System.Globalization;
using System.Text;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.ObserverCli;

// V0-08A（Issue #74）：表 45G M1 驗收報告。
// 只「量測與報告」：讀 Core 已發布的週報與世界狀態，獨立重新對帳，不改任何模擬規則或數值。
// 檢查 ID、中文名稱與 Hard／Warning／Behavior Check 分類一律從 config/sheets/45G_M1驗收.csv 讀取，
// 程式不另抄一份清單；45G 有啟用、但這裡沒有量測的項目一律標「尚未實作」。

/// <summary>單項判定結果（顯示時附中文）。</summary>
public enum M1Verdict
{
    /// <summary>通過（PASS）。</summary>
    Pass = 0,

    /// <summary>警告（WARN）：未觀察到、只有數據沒有定案門檻，或非 Hard 項目出現違反。</summary>
    Warn = 1,

    /// <summary>失敗（FAIL）：Hard 項目量測到違反，結束碼 1。</summary>
    Fail = 2,

    /// <summary>尚未實作（UNIMPLEMENTED）。</summary>
    Unimplemented = 3
}

/// <summary>45G「Hard/Warning」欄的分類。</summary>
public enum M1Category
{
    Hard = 0,
    Warning = 1,
    BehaviorCheck = 2
}

/// <summary>45G 的一列（只收 Enabled=TRUE）。</summary>
public sealed record M1CheckDefinition(
    string Id,
    string Name,
    string Condition,
    M1Category Category,
    string CategoryText,
    string Diagnosis);

/// <summary>45G 清單缺檔、缺欄、重複或格式錯誤。訊息為繁體中文。</summary>
public sealed class M1CatalogException : Exception
{
    public M1CatalogException(string message) : base(message)
    {
    }
}

/// <summary>讀取 config/sheets/45G_M1驗收.csv。</summary>
public static class M1CheckCatalog
{
    public const string RelativePath = "config/sheets/45G_M1驗收.csv";

    /// <summary>從目前目錄與程式所在目錄往上找程式倉庫的 45G CSV；找不到回傳 null。</summary>
    public static string? FindDefaultPath()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, "config", "sheets", "45G_M1驗收.csv");
                if (File.Exists(candidate))
                    return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }

    public static IReadOnlyList<M1CheckDefinition> Load(string path)
    {
        if (!File.Exists(path))
            throw new M1CatalogException($"找不到 M1 驗收清單：{path}");
        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new M1CatalogException($"無法讀取 M1 驗收清單 {path}：{ex.Message}");
        }

        return Parse(text, path);
    }

    /// <summary>解析 45G CSV 內容；保留來源列順序，只回傳 Enabled=TRUE 的列。</summary>
    public static IReadOnlyList<M1CheckDefinition> Parse(string text, string source)
    {
        var rows = ParseCsv(text, source);
        if (rows.Count == 0)
            throw new M1CatalogException($"M1 驗收清單是空的：{source}");

        var header = rows[0].Select(h => h.Trim()).ToList();
        int Column(string name)
        {
            var index = header.IndexOf(name);
            if (index < 0)
                throw new M1CatalogException($"M1 驗收清單缺少欄位「{name}」：{source}");
            return index;
        }

        var idColumn = Column("CheckID");
        var nameColumn = Column("中文名稱");
        var conditionColumn = Column("100年M1通過條件");
        var categoryColumn = Column("Hard/Warning");
        var enabledColumn = Column("Enabled");
        var diagnosisColumn = header.IndexOf("主要診斷");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<M1CheckDefinition>();
        for (var r = 1; r < rows.Count; r++)
        {
            var row = rows[r];
            if (row.All(string.IsNullOrWhiteSpace))
                continue;
            string Cell(int c) => c >= 0 && c < row.Count ? row[c].Trim() : string.Empty;
            var line = r + 1;

            var id = Cell(idColumn);
            if (id.Length == 0)
                throw new M1CatalogException($"M1 驗收清單第 {line} 列缺少 CheckID：{source}");
            if (!seen.Add(id))
                throw new M1CatalogException($"M1 驗收清單 CheckID 重複：{id}（第 {line} 列）：{source}");

            var enabledText = Cell(enabledColumn);
            bool enabled;
            if (enabledText.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
                enabled = true;
            else if (enabledText.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
                enabled = false;
            else
                throw new M1CatalogException($"M1 驗收清單 {id} 的 Enabled 必須是 TRUE 或 FALSE，收到「{enabledText}」：{source}");

            var categoryText = Cell(categoryColumn);
            var category = categoryText switch
            {
                "Hard" => M1Category.Hard,
                "Warning" => M1Category.Warning,
                "Behavior Check" => M1Category.BehaviorCheck,
                _ => throw new M1CatalogException(
                    $"M1 驗收清單 {id} 的分類必須是 Hard、Warning 或 Behavior Check，收到「{categoryText}」：{source}")
            };

            var name = Cell(nameColumn);
            if (name.Length == 0)
                throw new M1CatalogException($"M1 驗收清單 {id} 缺少中文名稱：{source}");

            if (enabled)
                result.Add(new M1CheckDefinition(id, name, Cell(conditionColumn), category, categoryText, Cell(diagnosisColumn)));
        }

        if (result.Count == 0)
            throw new M1CatalogException($"M1 驗收清單沒有任何 Enabled=TRUE 的檢查：{source}");
        return result.AsReadOnly();
    }

    internal static List<List<string>> ParseCsv(string text, string source)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                inQuotes = true;
            }
            else if (c == ',')
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString());
                field.Clear();
                rows.Add(row);
                row = new List<string>();
            }
            else if (c != '\r' && c != '﻿')
            {
                field.Append(c);
            }
        }

        if (inQuotes)
            throw new M1CatalogException($"M1 驗收清單有未關閉的引號：{source}");
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }
}

/// <summary>M1 驗收使用的既有 TEMP 情境（測試 fixture，不是世界常數）。</summary>
public sealed record M1Preset(string Key, string Name, DemoWeeklyTempConfig Config)
{
    public static M1Preset Normal { get; } = new("normal", "一般示範", DemoWeeklyTempConfig.Sample);

    public static M1Preset LocalFamine { get; } = new("local-famine", "局部饑荒（聚落 #1）", DemoWeeklyTempConfig.LocalFamineSample);

    public static M1Preset Frontier { get; } = new("frontier", "開拓", DemoWeeklyTempConfig.FrontierSample);

    public static M1Preset Famine { get; } = new("famine", "全面饑荒", DemoWeeklyTempConfig.FamineSample);

    public static IReadOnlyList<M1Preset> All { get; } = new[] { Normal, LocalFamine, Frontier, Famine };

    /// <summary>預設矩陣：一般／局部饑荒（遷徙）／開拓（新聚落）。</summary>
    public static IReadOnlyList<M1Preset> Default { get; } = new[] { Normal, LocalFamine, Frontier };

    public static M1Preset? Find(string key) => All.FirstOrDefault(p => p.Key == key);
}

/// <summary>某一刻的世界總量（各聚落＋途中隊伍，只計一次；資源點存量）。</summary>
public readonly record struct M1WorldTotals(long Population, long Food, long Reserve);

/// <summary>期末聚落資訊（M1_VARIETY 用）。</summary>
public sealed record M1SettlementInfo(int Id, string Name, string ResourceKinds, bool Founded, long FinalPopulation);

/// <summary>
/// 一次長跑留下的證據：全部已發布週報，以及每次週結算發布後直接讀世界狀態得到的總量。
/// 評估只讀這份資料，所以測試可以竄改其中一筆來證明 Hard 失敗會被抓到。
/// </summary>
public sealed record M1RunData(
    ulong Seed,
    M1Preset Preset,
    int Years,
    int DaysPerYear,
    M1WorldTotals Initial,
    IReadOnlyList<int> InitialSettlementIds,
    IReadOnlyList<DemoWeekReport> Weeks,
    IReadOnlyDictionary<int, M1WorldTotals> TotalsAfterWeek,
    IReadOnlyDictionary<int, DemoCellRef> SettlementCells,
    IReadOnlyList<M1SettlementInfo> Settlements,
    string? AbortReason = null,
    IReadOnlyList<DemoSettlementCapacity>? FinalCapacities = null);

/// <summary>跑一個種子 × 情境 × 年數，收集證據。不改 Core 任何規則。</summary>
public static class M1RunCollector
{
    /// <param name="daysPerYear">#82：一年日數，來自正式設定 01 表 WORLD_DAYS_PER_YEAR（與 --demo --years 同一個年界）。</param>
    public static M1RunData Collect(ulong seed, M1Preset preset, int years, int daysPerYear)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (years is < 1 or > DemoCenturyRun.MaxYears)
            throw new ArgumentOutOfRangeException(nameof(years), $"年數必須是 1～{DemoCenturyRun.MaxYears}。");
        if (daysPerYear < 1 || (long)daysPerYear * years > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(daysPerYear), "一年日數必須是正整數。");

        var world = new DemoObserverWorld(DemoScenario.Sample(seed), preset.Config);
        var initialIds = world.Sites.Settlements.Select(s => s.Id).ToList();
        var initial = Totals(world);
        var totals = new Dictionary<int, M1WorldTotals>();
        string? abort = null;
        var seen = 0;
        var days = years * daysPerYear;
        for (var d = 0; d < days; d++)
        {
            try
            {
                world.AdvanceOneDay();
            }
            catch (Exception ex) when (ex is InvalidOperationException or OverflowException)
            {
                abort = $"第 {d + 1} 日推進失敗（Core 在發布前拒絕）：{ex.Message}";
                break;
            }

            var latest = world.LatestWeek;
            if (latest is null || latest.Week == seen)
                continue;
            seen = latest.Week;
            totals[latest.Week] = Totals(world);
        }

        var sites = world.Sites;
        var cells = sites.Settlements.ToDictionary(s => s.Id, s => new DemoCellRef(s.Column, s.Row));
        var infos = sites.Settlements
            .Select(s => new M1SettlementInfo(
                s.Id, s.Name,
                string.Join("／", sites.ResourcePointsOf(s.Id).Select(p => KindName(p.Kind)).Distinct()),
                s.IsFounded, world.SettlementPopulation(s.Id)))
            .ToList();
        return new M1RunData(seed, preset, years, daysPerYear, initial, initialIds, world.WeekReports(), totals, cells, infos, abort,
            world.SettlementCapacities());
    }

    private static M1WorldTotals Totals(DemoObserverWorld world)
        => new(
            world.Sites.Settlements.Sum(s => world.SettlementPopulation(s.Id)) + world.PopulationInTransit,
            world.Sites.Settlements.Sum(s => world.SettlementFood(s.Id)) + world.FoodInTransit,
            world.Sites.ResourcePoints.Sum(p => world.ResourceReserve(p.Id)));

    private static string KindName(DemoResourceKind kind) => kind switch
    {
        DemoResourceKind.Farmland => "農地",
        DemoResourceKind.Woodland => "林地",
        DemoResourceKind.Fishery => "漁場",
        _ => kind.ToString()
    };
}

/// <summary>單項結果。</summary>
public sealed record M1CheckResult(M1CheckDefinition Check, M1Verdict Verdict, string Diagnosis);

/// <summary>一個種子 × 情境的完整報告（順序與 45G 相同）。</summary>
public sealed record M1RunReport(M1RunData Run, IReadOnlyList<M1CheckResult> Results)
{
    public bool HasHardFailure => Results.Any(r => r.Verdict == M1Verdict.Fail);
}

/// <summary>量測的原始結論；再依 45G 分類換成判定。</summary>
internal enum M1Raw
{
    Ok,
    Violation,
    NotObserved,
    DataOnly,
    Unimplemented
}

internal readonly record struct M1Finding(M1Raw Raw, string Text);

/// <summary>逐項量測。每一項只用已發布週報與世界狀態能證明的事實。</summary>
public static class M1Evaluator
{
    private static readonly Dictionary<string, Func<M1RunData, M1Finding>> Measures = new(StringComparer.Ordinal)
    {
        ["M1_RES"] = Resources,
        ["M1_POP"] = PopulationLedger,
        ["M1_NO_MAGIC_FOOD"] = NoMagicFood,
        ["M1_ACCESS"] = Access,
        ["M1_FAMINE"] = Famine,
        ["M1_MIG"] = Migration,
        ["M1_SETTLE"] = Settle,
        ["M1_STABILITY"] = Stability,
        ["M1_VARIETY"] = Variety,
        ["M1_HISTORY"] = History
    };

    public static M1RunReport Evaluate(M1RunData run, IReadOnlyList<M1CheckDefinition> checks)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(checks);
        var results = new List<M1CheckResult>(checks.Count);
        foreach (var check in checks)
        {
            M1Finding finding;
            if (run.AbortReason is not null)
                finding = new M1Finding(M1Raw.Violation, $"長跑中止，沒有跑完 {run.Years} 年：{run.AbortReason}");
            else if (Measures.TryGetValue(check.Id, out var measure))
                finding = measure(run);
            else
                finding = new M1Finding(M1Raw.Unimplemented, "45G 已啟用此項，但觀察者還沒有對應的量測");
            results.Add(new M1CheckResult(check, ToVerdict(finding.Raw, check.Category), finding.Text));
        }

        return new M1RunReport(run, results.AsReadOnly());
    }

    /// <summary>多個種子 × 情境合併：任一失敗＝失敗；否則任一通過＝通過；全部尚未實作＝尚未實作；其餘＝警告。</summary>
    public static M1Verdict Aggregate(IEnumerable<M1Verdict> verdicts)
    {
        var list = verdicts.ToList();
        if (list.Count == 0)
            return M1Verdict.Unimplemented;
        if (list.Contains(M1Verdict.Fail))
            return M1Verdict.Fail;
        if (list.Contains(M1Verdict.Pass))
            return M1Verdict.Pass;
        if (list.All(v => v == M1Verdict.Unimplemented))
            return M1Verdict.Unimplemented;
        return M1Verdict.Warn;
    }

    public static string Label(M1Verdict verdict) => verdict switch
    {
        M1Verdict.Pass => "通過（PASS）",
        M1Verdict.Warn => "警告（WARN）",
        M1Verdict.Fail => "失敗（FAIL）",
        _ => "尚未實作（UNIMPLEMENTED）"
    };

    internal static M1Verdict ToVerdict(M1Raw raw, M1Category category) => raw switch
    {
        M1Raw.Ok => M1Verdict.Pass,
        M1Raw.Violation => category == M1Category.Hard ? M1Verdict.Fail : M1Verdict.Warn,
        M1Raw.Unimplemented => M1Verdict.Unimplemented,
        _ => M1Verdict.Warn
    };

    /// <summary>#82：第 N 次週結算在第 7N 日；歸屬結算日所在年份，年內週次＝該年的第幾次週結算。</summary>
    private static string At(M1RunData run, int week)
    {
        var day = (long)week * DemoCenturyRun.DaysPerWeeklySettlement;
        var year = (day - 1) / run.DaysPerYear + 1;
        var yearStart = (year - 1) * run.DaysPerYear;
        var weekInYear = week - yearStart / DemoCenturyRun.DaysPerWeeklySettlement;
        return $"種子 {run.Seed} 第 {year} 年第 {weekInYear} 週（第 {day} 日）";
    }

    private static string Name(DemoSettlementWeekLedger ledger) => $"{ledger.SettlementName}（#{ledger.SettlementId}）";

    private static M1Finding Judge(List<string> problems, string okText)
        => problems.Count == 0
            ? new M1Finding(M1Raw.Ok, okText)
            : new M1Finding(M1Raw.Violation, $"{problems.Count} 處違反；第一處：{problems[0]}");

    private static M1WorldTotals Final(M1RunData run)
        => run.Weeks.Count > 0 && run.TotalsAfterWeek.TryGetValue(run.Weeks[^1].Week, out var t) ? t : run.Initial;

    // ── M1_RES：每個資源點／期初庫存＋流入－流出＝結束，所有差額有原因 ──────────────
    private static M1Finding Resources(M1RunData run)
    {
        var problems = new List<string>();
        var expectedWeeks = DemoCenturyRun.WeeklySettlementsThrough(run.Years * run.DaysPerYear);
        if (run.Weeks.Count != expectedWeeks)
            problems.Add($"週結算次數 {run.Weeks.Count}，{run.Years} 年應為 {expectedWeeks}");

        long ledgers = 0, migrationLedgers = 0, produced = 0, consumed = 0, regenerated = 0, harvested = 0, shortage = 0;
        var previous = run.Initial;
        for (var i = 0; i < run.Weeks.Count; i++)
        {
            var report = run.Weeks[i];
            if (report.Week != i + 1)
                problems.Add($"週報編號不連續：第 {i + 1} 筆是第 {report.Week} 週");
            long wProduced = 0, wConsumed = 0, wRegen = 0, wHarvested = 0;
            foreach (var l in report.Settlements)
            {
                ledgers++;
                if (!l.ReserveBalanced)
                    problems.Add($"{At(run, report.Week)} {Name(l)} 資源點存量帳：{l.ReserveOpening}＋再生 {l.Regenerated}－採收 {l.Harvested}≠{l.ReserveClosing}");
                if (!l.FoodBalanced)
                    problems.Add($"{At(run, report.Week)} {Name(l)} 糧食帳：{l.FoodOpening}＋生產 {l.FoodProduced}＋帶入 {l.FoodCarriedIn}－消耗 {l.FoodConsumed}－帶出 {l.FoodCarriedOut}≠{l.FoodClosing}");
                if (!l.TotalResourceBalanced)
                    problems.Add($"{At(run, report.Week)} {Name(l)} 總資源（存量＋糧食）帳不平衡");
                if (!l.HungerDebtBalanced)
                    problems.Add($"{At(run, report.Week)} {Name(l)} 缺糧差額沒有完整記入飢餓債");
                if (!l.NonNegative)
                    problems.Add($"{At(run, report.Week)} {Name(l)} 出現負值");
                wProduced += l.FoodProduced;
                wConsumed += l.FoodConsumed;
                wRegen += l.Regenerated;
                wHarvested += l.Harvested;
                shortage += l.FoodShortage;
            }

            foreach (var m in report.Migrations)
            {
                migrationLedgers++;
                if (!m.FoodBalanced)
                    problems.Add($"{At(run, report.Week)} 遷徙隊伍 #{m.BatchId} 糧食帳：{m.FoodOpening}－消耗 {m.FoodConsumed}≠{m.FoodClosing}");
                if (!m.NonNegative)
                    problems.Add($"{At(run, report.Week)} 遷徙隊伍 #{m.BatchId} 出現負值");
                wConsumed += m.FoodConsumed;
                shortage += m.FoodShortage;
            }

            // 兩端獨立對帳：週報流量 vs. 發布後直接讀到的世界狀態。
            if (!run.TotalsAfterWeek.TryGetValue(report.Week, out var after))
            {
                problems.Add($"{At(run, report.Week)} 缺少世界狀態總量，無法對帳");
            }
            else
            {
                if (previous.Food + wProduced - wConsumed != after.Food)
                    problems.Add($"{At(run, report.Week)} 世界糧食：{previous.Food}＋生產 {wProduced}－消耗 {wConsumed}≠世界狀態 {after.Food}");
                if (previous.Reserve + wRegen - wHarvested != after.Reserve)
                    problems.Add($"{At(run, report.Week)} 世界資源點存量：{previous.Reserve}＋再生 {wRegen}－採收 {wHarvested}≠世界狀態 {after.Reserve}");
                previous = after;
            }

            produced += wProduced;
            consumed += wConsumed;
            regenerated += wRegen;
            harvested += wHarvested;
        }

        var final = Final(run);
        return Judge(problems,
            $"{run.Weeks.Count} 週、聚落帳 {ledgers} 筆、遷徙隊伍帳 {migrationLedgers} 筆全部平衡；"
            + $"世界糧食 期初 {run.Initial.Food}＋生產 {produced}－消耗 {consumed}＝期末 {final.Food}；"
            + $"資源點存量 期初 {run.Initial.Reserve}＋再生 {regenerated}－採收 {harvested}＝期末 {final.Reserve}；"
            + $"缺糧合計 {shortage} 全部記入飢餓債（逐週與世界狀態對帳）");
    }

    // ── M1_POP：出生／死亡／遷徙／具體化全部對帳 ─────────────────────────────────
    private static M1Finding PopulationLedger(M1RunData run)
    {
        var problems = new List<string>();
        long births = 0, settledStarvation = 0, naturalDeaths = 0, roadDeaths = 0, emigrants = 0, immigrants = 0;
        var previous = run.Initial;
        DemoWeekReport? previousReport = null;
        foreach (var report in run.Weeks)
        {
            long wBirths = 0, wDeaths = 0;
            foreach (var l in report.Settlements)
            {
                if (!l.PopulationBalanced)
                    problems.Add($"{At(run, report.Week)} {Name(l)} 人口帳：{l.PopulationOpening}＋出生 {l.Births}－死亡 {l.Deaths}＋遷入 {l.Immigrants}－遷出 {l.Emigrants}≠{l.PopulationClosing}");

                // #98：逐聚落銜接（世界總量平衡也可能藏著「甲少一人、乙多一人」）：期初＝上週期末；新聚落期初 0。
                if (previousReport is not null)
                {
                    var before = previousReport.Settlements.FirstOrDefault(p => p.SettlementId == l.SettlementId);
                    var expectedOpening = before?.PopulationClosing ?? 0;
                    if (l.PopulationOpening != expectedOpening)
                        problems.Add($"{At(run, report.Week)} {Name(l)} 期初人口 {l.PopulationOpening}≠上週期末 {expectedOpening}（人口不能在週與週之間憑空增減或搬移）");
                }

                // #98／C02：出生必須逐筆有帶原因的出生事件（人類繁衍情境才會發生）；人數要與本週該聚落 Birth 事件合計一致。
                var birthEvents = report.Events
                    .Where(e => e.SettlementId == l.SettlementId && e.Kind == DemoSettlementEventKind.Birth)
                    .ToList();
                var birthEventCount = birthEvents.Sum(e => e.Count);
                if (birthEventCount != l.Births || birthEvents.Any(e => string.IsNullOrWhiteSpace(e.Cause)))
                    problems.Add($"{At(run, report.Week)} {Name(l)} 帳上出生 {l.Births} 人，帶原因的出生事件 {birthEventCount} 人"
                        + (birthEventCount == 0 ? "（沒有任何帶原因的出生事件）" : ""));

                // #98：遷出／遷入兩邊都要有事件與隊伍帳，人數一致（漏記或改人數都抓得到）。
                var leaveEvents = report.Events
                    .Where(e => e.SettlementId == l.SettlementId && e.Kind == DemoSettlementEventKind.Emigration)
                    .Sum(e => e.Count);
                var departedPeople = report.Migrations
                    .Where(m => m.Departed && m.FromSettlementId == l.SettlementId)
                    .Sum(m => m.PopulationOpening);
                if (leaveEvents != l.Emigrants || departedPeople != l.Emigrants)
                    problems.Add($"{At(run, report.Week)} {Name(l)} 遷出 {l.Emigrants} 人，外移事件 {leaveEvents} 人、隊伍出發帳 {departedPeople} 人");
                var arriveEvents = report.Events
                    .Where(e => e.SettlementId == l.SettlementId
                        && e.Kind is DemoSettlementEventKind.Arrival or DemoSettlementEventKind.SettlementFounded)
                    .Sum(e => e.Count);
                var arrivedPeople = report.Migrations
                    .Where(m => m.Arrived && m.ToSettlementId == l.SettlementId)
                    .Sum(m => m.PopulationClosing);
                if (arriveEvents != l.Immigrants || arrivedPeople != l.Immigrants)
                    problems.Add($"{At(run, report.Week)} {Name(l)} 遷入 {l.Immigrants} 人，抵達／建立事件 {arriveEvents} 人、隊伍抵達帳 {arrivedPeople} 人");
                // C02：死亡按原因分類對帳——餓死對 StarvationDeath 事件、自然死亡對 NaturalDeath 事件。
                var causedDeaths = report.Events
                    .Where(e => e.SettlementId == l.SettlementId && e.Kind == DemoSettlementEventKind.StarvationDeath)
                    .ToList();
                if (causedDeaths.Sum(e => e.Count) != l.StarvationDeaths || causedDeaths.Any(e => string.IsNullOrWhiteSpace(e.Cause)))
                    problems.Add($"{At(run, report.Week)} {Name(l)} 餓死 {l.StarvationDeaths} 人，帶原因的餓死事件 {causedDeaths.Sum(e => e.Count)} 人");
                var naturalEvents = report.Events
                    .Where(e => e.SettlementId == l.SettlementId && e.Kind == DemoSettlementEventKind.NaturalDeath)
                    .ToList();
                if (naturalEvents.Sum(e => e.Count) != l.NaturalDeaths || naturalEvents.Any(e => string.IsNullOrWhiteSpace(e.Cause)))
                    problems.Add($"{At(run, report.Week)} {Name(l)} 自然死亡 {l.NaturalDeaths} 人，帶原因的自然死亡事件 {naturalEvents.Sum(e => e.Count)} 人");
                wBirths += l.Births;
                wDeaths += l.Deaths;
                settledStarvation += l.StarvationDeaths;
                naturalDeaths += l.NaturalDeaths;
                emigrants += l.Emigrants;
                immigrants += l.Immigrants;
            }

            foreach (var m in report.Migrations)
            {
                if (!m.PopulationBalanced)
                    problems.Add($"{At(run, report.Week)} 遷徙隊伍 #{m.BatchId} 人口帳：{m.PopulationOpening}－死亡 {m.Deaths}≠{m.PopulationClosing}");

                // #98：途中隊伍逐週銜接：本週期初＝上週期末（上週仍在路上）。
                if (!m.Departed)
                {
                    var before = previousReport?.Migrations.FirstOrDefault(p => p.BatchId == m.BatchId);
                    if (before is null || before.Arrived || before.PopulationClosing != m.PopulationOpening)
                        problems.Add($"{At(run, report.Week)} 遷徙隊伍 #{m.BatchId} 期初 {m.PopulationOpening} 人，上週期末 {(before is null ? "無紀錄" : before.Arrived ? "已抵達" : before.PopulationClosing.ToString(CultureInfo.InvariantCulture) + " 人")}");
                }
                var causedDeaths = report.Events
                    .Where(e => e.MigrationBatchId == m.BatchId && e.Kind == DemoSettlementEventKind.MigrantStarvationDeath)
                    .ToList();
                if (causedDeaths.Sum(e => e.Count) != m.Deaths || causedDeaths.Any(e => string.IsNullOrWhiteSpace(e.Cause)))
                    problems.Add($"{At(run, report.Week)} 遷徙隊伍 #{m.BatchId} 途中死亡 {m.Deaths} 人，帶原因事件 {causedDeaths.Sum(e => e.Count)} 人");
                wDeaths += m.Deaths;
                roadDeaths += m.Deaths;
            }

            births += wBirths;
            if (!run.TotalsAfterWeek.TryGetValue(report.Week, out var after))
            {
                problems.Add($"{At(run, report.Week)} 缺少世界狀態總量，無法對帳");
            }
            else
            {
                if (previous.Population + wBirths - wDeaths != after.Population)
                    problems.Add($"{At(run, report.Week)} 世界人口（含途中）：{previous.Population}＋出生 {wBirths}－死亡 {wDeaths}≠世界狀態 {after.Population}");
                previous = after;
            }

            previousReport = report;
        }

        var final = Final(run);
        return Judge(problems,
            $"世界人口（各聚落＋途中隊伍，只計一次）期初 {run.Initial.Population}＋出生 {births}"
            + $"－死亡 {settledStarvation + naturalDeaths + roadDeaths}（聚落餓死 {settledStarvation}、自然死亡 {naturalDeaths}、途中餓死 {roadDeaths}，全部有帶原因事件）"
            + $"＝期末 {final.Population}；遷出 {emigrants}、遷入 {immigrants}（差額＝途中與途中死亡，見 M1_MIG）；"
            + "逐聚落、逐隊伍期初＝上週期末，遷出／遷入人數與外移／抵達事件及隊伍帳一致"
            + "；出生與自然死亡逐聚落對帶原因的出生／自然死亡事件"
            + "。【覆蓋限制 TEMP】出生只在人類繁衍情境發生，預設矩陣不含該情境；"
            + "具體化：個體具體化尚未實作（未實作，不是通過），沒有量測；這兩項只確認沒有憑空增減，不代表已驗證");
    }

    // ── M1_NO_MAGIC_FOOD：食物增量只能來自合法採集／農牧／配方／神蹟定義 ──────────
    private static M1Finding NoMagicFood(M1RunData run)
    {
        var problems = new List<string>();
        long produced = 0, farming = 0, gathering = 0;
        foreach (var report in run.Weeks)
        {
            foreach (var l in report.Settlements)
            {
                if (!l.NoMagicFood)
                    problems.Add($"{At(run, report.Week)} {Name(l)} 生產 {l.FoodProduced}，但實際採收 {l.Harvested}（農牧 {l.FoodFromFarming}＋採集 {l.FoodFromGathering}）");
                produced += l.FoodProduced;
                farming += l.FoodFromFarming;
                gathering += l.FoodFromGathering;
            }

            foreach (var m in report.Migrations)
            {
                if (m.FoodClosing > m.FoodOpening)
                    problems.Add($"{At(run, report.Week)} 遷徙隊伍 #{m.BatchId} 途中糧食增加 {m.FoodOpening}→{m.FoodClosing}（途中沒有生產來源）");
            }
        }

        return Judge(problems,
            produced == 0
                ? "本情境沒有任何糧食生產，也沒有任何憑空增加的糧食；配方、神蹟尚未實作（產量 0）"
                : $"糧食生產合計 {produced} 全部來自資源點實際採收：農牧 {farming}、採集 {gathering}；途中隊伍沒有生產；配方、神蹟尚未實作（產量 0）");
    }

    // ── M1_ACCESS：存在有糧但部分取用分組飢餓的情境可合法出現 ─────────────────────
    private static M1Finding Access(M1RunData run)
    {
        foreach (var report in run.Weeks)
        {
            var hungry = report.Settlements.FirstOrDefault(l => l.FoodShortage > 0);
            if (hungry is null)
                continue;
            var stocked = report.Settlements.FirstOrDefault(l => l.FoodClosing > 0 && l.SettlementId != hungry.SettlementId);
            if (stocked is null)
                continue;
            return new M1Finding(M1Raw.Unimplemented,
                "聚落內分組取用尚未實作（示範聚落整體共用糧倉，不會出現同一聚落有糧但部分分組挨餓）；"
                + $"跨聚落已可觀察：{At(run, report.Week)} {Name(hungry)} 缺糧 {hungry.FoodShortage} 時，"
                + $"{Name(stocked)} 庫存 {stocked.FoodClosing}（世界有糧但取不到）");
        }

        return new M1Finding(M1Raw.Unimplemented,
            "聚落內分組取用尚未實作（示範聚落整體共用糧倉）；本情境也沒有出現跨聚落「一處缺糧、另一處有糧」的週");
    }

    // ── M1_FAMINE：缺糧沿需求→死亡／遷徙→勞動力→實際採收傳導（量測，不推斷），恢復也有實際來源 ──
    private static M1Finding Famine(M1RunData run)
    {
        var events = run.Weeks.SelectMany(w => w.Events).ToList();
        var ledgerOf = new Dictionary<(int Week, int Settlement), DemoSettlementWeekLedger>();
        foreach (var report in run.Weeks)
            foreach (var l in report.Settlements)
                ledgerOf[(report.Week, l.SettlementId)] = l;

        // 恢復一定要有實際糧食來源。
        var problems = new List<string>();
        DemoSettlementEvent? recovery = null;
        DemoSettlementWeekLedger? recoveryLedger = null;
        foreach (var e in events.Where(x => x.Kind == DemoSettlementEventKind.HungerRecovered))
        {
            if (!ledgerOf.TryGetValue((e.Week, e.SettlementId), out var l)
                || l.FoodShortage != 0 || l.FoodConsumed <= 0 || l.FoodOpening + l.FoodProduced + l.FoodCarriedIn <= 0)
            {
                problems.Add($"{At(run, e.Week)} {e.SettlementName}（#{e.SettlementId}）飢餓解除，但該週沒有實際糧食來源");
                continue;
            }

            if (recovery is null)
            {
                recovery = e;
                recoveryLedger = l;
            }
        }

        if (problems.Count > 0)
            return new M1Finding(M1Raw.Violation, $"{problems.Count} 處違反；第一處：{problems[0]}");

        var firstHunger = events.FirstOrDefault(e => e.Kind == DemoSettlementEventKind.Hunger);
        if (firstHunger is null)
            return new M1Finding(M1Raw.NotObserved, $"本情境 {run.Years} 年內沒有缺糧，無法觀察危機傳導（不是通過）");

        // 生產力是量測值，不是由人口前後推斷：每週採收不得超過「期初人口×TEMP 每人每週產量」的勞動力上限。
        var yieldPerWorker = run.Preset.Config.YieldPerWorkerPerWeek;
        foreach (var report in run.Weeks)
        {
            foreach (var l in report.Settlements)
            {
                var cap = l.PopulationOpening * yieldPerWorker;
                if (l.FoodProduced > cap)
                    problems.Add($"{At(run, report.Week)} {Name(l)} 採收 {l.FoodProduced} 超過勞動力上限 {cap}"
                        + $"（期初人口 {l.PopulationOpening}×TEMP 每人每週產量 {yieldPerWorker}）");
            }
        }

        if (problems.Count > 0)
            return new M1Finding(M1Raw.Violation, $"{problems.Count} 處違反；第一處：{problems[0]}");

        // 需求→死亡／遷徙→勞動力→生產力：找缺糧後同一聚落的餓死或外移，而且
        // 效果當週實際採收＝舊勞動力上限、資源點有剩（前週確實受勞力限制、不是 0 或受存量限制），
        // 下一週實際採收＝降低後的新上限、資源點仍有剩，且採收確實下降，才算量測到生產力傳導。
        string? chain = null;
        string? unmeasured = null;
        var hungersOf = events.Where(e => e.Kind == DemoSettlementEventKind.Hunger)
            .GroupBy(e => e.SettlementId)
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var effect in events.Where(e => e.Kind == DemoSettlementEventKind.StarvationDeath || e.Kind == DemoSettlementEventKind.Emigration))
        {
            // 原因：同一聚落在效果當週或之前最近一次缺糧（事件依週次發布，清單已排序）。
            if (!hungersOf.TryGetValue(effect.SettlementId, out var hungers))
                continue;
            var hunger = hungers.LastOrDefault(e => e.Week <= effect.Week);
            if (hunger is null)
                continue;
            var what = effect.Kind == DemoSettlementEventKind.StarvationDeath ? $"{effect.Count} 人餓死" : $"{effect.Count} 人外移";
            var head = $"{At(run, hunger.Week)} {hunger.SettlementName}（#{hunger.SettlementId}）缺糧 {hunger.Count}（{hunger.Cause}）"
                + $"→ {At(run, effect.Week)} {what}";
            if (!ledgerOf.TryGetValue((effect.Week, effect.SettlementId), out var during)
                || !ledgerOf.TryGetValue((effect.Week + 1, effect.SettlementId), out var next))
            {
                unmeasured ??= $"{head}，但效果發生在最後一週，下週生產力不在量測範圍";
                continue;
            }

            var capBefore = during.PopulationOpening * yieldPerWorker;
            var capAfter = next.PopulationOpening * yieldPerWorker;
            var beforeBound = during.FoodProduced == capBefore && during.ReserveClosing > 0;
            var afterBound = next.FoodProduced == capAfter && next.ReserveClosing > 0;
            var laborBound = capAfter < capBefore && beforeBound && afterBound && next.FoodProduced < during.FoodProduced;
            if (!laborBound)
            {
                unmeasured ??= $"{head}，但勞動力上限 {capBefore}→{capAfter}、實際採收 {during.FoodProduced}→{next.FoodProduced}"
                    + $"（資源點剩 {during.ReserveClosing}→{next.ReserveClosing}）：前後兩週不是都受勞力限制（產量受資源點存量限制、"
                    + "前週採收未達舊上限或勞動力未下降），量測不到生產力因人口減少而下降";
                continue;
            }

            chain = $"{head} → 下週勞動力 {during.PopulationOpening}→{next.PopulationOpening} 人，"
                + $"勞動力上限 {capBefore}→{capAfter}（TEMP 每人每週產量 {yieldPerWorker}），"
                + $"實際採收 {during.FoodProduced}＝前週舊上限 → {next.FoodProduced}＝新上限"
                + $"（資源點剩 {during.ReserveClosing}→{next.ReserveClosing}，前後兩週產量受勞力限制）";
            break;
        }

        if (chain is null && unmeasured is not null)
            return new M1Finding(M1Raw.NotObserved, $"{unmeasured}（不是通過）");
        if (chain is null)
            return new M1Finding(M1Raw.NotObserved,
                $"有缺糧（{At(run, firstHunger.Week)} {firstHunger.SettlementName}），但 {run.Years} 年內沒有傳導到餓死或遷徙");
        if (recovery is null || recoveryLedger is null)
            return new M1Finding(M1Raw.NotObserved, $"{chain}；但本情境沒有出現飢餓解除，無法驗證恢復來源");

        return new M1Finding(M1Raw.Ok,
            $"{chain}；恢復：{At(run, recovery.Week)} {recovery.SettlementName}（#{recovery.SettlementId}）飢餓解除，"
            + $"來源 庫存 {recoveryLedger.FoodOpening}＋採收 {recoveryLedger.FoodProduced}＋帶入 {recoveryLedger.FoodCarriedIn}");
    }

    // ── M1_MIG：來源／途中／目的地人口帳一致；路線／成本有效 ──────────────────────
    private static M1Finding Migration(M1RunData run)
    {
        var problems = new List<string>();
        var departed = new Dictionary<int, (int Week, int From)>();
        var alive = new Dictionary<int, DemoMigrationWeekLedger>();
        int arrivals = 0, foundingArrivals = 0, moves = 0;
        long roadDeaths = 0;
        string? example = null;
        foreach (var report in run.Weeks)
        {
            var thisWeek = new Dictionary<int, DemoMigrationWeekLedger>();
            foreach (var m in report.Migrations)
            {
                var at = $"{At(run, report.Week)} 遷徙隊伍 #{m.BatchId}";
                if (!thisWeek.TryAdd(m.BatchId, m))
                {
                    problems.Add($"{at} 同一週出現兩筆帳");
                    continue;
                }

                if (!m.PopulationBalanced)
                    problems.Add($"{at} 途中人口帳不平衡");
                if (!m.NoTeleport)
                    problems.Add($"{at} 瞬移：{m.CellBefore}→{m.CellAfter} 不是原地或相鄰格");

                if (m.Departed)
                {
                    if (!departed.TryAdd(m.BatchId, (report.Week, m.FromSettlementId)) || alive.ContainsKey(m.BatchId))
                        problems.Add($"{at} 編號重複出發");
                }
                else
                {
                    if (!alive.TryGetValue(m.BatchId, out var previous))
                    {
                        problems.Add($"{at} 沒有上一週的紀錄（憑空出現）");
                    }
                    else if (previous.PopulationClosing != m.PopulationOpening || previous.FoodClosing != m.FoodOpening
                             || previous.HungerDebtClosing != m.HungerDebtOpening || previous.CellAfter != m.CellBefore)
                    {
                        problems.Add($"{at} 跨週不連續：上週期末 {previous.PopulationClosing} 人在 {previous.CellAfter}，本週期初 {m.PopulationOpening} 人在 {m.CellBefore}");
                    }

                    // 成本：途中每週需求＝人數×TEMP 每人每週糧食，取得不足的部分記為缺糧。
                    var demand = m.PopulationOpening * run.Preset.Config.FoodPerPersonPerWeek;
                    if (m.FoodConsumed + m.FoodShortage != demand)
                        problems.Add($"{at} 途中成本不符：消耗 {m.FoodConsumed}＋缺糧 {m.FoodShortage}≠需求 {demand}");
                    if (m.CellAfter != m.CellBefore)
                        moves++;
                    roadDeaths += m.Deaths;
                }

                if (m.Arrived)
                {
                    arrivals++;
                    if (m.Founding)
                        foundingArrivals++;
                    if (!run.SettlementCells.TryGetValue(m.ToSettlementId, out var cell) || cell != m.CellAfter)
                        problems.Add($"{at} 抵達的格 {m.CellAfter} 不是目的聚落 #{m.ToSettlementId} 的位置");
                    if (example is null && departed.TryGetValue(m.BatchId, out var origin))
                        example = $"{At(run, origin.Week)} 自 #{origin.From} 出發的隊伍 #{m.BatchId}，"
                            + $"走到 {m.CellAfter}，{At(run, report.Week)} 以 {m.PopulationClosing} 人抵達 #{m.ToSettlementId}";
                }
            }

            foreach (var id in alive.Keys.Where(k => !thisWeek.ContainsKey(k)))
                problems.Add($"{At(run, report.Week)} 遷徙隊伍 #{id} 消失：沒有抵達也沒有全數死亡的紀錄");

            // 來源／目的地兩端：聚落遷出＝本週出發隊伍人數；聚落遷入＝本週抵達隊伍人數。
            foreach (var l in report.Settlements)
            {
                var outbound = report.Migrations.Where(m => m.Departed && m.FromSettlementId == l.SettlementId).Sum(m => m.PopulationOpening);
                var inbound = report.Migrations.Where(m => m.Arrived && m.ToSettlementId == l.SettlementId).Sum(m => m.PopulationClosing);
                if (outbound != l.Emigrants || inbound != l.Immigrants)
                    problems.Add($"{At(run, report.Week)} {Name(l)} 兩端對不上：遷出 {l.Emigrants}／出發隊伍 {outbound}，遷入 {l.Immigrants}／抵達隊伍 {inbound}");
            }

            alive = thisWeek.Where(kv => !kv.Value.Arrived && kv.Value.PopulationClosing > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        if (problems.Count > 0)
            return new M1Finding(M1Raw.Violation, $"{problems.Count} 處違反；第一處：{problems[0]}");
        if (departed.Count == 0)
            return new M1Finding(M1Raw.NotObserved, $"本情境 {run.Years} 年內沒有遷徙，沒有可驗證的遷徙帳（沒有違反，但不算通過）");

        return new M1Finding(M1Raw.Ok,
            $"{departed.Count} 支隊伍出發、{arrivals} 支抵達（其中開拓 {foundingArrivals}）、期末仍在途中 {alive.Count} 支、途中餓死 {roadDeaths} 人；"
            + $"移動 {moves} 格次全部是相鄰格、跨週連續；來源遷出＝出發隊伍、目的地遷入＝抵達隊伍，逐週一致；"
            + "途中糧食消耗＋缺糧＝人數×TEMP 每人每週需求"
            + (example is null ? string.Empty : $"。例：{example}"));
    }

    // ── M1_SETTLE：建立人口／物資從來源扣除，失敗也能回寫 ─────────────────────────
    private static M1Finding Settle(M1RunData run)
    {
        var problems = new List<string>();
        var known = new HashSet<int>(run.InitialSettlementIds);
        var foundingDepartures = new Dictionary<int, (int Week, int From, long People)>();
        int founded = 0, failed = 0;
        string? example = null;
        foreach (var report in run.Weeks)
        {
            foreach (var m in report.Migrations.Where(x => x.Founding && x.Departed))
            {
                foundingDepartures[m.BatchId] = (report.Week, m.FromSettlementId, m.PopulationOpening);
                var source = report.Settlements.FirstOrDefault(l => l.SettlementId == m.FromSettlementId);
                if (source is null || source.Emigrants < m.PopulationOpening || source.FoodCarriedOut < m.FoodOpening)
                    problems.Add($"{At(run, report.Week)} 開拓隊 #{m.BatchId} 的人口／糧食沒有從來源聚落 #{m.FromSettlementId} 扣除");
            }

            foreach (var m in report.Migrations.Where(x => x.Founding && !x.Departed && !x.Arrived && x.PopulationClosing == 0))
            {
                failed++;
                if (!report.Events.Any(e => e.MigrationBatchId == m.BatchId
                        && e.Kind == DemoSettlementEventKind.MigrantStarvationDeath && !string.IsNullOrWhiteSpace(e.Cause)))
                    problems.Add($"{At(run, report.Week)} 開拓隊 #{m.BatchId} 全數死亡，但沒有回寫帶原因的失敗事件");
            }

            var foundedEvents = report.Events.Where(e => e.Kind == DemoSettlementEventKind.SettlementFounded).ToList();
            var newIds = report.Settlements.Select(l => l.SettlementId).Where(id => !known.Contains(id)).OrderBy(id => id).ToList();
            var eventIds = foundedEvents.Select(e => e.SettlementId).OrderBy(id => id).ToList();
            if (!newIds.SequenceEqual(eventIds))
                problems.Add($"{At(run, report.Week)} 新出現的聚落 [{string.Join(",", newIds)}] 與建立事件 [{string.Join(",", eventIds)}] 對不上（憑空出現）");

            foreach (var e in foundedEvents)
            {
                var batch = report.Migrations.FirstOrDefault(m => m.Founding && m.Arrived && m.BatchId == e.MigrationBatchId);
                var ledger = report.Settlements.FirstOrDefault(l => l.SettlementId == e.SettlementId);
                var at = $"{At(run, report.Week)} 新聚落 {e.SettlementName}（#{e.SettlementId}）";
                if (batch is null || ledger is null)
                {
                    problems.Add($"{at} 找不到建立它的開拓隊或它的帳");
                    continue;
                }

                if (!foundingDepartures.TryGetValue(batch.BatchId, out var origin))
                    problems.Add($"{at} 的開拓隊 #{batch.BatchId} 沒有出發紀錄");
                if (ledger.PopulationOpening != 0 || ledger.FoodOpening != 0 || ledger.HungerDebtOpening != 0
                    || ledger.ReserveOpening != 0 || ledger.Births != 0)
                    problems.Add($"{at} 期初不是全 0（人口 {ledger.PopulationOpening}、糧食 {ledger.FoodOpening}、存量 {ledger.ReserveOpening}）");
                if (ledger.Immigrants != batch.PopulationClosing || e.Count != batch.PopulationClosing || e.Count <= 0
                    || ledger.FoodCarriedIn != batch.FoodClosing)
                    problems.Add($"{at} 人口／糧食不是全部由開拓隊帶入（事件 {e.Count} 人、遷入 {ledger.Immigrants}、隊伍抵達 {batch.PopulationClosing}）");

                founded++;
                if (example is null && foundingDepartures.TryGetValue(batch.BatchId, out origin))
                    example = $"{At(run, origin.Week)} #{origin.From} 派出 {origin.People} 人開拓隊 #{batch.BatchId} → "
                        + $"{At(run, report.Week)} 以 {batch.PopulationClosing} 人、糧食 {batch.FoodClosing} 建立 {e.SettlementName}（#{e.SettlementId}）";
            }

            known.UnionWith(report.Settlements.Select(l => l.SettlementId));
        }

        if (problems.Count > 0)
            return new M1Finding(M1Raw.Violation, $"{problems.Count} 處違反；第一處：{problems[0]}");
        if (founded == 0)
            return new M1Finding(M1Raw.NotObserved,
                foundingDepartures.Count == 0
                    ? $"本情境 {run.Years} 年內沒有開拓（開拓只在 TEMP 開拓情境開啟），沒有可驗證的新聚落（不算通過）"
                    : $"出發開拓 {foundingDepartures.Count} 支、失敗 {failed} 支（失敗已回寫），但沒有建立成功的聚落可驗證（不算通過）");

        return new M1Finding(M1Raw.Ok,
            $"建立新聚落 {founded} 個；開拓失敗 {failed} 支（全數途中餓死並留下帶原因事件，沒有留下聚落）；"
            + "新聚落期初人口／糧食／飢餓債／資源存量皆為 0，人口與糧食全部由開拓隊帶入，來源聚落在出發週扣除"
            + (example is null ? string.Empty : $"。例：{example}"));
    }

    // ── M1_STABILITY（Warning）：只列數據，正式門檻未定案 ─────────────────────────
    private static M1Finding Stability(M1RunData run)
    {
        var final = Final(run);
        var populations = run.TotalsAfterWeek.Values.Select(t => t.Population).Append(run.Initial.Population).ToList();
        var inhabited = run.Weeks.Count == 0 ? 0 : run.Weeks[^1].Settlements.Count(l => l.PopulationClosing > 0);
        var settlements = run.Weeks.Count == 0 ? run.InitialSettlementIds.Count : run.Weeks[^1].Settlements.Count;
        var note = final.Population == 0 ? "；本種子在期內全滅" : string.Empty;
        return new M1Finding(M1Raw.DataOnly,
            $"人口 期初 {run.Initial.Population} → 期末 {final.Population}（{run.Years} 年內最低 {populations.Min()}、最高 {populations.Max()}）；"
            + $"期末有人聚落 {inhabited}／{settlements}{note}；{CapacityEvidence(run)}"
            + "「穩定」的正式門檻未定案，只列數據不判定（TEMP，見 #75）");
    }

    /// <summary>
    /// #103：長期存在 vs 逐漸滅絕的可重現證據（只列數據）。每個聚落：每週再生、長期可養活人數、期初→期末人口、
    /// 期內缺糧週數與餓死人數；TEST 饑荒聚落（不再生）另外標出，不與一般聚落混為一談。
    /// </summary>
    private static string CapacityEvidence(M1RunData run)
    {
        var capacities = run.FinalCapacities;
        if (capacities is null || capacities.Count == 0)
            return string.Empty;
        var initialPopulation = run.Weeks.Count == 0
            ? new Dictionary<int, long>()
            : run.Weeks[0].Settlements.ToDictionary(l => l.SettlementId, l => l.PopulationOpening);
        var parts = new List<string>();
        foreach (var c in capacities)
        {
            var ledgers = run.Weeks.SelectMany(w => w.Settlements).Where(l => l.SettlementId == c.SettlementId).ToList();
            var shortWeeks = ledgers.Count(l => l.FoodShortage > 0);
            var deaths = ledgers.Sum(l => l.Deaths);
            var opening = initialPopulation.TryGetValue(c.SettlementId, out var p0) ? p0 : 0;
            var limit = c.SustainablePopulation == long.MaxValue ? "不限" : c.SustainablePopulation.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var fate = c.Population == 0 ? "期末無人" : c.LongTermSustainable ? "期末可長期維持" : $"期末仍每週缺 {c.LongTermGap}";
            parts.Add($"{c.SettlementName}（{c.ResourceKinds}{(c.TempFamineSettlement ? "，TEST 饑荒：不再生" : string.Empty)}，"
                + $"再生 {c.RegenPerWeek}／週→長期可養活 {limit} 人）人口 {opening}→{c.Population}、缺糧 {shortWeeks} 週、餓死 {deaths}，{fate}");
        }

        return $"各聚落承載（TEMP 估計：存量與糧庫耗盡後每週再生 vs 需求）：{string.Join("；", parts)}；";
    }

    // ── M1_VARIETY（Warning）：只列各聚落差異，氣候與產業尚未實作 ──────────────────
    private static M1Finding Variety(M1RunData run)
    {
        var parts = run.Settlements
            .Select(s => $"{s.Name}（#{s.Id}，{(s.ResourceKinds.Length == 0 ? "無資源點" : s.ResourceKinds)}{(s.Founded ? "，新建" : string.Empty)}）{s.FinalPopulation} 人")
            .ToList();
        return new M1Finding(M1Raw.DataOnly,
            $"期末各聚落：{string.Join("、", parts)}；氣候、產業尚未實作，地區差異的正式門檻未定案，"
            + "只列數據，不以種子不同代替環境差異（TEMP，見 #75）");
    }

    // ── M1_HISTORY（Behavior Check）：地方史看得出原因→效果（#88：43F 因果索引） ─────────
    private static M1Finding History(M1RunData run)
    {
        var events = run.Weeks.SelectMany(w => w.Events).ToList();
        var uncaused = events.FirstOrDefault(e => e.Kind != DemoSettlementEventKind.HungerRecovered && string.IsNullOrWhiteSpace(e.Cause));
        if (uncaused is not null)
            return new M1Finding(M1Raw.Violation, $"{At(run, uncaused.Week)} {uncaused.SettlementName} 的 {uncaused.Kind} 事件沒有原因");

        // 真實因果證據：每筆事件都有連續不重用的事件編號與直接原因（CAUSE_PARENT），
        // 原因必須更早、種類與聚落／隊伍相符，而且與週帳（缺糧、隊伍出發帳）一致。
        var index = DemoCausalIndex.Build(run.Weeks);
        if (index.Problems.Count > 0)
            return new M1Finding(M1Raw.Violation,
                $"因果證據不合法 {index.Problems.Count} 項；第一項：{index.Problems[0]}");
        if (events.Count == 0)
            return new M1Finding(M1Raw.NotObserved, $"本情境 {run.Years} 年內沒有任何聚落事件，地方史沒有可讀的危機");

        const string limit = "。【覆蓋限制 TEMP】已用 43F 直接原因編號串接並以週帳核對；M1 示範每筆事件至多一個直接原因，完整 43F 歷史核心尚未實作";
        var linked = events.Count(e => e.CauseEventId != 0);

        // 優先找完整遷徙鏈：飢餓 → 外移 → 抵達／建立新聚落。
        foreach (var end in events.Where(e => e.Kind is DemoSettlementEventKind.Arrival or DemoSettlementEventKind.SettlementFounded))
        {
            var chain = index.ChainTo(end);
            if (chain.Count != 3 || chain[0].Kind != DemoSettlementEventKind.Hunger)
                continue;
            var (hunger, leave) = (chain[0], chain[1]);
            var what = end.Kind == DemoSettlementEventKind.SettlementFounded ? "建立" : "抵達";
            return new M1Finding(M1Raw.Ok,
                $"{At(run, hunger.Week)} {hunger.SettlementName}（#{hunger.SettlementId}）饑荒事件 #{hunger.EventId}：缺糧 {hunger.Count}"
                + $" → 外移事件 #{leave.EventId}：{leave.Count} 人（隊伍 #{leave.MigrationBatchId}）"
                + $" → {At(run, end.Week)} 事件 #{end.EventId}：{end.Count} 人{what} {end.SettlementName}（#{end.SettlementId}）；"
                + $"饑荒 #{hunger.EventId} 的直接後果 {index.ChildrenOf(hunger).Count} 筆（EFFECT_CHILD 反向索引）；"
                + $"全部 {events.Count} 筆事件中 {linked} 筆有直接原因、皆合法{limit}");
        }

        foreach (var death in events.Where(e => e.Kind == DemoSettlementEventKind.StarvationDeath))
        {
            var chain = index.ChainTo(death);
            if (chain.Count < 2 || chain[0].Kind != DemoSettlementEventKind.Hunger)
                continue;
            var hunger = chain[0];
            return new M1Finding(M1Raw.Ok,
                $"{At(run, hunger.Week)} {hunger.SettlementName}（#{hunger.SettlementId}）饑荒事件 #{hunger.EventId}：缺糧 {hunger.Count} → "
                + $"{At(run, death.Week)} 事件 #{death.EventId}：{death.Count} 人餓死（原因：{death.Cause}）；本情境沒有完整的遷徙鏈；"
                + $"全部 {events.Count} 筆事件中 {linked} 筆有直接原因、皆合法{limit}");
        }

        return new M1Finding(M1Raw.NotObserved, $"有 {events.Count} 筆事件、因果證據皆合法，但沒有可串成「原因→效果」的饑荒鏈");
    }
}

/// <summary>文字觀察者的 --m1-check 入口。</summary>
public static class M1AcceptanceCli
{
    /// <summary>TEMP 測試 fixture：預設三個固定種子。</summary>
    public static IReadOnlyList<ulong> DefaultSeeds { get; } = new ulong[] { 42, 7, 1 };

    public const int DefaultYears = 100;

    public static string Usage
        => $"用法：--m1-check [--years <1～{DemoCenturyRun.MaxYears}，預設 {DefaultYears}>] [--seed <非負整數>] "
         + $"[--preset {string.Join("|", M1Preset.All.Select(p => p.Key))}] [--checks <45G CSV 路徑>] [--world-params <01 CSV 路徑>]";

    /// <summary>執行並回傳結束碼：0＝Hard 全部沒有失敗；1＝有 Hard 失敗；2＝參數或清單錯誤。</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        var years = DefaultYears;
        ulong? seed = null;
        M1Preset? preset = null;
        string? checksPath = null;
        string? worldParamsPath = null;
        for (var i = 0; i < args.Count; i += 2)
        {
            var flag = args[i];
            var value = i + 1 < args.Count ? args[i + 1] : null;
            if (flag is not ("--years" or "--seed" or "--preset" or "--checks" or "--world-params"))
                return Error(output, $"不認得的參數「{flag}」。");
            if (value is null)
                return Error(output, $"參數 {flag} 缺少值。");

            switch (flag)
            {
                case "--years":
                    if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out years)
                        || years is < 1 or > DemoCenturyRun.MaxYears)
                        return Error(output, $"--years 必須是 1～{DemoCenturyRun.MaxYears} 的整數，收到「{value}」。");
                    break;
                case "--seed":
                    if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
                        return Error(output, $"--seed 必須是非負整數，收到「{value}」。");
                    seed = parsed;
                    break;
                case "--preset":
                    preset = M1Preset.Find(value);
                    if (preset is null)
                        return Error(output, $"--preset 必須是 {string.Join("、", M1Preset.All.Select(p => p.Key))} 之一，收到「{value}」。");
                    break;
                case "--world-params":
                    worldParamsPath = value;
                    break;
                default:
                    checksPath = value;
                    break;
            }
        }

        checksPath ??= M1CheckCatalog.FindDefaultPath();
        if (checksPath is null)
            return Error(output, $"找不到 {M1CheckCatalog.RelativePath}；請在程式倉庫內執行，或用 --checks 指定路徑。");

        IReadOnlyList<M1CheckDefinition> checks;
        try
        {
            checks = M1CheckCatalog.Load(checksPath);
        }
        catch (M1CatalogException ex)
        {
            return Error(output, ex.Message);
        }

        // #82：年界來自正式設定 01 表（與 --demo --years 相同），找不到或格式錯誤即停止。
        WorldCalendar calendar;
        try
        {
            calendar = worldParamsPath is null ? WorldCalendarConfig.LoadDefault() : WorldCalendarConfig.Load(worldParamsPath);
        }
        catch (WorldCalendarException ex)
        {
            return Error(output, ex.Message);
        }

        var seeds = seed is ulong s ? new[] { s } : DefaultSeeds.ToArray();
        var presets = preset is not null ? new[] { preset } : M1Preset.Default.ToArray();
        output.WriteLine("=== 世界觀察者 v0｜表 45G M1 驗收報告（V0-08A）===");
        output.WriteLine($"檢查清單：{M1CheckCatalog.RelativePath}（Enabled=TRUE 共 {checks.Count} 項，依清單順序）");
        output.WriteLine(calendar.Describe());
        output.WriteLine("注意：種子、情境、人口與糧食數值都是 TEMP 測試 fixture；本報告只量測，不改任何模擬規則或數值。");

        var reports = new List<M1RunReport>();
        foreach (var p in presets)
        {
            foreach (var runSeed in seeds)
            {
                var report = M1Evaluator.Evaluate(M1RunCollector.Collect(runSeed, p, years, calendar.DaysPerYear), checks);
                reports.Add(report);
                WriteRun(report, output);
            }
        }

        return WriteSummary(checks, reports, seeds.Length, presets.Length, years, output);
    }

    public static void WriteRun(M1RunReport report, TextWriter output)
    {
        var run = report.Run;
        output.WriteLine();
        output.WriteLine($"--- 種子 {run.Seed}｜情境：{run.Preset.Name}（{run.Preset.Key}，TEMP）｜{run.Years} 年 ---");
        foreach (var r in report.Results)
            output.WriteLine($"  {r.Check.Id}｜{r.Check.Name}｜{r.Check.CategoryText}｜{M1Evaluator.Label(r.Verdict)}｜{r.Diagnosis}");
    }

    private static int WriteSummary(
        IReadOnlyList<M1CheckDefinition> checks, IReadOnlyList<M1RunReport> reports,
        int seedCount, int presetCount, int years, TextWriter output)
    {
        output.WriteLine();
        output.WriteLine($"=== M1 驗收總結（{seedCount} 種子 × {presetCount} 情境，各 {years} 年；依 45G 順序）===");
        var failed = new List<string>();
        var hardPassed = 0;
        var hardTotal = 0;
        foreach (var check in checks)
        {
            var verdicts = reports.Select(r => r.Results.Single(x => x.Check.Id == check.Id).Verdict).ToList();
            var overall = M1Evaluator.Aggregate(verdicts);
            output.WriteLine($"  {check.Id}｜{check.Name}｜{check.CategoryText}｜{M1Evaluator.Label(overall)}｜"
                + $"通過 {verdicts.Count(v => v == M1Verdict.Pass)}、警告 {verdicts.Count(v => v == M1Verdict.Warn)}、"
                + $"失敗 {verdicts.Count(v => v == M1Verdict.Fail)}、尚未實作 {verdicts.Count(v => v == M1Verdict.Unimplemented)}");
            if (overall == M1Verdict.Fail)
                failed.Add(check.Id);
            if (check.Category == M1Category.Hard)
            {
                hardTotal++;
                if (overall == M1Verdict.Pass)
                    hardPassed++;
            }
        }

        output.WriteLine($"Hard 項目：通過 {hardPassed}／{hardTotal}，失敗 {failed.Count} 項。");
        if (failed.Count > 0)
        {
            output.WriteLine($"結論：有 Hard 項目失敗（{string.Join("、", failed)}），結束碼 1。");
            return 1;
        }

        output.WriteLine("結論：沒有 Hard 項目失敗；Warning／Behavior Check 只報告、不影響結束碼。"
            + "這不等於 P1-M1 已正式驗收（門檻與情境仍是 TEMP，見 #75）。");
        return 0;
    }

    private static int Error(TextWriter output, string message)
    {
        output.WriteLine($"錯誤：{message}");
        output.WriteLine(Usage);
        return 2;
    }
}
