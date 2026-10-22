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

        for (var row = 0; row < DemoHexWorld.Rows; row++)
        for (var col = 0; col < DemoHexWorld.Columns; col++)
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

        foreach (var cell in map.Cells)
        {
            var expectedTerrain = (DemoTerrain)(rng.NextUInt64(RngStreams.Geography) % 2UL);
            // 第 06-1 章：先抽陸地（草原／森林），海洋與主水道格之後改成水域，其餘格就是抽到的地形。
            Assert.Equal(cell.MainWaterway || cell.Sea ? DemoTerrain.Water : expectedTerrain, cell.Terrain);
        }

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
}
