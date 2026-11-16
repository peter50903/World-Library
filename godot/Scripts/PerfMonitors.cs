using System.Diagnostics;
using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 09-1e 章：畫面效能的自訂監看值（Godot 除錯器「監看」分頁的 world_sim 群組）。
/// Godot 內建的效能分析看不到 C# 函式，所以把地圖重畫、地表更新、烤圖的時間與記憶體配置自己記下來。
/// 監看分頁大約每秒取樣一次，單幀的尖峰會漏掉；所以時間類的值給「最近一秒內最慢的一幀」，次數類的值給「最近一秒的合計」。
/// 只量測，不影響畫面與世界。
/// </summary>
public static class PerfMonitors
{
    // 這一幀累計（每幀結算一次）。
    private static double _frameMapDrawMs, _frameSurfaceUpdateMs, _frameBakeMs;
    private static int _frameBakeChunks;

    // 這一秒累計。
    private static double _windowMapDrawMs, _windowSurfaceUpdateMs, _windowBakeMs;
    private static int _windowBakeChunks, _windowFrames;
    private static long _windowStartTicks, _windowStartBytes;
    private static int _windowStartGen0, _windowStartGen2;

    // 上一秒的結果（監看分頁讀這些）。
    private static double _mapDrawMs, _surfaceUpdateMs, _bakeMs, _allocKbPerFrame;
    private static int _bakeChunks, _gen0, _gen2;

    private static bool _started;

    /// <summary>登記監看值（第一次 <see cref="EndFrame"/> 時自動登記；重複呼叫沒關係）。</summary>
    private static void Register()
    {
        Add("world_sim/map_draw_ms_max", () => _mapDrawMs);
        Add("world_sim/surface_update_ms_max", () => _surfaceUpdateMs);
        Add("world_sim/bake_ms_max", () => _bakeMs);
        Add("world_sim/bake_chunks_per_sec", () => _bakeChunks);
        Add("world_sim/alloc_kb_per_frame", () => _allocKbPerFrame);
        Add("world_sim/gc_gen0_per_sec", () => _gen0);
        Add("world_sim/gc_gen2_per_sec", () => _gen2);
    }

    private static void Add(string id, Func<double> read)
    {
        if (!Performance.HasCustomMonitor(id))
            Performance.AddCustomMonitor(id, Callable.From(read));
    }

    /// <summary>地圖這一次重畫花的時間：<c>using var perf = PerfMonitors.MapDraw();</c></summary>
    public static Scope MapDraw() => new(0);

    /// <summary>地表資料更新（格子變動後重算方格、材質與縮圖）花的時間。</summary>
    public static Scope SurfaceUpdate() => new(1);

    /// <summary>烤一塊地表花的時間。</summary>
    public static Scope Bake() => new(2);

    public readonly struct Scope : IDisposable
    {
        private readonly int _kind;
        private readonly long _start;

        internal Scope(int kind)
        {
            _kind = kind;
            _start = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            var ms = Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
            switch (_kind)
            {
                case 0: _frameMapDrawMs += ms; break;
                case 1: _frameSurfaceUpdateMs += ms; break;
                default:
                    _frameBakeMs += ms;
                    _frameBakeChunks++;
                    break;
            }
        }
    }

    /// <summary>每幀呼叫一次：把這一幀的累計併進這一秒，滿一秒就換成新的結果。</summary>
    public static void EndFrame()
    {
        var now = Stopwatch.GetTimestamp();
        if (!_started)
        {
            _started = true;
            Register();
            StartWindow(now);
        }
        _windowMapDrawMs = Math.Max(_windowMapDrawMs, _frameMapDrawMs);
        _windowSurfaceUpdateMs = Math.Max(_windowSurfaceUpdateMs, _frameSurfaceUpdateMs);
        _windowBakeMs = Math.Max(_windowBakeMs, _frameBakeMs);
        _windowBakeChunks += _frameBakeChunks;
        _windowFrames++;
        _frameMapDrawMs = _frameSurfaceUpdateMs = _frameBakeMs = 0;
        _frameBakeChunks = 0;
        if (Stopwatch.GetElapsedTime(_windowStartTicks, now).TotalSeconds < 1.0)
            return;
        _mapDrawMs = _windowMapDrawMs;
        _surfaceUpdateMs = _windowSurfaceUpdateMs;
        _bakeMs = _windowBakeMs;
        _bakeChunks = _windowBakeChunks;
        _allocKbPerFrame = (GC.GetTotalAllocatedBytes(false) - _windowStartBytes) / 1024.0 / Math.Max(1, _windowFrames);
        _gen0 = GC.CollectionCount(0) - _windowStartGen0;
        _gen2 = GC.CollectionCount(2) - _windowStartGen2;
        StartWindow(now);
    }

    private static void StartWindow(long now)
    {
        _windowStartTicks = now;
        _windowStartBytes = GC.GetTotalAllocatedBytes(false);
        _windowStartGen0 = GC.CollectionCount(0);
        _windowStartGen2 = GC.CollectionCount(2);
        _windowMapDrawMs = _windowSurfaceUpdateMs = _windowBakeMs = 0;
        _windowBakeChunks = _windowFrames = 0;
    }
}
