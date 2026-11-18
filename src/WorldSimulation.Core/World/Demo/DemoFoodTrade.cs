namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-23 章【TEMP】糧食貿易（使用者 2026-11-18：文明要靠貿易、賦稅取得額外的糧食；理想的情況是一個小鎮主要的糧食由多個村莊提供、販賣）。
/// <list type="bullet">
/// <item>市集：城鎮以上、會以物易物或貨幣的聚落（由呼叫端判斷）。</item>
/// <item>每週結算的最後（糧稅之後），會以物易物、不是市集的聚落，把期末存糧超過留底（<see cref="DemoFoodTax.TempKeepWeeks"/> 週需求）
///   的部分賣 <see cref="TempSellPercent"/>% 給 <see cref="TempTradeReach"/> 格內最近的市集（同距離取人多的、再取編號小的）；
///   最多賣到本週的來源餘量為止（只賣自己的地多產的，不吃掉自己長大的空間）。</item>
/// <item>市集存糧滿 <see cref="TempMarketStockWeeks"/> 週需求就不再買（買到滿為止）。</item>
/// <item>只是移轉：全世界的糧食總量不變，週帳多「賣出」「買進」兩欄。賣了糧的村莊下週照 08-1 的補存糧多採收，等於替市集種田；
///   市集收到的糧算進下週的來源餘量（<see cref="DemoCrowding.EffectiveSlack"/>）：可以生育、不必分家。</item>
/// </list>
/// </summary>
public static class DemoFoodTrade
{
    /// <summary>以物易物、貨幣（市集要會其中一項；賣糧的村莊要會以物易物）。</summary>
    public const string BarterTech = "TECH_BARTER", CurrencyTech = "TECH_CURRENCY";

    /// <summary>【TEMP】村莊把糧賣到幾格內的市集。</summary>
    public const int TempTradeReach = 6;

    /// <summary>【TEMP】每週賣掉超過留底的存糧的幾 %。</summary>
    public const long TempSellPercent = 50;

    /// <summary>【TEMP】市集存糧滿幾週需求就不再買。</summary>
    public const long TempMarketStockWeeks = 6;

    /// <summary>這一週要賣的糧（無條件捨去；留底同糧稅）。</summary>
    public static long Offer(long foodClosing, long populationClosing, long foodPerPersonPerWeek)
    {
        var keep = checked(populationClosing * foodPerPersonPerWeek * DemoFoodTax.TempKeepWeeks);
        return foodClosing <= keep ? 0 : checked(foodClosing - keep) * TempSellPercent / 100;
    }

    /// <summary>依期末存糧買賣：改寫週帳（賣出、買進、期末存糧）與下週的期初存糧。</summary>
    public static void Trade(List<DemoSettlementWeekLedger> ledgers, IDictionary<int, long> nextFood, DemoWorldSites sites,
        Func<int, bool> isMarket, Func<int, bool> trades, long foodPerPersonPerWeek)
    {
        ArgumentNullException.ThrowIfNull(ledgers);
        ArgumentNullException.ThrowIfNull(nextFood);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(isMarket);
        ArgumentNullException.ThrowIfNull(trades);
        var index = new Dictionary<int, int>();
        for (var i = 0; i < ledgers.Count; i++)
            index[ledgers[i].SettlementId] = i;
        var place = sites.Settlements.Where(s => !s.IsRuin && index.ContainsKey(s.Id)).ToDictionary(s => s.Id, s => new DemoCellRef(s.Column, s.Row));
        var markets = ledgers.Where(l => l.PopulationClosing > 0 && place.ContainsKey(l.SettlementId) && isMarket(l.SettlementId))
            .Select(l => l.SettlementId).OrderBy(id => id).ToList();
        if (markets.Count == 0)
            return;
        var room = markets.ToDictionary(id => id, id =>
            Math.Max(0, checked(ledgers[index[id]].PopulationClosing * foodPerPersonPerWeek * TempMarketStockWeeks) - ledgers[index[id]].FoodClosing));
        var marketSet = markets.ToHashSet();
        var sellers = ledgers.Where(l => l.PopulationClosing > 0 && place.ContainsKey(l.SettlementId) && !marketSet.Contains(l.SettlementId)
                && trades(l.SettlementId))
            .Select(l => l.SettlementId).OrderBy(id => id).ToList();
        foreach (var seller in sellers)
        {
            var si = index[seller];
            var offer = Math.Min(Offer(ledgers[si].FoodClosing, ledgers[si].PopulationClosing, foodPerPersonPerWeek), Math.Max(0, ledgers[si].SourceSlack));
            if (offer <= 0)
                continue;
            var here = place[seller];
            var buyer = markets.Where(m => room[m] > 0)
                .Select(m => (Id: m, Distance: DemoHexWorld.Distance(here, place[m])))
                .Where(x => x.Distance <= TempTradeReach)
                .OrderBy(x => x.Distance).ThenByDescending(x => ledgers[index[x.Id]].PopulationClosing).ThenBy(x => x.Id)
                .Select(x => (int?)x.Id).FirstOrDefault();
            if (buyer is not { } b)
                continue;
            var food = Math.Min(offer, room[b]);
            room[b] -= food;
            ledgers[si] = ledgers[si] with { FoodTradeOut = checked(ledgers[si].FoodTradeOut + food), FoodClosing = ledgers[si].FoodClosing - food };
            nextFood[seller] = ledgers[si].FoodClosing;
            var bi = index[b];
            ledgers[bi] = ledgers[bi] with { FoodTradeIn = checked(ledgers[bi].FoodTradeIn + food), FoodClosing = checked(ledgers[bi].FoodClosing + food) };
            nextFood[b] = ledgers[bi].FoodClosing;
        }
    }

    /// <summary>
    /// 第 08-23 章：最近一週的商路（賣糧的聚落 → 它最近、那一週有買糧的市集；地圖畫商路用）。
    /// 只依同一條規則（6 格內最近、同距離取人多的）重算，不另存；遺跡與沒人的聚落不列。
    /// </summary>
    public static IReadOnlyList<(DemoCellRef From, DemoCellRef To)> Links(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var reports = world.WeekReports();
        if (reports.Count == 0)
            return Array.Empty<(DemoCellRef, DemoCellRef)>();
        var last = reports[reports.Count - 1];
        var byId = world.Sites.Settlements.Where(s => !s.IsRuin).ToDictionary(s => s.Id);
        var markets = last.Settlements.Where(l => l.FoodTradeIn > 0 && byId.ContainsKey(l.SettlementId))
            .Select(l => (Site: byId[l.SettlementId], People: l.PopulationClosing)).ToList();
        if (markets.Count == 0)
            return Array.Empty<(DemoCellRef, DemoCellRef)>();
        var links = new List<(DemoCellRef, DemoCellRef)>();
        foreach (var seller in last.Settlements.Where(l => l.FoodTradeOut > 0 && byId.ContainsKey(l.SettlementId)).OrderBy(l => l.SettlementId))
        {
            var from = new DemoCellRef(byId[seller.SettlementId].Column, byId[seller.SettlementId].Row);
            var market = markets
                .Select(x => (x.Site, x.People, Distance: DemoHexWorld.Distance(from, new DemoCellRef(x.Site.Column, x.Site.Row))))
                .Where(x => x.Distance <= TempTradeReach)
                .OrderBy(x => x.Distance).ThenByDescending(x => x.People).ThenBy(x => x.Site.Id)
                .Select(x => x.Site).FirstOrDefault();
            if (market is not null)
                links.Add((from, new DemoCellRef(market.Column, market.Row)));
        }
        return links;
    }
}
