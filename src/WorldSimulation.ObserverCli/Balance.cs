using System.Globalization;
using System.Text;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.ObserverCli;

/// <summary>
/// 第 12-2h 章：人類與怪物的消長（使用者 2026-11-18：雙方勢均力敵、輪流佔上風）。普通世界（人類採集）每 10 年取一次，
/// 每個種子印一行：人口、出生、怪物害死的人、有人聚落、城鎮與城市、最大聚落的人口、遺跡（第 12-2i 章：重建過的不算）、在遺跡上重建、獸潮次數與攻破、
/// 殺死的怪物、殺光的地下城、活著的地下城與怪物數、時代。只讀世界，不改規則；給調整獸潮、襲擊與討伐的數值用。
/// 用法：--balance [--seeds a,b,c] [--years 100] [--map small|medium|large]
/// </summary>
public static class Balance
{
    private const string Usage = "用法：--balance [--seeds a,b,c] [--years 1～1000] [--map small|medium|large]";

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

    /// <summary>一個種子跑完，回傳一行（每一欄是每 10 年的數字，以空格分隔）；小地圖另外存讀檔一次。</summary>
    public static string Line(ulong seed, int years, DemoMapSize size, int daysPerYear)
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(seed, null, size), DemoWeeklyTempConfig.LivingForagingSample);
        var tracker = new BalanceTracker();
        for (var year = 1; year <= years; year++)
        {
            for (var d = 0; d < daysPerYear; d++)
                world.AdvanceOneDay();
            tracker.ObserveYearEnd(world, year, year == years);
        }
        var text = new StringBuilder(tracker.Line(seed, size));
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

/// <summary>
/// 第 12-2h 章：逐年觀察世界、每 10 年取一次人類與怪物的消長（<see cref="Balance"/> 與文字史書共用；只讀世界）。
/// </summary>
public sealed class BalanceTracker
{
    /// <summary>每幾年取一次。</summary>
    public const int StepYears = 10;

    private sealed class Step
    {
        public long Population, Births, MonsterDeaths, WarDeaths, Slain, Largest, Tax, Trade;
        public int Living, Ruins, Rebuilt, Swarms, Overruns, Cleared, Lairs, Monsters, Towns, Cities, Capitals, Kingdoms;
        public string Crown = "", CityFacilities = "";
        public char Age, Era;
    }

    private readonly List<Step> _steps = new();
    private Step _current = new();
    private int _lastWeek;

