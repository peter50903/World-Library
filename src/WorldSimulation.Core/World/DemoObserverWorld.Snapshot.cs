using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.World;

/// <summary>
/// V0-07B TEMP direct snapshot restore（第 02 章 a 節起為存檔格式第 5 版，仍是 TEMP，不是正式存檔規格）。
/// 讀檔不再從第 0 天重播整個世界：
/// ① 由種子決定、成本很低的部分（時鐘、排程、十條亂數流）用
///    <see cref="AdvanceOneDayCore"/>(settleWeekly: false) 重算，與存檔逐項比對（無法偽造）；
/// ② 週結算的狀態（週報、資源點存量、聚落糧食／人口／飢餓債、途中隊伍、下一個隊伍編號、新聚落）
///    直接從存檔安裝，但安裝前先做結構驗證：每週帳平衡與死亡／遷徙兩端對帳
///    （<see cref="DemoWeeklySettlement.Verify"/>）、期初＝上週期末的銜接、新聚落期初為 0、
///    C04：全部實體 ID 由世界唯一分配器發號——每週配號區間銜接、本週新 ID 恰好填滿區間、現存 ID 互不重複且小於下一號；
///    C05：每週十條亂數流計數器依序齊全、提交前＝上週提交後、最後一週提交後＝檔內亂數狀態；
///    引用都存在、事件與帳的週次和世界時間一致、
///    途中隊伍與最後一週隊伍帳一致且路線逐格相鄰。
/// #97：事件與週帳雙向核對（<see cref="DemoCausalIndex"/>），刪除、複製、竄改出發／抵達／死亡／歷史原因／人數，
///    即使把後面的事件編號與世界指紋一起補正，也會被拒絕（DemoSaveLedgerTamperTests）。
/// 限制（明列，不宣稱完整）：一份「各週帳都平衡、銜接都正確，但數字不是規則算出來的」週結算
/// 只有重播才能抓到；這種偽造需要同時改動整條帳鏈。正式存檔的完整驗證仍待正式規格。
/// </summary>
public sealed partial class DemoObserverWorld
{
    private static DemoObserverWorld ImportDirectSnapshot(
        string json, JsonElement root, WorldRecipe recipe, DemoWeeklyTempConfig weekly, int days)
    {
        var state = root.GetProperty("State");

        // ① 種子決定的部分：重算，不讀檔案。
        // 以檔內配方（含程式版本與設定雜湊原值）重建，重新匯出比對才不會因換一次建置而讀不回。
        var candidate = new DemoObserverWorld(recipe, weekly);
        var map = candidate.Map;
        var initialSites = candidate._sites;
        var initialDemography = candidate._demography;
        var openingEntityNext = DemoEntityIds.NextValue(candidate._entityIds);
        var openingRng = candidate._rng.Capture();
        var reserves = new Dictionary<int, long>(candidate._reserves);
        var food = new Dictionary<int, long>(candidate._settlementFood);
        var population = new Dictionary<int, long>(candidate._population);
        var debt = new Dictionary<int, long>(candidate._hungerDebt);
        for (var i = 0; i < days; i++)
            candidate.AdvanceOneDayCore(settleWeekly: false);

        // ② 週結算狀態：讀入後逐項驗證。
        var weekCount = days / 7;
        var reports = ReadWeekReports(state.GetProperty("WeekReports"));
        if (reports.Count != weekCount)
            Reject("週報數量與世界日期不符");

        var settlements = ReadArray<DemoSettlement>(state, "Settlements");
        var points = ReadArray<DemoResourcePoint>(state, "ResourcePoints");
        var groups = ReadArray<DemoPopGroup>(state, "PopulationGroups");
        var sites = RebuildSites(map, initialSites, settlements, weekCount);
        if (!points.SequenceEqual(sites.ResourcePoints) || !groups.SequenceEqual(sites.PopGroups))
            Reject("資源點或人口群體與聚落紀錄不一致");

        var transit = new SortedDictionary<int, DemoMigrationWeekLedger>();
        var departedWeek = new Dictionary<int, int>();
        var entityNext = openingEntityNext;
        var rngCounters = openingRng.Streams.ToDictionary(x => x.StreamId, x => x.DrawCounter);
        var lastBatchId = 0;
        for (var week = 1; week <= weekCount; week++)
        {
            var report = reports[week - 1];
            if (report.Week != week || report.WorldTime != TimeSpan.FromDays(7L * week))
                Reject($"第 {week} 週週報的週次或世界時間不符");
            DemoWeeklySettlement.Verify(report);

            CheckSettlementLedgers(report, sites, reserves, food, population, debt);
            CheckEvents(report, sites);
            lastBatchId = CheckMigrationLedgers(report, sites, transit, departedWeek, lastBatchId);
            entityNext = CheckEntityIdRange(report, sites, entityNext);
            CheckRngCounters(report, rngCounters);
        }

        // C05：最後一週提交後的計數器＝檔內（並經種子重算的）亂數狀態。
        var finalRng = candidate._rng.Capture();
        if (finalRng.Streams.Any(x => rngCounters[x.StreamId] != x.DrawCounter))
            Reject("最後一週亂數流計數器與世界亂數狀態不一致");

        // #88：事件編號連續不重用、直接原因合法且與週帳相符（43F CAUSE_PARENT）。
        // 沒有事件編號的舊 TEMP 存檔在此被明確拒絕，不默默接受。
        var causal = DemoCausalIndex.Build(reports);
        if (causal.Problems.Count > 0)
            Reject($"事件因果證據不合法（{causal.Problems[0]}；共 {causal.Problems.Count} 項，舊版沒有事件編號的 TEMP 存檔無法讀取）");

        // 最後一週期末＝目前存量；隊伍狀態＝最後一週隊伍帳。
        if (!SameBalances(ReadBalances(state, "Reserves"), reserves)
            || !SameBalances(ReadBalances(state, "SettlementFood"), food)
            || !SameBalances(ReadBalances(state, "Population"), population)
            || !SameBalances(ReadBalances(state, "HungerDebt"), debt))
            Reject("目前存量與最後一週週帳期末不一致");
        var entityIds = state.GetProperty("EntityIds").Deserialize<EntityIdAllocatorSnapshot>()
            ?? throw new InvalidDataException("TEMP 存檔缺少實體 ID 分配器狀態。");
        if (entityIds.Version != EntityIdAllocatorSnapshot.CurrentVersion || entityIds.Exhausted
            || entityIds.NextValue != checked((ulong)entityNext))
            Reject("實體 ID 分配器的下一號與最後一週配號紀錄不一致（ID 不得重用）");

        var batches = ReadArray<DemoMigrationBatch>(state, "MigrationBatches");
        CheckBatchesInTransit(batches, transit, departedWeek, sites, map, weekCount);

        // 全部驗證通過才安裝到尚未交給呼叫端的候選世界。
        candidate._weeks = reports.ToImmutableList();
        candidate._reserves = reserves.ToImmutableSortedDictionary();
        candidate._settlementFood = food.ToImmutableSortedDictionary();
        candidate._population = population.ToImmutableSortedDictionary();
        candidate._hungerDebt = debt.ToImmutableSortedDictionary();
        candidate._migrations = batches.ToImmutableList();
        candidate._entityIds = entityIds;
        candidate._sites = sites;
        if (weekly.HumanLifecycleEnabled)
        {
            var initial = initialDemography ?? throw new InvalidDataException("Missing opening human cohort.");
            var replayed = DemoHumanLifecycle.Replay(initial, reports, sites);
            var supplied = state.GetProperty("Demography").Deserialize<DemoTempDemographicSnapshot>()
                ?? throw new InvalidDataException("Missing human pregnancy/age snapshot.");
            var candidateSnapshot = replayed.Snapshot();
            if (supplied.Seed != candidateSnapshot.Seed || supplied.LastWeek != candidateSnapshot.LastWeek
                || supplied.NextId != candidateSnapshot.NextId
                || !supplied.People.SequenceEqual(candidateSnapshot.People)
                || sites.Settlements.Any(place =>
                    replayed.People.Count(p => p.SettlementId == place.Id) != population[place.Id]))
                Reject("出生／自然死亡人口身份、孕期或週帳遭篡改");
            candidate._demography = replayed;
        }

        // C04（40C INV_ENTITY_ID）：現存全部實體 ID 互不重複，且都小於分配器下一號。
        var liveIds = sites.EntityIds().Concat(batches.Select(b => (long)b.Id))
            .Concat(candidate._demography?.People.Select(p => p.Id) ?? Enumerable.Empty<long>()).ToList();
        if (liveIds.Count != liveIds.Distinct().Count() || liveIds.Any(id => id < 1 || id >= entityNext))
            Reject("實體 ID 重複或不小於分配器下一號");

        // 安裝後重新匯出必須與檔案完全相同（含重算的時鐘、排程、亂數、日歷史與世界指紋）。
        var suppliedDoc = JsonNode.Parse(json);
        var generatedDoc = JsonNode.Parse(candidate.ExportTempSave());
        // Older TEMP v2 files legitimately predate the optional demographic property.
        // Normalize ONLY that absent-null field for non-demographic presets.
        if (!weekly.HumanLifecycleEnabled && suppliedDoc?["State"]?["Demography"] is null
            && suppliedDoc?["State"] is JsonObject oldState && !oldState.ContainsKey("Demography")
            && generatedDoc?["State"] is JsonObject generatedState)
            generatedState.Remove("Demography");
        if (!JsonNode.DeepEquals(suppliedDoc, generatedDoc))
            Reject("存檔內容與種子重算或驗證後的狀態不一致");
        return candidate;
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
                RngCounters = Array.AsReadOnly(ReadArray<DemoRngCounter>(item, "RngCounters").ToArray())
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

    private static bool SameBalances(Dictionary<int, long> supplied, Dictionary<int, long> expected)
        => supplied.Count == expected.Count
        && expected.All(kv => supplied.TryGetValue(kv.Key, out var v) && v == kv.Value);

    /// <summary>
    /// C04：本週配號區間 [Opening, Closing) 必須接續上週、且恰好被本週新實體填滿：
    /// 每個新聚落 2 號（聚落＋資源點）、每支出發隊伍 1 號、每個新生兒 1 號；聚落與隊伍的號碼都落在區間內。
    /// 新生兒的號碼另由 <see cref="DemoHumanLifecycle.Replay"/> 從區間尾端重算比對。
    /// </summary>
    private static long CheckEntityIdRange(DemoWeekReport report, DemoWorldSites sites, long expectedOpening)
    {
        var opening = report.EntityIdNextOpening;
        var closing = report.EntityIdNextClosing;
        if (opening != expectedOpening || closing < opening)
            Reject($"第 {report.Week} 週實體 ID 配號區間與上週不銜接");
        var founded = sites.Settlements.Where(s => s.FoundedWeek == report.Week).ToList();
        var newIds = founded.SelectMany(s => sites.ResourcePointsOf(s.Id).Select(p => (long)p.Id).Prepend(s.Id))
            .Concat(report.Migrations.Where(m => m.Departed).Select(m => (long)m.BatchId)).ToList();
        var births = report.Settlements.Sum(l => l.Births);
        if (newIds.Any(id => id < opening || id >= closing) || newIds.Count != newIds.Distinct().Count()
            || checked(newIds.Count + births) != closing - opening)
            Reject($"第 {report.Week} 週新實體 ID 沒有恰好填滿配號區間");
        return closing;
    }

    /// <summary>
    /// C05（44J FAIL_REPLAY）：每份週報恰十筆計數器、順序同 <see cref="RngStreams.All"/>、提交後 ≥ 提交前、
    /// 提交前＝上一週提交後（第 1 週＝開局：地圖與聚落生成後）。
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

    /// <summary>開局聚落必須等於種子產生的結果；之後的新聚落依序重新套用建立規則（ID、空格、非水域）。</summary>
    private static DemoWorldSites RebuildSites(
        DemoHexWorld map, DemoWorldSites initial, IReadOnlyList<DemoSettlement> settlements, int weekCount)
    {
        var count = initial.Settlements.Count;
        if (settlements.Count < count || !settlements.Take(count).SequenceEqual(initial.Settlements))
            Reject("開局聚落與世界種子產生的結果不同");

        var sites = initial;
        var lastWeek = 1;
        foreach (var founded in settlements.Skip(count))
        {
            var from = sites.Settlements.FirstOrDefault(s => s.Id == founded.FoundedFromSettlementId);
            if (!founded.IsFounded || founded.FoundedWeek < lastWeek || founded.FoundedWeek > weekCount
                || founded.Name != $"新聚落{checked(sites.FoundedSettlementCount + 1)}號" || founded.FoundedByBatchId < 1
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
        return sites;
    }

    private static void CheckSettlementLedgers(
        DemoWeekReport report, DemoWorldSites sites,
        Dictionary<int, long> reserves, Dictionary<int, long> food,
        Dictionary<int, long> population, Dictionary<int, long> debt)
    {
        var existing = sites.Settlements.Where(s => s.FoundedWeek <= report.Week).OrderBy(s => s.Id).ToList();
        if (!report.Settlements.Select(l => l.SettlementId).SequenceEqual(existing.Select(s => s.Id)))
            Reject($"第 {report.Week} 週的聚落帳與當時存在的聚落不符");

        foreach (var (ledger, settlement) in report.Settlements.Zip(existing))
        {
            var point = sites.ResourcePointsOf(settlement.Id).Single();
            if (ledger.Week != report.Week || ledger.SettlementName != settlement.Name)
                Reject($"第 {report.Week} 週 {settlement.Name} 的帳目標示不符");

            if (settlement.FoundedWeek == report.Week)
            {
                // 新聚落不憑空：期初全部為 0（M1_SETTLE）。
                if (ledger.ReserveOpening != 0 || ledger.FoodOpening != 0
                    || ledger.PopulationOpening != 0 || ledger.HungerDebtOpening != 0
                    || reserves.ContainsKey(point.Id) || food.ContainsKey(settlement.Id))
                    Reject($"第 {report.Week} 週新聚落 {settlement.Name} 期初不是 0");
            }
            else if (ledger.ReserveOpening != reserves[point.Id] || ledger.FoodOpening != food[settlement.Id]
                || ledger.PopulationOpening != population[settlement.Id]
                || ledger.HungerDebtOpening != debt[settlement.Id])
            {
                Reject($"第 {report.Week} 週 {settlement.Name} 的期初與上週期末不銜接");
            }

            reserves[point.Id] = ledger.ReserveClosing;
            food[settlement.Id] = ledger.FoodClosing;
            population[settlement.Id] = ledger.PopulationClosing;
            debt[settlement.Id] = ledger.HungerDebtClosing;
        }
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

    /// <summary>隊伍帳銜接：出發隊伍 ID 嚴格遞增（區間另由 CheckEntityIdRange 檢查）；途中隊伍每週都要有帳且期初＝上週期末、位置接續。</summary>
    private static int CheckMigrationLedgers(
        DemoWeekReport report, DemoWorldSites sites, SortedDictionary<int, DemoMigrationWeekLedger> transit,
        Dictionary<int, int> departedWeek, int lastBatchId)
    {
        var travelling = report.Migrations.Where(m => !m.Departed).Select(m => m.BatchId).ToHashSet();
        if (!travelling.SetEquals(transit.Keys))
            Reject($"第 {report.Week} 週途中隊伍的帳目缺漏或多出");

        foreach (var m in report.Migrations.OrderBy(m => m.BatchId))
        {
            if (m.Week != report.Week)
                Reject($"第 {report.Week} 週隊伍 #{m.BatchId} 帳目週次不符");

            if (m.Departed)
            {
                var from = sites.Settlements.Single(s => s.Id == m.FromSettlementId);
                if (m.BatchId <= lastBatchId || m.CellBefore != new DemoCellRef(from.Column, from.Row)
                    || m.PopulationOpening <= 0)
                    Reject($"第 {report.Week} 週出發的隊伍 #{m.BatchId} 編號或出發地不合法");
                lastBatchId = m.BatchId;
                departedWeek[m.BatchId] = report.Week;
            }
            else
            {
                var previous = transit[m.BatchId];
                var expectedTo = m.Founding && m.Arrived ? m.ToSettlementId : previous.ToSettlementId;
                if (m.PopulationOpening != previous.PopulationClosing || m.FoodOpening != previous.FoodClosing
                    || m.HungerDebtOpening != previous.HungerDebtClosing || m.CellBefore != previous.CellAfter
                    || m.FromSettlementId != previous.FromSettlementId || m.Founding != previous.Founding
                    || m.ToSettlementId != expectedTo)
                    Reject($"第 {report.Week} 週隊伍 #{m.BatchId} 與上週帳目不銜接");
                transit.Remove(m.BatchId);
            }

            if (!m.Arrived && m.PopulationClosing > 0)
                transit[m.BatchId] = m;
        }

        return lastBatchId;
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
            if (path.IsDefault || path.Length < 2 || path.Any(c => !OnMap(c))
                || path.Zip(path.Skip(1)).Any(p => !DemoMigrationRoutes.AreAdjacent(p.First, p.Second))
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

    private static bool OnMap(DemoCellRef cell)
        => cell.Column >= 0 && cell.Column < DemoHexWorld.Columns && cell.Row >= 0 && cell.Row < DemoHexWorld.Rows;

    [DoesNotReturn]
    private static void Reject(string reason)
        => throw new InvalidDataException($"TEMP 存檔驗證失敗：{reason}，已拒絕載入。");
}
