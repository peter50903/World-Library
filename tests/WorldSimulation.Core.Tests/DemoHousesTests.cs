using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-10g 章：領袖的性格、世襲與家族。</summary>
public sealed class DemoHousesTests
{
    private static readonly Lazy<DemoObserverWorld> Shared = new(() =>
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 45 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        return world;
    });

    [Fact]
    public void TraitsComeInTwoFromDifferentPairsAndCanBeInherited()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            ulong Draw(int k) => seed * 7919UL + (ulong)k * 104729UL + seed / 3UL;
            var traits = DemoTraits.Roll(Draw);
            var list = DemoTraits.List(traits);
            Assert.Equal(DemoTraits.TempCount, list.Count);
            Assert.All(list, t => Assert.False(DemoTraits.Has(traits, DemoTraits.Opposite(t)))); // 同一組不會兩邊都有
            // 有父母：仍是三項、不同組；承襲時其中一項來自父母。
            var parent = DemoTrait.Warlike | DemoTrait.Cruel | DemoTrait.Ambitious;
            var child = DemoTraits.Roll(Draw, parent);
            Assert.Equal(DemoTraits.TempCount, DemoTraits.List(child).Count);
            Assert.All(DemoTraits.List(child), t => Assert.False(DemoTraits.Has(child, DemoTraits.Opposite(t))));
            if ((int)(Draw(DemoTraits.TempCount * 2) % 100UL) < DemoTraits.TempInheritPercent)
                Assert.True((child & parent) != DemoTrait.None);
            else
                Assert.Equal(traits, child);
        }
        Assert.Equal("好戰、暴虐", DemoTraits.Names(DemoTrait.Cruel | DemoTrait.Warlike));
        Assert.Equal("", DemoTraits.Names(DemoTrait.None));
        Assert.Equal(DemoTraits.TempLenientPetition, DemoTraits.Petition(DemoTrait.Lenient | DemoTrait.Just));
        Assert.Equal(DemoTraits.TempHarshPetition, DemoTraits.Petition(DemoTrait.Harsh));
        Assert.Equal(DemoTraits.TempCruelLoyalty, DemoTraits.Loyalty(DemoTrait.Cruel | DemoTrait.Warlike));
        Assert.Equal(0, DemoTraits.Loyalty(DemoTrait.Warlike));
        // 第 08-10h 章：十六項性格各有名稱與說明；每一項的相反是同一組的另一項。
        Assert.Equal(16, DemoTraits.All.Count);
        Assert.Equal(16, DemoTraits.All.Select(DemoTraits.Name).Where(n => n.Length > 0).Distinct().Count());
        Assert.Equal(16, DemoTraits.All.Select(DemoTraits.Effect).Where(n => n.Length > 0).Distinct().Count());
        Assert.All(DemoTraits.All, t => Assert.Equal(t, DemoTraits.Opposite(DemoTraits.Opposite(t))));
        Assert.Equal(DemoTraits.TempBraveBattlePercent, DemoTraits.BattlePercent(DemoTrait.Brave));
        Assert.Equal(DemoTraits.TempCravenBattlePercent, DemoTraits.BattlePercent(DemoTrait.Craven | DemoTrait.Just));
        Assert.Equal(100, DemoTraits.BattlePercent(DemoTrait.Just));
        Assert.Equal(DemoTraits.TempStubbornSurrenderPercent, DemoTraits.SurrenderPercent(DemoTrait.Stubborn, 500));
        Assert.Equal(DemoTraits.TempYieldingSurrenderPercent, DemoTraits.SurrenderPercent(DemoTrait.Yielding, 500));
        Assert.Equal(500, DemoTraits.SurrenderPercent(DemoTrait.Brave, 500));
        Assert.Equal(DemoTraits.TempWarlikeChance, DemoTraits.WarChance(DemoTrait.Warlike | DemoTrait.Honest));
    }

    [Fact]
    public void NamesSplitIntoGivenAndFamily()
    {
        Assert.Equal("莫頓", DemoNames.FamilyOf("艾妲·莫頓"));
        Assert.Equal("艾妲", DemoNames.GivenOf("艾妲·莫頓"));
        Assert.Equal("無名者7", DemoNames.FamilyOf("無名者7"));
    }

    [Fact]
    public void LeadersHaveTraitsAndHeirsKeepTheFamilyName()
    {
        var world = Shared.Value;
        var leaders = world.PersonRecords.Where(p => p.Role == DemoRepresentativeRole.Leader).ToList();
        var byId = leaders.ToDictionary(p => p.Id);
        // 每位領袖兩項性格；其他職位沒有性格、沒有父母。
        Assert.All(leaders, p => Assert.Equal(DemoTraits.TempCount, DemoTraits.List(p.Traits).Count));
        Assert.All(world.PersonRecords.Where(p => p.Role is not (DemoRepresentativeRole.Leader or DemoRepresentativeRole.Hero)),
            p => Assert.Equal((DemoTrait.None, 0L), (p.Traits, p.ParentId)));
        // 世襲：45 年裡一定有人繼承；繼承人與父母同姓不同名、同一個聚落、比父母小 18～45 歲、在父母離任那一週接任。
        var heirs = leaders.Where(p => p.ParentId > 0).ToList();
        Assert.NotEmpty(heirs);
        var everyone = world.PersonRecords.ToDictionary(p => p.Id);
        List<string> Ancestors(DemoPersonRecord p)
        {
            var names = new List<string>();
            var id = p.ParentId;
            while (id > 0 && everyone.TryGetValue(id, out var a) && names.Count < 100)
            {
                names.Add(DemoNames.GivenOf(a.Name));
                id = a.ParentId;
            }
            return names;
        }
        Assert.All(heirs, heir =>
        {
            var parent = byId[heir.ParentId];
            Assert.Equal(DemoNames.FamilyOf(parent.Name), DemoNames.FamilyOf(heir.Name));
            Assert.DoesNotContain(DemoNames.GivenOf(heir.Name), Ancestors(heir)); // 第 08-10k 節：家譜裡不同名
            Assert.Equal(parent.SettlementId, heir.SettlementId);
            Assert.InRange(heir.BirthYear - parent.BirthYear, DemoHumanLifecycle.TempHeirMinGapYears, DemoHumanLifecycle.TempHeirMaxGapYears);
            Assert.Equal(parent.ExitWeek, heir.AppointedWeek);
        });
        // 在任領袖與登記表一致（性格、父母都帶著）；人物頁寫出家族與性格。
        var inOffice = world.Sites.Settlements.SelectMany(s => world.RepresentativesOf(s.Id)).First(r => r.Role == DemoRepresentativeRole.Leader);
        Assert.Equal((inOffice.Traits, inOffice.ParentId), (byId[inOffice.Id].Traits, byId[inOffice.Id].ParentId));
        var page = DemoPersonDetail.Of(world, inOffice.Id)!.Tabs[0].Lines;
        Assert.Contains(page, l => l.StartsWith($"家族：{DemoNames.FamilyOf(inOffice.Name)}家", StringComparison.Ordinal));
        Assert.Contains("性格：" + DemoTraits.Names(inOffice.Traits), page);
        // 史書終章的家族：每一句都是登記表上某位領袖的姓。
        var houses = new DemoSagaCast(world).Houses(100);
        Assert.NotEmpty(houses);
        Assert.All(houses, h => Assert.Contains(leaders, p => h.Contains(DemoNames.FamilyOf(p.Name) + "家（傳 ", StringComparison.Ordinal)));
    }

    // 第 05-2 章：家族頁——開創者打得開，成員、家譜與連結都對得上登記表；不是開創者的編號打不開。
    [Fact]
    public void HousePageListsTheFoundersLine()
    {
        var world = Shared.Value;
        var people = world.PersonRecords.ToDictionary(p => p.Id);
        var founders = people.Values.Where(p => DemoHouses.HouseOf(people, p.Id) == p.Id
            && DemoHouses.Members(people, p.Id).Count(m => m.Role == DemoRepresentativeRole.Leader) >= 2).ToList();
        Assert.NotEmpty(founders);
        var founder = founders[0];
        var members = DemoHouses.Members(people, founder.Id);
        var before = world.ExportTempSave();
        var detail = DemoHouseDetail.Of(world, founder.Id);
        Assert.Equal(before, world.ExportTempSave()); // 只讀
        Assert.NotNull(detail);
        Assert.Equal(new[] { "家族", "成員", "家譜" }, detail!.Tabs.Select(t => t.Title));
        Assert.Equal(founder.SettlementId, detail.SettlementId);
        Assert.Contains(DemoNames.FamilyOf(founder.Name) + "家", detail.Title, StringComparison.Ordinal);
        Assert.Equal(members.Select(m => m.Id), detail.Tabs[1].People.Select(p => p.Id));
        Assert.StartsWith(founder.Name + "（", detail.Tabs[2].Lines[0], StringComparison.Ordinal);
        Assert.Equal(members.Count, detail.Tabs[2].Lines.Count);
        Assert.All(members, m => Assert.Equal(founder.Id, DemoHouses.HouseOf(people, m.Id)));
        Assert.All(members.Where(m => m.Id != founder.Id), m => Assert.True(DemoHouses.GenerationOf(people, m.Id) >= 2));
        Assert.Equal(1, DemoHouses.GenerationOf(people, founder.Id));
        Assert.All(detail.Tabs[0].Links, l => Assert.True(l.Kind == DemoLinkKind.Settlement
            ? world.Sites.Settlements.Any(s => s.Id == l.Id) : people.ContainsKey(l.Id)));
        // 繼承人不是開創者：打不開；不存在的編號也打不開。
        var heir = members.First(m => m.Id != founder.Id);
        Assert.Null(DemoHouseDetail.Of(world, heir.Id));
        Assert.Null(DemoHouseDetail.Of(world, -1));
    }

    // 第 05-2 章：已過世的人也打得開人物頁；有父母或子女的有「家人」頁，連結指向登記表上的人。
    [Fact]
    public void PastPeopleHaveAPageAndTheirFamily()
    {
        var world = Shared.Value;
        var people = world.PersonRecords.ToDictionary(p => p.Id);
        var gone = people.Values.Where(p => !p.InOffice && DemoPersonDetail.Died(p)).OrderByDescending(p => p.ExitWeek).ThenBy(p => p.Id).ToList();
        Assert.NotEmpty(gone);
        var detail = DemoPersonDetail.Of(world, gone[0].Id);
        Assert.NotNull(detail);
        Assert.Contains("（已故）", detail!.Title, StringComparison.Ordinal);
        Assert.Equal(new[] { "人物", "生平" }, detail.Tabs.Select(t => t.Title).Take(2));
        Assert.Contains(detail.Tabs[0].Lines, l => l.Contains(DemoSagaCast.ExitText(gone[0].ExitReason), StringComparison.Ordinal));
        Assert.Equal(gone[0].Id, Assert.Single(detail.Tabs[0].People).Id);
        // 有父母的領袖：家人頁有父母，人物頁有父母與家族的連結。
        var heir = people.Values.First(p => p.Role == DemoRepresentativeRole.Leader && p.ParentId > 0);
        var page = DemoPersonDetail.Of(world, heir.Id)!;
        var family = Assert.Single(page.Tabs, t => t.Title == "家人");
        Assert.Contains(family.People, e => e.Id == heir.ParentId);
        Assert.Contains(page.Tabs[0].Links, l => l.Kind == DemoLinkKind.Person && l.Id == heir.ParentId);
        Assert.Contains(page.Tabs[0].Links, l => l.Kind == DemoLinkKind.House && l.Id == DemoHouses.HouseOf(people, heir.Id));
        // 父母的家人頁有這個孩子。
        var parentPage = DemoPersonDetail.Of(world, heir.ParentId)!;
        Assert.Contains(Assert.Single(parentPage.Tabs, t => t.Title == "家人").People, e => e.Id == heir.Id);
    }

    [Fact]
    public void LordsTemperChangesLoyalty()
    {
        var world = Shared.Value;
        // 第 12-2h 章：獸潮可能攻破聚落；這裡只用編號（人口由假的情境給），遺跡也可以。
        var sites = world.Sites.Settlements.OrderBy(s => s.Id).ToList();
        var (a, b) = (sites[0].Id, sites[1].Id);
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        var plain = new DemoRealmContext(world.Sites, _ => 50, _ => 1, _ => null, _ => DemoSettlementTier.Village, _ => false);
        DemoRealmContext With(DemoTrait traits) => plain with
        {
            Leader = id => id == a ? new DemoRepresentative(1, a, DemoRepresentativeRole.Leader, "甲·乙", DemoTempSex.Female, 1, 1, 0, traits) : null
        };
        var basis = DemoRealms.LoyaltyValue(b, a, 100, fold, plain);
        Assert.Equal(basis + DemoTraits.TempJustLoyalty, DemoRealms.LoyaltyValue(b, a, 100, fold, With(DemoTrait.Just | DemoTrait.Warlike)));
        Assert.Equal(basis + DemoTraits.TempCruelLoyalty, DemoRealms.LoyaltyValue(b, a, 100, fold, With(DemoTrait.Cruel | DemoTrait.Harsh)));
        Assert.Equal(basis, DemoRealms.LoyaltyValue(b, a, 100, fold, With(DemoTrait.Warlike | DemoTrait.Lenient)));
        Assert.Contains(DemoRealms.Loyalty(b, a, 100, fold, With(DemoTrait.Cruel | DemoTrait.Harsh)), f => f.Text == "領袖暴虐");
        Assert.Equal(basis + DemoTraits.TempHonestLoyalty, DemoRealms.LoyaltyValue(b, a, 100, fold, With(DemoTrait.Honest)));
        Assert.Equal(basis + DemoTraits.TempDeceitfulLoyalty, DemoRealms.LoyaltyValue(b, a, 100, fold, With(DemoTrait.Deceitful)));
        // 下級自己的領袖：野心更不忠誠、知足更忠誠。
        DemoRealmContext Vassal(DemoTrait traits) => plain with
        {
            Leader = id => id == b ? new DemoRepresentative(2, b, DemoRepresentativeRole.Leader, "丙·丁", DemoTempSex.Male, 1, 1, 0, traits) : null
        };
        Assert.Equal(basis + DemoTraits.TempAmbitiousLoyalty, DemoRealms.LoyaltyValue(b, a, 100, fold, Vassal(DemoTrait.Ambitious)));
        Assert.Equal(basis + DemoTraits.TempContentLoyalty, DemoRealms.LoyaltyValue(b, a, 100, fold, Vassal(DemoTrait.Content)));
        // 信仰不同：多疑的上級不在意；虔誠的上級更容不下。
        var faiths = plain with { Faith = id => id == a ? 1 : 2 };
        DemoRealmContext Lord(DemoTrait traits) => faiths with
        {
            Leader = id => id == a ? new DemoRepresentative(1, a, DemoRepresentativeRole.Leader, "甲·乙", DemoTempSex.Female, 1, 1, 0, traits) : null
        };
        var differs = DemoRealms.LoyaltyValue(b, a, 100, fold, faiths);
        Assert.Equal(basis - 15, differs);
        Assert.Equal(basis, DemoRealms.LoyaltyValue(b, a, 100, fold, Lord(DemoTrait.Cynical)));
        Assert.Equal(differs + DemoTraits.TempZealousFaithLoyalty, DemoRealms.LoyaltyValue(b, a, 100, fold, Lord(DemoTrait.Zealous)));
    }
}
