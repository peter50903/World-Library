using System.Reflection;
using WorldSimulation.Core.Entity;
using Xunit;

namespace WorldSimulation.Core.Tests;

public sealed class EntityCreationDraftTests
{
    [Fact]
    public void Begin_NullAllocator_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => EntityCreationDraft.Begin(null!));
    }

    [Fact]
    public void FreshDraft_CreatesOnlyProvisionalIds_WithoutConsumingLiveAllocator()
    {
        var live = new EntityIdAllocator();
        var before = live.Capture();
        var draft = EntityCreationDraft.Begin(live);

        var a = draft.Propose("REGION");
        var b = draft.Propose("CHARACTER");
        var c = draft.Propose("RESOURCE");
        Assert.Equal(new ulong[] { 1, 2, 3 }, new[] { a.Id.Value, b.Id.Value, c.Id.Value });
        Assert.Equal(new[] { a, b, c }, draft.ProposedIdentities().ToArray());
        Assert.Equal(new EntityIdAllocatorSnapshot(1, 4, false), draft.CaptureProposed());
        Assert.Equal(before, draft.BaseSnapshot);
        Assert.Equal(before, live.Capture());

        // The live allocator still issues the first world ID: draft IDs were never committed.
        Assert.Equal(1UL, live.Allocate().Value);
    }

    [Fact]
    public void DraftContinuesFromAnAlreadyUsedGlobalAllocatorPosition()
    {
        var live = new EntityIdAllocator();
        for (var i = 0; i < 9; i++) live.Allocate();
        var before = live.Capture();

        var draft = EntityCreationDraft.Begin(live);
        Assert.Equal(new EntityIdAllocatorSnapshot(1, 10, false), draft.BaseSnapshot);
        Assert.Equal(10UL, draft.Propose("TYPE_A").Id.Value);
        Assert.Equal(11UL, draft.Propose("TYPE_B").Id.Value);
        Assert.Equal(12UL, draft.CaptureProposed().NextValue);
        Assert.Equal(before, live.Capture());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void BlankOrMissingTypeId_IsRejectedBeforeAllocating(string? typeId)
    {
        var live = new EntityIdAllocator();
        var draft = EntityCreationDraft.Begin(live);
        var before = draft.CaptureProposed();

        Assert.Throws<ArgumentException>(() => draft.Propose(typeId!));

        Assert.Equal(before, draft.CaptureProposed());
        Assert.Empty(draft.ProposedIdentities());
        Assert.Equal(before, live.Capture());
        Assert.Equal(1UL, draft.Propose("VALID").Id.Value);
    }

    [Fact]
    public void TypeIdIsOpaqueCaseExactAndNotTrimmed()
    {
        var draft = EntityCreationDraft.Begin(new EntityIdAllocator());
        var a = draft.Propose(" Type_X ");
        var b = draft.Propose("type_x");

        Assert.Equal(" Type_X ", a.TypeId);
        Assert.Equal("type_x", b.TypeId);
        Assert.NotEqual(a.TypeId, b.TypeId);
        Assert.True(a.Id.IsValid);
        Assert.True(b.Id.IsValid);
    }

    [Fact]
    public void DifferentProvisionalEntityTypesShareOneMonotonicSequence()
    {
        var draft = EntityCreationDraft.Begin(new EntityIdAllocator());
        var types = new[] { "POP", "REGION", "POP", "ITEM", "TYPE_A", "TYPE_A" };
        var ids = types.Select(type => draft.Propose(type).Id).ToArray();

        Assert.Equal(new ulong[] { 1, 2, 3, 4, 5, 6 }, ids.Select(x => x.Value).ToArray());
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(id.IsValid));
        Assert.Equal(types, draft.ProposedIdentities().Select(p => p.TypeId).ToArray());
    }

    [Fact]
    public void TwoDraftsCanCompeteForSameProvisionalIdWithoutMutatingLive()
    {
        var live = new EntityIdAllocator();
        live.Allocate();
        var before = live.Capture();
        var a = EntityCreationDraft.Begin(live);
        var b = EntityCreationDraft.Begin(live);

        var first = a.Propose("TYPE_A");
        var second = b.Propose("TYPE_B");
        Assert.Equal(first.Id, second.Id); // Cannot both become committed entities.
        Assert.Equal(2UL, first.Id.Value);
        a.Propose("TYPE_C");
        Assert.Equal(4UL, a.CaptureProposed().NextValue);
        Assert.Equal(3UL, b.CaptureProposed().NextValue);
        Assert.Equal(before, live.Capture());
    }

    [Fact]
    public void DraftBaselineAndPreviouslyCapturedProposalRemainDetached()
    {
        var live = new EntityIdAllocator();
        var draft = EntityCreationDraft.Begin(live);
        var before = draft.BaseSnapshot;
        var initialCandidate = draft.CaptureProposed();
        var initialList = draft.ProposedIdentities();

        draft.Propose("TYPE_A");
        var listWithOne = draft.ProposedIdentities();
        var candidateWithOne = draft.CaptureProposed();
        draft.Propose("TYPE_B");
        live.Allocate();

        Assert.Equal(new EntityIdAllocatorSnapshot(1, 1, false), before);
        Assert.Equal(new EntityIdAllocatorSnapshot(1, 1, false), initialCandidate);
        Assert.Equal(new EntityIdAllocatorSnapshot(1, 2, false), candidateWithOne);
        Assert.Empty(initialList);
        Assert.Single(listWithOne);
        Assert.Equal("TYPE_A", listWithOne[0].TypeId);
        Assert.Equal(2, draft.ProposedIdentities().Count);
        Assert.Equal(3UL, draft.CaptureProposed().NextValue);
        Assert.Equal(2UL, live.NextValue);
    }

    [Fact]
    public void ProposalViewsAreReadOnlyAndEachCaptureIsDetached()
    {
        var draft = EntityCreationDraft.Begin(new EntityIdAllocator());
        draft.Propose("TYPE_A");
        var first = draft.ProposedIdentities();
        var mutableContract = Assert.IsAssignableFrom<IList<EntityIdentity>>(first);
        Assert.Throws<NotSupportedException>(() => mutableContract.Clear());
        Assert.Throws<NotSupportedException>(() =>
            mutableContract[0] = new EntityIdentity(EntityId.From(99), "TYPE_Z"));
        Assert.Throws<NotSupportedException>(() =>
            mutableContract.Add(new EntityIdentity(EntityId.From(100), "TYPE_Z")));

        draft.Propose("TYPE_B");
        Assert.Single(first);
        Assert.Equal(2, draft.ProposedIdentities().Count);
    }

    [Fact]
    public void CandidateSnapshotRestoresTheNextUncommittedPositionWithoutTouchingLive()
    {
        var live = new EntityIdAllocator();
        live.Allocate();
        var before = live.Capture();
        var draft = EntityCreationDraft.Begin(live);
        draft.Propose("TYPE_A");
        draft.Propose("TYPE_B");

        var restored = EntityIdAllocator.Restore(draft.CaptureProposed());
        Assert.Equal(4UL, restored.Allocate().Value);
        Assert.Equal(before, live.Capture());
        Assert.Equal(before, draft.BaseSnapshot);
    }

    [Fact]
    public void LastPossibleIdCanBeProposedExactlyOnceThenFailsClosed()
    {
        var live = EntityIdAllocator.Restore(
            new EntityIdAllocatorSnapshot(1, ulong.MaxValue, false));
        var original = live.Capture();
        var draft = EntityCreationDraft.Begin(live);

        var last = draft.Propose("MAX_ID");
        Assert.Equal(ulong.MaxValue, last.Id.Value);
        Assert.Equal(new EntityIdAllocatorSnapshot(1, ulong.MaxValue, true), draft.CaptureProposed());

        var atLimit = draft.CaptureProposed();
        var proposals = draft.ProposedIdentities().ToArray();
        Assert.Throws<InvalidOperationException>(() => draft.Propose("SECOND"));
        Assert.Throws<InvalidOperationException>(() => draft.Propose("SECOND"));
        Assert.Equal(atLimit, draft.CaptureProposed());
        Assert.Equal(proposals, draft.ProposedIdentities().ToArray());
        Assert.Equal(original, live.Capture());
    }

    [Fact]
    public void AlreadyExhaustedGlobalAllocatorProducesNoCandidates()
    {
        var live = EntityIdAllocator.Restore(
            new EntityIdAllocatorSnapshot(1, ulong.MaxValue, true));
        var before = live.Capture();
        var draft = EntityCreationDraft.Begin(live);

        Assert.True(draft.BaseSnapshot.Exhausted);
        Assert.Throws<InvalidOperationException>(() => draft.Propose("TYPE_A"));
        Assert.Empty(draft.ProposedIdentities());
        Assert.Equal(before, draft.CaptureProposed());
        Assert.Equal(before, live.Capture());
    }

    [Fact]
    public void InvalidTypeDoesNotAdvanceNearExhaustion()
    {
        var live = EntityIdAllocator.Restore(
            new EntityIdAllocatorSnapshot(1, ulong.MaxValue, false));
        var draft = EntityCreationDraft.Begin(live);
        Assert.Throws<ArgumentException>(() => draft.Propose(" "));
        Assert.Equal(ulong.MaxValue, draft.Propose("VALID").Id.Value);
        Assert.Equal(new EntityIdAllocatorSnapshot(1, ulong.MaxValue, true), draft.CaptureProposed());
        Assert.False(live.IsExhausted);
    }

    [Fact]
    public void PublicApiHasNoCommitRegisterOrLiveMutator()
    {
        var methods = typeof(EntityCreationDraft).GetMethods(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(
            new[] { "CaptureProposed", "Propose", "ProposedIdentities" },
            methods.Where(m => !m.IsSpecialName).Select(m => m.Name).OrderBy(n => n).ToArray());
        Assert.All(typeof(EntityCreationDraft).GetProperties(), p => Assert.Null(p.SetMethod));
        Assert.Null(typeof(EntityCreationDraft).GetProperty("LiveAllocator"));
    }
}
