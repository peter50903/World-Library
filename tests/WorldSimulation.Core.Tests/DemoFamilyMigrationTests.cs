using System.Collections.Immutable;
using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 07 章 b 節：遷徙以家庭為單位攜帶個人身分（不複製、不遺失）；人類繁衍情境可以遷徙。</summary>
public sealed class DemoFamilyMigrationTests
{
    private static readonly DemoTempDemographyRules Rules = DemoTempDemographyRules.HumanFixture;

    private static DemoTempPerson Adult(long id, DemoTempSex sex, int age, long? partner = null)
        => new(id, 1, sex, -age * 365, PartnerId: partner);

    private static DemoTempPerson Child(long id, long mother, long father, int age)
        => new(id, 1, DemoTempSex.Female, -age * 365, MotherId: mother, BirthFatherId: father);

    [Fact]
    public void FamiliesTravelWholeAndNeverExceedTheLimit()
    {
        var residents = new[]
        {
            Adult(1, DemoTempSex.Female, 50, 2), Adult(2, DemoTempSex.Male, 52, 1),
            Adult(3, DemoTempSex.Female, 25, 4), Adult(4, DemoTempSex.Male, 26, 3),
            Child(5, 3, 4, 3), Child(6, 3, 4, 1),
            Adult(7, DemoTempSex.Male, 30),
            Child(8, 90, 91, 10) // 父母都不在：不單獨出發
        };
        var families = DemoFamilyMigration.Families(residents, 0, Rules);
        Assert.Equal(new long[] { 3, 4, 5, 6 }, families[0].Select(p => p.Id).ToArray()); // 最年輕的家庭先走
        Assert.Equal(3, families.Count);
        Assert.DoesNotContain(families.SelectMany(f => f), p => p.Id == 8);

        Assert.Equal(new long[] { 3, 4, 5, 6 }, DemoFamilyMigration.Choose(residents, 4, 0, Rules));
        Assert.Equal(new long[] { 3, 4, 5, 6, 7 }, DemoFamilyMigration.Choose(residents, 5, 0, Rules));
        Assert.Equal(new long[] { 1, 2, 7 }, DemoFamilyMigration.Choose(residents, 3, 0, Rules)); // 四口之家放不下就跳過
        Assert.Empty(DemoFamilyMigration.Choose(residents, 0, 0, Rules));
    }

    [Fact]
    public void ChildBornOnTheRoadBelongsToTheBatch()
    {
        var noMiscarriage = Rules with { MiscarriageMonthly = 0 };
        var pop = DemoTempDemography.Restore(new DemoTempDemographicSnapshot(4, 3, ImmutableArray.Create(
            new DemoTempPerson(1, 1, DemoTempSex.Female, -25 * 365, PregnancyDueWeek: 5, FatherId: 2, PartnerId: 2, BatchId: 9),
            new DemoTempPerson(2, 1, DemoTempSex.Male, -25 * 365, PartnerId: 1, BatchId: 9))), noMiscarriage);
        var outcome = pop.AdvanceWeek(5, _ => true, noMiscarriage, new DemoPopulationDraws(RngRegistry.Create(1)));
        var birth = Assert.Single(outcome.Events, e => e.Kind == DemoTempDemographicKind.Birth);
        Assert.Equal(9, birth.BatchId);
        var baby = outcome.Population.People.Single(p => p.Id == birth.PersonId);
        Assert.Equal(9, baby.BatchId);
        Assert.Equal(3, outcome.Population.MembersOf(9));
        Assert.Equal(0, outcome.Population.ResidentsOf(1));
        // 途中不新配對、伴侶不被拆開。
        Assert.Equal(2, outcome.Population.People.Single(p => p.Id == 1).PartnerId);
    }

    [Fact]
    public void HumanWorldMigratesFamiliesAndConservesEveryIdentity()
    {
        var found = false;
        foreach (var seed in new ulong[] { 42, 7, 20261021, 3, 11, 99 })
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.LivingForagingSample);
            var everyone = world.HumanPopulationSnapshot!.People.Select(p => p.Id).ToHashSet();
            for (var day = 1; day <= 40 * TestCalendar.DaysPerYear; day++)
            {
                var before = world.HumanPopulationSnapshot!;
                var advance = world.AdvanceOneDay();
                if (advance.SettledWeek is not { } week) continue;
                var after = world.HumanPopulationSnapshot!;
                var born = after.People.Where(p => !before.People.Any(q => q.Id == p.Id)).ToList();
                // 不複製、不遺失：新出現的身分只能是本週出生；沒有人回到過去的名冊。
                Assert.All(born, p => Assert.Equal(week.Week * 7, p.BornDay));
                Assert.All(born, p => Assert.True(everyone.Add(p.Id)));
                var deaths = week.Settlements.Sum(l => l.Deaths) + week.Migrations.Sum(m => m.Deaths + m.NaturalDeaths);
                Assert.Equal(before.People.Length + born.Count - deaths, after.People.Length);
                Assert.Equal(world.WorldPopulation, after.People.Length);
                Assert.All(world.MigrationsInTransit(), b => Assert.Equal(b.People, after.People.Count(p => p.BatchId == b.Id)));

                foreach (var leg in week.Migrations.Where(m => m.Departed))
                {
                    found = true;
                    var members = after.People.Where(p => p.BatchId == leg.BatchId).ToList();
                    // 整戶同行：出發者的伴侶（若仍在世）與未成年子女都在同一隊。
                    foreach (var m in members.Where(m => m.PartnerId is not null))
                        Assert.Contains(members, q => q.Id == m.PartnerId);
                    // 留下的未成年子女必須還有另一位父母留在聚落照顧。
                    foreach (var parent in members)
                        Assert.DoesNotContain(after.People, c => c.IsSettled && c.SettlementId == leg.FromSettlementId
                            && !c.IsAdultAt(week.Week * 7, Rules) && (c.MotherId == parent.Id || c.BirthFatherId == parent.Id)
                            && !after.People.Any(o => o.IsSettled && o.SettlementId == leg.FromSettlementId
                                && (o.Id == c.MotherId || o.Id == c.BirthFatherId)));
                }

                if (found && world.MigrationsInTransit().Count > 0)
                {
                    // 存讀檔：途中隊伍的成員、孕期、年齡隨檔保存並驗證。
                    var loaded = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
                    Assert.Equal(after.People.ToArray(), loaded.HumanPopulationSnapshot!.People.ToArray());
                    Assert.Equal(world.StateFingerprint(), loaded.StateFingerprint());
                    return;
                }
            }
        }
        Assert.True(found, "人類繁衍情境在多個種子 40 年內都沒有任何遷徙。");
    }
}
