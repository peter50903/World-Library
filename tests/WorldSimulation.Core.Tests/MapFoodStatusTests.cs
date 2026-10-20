using WorldSimulation.Core.World.Demo;
using WorldSimulation.GodotClient;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 05 章 b 節：「糧食狀況」地圖模式分級（只讀 Core 週帳）。</summary>
public sealed class MapFoodStatusTests
{
    private static DemoSettlementWeekLedger Ledger(int id, long closing, long consumed, long shortage = 0, long debt = 0)
        => new(1, id, "測試", 0, 0, 0, 0, 0, consumed, consumed, closing, shortage, 5, 0, 0, 5, HungerDebtClosing: debt);

    [Fact]
    public void Classify_UsesShortageDebtAndTempReserveWeeks()
    {
        Assert.Equal(FoodStatus.Unsettled, MapFoodStatus.Classify(null));
        Assert.Equal(FoodStatus.Starving, MapFoodStatus.Classify(Ledger(1, 100, 20, shortage: 1)));
        Assert.Equal(FoodStatus.Starving, MapFoodStatus.Classify(Ledger(1, 100, 20, debt: 3)));
        Assert.Equal(FoodStatus.Tight, MapFoodStatus.Classify(Ledger(1, 20 * MapFoodStatus.TempTightWeeks - 1, 20)));
        Assert.Equal(FoodStatus.Ample, MapFoodStatus.Classify(Ledger(1, 20 * MapFoodStatus.TempTightWeeks, 20)));
        Assert.Equal(FoodStatus.Ample, MapFoodStatus.Classify(Ledger(1, 0, 0)));
    }

    [Fact]
    public void ForWeek_MapsEverySettlementId_AndRealWorldWeeksClassify()
    {
        var map = MapFoodStatus.ForWeek(new[] { Ledger(3, 0, 10, shortage: 4), Ledger(7, 500, 10) });
        Assert.Equal(FoodStatus.Starving, map[3]);
        Assert.Equal(FoodStatus.Ample, map[7]);
        Assert.Empty(MapFoodStatus.ForWeek(null));

        var world = new WorldSimulation.Core.World.DemoObserverWorld(WorldSimulation.Core.World.DemoScenario.Sample(42));
        for (var i = 0; i < 14; i++)
            world.AdvanceOneDay();
        var statuses = MapFoodStatus.ForWeek(world.LatestWeek!.Settlements);
        Assert.Equal(world.LatestWeek.Settlements.Count, statuses.Count);
        Assert.DoesNotContain(FoodStatus.Unsettled, statuses.Values);
        Assert.Equal("飢餓", MapFoodStatus.Label(FoodStatus.Starving));
    }
}
