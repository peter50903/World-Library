using System.Globalization;
using System.Text;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || args[0] != "--demo")
{
    Console.WriteLine("觀察者文字介面 v0（示範情境，尚非正式世界規則）");
    Console.WriteLine("用法：dotnet run --project src/WorldSimulation.ObserverCli -- --demo [--seed 42] [--years 100]");
    Console.WriteLine("  --years N：不開互動介面，直接跑 N 年（1～1000）並輸出年度摘要（V0-05 無畫面長測）。");
    return 2;
}

ulong seed = 42;
int? years = null;
for (var i = 1; i < args.Length; i += 2)
{
    var value = i + 1 < args.Length ? args[i + 1] : null;
    if (args[i] == "--seed" && value is not null &&
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSeed))
    {
        seed = parsedSeed;
    }
    else if (args[i] == "--years" && value is not null &&
             int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedYears) &&
             parsedYears is >= 1 and <= DemoCenturyRun.MaxYears)
    {
        years = parsedYears;
    }
    else
    {
        Console.WriteLine($"請以 --demo [--seed <非負整數>] [--years <1～{DemoCenturyRun.MaxYears}>] 指定參數。");
        return 2;
    }
}

if (years is int runYears)
    return RunHeadless(seed, runYears);

var world = new DemoObserverWorld(DemoScenario.Sample(seed));
Console.WriteLine("=== 世界觀察者 v0｜可執行示範 ===");
Console.WriteLine("注意：人口、初始食物與每日收支為非正式的示範數值；尚未載入試算表的正式世界設定。");
Console.WriteLine($"亂數種子：{seed}。世界在你閱讀時不會自行推進。");
Console.WriteLine("指令：n 下一日｜1-1000 快轉天數｜s 世界狀態｜h 最近歷史｜q 離開");
PrintStatus();

while (true)
{
    Console.Write("觀察者> ");
    var line = Console.ReadLine();
    if (line is null) return 0;
    var command = line.Trim().ToLowerInvariant();

    if (command is "q" or "quit" or "exit")
    {
        Console.WriteLine("離開觀察者；示範世界未儲存。");
        return 0;
    }
    if (command is "s" or "status")
    {
        PrintStatus();
        continue;
    }
    if (command is "h" or "history")
    {
        var history = world.History();
        Console.WriteLine($"歷史（共 {history.Count} 筆，顯示最近10筆）：");
        foreach (var e in history.Skip(Math.Max(0, history.Count - 10)))
            Console.WriteLine($"  第 {e.Day} 日：收穫{e.Harvested}／消耗{e.Consumed}／不足{e.Shortage}／結餘{e.FoodAfter}");
        if (history.Count == 0) Console.WriteLine("  尚未發生任何已結算事件。");
        continue;
    }
    if (command is "?" or "help")
    {
        Console.WriteLine("n 下一日｜1-1000 快轉天數｜s 世界狀態｜h 最近歷史｜q 離開");
        continue;
    }

    int steps;
    if (command == "n") steps = 1;
    else if (!int.TryParse(command, NumberStyles.None, CultureInfo.InvariantCulture, out steps) ||
             steps is < 1 or > 1000)
    {
        Console.WriteLine("未知指令；輸入 ? 查看可用操作。");
        continue;
    }

    try
    {
        DemoDayEvent? last = null;
        for (var i = 0; i < steps; i++)
            last = world.AdvanceOneDay();

        Console.WriteLine($"已處理事件：{steps} 日；最後一日為第 {last!.Day} 日，收穫{last.Harvested}、消耗{last.Consumed}、不足{last.Shortage}。");
        PrintStatus();
    }
    catch (Exception ex) when (ex is OverflowException or InvalidOperationException)
    {
        Console.WriteLine($"示範世界停止推進：{ex.Message}");
        return 1;
    }
}

void PrintStatus()
{
    var s = world.Snapshot();
    Console.WriteLine($"世界狀態｜第 {s.DaysElapsed} 日｜人口 {s.Population}｜食物 {s.FoodStock}｜累計食物不足 {s.TotalShortage}｜亂數抽樣次數 {s.ResourceRngDraws}");
}

// V0-05：無畫面長跑。每年印一行摘要；最後印守恆錯誤數。任何錯誤 → 結束碼 1。
static int RunHeadless(ulong seed, int years)
{
    Console.WriteLine("=== 世界觀察者 v0｜無畫面長測（V0-05）===");
    Console.WriteLine("注意：所有人口、糧食、資源與飢餓數值都是 TEMP 示範值；「一年＝52 週＝364 日」也是 TEMP 示範曆法。");
    Console.WriteLine($"亂數種子：{seed}｜年數：{years}");
    var watch = System.Diagnostics.Stopwatch.StartNew();
    DemoCenturyReport report;
    try
    {
        report = DemoCenturyRun.Run(seed, years, onYear: y => Console.WriteLine(y.Describe()));
    }
    catch (Exception ex) when (ex is OverflowException or InvalidOperationException)
    {
        Console.WriteLine($"長測中止（守恆或帳目錯誤）：{ex.Message}");
        return 1;
    }

    watch.Stop();
    var last = report.Years[^1];
    var totalDeaths = report.Years.Sum(y => y.Deaths);
    var totalEvents = report.Years.Sum(y => (long)y.SettlementEvents);
    Console.WriteLine($"長測完成：種子 {seed}，{years} 年（第 {last.LastDay} 日），期末人口 {last.PopulationClosing}，"
        + $"累計死亡 {totalDeaths}，累計聚落事件 {totalEvents}，守恆錯誤 {report.ConservationErrors.Count}，"
        + $"耗時 {watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} 秒");
    foreach (var error in report.ConservationErrors)
        Console.WriteLine($"  守恆錯誤：{error}");
    return report.ConservationErrors.Count == 0 ? 0 : 1;
}
