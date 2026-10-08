using System.Reflection;
using WorldSimulation.Core.Entity;
using Xunit;

namespace WorldSimulation.Core.Tests;

public sealed class EntityCandidatePreflightTests
{
    private static EntityIdentity Identity(ulong id, string type = "TYPE_A")
        => new(EntityId.From(id), type);

    [Fact]
    public void NullParameters_AreAllRejected()
    {
        var s = new EntityIdAllocator().Capture();
        var empty = Array.Empty<EntityIdentity>();
        Assert.Throws<ArgumentNullException>(() => EntityCandidatePreflight.IsCompatible(null!, s, s, empty));
        Assert.Throws<ArgumentNullException>(() => EntityCandidatePreflight.IsCompatible(s, null!, s, empty));
        Assert.Throws<ArgumentNullException>(() => EntityCandidatePreflight.IsCompatible(s, s, null!, empty));
        Assert.Throws<ArgumentNullException>(() => EntityCandidatePreflight.IsCompatible(s, s, s, null!));
    }

    [Fact]
    public void EmptyProposalFromFreshAllocator_IsCompatible()
    {
        var allocator = new EntityIdAllocator();
        var baseline = allocator.Capture();

        Assert.True(EntityCandidatePreflight.IsCompatible(baseline, baseline, baseline, Array.Empty<EntityIdentity>()));
        Assert.Equal(new EntityIdAllocatorSnapshot(1, 1, false), allocator.Capture());
    }

    [Fact]
    public void ValidOrderedProposals_MatchCandidateFromUsedAllocator()
    {
        var live = new EntityIdAllocator();
        for (var i = 0; i < 8; i++) live.Allocate();
        var old = live.Capture();

        var draft = EntityCreationDraft.Begin(live);
        var a = draft.Propose("TYPE_A");
        var b = draft.Propose(" type_b ");
        var c = draft.Propose("TYPE_A");

        Assert.Equal(new ulong[] { 9, 10, 11 }, new[] { a.Id.Value, b.Id.Value, c.Id.Value });
        Assert.True(EntityCandidatePreflight.IsCompatible(live.Capture(), draft.BaseSnapshot,
            draft.CaptureProposed(), draft.ProposedIdentities()));
        Assert.Equal(old, live.Capture());
    }

    [Fact]
    public void StaleCurrentIsRejectedWhenAnotherActorAllocated()
    {
        var live = new EntityIdAllocator();
        var draft = EntityCreationDraft.Begin(live);
        draft.Propose("TYPE_A");
        live.Allocate();

        Assert.False(EntityCandidatePreflight.IsCompatible(live.Capture(), draft.BaseSnapshot,
            draft.CaptureProposed(), draft.ProposedIdentities()));
    }

    [Fact]
    public void ChangedExhaustedFlagEvenWithSameNextValue_IsStale()
    {
        var baseline = new EntityIdAllocatorSnapshot(1, ulong.MaxValue, false);
        var current = new EntityIdAllocatorSnapshot(1, ulong.MaxValue, true);
        Assert.False(EntityCandidatePreflight.IsCompatible(current, baseline, baseline, Array.Empty<EntityIdentity>()));
    }

    [Fact]
    public void CandidateCannotAdvanceWithoutExactlyMatchingProposals()
    {
        var live = new EntityIdAllocator();
        var draft = EntityCreationDraft.Begin(live);
        draft.Propose("TYPE_A");
        draft.Propose("TYPE_B");
        var baseline = draft.BaseSnapshot;
        var candidate = draft.CaptureProposed();

        Assert.False(EntityCandidatePreflight.IsCompatible(baseline, baseline, candidate,
            new[] { Identity(1) }));
        Assert.False(EntityCandidatePreflight.IsCompatible(baseline, baseline, candidate,
            Array.Empty<EntityIdentity>()));
        Assert.False(EntityCandidatePreflight.IsCompatible(baseline, baseline,
            new EntityIdAllocatorSnapshot(1, 4, false), draft.ProposedIdentities()));
    }

