using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.History;
using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.World;

/// <summary>
/// 第 04-1 章：分層存檔的讀檔（存檔第 21 版起）。使用者 2026-10-22：存檔只要能回到存檔當下的狀態，
/// 過程不需要逐週保存；總綱第 4 節第 10 條：只需偵測損毀、版本不符、內部不一致，不以防竄改為需求。
/// ① 由種子決定、成本很低的部分（時鐘、排程、十條亂數流、地圖、開局聚落）重算，不讀檔案；
/// ② 目前狀態（聚落、存量、人口、途中隊伍、實體 ID、群體人口、正式史庫、聚落小史）與近期週報直接安裝，
///    安裝前做內部一致性檢查：近期週報每帳平衡（<see cref="DemoWeeklySettlement.Verify"/>）、週次連續、事件與聚落相符、
///    亂數計數器逐週銜接、途中隊伍與最後一週帳一致、群體人口＝帳上人口，以及全部世界不變量（<see cref="DemoInvariants"/>）；
/// ③ 安裝後重新匯出必須與檔案完全相同。
/// 不再從第 1 週重播整條帳鏈（第 04 章舊做法，存檔隨年數無限變大）。
/// </summary>
public sealed partial class DemoObserverWorld
{
    private static DemoObserverWorld ImportDirectSnapshot(
        string json, JsonElement root, WorldRecipe recipe, DemoWeeklyTempConfig weekly, int days)
    {
        var state = root.GetProperty("State");

        // ① 種子決定的部分：重算，不讀檔案。
        var candidate = new DemoObserverWorld(recipe, weekly);
        var map = candidate.Map;
        var initialSites = candidate._sites;
        for (var i = 0; i < days; i++)
            candidate.AdvanceOneDayCore(settleWeekly: false);

        // ② 近期週報與較早的年帳、事件。
        var weekCount = days / 7;
        var weekBase = state.GetProperty("WeekBase").GetInt32();
        if (weekBase < 0 || weekBase > weekCount
            || (weekBase > 0 && FirstWeekOfYear(YearsBeforeWeek(weekBase + 1) + 1) != weekBase + 1))
            Reject("近期週報的起點不合法（必須是某一年的第一次週結算）");
        var reports = ReadWeekReports(state.GetProperty("WeekReports"));
        if (reports.Count != weekCount - weekBase)
            Reject("週報數量與世界日期不符");
        for (var i = 0; i < reports.Count; i++)
        {
            var report = reports[i];
            if (report.Week != weekBase + i + 1 || report.WorldTime != TimeSpan.FromDays(7L * report.Week))
                Reject($"第 {weekBase + i + 1} 份週報的週次或世界時間不符");
            DemoWeeklySettlement.Verify(report);
        }
        var earlierYears = ReadArray<DemoYearSummary>(state, "EarlierYears");
        var expectedYears = weekBase == 0 ? 0 : YearsBeforeWeek(weekBase + 1);
        if (earlierYears.Count != expectedYears || earlierYears.Where((y, i) => y.Year != i + 1).Any())
            Reject("較早年份的年帳缺漏或年份不連續");
        var earlierEvents = ReadArray<DemoSettlementEvent>(state, "EarlierEvents");
        if (earlierEvents.Any(e => e.Week < 1 || e.Week > weekBase))
            Reject("較早的聚落事件週次不在近期週報之前");
        var chronicle = state.GetProperty("Chronicle").Deserialize<Dictionary<int, DemoSettlementChronicle>>()
            ?? throw new InvalidDataException("TEMP 存檔缺少聚落小史。");

        // 聚落：開局聚落＝種子產生的結果；之後的新聚落依序套用建立（ID 遞增、空格、非水域）。
        var settlements = ReadArray<DemoSettlement>(state, "Settlements");
        var points = ReadArray<DemoResourcePoint>(state, "ResourcePoints");
        var groups = ReadArray<DemoPopGroup>(state, "PopulationGroups");
        var sites = RebuildSites(map, initialSites, settlements, weekCount);
        if (!points.SequenceEqual(sites.ResourcePoints) || !groups.SequenceEqual(sites.PopGroups))
            Reject("資源點或人口群體與聚落紀錄不一致");
        if (chronicle.Keys.Any(id => sites.Settlements.All(s => s.Id != id)))
            Reject("聚落小史指向不存在的聚落");
        foreach (var report in reports)
            CheckEvents(report, sites);
        // 第 04 章 c 節：近期週報的來源帳對照地圖重算（來源清單、再生、份額只給可及聚落、需求與勞力上限、取水與腐敗）。
        // 存量從保留的第一週期初起逐週銜接。
        var stocks = reports.Count == 0 ? new Dictionary<int, long>()
            : reports[0].Sources.Where(x => x.StockOpening > 0).ToDictionary(x => x.SourceId, x => x.StockOpening);
        foreach (var report in reports)
            CheckSourceLedgers(report, map, sites, weekly, stocks);
        // C04：近期週報的配號區間逐週銜接，且每週恰好被本週新實體填滿。
        long? entityNext = reports.Count == 0 ? null : reports[0].EntityIdNextOpening;
        foreach (var report in reports)
            entityNext = CheckEntityIdRange(report, sites, entityNext!.Value);

        // 亂數計數器在近期週報內逐週銜接，最後一週提交後＝世界亂數狀態（C05）。
        var rngCounters = reports.Count == 0
            ? candidate._rng.Capture().Streams.ToDictionary(x => x.StreamId, x => x.DrawCounter)
            : reports[0].RngCounters.ToDictionary(c => c.StreamId, c => c.Before);
        foreach (var report in reports)
            CheckRngCounters(report, rngCounters);
        if (candidate._rng.Capture().Streams.Any(x => rngCounters[x.StreamId] != x.DrawCounter))
            Reject("最後一週亂數流計數器與世界亂數狀態不一致");

        // 目前存量。
        var reserves = ReadBalances(state, "Reserves");
        var food = ReadBalances(state, "SettlementFood");
        var population = ReadBalances(state, "Population");
        var debt = ReadBalances(state, "HungerDebt");
        var sourceStocks = ReadBalances(state, "SourceStocks");
        if (!sites.ResourcePoints.Select(p => p.Id).ToHashSet().SetEquals(reserves.Keys)
            || new[] { food, population, debt }.Any(d => !sites.Settlements.Select(s => s.Id).ToHashSet().SetEquals(d.Keys))
            || new[] { reserves, food, population, debt, sourceStocks }.Any(d => d.Values.Any(v => v < 0))
            || sourceStocks.Values.Any(v => v == 0))
            Reject("目前存量與聚落不對應或出現負值");
        // 第 08 章 a 節：目前沒有任何合法的開墾行動，土地開發狀態必須全部未開發。
        if (ReadArray<DemoLandParcel>(state, "Land").Count != 0)
            Reject("土地開發狀態沒有合法來源（尚無開墾行動）");
        var entityIds = state.GetProperty("EntityIds").Deserialize<EntityIdAllocatorSnapshot>()
            ?? throw new InvalidDataException("TEMP 存檔缺少實體 ID 分配器狀態。");
        if (entityIds.Version != EntityIdAllocatorSnapshot.CurrentVersion || entityIds.Exhausted)
            Reject("實體 ID 分配器狀態不合法");

        // 途中隊伍＝最後一週仍在路上的帳（出發週一定在近期週報內，見 SaveWindowStart）。
        var batches = ReadArray<DemoMigrationBatch>(state, "MigrationBatches");
        var transit = new SortedDictionary<int, DemoMigrationWeekLedger>();
        var departedWeek = new Dictionary<int, int>();
        foreach (var report in reports)
            foreach (var m in report.Migrations.Where(m => m.Departed))
                departedWeek[m.BatchId] = report.Week;
        if (reports.Count > 0)
            foreach (var m in reports[^1].Migrations.Where(m => !m.Arrived && m.PopulationClosing > 0))
                transit[m.BatchId] = m;
        if (batches.Any(b => !departedWeek.ContainsKey(b.Id)))
            Reject("途中隊伍的出發週不在近期週報內");
        CheckBatchesInTransit(batches, transit, departedWeek, sites, map, weekCount);

        // 安裝到尚未交給呼叫端的候選世界。
        candidate._weekBase = weekBase;
        candidate._weeks = reports.ToImmutableList();
        candidate._frozenYears = earlierYears.ToImmutableList();
        candidate._earlierEvents = earlierEvents.ToImmutableList();
        candidate._chronicle = chronicle.ToImmutableSortedDictionary();
        var debtSources = ReadBalances(state, "DebtSources");
        if (debtSources.Keys.Any(id => sites.Settlements.All(s => s.Id != id)) || debtSources.Values.Any(v => v < 1))
            Reject("飢餓債來源指向不存在的聚落或事件");
        candidate._debtSources = debtSources.ToImmutableSortedDictionary();
        candidate._reserves = reserves.ToImmutableSortedDictionary();
        candidate._settlementFood = food.ToImmutableSortedDictionary();
        candidate._population = population.ToImmutableSortedDictionary();
        candidate._hungerDebt = debt.ToImmutableSortedDictionary();
        candidate._sourceStocks = sourceStocks.ToImmutableSortedDictionary();
        candidate._migrations = batches.ToImmutableList();
        candidate._entityIds = entityIds;
        candidate._sites = sites;

        if (weekly.HumanLifecycleEnabled)
        {
            var supplied = state.GetProperty("Demography").Deserialize<DemoCohortSnapshot>()
                ?? throw new InvalidDataException("Missing human cohort snapshot.");
            if (supplied.Groups.IsDefault || supplied.Pregnancies.IsDefault || supplied.LastWeek != weekCount)
                Reject("群體人口快照缺漏或週次不符");
            var people = DemoCohortDemography.Restore(supplied);
            if (sites.Settlements.Any(place => people.ResidentsOf(place.Id) != population[place.Id])
                // 第 07 章 b 節：途中隊伍的人數＝攜帶的人數；沒有不屬於任何現存隊伍的旅人。
                || batches.Any(b => people.MembersOf(b.Id) != b.People)
                || people.Groups.Any(g => g.BatchId > 0 && batches.All(b => b.Id != g.BatchId))
                || people.Representatives.Any(r => sites.Settlements.All(s => s.Id != r.SettlementId)))
                Reject("群體人口與聚落、隊伍人數不一致");
            candidate._demography = people;
        }

        // 正式史庫：逐筆驗證（編號連續、時間不倒退、原因指向既有事件）；與聚落事件一一對應由不變量檢查。
        var history = ReadArray<WorldEvent>(state, "History");
        try
        {
            candidate._history = EventHistory.Rebuild(new[] { (IReadOnlyList<WorldEvent>)history });
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidDataException($"TEMP 存檔被拒絕：正式史庫不合法（{ex.Message}）。", ex);
        }
        var settlementEventIds = candidate.SettlementEvents().Select(e => e.EventId);
        if (!settlementEventIds.SequenceEqual(candidate._history.Events.Select(e => e.EventId)))
            Reject("聚落事件與正式史庫的事件編號不一致");
        // 近期週報的事件經提交時同一個轉換器投影，必須與正式史庫逐筆相同。
        var byId = candidate._history.Events.ToDictionary(e => e.EventId);
        foreach (var report in reports)
            foreach (var projected in DemoEventProjection.ToWorldEvents(report, sites, null))
                if (!byId.TryGetValue(projected.EventId, out var stored) || stored.Fingerprint() != projected.Fingerprint())
                    Reject($"第 {report.Week} 週的事件 #{projected.EventId} 與正式史庫不一致");
        // #88／#97：近期週報的事件與週帳雙向核對、直接原因合法（較早事件只建索引）。
        var causal = candidate.BuildCausalIndex();
        if (causal.Problems.Count > 0)
            Reject($"事件因果證據不合法（{causal.Problems[0]}；共 {causal.Problems.Count} 項）");

        // 第 08 章 c 節：讀檔與長測共用同一份不變量註冊表（第 04-1 章起從近期週報第一週檢查）。
        if (DemoInvariants.Check(candidate).FirstOrDefault(r => !r.Ok) is { } broken)
            Reject($"不變量 {broken.Id}（{broken.Name}）不成立：{broken.Problems[0]}");

        // ③ 安裝後重新匯出必須與檔案完全相同（含重算的時鐘、排程、亂數與世界指紋）。
        if (!JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(candidate.ExportTempSave())))
            Reject("存檔內容與種子重算或驗證後的狀態不一致");
        return candidate;
    }

    /// <summary>
    /// 第 04 章 c 節：來源帳（內部守恆已由 <see cref="DemoWeeklySettlement.Verify"/> 檢查）再對照地圖：
    /// 來源清單＝依地圖與聚落重算（預算＞0）；份額只給可及範圍內的聚落；每聚落合計 ≤ 需求且 ≤ 勞力上限。
    /// 第 07 章 c 節起共享採集情境也會建村與成為遺跡：來源清單依當週開始時的人造佔格重算。
    /// </summary>
    private static void CheckSourceLedgers(DemoWeekReport report, DemoHexWorld map, DemoWorldSites sites, DemoWeeklyTempConfig weekly,
        Dictionary<int, long> stocks)
    {
        var w = report.Week;
        if (!weekly.SharedCatchmentEnabled)
        {
            if (report.Sources.Count != 0 || report.Settlements.Any(l => l.SourceSlack != 0))
                Reject($"第 {w} 週來源帳：非共享採集情境不應有來源帳或來源餘量");
            return;
        }
        // 第 08-1 章：聚落帳上的來源餘量＝依地圖與本週來源帳重算（生育與擁擠外移都依它）。
        var slack = DemoCrowding.SlackInReach(map,
            sites.Settlements.Where(s => s.FoundedWeek < w && !(s.IsRuin && s.RuinedWeek < w)), report.Sources);
        foreach (var ledger in report.Settlements)
        {
            if (ledger.SourceSlack != slack.GetValueOrDefault(ledger.SettlementId))
                Reject($"第 {w} 週聚落 #{ledger.SettlementId} 的來源餘量與地圖重算不符");
        }
        // 第 06 章 c 節：期初存量＝上週期末、再生＝min(每週供應, 上限－期初)，由帳鏈重算後逐項比對。
        var expected = DemoSourceWeekLedger.Plan(DemoForagingWorld.Sources(map, sites.OccupiedCellsAtWeek(w)), stocks,
            DemoForagingWorld.Settings.SourceStockCapWeeks);
        if (report.Sources.Count != expected.Count)
            Reject($"第 {w} 週來源帳筆數 {report.Sources.Count} 與地圖重算 {expected.Count} 不符");
        for (var i = 0; i < expected.Count; i++)
        {
            var source = report.Sources[i];
            var (truth, opening, regenerated) = expected[i];
            if (source.SourceId != truth.Id || source.Kind != truth.Kind || source.Column != truth.Column
                || source.Row != truth.Row || source.FoodAvailable != truth.FoodAvailable
                || source.StockOpening != opening || source.Regenerated != regenerated)
                Reject($"第 {w} 週來源 #{source.SourceId} 的種類、位置、存量或再生與地圖及上週帳重算不符");
            var origin = map.GetCell(source.Column, source.Row);
            var includeOrigin = source.Kind is DemoCatchmentSourceKind.Forest or DemoCatchmentSourceKind.GrasslandForage;
            foreach (var share in source.Shares)
            {
                var place = sites.Settlements.FirstOrDefault(s => s.Id == share.SettlementId);
                if (place is null || !DemoHexFoodCatchment.IsInReach(origin, map.GetCell(place.Column, place.Row), includeOrigin))
                    Reject($"第 {w} 週來源 #{source.SourceId} 分給了範圍外的聚落 #{share.SettlementId}");
            }
        }
        foreach (var ledger in report.Settlements)
        {
            var total = report.Sources.SelectMany(x => x.Shares).Where(x => x.SettlementId == ledger.SettlementId).Sum(x => x.Food);
            var need = DemoCrowding.Claim(weekly.HumanLifecycleEnabled, checked(ledger.FoodConsumed + ledger.FoodShortage), ledger.FoodOpening);
            var workers = (ledger.AdultWorkersOpening < 0 ? ledger.PopulationOpening : ledger.AdultWorkersOpening) - ledger.WaterCarriers;
            var labour = checked(workers * weekly.YieldPerWorkerPerWeek);
            if (total > need || total > labour)
                Reject($"第 {w} 週聚落 #{ledger.SettlementId} 的採集合計 {total} 超過需求 {need} 或勞力上限 {labour}");
        }
        // 第 06 章 d 節：取水者、缺水債、腐敗與需求依規則重算比對。
        var survival = DemoSurvivalTempSettings.Current;
        foreach (var ledger in report.Settlements)
        {
            var place = sites.Settlements.Single(s => s.Id == ledger.SettlementId);
            var people = ledger.PopulationOpening;
            var adults = Math.Min(people, ledger.AdultWorkersOpening < 0 ? people : ledger.AdultWorkersOpening);
            var hasWater = DemoMigrationRoutes.HasWaterAccess(map, new DemoCellRef(place.Column, place.Row));
            var carriers = hasWater ? Math.Min(adults, survival.CarriersNeeded(people)) : 0;
            var served = hasWater ? Math.Min(people, checked(carriers * survival.PeoplePerWaterCarrier)) : 0;
            if (ledger.WaterCarriers != carriers
                || ledger.WaterShortage != DemoSurvivalTempSettings.DehydrationDebt(people - served, weekly.FoodPerPersonPerWeek)
                || ledger.FoodSpoiled != DemoSurvivalTempSettings.Spoilage(ledger.FoodOpening)
                || ledger.FoodConsumed + ledger.FoodShortage != survival.Demand(adults, people - adults, weekly.FoodPerPersonPerWeek))
                Reject($"第 {w} 週聚落 #{ledger.SettlementId} 的取水、缺水、腐敗或需求與規則重算不符");
        }

        stocks.Clear();
        foreach (var source in report.Sources.Where(x => x.StockClosing > 0))
            stocks[source.SourceId] = source.StockClosing;
    }

    private static List<DemoWeekReport> ReadWeekReports(JsonElement array)
    {
        var reports = new List<DemoWeekReport>();
        foreach (var item in array.EnumerateArray())
        {
            var ledgers = ReadArray<DemoSettlementWeekLedger>(item, "Settlements");
            var events = ReadArray<DemoSettlementEvent>(item, "Events");
            var migrations = ReadArray<DemoMigrationWeekLedger>(item, "Migrations");
            reports.Add(new DemoWeekReport(
                item.GetProperty("Week").GetInt32(),
                item.GetProperty("WorldTime").Deserialize<TimeSpan>(),
                Array.AsReadOnly(ledgers.ToArray()),
                Array.AsReadOnly(events.ToArray()),
                Array.AsReadOnly(migrations.ToArray()))
            {
                EntityIdNextOpening = item.GetProperty("EntityIdNextOpening").GetInt64(),
                EntityIdNextClosing = item.GetProperty("EntityIdNextClosing").GetInt64(),
                RngCounters = Array.AsReadOnly(ReadArray<DemoRngCounter>(item, "RngCounters").ToArray()),
                Sources = Array.AsReadOnly(ReadArray<DemoSourceWeekLedger>(item, "Sources").ToArray()),
                // 第 07-1 章 b 節：代表人物就任與卸任。
                Appointments = Array.AsReadOnly(ReadArray<DemoRepresentative>(item, "Appointments").ToArray()),
                RepresentativeExits = Array.AsReadOnly(ReadArray<DemoRepresentativeExit>(item, "RepresentativeExits").ToArray())
            });
        }

        return reports;
    }

    private static List<T> ReadArray<T>(JsonElement parent, string name) where T : class
    {
        var items = parent.GetProperty(name).Deserialize<List<T>>()
            ?? throw new InvalidDataException($"TEMP 存檔缺少 {name}。");
        if (items.Any(i => i is null))
            Reject($"{name} 含有空白項目");
        return items;
    }

    private static Dictionary<int, long> ReadBalances(JsonElement state, string name)
        => state.GetProperty(name).Deserialize<Dictionary<int, long>>()
            ?? throw new InvalidDataException($"TEMP 存檔缺少 {name}。");

    /// <summary>
    /// C04：本週配號區間 [Opening, Closing) 必須接續上週、且恰好被本週新實體填滿：
    /// 每個新聚落 2 號（聚落＋資源點）、每支出發隊伍 1 號、每位新任代表人物 1 號。
    /// </summary>
    private static long CheckEntityIdRange(DemoWeekReport report, DemoWorldSites sites, long expectedOpening)
    {
        var opening = report.EntityIdNextOpening;
        var closing = report.EntityIdNextClosing;
        if (opening != expectedOpening || closing < opening)
            Reject($"第 {report.Week} 週實體 ID 配號區間與上週不銜接");
        var founded = sites.Settlements.Where(s => s.FoundedWeek == report.Week).ToList();
        var newIds = founded.SelectMany(s => sites.ResourcePointsOf(s.Id).Select(p => (long)p.Id).Prepend(s.Id))
            .Concat(report.Migrations.Where(m => m.Departed).Select(m => (long)m.BatchId))
            .Concat(report.Appointments.Select(a => a.Id)).ToList();
        if (newIds.Any(id => id < opening || id >= closing) || newIds.Count != newIds.Distinct().Count()
            || newIds.Count != closing - opening)
            Reject($"第 {report.Week} 週新實體 ID 沒有恰好填滿配號區間");
        return closing;
    }

    /// <summary>
    /// C05（44J FAIL_REPLAY）：每份週報恰十筆計數器、順序同 <see cref="RngStreams.All"/>、提交後 ≥ 提交前、
    /// 提交前＝上一週提交後。
    /// </summary>
    private static void CheckRngCounters(DemoWeekReport report, Dictionary<string, ulong> counters)
    {
        var supplied = report.RngCounters;
        if (supplied.Count != RngStreams.All.Count
            || !supplied.Select(c => c.StreamId).SequenceEqual(RngStreams.All))
            Reject($"第 {report.Week} 週亂數流計數器缺漏或順序錯誤");
        foreach (var c in supplied)
        {
            if (c.Before != counters[c.StreamId] || c.After < c.Before)
                Reject($"第 {report.Week} 週亂數流 {c.StreamId} 計數器與上週不銜接");
            counters[c.StreamId] = c.After;
        }
    }

    /// <summary>開局聚落必須等於種子產生的結果；之後的新聚落依序套用建立（ID 遞增、空格、非水域、有水源）。</summary>
    private static DemoWorldSites RebuildSites(
        DemoHexWorld map, DemoWorldSites initial, IReadOnlyList<DemoSettlement> settlements, int weekCount)
    {
        var count = initial.Settlements.Count;
        // 第 07 章 c 節：成為遺跡的週次另外套用。
        if (settlements.Count < count
            || !settlements.Take(count).Select(s => s with { RuinedWeek = 0 }).SequenceEqual(initial.Settlements))
            Reject("開局聚落與世界種子產生的結果不同");

        var sites = initial;
        var lastWeek = 1;
        foreach (var founded in settlements.Skip(count).Select(s => s with { RuinedWeek = 0 }))
        {
            var from = sites.Settlements.FirstOrDefault(s => s.Id == founded.FoundedFromSettlementId);
            if (!founded.IsFounded || founded.FoundedWeek < lastWeek || founded.FoundedWeek > weekCount
                || string.IsNullOrWhiteSpace(founded.Name) || founded.FoundedByBatchId < 1
                || from is null || from.FoundedWeek >= founded.FoundedWeek
                || !DemoMigrationRoutes.HasWaterAccess(map, new DemoCellRef(founded.Column, founded.Row)))
                Reject($"新聚落 #{founded.Id} 的建立紀錄不合法");
            // 資源點緊接聚落之後配號（DemoWeeklySettlement）；檢查 ID 大於既有全部、空格、非水域。
            sites = sites.WithFoundedSettlement(founded, checked(founded.Id + 1), map);
            lastWeek = founded.FoundedWeek;
        }

        if (sites.Settlements.Select(s => s.FoundedByBatchId).Where(id => id > 0).Distinct().Count()
            != sites.Settlements.Count - count)
            Reject("同一支開拓隊建立了多個聚落");
        foreach (var ruin in settlements.Where(s => s.IsRuin))
        {
            if (ruin.RuinedWeek > weekCount || ruin.RuinedWeek <= ruin.FoundedWeek)
                Reject($"聚落 #{ruin.Id} 的遺跡週次不合法");
            sites = sites.WithRuin(ruin.Id, ruin.RuinedWeek);
        }
        return sites;
    }

    private static void CheckEvents(DemoWeekReport report, DemoWorldSites sites)
    {
        foreach (var e in report.Events)
        {
            var settlement = sites.Settlements.FirstOrDefault(s => s.Id == e.SettlementId);
            if (e.Week != report.Week || e.WorldTime != report.WorldTime || settlement is null
                || settlement.FoundedWeek > report.Week || e.SettlementName != settlement.Name
                || !Enum.IsDefined(e.Kind) || e.Count < 0 || e.MigrationBatchId < 0
                || string.IsNullOrWhiteSpace(e.Cause))
                Reject($"第 {report.Week} 週有不合法的聚落事件");

            if (e.Kind == DemoSettlementEventKind.SettlementFounded
                && (settlement.FoundedWeek != report.Week || settlement.FoundedByBatchId != e.MigrationBatchId))
                Reject($"第 {report.Week} 週建立聚落事件與聚落紀錄不符");
            if (e.Kind == DemoSettlementEventKind.SettlementRuined && settlement.RuinedWeek != report.Week)
                Reject($"第 {report.Week} 週成為遺跡事件與聚落紀錄不符");
        }

        foreach (var ruin in sites.Settlements.Where(s => s.RuinedWeek == report.Week))
        {
            if (report.Events.Count(e => e.Kind == DemoSettlementEventKind.SettlementRuined && e.SettlementId == ruin.Id) != 1)
                Reject($"遺跡 {ruin.Name} 沒有對應的成為遺跡事件");
        }

        foreach (var founded in sites.Settlements.Where(s => s.FoundedWeek == report.Week))
        {
            var arrival = report.Migrations.SingleOrDefault(m => m.BatchId == founded.FoundedByBatchId);
            if (arrival is null || !arrival.Founding || !arrival.Arrived || arrival.ToSettlementId != founded.Id
                || arrival.FromSettlementId != founded.FoundedFromSettlementId
                || arrival.CellAfter != new DemoCellRef(founded.Column, founded.Row))
                Reject($"新聚落 {founded.Name} 沒有對應的開拓隊抵達紀錄");
        }
    }

    private static void CheckBatchesInTransit(
        IReadOnlyList<DemoMigrationBatch> batches, SortedDictionary<int, DemoMigrationWeekLedger> transit,
        Dictionary<int, int> departedWeek, DemoWorldSites sites, DemoHexWorld map, int weekCount)
    {
        if (!batches.Select(b => b.Id).SequenceEqual(transit.Keys))
            Reject("途中隊伍與最後一週隊伍帳不一致");

        foreach (var batch in batches)
        {
            var ledger = transit[batch.Id];
            var path = batch.Path;
            var from = sites.Settlements.Single(s => s.Id == batch.FromSettlementId);
            if (path.IsDefault || path.Length < 2 || path.Any(c => !map.Contains(c.Column, c.Row))
                || path.Zip(path.Skip(1)).Any(p => !map.Routes.IsLandPassable(p.First, p.Second)) // 第 06 章 a 節：每步都要有陸路路線段
                || path[0] != new DemoCellRef(from.Column, from.Row))
                Reject($"途中隊伍 #{batch.Id} 的路線不合法");

            var destination = path[path.Length - 1];
            var to = sites.Settlements.FirstOrDefault(s => s.Id == batch.ToSettlementId);
            var destinationOk = batch.Founding
                ? batch.ToSettlementId == 0 && batch.NewsWeek == 0
                    && batch.ToName == $"新聚落預定地{destination}"
                    && sites.SettlementAt(destination.Column, destination.Row) is null
                : to is not null && to.Name == batch.ToName && destination == new DemoCellRef(to.Column, to.Row)
                    && batch.NewsWeek >= 1 && batch.NewsWeek < batch.DepartedWeek;
            if (!destinationOk || batch.FromName != from.Name
                || batch.DepartedWeek != departedWeek[batch.Id]
                || batch.StepIndex != weekCount - batch.DepartedWeek || batch.StepIndex >= path.Length - 1
                || path[batch.StepIndex] != ledger.CellAfter
                || batch.People != ledger.PopulationClosing || batch.Food != ledger.FoodClosing
                || batch.HungerDebt != ledger.HungerDebtClosing || batch.Founding != ledger.Founding
                || batch.FromSettlementId != ledger.FromSettlementId || batch.ToSettlementId != ledger.ToSettlementId)
                Reject($"途中隊伍 #{batch.Id} 與帳目不一致");
        }
    }

    [DoesNotReturn]
    private static void Reject(string reason)
        => throw new InvalidDataException($"TEMP 存檔驗證失敗：{reason}，已拒絕載入。");
}
