using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// #97：TEMP 存檔週帳「全鏈」防竄改。存檔點在第 735 日（跨過兩個 365 日年界），
/// 竄改目標優先取最晚一週（第 2 年以後）。每一種竄改都是「整段改寫」：
/// 刪除或插入事件後把後面所有事件編號與直接原因編號一起補正，並重算存檔裡的世界指紋，
/// 讓檔案不會只因為編號跳號或指紋不符而被擋下——必須靠週帳、隊伍帳、事件與因果的跨表核對抓到。
/// 竄改後一律拒絕讀取，且執行中的世界不變；未竄改的同一份存檔可讀、讀入後與存檔相同。
/// 限制（TEMP，明列）：一份從竄改點起每一週都依規則重新算過、所有數字都自洽的偽造週帳，
/// 只有重播週結算才能抓到；直接恢復（第 2 版）不重播週結算，正式存檔的完整驗證仍待正式規格。
/// </summary>
public sealed class DemoSaveLedgerTamperTests
{
    private const int SaveDay = 735; // 第 3 年第 5 日：已跨過第 365、730 日兩個年界。
    private const int FirstWeekOfYear2 = 53; // 第 371 日的週結算。

    private static readonly (string Name, DemoWeeklyTempConfig Config)[] PresetOrder =
    {
        ("局部饑荒", DemoWeeklyTempConfig.LocalFamineSample),
        ("開拓", DemoWeeklyTempConfig.FrontierSample),
        ("全面饑荒", DemoWeeklyTempConfig.FamineSample),
        ("一般", DemoWeeklyTempConfig.Sample)
    };

    private static readonly ulong[] Seeds = { 42, 7, 1, 2, 3, 4, 5 };

    private static readonly Dictionary<(string, ulong), string> SaveCache = new();

    private static string SaveAt(string preset, DemoWeeklyTempConfig config, ulong seed)
    {
        lock (SaveCache)
        {
            if (!SaveCache.TryGetValue((preset, seed), out var json))
            {
                var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
                for (var day = 0; day < SaveDay; day++)
                    world.AdvanceOneDay();
                SaveCache[(preset, seed)] = json = world.ExportTempSave();
            }

            return json;
        }
    }

    /// <summary>
    /// 依情境與種子順序找含有竄改目標的第 735 日存檔：先找目標落在第 2 年以後（第 53 週起）的存檔，
    /// 都沒有才退而接受第 1 年的目標（存檔本身仍跨過兩個年界，讀檔時整條週帳鏈都要核對）。
    /// </summary>
    private static (string Preset, ulong Seed, string Json, int TargetWeek) FindSave(Func<JsonNode, JsonNode?> target, string what)
    {
        foreach (var minWeek in new[] { FirstWeekOfYear2, 1 })
        {
            foreach (var (preset, config) in PresetOrder)
            {
                foreach (var seed in Seeds)
                {
                    var json = SaveAt(preset, config, seed);
                    if (target(JsonNode.Parse(json)!) is { } node && WeekOf(node) >= minWeek)
                        return (preset, seed, json, WeekOf(node));
                }
            }
        }

        throw new InvalidOperationException($"找不到含有「{what}」的第 {SaveDay} 日存檔。");
    }

    // ── JSON 小工具 ──

    private static JsonArray Weeks(JsonNode doc) => doc["State"]!["WeekReports"]!.AsArray();

    private static int WeekOf(JsonNode node) => node["Week"]!.GetValue<int>();

    private static long L(JsonNode node, string name) => node[name]!.GetValue<long>();

    private static int I(JsonNode node, string name) => node[name]!.GetValue<int>();

    private static bool B(JsonNode node, string name) => node[name]!.GetValue<bool>();

    private static void Add(JsonNode node, string name, long delta) => node[name] = L(node, name) + delta;

    private static DemoSettlementEventKind Kind(JsonNode e) => (DemoSettlementEventKind)I(e, "Kind");