    [Fact]
    public void WrongIdOrSkippedIdIsIncompatible()
    {
        var baseline = new EntityIdAllocator().Capture();
        var candidate = new EntityIdAllocatorSnapshot(1, 3, false);
        Assert.False(EntityCandidatePreflight.IsCompatible(baseline, baseline, candidate,
            new[] { Identity(2), Identity(3) }));
        Assert.False(EntityCandidatePreflight.IsCompatible(baseline, baseline, candidate,
            new[] { Identity(1), Identity(3) }));
    }

    [Fact]
    public void DuplicatedAndOutOfOrderIdsAreIncompatible()
    {
        var baseline = new EntityIdAllocator().Capture();
        var candidate = new EntityIdAllocatorSnapshot(1, 3, false);
        Assert.False(EntityCandidatePreflight.IsCompatible(baseline, baseline, candidate,
            new[] { Identity(1), Identity(1) }));
        Assert.False(EntityCandidatePreflight.IsCompatible(baseline, baseline, candidate,
            new[] { Identity(2), Identity(1) }));
    }

    [Fact]
    public void NullProposedMemberIsRejected()
    {
        var baseline = new EntityIdAllocator().Capture();
        Assert.Throws<ArgumentException>(() => EntityCandidatePreflight.IsCompatible(
            baseline, baseline, baseline, new EntityIdentity[] { null! }));
    }

    [Fact]
    public void MalformedVersionOnEachSnapshot_IsRejectedBeforeComparison()
    {
        var baseline = new EntityIdAllocator().Capture();
        var bad = new EntityIdAllocatorSnapshot(2, 1, false);
        var empty = Array.Empty<EntityIdentity>();

        Assert.Throws<ArgumentException>(() => EntityCandidatePreflight.IsCompatible(bad, baseline, baseline, empty));
        Assert.Throws<ArgumentException>(() => EntityCandidatePreflight.IsCompatible(baseline, bad, baseline, empty));
        Assert.Throws<ArgumentException>(() => EntityCandidatePreflight.IsCompatible(baseline, baseline, bad, empty));
    }

    [Fact]
    public void InvalidNextValueOrExhaustedFlagInEachPosition_IsRejected()
    {
        var valid = new EntityIdAllocator().Capture();
        var invalid = new[] {
            new EntityIdAllocatorSnapshot(1, 0, false),
            new EntityIdAllocatorSnapshot(1, 0, true),
            new EntityIdAllocatorSnapshot(1, 15, true)
        };

        foreach (var bad in invalid)
        {
            Assert.Throws<ArgumentException>(() => EntityCandidatePreflight.IsCompatible(bad, valid, valid, Array.Empty<EntityIdentity>()));
            Assert.Throws<ArgumentException>(() => EntityCandidatePreflight.IsCompatible(valid, bad, valid, Array.Empty<EntityIdentity>()));
            Assert.Throws<ArgumentException>(() => EntityCandidatePreflight.IsCompatible(valid, valid, bad, Array.Empty<EntityIdentity>()));
        }
    }

    [Fact]
    public void StaleSnapshotCannotHideMalformedCandidate()
    {
        var current = new EntityIdAllocatorSnapshot(1, 2, false);
        var baseline = new EntityIdAllocatorSnapshot(1, 1, false);
        var invalid = new EntityIdAllocatorSnapshot(1, 0, false);
        Assert.Throws<ArgumentException>(() => EntityCandidatePreflight.IsCompatible(
            current, baseline, invalid, Array.Empty<EntityIdentity>()));
    }

