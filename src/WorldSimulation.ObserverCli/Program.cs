using System.Globalization;
using System.Text;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;

Console.OutputEncoding = Encoding.UTF8;

// V0-08A：表 45G M1 驗收報告（多種子 × TEMP 情境，逐項通過／警告／失敗／尚未實作）。
if (args.Length > 0 && args[0] == "--m1-check")
    return M1AcceptanceCli.Run(args.Skip(1).ToArray(), Console.Out);

if (args.Length == 0 || args[0] != "--demo")
{
    Console.WriteLine("觀察者文字介面 v0（示範情境，尚非正式世界規則）");
    Console.WriteLine("用法：dotnet run --project src/WorldSimulation.ObserverCli -- --demo [--seed 42] [--years 100] [--frontier]");
    Console.WriteLine("  --years N：不開互動介面，直接跑 N 年（1～1000）並輸出年度摘要（V0-05 無畫面長測）。");
    Console.WriteLine("  --frontier：改用 TEMP 開拓情境（開局聚落全部缺糧，遷徙者可建立新聚落；V0-06B 測試用）。");
    Console.WriteLine("M1 驗收報告：dotnet run --project src/WorldSimulation.ObserverCli -- --m1-check [--years 100] [--seed N] [--preset normal|local-famine|frontier|famine]");
    return 2;
}

ulong seed = 42;
int? years = null;
var frontier = false;
for (var i = 1; i < args.Length; i += 2)
{
    var value = i + 1 < args.Length ? args[i + 1] : null;
    if (args[i] == "--frontier")
    {
        frontier = true;
        i--; // 這個旗標沒有值。
    }
    else if (args[i] == "--seed" && value is not null &&
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
        Console.WriteLine($"請以 --demo [--seed <非負整數>] [--years <1～{DemoCenturyRun.MaxYears}>] [--frontier] 指定參數。");
        return 2;
    }
}

const string HelpText = "指令：n 下一日｜1-1000 快轉天數｜s 狀態｜h 歷史｜save N 存到槽 N｜load N 讀槽 N｜slots 列出存檔槽（N＝1～3，TEMP）｜q 離開";

if (years is int runYears)
    return RunHeadless(seed, runYears, frontier);

var world = new DemoObserverWorld(DemoScenario.Sample(seed),
    frontier ? DemoWeeklyTempConfig.FrontierSample : DemoWeeklyTempConfig.Sample);
Console.WriteLine("=== 世界觀察者 v0｜可執行示範 ===");
Console.WriteLine("注意：人口、初始食物與每日收支為非正式的示範數值；尚未載入試算表的正式世界設定。");
Console.WriteLine($"亂數種子：{seed}。世界在你閱讀時不會自行推進。");
Console.WriteLine(HelpText);
PrintStatus();

