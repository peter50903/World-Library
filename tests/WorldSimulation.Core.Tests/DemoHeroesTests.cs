using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-10i 章：英雄與傳說物品。</summary>
public sealed class DemoHeroesTests
{
    private static DemoSettlementEvent Event(int week, int settlement, DemoSettlementEventKind kind, long count, string cause, long id)
        => new(week, TimeSpan.FromDays(week * 7), settlement, "", kind, count, cause, EventId: id);

    [Fact]
    public void TheSettlementThatLandedTheLastKillEarnsTheHero()
    {
        const long cell = 42;
        var events = new[]
        {
            Event(10, 3, DemoSettlementEventKind.MonsterSlain, DemoMonsters.EncodeKills(cell, 2), "擊退", 1),
            Event(10, 5, DemoSettlementEventKind.MonsterSlain, DemoMonsters.EncodeKills(cell, 1), "討伐", 2),
            Event(10, 9, DemoSettlementEventKind.MonsterSlain, DemoMonsters.EncodeKills(7, 4), "別的地下城", 3),
            Event(10, 3, DemoSettlementEventKind.DungeonCleared, cell, "灰燼洞窟的怪物被殺光，地下城沉寂下來【TEMP】", 4)
        };
        Assert.Equal(new[] { 5 }, DemoHeroes.Deeds(events).OrderBy(x => x));
        // 沒有擊殺紀錄時取事件記的鄰近聚落；沒有事件就沒有人成名。
        Assert.Equal(new[] { 3 }, DemoHeroes.Deeds(new[] { events[3] }).OrderBy(x => x));
        Assert.Empty(DemoHeroes.Deeds(null));
        Assert.Empty(DemoHeroes.Deeds(events.Take(3).ToList()));
        Assert.Equal("灰燼洞窟", DemoHeroes.DungeonOf(events[3]));
        Assert.Equal(DemoHeroes.ItemName("灰燼洞窟", 4), DemoHeroes.ItemName("灰燼洞窟", 4));
        Assert.StartsWith("灰燼洞窟之", DemoHeroes.ItemName("灰燼洞窟", 4), StringComparison.Ordinal);
        Assert.Equal(8, Enumerable.Range(0, 8).Select(i => DemoHeroes.ItemName("洞", i)).Distinct().Count());
        var slain = Assert.Single(DemoHeroes.DeedsOf(events));
        Assert.Equal((5, "帶隊殺光灰燼洞窟的怪物", DemoHeroes.ItemName("灰燼洞窟", 4)), (slain.SettlementId, slain.Text, slain.Item));
    }

    [Fact]
    public void VictoriesInWarAlsoMakeHeroes()
    {
        static DemoSettlementEvent Named(int settlement, string name, DemoSettlementEventKind kind, long count, string cause, long id)
            => new(20, TimeSpan.FromDays(140), settlement, name, kind, count, cause + "【TEMP】", EventId: id);
        string NameOf(int id) => id switch { 1 => "伯恩坡", 2 => "東塔林", 3 => "佩林谷", 4 => "歐文港", _ => "" };
        var events = new[]
        {
            // 打贏征服戰爭：攻方（1）成名，奪得對方的寶物；不戰而降不算。
            Named(2, "東塔林", DemoSettlementEventKind.Conquered, 1, "伯恩坡征服了東塔林與它的整個勢力（攻方死 5、守方死 8）", 11),
            Named(3, "佩林谷", DemoSettlementEventKind.Conquered, 1, "伯恩坡兵臨城下（兵力 50 對 5），佩林谷不戰而降，歸入伯恩坡", 12),
            // 打贏獨立戰爭：首領（3）成名。
            Named(3, "佩林谷", DemoSettlementEventKind.Independence, 4, "佩林谷在獨立戰爭中擊敗歐文港（叛軍死 2、上級死 3）", 13),
            Named(2, "東塔林", DemoSettlementEventKind.Independence, 1, "伯恩坡接受了東塔林的請願，讓它們獨立", 14),
            // 擊退來犯的大軍：守方（4）成名。擊潰叛軍：上級（4）已經有一件，不重複。
            Named(1, "伯恩坡", DemoSettlementEventKind.WarEnded, 4, "伯恩坡的大軍在歐文港城下敗退（攻方死 9、守方死 2）", 15),
            Named(2, "東塔林", DemoSettlementEventKind.SecessionFailed, 4, "東塔林的叛軍被歐文港擊潰（叛軍死 5、上級死 3）", 16),
            Named(1, "伯恩坡", DemoSettlementEventKind.WarEnded, 3, "征服佩林谷的戰爭打了一年不分勝負，伯恩坡與佩林谷白和平", 17)
        };
        var deeds = DemoHeroes.DeedsOf(events, NameOf);
        Assert.Equal(new[] { 1, 3, 4 }, deeds.Select(d => d.SettlementId));
        Assert.Equal(new[] { "率軍攻下東塔林", "率眾擊敗歐文港，為家鄉爭得獨立", "擊退伯恩坡來犯的大軍" }, deeds.Select(d => d.Text));
        Assert.Equal(new[] { DemoHeroes.ItemName("東塔林", 11), "", "" }, deeds.Select(d => d.Item));
        Assert.Equal(new[] { 1, 3, 4 }, DemoHeroes.Deeds(events).OrderBy(x => x));
    }

    [Fact]
    public void AHeroRaisesTheSoldiersLevelAndHasAName()
    {
        bool None(string _) => false;
        var plain = DemoCombat.Force(1000, DemoSettlementTier.Town, None);
        var led = DemoCombat.Force(1000, DemoSettlementTier.Town, None, hero: true);
        Assert.Equal(plain.SoldierLevel + 1, led.SoldierLevel);
        Assert.Equal(plain.Soldiers, led.Soldiers);
        // 沒有士兵的村子，有英雄也沒有戰鬥人員。
        Assert.Equal(DemoCombat.Force(300, DemoSettlementTier.Village, None), DemoCombat.Force(300, DemoSettlementTier.Village, None, hero: true));
        Assert.Equal("英雄", DemoRepresentative.RoleName(DemoRepresentativeRole.Hero));
        Assert.Contains("士兵", DemoPersonDetail.Duty(DemoRepresentativeRole.Hero), StringComparison.Ordinal);
    }

    [Fact]
    public void LegendsMatchTheHeroesOnRecord()
    {
        // 小地圖種子 1 跑 60 年：英雄（如果有）每一位都對得上一件傳說物品；沒有英雄時清單是空的。每個聚落同時最多一位在世的英雄。
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(1UL, null, DemoMapSize.Small), DemoWeeklyTempConfig.LivingForagingSample);
        for (var d = 0; d < 60 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        var heroes = world.PersonRecords.Where(p => p.Role == DemoRepresentativeRole.Hero).ToList();
        var legends = DemoHeroes.Legends(world);
        Assert.Equal(heroes.Select(h => h.Id).OrderBy(x => x), legends.Select(l => l.Hero.Id).OrderBy(x => x));
        Assert.All(legends, l => Assert.Equal(l.Hero.AppointedWeek - 1, l.Week));
        Assert.All(heroes.Where(h => h.InOffice).GroupBy(h => h.SettlementId), g => Assert.Single(g));
        Assert.All(heroes, h => Assert.Equal(DemoTraits.TempCount, DemoTraits.List(h.Traits).Count));
        var cast = new DemoSagaCast(world);
        Assert.Equal(legends.Count(l => l.Item.Length > 0), cast.Relics().Count);
        Assert.Equal(legends.Count, cast.Heroes(1, 60).Count);
    }
}
