using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-23 章：村莊把超過留底的存糧賣一半給 6 格內最近的市集；糧食只移轉、帳平衡；市集存糧滿了不買。</summary>
public sealed class DemoFoodTradeTests
{
    private static DemoSettlementWeekLedger Ledger(int id, long food, long people, long slack = 1000)
        => new(1, id, $"#{id}", 0, 0, 0, 0, food, 0, 0, food, 0, people, 0, 0, people, SourceSlack: slack);

    [Fact]
    public void Offer_SellsHalfOfWhatIsAboveTwoWeeks()
    {
        // 10 人 × 4 × 2 週＝留底 80。
        Assert.Equal(0, DemoFoodTrade.Offer(80, 10, 4));
        Assert.Equal(60, DemoFoodTrade.Offer(200, 10, 4));
    }

    private static (DemoWorldSites Sites, int Market, int Near1, int Near2, int Far) Place()
    {
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(42UL, null, DemoMapSize.Medium), DemoWeeklyTempConfig.ForagingSample); // 中地圖：找得到 6 格外的空地
        var sites = world.Sites;
        var market = sites.Settlements[0];
        var home = new DemoCellRef(market.Column, market.Row);
        var land = world.Map.Cells.Where(c => c.Terrain != DemoTerrain.Water && sites.SettlementAt(c.Column, c.Row) is null)
            .OrderBy(c => c.Row).ThenBy(c => c.Column).ToList();
        int D(DemoHexCell c) => DemoHexWorld.Distance(new DemoCellRef(c.Column, c.Row), home);
        var cells = land.Where(c => D(c) is >= 2 and <= 4).Take(2)
            .Append(land.First(c => D(c) > DemoFoodTrade.TempTradeReach)).ToList();
        var id = checked((int)sites.EntityIds().Max() + 1);
        var ids = new List<int>();
        foreach (var c in cells)
        {
            sites = sites.WithFoundedSettlement(new DemoSettlement(id, $"#{id}", c.Column, c.Row, FoundedWeek: 5,
                FoundedFromSettlementId: market.Id, FoundedByBatchId: id), id + 1, world.Map);
            ids.Add(id);
            id += 2;
        }
        return (sites, market.Id, ids[0], ids[1], ids[2]);
    }

    [Fact]
    public void VillagesSellToTheNearestMarketAndLedgersStayBalanced()
    {
        var (sites, market, near1, near2, far) = Place();
        var ledgers = new List<DemoSettlementWeekLedger> { Ledger(market, 100, 50), Ledger(near1, 280, 10), Ledger(near2, 180, 10), Ledger(far, 1000, 10) };
        var next = ledgers.ToDictionary(l => l.SettlementId, l => l.FoodClosing);
        DemoFoodTrade.Trade(ledgers, next, sites, id => id == market, _ => true, 4);
        Assert.Equal(100, ledgers[1].FoodTradeOut); // (280 − 80) × 50%
        Assert.Equal(50, ledgers[2].FoodTradeOut);  // (180 − 80) × 50%
        Assert.Equal(0, ledgers[3].FoodTradeOut);   // 離市集太遠
        Assert.Equal(150, ledgers[0].FoodTradeIn);
        Assert.Equal((250L, 180L, 130L, 1000L), (next[market], next[near1], next[near2], next[far]));
        Assert.All(ledgers, l => Assert.True(l.FoodBalanced && l.TotalResourceBalanced && l.NonNegative));
        Assert.Equal(1560, next.Values.Sum());
    }

    [Fact]
    public void AFullMarketStopsBuyingAndNonTradersKeepTheirFood()
    {
        var (sites, market, near1, near2, _) = Place();
        // 市集 50 人 × 4 × 6 週＝1200：已經滿了就不買。
        var full = new List<DemoSettlementWeekLedger> { Ledger(market, 1200, 50), Ledger(near1, 280, 10) };
        var next = full.ToDictionary(l => l.SettlementId, l => l.FoodClosing);
        DemoFoodTrade.Trade(full, next, sites, id => id == market, _ => true, 4);
        Assert.Equal(0, full[1].FoodTradeOut);
        // 只差 30 就滿：只買 30。
        var almost = new List<DemoSettlementWeekLedger> { Ledger(market, 1170, 50), Ledger(near1, 280, 10) };
        next = almost.ToDictionary(l => l.SettlementId, l => l.FoodClosing);
        DemoFoodTrade.Trade(almost, next, sites, id => id == market, _ => true, 4);
        Assert.Equal((30L, 30L), (almost[1].FoodTradeOut, almost[0].FoodTradeIn));
        // 只賣自己的地多產的：來源餘量只有 30 就最多賣 30；餘量是負的（正在吃老本）就不賣。
        var tight = new List<DemoSettlementWeekLedger> { Ledger(market, 100, 50), Ledger(near1, 280, 10, slack: 30), Ledger(near2, 280, 10, slack: -5) };
        next = tight.ToDictionary(l => l.SettlementId, l => l.FoodClosing);
        DemoFoodTrade.Trade(tight, next, sites, id => id == market, _ => true, 4);
        Assert.Equal((30L, 0L), (tight[1].FoodTradeOut, tight[2].FoodTradeOut));
        // 不會以物易物的村莊不賣。
        var none = new List<DemoSettlementWeekLedger> { Ledger(market, 100, 50), Ledger(near2, 280, 10) };
        next = none.ToDictionary(l => l.SettlementId, l => l.FoodClosing);
        DemoFoodTrade.Trade(none, next, sites, id => id == market, id => id != near2, 4);
        Assert.Equal(0, none[1].FoodTradeOut);
    }

    [Fact]
    public void ImportsCountAsRoomOnlyAfterWhatTheStoresAlreadyFed()
    {
        // 上週收入 100；本週吃掉的存糧（消耗超過採收）20 → 還能多養 80；沒有吃存糧就是 100；吃得比收入多就是 0。
        Assert.Equal(80, DemoCrowding.ImportRoom(100, 80, 60));
        Assert.Equal(100, DemoCrowding.ImportRoom(100, 50, 60));
        Assert.Equal(0, DemoCrowding.ImportRoom(10, 100, 50));
        Assert.Equal(0, DemoCrowding.ImportRoom(0, 0, 0));
    }
}
