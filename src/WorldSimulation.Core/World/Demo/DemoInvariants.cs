using WorldSimulation.Core.History;

namespace WorldSimulation.Core.World.Demo;

/// <summary>一條登錄的世界不變量：編號、中文名稱與檢查函式（回傳違反說明；空＝成立）。</summary>
public sealed record DemoInvariant(string Id, string Name, Func<DemoInvariantContext, IEnumerable<string>> Check);

/// <summary>一條不變量的一次檢查結果。</summary>
public sealed record DemoInvariantResult(string Id, string Name, IReadOnlyList<string> Problems)
{
    public bool Ok => Problems.Count == 0;
}

/// <summary>
/// 上一次檢查時記下的世界（只保留不可變的參照與計數），讓下一次檢查只看新增的週報、
/// 並確認舊的週報與史庫事件沒有被改寫或刪除。
/// </summary>
public sealed record DemoInvariantCheckpoint(
    int Day,
    IReadOnlyList<DemoWeekReport> Weeks,
    IReadOnlyList<WorldEvent> Events,
    long EntityIdNext);

/// <summary>檢查時的世界與上一次的檢查點（第一次或讀檔時為 null＝從第 1 週全部檢查）。</summary>
public sealed record DemoInvariantContext(DemoObserverWorld World, DemoInvariantCheckpoint? Previous)
{
    /// <summary>本次要檢查的第一份週報索引（上一次已檢查過的不再逐筆重算，改由「歷史不改寫」確認未被改動）。</summary>
    internal int FirstWeekIndex => Previous is null ? 0 : Math.Min(Previous.Weeks.Count, World.WeekReports().Count);
}

/// <summary>
/// 第 08 章 c 節（P1.7.01-a 提前）：世界不變量註冊表。全部不變量只在這裡登錄一次，
/// 無畫面長測（<see cref="DemoCenturyRun"/> 每年年底）與讀檔（<c>ImportTempSave</c> 安裝前）共用同一份清單。
/// 只讀世界，不改任何狀態。逐週檢查是增量的：有檢查點時只看之後的新週報。
/// </summary>
public static class DemoInvariants
{
    /// <summary>登錄的不變量（順序即報告順序）。</summary>
    public static IReadOnlyList<DemoInvariant> All { get; } = new DemoInvariant[]
    {
        new("TIME_MONOTONIC", "時間單調", TimeMonotonic),
        new("ID_NO_REUSE", "ID 不重用", IdNoReuse),
        new("HISTORY_APPEND_ONLY", "歷史不改寫", HistoryAppendOnly),
        new("POP_LEDGER", "人口帳", PopulationLedger),
        new("RESOURCE_LEDGER", "資源帳", ResourceLedger),
        new("SOURCE_LEDGER", "來源帳", SourceLedger),
        new("MIGRATION_CONSERVATION", "遷徙守恆", MigrationConservation)
    };

    /// <summary>依序檢查全部不變量。</summary>
    public static IReadOnlyList<DemoInvariantResult> Check(DemoObserverWorld world, DemoInvariantCheckpoint? previous = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        var context = new DemoInvariantContext(world, previous);
        return All.Select(i => new DemoInvariantResult(i.Id, i.Name, i.Check(context).Take(20).ToList().AsReadOnly()))
            .ToList().AsReadOnly();
    }

    /// <summary>記下目前世界作為下一次檢查的起點。</summary>
    public static DemoInvariantCheckpoint Capture(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new DemoInvariantCheckpoint(world.Snapshot().DaysElapsed, world.WeekReports(), world.EventHistory.Events,
            (long)world.EntityIdNextValue);
    }

    private static DemoWeekReport? Before(IReadOnlyList<DemoWeekReport> weeks, int index) => index > 0 ? weeks[index - 1] : null;

