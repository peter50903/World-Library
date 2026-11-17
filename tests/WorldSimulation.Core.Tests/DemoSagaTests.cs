using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 08-10e 章：文字史書（序、每十年一卷、終章；只引述編年史的世界大事；重寫結果相同）。</summary>
public sealed class DemoSagaTests
{
    private static readonly Lazy<(DemoObserverWorld World, DemoSagaOpening Opening)> Shared = new(() =>
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(3), DemoWeeklyTempConfig.LivingForagingSample);
        var opening = DemoSaga.Opening(world);
        for (var d = 0; d < 45 * TestCalendar.DaysPerYear; d++)
            world.AdvanceOneDay();
        return (world, opening);
    });

    [Fact]
    public void SagaHasAPrefaceOneVolumePerDecadeAndAnEnding()
    {
        var (world, opening) = Shared.Value;
        var lines = DemoSaga.Write(world, opening);

        Assert.StartsWith($"序　太初，{opening.Population} 人分居 {opening.Settlements} 處聚落", lines[0], StringComparison.Ordinal);
        var volumes = lines.Where(l => l.StartsWith("卷 ", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, volumes.Count);
        Assert.StartsWith("卷 1　第 1～10 年　", volumes[0], StringComparison.Ordinal);
        Assert.StartsWith("卷 5　第 41～45 年　", volumes[4], StringComparison.Ordinal);
        Assert.Contains("終章　第 45 年的世界", lines);
        Assert.Contains(lines, l => l.StartsWith($"　世上有 {world.WorldPopulation} 人、", StringComparison.Ordinal));
    }

    [Fact]
    public void SagaTellsEveryWorldEventOfTheChronicleAndNothingElse()
    {
        var (world, opening) = Shared.Value;
        var lines = DemoSaga.Write(world, opening);
        var years = DemoWorldChronicle.Build(world);

        var expected = years.SelectMany(y => y.Entries.Where(e => e.IsWorld).Select(e => $"　第 {y.Year} 年，{e.Text.Trim()}")).ToList();
        var told = lines.Where(l => l.StartsWith("　第 ", StringComparison.Ordinal) && l.Contains(" 年，", StringComparison.Ordinal)).ToList();
        Assert.Equal(expected.Count, told.Count);
        Assert.All(expected.Zip(told), pair => Assert.StartsWith(pair.First, pair.Second, StringComparison.Ordinal));
        Assert.Equal(years.Sum(y => y.Births),
            lines.Where(l => l.StartsWith("　這些年裡 ", StringComparison.Ordinal))
                .Sum(l => long.Parse(l["　這些年裡 ".Length..l.IndexOf(" 人出生", StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void SagaIsTheSameWhenWrittenAgainAndWithoutAnOpening()
    {
        var (world, opening) = Shared.Value;
        Assert.Equal(DemoSaga.Write(world, opening), DemoSaga.Write(world, opening));
        var plain = DemoSaga.Write(world);
        Assert.Equal("序　這是一個世界留下的紀錄。", plain[0]);
        Assert.Equal(DemoSaga.Write(world, opening).Skip(1), plain.Skip(1));
    }

    [Fact]
    public void EveryDecadeCharacterHasItsOwnVolumeTitle()
    {
        var titles = new[] { "戰亂的年代", "衰退的年代", "饑荒的年代", "擴張的年代", "創新的年代", "轉折的年代", "平靜的年代", "平穩的年代" }
            .Select(DemoSaga.VolumeTitle).ToList();
        Assert.Equal(titles.Count, titles.Distinct().Count());
    }
}