    private static List<JsonNode> AllEvents(JsonNode doc)
        => Weeks(doc).SelectMany(w => w!["Events"]!.AsArray()).Select(e => e!).ToList();

    private static List<JsonNode> AllMigrations(JsonNode doc)
        => Weeks(doc).SelectMany(w => w!["Migrations"]!.AsArray()).Select(m => m!).ToList();

    private static JsonNode WeekNode(JsonNode doc, int week) => Weeks(doc)[week - 1]!;

    private static JsonNode SettlementLedger(JsonNode doc, int week, int settlementId)
        => WeekNode(doc, week)["Settlements"]!.AsArray().Single(l => I(l!, "SettlementId") == settlementId)!;

    private static int LastWeek(JsonNode doc) => Weeks(doc).Count;

    /// <summary>最晚一筆符合條件者（存檔在第 735 日，最晚一筆通常落在第 2 年以後）。</summary>
    private static JsonNode? Latest(IEnumerable<JsonNode> items, Func<JsonNode, bool> match)
        => items.Where(match).OrderBy(WeekOf).LastOrDefault();

    private static bool Referenced(JsonNode doc, long eventId) => AllEvents(doc).Any(e => L(e, "CauseEventId") == eventId);

    private static JsonNode EventOfBatch(JsonNode doc, DemoSettlementEventKind kind, int batchId)
        => AllEvents(doc).Single(e => Kind(e) == kind && I(e, "MigrationBatchId") == batchId);

    /// <summary>刪除一筆事件，後面的事件編號與直接原因編號全部往前補（整段改寫，不留跳號）。</summary>
    private static void DeleteEventAndRenumber(JsonNode doc, JsonNode target)
    {
        var id = L(target, "EventId");
        Assert.True(WeekNode(doc, WeekOf(target))["Events"]!.AsArray().Remove(target));
        foreach (var e in AllEvents(doc))
        {
            if (L(e, "EventId") > id) Add(e, "EventId", -1);
            if (L(e, "CauseEventId") > id) Add(e, "CauseEventId", -1);
        }
    }

    /// <summary>在原事件後面插入一份複本（新編號＝原編號＋1），後面的編號與原因編號全部往後補。</summary>
    private static void DuplicateEventAndRenumber(JsonNode doc, JsonNode target)
    {
        var id = L(target, "EventId");
        foreach (var e in AllEvents(doc))
        {
            if (L(e, "EventId") > id) Add(e, "EventId", 1);
            if (L(e, "CauseEventId") > id) Add(e, "CauseEventId", 1);
        }

        var events = WeekNode(doc, WeekOf(target))["Events"]!.AsArray();
        var copy = JsonNode.Parse(target.ToJsonString())!;
        copy["EventId"] = id + 1;
        events.Insert(events.IndexOf(target) + 1, copy);
    }

