using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json.Nodes;
using WorldSimulation.Core.History;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 03 章 b 節：正式事件格式與追加式史庫。</summary>
public sealed class EventHistoryTests
{
    private static readonly Dictionary<string, DemoWeeklyTempConfig> Configs = new()
    {
        ["Sample"] = DemoWeeklyTempConfig.Sample,
        ["ForagingSample"] = DemoWeeklyTempConfig.ForagingSample,
        ["LivingForagingSample"] = DemoWeeklyTempConfig.LivingForagingSample,
        ["CrowdedForagingSample"] = DemoWeeklyTempConfig.CrowdedForagingSample,
        ["FamineSample"] = DemoWeeklyTempConfig.FamineSample,
        ["LocalFamineSample"] = DemoWeeklyTempConfig.LocalFamineSample,
        ["FrontierSample"] = DemoWeeklyTempConfig.FrontierSample
    };

    public static TheoryData<string, ulong> Matrix()
    {
        var data = new TheoryData<string, ulong>();
        foreach (var name in Configs.Keys)
            foreach (var seed in new ulong[] { 1, 2, 3, 42 })
                data.Add(name, seed);
        return data;
    }

    private static DemoObserverWorld Run(string config, ulong seed, int days)
    {
        var world = new DemoObserverWorld(DemoScenario.Sample(seed), Configs[config]);
        for (var i = 0; i < days; i++)
            world.AdvanceOneDay();
        return world;
    }

    private static WorldEvent Event(long id, long day = 7, string type = "TEMP_EV_HUNGER", long[]? related = null,
        CauseEdge[]? causes = null)
        => new(id, TimeSpan.FromDays(day), type, 1, 1,
            (related ?? new long[] { 3 }).ToImmutableArray(),
            (causes ?? Array.Empty<CauseEdge>()).ToImmutableArray(),
            CauseRootCategory.Resource, $"事件 {id}");

    // ── 與週報一致 ──

    [Theory]
    [MemberData(nameof(Matrix))]
    public void FiveYears_HistoryMatchesEveryWeeklyEvent_AndTheOldCausalIndex(string config, ulong seed)
    {
        var world = Run(config, seed, 5 * 365);
        var history = world.EventHistory;
        var weekly = world.WeekReports().SelectMany(w => w.Events).ToList();
        Assert.Equal(weekly.Count, history.Count);
        var causal = DemoCausalIndex.Build(world.WeekReports());
        Assert.Empty(causal.Problems);
        for (var i = 0; i < weekly.Count; i++)
        {
            var e = weekly[i];
            var formal = history.Events[i];
            Assert.Equal(e.EventId, formal.EventId);
            Assert.Equal(e.WorldTime, formal.WorldTime);
            Assert.Equal(DemoEventTypes.Of(e.Kind).TypeId, formal.EventTypeId);
            Assert.Equal(e.CauseEventId > 0 ? new[] { new CauseEdge(e.CauseEventId, CauseEdgeKind.Direct) } : Array.Empty<CauseEdge>(),
                formal.Causes.ToArray());
            Assert.Contains((long)e.SettlementId, formal.RelatedEntityIds);
            Assert.Equal(causal.ChildrenOf(e).Select(c => c.EventId), history.ChildrenOf(e.EventId).Select(c => c.EventId));
            Assert.Same(formal, history.Find(e.EventId));
        }
    }

    [Fact]
    public void HistoryIsAppendOnly_EarlierRecordsNeverChange()
    {
        var world = Run("FrontierSample", 42, 26 * 7);
        var first = world.EventHistory.Events.ToArray();
        Assert.NotEmpty(first);
        for (var i = 0; i < 52 * 7; i++)
            world.AdvanceOneDay();
        var later = world.EventHistory.Events;
        Assert.True(later.Count >= first.Length);
        for (var i = 0; i < first.Length; i++)
            Assert.Same(first[i], later[i]);
    }

    [Fact]
    public void FailedCommit_LeavesTheHistoryUntouched()
    {
        var world = Run("FamineSample", 42, 6);
        var before = world.EventHistory;
        foreach (var stage in Enum.GetValues<DemoCommitStage>())
        {
            world.FaultInjector = s => { if (s == stage) throw new IOException("注入"); };
            Assert.Throws<IOException>(() => world.AdvanceOneDay());
            Assert.Same(before, world.EventHistory);
        }
        world.FaultInjector = null;
        world.AdvanceOneDay();
        Assert.Equal(world.WeekReports().Sum(w => w.Events.Count), world.EventHistory.Count);
    }