    // ── 時間：世界日單調前進；第 N 份週報＝第 N 週、第 7N 日；週報數＝已過日數÷7 ───────────
    private static IEnumerable<string> TimeMonotonic(DemoInvariantContext c)
    {
        var world = c.World;
        var day = world.Snapshot().DaysElapsed;
        var weeks = world.WeekReports();
        if (c.Previous is { } p && day < p.Day)
            yield return $"世界日倒退：上次第 {p.Day} 日，現在第 {day} 日";
        if (weeks.Count != day / DemoCenturyRun.DaysPerWeeklySettlement)
            yield return $"第 {day} 日應有 {day / DemoCenturyRun.DaysPerWeeklySettlement} 份週報，實際 {weeks.Count} 份";
        for (var i = c.FirstWeekIndex; i < weeks.Count; i++)
        {
            var r = weeks[i];
            if (r.Week != i + 1 || r.WorldTime != TimeSpan.FromDays((long)DemoCenturyRun.DaysPerWeeklySettlement * (i + 1)))
                yield return $"第 {i + 1} 份週報標示第 {r.Week} 週、世界時間 {r.WorldTime.TotalDays} 日";
            if (r.Settlements.Any(l => l.Week != r.Week) || r.Events.Any(e => e.Week != r.Week || e.WorldTime != r.WorldTime)
                || r.Migrations.Any(m => m.Week != r.Week))
                yield return $"第 {r.Week} 週週報內的帳或事件標示了別的週次或時間";
        }
    }

    // ── ID：配號區間逐週銜接且不倒退；現存實體 ID 互不重複且小於下一號 ─────────────────
    private static IEnumerable<string> IdNoReuse(DemoInvariantContext c)
    {
        var world = c.World;
        var weeks = world.WeekReports();
        var next = (long)world.EntityIdNextValue;
        if (c.Previous is { } p && next < p.EntityIdNext)
            yield return $"實體 ID 分配器倒退：上次下一號 {p.EntityIdNext}，現在 {next}";
        for (var i = Math.Max(1, c.FirstWeekIndex); i < weeks.Count; i++)
        {
            if (weeks[i].EntityIdNextOpening != weeks[i - 1].EntityIdNextClosing)
                yield return $"第 {weeks[i].Week} 週配號區間起點 {weeks[i].EntityIdNextOpening}≠上週終點 {weeks[i - 1].EntityIdNextClosing}";
        }
        foreach (var r in weeks.Skip(c.FirstWeekIndex))
        {
            if (r.EntityIdNextClosing < r.EntityIdNextOpening)
                yield return $"第 {r.Week} 週配號區間倒退";
        }
        if (weeks.Count > 0 && weeks[^1].EntityIdNextClosing != next)
            yield return $"最後一週配號終點 {weeks[^1].EntityIdNextClosing}≠分配器下一號 {next}";

        var live = world.Sites.EntityIds()
            .Concat(world.MigrationsInTransit().Select(b => (long)b.Id))
            .Concat(world.HumanPopulationSnapshot?.People.Select(x => x.Id) ?? Enumerable.Empty<long>())
            .ToList();
        var duplicate = live.GroupBy(x => x).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            yield return $"實體 ID {duplicate.Key} 同時被 {duplicate.Count()} 個實體使用";
        var outOfRange = live.Where(x => x < 1 || x >= next).ToList();
        if (outOfRange.Count > 0)
            yield return $"實體 ID {outOfRange[0]} 不在已配發範圍（1～{next - 1}）";
    }

    // ── 歷史：已發布的週報與正式事件只追加不改寫；事件編號從 1 起連續、時間不倒退，與週報事件一一對應 ──
    private static IEnumerable<string> HistoryAppendOnly(DemoInvariantContext c)
    {
        var world = c.World;
        var weeks = world.WeekReports();
        var events = world.EventHistory.Events;
        if (c.Previous is { } p)
        {
            if (weeks.Count < p.Weeks.Count)
                yield return $"週報被刪除：上次 {p.Weeks.Count} 份，現在 {weeks.Count} 份";
            else
            {
                for (var i = 0; i < p.Weeks.Count; i++)
                {
                    if (!ReferenceEquals(weeks[i], p.Weeks[i]))
                    {
                        yield return $"已發布的第 {i + 1} 週週報被替換";
                        break;
                    }
                }
            }
            if (events.Count < p.Events.Count)
                yield return $"正式史庫事件被刪除：上次 {p.Events.Count} 筆，現在 {events.Count} 筆";
            else
            {
                for (var i = 0; i < p.Events.Count; i++)
                {
                    if (!Equals(events[i], p.Events[i]))
                    {
                        yield return $"正式史庫事件 #{p.Events[i].EventId} 被改寫";
                        break;
                    }
                }
            }
        }

        var start = c.Previous is null ? 0 : Math.Min(c.Previous.Events.Count, events.Count);
        for (var i = start; i < events.Count; i++)
        {
            if (events[i].EventId != i + 1)
            {
                yield return $"正式史庫第 {i + 1} 筆事件編號是 #{events[i].EventId}（跳號或重複）";
                break;
            }
            if (i > 0 && events[i].WorldTime < events[i - 1].WorldTime)
            {
                yield return $"正式史庫事件 #{events[i].EventId} 的世界時間倒退";
                break;
            }
        }

        var settlementEvents = weeks.Sum(w => (long)w.Events.Count);
        if (settlementEvents != events.Count)
            yield return $"週報事件 {settlementEvents} 筆，正式史庫 {events.Count} 筆（必須一一對應）";
    }

