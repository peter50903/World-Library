using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-22c 章：身體特質（會遺傳、效果小）與傳說特性（不會遺傳、效果大）。</summary>
public sealed class DemoBodyTraitsTests
{
    private static Func<int, ulong> Draws(ulong seed) => k => (ulong)(k + 1) * 0x9E37_79B9UL ^ seed * 0x85EB_CA6BUL;

    [Fact]
    public void BodyTraitsAreRareInheritedAndNeverBothSidesOfAnAxis()
    {
        var counts = new Dictionary<DemoTrait, int>();
        var inherited = 0;
        for (ulong seed = 0; seed < 2000; seed++)
        {
            var body = DemoTraits.RollBody(Draws(seed));
            foreach (var t in DemoTraits.Body.Where(t => DemoTraits.Has(body, t)))
                counts[t] = counts.GetValueOrDefault(t) + 1;
            Assert.False(DemoTraits.Has(body, DemoTrait.Strong) && DemoTraits.Has(body, DemoTrait.Frail));
            Assert.False(DemoTraits.Has(body, DemoTrait.Clever) && DemoTraits.Has(body, DemoTrait.Dull));
            Assert.False(DemoTraits.Has(body, DemoTrait.Comely) && DemoTraits.Has(body, DemoTrait.Homely));
            if (DemoTraits.Has(DemoTraits.RollBody(Draws(seed), DemoTrait.Strong), DemoTrait.Strong))
                inherited++;
        }
        // 每一項約 12%：2000 人裡大約 240 個。
        Assert.All(DemoTraits.Body, t => Assert.InRange(counts.GetValueOrDefault(t), 120, 400));
        // 父母強壯：約一半遺傳，再加上自己抽到的。
        Assert.InRange(inherited, 900, 1400);
    }

    [Fact]
    public void LegendsAreRareNeverInheritedAndListedFirst()
    {
        var legends = 0;
        for (ulong seed = 0; seed < 2000; seed++)
        {
            var person = DemoTraits.RollPerson(Draws(seed), DemoTrait.Dragonblood | DemoTrait.Chosen, 0);
            Assert.False(DemoTraits.Legends.Any(l => DemoTraits.Has(person, l))); // 不會遺傳
            Assert.Equal(DemoTraits.TempCount, DemoTraits.List(person).Count); // 性格照舊三項
            if (DemoTraits.Legends.Any(l => DemoTraits.Has(DemoTraits.RollPerson(Draws(seed), DemoTrait.None, DemoTraits.TempHeroLegendPercent), l)))
                legends++;
        }
        Assert.InRange(legends, 650, 950); // 英雄約四成
        Assert.Equal("龍血、好戰、強壯", DemoTraits.Names(DemoTrait.Strong | DemoTrait.Warlike | DemoTrait.Dragonblood));
        Assert.True(DemoTraits.IsLegend(DemoTrait.Sage));
        Assert.False(DemoTraits.IsLegend(DemoTrait.Clever));
        Assert.All(DemoTraits.Body.Concat(DemoTraits.Legends), t => Assert.NotEqual("", DemoTraits.Effect(t)));
    }

    [Fact]
    public void TraitsChangeBattleResearchAndLoyalty()
    {
        Assert.Equal(100, DemoTraits.BattlePercent(DemoTrait.None));
        Assert.Equal(DemoTraits.TempStrongBattlePercent, DemoTraits.BattlePercent(DemoTrait.Strong));
        Assert.Equal(DemoTraits.TempBraveBattlePercent * DemoTraits.TempStrongBattlePercent / 100 * DemoTraits.TempDragonbloodBattlePercent / 100,
            DemoTraits.BattlePercent(DemoTrait.Brave | DemoTrait.Strong | DemoTrait.Dragonblood));
        Assert.Equal(100 + DemoTraits.TempCleverResearch + DemoTraits.TempSageResearch, DemoTraits.ResearchPercent(DemoTrait.Clever | DemoTrait.Sage));
        Assert.Equal(100 + DemoTraits.TempDullResearch, DemoTraits.ResearchPercent(DemoTrait.Dull));
        Assert.Contains(DemoTraits.LordLoyalty(DemoTrait.Chosen | DemoTrait.Homely), x => x.Text == "領袖天命所歸" && x.Value == DemoTraits.TempChosenLoyalty);
        Assert.Equal(DemoTraits.TempChosenLoyalty + DemoTraits.TempHomelyLoyalty, DemoTraits.Loyalty(DemoTrait.Chosen | DemoTrait.Homely));
    }

    // 第 08-22h 章：體魄與壽命——強壯比較長壽、虛弱比較短命、龍血長壽（可以和體魄相乘）。
    [Fact]
    public void ConstitutionChangesHowLongNamedPeopleLive()
    {
        Assert.Equal(100, DemoTraits.LifePercent(DemoTrait.None));
        Assert.Equal(100, DemoTraits.LifePercent(DemoTrait.Clever | DemoTrait.Brave));
        Assert.Equal(DemoTraits.TempStrongLifePercent, DemoTraits.LifePercent(DemoTrait.Strong));
        Assert.Equal(DemoTraits.TempFrailLifePercent, DemoTraits.LifePercent(DemoTrait.Frail));
        Assert.Equal(DemoTraits.TempDragonbloodLifePercent, DemoTraits.LifePercent(DemoTrait.Dragonblood));
        Assert.Equal(DemoTraits.TempStrongLifePercent * DemoTraits.TempDragonbloodLifePercent / 100, DemoTraits.LifePercent(DemoTrait.Strong | DemoTrait.Dragonblood));
        Assert.True(DemoTraits.LifePercent(DemoTrait.Strong) < 100 && DemoTraits.LifePercent(DemoTrait.Frail) > 100);
        Assert.Contains("長壽", DemoTraits.Effect(DemoTrait.Strong), StringComparison.Ordinal);
    }
}