    /// <summary>每年年底呼叫一次；<paramref name="last"/>＝最後一年（不滿 10 年也取一次）。</summary>
    public void ObserveYearEnd(DemoObserverWorld world, int year, bool last)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var week in world.WeekReports().Where(w => w.Week > _lastWeek))
        {
            _current.Births += week.Settlements.Sum(l => l.Births);
            _current.Tax += week.Settlements.Sum(l => l.FoodTaxReceived); // 第 08-23 章：糧稅與貿易（每 10 年合計，千為單位）
            _current.Trade += week.Settlements.Sum(l => l.FoodTradeIn);
            foreach (var e in week.Events)
            {
                if (e.Count > 0 && DemoSaga.DeathCause(e.Kind) == "戰火")
                    _current.WarDeaths += e.Count; // 第 08-22i 章：戰火的死亡
                switch (e.Kind)
                {
                    case DemoSettlementEventKind.MonsterRaid or DemoSettlementEventKind.MonsterHunt:
                        _current.MonsterDeaths += e.Count;
                        break;
                    case DemoSettlementEventKind.MonsterSwarm:
                        _current.MonsterDeaths += e.Count;
                        _current.Swarms++;
                        if (e.Cause.Contains("攻破", StringComparison.Ordinal))
                            _current.Overruns++;
                        break;
                    case DemoSettlementEventKind.MonsterSlain:
                        _current.Slain += DemoMonsters.DecodeKills(e.Count).Kills;
                        break;
                    case DemoSettlementEventKind.DungeonCleared:
                        _current.Cleared++;
                        break;
                    case DemoSettlementEventKind.SettlementFounded when world.Sites.RebuiltOn(e.SettlementId) is not null:
                        _current.Rebuilt++; // 第 12-2i 章：在遺跡上重建
                        break;
                }
            }
            _lastWeek = week.Week;
        }
        if (year % StepYears != 0 && !last)
            return;
        _current.Population = world.WorldPopulation;
        var living = world.Sites.Settlements.Where(s => !s.IsRuin && world.SettlementPopulation(s.Id) > 0).ToList();
        _current.Living = living.Count;
        // 文明發展：城鎮、城市以上幾個，最大的聚落幾人。
        _current.Towns = living.Count(s => world.ChronicleOf(s.Id).Tier >= DemoSettlementTier.Town);
        _current.Cities = living.Count(s => world.ChronicleOf(s.Id).Tier >= DemoSettlementTier.City);
        // 第 08-24 章：首都幾個、通往王權的技術與城市級設施各有幾個聚落會、世界在哪個紀元（希望、開拓、征服＝第一個王國成立）。
        _current.Capitals = living.Count(s => world.ChronicleOf(s.Id).Tier >= DemoSettlementTier.Capital);
        _current.Kingdoms = world.KingdomHeads().Count;
        string Knowing(params string[] techs) => string.Join('/', techs.Select(t => living.Count(s => world.Knowledge.Knows(s.Id, DemoTechnology.Of(t))))
            .Select(n => n.ToString(CultureInfo.InvariantCulture)));
        _current.Crown = Knowing("TECH_CODIFIED_LAW", "TECH_HEREDITARY_NOBILITY", "TECH_TAXATION", DemoSettlementHistory.CapitalTechId);
        _current.CityFacilities = Knowing(DemoSettlementHistory.FacilitiesOf(DemoSettlementTier.City).Select(f => f.TechId!).ToArray());
        _current.Era = DemoKnowledge.EraName(world.Era)[0];
        _current.Largest = living.Count == 0 ? 0 : living.Max(s => world.SettlementPopulation(s.Id));
        _current.Ruins = world.Sites.Settlements.Count(world.Sites.IsStandingRuin);
        var nests = world.MonsterNests(world.CommittedWeeks);
        _current.Lairs = nests.Count(n => !n.Dormant && n.Monsters > 0);
        _current.Monsters = nests.Where(n => !n.Dormant).Sum(n => n.Monsters);
        _current.Age = DemoAges.Name(world.Age)[0];
        _steps.Add(_current);
        _current = new Step();
    }

    /// <summary>一行：每一欄是每 10 年的數字，以空格分隔。</summary>
    public string Line(ulong seed, DemoMapSize size)
    {
        string Row(string title, Func<Step, string> cell) => $"{title} {string.Join(' ', _steps.Select(cell))}";
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"種子 {seed}（{DemoMapSizes.Name(size)}）｜");
        text.AppendJoin('｜', new[]
        {
            Row("人口", s => s.Population.ToString(CultureInfo.InvariantCulture)),
            Row("出生", s => s.Births.ToString(CultureInfo.InvariantCulture)),
            Row("怪物害死", s => s.MonsterDeaths.ToString(CultureInfo.InvariantCulture)),
            Row("戰死", s => s.WarDeaths.ToString(CultureInfo.InvariantCulture)),
            Row("聚落", s => s.Living.ToString(CultureInfo.InvariantCulture)),
            Row("鎮/城/都/國", s => string.Create(CultureInfo.InvariantCulture, $"{s.Towns}/{s.Cities}/{s.Capitals}/{s.Kingdoms}")),
            Row("法/世/稅/王", s => s.Crown),
            Row("堡/殿/書/醫", s => s.CityFacilities),
            Row("最大", s => s.Largest.ToString(CultureInfo.InvariantCulture)),
            Row("稅/貿(千)", s => string.Create(CultureInfo.InvariantCulture, $"{s.Tax / 1000}/{s.Trade / 1000}")),
            Row("遺跡", s => s.Ruins.ToString(CultureInfo.InvariantCulture)),
            Row("重建", s => s.Rebuilt.ToString(CultureInfo.InvariantCulture)),
            Row("獸潮/攻破", s => string.Create(CultureInfo.InvariantCulture, $"{s.Swarms}/{s.Overruns}")),
            Row("殺怪", s => s.Slain.ToString(CultureInfo.InvariantCulture)),
            Row("殺光", s => s.Cleared.ToString(CultureInfo.InvariantCulture)),
            Row("巢/怪", s => string.Create(CultureInfo.InvariantCulture, $"{s.Lairs}/{s.Monsters}")),
            Row("時代", s => s.Age.ToString()),
            Row("紀元", s => s.Era.ToString())
        });
        return text.ToString();
    }
}
