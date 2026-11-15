using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-19g 章：下級把超過留底的存糧繳一成給直屬上級；糧食只移轉、帳平衡。</summary>
public sealed class DemoFoodTaxTests
{
    private static DemoSettlementWeekLedger Ledger(int id, long food, long people)
        => new(1, id, $"#{id}", 0, 0, 0, 0, food, 0, 0, food, 0, people, 0, 0, people);

    [Fact]
    public void Due_KeepsEightWeeksForThePeople()
    {
        // 10 人 × 4 × 8 週＝留底 320；超過的 1000 繳一成。
        Assert.Equal(0, DemoFoodTax.Due(320, 10, 4));
        Assert.Equal(100, DemoFoodTax.Due(1320, 10, 4));
    }

    [Fact]
    public void Collect_MovesFoodToTheLiege_AndKeepsLedgersBalanced()
    {
        var ledgers = new List<DemoSettlementWeekLedger> { Ledger(1, 500, 20), Ledger(2, 1320, 10), Ledger(3, 2000, 0) };
        var next = new Dictionary<int, long> { [1] = 500, [2] = 1320, [3] = 2000 };
        // 2 隸屬 1；3 隸屬 1 但 1 有人、3 沒人留底 0 → 3 繳 200。
        DemoFoodTax.Collect(ledgers, next, id => id is 2 or 3 ? 1 : null, 4);
        Assert.Equal(100, ledgers[1].FoodTaxPaid);
        Assert.Equal(200, ledgers[2].FoodTaxPaid);
        Assert.Equal(300, ledgers[0].FoodTaxReceived);
        Assert.Equal(800, next[1]);
        Assert.Equal(1220, next[2]);
        Assert.All(ledgers, l => Assert.True(l.FoodBalanced && l.TotalResourceBalanced && l.NonNegative));
        Assert.Equal(3820, next.Values.Sum());
    }

    [Fact]
    public void Collect_SkipsEmptyLiege()
    {
        var ledgers = new List<DemoSettlementWeekLedger> { Ledger(1, 0, 0), Ledger(2, 5000, 1) };
        var next = new Dictionary<int, long> { [1] = 0, [2] = 5000 };
        DemoFoodTax.Collect(ledgers, next, id => id == 2 ? 1 : null, 4);
        Assert.Equal(0, ledgers[1].FoodTaxPaid);
        Assert.Equal(5000, next[2]);
    }
}
