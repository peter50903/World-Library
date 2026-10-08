using System.Reflection;
using WorldSimulation.Core.Rng;
using Xunit;

namespace WorldSimulation.Core.Tests;

public sealed class RngCandidatePreflightTests
{
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;

    private static RngSnapshot Repack(
        RngSnapshot source,
        IEnumerable<RngStreamState> streams,
        int? version = null)
        => new(version ?? source.Version, source.WorldSeed, source.HistorySeed, streams);

    private static void AssertUnchanged(RngSnapshot previous, RngSnapshot current)
    {
        Assert.Equal(previous.Version, current.Version);
        Assert.Equal(previous.WorldSeed, current.WorldSeed);
        Assert.Equal(previous.HistorySeed, current.HistorySeed);
        Assert.Equal(previous.Streams.ToArray(), current.Streams.ToArray());
    }

    [Fact]
    public void AllThreeSnapshotParameters_MustBeNonNull()
    {
        var snapshot = RngRegistry.Create(42).Capture();
        Assert.Throws<ArgumentNullException>(() => RngCandidatePreflight.IsCompatible(null!, snapshot, snapshot));
        Assert.Throws<ArgumentNullException>(() => RngCandidatePreflight.IsCompatible(snapshot, null!, snapshot));
        Assert.Throws<ArgumentNullException>(() => RngCandidatePreflight.IsCompatible(snapshot, snapshot, null!));
    }

    [Fact]
    public void UnchangedValidCandidate_IsCompatibleIncludingZeroDraws()
    {
        var live = RngRegistry.Create(42);
        var baseline = live.Capture();
        var draft = RngResolutionDraft.Begin(live);
        Assert.True(RngCandidatePreflight.IsCompatible(live.Capture(), draft.BaseSnapshot, draft.CaptureProposed()));
        AssertUnchanged(baseline, live.Capture());
    }

    [Fact]
    public void CandidateMayAdvanceOneOrSeveralIndependentStreams()
    {
        var live = RngRegistry.Create(42, 777);
        live.NextUInt64(RngStreams.Event);
        var draft = RngResolutionDraft.Begin(live);
        var before = live.Capture();
        draft.NextUInt64(RngStreams.Event);
        draft.NextUInt64(RngStreams.Event);
        draft.NextUInt64(RngStreams.Geography);
        draft.NextUInt64(RngStreams.Threat);

        Assert.True(RngCandidatePreflight.IsCompatible(live.Capture(), draft.BaseSnapshot, draft.CaptureProposed()));
        AssertUnchanged(before, live.Capture());
    }

    [Fact]
    public void StaleCurrentStreamCounter_IsIncompatible()
    {
        var live = RngRegistry.Create(42);
        var draft = RngResolutionDraft.Begin(live);
        draft.NextUInt64(RngStreams.Event);
        live.NextUInt64(RngStreams.Pop);

        Assert.False(RngCandidatePreflight.IsCompatible(live.Capture(), draft.BaseSnapshot, draft.CaptureProposed()));
    }

    [Fact]
    public void CurrentWithOtherWorldSeed_IsIncompatible()
    {
        var baseline = RngRegistry.Create(42).Capture();
        var current = RngRegistry.Create(43).Capture();
        Assert.False(RngCandidatePreflight.IsCompatible(current, baseline, baseline));
    }

    [Fact]
    public void CurrentWithOtherHistorySeed_IsIncompatible()
    {
        var baseline = RngRegistry.Create(42, 5).Capture();
        var current = RngRegistry.Create(42, 6).Capture();
        Assert.False(RngCandidatePreflight.IsCompatible(current, baseline, baseline));
    }

    [Fact]
    public void CandidateFromAnotherWorldOrHistoryRoot_IsIncompatible()
    {
        var baseline = RngRegistry.Create(42, 5).Capture();
        Assert.False(RngCandidatePreflight.IsCompatible(baseline, baseline, RngRegistry.Create(43, 5).Capture()));
        Assert.False(RngCandidatePreflight.IsCompatible(baseline, baseline, RngRegistry.Create(42, 6).Capture()));
    }

    [Fact]
    public void CandidateCannotRewindAnyStreamCounter()
    {
        var live = RngRegistry.Create(42);
        live.NextUInt64(RngStreams.Combat);
        live.NextUInt64(RngStreams.Combat);
        var baseline = live.Capture();

        // An older snapshot is individually protocol-valid but must be rejected as a candidate.
        var earlier = RngRegistry.Create(42).Capture();
        Assert.False(RngCandidatePreflight.IsCompatible(baseline, baseline, earlier));
    }

    [Fact]
    public void StreamOrderingDoesNotAffectComparison()
    {
        var live = RngRegistry.Create(42);
        live.NextUInt64(RngStreams.Character);
        var draft = RngResolutionDraft.Begin(live);
        draft.NextUInt64(RngStreams.Combat);
        var current = live.Capture();
        var baseline = draft.BaseSnapshot;
        var proposed = draft.CaptureProposed();

        var reorderedCurrent = Repack(current, current.Streams.Reverse());
        var reorderedBase = Repack(baseline, baseline.Streams.Reverse());
        var reorderedCandidate = Repack(proposed, proposed.Streams.Reverse());

        Assert.True(RngCandidatePreflight.IsCompatible(reorderedCurrent, reorderedBase, reorderedCandidate));
    }

