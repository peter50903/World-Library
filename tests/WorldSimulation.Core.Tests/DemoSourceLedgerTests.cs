using System.Text.Json.Nodes;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 04 章 c 節：共享採集來源層級週帳。</summary>
public sealed class DemoSourceLedgerTests
{
    private static readonly Dictionary<string, DemoWeeklyTempConfig> Shared = new()
    {
        ["ForagingSample"] = DemoWeeklyTempConfig.ForagingSample,
        ["LivingForagingSample"] = DemoWeeklyTempConfig.LivingForagingSample,
        ["CrowdedForagingSample"] = DemoWeeklyTempConfig.CrowdedForagingSample
    };

    public static TheoryData<string, ulong> Matrix()
    {
        var data = new TheoryData<string, ulong>();
        foreach (var name in Shared.Keys)
            for (ulong seed = 1; seed <= 5; seed++)
                data.Add(name, seed);
        return data;
    }

    private static DemoObserverWorld Run(DemoWeeklyTempConfig config, ulong seed, int days)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        for (var i = 0; i < days; i++)
            world.AdvanceOneDay();
        return world;
    }

    [Theory]
    [MemberData(nameof(Matrix))]
    public void TwoYears_EverySourceConserves_AndMatchesTheRecomputedAllocation(string config, ulong seed)
    {
        var weekly = Shared[config];
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), weekly);
        for (var day = 1; day <= 2 * 365; day++)
        {
            var advance = world.AdvanceOneDay();
            if (advance.SettledWeek is not { } week)
                continue;
            Assert.NotEmpty(week.Sources);
            foreach (var source in week.Sources)
                Assert.Equal(source.FoodAvailable, source.Shares.Sum(x => x.Food) + source.Unharvested);
            foreach (var ledger in week.Settlements)
                Assert.Equal(week.Sources.SelectMany(x => x.Shares).Where(x => x.SettlementId == ledger.SettlementId).Sum(x => x.Food),
                    ledger.Harvested);

            // 帳＝依地圖與本週期初人口重算的分配，逐筆相同。
            var recomputed = world.RecomputeLatestSharedHarvest()!;
            var expected = DemoSourceWeekLedger.From(week.Week, DemoForagingWorld.Sources(world.Map, world.Sites), recomputed);
            Assert.Equal(expected, week.Sources);
            var fromLedger = world.LatestSharedHarvest()!;
            Assert.Equal(recomputed.Harvests.OrderBy(h => h.SourceId).ThenBy(h => h.SettlementId),
                fromLedger.Harvests.OrderBy(h => h.SourceId).ThenBy(h => h.SettlementId));
        }
    }

    [Fact]
    public void NonSharedScenarios_HaveNoSourceLedger()
    {
        foreach (var config in new[] { DemoWeeklyTempConfig.Sample, DemoWeeklyTempConfig.FrontierSample, DemoWeeklyTempConfig.FamineSample })
            Assert.All(Run(config, 3, 70).WeekReports(), w => Assert.Empty(w.Sources));
    }

    [Fact]
    public void SaveLoad_RoundTripsTheSourceLedger_AndResumesIdentically()
    {
        var world = Run(DemoWeeklyTempConfig.LivingForagingSample, 2, 400);
        var json = world.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(json);
        Assert.Equal(world.WeekReports().Select(w => w.Sources), loaded.WeekReports().Select(w => w.Sources));
        for (var i = 0; i < 30; i++)
        {
            world.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(world.ExportTempSave(), loaded.ExportTempSave());
    }

    public static TheoryData<string> Forgeries() => new()
    {
        "harvest+100", "share+100-unharvested-100", "budget+100", "share-to-out-of-range", "delete-source"
    };

    /// <summary>反例在不跑重播的一般讀檔就被拒（連世界指紋與校驗碼都補正）。</summary>
    [Theory]
    [MemberData(nameof(Forgeries))]
    public void ForgedSourceLedger_IsRejectedByAnOrdinaryLoad(string what)
    {
        var world = Run(DemoWeeklyTempConfig.ForagingSample, 1, 70);
        var original = world.ExportTempSave();
        var doc = JsonNode.Parse(original)!;
        var weeks = doc["State"]!["WeekReports"]!.AsArray();
        var week = weeks[4]!;
        var sources = week["Sources"]!.AsArray();
        var shared = sources.First(x => x!["Shares"]!.AsArray().Count > 0)!;
        var share = shared["Shares"]!.AsArray()[0]!;
        switch (what)
        {
            case "harvest+100":
            {
                var id = share["SettlementId"]!.GetValue<int>();
                var ledger = week["Settlements"]!.AsArray().First(l => l!["SettlementId"]!.GetValue<int>() == id)!;
                ledger["Harvested"] = ledger["Harvested"]!.GetValue<long>() + 100;
                ledger["ReserveClosing"] = ledger["ReserveClosing"]!.GetValue<long>() - 100;
                break;
            }
            case "share+100-unharvested-100":
                share["Food"] = share["Food"]!.GetValue<long>() + 100;
                shared["Unharvested"] = shared["Unharvested"]!.GetValue<long>() - 100;
                break;
            case "budget+100":
                shared["FoodAvailable"] = shared["FoodAvailable"]!.GetValue<long>() + 100;
                shared["Unharvested"] = shared["Unharvested"]!.GetValue<long>() + 100;
                break;
            case "share-to-out-of-range":
            {
                // 份額改給一個不在這個來源範圍內的聚落（兩邊聚落的週帳一起改，讓帳內合計自洽）。
                var fromId = share["SettlementId"]!.GetValue<int>();
                var origin = world.Map.GetCell(shared["Column"]!.GetValue<int>(), shared["Row"]!.GetValue<int>());
                var far = world.Sites.Settlements.FirstOrDefault(s => s.Id != fromId
                    && !DemoHexFoodCatchment.IsInReach(origin, world.Map.GetCell(s.Column, s.Row), true));
                var food = share["Food"]!.GetValue<long>();
                if (far is null)
                {
                    share["SettlementId"] = 999999; // 地圖上沒有範圍外的聚落時改給不存在的聚落
                    break;
                }
                share["SettlementId"] = far.Id;
                foreach (var (id, delta) in new[] { (fromId, -food), (far.Id, food) })
                {
                    var ledger = week["Settlements"]!.AsArray().First(l => l!["SettlementId"]!.GetValue<int>() == id)!;
                    foreach (var field in new[] { "Harvested", "Regenerated" })
                        ledger[field] = ledger[field]!.GetValue<long>() + delta;
                }
                break;
            }
            case "delete-source":
                sources.Remove(shared);
                break;
        }
        DemoSaveLedgerTamperTests.RecomputeFingerprint(doc);
        var forged = doc.ToJsonString();
        Assert.NotEqual(original, forged);
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(forged));
        Assert.Equal(original, world.ExportTempSave());
    }
}
