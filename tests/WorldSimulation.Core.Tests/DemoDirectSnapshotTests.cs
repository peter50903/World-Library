using System.Text.Json.Nodes;
using Xunit;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.Tests;

/// <summary>V0-07B：TEMP 存檔直接恢復（C05 起第 4 版）（不從第 0 天重播週結算），並在安裝前驗證。</summary>
public sealed class DemoDirectSnapshotTests
{
    /// <summary>推進到「有隊伍在途中、且已建立新聚落」的那一天（開拓情境），回傳世界與天數。</summary>
    private static (DemoObserverWorld World, ulong Seed, int Day) FrontierWorldWithTransitAndFoundedSettlement()
    {
        foreach (var seed in new ulong[] { 42, 7, 1, 2, 3, 4, 5, 6, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 })
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.FrontierSample);
            for (var day = 1; day <= 728; day++)
            {
                world.AdvanceOneDay();
                if (world.MigrationsInTransit().Count > 0 && world.Sites.Settlements.Any(s => s.IsFounded))
                    return (world, seed, day);
            }
        }

        throw new InvalidOperationException("開拓情境找不到同時有途中隊伍與新聚落的種子。");
    }

    [Fact]
    public void NewSaves_AreVersion13()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42));
        world.AdvanceOneDay();
        Assert.Equal(13, JsonNode.Parse(world.ExportTempSave())!["Version"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(42UL, "Sample", 5, 40)]
    [InlineData(7UL, "Sample", 7, 50)]
    [InlineData(42UL, "Famine", 21, 70)]
    [InlineData(7UL, "LocalFamine", 30, 90)]
    [InlineData(42UL, "Frontier", 14, 84)]
    [InlineData(42UL, "Frontier", 35, 120)]
    public void DirectRestoreAndContinue_MatchesUninterruptedWorld(
        ulong seed, string preset, int checkpointDay, int lastDay)
    {
        var config = Preset(preset);
        var direct = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        var saving = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        for (var i = 0; i < checkpointDay; i++)
        {
            direct.AdvanceOneDay();
            saving.AdvanceOneDay();
        }

        var json = saving.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(json);
        Assert.Equal(json, loaded.ExportTempSave());
        Assert.Equal(json, DemoObserverWorld.ImportTempSave(json).ExportTempSave()); // 讀兩次結果相同

        for (var i = checkpointDay; i < lastDay; i++)
        {
            direct.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }

        Assert.Equal(direct.ExportTempSave(), loaded.ExportTempSave());
    }

    [Fact]
    public void TransitBatchesAndFoundedSettlements_RestoreDirectlyAndContinueIdentically()
    {
        var (saving, seed, day) = FrontierWorldWithTransitAndFoundedSettlement();
        var direct = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.FrontierSample);
        for (var i = 0; i < day; i++)
            direct.AdvanceOneDay();

        var loaded = DemoObserverWorld.ImportTempSave(saving.ExportTempSave());
        Assert.Equal(saving.MigrationsInTransit().Count, loaded.MigrationsInTransit().Count);
        Assert.Equal(saving.Sites.Settlements, loaded.Sites.Settlements);
        for (var i = 0; i < 70; i++)
        {
            direct.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }

        Assert.Equal(direct.ExportTempSave(), loaded.ExportTempSave());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void OlderSaveVersions_AreRejectedAsTooOld(int version)
    {
        // C04／C05：比目前第 13 版舊的存檔一律明確拒讀（使用者 2026-10-14 裁決），不重播、不轉換。
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.FrontierSample);
        for (var i = 0; i < 30; i++)
            world.AdvanceOneDay();
        var old = JsonNode.Parse(world.ExportTempSave())!;
        old["Version"] = version;

        var ex = Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(old.ToJsonString()));
        Assert.Contains("版本太舊，請建立新世界", ex.Message);
    }

    [Fact]
    public void TwentyYearSave_RestoresDirectly()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(7), DemoWeeklyTempConfig.FrontierSample);
        for (var i = 0; i < 7280; i++)
            world.AdvanceOneDay();
        var json = world.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(json);
        Assert.Equal(json, loaded.ExportTempSave());
        loaded.AdvanceOneDay();
        world.AdvanceOneDay();
        Assert.Equal(world.ExportTempSave(), loaded.ExportTempSave());
    }

    public static TheoryData<string> Tamperings => new()
    {
        "week-rng-before", "week-rng-missing", "week-rng-order", "week-rng-after", "scheduler-sequence", "rng-counter", "drop-last-week", "week-number",
        "ledger-population", "ledger-and-stock-population", "reserve-stock", "food-stock",
        "next-batch-id", "batch-people", "batch-step", "batch-path", "event-settlement",
        "founded-week", "founded-id", "extra-settlement", "resource-point", "week-time",
        "event-id", "event-cause-missing", "event-cause-future", "legacy-without-event-ids"
    };

    [Theory]
    [MemberData(nameof(Tamperings))]
    public void TamperedSnapshot_IsRejectedAndLiveWorldIsUnchanged(string tampering)
    {
        var (live, _, _) = FrontierWorldWithTransitAndFoundedSettlement();
        var original = live.ExportTempSave();
        var doc = JsonNode.Parse(original)!;
        var state = doc["State"]!;
        var weeks = state["WeekReports"]!.AsArray();
        var lastWeek = weeks[weeks.Count - 1]!;
        var lastLedger = lastWeek["Settlements"]![0]!;
        var settlements = state["Settlements"]!.AsArray();
        var founded = settlements[settlements.Count - 1]!;
        var batch = state["MigrationBatches"]![0]!;

        switch (tampering)
        {
            // C05：每週十條亂數流計數器（提交前＝上週提交後、順序與數量固定、最後提交後＝Random）。
            case "week-rng-before":
                lastWeek["RngCounters"]![0]!["Before"] = lastWeek["RngCounters"]![0]!["Before"]!.GetValue<ulong>() + 1;
                break;
            case "week-rng-missing":
                lastWeek["RngCounters"]!.AsArray().RemoveAt(9);
                break;
            case "week-rng-order":
            {
                var counters = lastWeek["RngCounters"]!.AsArray();
                var first = counters[0]!.DeepClone();
                counters[0] = counters[1]!.DeepClone();
                counters[1] = first;
                break;
            }
            case "week-rng-after":
                lastWeek["RngCounters"]![4]!["After"] = lastWeek["RngCounters"]![4]!["After"]!.GetValue<ulong>() + 1;
                break;
            case "scheduler-sequence":
                state["SchedulerNextSequence"] = state["SchedulerNextSequence"]!.GetValue<long>() + 1;
                break;
            case "rng-counter":
                state["Random"]!["Streams"]![0]!["DrawCounter"] = state["Random"]!["Streams"]![0]!["DrawCounter"]!.GetValue<ulong>() + 1;
                break;
            case "drop-last-week":
                weeks.RemoveAt(weeks.Count - 1);
                break;
            case "week-number":
                lastWeek["Week"] = 999;
                break;
            case "week-time":
                lastWeek["WorldTime"] = "1.00:00:00";
                break;
            case "ledger-population":
                lastLedger["PopulationClosing"] = lastLedger["PopulationClosing"]!.GetValue<long>() + 1;
                break;
            case "ledger-and-stock-population":
                // 帳本身仍平衡、目前人口也跟著改，但期初與上週期末不銜接（憑空多一人）。
                lastLedger["PopulationOpening"] = lastLedger["PopulationOpening"]!.GetValue<long>() + 1;
                lastLedger["PopulationClosing"] = lastLedger["PopulationClosing"]!.GetValue<long>() + 1;
                var id = lastLedger["SettlementId"]!.GetValue<int>().ToString();
                state["Population"]![id] = state["Population"]![id]!.GetValue<long>() + 1;
                break;
            case "reserve-stock":
                var reserveKey = state["Reserves"]!.AsObject().First().Key;
                state["Reserves"]![reserveKey] = state["Reserves"]![reserveKey]!.GetValue<long>() + 1;
                break;
            case "food-stock":
                var foodKey = state["SettlementFood"]!.AsObject().First().Key;
                state["SettlementFood"]![foodKey] = -1;
                break;
            case "next-batch-id":
                state["EntityIds"]!["NextValue"] = state["EntityIds"]!["NextValue"]!.GetValue<ulong>() + 1;
                break;
            case "batch-people":
                batch["People"] = batch["People"]!.GetValue<long>() + 1;
                break;
            case "batch-step":
                batch["StepIndex"] = batch["StepIndex"]!.GetValue<int>() + 1;
                break;
            case "batch-path":
                batch["Path"]!.AsArray().RemoveAt(0);
                break;
            case "event-settlement":
                var withEvents = weeks.Last(w => w!["Events"]!.AsArray().Count > 0)!;
                withEvents["Events"]![0]!["SettlementId"] = 99;
                break;
            // #88：事件編號與直接原因（43F CAUSE_PARENT）也是存檔證據的一部分。
            case "event-id":
                var numbered = weeks.Last(w => w!["Events"]!.AsArray().Count > 0)!;
                numbered["Events"]![0]!["EventId"] = numbered["Events"]![0]!["EventId"]!.GetValue<long>() + 1;
                break;
            case "event-cause-missing":
                AllEvents(weeks).First(x => x["Kind"]!.GetValue<int>() == (int)DemoSettlementEventKind.Emigration)["CauseEventId"] = 0;
                break;
            case "event-cause-future":
                var effect = AllEvents(weeks).First(x => x["CauseEventId"]!.GetValue<long>() > 0);
                effect["CauseEventId"] = effect["EventId"]!.GetValue<long>() + 1;
                break;
            case "legacy-without-event-ids":
                foreach (var legacy in AllEvents(weeks))
                {
                    legacy.AsObject().Remove("EventId");
                    legacy.AsObject().Remove("CauseEventId");
                }

                break;
            case "founded-week":
                founded["FoundedWeek"] = weeks.Count + 5;
                break;
            case "founded-id":
                founded["Id"] = founded["Id"]!.GetValue<int>() + 1;
                break;
            case "extra-settlement":
                var copy = JsonNode.Parse(founded.ToJsonString())!;
                copy["Id"] = founded["Id"]!.GetValue<int>() + 1;
                copy["Column"] = (founded["Column"]!.GetValue<int>() + 1) % DemoHexWorld.Columns;
                settlements.Add(copy);
                break;
            case "resource-point":
                state["ResourcePoints"]![0]!["Kind"] = (state["ResourcePoints"]![0]!["Kind"]!.GetValue<int>() + 1) % 3;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(tampering));
        }

        // 第 04 章 b 節：補正校驗碼，確認拒絕來自內容驗證而不只是校驗碼。
        doc["Manifest"]!["StateChecksum"] = DemoSaveHeader.Checksum(System.Text.Encoding.UTF8.GetBytes(state.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
        Assert.Equal(original, live.ExportTempSave());
    }

    [Fact]
    public void TruncatedSnapshot_IsRejected()
    {
        var (live, _, _) = FrontierWorldWithTransitAndFoundedSettlement();
        var original = live.ExportTempSave();
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(original[..(original.Length * 2 / 3)]));
        Assert.Equal(original, live.ExportTempSave());
    }

    private static List<JsonNode> AllEvents(JsonArray weeks)
        => weeks.SelectMany(w => w!["Events"]!.AsArray()).Select(e => e!).ToList();

    private static DemoWeeklyTempConfig Preset(string name) => name switch
    {
        "Famine" => DemoWeeklyTempConfig.FamineSample,
        "LocalFamine" => DemoWeeklyTempConfig.LocalFamineSample,
        "Frontier" => DemoWeeklyTempConfig.FrontierSample,
        _ => DemoWeeklyTempConfig.Sample
    };
}
