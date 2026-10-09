namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// V0-05 一個 TEMP 示範年的摘要（全部聚落合計）。期初／期末值直接讀世界狀態，
/// 流量（死亡、生產、消耗、再生、採收、事件數）由該年各週已提交的週報加總，
/// 所以兩者能互相對帳：任何不一致都列在 <see cref="Problems"/>。
/// V0-06A 起人口與糧食的期初／期末都含「途中」遷徙隊伍（只計一次），死亡、消耗與缺糧也含途中隊伍；
/// 遷出／遷入是聚落之間的轉移，不改變世界總數。
/// </summary>
public sealed record DemoYearSummary(
    int Year,
    int LastDay,
    int Weeks,
    long PopulationOpening,
    long Births,
    long Deaths,
    long PopulationClosing,
    long FoodOpening,
    long FoodProduced,
    long FoodConsumed,
    long FoodShortage,
    long FoodClosing,
    long ReserveOpening,
    long Regenerated,
    long Harvested,
    long ReserveClosing,
    int HungerEvents,
    int StarvationEvents,
    long StarvationDeathsInEvents,
    int RecoveryEvents,
    int InhabitedSettlements,
    long DailyDemoHarvest,
    long Emigrants = 0,
    long Immigrants = 0,
    long PopulationInTransit = 0,
    int EmigrationEvents = 0,
    int ArrivalEvents = 0,
    int SettlementsFounded = 0,
    int SettlementsClosing = 0)
{
    /// <summary>本年所有帶原因的聚落事件數（飢餓＋餓死＋飢餓解除＋外移＋抵達＋建立新聚落）。</summary>
    public int SettlementEvents
        => HungerEvents + StarvationEvents + RecoveryEvents + EmigrationEvents + ArrivalEvents + SettlementsFounded;

    /// <summary>
    /// 年度守恆檢查（M1_RES、M1_POP、M1_NO_MAGIC_FOOD 在年尺度的對帳）。空清單＝零錯誤。
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (Weeks != DemoCenturyRun.TempWeeksPerYear)
            problems.Add($"第 {Year} 年週結算次數 {Weeks}，應為 {DemoCenturyRun.TempWeeksPerYear}");
        if (PopulationOpening + Births - Deaths != PopulationClosing)
            problems.Add($"第 {Year} 年人口帳不平衡：{PopulationOpening}＋{Births}－{Deaths}≠{PopulationClosing}");
        if (FoodOpening + FoodProduced - FoodConsumed != FoodClosing)
            problems.Add($"第 {Year} 年糧食帳不平衡：{FoodOpening}＋{FoodProduced}－{FoodConsumed}≠{FoodClosing}");
        if (ReserveOpening + Regenerated - Harvested != ReserveClosing)
            problems.Add($"第 {Year} 年資源存量帳不平衡：{ReserveOpening}＋{Regenerated}－{Harvested}≠{ReserveClosing}");
        if (FoodProduced != Harvested)
            problems.Add($"第 {Year} 年出現魔法糧食：生產 {FoodProduced}≠採收 {Harvested}");
        if (Deaths != StarvationDeathsInEvents)
            problems.Add($"第 {Year} 年死亡 {Deaths} 人，但帶原因的餓死事件只有 {StarvationDeathsInEvents} 人");
        if (PopulationInTransit > PopulationClosing)
            problems.Add($"第 {Year} 年途中人口 {PopulationInTransit} 超過世界人口 {PopulationClosing}");
        if (InhabitedSettlements > SettlementsClosing && SettlementsClosing > 0)
            problems.Add($"第 {Year} 年有人聚落 {InhabitedSettlements} 超過聚落總數 {SettlementsClosing}");
        if (PopulationClosing < 0 || FoodClosing < 0 || ReserveClosing < 0 || InhabitedSettlements < 0
            || Emigrants < 0 || Immigrants < 0 || PopulationInTransit < 0 || SettlementsFounded < 0)
            problems.Add($"第 {Year} 年出現負值");
        return problems;
    }

    /// <summary>玩家可讀的一行年度摘要（繁體中文）。</summary>
    public string Describe()
        => $"第 {Year} 年（至第 {LastDay} 日）｜人口 {PopulationClosing}（出生 {Births}、死亡 {Deaths}）"
         + $"｜有人聚落 {InhabitedSettlements}、新建聚落 {SettlementsFounded}"
         + $"｜遷出 {Emigrants}、遷入 {Immigrants}、年底途中 {PopulationInTransit}"
         + $"｜糧食 {FoodClosing}（生產 {FoodProduced}、消耗 {FoodConsumed}、缺糧 {FoodShortage}）"
         + $"｜資源存量 {ReserveClosing}"
         + $"｜事件 {SettlementEvents}（飢餓 {HungerEvents}、餓死 {StarvationEvents}、飢餓解除 {RecoveryEvents}"
         + $"、外移 {EmigrationEvents}、抵達 {ArrivalEvents}）"
         + $"｜示範每日收穫合計 {DailyDemoHarvest}";
}

/// <summary>一次長跑的結果：種子、各年摘要與全部守恆錯誤（空＝零錯誤）。</summary>
public sealed record DemoCenturyReport(
    ulong Seed,
    IReadOnlyList<DemoYearSummary> Years,
    IReadOnlyList<string> ConservationErrors);

/// <summary>
/// V0-05｜無畫面長跑（TEMP 示範）。用同一個 <see cref="DemoObserverWorld"/> 逐日原子推進，
/// 沿用 V0-04A／V0-04B 的每週結算與帳目檢查（週帳不平衡會在發布前直接丟出例外），
/// 另外在每年結束時以世界狀態的期初／期末值對帳一次。
/// 「一年＝52 週＝364 日」是 TEMP 暫定的示範曆法（正式曆法未定案），讓每年剛好包含整數個週結算。
/// 執行成本是線性的：每週只讀最新一筆週報，不重新掃描整段歷史。
/// </summary>
public static class DemoCenturyRun
{
    /// <summary>TEMP：每年的週結算次數（正式曆法未定案）。</summary>
    public const int TempWeeksPerYear = 52;

