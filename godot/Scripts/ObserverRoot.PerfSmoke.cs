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
        _refreshParts.Clear();
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
        var parts = string.Join(" ", _refreshParts.Select(kv => $"R_{kv.Key}={Ms(kv.Value / refreshDays)}"));
        var refreshAllocKb = (GC.GetTotalAllocatedBytes(true) - allocBefore) / 1024.0 / refreshDays;
        // 第 09-3 章：頂部資訊列單獨的成本（同一天重複更新，不推進世界）。
        watch.Restart();
        for (var k = 0; k < refreshDays; k++)
            RefreshHud();
        var hudMs = watch.Elapsed.TotalMilliseconds / refreshDays;
        watch.Restart();
        for (var k = 0; k < refreshDays; k++)
            _ = _world.Snapshot();
        var snapMs = watch.Elapsed.TotalMilliseconds / refreshDays;
        // 第 09-3 章：播放時實際走的路（同一週只更新日期與時間控制）。
        double dayTotal = 0, lightTotal = 0;
        var lightDays = 0;
        for (var d = 0; d < refreshDays; d++)
        {
            var weekBefore = _world.CommittedWeeks;
            _world.AdvanceOneDay();
            watch.Restart();
            RefreshAfterDays();
            var ms = watch.Elapsed.TotalMilliseconds;
            dayTotal += ms;
            if (_world.CommittedWeeks == weekBefore)
            {
                lightTotal += ms;
                lightDays++;
            }
        }

        // ③ 地圖：推進一週後刷新（第 08-25 章：舊像素地圖的地表、聚落版面量測已移除，政治地圖的省份重算見 PoliticalMs）。
        var mapWatch = System.Diagnostics.Stopwatch.StartNew();
        for (var d = 0; d < 7; d++)
            _world.AdvanceOneDay();
        var coreDone = mapWatch.Elapsed.TotalMilliseconds;
        _refreshParts.Clear();
        _refreshPartWatch.Restart();
        Refresh();
        // 第 08-52 章：只看換週那一次整套更新（快轉時每週卡一下的就是它），各段分開印 W_*。
        var weekParts = string.Join(" ", _refreshParts.Select(kv => $"W_{kv.Key}={Ms(kv.Value)}"));
        var map = $"MapWeekMs={Ms(mapWatch.Elapsed.TotalMilliseconds)} WeekRefreshMs={Ms(mapWatch.Elapsed.TotalMilliseconds - coreDone)} {weekParts}";
        var settlements = _world.Sites.Settlements.Count(s => !s.IsRuin);
        var politicalMs = _hexMap.PoliticalBuildMs(); // 第 08-21 章：政治地圖每週重算的成本
        GD.Print("WORLD_SIM_PERF: "
            + $"Map={world.Map.Columns}x{world.Map.Rows} Settlements={settlements} Population={_world.SettledPopulation} "
            + $"CreateMs={Ms(createMs)} CoreWeekMs={Ms(coreWeekMs)} CoreAllocMBPerWeek={Ms(coreAllocMbPerWeek)} CoreGen0={coreGen0} "
            + $"InstallMs={Ms(installMs)} RefreshMs={Ms(refreshTotal / refreshDays)} RefreshAllocKB={Ms(refreshAllocKb)} DayRefreshMs={Ms(dayTotal / refreshDays)} LightDayMs={Ms(lightDays == 0 ? 0 : lightTotal / lightDays)} HudMs={Ms(hudMs)} SnapshotMs={Ms(snapMs)} {parts} "
            + map + $" PoliticalMs={Ms(politicalMs)}");
    }
}
