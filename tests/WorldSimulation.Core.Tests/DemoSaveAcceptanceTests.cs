using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// V0-07C 存讀檔完整驗收（#70）：多種子 × 四種 TEMP 情境 × 多種存檔點，
/// 「存 → 讀 → 續跑 8 週」的完整狀態（整份匯出含世界指紋）必須與「從未存讀、一路直接跑」相同；
/// 第 2 版（直接恢復）與第 1 版（重播）兩種存檔都要通過。
/// </summary>
public sealed class DemoSaveAcceptanceTests
{
    private const int SearchDays = 728;      // 每個存檔點最多找兩年（TEMP 曆法）。
    private const int ContinueDays = 8 * 7;  // 讀檔後續跑 8 週。

    private readonly ITestOutputHelper _output;

    public DemoSaveAcceptanceTests(ITestOutputHelper output) => _output = output;

    private static readonly ulong[] Seeds = { 42, 7, 1 };

    private static readonly (string Name, DemoWeeklyTempConfig Config)[] Presets =
    {
        ("一般示範", DemoWeeklyTempConfig.Sample),
        ("全面饑荒", DemoWeeklyTempConfig.FamineSample),
        ("局部饑荒", DemoWeeklyTempConfig.LocalFamineSample),
        ("開拓", DemoWeeklyTempConfig.FrontierSample)
    };

    private enum SavePoint
    {
        MidWeek,          // 非週界（第 10 日）
        WeekBoundary,     // 週界（第 14 日）
        BatchInTransit,   // 第一次有遷徙隊伍在途中
        JustFounded,      // 剛建立新聚落的那一天
        SettlementEmptied // 開局聚落人口歸零之後（開拓／饑荒全滅）
    }

    private static bool Reached(SavePoint point, DemoObserverWorld world, int day) => point switch
    {
        SavePoint.MidWeek => day == 10,
        SavePoint.WeekBoundary => day == 14,
        SavePoint.BatchInTransit => world.MigrationsInTransit().Count > 0,
        SavePoint.JustFounded => world.Sites.Settlements.Any(s => s.IsFounded),
        SavePoint.SettlementEmptied => world.Sites.Settlements
            .Where(s => !s.IsFounded)
            .Any(s => world.SettlementPopulation(s.Id) == 0),
        _ => false
    };

    [Fact]
    public void SaveLoadContinue_MatchesDirectRun_AcrossSeedsScenariosAndSavePoints()
    {
        var found = new HashSet<SavePoint>();
        var checks = 0;
        foreach (var seed in Seeds)
        foreach (var (name, config) in Presets)
        {
            // 一路直接跑，記下每個存檔點第一次出現的日子與當時的存檔。
            var direct = new DemoObserverWorld(DemoScenario.Sample(seed), config);
            var checkpoints = new List<(SavePoint Point, int Day, string Json)>();
            var pending = Enum.GetValues<SavePoint>().ToList();
            for (var day = 1; day <= SearchDays && pending.Count > 0; day++)
            {
                direct.AdvanceOneDay();
                foreach (var point in pending.Where(p => Reached(p, direct, day)).ToList())
                {
                    checkpoints.Add((point, day, direct.ExportTempSave()));
                    pending.Remove(point);
                }
            }

            Assert.Contains(checkpoints, c => c.Point == SavePoint.MidWeek);
            Assert.Contains(checkpoints, c => c.Point == SavePoint.WeekBoundary);

            var endDay = checkpoints.Max(c => c.Day) + ContinueDays;
            while (direct.Snapshot().DaysElapsed < endDay)
                direct.AdvanceOneDay();
            var expected = direct.ExportTempSave();

            foreach (var (point, day, json) in checkpoints)
            {
                found.Add(point);
                foreach (var version in new[] { 2, 1 })
                {
                    var file = json;
                    if (version == 1)
                    {
                        var v1 = JsonNode.Parse(json)!;
                        v1["Version"] = 1;
                        file = v1.ToJsonString();
                    }

                    var loaded = DemoObserverWorld.ImportTempSave(file);
                    Assert.Equal(json, loaded.ExportTempSave()); // 讀入後的狀態＝存檔當時
                    while (loaded.Snapshot().DaysElapsed < endDay)
                        loaded.AdvanceOneDay();
                    Assert.True(expected == loaded.ExportTempSave(),
                        $"種子 {seed}｜{name}｜{point}（第 {day} 日）｜第 {version} 版：續跑到第 {endDay} 日後與直接跑不同");
                    checks++;
                }
            }

            _output.WriteLine($"種子 {seed}｜{name}：" + string.Join("、", checkpoints.Select(c => $"{c.Point}@{c.Day}")));
        }

        // 整個矩陣至少要真的涵蓋每一種存檔點（不能因為情境沒觸發就默默略過）。
        var missing = Enum.GetValues<SavePoint>().Where(p => !found.Contains(p)).ToList();
        Assert.True(missing.Count == 0, "矩陣沒有涵蓋的存檔點：" + string.Join("、", missing));
        _output.WriteLine($"存→讀→續跑比對 {checks} 組全部相同。");
    }

    [Fact]
    public void SameSaveLoadedTwice_GivesIdenticalWorlds_AndLoadingDoesNotTouchTheSource()
    {
        foreach (var (_, config) in Presets)
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(42), config);
            for (var i = 0; i < 50; i++)
                world.AdvanceOneDay();
            var json = world.ExportTempSave();

            var first = DemoObserverWorld.ImportTempSave(json);
            var second = DemoObserverWorld.ImportTempSave(json);
            Assert.NotSame(first, second);
            for (var i = 0; i < 21; i++)
            {
                first.AdvanceOneDay();
                second.AdvanceOneDay();
            }

            Assert.Equal(first.ExportTempSave(), second.ExportTempSave());
            Assert.Equal(json, world.ExportTempSave()); // 讀檔不會改動原本的世界
        }
    }
}