    /// <summary>
    /// 依竄改後的週報重算存檔裡的世界指紋（週報以外的部分沿用原指紋），
    /// 讓偽造檔不會只因指紋過期而被擋下。格式與 <c>DemoObserverWorld.StateFingerprint</c> 的週報段相同。
    /// </summary>
    internal static void RecomputeFingerprint(JsonNode doc)
    {
        var state = doc["State"]!;
        var fingerprint = state["WorldFingerprint"]!.GetValue<string>();
        var start = fingerprint.IndexOf(";week1@", StringComparison.Ordinal) + 1;
        Assert.True(start > 0, "世界指紋找不到週報段");

        var inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(fingerprint[..start]);
        foreach (var week in Weeks(doc).Select(w => w!))
        {
            sb.Append(inv, $"week{WeekOf(week)}@{week["WorldTime"]!.Deserialize<TimeSpan>().Ticks}:ids{week["EntityIdNextOpening"]!.GetValue<long>()}-{week["EntityIdNextClosing"]!.GetValue<long>()};");
            foreach (var c in week["RngCounters"]!.Deserialize<List<DemoRngCounter>>()!) sb.Append(inv, $"rngc{c.StreamId}={c.Before}-{c.After};");
            foreach (var ledger in week["Settlements"]!.Deserialize<List<DemoSettlementWeekLedger>>()!) sb.Append(inv, $"{ledger};");
            foreach (var e in week["Events"]!.Deserialize<List<DemoSettlementEvent>>()!) sb.Append(inv, $"{e};");
            foreach (var m in week["Migrations"]!.Deserialize<List<DemoMigrationWeekLedger>>()!) sb.Append(inv, $"{m};");
            foreach (var x in week["Sources"]!.Deserialize<List<DemoSourceWeekLedger>>()!) sb.Append(inv, $"{x};");
        }
        // 第 03 章 b 節：指紋尾段是正式史庫（沿用檔內史庫；史庫本身另由讀檔重建比對）。
        foreach (var e in state["History"]!.Deserialize<List<WorldSimulation.Core.History.WorldEvent>>()!)
            sb.Append(inv, $"{e.Fingerprint()};");

        state["WorldFingerprint"] = sb.ToString();
        // 第 04 章 b 節：連標頭校驗碼一起補正，讓偽造檔不會只因校驗碼被擋下，仍需靠內容驗證拒絕。
        doc["Manifest"]!["StateChecksum"] = WorldSimulation.Core.World.DemoSaveHeader.Checksum(
            Encoding.UTF8.GetBytes(state.ToJsonString()));
    }

    // ── 竄改目錄：名稱 →（存檔需含的目標、竄改方式）。全部要被拒絕。 ──

    private sealed record Tampering(string What, Func<JsonNode, JsonNode?> Target, Action<JsonNode> Apply);

    private static JsonNode? ContinuingDeparture(JsonNode doc)
        => Latest(AllMigrations(doc), m => B(m, "Departed") && WeekOf(m) < LastWeek(doc)
            && SettlementLedger(doc, WeekOf(m), I(m, "FromSettlementId"))["PopulationClosing"]!.GetValue<long>() >= 1
            && AllMigrations(doc).Any(n => !B(n, "Departed") && WeekOf(n) == WeekOf(m) + 1 && I(n, "BatchId") == I(m, "BatchId")));

    private static JsonNode? Departure(JsonNode doc) => Latest(AllMigrations(doc), m => B(m, "Departed"));

    private static JsonNode? Arrival(JsonNode doc)
        => Latest(AllMigrations(doc), m => !B(m, "Departed") && B(m, "Arrived") && !B(m, "Founding"));

    private static JsonNode? DeathLedger(JsonNode doc)
        => Latest(Weeks(doc).SelectMany(w => w!["Settlements"]!.AsArray()).Select(l => l!),
            l => L(l, "Deaths") > 0 && WeekOf(l) < LastWeek(doc) && L(l, "PopulationClosing") >= 1);

    private static JsonNode? DeathEvent(JsonNode doc)
        => Latest(AllEvents(doc), e => Kind(e) == DemoSettlementEventKind.StarvationDeath);

    private static JsonNode? UnreferencedHunger(JsonNode doc)
        => Latest(AllEvents(doc), e => Kind(e) == DemoSettlementEventKind.Hunger && !Referenced(doc, L(e, "EventId")));

    private static JsonNode? LatestHunger(JsonNode doc)
        => Latest(AllEvents(doc), e => Kind(e) == DemoSettlementEventKind.Hunger);

    private static JsonNode? Recovery(JsonNode doc)
        => Latest(AllEvents(doc), e => Kind(e) == DemoSettlementEventKind.HungerRecovered);

