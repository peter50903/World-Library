using System.Globalization;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.ObserverCli;

/// <summary>
/// 第 08-10a 章 V0：世界變化報告。普通世界（人類採集）每 10 年列出新建／遺跡、等級升降、技術首創與傳入、
/// 開墾、家系數與重大事件數，最後列第一項技術與開拓時代的年份。只讀世界，不改規則。
/// 用法：--change-report [--seed N | --seeds a,b,c] [--years 100] [--map small|medium|large]
/// </summary>
public static class ChangeReport
{
    public static int Run(IReadOnlyList<string> args, TextWriter output, int daysPerYear)
    {
        var seeds = new List<ulong> { 1 };
        var years = 100;
        var size = DemoMapSize.Small;
        for (var i = 0; i + 1 < args.Count; i += 2)
        {
            if (args[i] == "--seed" && ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var s))
                seeds = new List<ulong> { s };
            else if (args[i] == "--seeds" && TryParseSeeds(args[i + 1], out var list))
                seeds = list;
            else if (args[i] == "--years" && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1 and <= 1000)
                years = y;
            else if (args[i] == "--map" && Enum.TryParse<DemoMapSize>(args[i + 1], ignoreCase: true, out var m) && Enum.IsDefined(m))
                size = m;
            else
            {
                output.WriteLine("用法：--change-report [--seed N | --seeds a,b,c] [--years 1～1000] [--map small|medium|large]");
                return 2;
            }
        }
        if (args.Count % 2 != 0)
        {
            output.WriteLine("用法：--change-report [--seed N | --seeds a,b,c] [--years 1～1000] [--map small|medium|large]");
            return 2;
        }

        foreach (var seed in seeds)
        {
            var world = new DemoObserverWorld(WorldRecipe.ForSeeds(seed, null, size), DemoWeeklyTempConfig.LivingForagingSample);
            output.WriteLine($"=== 世界變化報告｜種子 {seed}｜{years} 年｜地圖 {DemoMapSizes.Name(size)}（{world.Map.Columns}×{world.Map.Rows}）===");
            var tracker = new DemoChangeTracker();
            var printed = 0;
            for (var year = 1; year <= years; year++)
            {
                for (var d = 0; d < daysPerYear; d++)
                    world.AdvanceOneDay();
                tracker.ObserveYearEnd(world, year);
                printed = Print(tracker, output, printed);
            }
            tracker.Finish(world);
            Print(tracker, output, printed);
            foreach (var line in tracker.RuinCauses)
                output.WriteLine(line); // 第 08-11 章：遺跡成因
            output.WriteLine(tracker.Summary());
        }
        return 0;
    }

    private static int Print(DemoChangeTracker tracker, TextWriter output, int printed)
    {
        foreach (var decade in tracker.Decades.Skip(printed))
            output.WriteLine(decade.Describe());
        return tracker.Decades.Count;
    }

    private static bool TryParseSeeds(string text, out List<ulong> seeds)
    {
        seeds = new List<ulong>();
        foreach (var part in text.Split(','))
        {
            if (!ulong.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var s))
                return false;
            seeds.Add(s);
        }
        return seeds.Count > 0;
    }
}
