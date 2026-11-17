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
        // 第 08-6c 章：一個世界時代一卷；第一卷一定是希望時代，最後一卷寫到目前這一年。
        var ages = world.Ages();
        var volumes = lines.Where(l => l.StartsWith("卷 ", StringComparison.Ordinal)).ToList();
        Assert.Equal(ages.Count, volumes.Count);
        Assert.StartsWith("卷 1　希望時代　第 1～", volumes[0], StringComparison.Ordinal);
        Assert.Contains($"　{DemoAges.Name(ages[^1].Age)}　第 {ages[^1].FromYear}～45 年　", volumes[^1], StringComparison.Ordinal);
        Assert.Contains("終章　第 45 年的世界", lines);
        Assert.Contains(lines, l => l.StartsWith($"　世上有 {world.WorldPopulation} 人、", StringComparison.Ordinal));
    }

    [Fact]
    public void SagaTellsEveryWorldEventOnceAndSumsUpTheNewTechnologies()
    {
        var (world, opening) = Shared.Value;
        var lines = DemoSaga.Write(world, opening);
        var years = DemoWorldChronicle.Build(world);

        // 世界大事（技術除外）、遺跡、城鎮以上的升降：每一句都在史書裡，同一卷裡一模一樣的句子只寫一次。
        var sentences = world.Ages().Select(a => years.Where(y => y.Year >= a.FromYear && y.Year <= a.ToYear))
            .SelectMany(volume => DemoSaga.ToldEntries(years).Where(e => volume.Any(y => y.Entries.Contains(e))).Select(DemoSaga.Sentence).Distinct()).ToList();
        var told = lines.Where(l => l.StartsWith("　第 ", StringComparison.Ordinal) && l.Contains(" 年，", StringComparison.Ordinal)).ToList();
        Assert.Equal(sentences.Count, told.Count);
        Assert.All(sentences, sentence => Assert.Contains(told, l => l.EndsWith(" 年，" + sentence, StringComparison.Ordinal)));
        Assert.All(years.SelectMany(y => y.Entries).Where(e => e.IsWorld && e.Category != DemoChronicleCategory.Knowledge),
            e => Assert.True(DemoSaga.IsTold(e)));
        // 同一個聚落升到同一級只寫第一次；降級照寫。
        static DemoChronicleYear Year(int year, params string[] texts) => new(year, DemoEra.Pioneer, false,
            texts.Select(t => new DemoChronicleEntry(year * 52, DemoChronicleLevel.Major, DemoChronicleCategory.Tier,
                new[] { new DemoChronicleSegment("東塔林", 7), new DemoChronicleSegment(t) })).ToList(), 0, 0, new Dictionary<int, (long, long)>());
        var flapping = DemoSaga.ToldEntries(new[]
        {
            Year(1, " 人口 60 人，由村莊發展為城鎮"), Year(2, " 人口降到 40 人，降為村莊"), Year(3, " 人口 61 人，由村莊發展為城鎮"), Year(4, " 人口 200 人，由城鎮發展為城市")
        });
        Assert.Equal(new[] { 52, 208 }, flapping.Select(e => e.Week)); // 降為村莊不在史書的範圍（只寫城鎮以上）
        // 戰爭沒打贏的結局是一般事件，也要寫進史書；其他一般事件不寫。
        static DemoChronicleEntry Realm(string text) => new(1, DemoChronicleLevel.Normal, DemoChronicleCategory.Realm, new[] { new DemoChronicleSegment(text) });
        Assert.True(DemoSaga.IsTold(Realm("東塔林的叛軍被佩林谷擊潰（叛軍死 3、上級死 1）")));
        Assert.True(DemoSaga.IsTold(Realm("獨立戰爭打了一年不分勝負，東塔林只好維持原狀")));
        Assert.False(DemoSaga.IsTold(Realm("東塔林歸附佩林谷")));

        // 世界首次的技術不逐條列，每卷收成一句，項數加起來等於編年史的首創數。
        var firsts = years.SelectMany(y => y.Entries).Count(e => e.IsWorld && e.Category == DemoChronicleCategory.Knowledge);
        const string head = "　世人學會了 ";
        Assert.Equal(firsts, lines.Where(l => l.StartsWith(head, StringComparison.Ordinal))
            .Sum(l => int.Parse(l[head.Length..l.IndexOf(" 項", StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture)));
        Assert.DoesNotContain(lines, l => l.Contains("（世界首次）", StringComparison.Ordinal));

        // 生死用世界的年帳：出生、壽終、其餘死亡加起來對得上。
        var ledger = world.YearSummaries();
        const string people = "　這些年裡 ";
        Assert.Equal(ledger.Sum(y => y.Births), lines.Where(l => l.StartsWith(people, StringComparison.Ordinal))
            .Sum(l => long.Parse(l[people.Length..l.IndexOf(" 人出生", StringComparison.Ordinal)], System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Contains(lines, l => l.EndsWith($"；第 45 年末世上有 {world.WorldPopulation} 人。", StringComparison.Ordinal));
    }

    [Fact]
    public void SentenceDoesNotRepeatTheSettlementNameAndDropsSpacesBetweenChineseCharacters()
    {
        static DemoChronicleEntry Entry(params DemoChronicleSegment[] segments)
            => new(1, DemoChronicleLevel.Major, DemoChronicleCategory.Realm, segments);
        Assert.Equal("佩林谷接受了提爾谷的請願，讓它們獨立。",
            DemoSaga.Sentence(Entry(new DemoChronicleSegment("提爾谷", 2), new DemoChronicleSegment(" 佩林谷接受了提爾谷的請願，讓它們獨立"))));
        Assert.Equal("塔林丘隨東塔林脫離佩林谷。",
            DemoSaga.Sentence(Entry(new DemoChronicleSegment("塔林丘", 3), new DemoChronicleSegment(" 隨東塔林脫離佩林谷"))));
        Assert.Equal("歐文港人口 29 人，由營地發展為村莊。",
            DemoSaga.Sentence(Entry(new DemoChronicleSegment("歐文港", 1), new DemoChronicleSegment(" 人口 29 人，由營地發展為村莊"))));
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
}
