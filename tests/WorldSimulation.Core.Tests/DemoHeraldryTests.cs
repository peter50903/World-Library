using System.Collections.Immutable;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-16 章：人像、紋章、信仰符號的選擇規則（固定、不抽世界亂數）。</summary>
public sealed class DemoHeraldryTests
{
    private static DemoRepresentative Rep(long id, DemoTempSex sex, DemoRepresentativeRole role = DemoRepresentativeRole.Leader)
        => new(id, 1, role, "甲·乙", sex, 1, 1);

    [Fact]
    public void PortraitsAreStableAndFollowSexAgeAndRole()
    {
        for (long id = 1; id <= 300; id++)
        {
            var female = id % 2 == 0;
            var r = Rep(id, female ? DemoTempSex.Female : DemoTempSex.Male, (DemoRepresentativeRole)(id % 7));
            var young = DemoHeraldry.PortraitOf(r, 25, 3);
            Assert.Equal(young, DemoHeraldry.PortraitOf(r, 25, 3));
            Assert.Equal(female, young.Female);
            Assert.InRange(young.Face, 1, DemoHeraldry.FacesPerSex);
            Assert.InRange(young.Hair, 1, DemoHeraldry.HairsPerSex);
            Assert.InRange(young.SkinTone, 0, 4);
            Assert.InRange(young.HairColor, 0, 4); // 年輕人不會灰白
            if (female) Assert.Equal(0, young.Beard); else Assert.InRange(young.Beard, 0, 3);
            Assert.Equal(0, young.Age);
            Assert.Equal(1, DemoHeraldry.PortraitOf(r, 45, 3).Age);
            var old = DemoHeraldry.PortraitOf(r, 70, 3);
            Assert.Equal((2, 5), (old.Age, old.HairColor));
            Assert.Equal(young.Face, old.Face); // 同一個人變老，臉不變
            Assert.Equal(DemoHeraldry.ClothesOf(r.Role), young.Clothes);
        }
        Assert.Equal("blacksmith", DemoHeraldry.ClothesOf(DemoRepresentativeRole.Smith));
    }

    [Fact]
    public void EmblemsFollowCultureTraitsAndFaithSymbolsFollowTenets()
    {
        var forest = new DemoCulture(5, "林", ImmutableList.Create("CT_FOREST_FOLK", "CT_MARTIAL"));
        Assert.Equal("tree", DemoHeraldry.EmblemOf(forest).Charge);
        Assert.Contains(DemoHeraldry.EmblemOf(forest).Field, DemoHeraldry.Fields);
        var plain = new DemoCulture(9, "無", ImmutableList<string>.Empty);
        Assert.Contains(DemoHeraldry.EmblemOf(plain).Charge, DemoHeraldry.Charges);
        var ancestors = new DemoFaith(1, "祖", 1, 1, 1, ImmutableList.Create("DOC_WAR", "DOC_ANCESTOR"), ImmutableList<string>.Empty);
        Assert.Equal("ancestor", DemoHeraldry.FaithSymbolOf(ancestors));
        var other = new DemoFaith(2, "他", 1, 1, 1, ImmutableList.Create("DOC_WEALTH"), ImmutableList<string>.Empty);
        Assert.Contains(DemoHeraldry.FaithSymbolOf(other), DemoHeraldry.FaithSymbols);
    }

    [Fact]
    public void SettlementDetailCarriesOnePortraitPerRepresentative()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 20 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        foreach (var s in world.Sites.Settlements.Where(s => !s.IsRuin))
        {
            var detail = world.PlayerDetail(s.Column, s.Row);
            var people = detail.Tabs[1].People;
            Assert.Equal(world.RepresentativesOf(s.Id).Select(r => r.Id).OrderBy(i => i), people.Select(p => p.Id).OrderBy(i => i));
            Assert.Equal(detail.Tabs[1].Lines, people.SelectMany(p => p.Lines).DefaultIfEmpty(detail.Tabs[1].Lines.FirstOrDefault() ?? "").ToList());
            if (world.Culture.MainCultureOf(s.Id) is not null)
                Assert.NotNull(detail.Tabs[0].Emblem);
        }
    }
}