    [Fact]
    public void SameSeedTwice_GivesTheSameHistory()
    {
        var a = Run("LocalFamineSample", 3, 400);
        var b = Run("LocalFamineSample", 3, 400);
        Assert.Equal(a.EventHistory.Events, b.EventHistory.Events);
    }

    // ── 人類繁衍：出生與自然死亡的當事人 ──

    [Fact]
    public void BirthAndNaturalDeath_NameTheirPeople()
    {
        var world = Run("LivingForagingSample", 42, 3 * 365);
        var reports = world.WeekReports().SelectMany(w => w.Events).ToList();
        var birth = reports.FirstOrDefault(e => e.Kind == DemoSettlementEventKind.Birth);
        Assert.NotNull(birth);
        var formalBirth = world.EventHistory.Find(birth!.EventId)!;
        Assert.Equal("EV_BIRTH", formalBirth.EventTypeId);
        Assert.Equal(CauseRootCategory.Lifecycle, formalBirth.Root);
        // 新生兒＋母＋父（聚落之外至少三個人）；新生兒在人口名冊裡。
        var people = formalBirth.RelatedEntityIds.Where(id => id != birth.SettlementId).ToArray();
        Assert.Equal(3, people.Length);
        var roster = world.HumanPopulationSnapshot!.People.Select(p => p.Id).ToHashSet();
        Assert.Contains(people[0], roster.Concat(DeadIds(world)));

        var death = reports.FirstOrDefault(e => e.Kind == DemoSettlementEventKind.NaturalDeath);
        if (death is not null)
        {
            var formalDeath = world.EventHistory.Find(death.EventId)!;
            Assert.Equal("EV_DEATH", formalDeath.EventTypeId);
            var deceased = formalDeath.RelatedEntityIds.Single(id => id != death.SettlementId);
            Assert.DoesNotContain(deceased, roster); // 死者不在人口快照，但事件仍查得到
        }
    }

    private static IEnumerable<long> DeadIds(DemoObserverWorld world)
        => world.EventHistory.Events.Where(e => e.EventTypeId == "EV_DEATH").SelectMany(e => e.RelatedEntityIds);

    // ── AppendCommitted 反例：全拒且原物件不變 ──

    public static TheoryData<string> BadAppends() => new()
    {
        "future", "missing", "self", "skip", "duplicate", "backwards", "blank", "unknown", "zero-entity", "repeat-parent"
    };

    [Theory]
    [MemberData(nameof(BadAppends))]
    public void BadEvents_AreRejected_AndTheOriginalIsUnchanged(string what)
    {
        var history = EventHistory.Empty.AppendCommitted(new[] { Event(1), Event(2, causes: new[] { new CauseEdge(1, CauseEdgeKind.Direct) }) });
        var bad = what switch
        {
            "future" => Event(3, causes: new[] { new CauseEdge(4, CauseEdgeKind.Direct) }),
            "missing" => Event(3, causes: new[] { new CauseEdge(0, CauseEdgeKind.Direct) }),
            "self" => Event(3, causes: new[] { new CauseEdge(3, CauseEdgeKind.Direct) }),
            "skip" => Event(4),
            "duplicate" => Event(2),
            "backwards" => Event(3, day: 6),
            "blank" => Event(3, type: " "),
            "unknown" => Event(3, type: "EV_DRAGON"),
            "zero-entity" => Event(3, related: new long[] { 0 }),
            "repeat-parent" => Event(3, causes: new[] { new CauseEdge(1, CauseEdgeKind.Direct), new CauseEdge(1, CauseEdgeKind.Contributing) }),
            _ => throw new ArgumentOutOfRangeException(nameof(what))
        };
        var before = history.Events.ToArray();
        Assert.Throws<InvalidOperationException>(() => history.AppendCommitted(new[] { bad }));
        Assert.Equal(before, history.Events);
        Assert.Equal(2, history.LastEventId);
        Assert.Single(history.ChildrenOf(1));
    }

    [Fact]
    public void GoodAppend_ReturnsANewHistory_AndIndexesChildren()
    {
        var one = EventHistory.Empty.AppendCommitted(new[] { Event(1) });
        var two = one.AppendCommitted(new[] { Event(2, day: 14, causes: new[] { new CauseEdge(1, CauseEdgeKind.Contributing) }) });
        Assert.Equal(1, one.Count);
        Assert.Equal(2, two.Count);
        Assert.Equal(new long[] { 2 }, two.ChildrenOf(1).Select(e => e.EventId));
        Assert.Equal(new long[] { 1 }, two.ParentsOf(2).Select(e => e.EventId));
        Assert.Empty(one.ChildrenOf(1));
    }

