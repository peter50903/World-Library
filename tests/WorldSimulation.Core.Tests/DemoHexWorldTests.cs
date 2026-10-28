using WorldSimulation.Core.Rng;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>
/// Tests the concrete 96-cell data, not just a hash of it. No official
/// geography parameters are supplied or inferred by this demonstration.
/// </summary>
public sealed class DemoHexWorldTests
{
    [Fact]
    public void SameSeedProducesExactlyEqualCellsInStableRowMajorOrder()
    {
        var a = DemoHexWorld.Generate(42);
        var b = DemoHexWorld.Generate(42);

        Assert.Equal((ulong)42, a.Seed);
        Assert.Equal(96, a.Cells.Count);
        Assert.Equal(a.Cells.ToArray(), b.Cells.ToArray());

        for (var row = 0; row < DemoHexWorld.SmallRows; row++)
        for (var col = 0; col < DemoHexWorld.SmallColumns; col++)
        {
            var item = a.GetCell(col, row);
            Assert.Equal(col, item.Column);
            Assert.Equal(row, item.Row);
            Assert.Equal(row, item.AxialR);
            Assert.Equal(col - (row - (row & 1)) / 2, item.AxialQ);
        }
    }

    [Fact]
    public void DifferentSeedsProduceAtLeastOneActuallyDifferentTerrainCell()
    {
        var a = DemoHexWorld.Generate(42);
        var b = DemoHexWorld.Generate(8128);

        Assert.True(a.Cells.Zip(b.Cells, (left, right) =>
                left.Column == right.Column &&
                left.Row == right.Row &&
                left.Terrain != right.Terrain)
            .Any(changed => changed));
    }

    [Fact]
    public void EachCellIsReadFromExistingGeographyStreamInExactOrder()
    {
        const ulong seed = 99;
        var map = DemoHexWorld.Generate(seed);
        var rng = RngRegistry.Create(seed);

        // 第 06-1 章：先抽陸地（草原／森林），海洋與主水道格之後改成水域；第 06-2 章：陸地抽完先做多數決平滑。
        var raw = map.Cells.Select(_ => (DemoTerrain)(rng.NextUInt64(RngStreams.Geography) % 2UL)).ToArray();
        var expected = DemoHexWorld.SmoothLand(raw, DemoHexWorld.SmallColumns, DemoHexWorld.SmallRows);
        foreach (var (cell, i) in map.Cells.Select((c, i) => (c, i)))
            Assert.Equal(cell.MainWaterway || cell.Sea ? DemoTerrain.Water : expected[i], cell.Terrain);

        Assert.Equal(96UL, rng.GetDrawCounter(RngStreams.Geography));
        var replay = RngRegistry.Create(seed);
        DemoHexWorld.Generate(replay);
        Assert.Equal((ulong)DemoHexWorld.GeographyDraws, replay.GetDrawCounter(RngStreams.Geography));
        Assert.Equal(0UL, rng.GetDrawCounter(RngStreams.Resource));
        Assert.Equal(0UL, rng.GetDrawCounter(RngStreams.Pop));
    }

    [Fact]
    public void ViewQueriesArePureAndDetachedFromNewMapGeneration()
    {
        var map = DemoHexWorld.Generate(7);
        var copy = map.Cells.ToArray();
        var readOnly = Assert.IsAssignableFrom<IList<DemoHexCell>>(map.Cells);

        Assert.Throws<NotSupportedException>(() => readOnly.Clear());
        Assert.Same(map.GetCell(3, 4), map.GetCell(3, 4));

        _ = DemoHexWorld.Generate(99);
        Assert.Equal(copy, map.Cells.ToArray());
    }

    [Fact]
    public void InvalidOffsetCoordinatesAreRejected()
    {
        var map = DemoHexWorld.Generate(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetCell(-1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetCell(12, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetCell(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.GetCell(0, 8));
    }

    [Fact]
    public void TerrainIsExplicitlyDemoOnlyAndNeverDeclaredAsFormalTerrainType()
    {
        Assert.All(DemoHexWorld.Generate(42).Cells,
            cell => Assert.True(Enum.IsDefined(cell.Terrain)));

        Assert.Equal("WorldSimulation.Core.World.Demo",
            typeof(DemoTerrain).Namespace);
    }

    /// <summary>第 06-2 章：森林與草原成片（相鄰陸地同地形的比例明顯高於各擲一次硬幣的約五成），兩種地形都還在。</summary>
    [Fact]
    public void LandFormsPatches_NotACoinFlipPerCell()
    {
        double same = 0, pairs = 0;
        foreach (var seed in Enumerable.Range(1, 20).Select(i => (ulong)i))
        {
            var map = DemoHexWorld.Generate(seed);
            var land = map.Cells.Where(c => c.Terrain != DemoTerrain.Water).ToList();
            var forest = land.Count(c => c.Terrain == DemoTerrain.Forest);
            Assert.InRange(forest, 1, land.Count - 1);
            foreach (var cell in land)
                foreach (var (dq, dr, _) in DemoRouteNetwork.Directions)
                    if (map.AtAxial(cell.AxialQ + dq, cell.AxialR + dr) is { Terrain: not DemoTerrain.Water } next)
                    {
                        pairs++;
                        if (next.Terrain == cell.Terrain) same++;
                    }
        }
        Assert.True(same / pairs >= 0.65, $"same-terrain neighbour share {same / pairs:F2}");
    }

    /// <summary>第 06-2 章：中地圖 2 條河、大地圖 4 條河（每 12 欄一條）。</summary>
    [Theory]
    [InlineData(DemoMapSize.Medium)]
    [InlineData(DemoMapSize.Large)]
    public void WiderMapsHaveMoreRivers(DemoMapSize size)
    {
        var (columns, _) = DemoMapSizes.Dimensions(size);
        var rivers = DemoHexWorld.RiversFor(columns);
        Assert.Equal(columns / DemoHexWorld.TempColumnsPerRiver, rivers);
        var withSeveral = Enumerable.Range(1, 10).Count(i =>
        {
            var map = DemoHexWorld.Generate((ulong)i, size);
            var band = columns / rivers;
            return Enumerable.Range(0, rivers).Count(k => map.MainWaterway.Any(c => c.Column / band == k)) >= 2;
        });
        Assert.True(withSeveral >= 8, $"only {withSeveral}/10 maps had rivers in two or more bands");
    }
}