    [Fact]
    public void MaxCounterCanStayAtMaxWithoutOverflow()
    {
        var baseline = RngRegistry.Create(42).Capture();
        var seed = SeedDerivation.WorldStream(baseline.WorldSeed, RngStreams.Geography);
        var atMax = new RngStreamState(
            RngStreams.Geography,
            unchecked(seed + Gamma * ulong.MaxValue),
            ulong.MaxValue);
        var snap = Repack(baseline, baseline.Streams.Select(
            s => s.StreamId == RngStreams.Geography ? atMax : s));

        Assert.True(RngCandidatePreflight.IsCompatible(snap, snap, snap));
        // The preflight did not try to draw beyond the maximum counter.
        Assert.Equal(ulong.MaxValue, snap.Streams.Single(s => s.StreamId == RngStreams.Geography).DrawCounter);
    }

    [Fact]
    public void WrongVersionOnAnySnapshot_ThrowsInsteadOfReturningFalse()
    {
        var valid = RngRegistry.Create(42).Capture();
        var bad = Repack(valid, valid.Streams, version: 2);
        Assert.Throws<ArgumentException>(() => RngCandidatePreflight.IsCompatible(bad, valid, valid));
        Assert.Throws<ArgumentException>(() => RngCandidatePreflight.IsCompatible(valid, bad, valid));
        Assert.Throws<ArgumentException>(() => RngCandidatePreflight.IsCompatible(valid, valid, bad));
    }

    [Fact]
    public void MissingStreamOnAnySnapshot_ThrowsInsteadOfReturningFalse()
    {
        var valid = RngRegistry.Create(42).Capture();
        var bad = Repack(valid, valid.Streams.Skip(1));
        Assert.Throws<ArgumentException>(() => RngCandidatePreflight.IsCompatible(bad, valid, valid));
        Assert.Throws<ArgumentException>(() => RngCandidatePreflight.IsCompatible(valid, bad, valid));
        Assert.Throws<ArgumentException>(() => RngCandidatePreflight.IsCompatible(valid, valid, bad));
    }

    [Fact]
    public void DuplicateStreamAndBadState_AreRejected()
    {
        var valid = RngRegistry.Create(42).Capture();
        var duplicate = Repack(valid, valid.Streams.Append(valid.Streams[0]));
        var wrongState = Repack(
            valid,
            valid.Streams.Select(s => s.StreamId == RngStreams.Event
                ? s with { State = unchecked(s.State + 1UL) }
                : s));

        Assert.Throws<ArgumentException>(() => RngCandidatePreflight.IsCompatible(valid, duplicate, valid));
        Assert.Throws<ArgumentException>(() => RngCandidatePreflight.IsCompatible(valid, valid, duplicate));
        Assert.Throws<ArgumentException>(() => RngCandidatePreflight.IsCompatible(wrongState, valid, valid));
        Assert.Throws<ArgumentException>(() => RngCandidatePreflight.IsCompatible(valid, valid, wrongState));
    }

    [Fact]
    public void AllSnapshotsMustBeValidEvenIfAConflictIsAlreadyObvious()
    {
        var valid = RngRegistry.Create(42).Capture();
        var otherRoot = RngRegistry.Create(43).Capture();
        var wrongVersion = Repack(valid, valid.Streams, version: 9);

        // An otherwise valid mismatch cannot hide a malformed candidate.
        Assert.Throws<ArgumentException>(() =>
            RngCandidatePreflight.IsCompatible(otherRoot, valid, wrongVersion));
    }

    [Fact]
    public void IndependentCandidatesAreBothPurelyCheckedNeverApplied()
    {
        var live = RngRegistry.Create(42);
        var original = live.Capture();
        var a = RngResolutionDraft.Begin(live);
        var b = RngResolutionDraft.Begin(live);
        a.NextUInt64(RngStreams.Event);
        b.NextUInt64(RngStreams.Pop);

        Assert.True(RngCandidatePreflight.IsCompatible(live.Capture(), a.BaseSnapshot, a.CaptureProposed()));
        Assert.True(RngCandidatePreflight.IsCompatible(live.Capture(), b.BaseSnapshot, b.CaptureProposed()));
        AssertUnchanged(original, live.Capture());
        Assert.Equal(0UL, live.GetDrawCounter(RngStreams.Event));
        Assert.Equal(0UL, live.GetDrawCounter(RngStreams.Pop));
    }

    [Fact]
    public void ValidationDoesNotModifyAnyProvidedSnapshot()
    {
        var live = RngRegistry.Create(42);
        live.NextUInt64(RngStreams.Event);
        var draft = RngResolutionDraft.Begin(live);
        draft.NextUInt64(RngStreams.Combat);

        var current = live.Capture();
        var basis = draft.BaseSnapshot;
        var proposed = draft.CaptureProposed();
        var currentSaved = Repack(current, current.Streams);
        var baseSaved = Repack(basis, basis.Streams);
        var proposedSaved = Repack(proposed, proposed.Streams);

        Assert.True(RngCandidatePreflight.IsCompatible(current, basis, proposed));
        AssertUnchanged(currentSaved, current);
        AssertUnchanged(baseSaved, basis);
        AssertUnchanged(proposedSaved, proposed);
    }

    [Fact]
    public void OnlyPublicMethodIsPureCompatibilityCheck_NotCommitOrApply()
    {
        var names = typeof(RngCandidatePreflight).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToArray();
        Assert.Equal(new[] { nameof(RngCandidatePreflight.IsCompatible) }, names);
    }
}