while (true)
{
    Console.Write("觀察者> ");
    var line = Console.ReadLine();
    if (line is null) return 0;
    var command = line.Trim().ToLowerInvariant();

    if (command is "q" or "quit" or "exit")
    {
        Console.WriteLine("離開觀察者；未執行 save 的後續進度不會儲存。");
        return 0;
    }
    if (command is "s" or "status")
    {
        PrintStatus();
        continue;
    }
    // V0-07C：TEMP 3 個存檔槽。save／load 不加槽號＝槽 1。
    if (command is "slots")
    {
        PrintSlots();
        continue;
    }
    if (TryParseSlotCommand(command, "save", out var saveSlot))
    {
        try
        {
            DemoSaveSlots.Save(world, saveSlot);
            Console.WriteLine($"已儲存到 TEMP 存檔槽 {saveSlot}：{DemoSaveSlots.ReadInfo(saveSlot).Describe()}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            Console.WriteLine($"存檔失敗，世界未改變：{ex.Message}");
        }
        continue;
    }
    if (TryParseSlotCommand(command, "load", out var loadSlot))
    {
        try
        {
            var candidate = DemoSaveSlots.Load(loadSlot);
            world = candidate; // Replace only after the whole TEMP save is verified.
            Console.WriteLine($"已讀取 TEMP 存檔槽 {loadSlot}。");
            PrintStatus();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            Console.WriteLine($"讀檔失敗，現在的世界沒有改變：{ex.Message}");
        }
        continue;
    }
    if (command.StartsWith("save", StringComparison.Ordinal) || command.StartsWith("load", StringComparison.Ordinal))
    {
        Console.WriteLine($"存檔槽必須是 1～{DemoSaveSlots.TempSlotCount}，例如 save 2、load 2。");
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
        Console.WriteLine(HelpText);
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

void PrintSlots()
{
    Console.WriteLine($"TEMP 存檔槽（{DemoSaveSlots.DefaultDirectory}）：");
    foreach (var info in DemoSaveSlots.ListAll())
        Console.WriteLine($"  {info.Describe()}");
}

static bool TryParseSlotCommand(string command, string verb, out int slot)
{
    slot = 1;
    if (command == verb)
        return true;
    var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    return parts.Length == 2 && parts[0] == verb
        && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out slot)
        && slot is >= 1 and <= DemoSaveSlots.TempSlotCount;
}

void PrintStatus()
{
    var s = world.Snapshot();
    Console.WriteLine($"世界狀態｜種子 {world.TempWorldSeed}｜第 {s.DaysElapsed} 日｜人口 {s.Population}｜食物 {s.FoodStock}｜累計食物不足 {s.TotalShortage}｜亂數抽樣次數 {s.ResourceRngDraws}");
}

// V0-05：無畫面長跑。每年印一行摘要；最後印守恆錯誤數。任何錯誤 → 結束碼 1。
static int RunHeadless(ulong seed, int years, bool frontier)
{
    Console.WriteLine("=== 世界觀察者 v0｜無畫面長測（V0-05）===");
    // #82：一年日數讀正式設定 01 表 WORLD_DAYS_PER_YEAR（與 --m1-check 同一個年界），找不到或格式錯誤即停止。
    WorldCalendar calendar;
    try
    {
        calendar = WorldCalendarConfig.LoadDefault();
    }
    catch (WorldCalendarException ex)
    {
        Console.WriteLine($"無法讀取正式曆法：{ex.Message}");
        return 2;
    }

    Console.WriteLine(calendar.Describe());
    Console.WriteLine("注意：所有人口、糧食、資源與飢餓數值都是 TEMP 示範值；週結算每 7 日一次是既有示範機制。");
    Console.WriteLine($"亂數種子：{seed}｜年數：{years}" + (frontier ? "｜TEMP 開拓情境（V0-06B）" : ""));
    var watch = System.Diagnostics.Stopwatch.StartNew();
    DemoCenturyReport report;
    try
    {
        report = DemoCenturyRun.Run(seed, years, calendar.DaysPerYear,
            frontier ? DemoWeeklyTempConfig.FrontierSample : DemoWeeklyTempConfig.Sample,
            onYear: y => Console.WriteLine(y.Describe()),
            onYearSettlements: (y, capacities) =>
            {
                // #103：第 1、10、100 年與最後一年列出各聚落的長期再生 vs 需求（TEMP 描述性估計）。
                if (y.Year is not (1 or 10 or 100) && y.Year != years)
                    return;
                Console.WriteLine($"  第 {y.Year} 年底各聚落承載（TEMP 估計：存量與糧庫耗盡後每週再生 vs 需求；不含遷徙與出生）：");
                foreach (var c in capacities)
                    Console.WriteLine($"    {c.Describe()}");
            });
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
    var totalFounded = report.Years.Sum(y => y.SettlementsFounded);
    Console.WriteLine($"長測完成：種子 {seed}，{years} 年（第 {last.LastDay} 日），期末人口 {last.PopulationClosing}，"
        + $"累計死亡 {totalDeaths}，累計聚落事件 {totalEvents}，新建聚落 {totalFounded}，守恆錯誤 {report.ConservationErrors.Count}，"
        + $"耗時 {watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} 秒");
    foreach (var error in report.ConservationErrors)
        Console.WriteLine($"  守恆錯誤：{error}");
    return report.ConservationErrors.Count == 0 ? 0 : 1;
}