    // ── 人口：每帳平衡、逐週銜接、目前人口＝最後一週期末；世界人口＝聚落＋途中 ─────────────
    private static IEnumerable<string> PopulationLedger(DemoInvariantContext c)
    {
        var world = c.World;
        var weeks = world.WeekReports();
        for (var i = c.FirstWeekIndex; i < weeks.Count; i++)
        {
            var r = weeks[i];
            var before = Before(weeks, i);
            foreach (var l in r.Settlements)
            {
                if (!l.PopulationBalanced)
                    yield return $"第 {r.Week} 週 {l.SettlementName} 人口帳：{l.PopulationOpening}＋{l.Births}－{l.Deaths}＋{l.Immigrants}－{l.Emigrants}≠{l.PopulationClosing}";
                var expected = before?.Settlements.FirstOrDefault(x => x.SettlementId == l.SettlementId)?.PopulationClosing ?? (i == 0 ? l.PopulationOpening : 0);
                if (l.PopulationOpening != expected)
                    yield return $"第 {r.Week} 週 {l.SettlementName} 期初人口 {l.PopulationOpening}≠上週期末 {expected}";
                if (l.PopulationClosing < 0 || l.NaturalDeaths > l.Deaths)
                    yield return $"第 {r.Week} 週 {l.SettlementName} 人口出現負值或自然死亡多於死亡";
            }
            foreach (var m in r.Migrations.Where(m => !m.PopulationBalanced))
                yield return $"第 {r.Week} 週遷徙隊伍 #{m.BatchId} 人口帳不平衡";
            if (before is not null && before.Settlements.Any(b => r.Settlements.All(l => l.SettlementId != b.SettlementId)))
                yield return $"第 {r.Week} 週有聚落的帳消失（聚落不得刪除）";
        }

        if (weeks.Count > 0)
        {
            var last = weeks[^1];
            foreach (var l in last.Settlements)
            {
                if (world.SettlementPopulation(l.SettlementId) != l.PopulationClosing)
                    yield return $"{l.SettlementName} 目前人口 {world.SettlementPopulation(l.SettlementId)}≠最後一週期末 {l.PopulationClosing}";
            }
        }
        var settled = world.Sites.Settlements.Sum(s => world.SettlementPopulation(s.Id));
        if (world.SettledPopulation != settled || world.WorldPopulation != settled + world.PopulationInTransit)
            yield return $"世界人口 {world.WorldPopulation}≠聚落 {settled}＋途中 {world.PopulationInTransit}";
        if (world.HumanPopulationSnapshot is { } people && people.People.Length != world.WorldPopulation)
            yield return $"人口身分 {people.People.Length} 人≠世界人口 {world.WorldPopulation} 人";
    }

