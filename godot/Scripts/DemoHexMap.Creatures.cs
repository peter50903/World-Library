using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 地下城入口（近景畫洞窟、古代石門、墓室三種之一）。
/// 第 09-2 章（使用者 2026-11-15：地圖上只看得到建築與地形）：野生動物、地下城怪物的走動畫法已移除；模擬裡的獵物、狼、怪物數照舊。
/// </summary>
public partial class DemoHexMap
{
    /// <summary>【TEMP】格半徑至少幾像素才畫地下城入口素材（以下畫地圖標記）。</summary>
    private const float TempCreatureMinRadius = 18f * TerrainSurface.TempZoomScale;

    private List<(Vector2I Tile, int Seed)>? _monsters;
    private (object?, object?, object?) _groupKey;

    /// <summary>地下城入口的方格（地表或地下城改變時重算）。</summary>
    private void EnsureGroups()
    {
        if (World is null || _surface is not { Active: true })
            return; // 地表還沒畫（不知道哪裡是空地），下次再算
        var key = ((object?)World, (object?)_ground, (object?)Dungeons);
        if (_monsters is not null && key.Equals(_groupKey))
            return;
        _groupKey = key;
        _monsters = new List<(Vector2I, int)>();
        foreach (var d in Dungeons)
            if (World.Contains(d.Column, d.Row) && OpenTileIn(d.Row * World.Columns + d.Column, d.Column * 31 + d.Row) is { } gate)
                _monsters.Add((gate, d.Column * 31 + d.Row));
    }

    /// <summary>一格裡的某個空地方格（依固定雜湊挑起點，找不到就回傳 null）。</summary>
    private Vector2I? OpenTileIn(int cell, int seed)
    {
        var n = TerrainSurface.TilesPerCell;
        var x0 = cell % World!.Columns * n;
        var y0 = cell / World.Columns * n;
        var start = (int)(LayoutHash(seed, cell, 111) * n * n);
        for (var k = 0; k < n * n; k++)
        {
            var t = (start + k * 7) % (n * n);
            var tile = new Vector2I(x0 + t % n, y0 + t / n);
            if (_surface!.OpenTile(tile.X, tile.Y))
                return tile;
        }
        return null;
    }

    /// <summary>方格裡站的位置（畫面座標，腳底）。</summary>
    private static Vector2 TileFeet(Vector2 tile, Vector2 start, float radius)
        => TerrainSurface.TexelToScreen(new Vector2((tile.X + 0.5f) * TerrainSurface.TempTile, (tile.Y + 0.85f) * TerrainSurface.TempTile), start, radius);

    /// <summary>近景：地下城入口素材（排在建築一起由上而下畫）。</summary>
    private void DrawCreatures(Vector2 start, float radius)
    {
        if (World is null || radius < TempCreatureMinRadius)
            return;
        EnsureGroups();
        if (_monsters is null)
            return;
        foreach (var (tile, seed) in _monsters)
        {
            var gate = TileFeet(tile, start, radius);
            if (OnScreen(gate, radius * 3f) && PixelParts.Texture(DungeonArt(seed)) is { } entrance)
                DrawArt(entrance, gate, radius);
        }
    }

    private static string DungeonArt(int seed) => (Math.Abs(seed) % 3) switch { 0 => "dun_cave", 1 => "dun_ruin_gate", _ => "dun_crypt" };

    /// <summary>這個地下城在近景有沒有畫入口（有就不再畫地圖標記）。</summary>
    private bool DungeonDrawn(int column, int row, float radius)
        => radius >= TempCreatureMinRadius && _monsters is not null && _monsters.Any(m => m.Seed == column * 31 + row)
            && PixelParts.Texture(DungeonArt(column * 31 + row)) is not null;
}
