using System.Text.Json.Nodes;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// #90：正式 365 日年的年界存讀檔一致性。365 不是 7 的倍數，年界與週結算錯開；
/// 在年界前後存檔 → 讀檔 → 續跑到下一個年界之後，整份狀態（世界指紋、週報、事件、存量）
/// 與年報歸屬都必須與「從未存讀、一路直接跑」相同。TEMP 存檔上限涵蓋 100 個 365 日年。
/// </summary>
public sealed class DemoYearBoundarySaveTests
{
    private static readonly int[] YearBoundaryDays = { 364, 365, 366, 370, 371, 730 };

    public static TheoryData<string> Presets => new() { "一般", "局部饑荒", "開拓" };

    private static DemoWeeklyTempConfig Preset(string name) => name switch
    {
        "局部饑荒" => DemoWeeklyTempConfig.LocalFamineSample,
        "開拓" => DemoWeeklyTempConfig.FrontierSample,
        _ => DemoWeeklyTempConfig.Sample
    };

    /// <summary>第 <paramref name="day"/> 日之後的下一個年界再多跑一週（跨過年界與至少一次週結算）。</summary>
    private static int EndAfterNextYearBoundary(int day, int daysPerYear) => (day / daysPerYear + 1) * daysPerYear + 7;

    /// <summary>週報依結算日（第 7×週 日）歸屬年份：年 →（週數、本年死亡、本年事件數）。</summary>
    private static IReadOnlyList<(int Year, int Weeks, long Deaths, int Events)> YearAttribution(
        IReadOnlyList<DemoWeekReport> weeks, int daysPerYear)
        => weeks
            .GroupBy(w => (w.Week * DemoCenturyRun.DaysPerWeeklySettlement - 1) / daysPerYear + 1)
            .OrderBy(g => g.Key)
            .Select(g => (g.Key, g.Count(),
                g.Sum(w => w.Settlements.Sum(l => l.Deaths) + w.Migrations.Sum(m => m.Deaths)),
                g.Sum(w => w.Events.Count)))
            .ToList();

    private static string AsVersion1(string json)
    {
        var node = JsonNode.Parse(json)!;
        node["Version"] = 1;
        return node.ToJsonString();
    }

    [Theory]
    [MemberData(nameof(Presets))]
    public void SaveAroundYearBoundaries_LoadAndContinue_MatchesDirectRun(string preset)
    {
        var daysPerYear = TestCalendar.DaysPerYear;
        var config = Preset(preset);
        var ends = YearBoundaryDays.Select(d => EndAfterNextYearBoundary(d, daysPerYear)).ToHashSet();
        var lastDay = ends.Max();

        // 一路直接跑（從未存讀），記下每個存檔點的存檔與每個終點的完整狀態。
        var direct = new DemoObserverWorld(DemoScenario.Sample(42), config);
        var saves = new Dictionary<int, string>();
        var expected = new Dictionary<int, (string Json, IReadOnlyList<DemoWeekReport> Weeks, IReadOnlyList<DemoYearSummary> Years)>();
        for (var day = 1; day <= lastDay; day++)
        {
            direct.AdvanceOneDay();
            if (YearBoundaryDays.Contains(day))
                saves[day] = direct.ExportTempSave();
            if (ends.Contains(day))
                expected[day] = (direct.ExportTempSave(), direct.WeekReports(), direct.YearSummaries());
        }

        foreach (var saveDay in YearBoundaryDays)
        {
            var end = EndAfterNextYearBoundary(saveDay, daysPerYear);
            foreach (var (version, file) in new[] { (3, saves[saveDay]) })
            {
                var loaded = DemoObserverWorld.ImportTempSave(file);
                Assert.Equal(saveDay, loaded.Snapshot().DaysElapsed);
                Assert.Equal(saves[saveDay], loaded.ExportTempSave()); // 讀入後＝存檔當時
                while (loaded.Snapshot().DaysElapsed < end)
                    loaded.AdvanceOneDay();

                Assert.True(expected[end].Json == loaded.ExportTempSave(),
                    $"{preset}｜第 {saveDay} 日存檔（第 {version} 版）續跑到第 {end} 日後與直接跑不同");
                // 第 04-1 章：讀檔後只保留近期週報；保留的週歸屬相同，年帳（含之前的年份）完全相同。
                var first = loaded.FirstRetainedWeek;
                Assert.Equal(YearAttribution(expected[end].Weeks.Where(w => w.Week >= first).ToList(), daysPerYear),
                    YearAttribution(loaded.WeekReports(), daysPerYear));
                Assert.Equal(expected[end].Years, loaded.YearSummaries());
            }
        }

        // 年報歸屬：365 日年第 1 年 52 次週結算（第 7～364 日），第 2 年 52 次（第 371～728 日）。
        var attribution = YearAttribution(expected[lastDay].Weeks, daysPerYear);
        Assert.Equal(DemoCenturyRun.WeeklySettlementsThrough(daysPerYear), attribution[0].Weeks);
        Assert.Equal(
            DemoCenturyRun.WeeklySettlementsThrough(2 * daysPerYear) - DemoCenturyRun.WeeklySettlementsThrough(daysPerYear),
            attribution[1].Weeks);
    }

