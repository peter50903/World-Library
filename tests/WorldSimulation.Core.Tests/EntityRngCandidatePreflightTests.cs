using System.Reflection;
using WorldSimulation.Core.Commit;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;
using Xunit;

namespace WorldSimulation.Core.Tests;

public sealed class EntityRngCandidatePreflightTests
{
    private static EntityRngResolutionCandidate NoopCandidate()
    {
        var entity = new EntityIdAllocator().Capture();
        var rng = RngRegistry.Create(42).Capture();
        return new EntityRngResolutionCandidate(
            entity, entity, Array.Empty<EntityIdentity>(), rng, rng);
    }

    private static EntityRngResolutionCandidate BuildCandidate(
        EntityCreationDraft entities, RngResolutionDraft rng)
        => new(
            entities.BaseSnapshot,
            entities.CaptureProposed(),
            entities.ProposedIdentities(),
            rng.BaseSnapshot,
            rng.CaptureProposed());

    [Fact]
    public void ConstructorRejectsEachOfFiveNullInputs()
    {
        var no = NoopCandidate();
        Assert.Throws<ArgumentNullException>(() =>
            new EntityRngResolutionCandidate(null!, no.EntityProposed, no.ProposedIdentities, no.RngBaseline, no.RngProposed));
        Assert.Throws<ArgumentNullException>(() =>
            new EntityRngResolutionCandidate(no.EntityBaseline, null!, no.ProposedIdentities, no.RngBaseline, no.RngProposed));
        Assert.Throws<ArgumentNullException>(() =>
            new EntityRngResolutionCandidate(no.EntityBaseline, no.EntityProposed, null!, no.RngBaseline, no.RngProposed));
        Assert.Throws<ArgumentNullException>(() =>
            new EntityRngResolutionCandidate(no.EntityBaseline, no.EntityProposed, no.ProposedIdentities, null!, no.RngProposed));
        Assert.Throws<ArgumentNullException>(() =>
            new EntityRngResolutionCandidate(no.EntityBaseline, no.EntityProposed, no.ProposedIdentities, no.RngBaseline, null!));
    }

    [Fact]
    public void PreflightRejectsThreeNullInputs()
    {
        var no = NoopCandidate();
        Assert.Throws<ArgumentNullException>(() =>
            EntityRngCandidatePreflight.IsCompatible(null!, no.RngBaseline, no));
        Assert.Throws<ArgumentNullException>(() =>
            EntityRngCandidatePreflight.IsCompatible(no.EntityBaseline, null!, no));
        Assert.Throws<ArgumentNullException>(() =>
            EntityRngCandidatePreflight.IsCompatible(no.EntityBaseline, no.RngBaseline, null!));
    }

    [Fact]
    public void BothDomainsUnchanged_AreCompatible()
    {
        var no = NoopCandidate();
        Assert.True(EntityRngCandidatePreflight.IsCompatible(no.EntityBaseline, no.RngBaseline, no));
        Assert.Empty(no.ProposedIdentities);
    }

    [Fact]
    public void ValidProvisionalEntityAndRngDraws_AreJointlyCompatible()
    {
        var liveEntity = new EntityIdAllocator();
        liveEntity.Allocate();
        var liveRng = RngRegistry.Create(42, 99);
        liveRng.NextUInt64(RngStreams.Event);
        var initialEntity = liveEntity.Capture();
        var initialRng = liveRng.Capture();

        var draftEntity = EntityCreationDraft.Begin(liveEntity);
        draftEntity.Propose("PERSON");
        draftEntity.Propose("Item");
        var draftRng = RngResolutionDraft.Begin(liveRng);
        draftRng.NextUInt64(RngStreams.Event);
        draftRng.NextUInt64(RngStreams.Combat);
        var candidate = BuildCandidate(draftEntity, draftRng);

        Assert.True(EntityRngCandidatePreflight.IsCompatible(liveEntity.Capture(), liveRng.Capture(), candidate));
        Assert.Equal(initialEntity, liveEntity.Capture());
        Assert.Equal(initialRng.Streams.ToArray(), liveRng.Capture().Streams.ToArray());
        Assert.Equal(initialRng.WorldSeed, liveRng.Capture().WorldSeed);
    }

    [Fact]
    public void StaleEntityOnly_ReturnsFalse()
    {
        var liveEntity = new EntityIdAllocator();
        var liveRng = RngRegistry.Create(42);
        var draftEntity = EntityCreationDraft.Begin(liveEntity);
        draftEntity.Propose("PERSON");
        var draftRng = RngResolutionDraft.Begin(liveRng);
        var candidate = BuildCandidate(draftEntity, draftRng);

        liveEntity.Allocate();

        Assert.False(EntityRngCandidatePreflight.IsCompatible(liveEntity.Capture(), liveRng.Capture(), candidate));
    }

