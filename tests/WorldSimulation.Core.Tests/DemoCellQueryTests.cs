using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using WorldSimulation.ObserverCli;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>C07（P1.0.01-b）：Core 唯讀點格查詢；Godot 與 CLI `cell` 共用同一段文字。</summary>
public sealed class DemoCellQueryTests
{
    private const ulong Seed = 42;

    public static TheoryData<string, int> ConfigsAndDays()
    {
        var data = new TheoryData<string, int>();
        foreach (var name in new[] { "Sample", "LocalFamineSample", "FrontierSample", "ForagingSample" })
            foreach (var days in new[] { 0, 7, 35, 200 })
                data.Add(name, days);
        return data;
    }

    private static DemoWeeklyTempConfig Config(string name) => name switch
    {
        "Sample" => DemoWeeklyTempConfig.Sample,
        "LocalFamineSample" => DemoWeeklyTempConfig.LocalFamineSample,
        "FrontierSample" => DemoWeeklyTempConfig.FrontierSample,
        "ForagingSample" => DemoWeeklyTempConfig.ForagingSample,
        _ => throw new ArgumentOutOfRangeException(nameof(name))
    };

    private static DemoObserverWorld WorldAt(string config, int days, ulong seed = Seed)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), Config(config));
        for (var i = 0; i < days; i++)
            world.AdvanceOneDay();
        return world;
    }

    private static IEnumerable<(int Column, int Row)> AllCells()
    {
        for (var row = 0; row < DemoHexWorld.SmallRows; row++)
            for (var column = 0; column < DemoHexWorld.SmallColumns; column++)
                yield return (column, row);
    }

    private static string Cli(DemoObserverWorld world, string command)
    {
        var output = new StringWriter { NewLine = "\n" };
        Assert.True(CellCommand.TryRun(world, command, output));
        return output.ToString();
    }

    private static string[] AllText(DemoObserverWorld world)
        => AllCells().Select(c => world.QueryCell(c.Column, c.Row).Text).ToArray();

    [Theory]
    [MemberData(nameof(ConfigsAndDays))]
    public void CliOutputMatchesCoreQueryForEveryCell(string config, int days)
    {
        var world = WorldAt(config, days);
        foreach (var (column, row) in AllCells())
        {
            var view = world.QueryCell(column, row);
            Assert.Equal(string.Join("\n", view.Lines), view.Text);
            Assert.Equal(view.Text + "\n", Cli(world, $"cell {column} {row}"));
        }
    }

    [Theory]
    [MemberData(nameof(ConfigsAndDays))]
    public void SettlementCellsShowCoreNumbers(string config, int days)
    {
        var world = WorldAt(config, days);
        foreach (var settlement in world.Sites.Settlements)
        {
            var view = world.QueryCell(settlement.Column, settlement.Row);
            Assert.Equal(settlement.Id, view.SettlementId);
            Assert.Contains(view.Lines, l => l.StartsWith($"聚落：{settlement.Name}", StringComparison.Ordinal));
            Assert.Contains($"目前人口：{world.SettlementPopulation(settlement.Id)}", view.Lines);
            Assert.Contains($"糧食庫存：{world.SettlementFood(settlement.Id)}", view.Lines);
            foreach (var resource in world.Sites.ResourcePointsOf(settlement.Id))
                Assert.Contains($"資源點 #{resource.Id}：{DemoCellQuery.ResourceName(resource.Kind)}", view.Lines);
        }

        foreach (var (column, row) in AllCells())
        {
            var view = world.QueryCell(column, row);
            Assert.Equal(world.Sites.SettlementAt(column, row)?.Id, view.SettlementId);
            Assert.Contains($"核心地形：{DemoCellQuery.TerrainName(view.Cell)}", view.Lines[2]);
        }
    }

    [Fact]
    public void SharedSourceCellsExposeCatchmentReach()
    {
        var world = WorldAt("ForagingSample", 7);
        var sources = world.SharedFoodSources();
        Assert.NotEmpty(sources);
        foreach (var source in sources)
        {
            var view = world.QueryCell(source.Column, source.Row);
            Assert.Equal(source, view.SharedSource);
            var expected = DemoHexFoodCatchment.EligibleCells(world.Map, source)
                .Select(c => new DemoCellRef(c.Column, c.Row)).ToArray();
            Assert.Equal(expected, view.FoodReachCells.ToArray());
            Assert.Contains(view.Lines, l => l.StartsWith($"共享來源：每週供糧上限 {source.FoodAvailable}", StringComparison.Ordinal));
        }

        // 非來源格沒有黃框。
        var sourceCells = sources.Select(s => (s.Column, s.Row)).ToHashSet();
        foreach (var (column, row) in AllCells().Where(c => !sourceCells.Contains(c)))
        {
            Assert.Null(world.QueryCell(column, row).SharedSource);
            Assert.Empty(world.QueryCell(column, row).FoodReachCells);
        }
    }

    [Fact]
    public void MigrantBatchCellsListTheBatch()
    {
        // 開拓情境：找到第一個有隊伍在途中的日子（多種子搜尋，確保斷言真的執行）。
        for (ulong seed = 1; seed <= 40; seed++)
        {
            var world = new DemoObserverWorld(DemoScenario.Sample(seed), DemoWeeklyTempConfig.FrontierSample);
            for (var day = 0; day < 400; day++)
            {
                world.AdvanceOneDay();
                var batches = world.MigrationsInTransit();
                if (batches.Count == 0)
                    continue;
                foreach (var batch in batches)
                {
                    var view = world.QueryCell(batch.Cell.Column, batch.Cell.Row);
                    Assert.Contains(batch, view.Migrants);
                    Assert.Contains(view.Lines, l => l.StartsWith($"遷徙隊伍 #{batch.Id}：{batch.People} 人", StringComparison.Ordinal));
                    Assert.Equal(view.Text + "\n", Cli(world, $"cell {batch.Cell.Column} {batch.Cell.Row}"));
                }
                return;
            }
        }
        Assert.Fail("開拓情境 40 個種子 × 400 日內都沒有遷徙隊伍在途中，無法驗證隊伍顯示。");
    }

    [Fact]
    public void QueryingNeverChangesTheWorldOrConsumesRng()
    {
        var world = WorldAt("LocalFamineSample", 35);
        var fingerprint = world.StateFingerprint();
        var save = world.ExportTempSave();

        foreach (var (column, row) in AllCells())
            for (var i = 0; i < 100; i++)
                world.QueryCell(column, row);
        foreach (var command in new[] { "cell 2 2", "cell 12 0", "cell a b", "cell 1", "cell -1 0", "cell 0 8" })
            for (var i = 0; i < 100; i++)
                Cli(world, command);
        for (var i = 0; i < 100; i++)
            Assert.Throws<ArgumentOutOfRangeException>(() => world.QueryCell(12, 0));

        Assert.Equal(fingerprint, world.StateFingerprint());
        Assert.Equal(save, world.ExportTempSave());
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(12, 0)]
    [InlineData(0, 8)]
    [InlineData(0, -1)]
    public void OutOfMapQueryThrows(int column, int row)
    {
        var world = WorldAt("Sample", 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => world.QueryCell(column, row));
    }

    [Theory]
    [InlineData("cell 12 0", "格子超出地圖：欄 0～11、列 0～7\n")]
    [InlineData("cell 0 8", "格子超出地圖：欄 0～11、列 0～7\n")]
    [InlineData("cell -1 0", "格子超出地圖：欄 0～11、列 0～7\n")]
    [InlineData("cell a b", "用法：cell 欄 列，例如 cell 2 2\n")]
    [InlineData("cell 1", "用法：cell 欄 列，例如 cell 2 2\n")]
    [InlineData("cell", "用法：cell 欄 列，例如 cell 2 2\n")]
    [InlineData("cell 1 2 3", "用法：cell 欄 列，例如 cell 2 2\n")]
    public void BadCliCellCommandsExplainInChineseAndKeepTheWorld(string command, string expected)
    {
        var world = WorldAt("Sample", 7);
        var fingerprint = world.StateFingerprint();
        Assert.Equal(expected, Cli(world, command));
        Assert.Equal(fingerprint, world.StateFingerprint());
    }

    [Theory]
    [InlineData("s")]
    [InlineData("cells")]
    [InlineData("n")]
    public void OtherCommandsAreNotHandled(string command)
    {
        var output = new StringWriter();
        Assert.False(CellCommand.TryRun(WorldAt("Sample", 0), command, output));
        Assert.Equal(string.Empty, output.ToString());
    }

    [Theory]
    [InlineData("Sample")]
    [InlineData("FrontierSample")]
    [InlineData("ForagingSample")]
    public void SameSeedReplaysToTheSameCellText(string config)
    {
        Assert.Equal(AllText(WorldAt(config, 200)), AllText(WorldAt(config, 200)));
    }

    [Theory]
    [InlineData("LocalFamineSample")]
    [InlineData("FrontierSample")]
    [InlineData("ForagingSample")]
    public void CellTextSurvivesSaveAndLoad(string config)
    {
        var world = WorldAt(config, 35);
        var restored = DemoObserverWorld.ImportTempSave(world.ExportTempSave());
        Assert.Equal(AllText(world), AllText(restored));
    }
}
