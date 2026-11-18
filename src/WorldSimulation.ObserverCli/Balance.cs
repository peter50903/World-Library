using System.Globalization;
using System.Text;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.ObserverCli;

/// <summary>
/// 第 12-2h 章：人類與怪物的消長（使用者 2026-11-18：雙方勢均力敵、輪流佔上風）。普通世界（人類採集）每 10 年取一次，
/// 每個種子印一行：人口、出生、怪物害死的人、有人聚落、遺跡（第 12-2i 章：重建過的不算）、在遺跡上重建、獸潮次數與攻破、
/// 殺死的怪物、殺光的地下城、活著的地下城與怪物數、時代。只讀世界，不改規則；給調整獸潮、襲擊與討伐的數值用。
/// 用法：--balance [--seeds a,b,c] [--years 100] [--map small|medium|large]
/// </summary>
public static class Balance
{
    private const string Usage = "用法：--balance [--seeds a,b,c] [--years 1～1000] [--map small|medium|large]";

    /// <summary>每幾年取一次。</summary>
    public const int StepYears = 10;

    public static int Run(IReadOnlyList<string> args, TextWriter output, int daysPerYear)
    {
        var seeds = new List<ulong> { 1, 2, 3, 4, 5 };
        var years = 100;
        var size = DemoMapSize.Small;
        if (args.Count % 2 != 0)
        {
            output.WriteLine(Usage);
            return 2;
        }
        for (var i = 0; i + 1 < args.Count; i += 2)
        {
            if (args[i] == "--seeds" && TryParseSeeds(args[i + 1], out var list))
                seeds = list;
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
        foreach (var seed in seeds)
            output.WriteLine(Line(seed, years, size, daysPerYear));
        return 0;
    }

    private sealed class Step
    {
        public long Population, Births, MonsterDeaths, Slain;
        public int Living, Ruins, Rebuilt, Swarms, Overruns, Cleared, Lairs, Monsters;
        public char Age;
    }

    /// <summary>一個種子跑完，回傳一行（每一欄是每 10 年的數字，以空格分隔）。</summary>
    public static string Line(ulong seed, int years, DemoMapSize size, int daysPerYear)
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(seed, null, size), DemoWeeklyTempConfig.LivingForagingSample);
        var steps = new List<Step>();
        var current = new Step();
        var lastWeek = 0;
        for (var year = 1; year <= years; year++)
        {
            for (var d = 0; d < daysPerYear; d++)
                world.AdvanceOneDay();
            foreach (var week in world.WeekReports().Where(w => w.Week > lastWeek))
            {
                current.Births += week.Settlements.Sum(l => l.Births);
                foreach (var e in week.Events)
                {
                    switch (e.Kind)
                    {
                        case DemoSettlementEventKind.MonsterRaid or DemoSettlementEventKind.MonsterHunt:
                            current.MonsterDeaths += e.Count;
                            break;
                        case DemoSettlementEventKind.MonsterSwarm:
                            current.MonsterDeaths += e.Count;
                            current.Swarms++;
                            if (e.Cause.Contains("攻破", StringComparison.Ordinal))
                                current.Overruns++;
                            break;
                        case DemoSettlementEventKind.MonsterSlain:
                            current.Slain += DemoMonsters.DecodeKills(e.Count).Kills;
                            break;
                        case DemoSettlementEventKind.DungeonCleared:
                            current.Cleared++;
                            break;
                        case DemoSettlementEventKind.SettlementFounded when world.Sites.RebuiltOn(e.SettlementId) is not null:
                            current.Rebuilt++; // 第 12-2i 章：在遺跡上重建
                            break;
                    }
                }
                lastWeek = week.Week;
            }
            if (year % StepYears != 0 && year != years)
                continue;
            current.Population = world.WorldPopulation;
            current.Living = world.Sites.Settlements.Count(s => !s.IsRuin && world.SettlementPopulation(s.Id) > 0);
            current.Ruins = world.Sites.Settlements.Count(world.Sites.IsStandingRuin);
            var nests = world.MonsterNests(world.CommittedWeeks);
            current.Lairs = nests.Count(n => !n.Dormant && n.Monsters > 0);
            current.Monsters = nests.Where(n => !n.Dormant).Sum(n => n.Monsters);
            current.Age = DemoAges.Name(world.Age)[0];
            steps.Add(current);
            current = new Step();
        }

        string Row(string title, Func<Step, string> cell) => $"{title} {string.Join(' ', steps.Select(cell))}";
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"種子 {seed}（{DemoMapSizes.Name(size)}）｜");
        text.AppendJoin('｜', new[]
        {
            Row("人口", s => s.Population.ToString(CultureInfo.InvariantCulture)),
            Row("出生", s => s.Births.ToString(CultureInfo.InvariantCulture)),
            Row("怪物害死", s => s.MonsterDeaths.ToString(CultureInfo.InvariantCulture)),
            Row("聚落", s => s.Living.ToString(CultureInfo.InvariantCulture)),
            Row("遺跡", s => s.Ruins.ToString(CultureInfo.InvariantCulture)),
            Row("重建", s => s.Rebuilt.ToString(CultureInfo.InvariantCulture)),
            Row("獸潮/攻破", s => string.Create(CultureInfo.InvariantCulture, $"{s.Swarms}/{s.Overruns}")),
            Row("殺怪", s => s.Slain.ToString(CultureInfo.InvariantCulture)),
            Row("殺光", s => s.Cleared.ToString(CultureInfo.InvariantCulture)),
            Row("巢/怪", s => string.Create(CultureInfo.InvariantCulture, $"{s.Lairs}/{s.Monsters}")),
            Row("時代", s => s.Age.ToString())
        });
        // 第 12-2i 章：小地圖跑完再存讀檔一次（遺跡上重建的聚落也要讀得回來）。
        if (size == DemoMapSize.Small)
        {
            try
            {
                var json = world.ExportTempSave();
                text.Append(DemoObserverWorld.ImportTempSave(json).ExportTempSave() == json ? "｜存讀檔 一致" : "｜存讀檔 不一致");
            }
            catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or ArgumentException)
            {
                text.Append("｜存讀檔 失敗：").Append(ex.Message);
            }
        }
        return text.ToString();
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