    /// <summary>TEMP：每年的世界日數。</summary>
    public const int TempDaysPerYear = TempWeeksPerYear * 7;

    /// <summary>長跑年數上限（避免誤輸入造成過長執行）。</summary>
    public const int MaxYears = 1000;

    public static DemoCenturyReport Run(
        ulong seed,
        int years,
        DemoWeeklyTempConfig? config = null,
        Action<DemoYearSummary>? onYear = null)
    {
        if (years is < 1 or > MaxYears)
            throw new ArgumentOutOfRangeException(nameof(years), $"Years must be between 1 and {MaxYears}.");

        var world = new DemoObserverWorld(DemoScenario.Sample(seed), config ?? DemoWeeklyTempConfig.Sample);
        // V0-06A：世界總數＝各聚落＋途中隊伍（只計一次）。
        // V0-06B：聚落與資源點會因開拓而增加，所以每次都讀目前的 Sites；新聚落建立前不存在，期初視為 0
        // （它的期初人口／糧食／存量本來就是 0，全部由開拓隊帶入，M1_SETTLE）。
        long TotalPopulation() => world.Sites.Settlements.Sum(s => world.SettlementPopulation(s.Id)) + world.PopulationInTransit;
        long TotalFood() => world.Sites.Settlements.Sum(s => world.SettlementFood(s.Id)) + world.FoodInTransit;
        long TotalReserve() => world.Sites.ResourcePoints.Sum(p => world.ResourceReserve(p.Id));

        var summaries = new List<DemoYearSummary>(years);
        var errors = new List<string>();
        var weeksSeen = 0;

        for (var year = 1; year <= years; year++)
        {
            var popOpening = TotalPopulation();
            var foodOpening = TotalFood();
            var reserveOpening = TotalReserve();
            int weeks = 0, hungerEvents = 0, starvationEvents = 0, recoveryEvents = 0;
            int emigrationEvents = 0, arrivalEvents = 0, foundedEvents = 0;
            long emigrants = 0, immigrants = 0;
            long births = 0, deaths = 0, produced = 0, consumed = 0, shortage = 0;
            long regenerated = 0, harvested = 0, deathsInEvents = 0, dailyHarvest = 0;
            var lastDay = 0;

            for (var d = 0; d < TempDaysPerYear; d++)
            {
                var day = world.AdvanceOneDay();
                lastDay = day.Day;
                dailyHarvest = checked(dailyHarvest + day.Harvested);

                var latest = world.LatestWeek;
                if (latest is null || latest.Week == weeksSeen) continue;
                if (latest.Week != weeksSeen + 1)
                    errors.Add($"第 {year} 年週報編號跳號：{weeksSeen}→{latest.Week}");
                weeksSeen = latest.Week;
                weeks++;

                foreach (var l in latest.Settlements)
                {
                    births = checked(births + l.Births);
                    deaths = checked(deaths + l.Deaths);
                    produced = checked(produced + l.FoodProduced);
                    consumed = checked(consumed + l.FoodConsumed);
                    shortage = checked(shortage + l.FoodShortage);
                    regenerated = checked(regenerated + l.Regenerated);
                    harvested = checked(harvested + l.Harvested);
                    emigrants = checked(emigrants + l.Emigrants);
                    immigrants = checked(immigrants + l.Immigrants);
                }

                foreach (var m in latest.Migrations)
                {
                    deaths = checked(deaths + m.Deaths);
                    consumed = checked(consumed + m.FoodConsumed);
                    shortage = checked(shortage + m.FoodShortage);
                }

                foreach (var e in latest.Events)
                {
                    switch (e.Kind)
                    {
                        case DemoSettlementEventKind.Hunger:
                            hungerEvents++;
                            break;
                        case DemoSettlementEventKind.StarvationDeath:
                            starvationEvents++;
                            deathsInEvents = checked(deathsInEvents + e.Count);
                            break;
                        case DemoSettlementEventKind.HungerRecovered:
                            recoveryEvents++;
                            break;
                        case DemoSettlementEventKind.MigrantStarvationDeath:
                            starvationEvents++;
                            deathsInEvents = checked(deathsInEvents + e.Count);
                            break;
                        case DemoSettlementEventKind.Emigration:
                            emigrationEvents++;
                            break;
                        case DemoSettlementEventKind.Arrival:
                            arrivalEvents++;
                            break;
                        case DemoSettlementEventKind.SettlementFounded:
                            foundedEvents++;
                            break;
                    }
                }
            }

            var summary = new DemoYearSummary(
                year, lastDay, weeks,
                popOpening, births, deaths, TotalPopulation(),
                foodOpening, produced, consumed, shortage, TotalFood(),
                reserveOpening, regenerated, harvested, TotalReserve(),
                hungerEvents, starvationEvents, deathsInEvents, recoveryEvents,
                world.Sites.Settlements.Count(s => world.SettlementPopulation(s.Id) > 0),
                dailyHarvest,
                emigrants, immigrants, world.PopulationInTransit,
                emigrationEvents, arrivalEvents, foundedEvents, world.Sites.Settlements.Count);
            errors.AddRange(summary.Problems());
            summaries.Add(summary);
            onYear?.Invoke(summary);
        }

        return new DemoCenturyReport(seed, summaries.AsReadOnly(), errors.AsReadOnly());
    }
}
