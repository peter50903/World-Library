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
        // 第 12-2f 節：終章寫出險境（有地下城時）與最強的軍隊。
        Assert.Single(lines, l => l.StartsWith("　最強的軍隊：", StringComparison.Ordinal) || l == "　世上還沒有軍隊。");
        if (world.Dungeons.Count > 0)
            Assert.Single(lines, l => l.StartsWith("　險境：", StringComparison.Ordinal) && world.Dungeons.All(d => l.Contains(d.Name, StringComparison.Ordinal)));
    }

    // 第 08-10m 節：同一卷的請願獨立合成一句、跟著首領脫離的不另寫、同一個征服者收服兩處以上合成一句（單獨一次照常逐條寫）。
    [Fact]
    public void RealmEventsInAVolumeAreCondensed()
    {
        static DemoSettlementEvent Ev(long id, int settlement, DemoSettlementEventKind kind, long count, string cause)
            => new(52, TimeSpan.Zero, settlement, "", kind, count, cause, EventId: id);
        static DemoChronicleEntry En(long id) => new(52, DemoChronicleLevel.Major, DemoChronicleCategory.Realm, new[] { new DemoChronicleSegment("某事") }, id);
        var events = new[]
        {
            Ev(1, 2, DemoSettlementEventKind.Independence, 1, "甲接受了乙的請願，讓它們獨立"),
            Ev(2, 3, DemoSettlementEventKind.Independence, 1, "隨乙脫離甲"),
            Ev(3, 4, DemoSettlementEventKind.Independence, 5, "戊接受了丁的請願，讓它們獨立"),
            Ev(4, 6, DemoSettlementEventKind.Conquered, 7, "庚兵臨城下（兵力 9 對 2），己不戰而降，歸入庚"),
            Ev(5, 8, DemoSettlementEventKind.Conquered, 7, "庚征服了辛（攻方死 2、守方死 5）"),
            Ev(6, 9, DemoSettlementEventKind.Conquered, 10, "癸征服了壬（攻方死 1、守方死 3）"),
        }.ToDictionary(e => e.EventId);
        var names = new Dictionary<int, string> { [1] = "甲", [2] = "乙", [3] = "丙", [4] = "丁", [5] = "戊", [6] = "己", [7] = "庚", [8] = "辛", [9] = "壬", [10] = "癸" };
        var entries = new[] { (3, En(1)), (3, En(2)), (5, En(3)), (6, En(4)), (8, En(5)), (9, En(6)) };
        var (lines, consumed) = DemoSaga.Condense(entries, events, names);
        Assert.Equal(new[] { (3, "　第 3～5 年，2 處聚落請願獲准、脫離上級獨立：乙、丁。"), (6, "　第 6～8 年，庚先後收服己、辛（2 處聚落）。") },
            lines.Select(l => (l.FirstYear, l.Text)));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, consumed.Select(e => e.EventId).OrderBy(x => x));
    }

    // 第 08-10j 節：未得善終的死因由事件數得；每年的死因合計不超過年帳的非壽終死亡。
    [Fact]
    public void SagaListsTheCausesOfUnnaturalDeaths()
    {
        var (world, opening) = Shared.Value;
        var causes = DemoSaga.DeathCauses(world.SettlementEvents());
        foreach (var y in world.YearSummaries())
        {
            var known = causes.TryGetValue(y.Year, out var t) ? t.Values.Sum() : 0;
            Assert.True(known <= y.Deaths - y.NaturalDeaths, $"第 {y.Year} 年：死因合計 {known}、未得善終 {y.Deaths - y.NaturalDeaths}");
        }
        Assert.All(causes.Values.SelectMany(t => t.Keys), c => Assert.Contains(c, DemoSaga.DeathCauseNames));
        var lines = DemoSaga.Write(world, opening);
        Assert.All(lines.Where(l => l.Contains("人未得善終", StringComparison.Ordinal)),
            l => Assert.Contains("人未得善終（", l, StringComparison.Ordinal));
    }

    [Fact]
    public void SagaTellsEveryWorldEventOnceAndSumsUpTheNewTechnologies()
    {
        var (world, opening) = Shared.Value;
        var lines = DemoSaga.Write(world, opening);
        var years = DemoWorldChronicle.Build(world);

        // 世界大事（技術除外）、遺跡、城鎮以上的升降：每一句都在史書裡，同一卷裡一模一樣的句子只寫一次。
        // 勢力大事的句子另外註明領袖（第 08-10f 章），這裡只逐句比對其他類別。
        var toldEntries = DemoSaga.ToldEntries(years);
        var sentences = toldEntries.Where(e => e.Category != DemoChronicleCategory.Realm).Select(DemoSaga.Sentence).Distinct().ToList();
        var told = lines.Where(l => l.StartsWith("　第 ", StringComparison.Ordinal) && l.Contains(" 年，", StringComparison.Ordinal)).ToList();
        Assert.InRange(told.Count, sentences.Count, toldEntries.Count);
        Assert.All(sentences, sentence => Assert.Contains(told, l => l.EndsWith(" 年，" + sentence, StringComparison.Ordinal)));
        Assert.All(years.SelectMany(y => y.Entries).Where(e => e.IsWorld && e.Category != DemoChronicleCategory.Knowledge && e.Weight != DemoWorldChronicle.WeightWar),
            e => Assert.True(DemoSaga.IsTold(e)));
        Assert.False(DemoSaga.IsTold(new DemoChronicleEntry(1, DemoChronicleLevel.Major, DemoChronicleCategory.Realm,
            new[] { new DemoChronicleSegment("甲向乙宣戰") }, Weight: DemoWorldChronicle.WeightWar))); // 開戰不另寫，結局會說
        // 同一個聚落升到同一級只寫第一次；降級照寫。
        static DemoChronicleYear Year(int year, params string[] texts) => new(year, DemoEra.Pioneer, false,
            texts.Select(t => new DemoChronicleEntry(year * 52, DemoChronicleLevel.Major, DemoChronicleCategory.Tier,
                new[] { new DemoChronicleSegment("東塔林", 7), new DemoChronicleSegment(t) })).ToList(), 0, 0, new Dictionary<int, (long, long)>());
        var flapping = DemoSaga.ToldEntries(new[]
        {
            Year(1, " 人口 60 人，由村莊發展為城鎮"), Year(2, " 人口降到 40 人，降為村莊"), Year(3, " 人口 61 人，由村莊發展為城鎮"), Year(4, " 人口 200 人，由城鎮發展為城市")
        });
        Assert.Equal(new[] { 208 }, flapping.Select(e => e.Week)); // 第 08-10m 節：城鎮成立與降為村莊算在「各地」，城市以上才逐條寫
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

    // 第 08-10f 章：勢力大事的句子裡，每個聚落第一次出現時註明當時的領袖；長名稱先佔位。
    [Fact]
    public void RealmSentencesNameTheLeadersOfTheDay()
    {
        var cast = new[] { ("柯林菲爾", "莉亞·伍德"), ("伯恩坡", "哈洛·泰恩"), ("柯林", "某甲") };
        Assert.Equal("伯恩坡（哈洛·泰恩）兵臨城下（兵力 57 對 5），柯林菲爾（莉亞·伍德）不戰而降，歸入伯恩坡。",
            DemoSagaCast.Annotate("伯恩坡兵臨城下（兵力 57 對 5），柯林菲爾不戰而降，歸入伯恩坡。", cast));
        Assert.Equal("沒有提到任何聚落。", DemoSagaCast.Annotate("沒有提到任何聚落。", cast));
        Assert.Equal("柯林（某甲）與柯林菲爾（莉亞·伍德）議和。", DemoSagaCast.Annotate("柯林與柯林菲爾議和。", cast));
        Assert.Equal("壽終（63 歲）", DemoSagaCast.ExitText("老死（63 歲）"));
        Assert.Equal("死於災禍或兵燹（40 歲）", DemoSagaCast.ExitText("死於天災（40 歲）"));
        Assert.Equal("隨聚落消亡", DemoSagaCast.ExitText("聚落無人"));

        // 真實世界：登記表上的領袖查得到，任期外查不到；人物句都來自登記表上的領袖。
        var (world, opening) = Shared.Value;
        var saga = new DemoSagaCast(world);
        var leader = world.PersonRecords.First(p => p.Role == DemoRepresentativeRole.Leader && p.AppointedWeek >= 1);
        Assert.NotNull(saga.LeaderAt(leader.SettlementId, leader.AppointedWeek));
        Assert.Null(saga.LeaderAt(-1, 10));
        var names = world.PersonRecords.Where(p => p.Role == DemoRepresentativeRole.Leader).Select(p => p.Name).ToHashSet();
        Assert.All(DemoSaga.Write(world, opening).Where(l => l.StartsWith("　人物：", StringComparison.Ordinal)),
            l => Assert.Contains(names, n => l.Contains(n, StringComparison.Ordinal)));
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

    // 第 05-2 章：名稱的連結標記——去掉標記就是純文字的史書；每個連結都指向世界上真的存在的聚落、人物或家族，文字就是那個名稱。
    [Fact]
    public void MarkedSagaLinksRealNamesAndStripsToThePlainSaga()
    {
        var (world, opening) = Shared.Value;
        var marked = DemoSaga.WriteMarked(world, opening);
        Assert.Equal(DemoSaga.Write(world, opening), marked.Select(DemoSaga.Plain));
        var pieces = marked.SelectMany(DemoSaga.Pieces).ToList();
        Assert.Equal(string.Concat(marked.Select(DemoSaga.Plain)), string.Concat(pieces.Select(p => p.Text)));
        var people = world.PersonRecords.ToDictionary(p => p.Id);
        var links = pieces.Where(p => p.Kind != '\0').ToList();
        Assert.Contains(links, l => l.Kind == 's');
        Assert.Contains(links, l => l.Kind == 'p');
        Assert.All(links, l =>
        {
            switch (l.Kind)
            {
                case 's':
                    Assert.Equal(world.Sites.Settlements.Single(s => s.Id == l.Id).Name, l.Text);
                    break;
                case 'p':
                    Assert.True(people.ContainsKey(l.Id));
                    Assert.Contains(l.Text, people[l.Id].Name, StringComparison.Ordinal); // 家族一句裡只寫名
                    break;
                case 'h':
                    Assert.Equal(l.Id, DemoHouses.HouseOf(people, l.Id));
                    Assert.Equal(DemoNames.FamilyOf(people[l.Id].Name) + "家", l.Text);
                    break;
                default:
                    Assert.Fail($"未知的連結種類 {l.Kind}");
                    break;
            }
        });
        // 拆段與還原：沒有標記的一行就是一段正文。
        Assert.Equal(new[] { new DemoSaga.Piece("沒有名稱。") }, DemoSaga.Pieces("沒有名稱。"));
        var one = DemoSaga.Link('s', 7, "伯恩坡") + "征服了" + DemoSaga.Link('s', 9, "東塔林");
        Assert.Equal("伯恩坡征服了東塔林", DemoSaga.Plain(one));
        Assert.Equal(new[] { new DemoSaga.Piece("伯恩坡", 's', 7), new DemoSaga.Piece("征服了"), new DemoSaga.Piece("東塔林", 's', 9) }, DemoSaga.Pieces(one));
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