    [Fact]
    public void MaxIdMayBeProposedExactlyOnceAndSetsExhausted()
    {
        var live = EntityIdAllocator.Restore(new EntityIdAllocatorSnapshot(1, ulong.MaxValue, false));
        var original = live.Capture();
        var draft = EntityCreationDraft.Begin(live);
        var last = draft.Propose("MAX");

        Assert.Equal(ulong.MaxValue, last.Id.Value);
        Assert.True(EntityCandidatePreflight.IsCompatible(live.Capture(), draft.BaseSnapshot,
            draft.CaptureProposed(), draft.ProposedIdentities()));
        Assert.Equal(new EntityIdAllocatorSnapshot(1, ulong.MaxValue, true), draft.CaptureProposed());
        Assert.Equal(original, live.Capture());
    }

    [Fact]
    public void MaxIdCannotBeProposedTwice()
    {
        var baseline = new EntityIdAllocatorSnapshot(1, ulong.MaxValue, false);
        var candidate = new EntityIdAllocatorSnapshot(1, ulong.MaxValue, true);
        Assert.False(EntityCandidatePreflight.IsCompatible(baseline, baseline, candidate,
            new[] { Identity(ulong.MaxValue), Identity(ulong.MaxValue) }));
    }

    [Fact]
    public void ExhaustedAllocatorAllowsEmptyButNoNonemptyProposal()
    {
        var baseline = new EntityIdAllocatorSnapshot(1, ulong.MaxValue, true);
        Assert.True(EntityCandidatePreflight.IsCompatible(baseline, baseline, baseline,
            Array.Empty<EntityIdentity>()));
        Assert.False(EntityCandidatePreflight.IsCompatible(baseline, baseline, baseline,
            new[] { Identity(ulong.MaxValue) }));
    }

    [Fact]
    public void TwoCompetingDraftsLookCompatibleBeforeAnyRealCommitButNotAfterLiveMove()
    {
        var live = new EntityIdAllocator();
        var a = EntityCreationDraft.Begin(live);
        var b = EntityCreationDraft.Begin(live);
        a.Propose("TYPE_A");
        b.Propose("TYPE_B");
        var before = live.Capture();

        Assert.True(EntityCandidatePreflight.IsCompatible(before, a.BaseSnapshot,
            a.CaptureProposed(), a.ProposedIdentities()));
        Assert.True(EntityCandidatePreflight.IsCompatible(before, b.BaseSnapshot,
            b.CaptureProposed(), b.ProposedIdentities()));
        Assert.Equal(before, live.Capture());

        // A real Commit Engine must atomically recheck the world allocator. This
        // demonstration only advances live through an existing public API.
        live.Allocate();
        Assert.False(EntityCandidatePreflight.IsCompatible(live.Capture(), a.BaseSnapshot,
            a.CaptureProposed(), a.ProposedIdentities()));
        Assert.False(EntityCandidatePreflight.IsCompatible(live.Capture(), b.BaseSnapshot,
            b.CaptureProposed(), b.ProposedIdentities()));
    }

    [Fact]
    public void CheckingDoesNotChangeSnapshotsProposalsOrLiveAllocator()
    {
        var live = new EntityIdAllocator();
        live.Allocate();
        var initial = live.Capture();
        var draft = EntityCreationDraft.Begin(live);
        draft.Propose("TYPE_A");
        var current = live.Capture();
        var baseline = draft.BaseSnapshot;
        var candidate = draft.CaptureProposed();
        var proposals = draft.ProposedIdentities().ToArray();

        Assert.True(EntityCandidatePreflight.IsCompatible(current, baseline, candidate, proposals));
        Assert.Equal(initial, live.Capture());
        Assert.Equal(baseline, draft.BaseSnapshot);
        Assert.Equal(candidate, draft.CaptureProposed());
        Assert.Equal(new[] { Identity(2) }, proposals);
    }

    [Fact]
    public void PublicApiDoesNotExposeAnyWorldMutator()
    {
        var publicMethods = typeof(EntityCandidatePreflight).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .OrderBy(s => s)
            .ToArray();
        Assert.Equal(new[] { "IsCompatible" }, publicMethods);
    }
}
