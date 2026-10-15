using System.Text.Json.Nodes;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// C04（P1.0.03-a）：示範世界所有實體（聚落、資源點、人口群體、遷徙隊伍、TEMP 人類個體）
/// 由同一個世界實體 ID 分配器發號；全世界 ID 永不重複、永不重用（40C INV_ENTITY_ID、44H SAVE_ID）。
/// </summary>
public sealed class DemoEntityIdIntegrationTests
{
    private static DemoObserverWorld Run(ulong seed, DemoWeeklyTempConfig config, int days)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), config);
        for (var i = 0; i < days; i++)
            world.AdvanceOneDay();
        return world;
    }

    /// <summary>世界目前看得到的全部實體 ID（含已抵達或全滅的隊伍：取自週報隊伍帳）。</summary>
    private static List<long> VisibleIds(DemoObserverWorld world)
    {
        var ids = world.Sites.Settlements.Select(s => (long)s.Id)
            .Concat(world.Sites.ResourcePoints.Select(p => (long)p.Id))
            .Concat(world.Sites.PopGroups.Select(g => (long)g.Id))
            .Concat(world.WeekReports().SelectMany(w => w.Migrations).Where(m => m.Departed).Select(m => (long)m.BatchId))
            .ToList();
        if (world.HumanPopulationSnapshot is { } humans)
            ids.AddRange(humans.People.Select(p => p.Id));
        return ids;
    }

    [Theory]
    [InlineData(42UL, "Living")]
    [InlineData(7UL, "Frontier")]
    [InlineData(42UL, "Frontier")]
    public void TenYears_AllIdsDistinctBelowNextValue_AndWeeklyRangesChain(ulong seed, string preset)
    {
        var config = preset == "Living" ? DemoWeeklyTempConfig.LivingForagingSample : DemoWeeklyTempConfig.FrontierSample;
        var opening = new DemoObserverWorld(DemoScenario.Sample(seed), config).EntityIdNextValue;
        var world = Run(seed, config, 10 * 365);
        var ids = VisibleIds(world);

        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.InRange((ulong)id, 1UL, world.EntityIdNextValue - 1));

        // 每週配號區間首尾相接，從開局下一號一路接到目前下一號（死亡的人與全滅的隊伍也佔過號，不會被重發）。
        var expected = (long)opening;
        foreach (var week in world.WeekReports())
        {
            Assert.Equal(expected, week.EntityIdNextOpening);
            Assert.True(week.EntityIdNextClosing >= week.EntityIdNextOpening);
            expected = week.EntityIdNextClosing;
        }
        Assert.Equal((ulong)expected, world.EntityIdNextValue);
    }

    [Theory]
    [InlineData("Sample")]
    [InlineData("Living")]
    [InlineData("Frontier")]
    public void OpeningIds_SettlementsFirst_AndExactlyFillOneToNextValue(string preset)
    {
        var config = preset switch
        {
            "Living" => DemoWeeklyTempConfig.LivingForagingSample,
            "Frontier" => DemoWeeklyTempConfig.FrontierSample,
            _ => DemoWeeklyTempConfig.Sample
        };
        var world = new DemoObserverWorld(DemoScenario.Sample(42), config);
        var n = world.Sites.Settlements.Count;
        Assert.Equal(Enumerable.Range(1, n), world.Sites.Settlements.Select(s => s.Id));

        var ids = VisibleIds(world).OrderBy(id => id).ToList();
        Assert.Equal(Enumerable.Range(1, (int)world.EntityIdNextValue - 1).Select(i => (long)i), ids);
    }

    [Fact]
    public void SaveLoadContinue_NeverReusesAnId()
    {
        var saving = Run(42, DemoWeeklyTempConfig.FrontierSample, 5 * 365);
        var before = VisibleIds(saving).ToHashSet();
        var savedNext = saving.EntityIdNextValue;

        var loaded = DemoObserverWorld.ImportTempSave(saving.ExportTempSave());
        Assert.Equal(savedNext, loaded.EntityIdNextValue);
        for (var i = 0; i < 5 * 365; i++)
            loaded.AdvanceOneDay();

        var fresh = VisibleIds(loaded).Where(id => !before.Contains(id)).ToList();
        Assert.All(fresh, id => Assert.True((ulong)id >= savedNext, $"ID {id} 小於存檔時的下一號 {savedNext}"));
        Assert.True(loaded.EntityIdNextValue > savedNext, "續跑五年應至少發出新 ID（開拓情境）。");
    }

    [Fact]
    public void FailedCommitInAWeekThatAllocates_ConsumesNoIds()
    {
        // 找一個會配號（有隊伍出發或新聚落）的週。
        var probe = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.FrontierSample);
        var targetDay = 0;
        for (var day = 1; day <= 5 * 365 && targetDay == 0; day++)
        {
            probe.AdvanceOneDay();
            if (day % 7 == 0 && probe.LatestWeek is { } w && w.EntityIdNextClosing > w.EntityIdNextOpening)
                targetDay = day;
        }
        Assert.True(targetDay > 0, "開拓情境五年內應有配號的週。");

        var world = Run(42, DemoWeeklyTempConfig.FrontierSample, targetDay - 1);
        var direct = Run(42, DemoWeeklyTempConfig.FrontierSample, targetDay - 1);
        var nextBefore = world.EntityIdNextValue;
        var fingerprint = world.StateFingerprint();

        world.FaultInjector = stage =>
        {
            if (stage == DemoCommitStage.AfterLedgerStaged) throw new InvalidOperationException("injected");
        };
        Assert.Throws<InvalidOperationException>(() => world.AdvanceOneDay());
        Assert.Equal(nextBefore, world.EntityIdNextValue);
        Assert.Equal(fingerprint, world.StateFingerprint());

        world.FaultInjector = null;
        world.AdvanceOneDay();
        direct.AdvanceOneDay();
        Assert.True(world.EntityIdNextValue > nextBefore);
        Assert.Equal(direct.StateFingerprint(), world.StateFingerprint());
        Assert.Equal(direct.ExportTempSave(), world.ExportTempSave());
    }

    [Theory]
    [InlineData("next-below-live-id")]
    [InlineData("opening-not-previous-closing")]
    [InlineData("closing-gap")]
    [InlineData("duplicate-batch-id")]
    public void TamperedEntityIds_AreRejected_AndCallerWorldIsUnchanged(string tamper)
    {
        var world = Run(42, DemoWeeklyTempConfig.FrontierSample, 3 * 365);
        var doc = JsonNode.Parse(world.ExportTempSave())!;
        var state = doc["State"]!;
        var weeks = state["WeekReports"]!.AsArray();
        switch (tamper)
        {
            case "next-below-live-id":
                state["EntityIds"]!["NextValue"] = 2UL;
                break;
            case "opening-not-previous-closing":
                weeks[1]!["EntityIdNextOpening"] = weeks[1]!["EntityIdNextOpening"]!.GetValue<long>() + 1;
                break;
            case "closing-gap":
                var last = weeks[weeks.Count - 1]!;
                last["EntityIdNextClosing"] = last["EntityIdNextClosing"]!.GetValue<long>() + 1;
                state["EntityIds"]!["NextValue"] = state["EntityIds"]!["NextValue"]!.GetValue<ulong>() + 1;
                break;
            case "duplicate-batch-id":
                var departures = weeks.SelectMany(w => w!["Migrations"]!.AsArray())
                    .Where(m => m!["Departed"]!.GetValue<bool>()).ToList();
                Assert.True(departures.Count >= 2, "開拓情境三年內應有兩支以上隊伍出發。");
                departures[1]!["BatchId"] = departures[0]!["BatchId"]!.GetValue<int>();
                break;
        }

        var before = world.StateFingerprint();
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
        Assert.Equal(before, world.StateFingerprint());
    }
}
