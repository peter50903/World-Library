namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// V0-05 一個 TEMP 示範年的摘要（全部聚落合計）。期初／期末值直接讀世界狀態，
/// 流量（死亡、生產、消耗、再生、採收、事件數）由該年各週已提交的週報加總，
/// 所以兩者能互相對帳：任何不一致都列在 <see cref="Problems"/>。
/// V0-06A 起人口與糧食的期初／期末都含「途中」遷徙隊伍（只計一次），死亡、消耗與缺糧也含途中隊伍；
/// 遷出／遷入是聚落之間的轉移，不改變世界總數。
/// C02：死亡按原因分類（Notion 02 §E）——<see cref="NaturalDeaths"/> 是年齡／自然死亡人數，
/// 其餘死亡（Deaths－NaturalDeaths）是餓死（含途中）；出生與自然死亡各自對帶原因事件的人數
/// （<see cref="BirthEvents"/>、<see cref="NaturalDeathEvents"/>，為事件 Count 合計）。
/// 三個新欄位是選用參數（預設 0），不破壞既有呼叫。
/// 第 08 章 b 節：加 <see cref="FoodSpoiled"/>（第 06 章 d 節腐敗）；途中隊伍的出生與自然死亡也計入（第 07 章 b 節）。
/// 同一份年帳也由 <see cref="DemoYearLedger.FromWeeks"/> 只從週報彙整（觀察者年度圖表），兩者逐欄相同。
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
    long Emigrants = 0,
    long Immigrants = 0,
    long PopulationInTransit = 0,
    int EmigrationEvents = 0,
    int ArrivalEvents = 0,
    int SettlementsFounded = 0,
    int SettlementsClosing = 0,
    int FirstDay = 1,
    long NaturalDeaths = 0,
    long BirthEvents = 0,
    long NaturalDeathEvents = 0,
    long FoodSpoiled = 0,
    long FoodFromOther = 0)
{
    /// <summary>本年餓死人數（聚落＋途中）＝全部死亡－自然死亡。</summary>
    public long StarvationDeaths => Deaths - NaturalDeaths;

    /// <summary>
    /// #82：本年（第 <see cref="FirstDay"/>～<see cref="LastDay"/> 日）應有的週結算次數＝其間 7 的倍數日數。
    /// 週結算每 7 個世界日一次、不因年界重置，所以 365 日年會有 52 或 53 次。
    /// </summary>
    public int ExpectedWeeks => DemoCenturyRun.WeeklySettlementsThrough(LastDay) - DemoCenturyRun.WeeklySettlementsThrough(FirstDay - 1);

    /// <summary>本年所有帶原因的聚落事件數（飢餓＋餓死＋飢餓解除＋外移＋抵達＋建立新聚落）。</summary>
    public int SettlementEvents
        => HungerEvents + StarvationEvents + RecoveryEvents + EmigrationEvents + ArrivalEvents + SettlementsFounded;

    /// <summary>
    /// 年度守恆檢查（M1_RES、M1_POP、M1_NO_MAGIC_FOOD 在年尺度的對帳）。空清單＝零錯誤。
    /// </summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (FirstDay < 1 || LastDay < FirstDay)
            problems.Add($"第 {Year} 年日期範圍錯誤：第 {FirstDay}～{LastDay} 日");
        else if (Weeks != ExpectedWeeks)
            problems.Add($"第 {Year} 年週結算次數 {Weeks}，第 {FirstDay}～{LastDay} 日應為 {ExpectedWeeks}");
        if (PopulationOpening + Births - Deaths != PopulationClosing)
            problems.Add($"第 {Year} 年人口帳不平衡：{PopulationOpening}＋{Births}－{Deaths}≠{PopulationClosing}");
        // 第 08-9 章 B3b、C1：死者化為的糧食與獵得的糧食（都有真實事件為證）。
        if (FoodOpening + FoodProduced + FoodFromOther - FoodConsumed - FoodSpoiled != FoodClosing)
            problems.Add($"第 {Year} 年糧食帳不平衡：{FoodOpening}＋{FoodProduced}＋其他 {FoodFromOther}－{FoodConsumed}－腐敗 {FoodSpoiled}≠{FoodClosing}");
        if (ReserveOpening + Regenerated - Harvested != ReserveClosing)
            problems.Add($"第 {Year} 年資源存量帳不平衡：{ReserveOpening}＋{Regenerated}－{Harvested}≠{ReserveClosing}");
        if (FoodProduced != Harvested)
            problems.Add($"第 {Year} 年出現魔法糧食：生產 {FoodProduced}≠採收 {Harvested}");
        if (StarvationDeaths != StarvationDeathsInEvents)
            problems.Add($"第 {Year} 年餓死 {StarvationDeaths} 人（死亡 {Deaths}－自然死亡 {NaturalDeaths}），但帶原因的餓死事件有 {StarvationDeathsInEvents} 人");
        if (NaturalDeaths != NaturalDeathEvents)
            problems.Add($"第 {Year} 年自然死亡 {NaturalDeaths} 人，但帶原因的自然死亡事件有 {NaturalDeathEvents} 人");
        if (Births != BirthEvents)
            problems.Add($"第 {Year} 年出生 {Births} 人，但帶原因的出生事件有 {BirthEvents} 人（出生無事件來源）");
        if (NaturalDeaths < 0 || NaturalDeaths > Deaths || Births < 0)
            problems.Add($"第 {Year} 年出生／自然死亡人數不合理：出生 {Births}、死亡 {Deaths}、自然死亡 {NaturalDeaths}");
        if (PopulationInTransit > PopulationClosing)
            problems.Add($"第 {Year} 年途中人口 {PopulationInTransit} 超過世界人口 {PopulationClosing}");
        if (InhabitedSettlements > SettlementsClosing && SettlementsClosing > 0)
            problems.Add($"第 {Year} 年有人聚落 {InhabitedSettlements} 超過聚落總數 {SettlementsClosing}");
        if (PopulationClosing < 0 || FoodClosing < 0 || ReserveClosing < 0 || InhabitedSettlements < 0
            || Emigrants < 0 || Immigrants < 0 || PopulationInTransit < 0 || SettlementsFounded < 0 || FoodSpoiled < 0)
            problems.Add($"第 {Year} 年出現負值");
        return problems;
    }

    /// <summary>玩家可讀的一行年度摘要（繁體中文）。</summary>
    public string Describe()
        => $"第 {Year} 年（至第 {LastDay} 日）｜週結算 {Weeks} 次｜人口 {PopulationClosing}（出生 {Births}、死亡 {Deaths}（餓死 {StarvationDeaths}、自然死亡 {NaturalDeaths}））"
         + $"｜有人聚落 {InhabitedSettlements}、新建聚落 {SettlementsFounded}"
         + $"｜遷出 {Emigrants}、遷入 {Immigrants}、年底途中 {PopulationInTransit}"
         + $"｜糧食 {FoodClosing}（生產 {FoodProduced}、消耗 {FoodConsumed}、缺糧 {FoodShortage}）"
         + $"｜資源存量 {ReserveClosing}"
         + $"｜事件 {SettlementEvents}（飢餓 {HungerEvents}、餓死 {StarvationEvents}、飢餓解除 {RecoveryEvents}"
         + $"、外移 {EmigrationEvents}、抵達 {ArrivalEvents}）";
}

