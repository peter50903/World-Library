using System.Globalization;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.ObserverCli;

/// <summary>
/// 第 08-1 章：普通世界（人類採集）人口診斷。逐年列出世界與各聚落的人口結構、伴侶、採收／需求／勞力上限、
/// 可及來源供應、無缺糧週比例（受孕條件）與死因，用來找出人口成長不起來的環節。只讀世界，不改規則。
/// 用法：--diagnose-living --seed N [--years 100]
/// </summary>
public static class LivingDiagnosis
{
    public static int Run(IReadOnlyList<string> args, TextWriter output, int daysPerYear)
    {
        ulong seed = 1;
        var years = 100;
        for (var i = 0; i + 1 < args.Count; i += 2)
        {
            if (args[i] == "--seed" && ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var s)) seed = s;
            else if (args[i] == "--years" && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1 and <= 1000) years = y;
            else
            {
                output.WriteLine("用法：--diagnose-living --seed N [--years 1～1000]");
                return 2;
            }
        }

        var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
        var rules = DemoTempDemographyRules.HumanFixture;
        output.WriteLine($"=== 普通世界人口診斷｜種子 {seed}｜{years} 年｜{world.MapCapacity.Describe()} ===");
        var seen = 0;
        for (var year = 1; year <= years; year++)
        {
            for (var d = 0; d < daysPerYear; d++)
                world.AdvanceOneDay();
            var weeks = world.WeekReports().Skip(seen).ToList();
            seen = world.WeekReports().Count;
            if (year % 5 != 0 && year != 1 && year != years)
                continue;
            var day = world.Snapshot().DaysElapsed;
            IReadOnlyList<DemoTempPerson> people = world.HumanPopulationSnapshot is { } snapshot ? snapshot.People : Array.Empty<DemoTempPerson>();
            var births = weeks.Sum(w => w.Settlements.Sum(l => l.Births) + w.Migrations.Sum(m => m.Births));
            var starved = weeks.Sum(w => w.Settlements.Sum(l => l.StarvationDeaths) + w.Migrations.Sum(m => m.Deaths));
            var natural = weeks.Sum(w => w.Settlements.Sum(l => l.NaturalDeaths) + w.Migrations.Sum(m => m.NaturalDeaths));
            var emig = weeks.Sum(w => w.Settlements.Sum(l => l.Emigrants));
            output.WriteLine($"第 {year} 年｜人口 {world.WorldPopulation}（途中 {world.PopulationInTransit}）｜本年出生 {births}、餓死 {starved}、老死 {natural}、外移 {emig}");
            foreach (var s in world.Sites.Settlements.Where(s => world.SettlementPopulation(s.Id) > 0))
            {
                var residents = people.Where(p => p.IsSettled && p.SettlementId == s.Id).ToList();
                var adults = residents.Count(p => p.IsAdultAt(day, rules));
                var fertileWomen = residents.Count(p => p.Sex == DemoTempSex.Female && p.IsAdultAt(day, rules) && p.AgeYearsAt(day) < rules.FertileAgeMaxYears);
                var couples = residents.Count(p => p.Sex == DemoTempSex.Female && p.PartnerId is not null && p.AgeYearsAt(day) < rules.FertileAgeMaxYears);
                var pregnant = residents.Count(p => p.PregnancyDueWeek is not null);
                var ledgers = weeks.SelectMany(w => w.Settlements).Where(l => l.SettlementId == s.Id).ToList();
                var n = Math.Max(1, ledgers.Count);
                var fed = ledgers.Count(l => l.FoodShortage == 0 && l.HungerDebtClosing == 0);
                var waterShort = ledgers.Count(l => l.WaterShortage > 0);
                var harvest = ledgers.Sum(l => l.FoodProduced) / n;
                var demand = ledgers.Sum(l => l.FoodConsumed + l.FoodShortage) / n;
                var labour = ledgers.Sum(l => Math.Max(0, (l.AdultWorkersOpening < 0 ? l.PopulationOpening : l.AdultWorkersOpening) - l.WaterCarriers)) * world.WeeklyConfig.YieldPerWorkerPerWeek / n;
                var carriers = ledgers.Sum(l => l.WaterCarriers) / (double)n;
                var supply = weeks.Count == 0 ? 0 : weeks[^1].Sources.Where(x => (x.Shares ?? Array.Empty<DemoSourceShare>()).Any(sh => sh.SettlementId == s.Id)).Sum(x => x.FoodAvailable);
                var food = world.SettlementFood(s.Id);
                var ample = weeks.Count(w => w.Settlements.Any(l => l.SettlementId == s.Id
                    && DemoCrowding.Ample(true, l.FoodShortage, l.FoodConsumed + l.FoodShortage, DemoCrowding.Slack(w.Sources, s.Id))));
                output.WriteLine($"  #{s.Id} 人 {residents.Count}（成 {adults}／伴 {couples}／孕 {pregnant}）｜採 {harvest}／需 {demand}／勞 {labour}｜可採 {supply}"
                    + $"｜飽 {fed}、寬裕 {ample}／{ledgers.Count} 週");
            }
        }
        return 0;
    }
}