    [Fact]
    public void CauseEdgeKind_HasExactlyTwoValues()
        => Assert.Equal(new[] { CauseEdgeKind.Direct, CauseEdgeKind.Contributing }, Enum.GetValues<CauseEdgeKind>());

    [Fact]
    public void EventHistory_HasNoPublicMutator()
    {
        var allowed = new HashSet<string>
        {
            "get_Count", "get_LastEventId", "get_Events", "get_Empty", "Find", "ParentsOf", "ChildrenOf",
            "ToString", "Equals", "GetHashCode", "GetType"
        };
        var publicMethods = typeof(EventHistory).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        Assert.All(publicMethods, m => Assert.Contains(m.Name, allowed));
        Assert.Empty(typeof(EventHistory).GetProperties().Where(p => p.SetMethod?.IsPublic == true));
    }

    [Fact]
    public void QueryingAThousandTimes_ChangesNothing()
    {
        var world = Run("FrontierSample", 42, 300);
        var before = world.StateFingerprint();
        var history = world.EventHistory;
        for (var i = 0; i < 1000; i++)
        {
            var id = (i % Math.Max(1, history.Count)) + 1;
            _ = history.Find(id);
            _ = history.ChildrenOf(id);
            _ = history.ParentsOf(id);
            _ = DemoEventProjection.DescribeLines(history.Events[(int)(id - 1)]);
        }
        Assert.Equal(before, world.StateFingerprint());
    }

    // ── 存檔：重建、拒讀竄改 ──

    [Fact]
    public void SaveAndLoad_RebuildsTheSameHistory_AndContinuesIdentically()
    {
        var world = Run("LivingForagingSample", 42, 2 * 365);
        var json = world.ExportTempSave();
        var loaded = DemoObserverWorld.ImportTempSave(json);
        Assert.Equal(world.EventHistory.Events, loaded.EventHistory.Events);
        for (var i = 0; i < 120; i++)
        {
            world.AdvanceOneDay();
            loaded.AdvanceOneDay();
        }
        Assert.Equal(world.StateFingerprint(), loaded.StateFingerprint());
        Assert.Equal(world.EventHistory.Events, loaded.EventHistory.Events);
    }

    public static TheoryData<string> HistoryTampering() => new()
    {
        "EventTypeId", "LocationColumn", "RelatedEntityIds", "Causes", "Root", "Summary", "WorldTime", "Drop"
    };

    [Theory]
    [MemberData(nameof(HistoryTampering))]
    public void TamperedHistoryField_IsRejected(string field)
    {
        var world = Run("FrontierSample", 42, 300);
        var doc = JsonNode.Parse(world.ExportTempSave())!;
        var history = doc["State"]!["History"]!.AsArray();
        Assert.NotEmpty(history);
        var target = history.Last(e => e!["Causes"]!.AsArray().Count > 0)!;
        switch (field)
        {
            case "EventTypeId": target["EventTypeId"] = "EV_BIRTH"; break;
            case "LocationColumn": target["LocationColumn"] = target["LocationColumn"]!.GetValue<int>() + 1; break;
            case "RelatedEntityIds": target["RelatedEntityIds"]!.AsArray().Add(999999); break;
            case "Causes": target["Causes"] = new JsonArray(); break;
            case "Root": target["Root"] = 2; break;
            case "Summary": target["Summary"] = "竄改"; break;
            case "WorldTime": target["WorldTime"] = "1.00:00:00"; break;
            case "Drop": history.Remove(target); break;
        }
        // 補正校驗碼（第 04 章 b 節），確認是史庫重建比對把它擋下。
        doc["Manifest"]!["StateChecksum"] = DemoSaveHeader.Checksum(System.Text.Encoding.UTF8.GetBytes(doc["State"]!.ToJsonString()));
        Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
    }

    [Fact]
    public void PreviousVersionFile_IsTooOld()
    {
        var doc = JsonNode.Parse(Run("Sample", 42, 30).ExportTempSave())!;
        doc["Version"] = 5;
        doc["State"]!.AsObject().Remove("History");
        var ex = Assert.Throws<InvalidDataException>(() => DemoObserverWorld.ImportTempSave(doc.ToJsonString()));
        Assert.Contains("版本太舊，請建立新世界", ex.Message, StringComparison.Ordinal);
    }
}
