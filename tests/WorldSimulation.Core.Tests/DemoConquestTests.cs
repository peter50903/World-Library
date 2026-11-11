using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-19e 章：征服時代的王國戰爭（宣戰、交戰、征服、白和平與停戰）。</summary>
public sealed class DemoConquestTests
{
    /// <summary>王國 a（城市、有臣屬 b）與 10 格內獨立的 c。</summary>
    private static (DemoWorldSites Sites, int A, int B, int C) Setup()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        int D(DemoSettlement x, DemoSettlement y) => DemoHexWorld.Distance(new DemoCellRef(x.Column, x.Row), new DemoCellRef(y.Column, y.Row));
        var (a, c) = alive.SelectMany(x => alive.Where(y => y.Id != x.Id && D(x, y) <= DemoRealms.TempConquestReach).Select(y => (x, y))).First();
        var b = alive.First(s => s.Id != a.Id && s.Id != c.Id);
        return (world.Sites, a.Id, b.Id, c.Id);
    }

    private static DemoRealmContext Ctx(DemoWorldSites sites, int king, Func<int, long> residents, Func<int, int?>? faith = null)
        => new(sites, residents, _ => 1, faith ?? (_ => null), id => id == king ? DemoSettlementTier.City : DemoSettlementTier.Village, _ => false);

    private static IReadOnlyList<DemoSettlementEvent> Step(DemoRealms.Fold fold, int week, DemoRealmContext ctx, RngRegistry rng, bool conquest = true)
    {
        var battles = DemoRealms.Battles(week, fold, ctx.Sites, ctx.Residents, new HashSet<int>(), RngRegistry.Create(1)).Values
            .Select((b, k) => new DemoSettlementEvent(week, TimeSpan.FromDays(week * 7), b.SettlementId, "", b.Kind, b.Deaths, b.Cause, EventId: week * 100 + k))
            .ToList();
        foreach (var e in battles)
            fold.Apply(e);
        var events = DemoRealms.Advance(week, TimeSpan.FromDays(week * 7), fold, ctx, new Dictionary<int, int>(), rng, false, week * 100 + 50, conquest);
        foreach (var e in events)
            fold.Apply(e);
        return battles.Concat(events).ToList();
    }

    [Fact]
    public void StrongKingdomDeclaresFightsAndConquers()
    {
        var (sites, a, b, c) = Setup();
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        var ctx = Ctx(sites, a, id => id == a ? 200 : id == b ? 100 : id == c ? 50 : 0, id => id == c ? 2 : 1);
        var rng = RngRegistry.Create(7);
        var all = new List<DemoSettlementEvent>();
        for (var week = 2; week < 2 + 13 * 60 && !all.Any(e => e.Kind is DemoSettlementEventKind.Conquered or DemoSettlementEventKind.WarEnded); week++)
            all.AddRange(Step(fold, week, ctx, rng));
        var declared = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.WarDeclared);
        Assert.Equal((a, (long)c), (declared.SettlementId, declared.Count));
        Assert.Contains("聖戰", declared.Cause, StringComparison.Ordinal); // 信仰不同
        Assert.Contains(all, e => e.Kind == DemoSettlementEventKind.ConquestBattle && e.SettlementId == c && e.Count > 0);
        Assert.Contains(all, e => e.Kind == DemoSettlementEventKind.ConquestBattle && e.SettlementId == a);
        var won = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.Conquered);
        Assert.Equal((c, (long)a), (won.SettlementId, won.Count));
        Assert.Equal(a, fold.Liege[c]);
        Assert.Empty(fold.Wars);
        Assert.Contains(DemoRealms.Loyalty(c, a, won.Week + 1, fold, ctx), f => f.Text == "剛被征服" && f.Value == DemoRealms.TempConqueredLoyalty);
        Assert.True(won.Week - declared.Week >= DemoRealms.TempWarMinWeeks);
    }

    [Fact]
    public void NoWarsBeforeTheConquestEraOrWhenTooWeak()
    {
        var (sites, a, b, c) = Setup();
        foreach (var (residents, conquest) in new[] { (200L, false), (20L, true) })
        {
            var fold = new DemoRealms.Fold();
            fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
            var ctx = Ctx(sites, a, id => id == a ? residents : id == b ? 10 : id == c ? 300 : 0);
            var rng = RngRegistry.Create(7);
            for (var week = 2; week < 2 + 13 * 30; week++)
                Assert.DoesNotContain(Step(fold, week, ctx, rng, conquest), e => e.Kind == DemoSettlementEventKind.WarDeclared);
        }
    }

    [Fact]
    public void WhitePeaceBringsATruceAndTheWarIsDescribed()
    {
        var (sites, a, b, c) = Setup();
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        fold.Apply(new DemoSettlementEvent(5, TimeSpan.Zero, a, "", DemoSettlementEventKind.WarDeclared, c, "甲向丙宣戰"));
        var war = Assert.Single(fold.Wars.Values);
        Assert.Equal((a, c, c, true), (war.AttackerId, war.DefenderId, war.TargetId, war.WholeRealm));
        Assert.Same(war, fold.WarOf(c));
        Assert.Contains("征服戰爭", DemoRealms.DescribeWar(war, sites, 9), StringComparison.Ordinal);
        fold.Apply(new DemoSettlementEvent(30, TimeSpan.Zero, a, "", DemoSettlementEventKind.WarEnded, c, ""));
        Assert.Empty(fold.Wars);
        Assert.True(fold.WarTruce(c, a, 31));
        Assert.False(fold.WarTruce(a, c, 30 + DemoRealms.TempConquestTruceWeeks));
    }

    [Fact]
    public void LawsCanStopWars()
    {
        Assert.False(DemoWorldLaws.Default.With("wars", false).Wars);
        Assert.Contains(DemoWorldLaws.Default.Entries, e => e.Key == "wars" && e.On);
    }
}
