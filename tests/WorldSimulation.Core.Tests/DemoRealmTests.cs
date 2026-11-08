using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-19b 章：歸附、脫離鬥爭（武力、非武力）、獨立與失敗。</summary>
public sealed class DemoRealmTests
{
    private static (DemoObserverWorld World, int A, int B, int C) Setup()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        var alive = world.Sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        Assert.True(alive.Count >= 3);
        return (world, alive[0].Id, alive[1].Id, alive[2].Id);
    }

    /// <summary>把一週的事件餵進摺疊，回傳事件。</summary>
    private static IReadOnlyList<DemoSettlementEvent> Step(DemoRealms.Fold fold, int week, DemoWorldSites sites, IReadOnlyDictionary<int, int> natural,
        Func<int, long> residents, Func<int, int?> culture, RngRegistry rng)
    {
        var events = DemoRealms.Advance(week, TimeSpan.FromDays(week * 7), fold, sites, natural, residents, culture, rng, 1000 + week * 10);
        foreach (var e in events)
            fold.Apply(e);
        return events;
    }

    [Fact]
    public void SettlementsJoinTheirNaturalLiegeAndStruggleBeforeLeaving()
    {
        var (world, a, b, c) = Setup();
        var rng = RngRegistry.Create(9);
        var fold = new DemoRealms.Fold();
        long Residents(int id) => id == a ? 300 : 100;
        int? SameCulture(int id) => 1;
        // 第 1 週：b、c 落在 a 的範圍裡而歸附。
        var natural = new Dictionary<int, int> { [b] = a, [c] = a };
        var joined = Step(fold, 1, world.Sites, natural, Residents, SameCulture, rng);
        Assert.Equal(new[] { b, c }, joined.Where(e => e.Kind == DemoSettlementEventKind.VassalJoined).Select(e => e.SettlementId).OrderBy(x => x));
        Assert.All(joined, e => Assert.Equal(a, e.Count));
        Assert.Equal(a, fold.Liege[b]);
        // 沒有變化：沒有事件。
        Assert.Empty(Step(fold, 2, world.Sites, natural, Residents, SameCulture, rng));
        // 第 3 週：b 壯大到不必依附——不會立刻脫離，而是開始鬥爭（文化相同時武力或非武力）。
        var grown = new Dictionary<int, int> { [c] = a };
        var begin = Assert.Single(Step(fold, 3, world.Sites, grown, Residents, SameCulture, rng));
        Assert.Equal(b, begin.SettlementId);
        Assert.Contains(begin.Kind, new[] { DemoSettlementEventKind.SecessionArmed, DemoSettlementEventKind.SecessionPeaceful });
        Assert.Equal(a, begin.Count);
        var struggle = fold.Struggles[b];
        Assert.Equal(a, fold.Liege[b]); // 鬥爭期間仍隸屬
        Assert.Equal(struggle.Armed ? DemoRealms.TempArmedWeeks : DemoRealms.TempPeacefulWeeks, struggle.EndWeek - 3);
        Assert.NotNull(DemoRealms.Describe(struggle, world.Sites, 4));
        // 鬥爭期間沒有新事件；期滿時獨立或失敗。
        for (var week = 4; week < struggle.EndWeek; week++)
            Assert.Empty(Step(fold, week, world.Sites, grown, Residents, SameCulture, rng));
        var end = Assert.Single(Step(fold, struggle.EndWeek, world.Sites, grown, Residents, SameCulture, rng));
        Assert.Contains(end.Kind, new[] { DemoSettlementEventKind.Independence, DemoSettlementEventKind.SecessionFailed });
        Assert.Empty(fold.Struggles);
        if (end.Kind == DemoSettlementEventKind.Independence)
            Assert.False(fold.Liege.ContainsKey(b));
        else
        {
            // 失敗：冷卻期內不再鬥爭。
            Assert.Equal(a, fold.Liege[b]);
            Assert.True(fold.Calm(b, struggle.EndWeek + DemoRealms.TempCooldownWeeks - 1));
            Assert.Empty(Step(fold, struggle.EndWeek + 1, world.Sites, grown, Residents, SameCulture, rng));
            Assert.False(fold.Calm(b, struggle.EndWeek + DemoRealms.TempCooldownWeeks));
        }
    }

    [Fact]
    public void DifferentCulturesFightAndLiegeRuinFreesTheVassal()
    {
        var (world, a, b, _) = Setup();
        var rng = RngRegistry.Create(3);
        var fold = new DemoRealms.Fold();
        var natural = new Dictionary<int, int> { [b] = a };
        long Residents(int id) => 120;
        int? Culture(int id) => id == a ? 1 : 2;
        Step(fold, 1, world.Sites, natural, Residents, Culture, rng);
        // 文化不同一定走武力；武力抗爭每週兩邊都有傷亡（天災、怪物優先）。
        var armed = Assert.Single(Step(fold, 2, world.Sites, new Dictionary<int, int>(), Residents, Culture, rng));
        Assert.Equal(DemoSettlementEventKind.SecessionArmed, armed.Kind);
        var battles = DemoRealms.Battles(3, fold, world.Sites, Residents, new HashSet<int>());
        Assert.Equal(new[] { a, b }.OrderBy(x => x), battles.Keys.OrderBy(x => x));
        Assert.All(battles.Values, s => Assert.Equal(DemoSettlementEventKind.SecessionBattle, s.Kind));
        Assert.Equal(1 + 120 / DemoRealms.TempRebelsPerDeath, battles[b].Deaths);
        Assert.Equal(1 + 120 / DemoRealms.TempLoyalistsPerDeath, battles[a].Deaths);
        Assert.Equal(new[] { a }, DemoRealms.Battles(3, fold, world.Sites, Residents, new HashSet<int> { b }).Keys);
        Assert.Empty(DemoRealms.Battles(2, fold, world.Sites, Residents, new HashSet<int>())); // 開始那週還沒開打
        Assert.Empty(DemoRealms.Battles(2 + DemoRealms.TempArmedWeeks, fold, world.Sites, Residents, new HashSet<int>()));
        // 上級沒人了：直接獨立（不必等鬥爭結束）。
        var free = Assert.Single(Step(fold, 4, world.Sites, new Dictionary<int, int>(), id => id == a ? 0 : 120, Culture, rng));
        Assert.Equal((DemoSettlementEventKind.Independence, b, (long)a), (free.Kind, free.SettlementId, free.Count));
        Assert.False(fold.Liege.ContainsKey(b));
        Assert.Empty(fold.Struggles);
    }

    [Fact]
    public void JoiningNeverFormsACycleAndResultsAreDeterministic()
    {
        var (world, a, b, _) = Setup();
        var fold = new DemoRealms.Fold();
        var events = Step(fold, 1, world.Sites, new Dictionary<int, int> { [a] = b, [b] = a }, _ => 50, _ => 1, RngRegistry.Create(1));
        Assert.Single(events);
        // 同樣的輸入、同樣的種子：同樣的結果。
        var again = DemoRealms.Advance(1, TimeSpan.FromDays(7), new DemoRealms.Fold(), world.Sites, new Dictionary<int, int> { [a] = b, [b] = a },
            _ => 50, _ => 1, RngRegistry.Create(1), 1010);
        Assert.Equal(events, again);
    }

    [Fact]
    public void TerritoryUsesTheLiegesFromEvents()
    {
        var (world, a, b, c) = Setup();
        var map = DemoTerritory.Compute(world.Map, world.Sites, _ => DemoSettlementTier.Village, null, new Dictionary<int, int> { [b] = a, [c] = b });
        Assert.Equal(a, map.LiegeOf(b));
        Assert.Equal(a, map.RealmOf(c));
        Assert.Null(map.LiegeOf(a));
        // 沒有給隸屬時照層級（同級沒有上級）。
        Assert.Empty(DemoTerritory.Compute(world.Map, world.Sites, _ => DemoSettlementTier.Village).Liege);
    }

    [Fact]
    public void LivingWorldRecordsRealmEventsWithValidCauses()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(4UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 7 * 104; d++)
            world.AdvanceOneDay();
        var index = world.BuildCausalIndex();
        Assert.Empty(index.Problems);
        // 隸屬只由事件決定：勢力範圍的上級都是由事件推得的。
        Assert.Equal(world.Realms().Liege.Where(kv => world.Territory().Realm.ContainsKey(kv.Key) && world.Territory().Realm.ContainsKey(kv.Value))
            .OrderBy(kv => kv.Key), world.Territory().Liege.OrderBy(kv => kv.Key));
    }
}