    [Fact]
    public void StaleRngOnly_ReturnsFalse()
    {
        var liveEntity = new EntityIdAllocator();
        var liveRng = RngRegistry.Create(42);
        var draftEntity = EntityCreationDraft.Begin(liveEntity);
        var draftRng = RngResolutionDraft.Begin(liveRng);
        draftRng.NextUInt64(RngStreams.Event);
        var candidate = BuildCandidate(draftEntity, draftRng);

        liveRng.NextUInt64(RngStreams.Combat);

        Assert.False(EntityRngCandidatePreflight.IsCompatible(liveEntity.Capture(), liveRng.Capture(), candidate));
    }

    [Fact]
    public void BothDomainsStale_ReturnFalse()
    {
        var liveEntity = new EntityIdAllocator();
        var liveRng = RngRegistry.Create(42);
        var entityDraft = EntityCreationDraft.Begin(liveEntity);
        var rngDraft = RngResolutionDraft.Begin(liveRng);
        var candidate = BuildCandidate(entityDraft, rngDraft);

        liveEntity.Allocate();
        liveRng.NextUInt64(RngStreams.Event);

        Assert.False(EntityRngCandidatePreflight.IsCompatible(liveEntity.Capture(), liveRng.Capture(), candidate));
    }

    [Fact]
    public void MalformedRngCandidateCannotBeHiddenByStaleEntity()
    {
        var no = NoopCandidate();
        var currentEntity = new EntityIdAllocatorSnapshot(1, 2, false);
        var badRng = new RngSnapshot(
            99, no.RngProposed.WorldSeed, no.RngProposed.HistorySeed, no.RngProposed.Streams);
        var candidate = new EntityRngResolutionCandidate(
            no.EntityBaseline, no.EntityProposed, no.ProposedIdentities, no.RngBaseline, badRng);

        Assert.Throws<ArgumentException>(() =>
            EntityRngCandidatePreflight.IsCompatible(currentEntity, no.RngBaseline, candidate));
    }

    [Fact]
    public void MalformedEntityCandidateCannotBeHiddenByStaleRng()
    {
        var no = NoopCandidate();
        var currentRng = RngRegistry.Create(42);
        currentRng.NextUInt64(RngStreams.Event);
        var invalidEntity = new EntityIdAllocatorSnapshot(99, 1, false);
        var candidate = new EntityRngResolutionCandidate(
            no.EntityBaseline, invalidEntity, no.ProposedIdentities, no.RngBaseline, no.RngProposed);

        Assert.Throws<ArgumentException>(() =>
            EntityRngCandidatePreflight.IsCompatible(no.EntityBaseline, currentRng.Capture(), candidate));
    }

    [Fact]
    public void MalformedRngCurrentIsRejectedEvenWhenEntityStale()
    {
        var no = NoopCandidate();
        var badCurrent = new RngSnapshot(
            no.RngBaseline.Version, no.RngBaseline.WorldSeed,
            no.RngBaseline.HistorySeed, no.RngBaseline.Streams.Skip(1));

        Assert.Throws<ArgumentException>(() => EntityRngCandidatePreflight.IsCompatible(
            new EntityIdAllocatorSnapshot(1, 2, false), badCurrent, no));
    }

    [Fact]
    public void WrongEntityProvisionalSequenceMakesBothDomainsIncompatible()
    {
        var no = NoopCandidate();
        var wrongId = new EntityIdentity(EntityId.From(2), "PERSON");
        var candidate = new EntityRngResolutionCandidate(
            no.EntityBaseline, new EntityIdAllocatorSnapshot(1, 2, false),
            new[] { wrongId }, no.RngBaseline, no.RngProposed);

        Assert.False(EntityRngCandidatePreflight.IsCompatible(no.EntityBaseline, no.RngBaseline, candidate));
    }

    [Fact]
    public void OtherRngRootsMakesCombinedCandidateIncompatible()
    {
        var no = NoopCandidate();
        var otherRoots = RngRegistry.Create(43).Capture();
        var candidate = new EntityRngResolutionCandidate(
            no.EntityBaseline, no.EntityProposed, no.ProposedIdentities, no.RngBaseline, otherRoots);

        Assert.False(EntityRngCandidatePreflight.IsCompatible(no.EntityBaseline, no.RngBaseline, candidate));
    }

