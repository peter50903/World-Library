using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// 第 07 章 a 節驗收：多種子 100 年的人類繁衍世界出現出生與老死（自然死亡）；
/// 第 08-2 章起是希望時代：沒有餓死，自然死亡全部是老年人；
/// 無近親配對；人口帳逐週平衡；同配方可重現；開局為混齡社會。
/// </summary>
public sealed class DemoHumanLifeRulesTests
{
    private static readonly ulong[] Seeds = { 42UL, 7UL, 20261021UL };

    [Fact]
    public void MultiSeedCenturyHasBirthsAndOldAgeDeathsButNoStarvationOrParentChildPairs()
    {
        long starvation = 0, births = 0, natural = 0;
        var summaries = new List<string>();
        var openings = new List<DemoTempPerson>();
        foreach (var seed in Seeds)
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
            var opening = world.HumanPopulationSnapshot!.People;
            openings.AddRange(opening);
            // 每個聚落至少有一對育齡建村夫婦。
            Assert.All(opening.GroupBy(p => p.SettlementId), g =>
            {
                Assert.Contains(g, p => p.Sex == DemoTempSex.Female && p.AgeYearsAt(0) is >= 18 and < 45);
                Assert.Contains(g, p => p.Sex == DemoTempSex.Male && p.AgeYearsAt(0) is >= 18 and < 45);
            });

            var trace = new System.Text.StringBuilder();
            for (var day = 0; day < 100 * TestCalendar.DaysPerYear; day++)
            {
                world.AdvanceOneDay();
                if ((day + 1) % (10 * TestCalendar.DaysPerYear) == 0)
                    trace.Append($" {(day + 1) / TestCalendar.DaysPerYear}年:{world.SettledPopulation}+{world.PopulationInTransit}");
            }

            var ledgers = world.WeekReports().SelectMany(w => w.Settlements).ToList();
            var legs = world.WeekReports().SelectMany(w => w.Migrations).ToList();
            var summary = $"種子 {seed}：出生 {ledgers.Sum(l => l.Births)}+{legs.Sum(m => m.Births)}、自然死亡 {ledgers.Sum(l => l.NaturalDeaths)}+{legs.Sum(m => m.NaturalDeaths)}、"
                + $"餓死 {ledgers.Sum(l => l.Deaths - l.NaturalDeaths)}、途中餓死 {legs.Sum(m => m.Deaths)}、出發 {legs.Count(m => m.Departed)} 隊；人口{trace}";
            Assert.All(ledgers, l => Assert.True(l.IsBalanced, $"種子 {seed} 第 {l.SettlementId} 聚落週帳不平衡"));
            // 第 07 章 b 節起小型世界可能整體滅絕（例：種子 7 約 20 年內），所以出生／老死／餓死三類以多種子合計驗收。
            births += ledgers.Sum(l => l.Births) + legs.Sum(m => m.Births);
            natural += ledgers.Sum(l => l.NaturalDeaths) + legs.Sum(m => m.NaturalDeaths);
            summaries.Add(summary);
            starvation += ledgers.Sum(l => l.Deaths - l.NaturalDeaths) + legs.Sum(m => m.Deaths);

            var people = world.HumanPopulationSnapshot!.People.ToDictionary(p => p.Id);
            foreach (var p in people.Values.Where(p => p.PartnerId is not null))
            {
                var partner = people[p.PartnerId!.Value];
                Assert.Equal(p.Id, partner.PartnerId);
                Assert.False(p.IsParentOrChildOf(partner), $"種子 {seed}：#{p.Id} 與 #{partner.Id} 是親子");
            }
            // 每次出生的雙親必須是當時的伴侶，事件原因列出雙親。
            Assert.All(world.WeekReports().SelectMany(w => w.Events).Where(e => e.Kind == DemoSettlementEventKind.Birth),
                e => Assert.Contains("雙親", e.Cause));
            // 第 08-2 章：希望時代只有老死——每一筆自然死亡的年齡都達 TEMP 老年起算年齡。
            Assert.All(world.WeekReports().SelectMany(w => w.Events).Where(e => e.Kind == DemoSettlementEventKind.NaturalDeath),
                e => Assert.True(int.Parse(System.Text.RegularExpressions.Regex.Match(e.Cause, @"年齡 (\d+)").Groups[1].Value)
                    >= DemoSurvivalTempSettings.Current.ElderAgeYears, $"種子 {seed}：{e.Cause}"));
        }
        var all = string.Join("／", summaries);
        Assert.True(births > 0, $"多種子百年都沒有出生｜{all}");
        Assert.True(natural > 0, $"多種子百年都沒有自然死亡｜{all}");
        Assert.True(starvation == 0, $"希望時代不應有人餓死｜{all}");
        // 混齡：三個種子的開局合計有兒童，也有 45 歲以上的人。
        Assert.Contains(openings, p => !p.IsAdultAt(0, DemoTempDemographyRules.HumanFixture));
        Assert.Contains(openings, p => p.AgeYearsAt(0) >= 45);
    }

    [Fact]
    public void SameRecipeReproducesTheSameMixedOpeningAndFamilies()
    {
        DemoObserverWorld Run()
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(7), DemoWeeklyTempConfig.LivingForagingSample);
            for (var day = 0; day < 10 * TestCalendar.DaysPerYear; day++)
                world.AdvanceOneDay();
            return world;
        }
        var a = Run();
        var b = Run();
        Assert.Equal(a.HumanPopulationSnapshot!.People.ToArray(), b.HumanPopulationSnapshot!.People.ToArray());
        Assert.Equal(a.StateFingerprint(), b.StateFingerprint());
        Assert.Contains(a.HumanPopulationSnapshot.People, p => p.MotherId is not null && p.BirthFatherId is not null);

        // 存檔→讀檔後名冊（父母、伴侶）與世界指紋相同。
        var loaded = DemoObserverWorld.ImportTempSave(a.ExportTempSave());
        Assert.Equal(a.HumanPopulationSnapshot.People.ToArray(), loaded.HumanPopulationSnapshot!.People.ToArray());
        Assert.Equal(a.StateFingerprint(), loaded.StateFingerprint());
    }
}
