using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-19f 章：繼位、篡位派系、內戰與換王朝（十字軍之王：國家不變，換統治者）。</summary>
public sealed class DemoCivilWarTests
{
    private static (DemoWorldSites Sites, int A, int B) Setup()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        return (world.Sites, alive[0].Id, alive[1].Id);
    }

    /// <summary>王國 a（城市）與同文化的臣屬 b；村長 <paramref name="appointed"/> 週上任。</summary>
    private static DemoRealmContext Ctx(DemoWorldSites sites, int a, int b, long residentsA, long residentsB, int appointed = 1)
        => new(sites, id => id == a ? residentsA : id == b ? residentsB : 0, _ => 1, _ => null,
            id => id == a ? DemoSettlementTier.City : DemoSettlementTier.Village, _ => false)
        {
            Leader = id => new DemoRepresentative(id * 10L + 1, id, DemoRepresentativeRole.Leader, $"王{id}", DemoTempSex.Male, 1, appointed)
        };

    private static List<DemoSettlementEvent> Run(DemoRealms.Fold fold, DemoRealmContext ctx, int from, int to)
    {
        var rng = RngRegistry.Create(5);
        var all = new List<DemoSettlementEvent>();
        for (var week = from; week < to; week++)
        {
            var events = DemoRealms.Advance(week, TimeSpan.FromDays(week * 7), fold, ctx, new Dictionary<int, int>(), rng, true, week * 100 + 50, conquestEra: true);
            foreach (var e in events)
                fold.Apply(e);
            all.AddRange(events);
        }
        return all;
    }

    private static DemoRealms.Fold Kingdom(int a, int b)
    {
        var fold = new DemoRealms.Fold();
        fold.Apply(new DemoSettlementEvent(1, TimeSpan.Zero, b, "", DemoSettlementEventKind.VassalJoined, a, ""));
        return fold;
    }

    [Fact]
    public void StrongSameCultureFactionForcesTheKingOutAndTheCrownMoves()
    {
        var (sites, a, b) = Setup();
        var fold = Kingdom(a, b);
        var ctx = Ctx(sites, a, b, 100, 200);
        var all = Run(fold, ctx, 2, 2 + DemoRealms.TempDiscontentWeeks + 3);
        var joined = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.FactionJoined);
        Assert.Contains("篡位派系", joined.Cause, StringComparison.Ordinal);
        var usurped = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.Usurped);
        Assert.Equal((b, (long)a), (usurped.SettlementId, usurped.Count));
        Assert.DoesNotContain(all, e => e.Kind is DemoSettlementEventKind.Independence or DemoSettlementEventKind.SecessionArmed);
        // 國家、隸屬不變，王座換到 b。
        Assert.Equal(a, fold.Liege[b]);
        Assert.Equal(b, fold.Seat(a));
        var factors = DemoRealms.Loyalty(b, a, usurped.Week + 1, fold, ctx);
        Assert.Contains(factors, f => f.Text == "國王出身於此");
        Assert.Contains(factors, f => f.Text == "擁立了現任國王");
        Assert.Contains(factors, f => f.Text == "新君即位");
        Assert.Contains("王朝", DemoRealms.RulerLine(a, fold, ctx.Leader!, sites, ctx.Tier, 400)!, StringComparison.Ordinal);
    }

    [Fact]
    public void EvenFactionStartsACivilWar()
    {
        var (sites, a, b) = Setup();
        var fold = Kingdom(a, b);
        var all = Run(fold, Ctx(sites, a, b, 100, 100), 2, 2 + DemoRealms.TempDiscontentWeeks + 3);
        var war = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.CivilWar);
        Assert.Equal((b, (long)a), (war.SettlementId, war.Count));
        var struggle = Assert.Single(fold.Struggles.Values);
        Assert.True(struggle.Civil && struggle.Armed);
        Assert.Contains("內戰", DemoRealms.Describe(struggle, sites, war.Week + 2), StringComparison.Ordinal);
        var battles = DemoRealms.Battles(war.Week + 1, fold, sites, id => 100, new HashSet<int>(), RngRegistry.Create(5));
        Assert.Contains(battles.Values, x => x.Cause.Contains("內戰", StringComparison.Ordinal));
    }

    [Fact]
    public void NewKingIsRecordedAndCostsLoyalty()
    {
        var (sites, a, b) = Setup();
        var fold = Kingdom(a, b);
        var ctx = Ctx(sites, a, b, 300, 10, appointed: 7);
        var all = Run(fold, ctx, 2, 10);
        var crowned = Assert.Single(all, e => e.Kind == DemoSettlementEventKind.Succession);
        Assert.Equal((a, 7, a * 10L + 1), (crowned.SettlementId, crowned.Week, crowned.Count));
        Assert.Equal(7, fold.ReignWeek(a));
        Assert.Contains(DemoRealms.Loyalty(b, a, 8, fold, ctx), f => f.Text == "新君即位" && f.Value == DemoRealms.TempNewReignLoyalty);
        Assert.DoesNotContain(DemoRealms.Loyalty(b, a, 7 + DemoRealms.TempNewReignWeeks, fold, ctx), f => f.Text == "新君即位");
    }
}
