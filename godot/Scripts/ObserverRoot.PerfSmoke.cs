using System.Diagnostics;
using System.Globalization;
using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 09-1 章：效能量測（CI 以 WORLD_SIM_PERF_SMOKE=1 執行，只印數字、不設門檻）。
/// 大地圖、人類繁衍世界跑五年，量：核心每週結算、每日畫面更新（Refresh）、地表代碼與聚落版面、地表整張重建、烤一塊地表、
/// 每週版面增量更新與記憶體配置。同一個種子每次相同，改版前後比較用。
/// </summary>
public partial class ObserverRoot
{
    private static readonly bool PerfSmoke = OS.HasEnvironment("WORLD_SIM_PERF_SMOKE");

    /// <summary>【TEMP】量測用的年數與種子。</summary>
    private const int TempPerfYears = 5;
    private const ulong TempPerfSeed = 7;

    private void RunPerfSmoke()
    {
        static string Ms(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        var watch = Stopwatch.StartNew();
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(TempPerfSeed, null, DemoMapSize.Large), DemoWeeklyTempConfig.LivingForagingSample);
        var createMs = watch.Elapsed.TotalMilliseconds;

        // ① 核心：五年，每週結算的平均（含每天推進）。
        var days = TempPerfYears * 52 * 7;
        var allocBefore = GC.GetTotalAllocatedBytes(true);
        var gen0 = GC.CollectionCount(0);
        watch.Restart();
        for (var d = 0; d < days; d++)
            world.AdvanceOneDay();
        var coreWeekMs = watch.Elapsed.TotalMilliseconds / (days / 7.0);
        var coreAllocMbPerWeek = (GC.GetTotalAllocatedBytes(true) - allocBefore) / 1048576.0 / (days / 7.0);
        var coreGen0 = GC.CollectionCount(0) - gen0;

        // ② 換上世界、每日畫面更新。
        watch.Restart();
        InstallWorld(world, "效能量測");
        var installMs = watch.Elapsed.TotalMilliseconds;
        const int refreshDays = 28;
        double refreshTotal = 0;
        allocBefore = GC.GetTotalAllocatedBytes(true);
        for (var d = 0; d < refreshDays; d++)
        {
            _world.AdvanceOneDay();
            watch.Restart();
            Refresh();
            refreshTotal += watch.Elapsed.TotalMilliseconds;
        }
        var refreshAllocKb = (GC.GetTotalAllocatedBytes(true) - allocBefore) / 1024.0 / refreshDays;
        // 第 09-3 章：播放時實際走的路（同一週只更新日期與時間控制）。
        double dayTotal = 0;
        for (var d = 0; d < refreshDays; d++)
        {
            _world.AdvanceOneDay();
            watch.Restart();
            RefreshAfterDays();
            dayTotal += watch.Elapsed.TotalMilliseconds;
        }

        // ③ 地圖：地表代碼、聚落版面、地表整張重建、烤一塊、每週增量。
        var map = _hexMap.PerfMeasure(() =>
        {
            for (var d = 0; d < 7; d++)
                _world.AdvanceOneDay();
            Refresh();
        });
        var settlements = _world.Sites.Settlements.Count(s => !s.IsRuin);
        GD.Print("WORLD_SIM_PERF: "
            + $"Map={world.Map.Columns}x{world.Map.Rows} Settlements={settlements} Population={_world.SettledPopulation} "
            + $"CreateMs={Ms(createMs)} CoreWeekMs={Ms(coreWeekMs)} CoreAllocMBPerWeek={Ms(coreAllocMbPerWeek)} CoreGen0={coreGen0} "
            + $"InstallMs={Ms(installMs)} RefreshMs={Ms(refreshTotal / refreshDays)} RefreshAllocKB={Ms(refreshAllocKb)} DayRefreshMs={Ms(dayTotal / refreshDays)} "
            + map);
    }
}
