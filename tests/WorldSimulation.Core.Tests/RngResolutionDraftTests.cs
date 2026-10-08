using System.Reflection;
using WorldSimulation.Core.Rng;
using Xunit;

namespace WorldSimulation.Core.Tests;

public sealed class RngResolutionDraftTests
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;

    private static void AssertSameSnapshot(RngSnapshot expected, RngSnapshot actual)
    {
        Assert.Equal(expected.Version, actual.Version);
        Assert.Equal(expected.WorldSeed, actual.WorldSeed);
        Assert.Equal(expected.HistorySeed, actual.HistorySeed);
        Assert.Equal(expected.Streams.ToArray(), actual.Streams.ToArray());
    }

    [Fact]
    public void Begin_NullRegistry_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => RngResolutionDraft.Begin(null!));
    }

    [Fact]
    public void BeginAndDrawingWithoutCommit_NeverChangesLiveRegistry()
    {
        var live = RngRegistry.Create(42);
        var before = live.Capture();

        var draft = RngResolutionDraft.Begin(live);
        draft.NextUInt64(RngStreams.Event);
        draft.NextUInt64(RngStreams.Event);
        draft.NextUInt64(RngStreams.Combat);
        var proposed = draft.CaptureProposed();

        AssertSameSnapshot(before, draft.BaseSnapshot);
        AssertSameSnapshot(before, live.Capture());
        Assert.Equal(2UL, proposed.Streams.Single(s => s.StreamId == RngStreams.Event).DrawCounter);
        Assert.Equal(1UL, proposed.Streams.Single(s => s.StreamId == RngStreams.Combat).DrawCounter);
        Assert.Equal(0UL, live.GetDrawCounter(RngStreams.Event));
        Assert.Equal(0UL, live.GetDrawCounter(RngStreams.Combat));
    }

    [Fact]
    public void Begin_InheritsAllTenMidstreamPositionsInsteadOfRestarting()
    {
        var live = RngRegistry.Create(42, 123);
        var i = 1;
        foreach (var stream in RngStreams.All)
        {
            for (var n = 0; n < i++; n++)
                live.NextUInt64(stream);
        }

        var before = live.Capture();
        var draft = RngResolutionDraft.Begin(live);
        var expectedWorker = RngRegistry.Restore(before);

        foreach (var stream in RngStreams.All)
        {
            Assert.Equal(expectedWorker.NextUInt64(stream), draft.NextUInt64(stream));
        }

        AssertSameSnapshot(before, live.Capture());
        AssertSameSnapshot(before, draft.BaseSnapshot);
        AssertSameSnapshot(expectedWorker.Capture(), draft.CaptureProposed());
    }

    [Fact]
    public void TwoDraftsFromOneBase_AreIndependent()
    {
        var live = RngRegistry.Create(42);
        var a = RngResolutionDraft.Begin(live);
        var b = RngResolutionDraft.Begin(live);

        var first = a.NextUInt64(RngStreams.Event);
        a.NextUInt64(RngStreams.Event);
        Assert.Equal(first, b.NextUInt64(RngStreams.Event));
        Assert.NotEqual(
            a.CaptureProposed().Streams.Single(s => s.StreamId == RngStreams.Event).DrawCounter,
            b.CaptureProposed().Streams.Single(s => s.StreamId == RngStreams.Event).DrawCounter);

        Assert.Equal(0UL, live.GetDrawCounter(RngStreams.Event));
        Assert.Equal(0UL, b.CaptureProposed().Streams.Single(s => s.StreamId == RngStreams.Combat).DrawCounter);
    }

    [Fact]
    public void SameBaseAndDrawOrder_GiveIdenticalProposedSnapshots()
    {
        var live = RngRegistry.Create(7, 99);
        live.NextUInt64(RngStreams.Pop);
        var first = RngResolutionDraft.Begin(live);
        var second = RngResolutionDraft.Begin(live);

        foreach (var id in new[] { RngStreams.Pop, RngStreams.Combat, RngStreams.Pop, RngStreams.Geography })
            Assert.Equal(first.NextUInt64(id), second.NextUInt64(id));

        AssertSameSnapshot(first.CaptureProposed(), second.CaptureProposed());
        Assert.Equal(1UL, live.GetDrawCounter(RngStreams.Pop));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("event")]
    [InlineData(" EVENT")]
    [InlineData("UNKNOWN")]
    public void InvalidStream_RejectsWithoutAdvancingDraftOrLive(string? streamId)
    {
        var live = RngRegistry.Create(42);
        var before = live.Capture();
        var draft = RngResolutionDraft.Begin(live);
        var proposedBefore = draft.CaptureProposed();

        Assert.Throws<ArgumentException>(() => draft.NextUInt64(streamId!));

        AssertSameSnapshot(proposedBefore, draft.CaptureProposed());
        AssertSameSnapshot(before, live.Capture());
    }

    [Fact]
    public void OneDraftStreamDoesNotAdvanceOtherStreamsOrLive()
    {
        var live = RngRegistry.Create(42);
        var before = live.Capture();
        var draft = RngResolutionDraft.Begin(live);

        for (var i = 0; i < 30; i++)
            draft.NextUInt64(RngStreams.Character);

        var after = draft.CaptureProposed();
        Assert.Equal(10, after.Streams.Count);
        Assert.Equal(30UL, after.Streams.Single(s => s.StreamId == RngStreams.Character).DrawCounter);
        foreach (var stream in RngStreams.All.Where(s => s != RngStreams.Character))
        {
            Assert.Equal(
                before.Streams.Single(s => s.StreamId == stream),
                after.Streams.Single(s => s.StreamId == stream));
        }

        AssertSameSnapshot(before, live.Capture());
    }

    [Fact]
    public void CandidateCanBeRestoredAndContinuedLikeTheDraft()
    {
        var live = RngRegistry.Create(10, 20);
        live.NextUInt64(RngStreams.Event);
        var baseline = live.Capture();
        var draft = RngResolutionDraft.Begin(live);
        draft.NextUInt64(RngStreams.Event);
        draft.NextUInt64(RngStreams.Threat);

        var candidate = draft.CaptureProposed();
        var resumed = RngRegistry.Restore(candidate);
        Assert.Equal(draft.NextUInt64(RngStreams.Event), resumed.NextUInt64(RngStreams.Event));
        Assert.Equal(draft.NextUInt64(RngStreams.Threat), resumed.NextUInt64(RngStreams.Threat));
        AssertSameSnapshot(baseline, live.Capture());
    }

    [Fact]
    public void BaseAndPreviouslyCapturedCandidate_AreDetachedFromFutureDraftDraws()
    {
        var live = RngRegistry.Create(42);
        var draft = RngResolutionDraft.Begin(live);
        var baseline = draft.BaseSnapshot;
        var candidate = draft.CaptureProposed();
        var saved = candidate.Streams.ToArray();

        draft.NextUInt64(RngStreams.Event);
        live.NextUInt64(RngStreams.Geography);

        Assert.Equal(saved, candidate.Streams.ToArray());
        Assert.Equal(0UL, baseline.Streams.Single(s => s.StreamId == RngStreams.Geography).DrawCounter);
        Assert.Equal(0UL, baseline.Streams.Single(s => s.StreamId == RngStreams.Event).DrawCounter);
        Assert.Equal(1UL, draft.CaptureProposed().Streams.Single(s => s.StreamId == RngStreams.Event).DrawCounter);
    }

    [Fact]
    public void CapturedStreamCollectionCannotBeMutated()
    {
        var draft = RngResolutionDraft.Begin(RngRegistry.Create(42));
        var immutable = Assert.IsAssignableFrom<IList<RngStreamState>>(draft.BaseSnapshot.Streams);
        Assert.Throws<NotSupportedException>(() => immutable.Clear());
        Assert.Throws<NotSupportedException>(() => immutable[0] = new RngStreamState("GEOGRAPHY", 0, 0));
        var proposed = Assert.IsAssignableFrom<IList<RngStreamState>>(draft.CaptureProposed().Streams);
        Assert.Throws<NotSupportedException>(() => proposed.Add(new RngStreamState("FAKE", 0, 0)));
    }

    [Fact]
    public void MaxCounterRejectedInDraft_WithoutChangingWorkerOrLive()
    {
        var origin = RngRegistry.Create(42);
        var baseCapture = origin.Capture();
        var initial = SeedDerivation.HistoryStream(baseCapture.HistorySeed, RngStreams.Event);
        var streamAtMax = new RngStreamState(
            RngStreams.Event,
            unchecked(initial + Gamma * ulong.MaxValue),
            ulong.MaxValue);
        var atLimit = new RngSnapshot(
            baseCapture.Version,
            baseCapture.WorldSeed,
            baseCapture.HistorySeed,
            baseCapture.Streams.Select(s => s.StreamId == RngStreams.Event ? streamAtMax : s));
        var live = RngRegistry.Restore(atLimit);
        var before = live.Capture();
        var draft = RngResolutionDraft.Begin(live);
        var proposed = draft.CaptureProposed();

        Assert.Throws<InvalidOperationException>(() => draft.NextUInt64(RngStreams.Event));
        AssertSameSnapshot(proposed, draft.CaptureProposed());
        AssertSameSnapshot(before, live.Capture());
        Assert.Equal(1UL, draft.NextUInt64(RngStreams.Combat) == 0UL
            ? draft.CaptureProposed().Streams.Single(s => s.StreamId == RngStreams.Combat).DrawCounter
            : draft.CaptureProposed().Streams.Single(s => s.StreamId == RngStreams.Combat).DrawCounter);
        AssertSameSnapshot(before, live.Capture());
    }

    [Fact]
    public void PublicInstanceAPI_ExposesNoCommitOrLiveMutation()
    {
        var names = typeof(RngResolutionDraft).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)
            .Select(method => method.Name)
            .OrderBy(name => name)
            .ToArray();

        Assert.Equal(new[] { "CaptureProposed", "NextUInt64" }, names);
        Assert.Null(typeof(RngResolutionDraft).GetProperty("LiveRegistry"));
    }
}