/// <summary>第 08 章 c 節：一條不變量在一次長跑中的檢查次數與違反次數（每年年底檢查一次）。</summary>
public sealed record DemoInvariantTally(string Id, string Name, int Checks, int Failures, string? FirstProblem)
{
    public string Describe()
        => Failures == 0
            ? $"{Id} {Name} 通過 {Checks}／{Checks} 次"
            : $"{Id} {Name} 失敗 {Failures}／{Checks} 次（第一處：{FirstProblem}）";
}

/// <summary>一次長跑的結果：種子、各年摘要與全部守恆錯誤（空＝零錯誤）；第 08 章 c 節起含不變量統計。</summary>
public sealed record DemoCenturyReport(
    ulong Seed,
    IReadOnlyList<DemoYearSummary> Years,
    IReadOnlyList<string> ConservationErrors,
    IReadOnlyList<DemoInvariantTally>? Invariants = null);

/// <summary>
/// V0-05｜無畫面長跑（TEMP 示範）。用同一個 <see cref="DemoObserverWorld"/> 逐日原子推進，
/// 沿用 V0-04A／V0-04B 的每週結算與帳目檢查（週帳不平衡會在發布前直接丟出例外），
/// 另外在每年結束時以世界狀態的期初／期末值對帳一次。
/// #82：一年的日數由呼叫端傳入（文字觀察者從正式設定 config/sheets/01_世界參數.csv 的
/// WORLD_DAYS_PER_YEAR 讀取，目前 365），Core 不自己寫死年長、也不讀檔。
/// 週結算仍每 7 個世界日一次、跨年不重置；每次週結算歸屬於「結算日所在的年份」
/// （例如 365 日年：第 364 日屬第 1 年、第 371 日屬第 2 年），所以每年有 52 或 53 次。
/// 年度期初／期末讀年界當日的世界狀態；週結算以外的日子不改聚落、資源點或隊伍狀態，所以能與週報流量對帳。
/// 執行成本是線性的：每週只讀最新一筆週報，不重新掃描整段歷史。
/// #103：可另外在每年年底取得各聚落的 TEMP 承載估計（<see cref="DemoSettlementCapacity"/>），
/// 用來說明哪些聚落長期可活、哪些長期缺糧；只讀世界狀態，不改年度摘要與守恆檢查。
/// </summary>
public static class DemoCenturyRun
{
    /// <summary>示範週結算間隔（世界日；與 DemoObserverWorld 的每週排程一致）。</summary>
    public const int DaysPerWeeklySettlement = 7;