    // ── 資源：資源點存量、糧食、飢餓債逐帳平衡且逐週銜接；無魔法糧食；目前存量＝最後一週期末 ──────
    private static IEnumerable<string> ResourceLedger(DemoInvariantContext c)
    {
        var world = c.World;
        var weeks = world.WeekReports();
        for (var i = c.FirstWeekIndex; i < weeks.Count; i++)
        {
            var r = weeks[i];
            var before = Before(weeks, i);
            foreach (var l in r.Settlements)
            {
                if (!l.ReserveBalanced || !l.FoodBalanced || !l.TotalResourceBalanced || !l.HungerDebtBalanced || !l.NonNegative)
                    yield return $"第 {r.Week} 週 {l.SettlementName} 資源／糧食／飢餓債帳不平衡或出現負值";
                if (!l.NoMagicFood)
                    yield return $"第 {r.Week} 週 {l.SettlementName} 出現魔法糧食：生產 {l.FoodProduced}，採收 {l.Harvested}";
                var b = before?.Settlements.FirstOrDefault(x => x.SettlementId == l.SettlementId);
                if (b is not null && (l.ReserveOpening != b.ReserveClosing || l.FoodOpening != b.FoodClosing
                    || l.HungerDebtOpening != b.HungerDebtClosing))
                    yield return $"第 {r.Week} 週 {l.SettlementName} 存量／糧食／飢餓債期初與上週期末不銜接";
                if (b is null && i > 0 && (l.ReserveOpening != 0 || l.FoodOpening != 0 || l.HungerDebtOpening != 0))
                    yield return $"第 {r.Week} 週新聚落 {l.SettlementName} 期初不是 0";
            }
            foreach (var m in r.Migrations.Where(m => !m.FoodBalanced || !m.NonNegative || m.FoodClosing > m.FoodOpening))
                yield return $"第 {r.Week} 週遷徙隊伍 #{m.BatchId} 糧食帳不平衡或途中憑空增加";
        }

        if (weeks.Count > 0)
        {
            foreach (var l in weeks[^1].Settlements)
            {
                var point = world.Sites.ResourcePointsOf(l.SettlementId).Single();
                if (world.ResourceReserve(point.Id) != l.ReserveClosing || world.SettlementFood(l.SettlementId) != l.FoodClosing
                    || world.SettlementHungerDebt(l.SettlementId) != l.HungerDebtClosing)
                    yield return $"{l.SettlementName} 目前存量／糧食／飢餓債與最後一週期末不一致";
            }
        }
    }

    // ── 來源（共享採集）：Σ份額＋未採＝可採＝期初＋再生；期初＝上週期末；目前來源存量＝最後一週期末 ──
    private static IEnumerable<string> SourceLedger(DemoInvariantContext c)
    {
        var world = c.World;
        var weeks = world.WeekReports();
        for (var i = c.FirstWeekIndex; i < weeks.Count; i++)
        {
            var r = weeks[i];
            if (!world.UsesSharedForaging)
            {
                if (r.Sources.Count != 0)
                    yield return $"第 {r.Week} 週：非共享採集情境出現來源帳";
                continue;
            }
            var previous = Before(weeks, i)?.Sources.ToDictionary(s => s.SourceId, s => s.StockClosing) ?? new Dictionary<int, long>();
            foreach (var s in r.Sources)
            {
                var shares = s.Shares ?? Array.Empty<DemoSourceShare>();
                if (s.Harvested + s.Unharvested != s.FoodAvailable || s.StockOpening + s.Regenerated != s.FoodAvailable
                    || s.Unharvested < 0 || s.Regenerated < 0 || shares.Any(x => x.Food < 0)
                    || shares.Select(x => x.SettlementId).Distinct().Count() != shares.Count)
                    yield return $"第 {r.Week} 週來源 #{s.SourceId}：{s}";
                if (s.StockOpening != previous.GetValueOrDefault(s.SourceId))
                    yield return $"第 {r.Week} 週來源 #{s.SourceId} 期初存量 {s.StockOpening}≠上週期末 {previous.GetValueOrDefault(s.SourceId)}";
            }
            // 上週還有存量的來源本週必須續帳；唯一例外是該格在本週前已被聚落佔用（建村佔格，來源依佔格重算）。
            foreach (var gone in Before(weeks, i)?.Sources.Where(x => x.StockClosing > 0 && r.Sources.All(s => s.SourceId != x.SourceId))
                         ?? Enumerable.Empty<DemoSourceWeekLedger>())
            {
                var occupied = world.Sites.Settlements.Any(x => x.Column == gone.Column && x.Row == gone.Row && x.FoundedWeek <= r.Week);
                if (!occupied)
                    yield return $"第 {r.Week} 週來源 #{gone.SourceId} 上週剩 {gone.StockClosing}，本週帳消失";
            }
            foreach (var l in r.Settlements)
            {
                var taken = r.Sources.SelectMany(s => s.Shares ?? Array.Empty<DemoSourceShare>())
                    .Where(x => x.SettlementId == l.SettlementId).Sum(x => x.Food);
                if (taken != l.Harvested)
                    yield return $"第 {r.Week} 週 {l.SettlementName} 來源份額 {taken}≠聚落採收 {l.Harvested}";
            }
        }

        if (weeks.Count > 0 && world.UsesSharedForaging)
        {
            foreach (var s in weeks[^1].Sources)
            {
                if (world.SourceStock(s.SourceId) != s.StockClosing)
                    yield return $"來源 #{s.SourceId} 目前存量 {world.SourceStock(s.SourceId)}≠最後一週期末 {s.StockClosing}";
            }
        }
    }