    /// <summary>有更早一筆同聚落飢餓可以冒充原因的餓死／飢餓解除事件。</summary>
    private static (JsonNode Effect, JsonNode OlderHunger)? EffectWithOlderHunger(JsonNode doc)
    {
        var events = AllEvents(doc);
        foreach (var e in events.Where(e => Kind(e) is DemoSettlementEventKind.StarvationDeath or DemoSettlementEventKind.HungerRecovered)
                     .OrderByDescending(WeekOf))
        {
            var older = events.LastOrDefault(h => Kind(h) == DemoSettlementEventKind.Hunger
                && I(h, "SettlementId") == I(e, "SettlementId") && L(h, "EventId") < L(e, "CauseEventId"));
            if (older is not null)
                return (e, older);
        }

        return null;
    }

    /// <summary>可以冒充原因、但屬於別的聚落的更早飢餓事件。</summary>
    private static (JsonNode Effect, JsonNode ForeignHunger)? DeathWithForeignHunger(JsonNode doc)
    {
        var events = AllEvents(doc);
        foreach (var e in events.Where(e => Kind(e) == DemoSettlementEventKind.StarvationDeath).OrderByDescending(WeekOf))
        {
            var foreign = events.LastOrDefault(h => Kind(h) == DemoSettlementEventKind.Hunger
                && I(h, "SettlementId") != I(e, "SettlementId") && L(h, "EventId") < L(e, "EventId"));
            if (foreign is not null)
                return (e, foreign);
        }

        return null;
    }

    /// <summary>可以冒充原因、但屬於別支隊伍的更早外移事件。</summary>
    private static (JsonNode Effect, JsonNode OtherEmigration)? TravelWithOtherBatch(JsonNode doc)
    {
        var events = AllEvents(doc);
        foreach (var e in events.Where(e => Kind(e) is DemoSettlementEventKind.Arrival or DemoSettlementEventKind.SettlementFounded
                         or DemoSettlementEventKind.MigrantStarvationDeath).OrderByDescending(WeekOf))
        {
            var other = events.LastOrDefault(m => Kind(m) == DemoSettlementEventKind.Emigration
                && I(m, "MigrationBatchId") != I(e, "MigrationBatchId") && WeekOf(m) < WeekOf(e));
            if (other is not null)
                return (e, other);
        }

        return null;
    }