    /// <summary>長跑年數上限（避免誤輸入造成過長執行）。</summary>
    public const int MaxYears = 1000;

    /// <summary>第 1～<paramref name="day"/> 日之間發生的週結算次數（第 7、14、21……日各一次）。</summary>
    public static int WeeklySettlementsThrough(int day) => day <= 0 ? 0 : day / DaysPerWeeklySettlement;

    public static DemoCenturyReport Run(
        ulong seed,
        int years,
        int daysPerYear,
        DemoWeeklyTempConfig? config = null,
        Action<DemoYearSummary>? onYear = null,
        Action<DemoYearSummary, IReadOnlyList<DemoSettlementCapacity>>? onYearSettlements = null)
    {
        if (years is < 1 or > MaxYears)
            throw new ArgumentOutOfRangeException(nameof(years), $"Years must be between 1 and {MaxYears}.");
        if (daysPerYear < 1 || (long)daysPerYear * years > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(daysPerYear), "Days per year must be positive and fit the run length.");

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
        // 第 08 章 c 節：每年年底以註冊表檢查全部不變量（增量：只看新週報，並確認舊帳未被改寫）。
        DemoInvariantCheckpoint? checkpoint = null;
        var tallies = DemoInvariants.All.ToDictionary(i => i.Id, _ => (Checks: 0, Failures: 0, First: (string?)null));

        for (var year = 1; year <= years; year++)
        {
            var popOpening = TotalPopulation();
            var foodOpening = TotalFood();
            var reserveOpening = TotalReserve();
            int weeks = 0, hungerEvents = 0, starvationEvents = 0, recoveryEvents = 0;
            int emigrationEvents = 0, arrivalEvents = 0, foundedEvents = 0;
            long emigrants = 0, immigrants = 0;
            long births = 0, deaths = 0, produced = 0, consumed = 0, shortage = 0;
            long regenerated = 0, harvested = 0, deathsInEvents = 0;
            long naturalDeaths = 0, birthsInEvents = 0, naturalDeathsInEvents = 0, spoiled = 0, other = 0;
            var firstDay = checked((year - 1) * daysPerYear + 1);
            var lastDay = 0;

            for (var d = 0; d < daysPerYear; d++)
            {
                var day = world.AdvanceOneDay();
                lastDay = day.Day;

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
                    naturalDeaths = checked(naturalDeaths + l.NaturalDeaths);
                    produced = checked(produced + l.FoodProduced);
                    consumed = checked(consumed + l.FoodConsumed);
                    shortage = checked(shortage + l.FoodShortage);
                    spoiled = checked(spoiled + l.FoodSpoiled);
                    other = checked(other + l.FoodFromDead + l.FoodFromHunting);
                    regenerated = checked(regenerated + l.Regenerated);
                    harvested = checked(harvested + l.Harvested);
                    emigrants = checked(emigrants + l.Emigrants);
                    immigrants = checked(immigrants + l.Immigrants);
                }

                foreach (var m in latest.Migrations)
                {
                    // 第 07 章 b 節：途中出生與自然死亡也是世界人口的增減。
                    births = checked(births + m.Births);
                    deaths = checked(deaths + m.Deaths + m.NaturalDeaths);
                    naturalDeaths = checked(naturalDeaths + m.NaturalDeaths);
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
                        case DemoSettlementEventKind.MigrantKilledByBeasts: // 第 08-9 章 C2：非自然死亡，一起對帳
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
                        case DemoSettlementEventKind.Birth:
                            birthsInEvents = checked(birthsInEvents + e.Count);
                            break;
                        case DemoSettlementEventKind.NaturalDeath:
                            naturalDeathsInEvents = checked(naturalDeathsInEvents + e.Count);
                            break;
                    }
                }
            }