    [Fact]
    public void IdentityListIsDetachedReadOnlyAndDoesNotChangeAfterSourceMutates()
    {
        var no = NoopCandidate();
        var source = new List<EntityIdentity> { new(EntityId.From(1), "PERSON") };
        var candidate = new EntityRngResolutionCandidate(
            no.EntityBaseline, new EntityIdAllocatorSnapshot(1, 2, false),
            source, no.RngBaseline, no.RngProposed);

        var view = Assert.IsAssignableFrom<IList<EntityIdentity>>(candidate.ProposedIdentities);
        Assert.Throws<NotSupportedException>(() => view.Clear());
        Assert.Throws<NotSupportedException>(() =>
            view[0] = new EntityIdentity(EntityId.From(7), "OTHER"));
        Assert.Throws<NotSupportedException>(() =>
            view.Add(new EntityIdentity(EntityId.From(8), "OTHER")));

        source.Clear();
        source.Add(new EntityIdentity(EntityId.From(8), "WRONG"));
        Assert.Single(candidate.ProposedIdentities);
        Assert.Equal(1UL, candidate.ProposedIdentities[0].Id.Value);
        Assert.True(EntityRngCandidatePreflight.IsCompatible(no.EntityBaseline, no.RngBaseline, candidate));
    }

    [Fact]
    public void CandidateCapturesAreDetachedFromLaterDraftEvolution()
    {
        var liveEntity = new EntityIdAllocator();
        var liveRng = RngRegistry.Create(42);
        var entity = EntityCreationDraft.Begin(liveEntity);
        var rng = RngResolutionDraft.Begin(liveRng);
        entity.Propose("ONE");
        rng.NextUInt64(RngStreams.Event);
        var captured = BuildCandidate(entity, rng);
        var savedStreamStates = captured.RngProposed.Streams.ToArray();

        entity.Propose("TWO");
        rng.NextUInt64(RngStreams.Combat);

        Assert.Single(captured.ProposedIdentities);
        Assert.Equal(new EntityIdAllocatorSnapshot(1, 2, false), captured.EntityProposed);
        Assert.Equal(savedStreamStates, captured.RngProposed.Streams.ToArray());
        Assert.True(EntityRngCandidatePreflight.IsCompatible(liveEntity.Capture(), liveRng.Capture(), captured));
    }

    [Fact]
    public void ExhaustionBoundaryIsDelegatedToExistingEntityPreflight()
    {
        var baseEntity = new EntityIdAllocatorSnapshot(1, ulong.MaxValue, false);
        var proposedEntity = new EntityIdAllocatorSnapshot(1, ulong.MaxValue, true);
        var rng = RngRegistry.Create(42).Capture();
        var candidate = new EntityRngResolutionCandidate(
            baseEntity, proposedEntity,
            new[] { new EntityIdentity(EntityId.From(ulong.MaxValue), "LAST") }, rng, rng);

        Assert.True(EntityRngCandidatePreflight.IsCompatible(baseEntity, rng, candidate));

        var wrongCandidate = new EntityRngResolutionCandidate(
            baseEntity, proposedEntity,
            new[] {
                new EntityIdentity(EntityId.From(ulong.MaxValue), "LAST"),
                new EntityIdentity(EntityId.From(ulong.MaxValue), "DUPLICATE")
            }, rng, rng);

        Assert.False(EntityRngCandidatePreflight.IsCompatible(baseEntity, rng, wrongCandidate));
    }

    [Fact]
    public void CheckingDoesNotAdvanceEitherLiveAllocatorOrRng()
    {
        var liveEntity = new EntityIdAllocator();
        var liveRng = RngRegistry.Create(42);
        var beforeEntity = liveEntity.Capture();
        var beforeRng = liveRng.Capture();

        var entity = EntityCreationDraft.Begin(liveEntity);
        var rng = RngResolutionDraft.Begin(liveRng);
        entity.Propose("PERSON");
        rng.NextUInt64(RngStreams.Combat);

        Assert.True(EntityRngCandidatePreflight.IsCompatible(
            liveEntity.Capture(), liveRng.Capture(), BuildCandidate(entity, rng)));
        Assert.Equal(beforeEntity, liveEntity.Capture());
        Assert.Equal(beforeRng.Streams.ToArray(), liveRng.Capture().Streams.ToArray());
        Assert.Equal(beforeRng.WorldSeed, liveRng.Capture().WorldSeed);
        Assert.Equal(beforeRng.HistorySeed, liveRng.Capture().HistorySeed);
    }

    [Fact]
    public void CandidateAndPreflightHaveNoPublicWorldMutationMethods()
    {
        var candidateMethods = typeof(EntityRngResolutionCandidate).GetMethods(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToArray();
        Assert.Empty(candidateMethods);

        var preflightMethods = typeof(EntityRngCandidatePreflight).GetMethods(
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName)
            .Select(m => m.Name)
            .ToArray();
        Assert.Equal(new[] { "IsCompatible" }, preflightMethods);

        Assert.All(typeof(EntityRngResolutionCandidate).GetProperties(), p => Assert.Null(p.SetMethod));
    }
}