    private static readonly Dictionary<string, Tampering> Catalog = new()
    {
        // 出發
        ["出發帳刪除（連同外移事件，編號補正）"] = new("出發", Departure, d =>
        {
            var m = Departure(d)!;
            DeleteEventAndRenumber(d, EventOfBatch(d, DemoSettlementEventKind.Emigration, I(m, "BatchId")));
            Assert.True(WeekNode(d, WeekOf(m))["Migrations"]!.AsArray().Remove(m));
        }),
        ["出發帳複製"] = new("出發", Departure, d =>
        {
            var m = Departure(d)!;
            WeekNode(d, WeekOf(m))["Migrations"]!.AsArray().Add(JsonNode.Parse(m.ToJsonString()));
        }),
        ["外移事件複製（編號補正）"] = new("出發", Departure, d =>
            DuplicateEventAndRenumber(d, EventOfBatch(d, DemoSettlementEventKind.Emigration, I(Departure(d)!, "BatchId")))),
        ["出發人數整週自洽改寫（隊伍帳＋外移事件＋來源聚落帳）"] = new("之後仍在途中的出發", ContinuingDeparture, d =>
        {
            var m = ContinuingDeparture(d)!;
            Add(m, "PopulationOpening", 1);
            Add(m, "PopulationClosing", 1);
            Add(EventOfBatch(d, DemoSettlementEventKind.Emigration, I(m, "BatchId")), "Count", 1);
            var source = SettlementLedger(d, WeekOf(m), I(m, "FromSettlementId"));
            Add(source, "Emigrants", 1);
            Add(source, "PopulationClosing", -1);
        }),

        // 抵達
        ["抵達帳刪除（連同抵達事件，編號補正）"] = new("抵達既有聚落", Arrival, d =>
        {
            var m = Arrival(d)!;
            DeleteEventAndRenumber(d, EventOfBatch(d, DemoSettlementEventKind.Arrival, I(m, "BatchId")));
            Assert.True(WeekNode(d, WeekOf(m))["Migrations"]!.AsArray().Remove(m));
        }),
        ["抵達帳複製"] = new("抵達既有聚落", Arrival, d =>
        {
            var m = Arrival(d)!;
            WeekNode(d, WeekOf(m))["Migrations"]!.AsArray().Add(JsonNode.Parse(m.ToJsonString()));
        }),
        ["抵達事件複製（編號補正）"] = new("抵達既有聚落", Arrival, d =>
            DuplicateEventAndRenumber(d, EventOfBatch(d, DemoSettlementEventKind.Arrival, I(Arrival(d)!, "BatchId")))),
        ["抵達人數整週自洽改寫（隊伍帳＋抵達事件＋目的聚落帳）"] = new("抵達既有聚落", Arrival, d =>
        {
            var m = Arrival(d)!;
            Add(m, "PopulationOpening", 1);
            Add(m, "PopulationClosing", 1);
            Add(EventOfBatch(d, DemoSettlementEventKind.Arrival, I(m, "BatchId")), "Count", 1);
            var target = SettlementLedger(d, WeekOf(m), I(m, "ToSettlementId"));
            Add(target, "Immigrants", 1);
            Add(target, "PopulationClosing", 1);
        }),

        // 死亡
        ["餓死事件刪除（編號補正）"] = new("餓死", DeathEvent, d => DeleteEventAndRenumber(d, DeathEvent(d)!)),
        ["餓死事件複製（編號補正）"] = new("餓死", DeathEvent, d => DuplicateEventAndRenumber(d, DeathEvent(d)!)),
        ["死亡人數整週自洽改寫（聚落帳＋餓死事件）"] = new("非最後一週的餓死", DeathLedger, d =>
        {
            var l = DeathLedger(d)!;
            Add(l, "Deaths", 1);
            Add(l, "PopulationClosing", -1);
            var e = AllEvents(d).Single(x => Kind(x) == DemoSettlementEventKind.StarvationDeath
                && WeekOf(x) == WeekOf(l) && I(x, "SettlementId") == I(l, "SettlementId"));
            Add(e, "Count", 1);
        }),

        // 歷史原因
        ["沒有後果的飢餓事件刪除（編號補正）"] = new("沒有後果的飢餓", UnreferencedHunger,
            d => DeleteEventAndRenumber(d, UnreferencedHunger(d)!)),
        ["飢餓解除事件刪除（編號補正）"] = new("飢餓解除", Recovery, d => DeleteEventAndRenumber(d, Recovery(d)!)),
        ["原因改指同聚落更早的飢餓"] = new("可冒充原因的更早飢餓", d => EffectWithOlderHunger(d)?.Effect, d =>
        {
            var (effect, older) = EffectWithOlderHunger(d)!.Value;
            effect["CauseEventId"] = L(older, "EventId");
        }),
        ["餓死原因改指別的聚落的飢餓"] = new("別聚落的更早飢餓", d => DeathWithForeignHunger(d)?.Effect, d =>
        {
            var (effect, foreign) = DeathWithForeignHunger(d)!.Value;
            effect["CauseEventId"] = L(foreign, "EventId");
        }),
        ["隊伍事件原因改指別支隊伍的外移"] = new("兩支以上的隊伍", d => TravelWithOtherBatch(d)?.Effect, d =>
        {
            var (effect, other) = TravelWithOtherBatch(d)!.Value;
            effect["CauseEventId"] = L(other, "EventId");
        }),
        ["外移原因清空"] = new("出發", Departure, d =>
            EventOfBatch(d, DemoSettlementEventKind.Emigration, I(Departure(d)!, "BatchId"))["CauseEventId"] = 0),

        // 人數
        ["飢餓事件缺糧數改寫"] = new("飢餓", LatestHunger, d => Add(LatestHunger(d)!, "Count", 1)),
        ["外移事件人數改寫"] = new("出發", Departure, d =>
            Add(EventOfBatch(d, DemoSettlementEventKind.Emigration, I(Departure(d)!, "BatchId")), "Count", 1)),
        ["抵達事件人數改寫"] = new("抵達既有聚落", Arrival, d =>
            Add(EventOfBatch(d, DemoSettlementEventKind.Arrival, I(Arrival(d)!, "BatchId")), "Count", 1)),
        ["餓死事件人數改寫"] = new("餓死", DeathEvent, d => Add(DeathEvent(d)!, "Count", 1))
    };

