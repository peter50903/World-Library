using System.Globalization;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.ObserverCli;

/// <summary>
/// 第 08-10e 章：文字史書。普通世界（人類採集）跑 N 年，印出一頁歷史（序、每十年一卷、終章）。只讀世界，不改規則。
/// 用法：--saga [--seed N] [--years 100] [--map small|medium|large]
/// </summary>
public static class Saga
{
    private const string Usage = "用法：--saga [--seed N] [--years 1～1000] [--map small|medium|large]";

    public static int Run(IReadOnlyList<string> args, TextWriter output, int daysPerYear)
    {
        ulong seed = 1;
        var years = 100;
        var size = DemoMapSize.Small;
        if (args.Count % 2 != 0)
        {
            output.WriteLine(Usage);
            return 2;
        }
        for (var i = 0; i + 1 < args.Count; i += 2)
        {
            if (args[i] == "--seed" && ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var s))
                seed = s;
            else if (args[i] == "--years" && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1 and <= 1000)
                years = y;
            else if (args[i] == "--map" && Enum.TryParse<DemoMapSize>(args[i + 1], ignoreCase: true, out var m) && Enum.IsDefined(m))
                size = m;
            else
            {
                output.WriteLine(Usage);
                return 2;
            }
        }

        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(seed, null, size), DemoWeeklyTempConfig.LivingForagingSample);
        var opening = DemoSaga.Opening(world);
        var balance = new BalanceTracker(); // 第 12-2h 章：順便每 10 年取一次人類與怪物的消長（大地圖不必另跑一次）
        for (var year = 1; year <= years; year++)
        {
            for (var d = 0; d < daysPerYear; d++)
                world.AdvanceOneDay();
            balance.ObserveYearEnd(world, year, year == years);
        }
        output.WriteLine($"=== 史書｜種子 {seed}｜{years} 年｜地圖 {DemoMapSizes.Name(size)}（{world.Map.Columns}×{world.Map.Rows}）===");
        foreach (var line in DemoSaga.Write(world, opening))
            output.WriteLine(line);
        output.WriteLine();
        output.WriteLine("人類與怪物消長：" + balance.Line(seed, size));
        return 0;
    }
}
