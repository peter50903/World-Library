using System.Diagnostics;
using Godot;

namespace WorldSimulation.GodotClient;

/// <summary>第 09-1 章：效能量測——整張重建（方格、距離、生態、縮圖）與烤一塊地表的平均。</summary>
public partial class TerrainSurface
{
    /// <summary>【TEMP】量測時烤幾塊。</summary>
    private const int TempPerfChunks = 24;

    internal (double FullMs, double BakeMs, int Tiles, double Bake2Ms, double Bake4Ms) PerfMeasure(byte[] codes, int columns, int rows, IReadOnlyDictionary<int, byte> ground)
    {
        _codes = null;
        _thumbReady = false;
        _uploaded = null;
        var watch = Stopwatch.StartNew();
        Update(codes, columns, rows, ground);
        UploadThumb();
        _uploaded = codes;
        var fullMs = watch.Elapsed.TotalMilliseconds;
        // 從地圖中央往外烤幾塊（聚落多在陸地中間）。
        var cx = _width / TempChunk / 2;
        var cy = _height / TempChunk / 2;
        var baked = 0;
        watch.Restart();
        for (var r = 0; baked < TempPerfChunks && r < 8; r++)
            for (var dy = -r; dy <= r && baked < TempPerfChunks; dy++)
                for (var dx = -r; dx <= r && baked < TempPerfChunks; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || cx + dx < 0 || cy + dy < 0 || (cx + dx) * TempChunk >= _width || (cy + dy) * TempChunk >= _height)
                        continue;
                    var chunk = new Chunk { Dirty = true };
                    BakeChunk(cx + dx, cy + dy, chunk);
                    chunk.Base?.Dispose();
                    chunk.Detail?.Dispose();
                    baked++;
                }
        var bakeMs = baked == 0 ? 0 : watch.Elapsed.TotalMilliseconds / baked;
        return (fullMs, bakeMs, _tilesX * _tilesY, BakeLod(2), BakeLod(4));
    }

    /// <summary>第 09-1d 章：分級塊（2 倍、4 倍）從地圖中央烤幾塊的平均毫秒。</summary>
    private double BakeLod(int lod)
    {
        var span = TempChunk * lod;
        var cx = _width / span / 2;
        var cy = _height / span / 2;
        var baked = 0;
        var watch = Stopwatch.StartNew();
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (cx + dx < 0 || cy + dy < 0 || (cx + dx) * span >= _width || (cy + dy) * span >= _height)
                    continue;
                var chunk = new Chunk { Dirty = true };
                BakeChunk(cx + dx, cy + dy, chunk, lod);
                chunk.Base?.Dispose();
                chunk.Detail?.Dispose();
                baked++;
            }
        return baked == 0 ? 0 : watch.Elapsed.TotalMilliseconds / baked;
    }

    /// <summary>第 09-1e 章：增量更新後的材質表與格點，是否與整張重算完全相同（比完後表是整張重算的結果）。</summary>
    internal bool MaterialsMatchFullRebuild()
    {
        var materials = (byte[])_materials.Clone();
        var corners = (byte[])_corners.Clone();
        BuildMaterials();
        return materials.AsSpan().SequenceEqual(_materials) && corners.AsSpan().SequenceEqual(_corners);
    }

    internal void PerfUpdate(byte[] codes, int columns, int rows, IReadOnlyDictionary<int, byte> ground)
    {
        Update(codes, columns, rows, ground);
        _uploaded = codes;
    }
}
