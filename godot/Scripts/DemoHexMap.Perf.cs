using System.Diagnostics;
using System.Globalization;
using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>第 09-1 章：效能量測——地表代碼、聚落版面、地表重建與烤圖、每週增量（只給 CI 的量測用）。</summary>
public partial class DemoHexMap
{
    internal string PerfMeasure(Action advanceOneWeek)
    {
        static string Ms(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
        if (World is null || _surface is null)
            return "Map=none";
        var watch = Stopwatch.StartNew();
        // 全部重算：聚落規劃與版面。
        _layoutKey = default;
        _layouts.Clear();
        _layoutSignatures.Clear();
        _plans.Clear();
        _builds.Clear();
        _plansWorld = null;
        watch.Restart();
        EnsureLayouts();
        var layoutMs = watch.Elapsed.TotalMilliseconds;
        // 地表代碼（版面已算好）。
        _surfaceCodes = null;
        _surfaceBuiltMs = 0;
        watch.Restart();
        var codes = SurfaceCodes();
        var codesMs = watch.Elapsed.TotalMilliseconds;
        var (fullMs, bakeMs, tiles) = _surface.PerfMeasure(codes, World.Columns, World.Rows, _ground);
        // 每週增量：推進一週後的版面與地表局部更新。
        advanceOneWeek();
        _surfaceBuiltMs = 0;
        watch.Restart();
        var next = SurfaceCodes();
        var weekLayoutMs = watch.Elapsed.TotalMilliseconds;
        watch.Restart();
        _surface.PerfUpdate(next, World.Columns, World.Rows, _ground);
        var weekSurfaceMs = watch.Elapsed.TotalMilliseconds;
        var lots = _layouts.Values.Sum(l => l.Lots.Count);
        return $"Tiles={tiles} Lots={lots} Ground={_ground.Count} LayoutMs={Ms(layoutMs)} CodesMs={Ms(codesMs)} SurfaceFullMs={Ms(fullMs)} "
            + $"BakeChunkMs={Ms(bakeMs)} WeekLayoutMs={Ms(weekLayoutMs)} WeekSurfaceMs={Ms(weekSurfaceMs)}";
    }
}