    [Fact]
    public void LastDaysOfTheHundredthYearCanBeSavedAndLoaded_AndDay36501IsRejected()
    {
        var daysPerYear = TestCalendar.DaysPerYear;
        var lastDay = 100 * daysPerYear;
        Assert.Equal(lastDay, DemoObserverWorld.TempMaxSaveDays); // TEMP 上限＝100 個 365 日年

        var direct = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.Sample);
        var saves = new Dictionary<int, string>();
        var end = EndAfterNextYearBoundary(lastDay, daysPerYear);
        string? expected = null;
        string? beyond = null;
        for (var day = 1; day <= end; day++)
        {
            direct.AdvanceOneDay();
            if (day == lastDay - 1 || day == lastDay)
                saves[day] = direct.ExportTempSave();
            if (day == lastDay + 1)
                beyond = direct.ExportTempSave();
        }

        expected = direct.ExportTempSave();

        foreach (var saveDay in new[] { lastDay - 1, lastDay })
        {
            var loaded = DemoObserverWorld.ImportTempSave(saves[saveDay]);
            Assert.Equal(saveDay, loaded.Snapshot().DaysElapsed);
            Assert.Equal(saves[saveDay], loaded.ExportTempSave());
            while (loaded.Snapshot().DaysElapsed < end)
                loaded.AdvanceOneDay();
            Assert.True(expected == loaded.ExportTempSave(), $"第 {saveDay} 日存檔續跑到第 {end} 日後與直接跑不同");
        }

        // 超過上限一天：明確拒絕（中文錯誤）。C04：第 1 版（重播）已一律「版本太舊」。
        var error = Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(beyond!));
        Assert.Contains("超出可重播範圍", error.Message);
        var outdated = Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(AsVersion1(saves[lastDay])));
        Assert.Contains("版本太舊，請建立新世界", outdated.Message);
    }

    [Fact]
    public void CorruptOrOutOfRangeYearBoundarySaveIsRejectedAndTheRunningWorldIsUnchanged()
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.Sample);
        for (var day = 0; day < TestCalendar.DaysPerYear; day++)
            world.AdvanceOneDay();
        var before = world.ExportTempSave();

        var tooLate = JsonNode.Parse(before)!;
        tooLate["Manifest"]!["WorldTimeTicks"] = TimeSpan.FromDays(DemoObserverWorld.TempMaxSaveDays + 1).Ticks;
        var negative = JsonNode.Parse(before)!;
        negative["Manifest"]!["WorldTimeTicks"] = -TimeSpan.TicksPerDay;
        var truncated = before[..(before.Length / 2)];

        foreach (var file in new[] { tooLate.ToJsonString(), negative.ToJsonString(), truncated })
        {
            var error = Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(file));
            Assert.Contains("TEMP 存檔", error.Message);
        }

        Assert.Equal(before, world.ExportTempSave()); // 讀檔失敗不會改動執行中的世界
    }
}
