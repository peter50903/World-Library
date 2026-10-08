using System.Globalization;
using System.Text;
using WorldSimulation.Core.World;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length == 0 || args[0] != "--demo")
{
    Console.WriteLine("Observer CLI v0 (示範場景，尚非正式世界規則)");
    Console.WriteLine("用法：dotnet run --project src/WorldSimulation.ObserverCli -- --demo [--seed 42]");
    return 2;
}

ulong seed = 42;
if (args.Length > 1)
{
    if (args.Length != 3 || args[1] != "--seed" ||
        !ulong.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out seed))
    {
        Console.WriteLine("請以 --demo --seed <非負整數> 指定情境亂數種子。");
        return 2;
    }
}

var world = new DemoObserverWorld(DemoScenario.Sample(seed));
Console.WriteLine("=== 世界觀察者 v0｜可執行示範 ===");
Console.WriteLine("注意：人口、初始食物及每日收支為非正式Demo情境值；尚未載入Sheets正式世界設定。");
Console.WriteLine($"Seed: {seed}。世界在你閱讀時不會自行推進。");
Console.WriteLine("指令：n 下一日｜1-1000 快轉天數｜s 世界狀態｜h 最近歷史｜q 離開");
PrintStatus();

while (true)
{
    Console.Write("observer> ");
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
            Console.WriteLine($"  Day {e.Day}：收穫{e.Harvested}／消耗{e.Consumed}／不足{e.Shortage}／結餘{e.FoodAfter}");
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

        Console.WriteLine($"已處理事件：{steps} 日；最後一日 Day {last!.Day} 收穫{last.Harvested}、消耗{last.Consumed}、不足{last.Shortage}。");
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
    Console.WriteLine($"世界狀態｜Day {s.DaysElapsed}｜人口 {s.Population}｜食物 {s.FoodStock}｜累計食物不足 {s.TotalShortage}｜RNG抽樣 {s.ResourceRngDraws}");
}