            // #82：年界不跳日、不重複：第 N 年剛好是第 (N-1)×年長＋1 ～ N×年長 日。
            if (lastDay != firstDay + daysPerYear - 1)
                errors.Add($"第 {year} 年年界錯誤：應至第 {firstDay + daysPerYear - 1} 日，實際至第 {lastDay} 日");

            var summary = new DemoYearSummary(
                year, lastDay, weeks,
                popOpening, births, deaths, TotalPopulation(),
                foodOpening, produced, consumed, shortage, TotalFood(),
                reserveOpening, regenerated, harvested, TotalReserve(),
                hungerEvents, starvationEvents, deathsInEvents, recoveryEvents,
                world.Sites.Settlements.Count(s => world.SettlementPopulation(s.Id) > 0),
                emigrants, immigrants, world.PopulationInTransit,
                emigrationEvents, arrivalEvents, foundedEvents, world.Sites.Settlements.Count,
                firstDay,
                naturalDeaths, birthsInEvents, naturalDeathsInEvents, spoiled, other);
            errors.AddRange(summary.Problems());
            foreach (var result in DemoInvariants.Check(world, checkpoint))
            {
                var t = tallies[result.Id];
                t.Checks++;
                if (!result.Ok)
                {
                    t.Failures++;
                    t.First ??= $"第 {year} 年：{result.Problems[0]}";
                    errors.Add($"第 {year} 年不變量 {result.Id}（{result.Name}）不成立：{result.Problems[0]}");
                }
                tallies[result.Id] = t;
            }
            checkpoint = DemoInvariants.Capture(world);
            summaries.Add(summary);
            onYear?.Invoke(summary);
            // #103：年底各聚落的 TEMP 承載估計（純讀取；只在呼叫端要求時計算，不影響世界與摘要）。
            onYearSettlements?.Invoke(summary, world.SettlementCapacities());
        }

        var invariantTallies = DemoInvariants.All
            .Select(i => new DemoInvariantTally(i.Id, i.Name, tallies[i.Id].Checks, tallies[i.Id].Failures, tallies[i.Id].First))
            .ToList().AsReadOnly();
        return new DemoCenturyReport(seed, summaries.AsReadOnly(), errors.AsReadOnly(), invariantTallies);
    }
}