    public static TheoryData<string> Tamperings
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var name in Catalog.Keys)
                data.Add(name);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Tamperings))]
    public void WholeChainTampering_IsRejected_AndTheRunningWorldIsUnchanged(string name)
    {
        var tampering = Catalog[name];
        var (preset, seed, original, targetWeek) = FindSave(tampering.Target, tampering.What);

        // 同一份未竄改的存檔可讀，讀入後與存檔完全相同（基準本身合法）。
        var live = DemoObserverWorld.ImportTempSave(original);
        Assert.Equal(original, live.ExportTempSave());

        var doc = JsonNode.Parse(original)!;
        tampering.Apply(doc);
        RecomputeFingerprint(doc);
        var forged = doc.ToJsonString();
        Assert.NotEqual(original, forged);

        var error = Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(forged));
        Assert.Contains("TEMP 存檔", error.Message);
        Assert.True(original == live.ExportTempSave(), $"{preset}／種子 {seed}／第 {targetWeek} 週｜{name}：讀檔失敗卻改動了執行中的世界");
    }

    [Fact]
    public void RecomputedFingerprintOfAnUntouchedSaveIsIdentical()
    {
        var original = SaveAt("開拓", DemoWeeklyTempConfig.FrontierSample, 42);
        var doc = JsonNode.Parse(original)!;
        RecomputeFingerprint(doc);
        Assert.Equal(original, doc.ToJsonString());
    }

    /// <summary>
    /// #97 新增的反向核對（聚落帳 → 飢餓／餓死／飢餓解除事件筆數）：即使編號連續、原因合法，
    /// 漏記或拆分也要被 <see cref="DemoCausalIndex"/> 指出；真實世界則一筆問題都沒有。
    /// </summary>
    [Fact]
    public void CausalIndexFlagsMissingOrSplitSettlementEvents()
    {
        var (_, _, json, _) = FindSave(UnreferencedHunger, "沒有後果的飢餓");
        var world = DemoObserverWorld.ImportTempSave(json);
        var weeks = world.WeekReports();
        Assert.Empty(DemoCausalIndex.Build(weeks).Problems);

        var events = weeks.SelectMany(w => w.Events).ToList();
        var referenced = events.Select(e => e.CauseEventId).ToHashSet();
        var hunger = events.Last(e => e.Kind == DemoSettlementEventKind.Hunger && !referenced.Contains(e.EventId));

        // 刪掉這筆飢餓並把後面的編號補正：編號連續、每個原因仍指向合法事件，只有反向核對抓得到。
        var forged = weeks.Select(w => w with
        {
            Events = w.Events.Where(e => e != hunger).Select(e => e with
            {
                EventId = e.EventId > hunger.EventId ? e.EventId - 1 : e.EventId,
                CauseEventId = e.CauseEventId > hunger.EventId ? e.CauseEventId - 1 : e.CauseEventId
            }).ToArray()
        }).ToList();
        var problems = DemoCausalIndex.Build(forged).Problems;
        Assert.Single(problems);
        Assert.Contains("應有 1 筆飢餓事件，實際 0 筆", problems[0]);
    }
}