    // ── 遷徙：出發＝來源遷出、抵達＝目的地遷入；途中隊伍逐週銜接、只走相鄰格；途中隊伍＝最後一週仍在路上的帳 ──
    private static IEnumerable<string> MigrationConservation(DemoInvariantContext c)
    {
        var world = c.World;
        var weeks = world.WeekReports();
        for (var i = c.FirstWeekIndex; i < weeks.Count; i++)
        {
            var r = weeks[i];
            var travelling = Before(weeks, i)?.Migrations.Where(m => !m.Arrived && m.PopulationClosing > 0)
                .ToDictionary(m => m.BatchId) ?? new Dictionary<int, DemoMigrationWeekLedger>();
            foreach (var m in r.Migrations)
            {
                if (!m.NoTeleport)
                    yield return $"第 {r.Week} 週遷徙隊伍 #{m.BatchId} 瞬移：{m.CellBefore}→{m.CellAfter}";
                if (m.Departed)
                {
                    if (travelling.ContainsKey(m.BatchId))
                        yield return $"第 {r.Week} 週遷徙隊伍 #{m.BatchId} 重複出發";
                    continue;
                }
                if (!travelling.Remove(m.BatchId, out var b))
                    yield return $"第 {r.Week} 週遷徙隊伍 #{m.BatchId} 沒有上週紀錄（憑空出現）";
                else if (b.PopulationClosing != m.PopulationOpening || b.FoodClosing != m.FoodOpening || b.CellAfter != m.CellBefore)
                    yield return $"第 {r.Week} 週遷徙隊伍 #{m.BatchId} 與上週不銜接";
            }
            foreach (var lost in travelling.Keys)
                yield return $"第 {r.Week} 週遷徙隊伍 #{lost} 消失（沒有抵達也沒有全數死亡）";
            foreach (var l in r.Settlements)
            {
                var outbound = r.Migrations.Where(m => m.Departed && m.FromSettlementId == l.SettlementId).Sum(m => m.PopulationOpening);
                var inbound = r.Migrations.Where(m => m.Arrived && m.ToSettlementId == l.SettlementId).Sum(m => m.PopulationClosing);
                if (outbound != l.Emigrants || inbound != l.Immigrants)
                    yield return $"第 {r.Week} 週 {l.SettlementName} 遷出 {l.Emigrants}／出發 {outbound}，遷入 {l.Immigrants}／抵達 {inbound}";
            }
        }

        var expected = weeks.Count == 0
            ? new List<DemoMigrationWeekLedger>()
            : weeks[^1].Migrations.Where(m => !m.Arrived && m.PopulationClosing > 0).OrderBy(m => m.BatchId).ToList();
        var batches = world.MigrationsInTransit().OrderBy(b => b.Id).ToList();
        if (batches.Count != expected.Count
            || batches.Zip(expected).Any(p => p.First.Id != p.Second.BatchId || p.First.People != p.Second.PopulationClosing
                || p.First.Food != p.Second.FoodClosing || p.First.Cell != p.Second.CellAfter))
            yield return $"途中隊伍 {batches.Count} 支與最後一週仍在路上的帳 {expected.Count} 支不一致";
    }
}
