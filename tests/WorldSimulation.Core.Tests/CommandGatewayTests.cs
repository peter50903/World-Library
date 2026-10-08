using System.Reflection;
using WorldSimulation.Core.Commands;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;
using WorldSimulation.Core.Scheduling;
using WorldSimulation.Core.Time;
using Xunit;

namespace WorldSimulation.Core.Tests;

public sealed class CommandGatewayTests
{
    private static CommandRequest Request(
        string? commandId = "CMD-A",
        string? resolutionId = "RES-A",
        string? sourceId = "SYSTEM",
        string? commandTypeId = "EXAMPLE_INTENT",
        EntityId? actorId = null)
        => new(commandId, resolutionId, sourceId, commandTypeId, actorId);

    [Fact]
    public void Constructor_RequiresSharedWorldClock()
    {
        Assert.Throws<ArgumentNullException>(() => new CommandGateway(null!));
    }

    [Fact]
    public void Normalize_RejectsNullRequest()
    {
        Assert.Throws<ArgumentNullException>(() => new CommandGateway(new WorldClock()).Normalize(null!));
    }

    [Fact]
    public void SystemCommandWithoutActor_AtWorldOrigin_IsNormalized()
    {
        var clock = new WorldClock();
        var command = new CommandGateway(clock).Normalize(Request());

        Assert.Equal("CMD-A", command.CommandId);
        Assert.Equal("RES-A", command.ResolutionId);
        Assert.Equal("SYSTEM", command.SourceId);
        Assert.Equal("EXAMPLE_INTENT", command.CommandTypeId);
        Assert.Null(command.ActorId);
        Assert.Equal(TimeSpan.Zero, command.WorldTime);
        Assert.Equal(TimeSpan.Zero, clock.Now);
    }

    [Fact]
    public void ValidActorId_IsPreserved()
    {
        var actor = EntityId.From(ulong.MaxValue);
        var result = new CommandGateway(new WorldClock()).Normalize(Request(actorId: actor));
        Assert.Equal(actor, result.ActorId);
    }

    [Fact]
    public void ExplicitDefaultActorId_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            new CommandGateway(new WorldClock()).Normalize(Request(actorId: default(EntityId))));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void BlankCommandId_IsRejected(string? value)
    {
        Assert.Throws<ArgumentException>(() =>
            new CommandGateway(new WorldClock()).Normalize(Request(commandId: value)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void BlankResolutionId_IsRejected(string? value)
    {
        Assert.Throws<ArgumentException>(() =>
            new CommandGateway(new WorldClock()).Normalize(Request(resolutionId: value)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void BlankSourceId_IsRejected(string? value)
    {
        Assert.Throws<ArgumentException>(() =>
            new CommandGateway(new WorldClock()).Normalize(Request(sourceId: value)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void BlankCommandTypeId_IsRejected(string? value)
    {
        Assert.Throws<ArgumentException>(() =>
            new CommandGateway(new WorldClock()).Normalize(Request(commandTypeId: value)));
    }

    [Fact]
    public void Identifiers_AreOpaqueCaseExactAndNotTrimmed()
    {
        var input = Request(" cmd ", "res", "system", "IntentType");
        var result = new CommandGateway(new WorldClock()).Normalize(input);
        Assert.Equal(" cmd ", result.CommandId);
        Assert.Equal("res", result.ResolutionId);
        Assert.Equal("system", result.SourceId);
        Assert.Equal("IntentType", result.CommandTypeId);

        var differentlyCased = new CommandGateway(new WorldClock())
            .Normalize(Request(" CMD ", "res", "system", "IntentType"));
        Assert.NotEqual(result, differentlyCased);
    }

    [Fact]
    public void SameRequestAtSameTime_HasValueEqualityButDifferentIdentity()
    {
        var gateway = new CommandGateway(new WorldClock());
        var a = gateway.Normalize(Request(actorId: EntityId.From(7)));
        var b = gateway.Normalize(Request(actorId: EntityId.From(7)));

        Assert.Equal(a, b);
        Assert.NotSame(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void ClockAdvancesViaScheduler_NewEnvelopeCapturesNewTime()
    {
        var clock = new WorldClock();
        var scheduler = new Scheduler(clock);
        var gateway = new CommandGateway(clock);

        var before = gateway.Normalize(Request());
        scheduler.Schedule(TimeSpan.FromHours(3), "DUE");
        var due = scheduler.TakeNextDueBatch();
        var after = gateway.Normalize(Request());

        Assert.Single(due);
        Assert.Equal(TimeSpan.FromHours(3), clock.Now);
        Assert.Equal(TimeSpan.Zero, before.WorldTime);
        Assert.Equal(TimeSpan.FromHours(3), after.WorldTime);
        Assert.NotEqual(before, after);
    }

    [Fact]
    public void RejectedCommand_DoesNotChangeClockSchedulerOrRng()
    {
        var clock = new WorldClock();
        var scheduler = new Scheduler(clock);
        scheduler.Schedule(TimeSpan.FromHours(2), "WORK");
        var originalDue = scheduler.PeekNext();
        var rng = RngRegistry.Create(42);
        var rngBefore = rng.Capture();

        Assert.Throws<ArgumentException>(() =>
            new CommandGateway(clock).Normalize(Request(sourceId: " ")));

        Assert.Equal(TimeSpan.Zero, clock.Now);
        Assert.Equal(1, scheduler.Count);
        Assert.Equal(originalDue, scheduler.PeekNext());
        Assert.Equal(rngBefore.Streams, rng.Capture().Streams);
    }

    [Fact]
    public void ReusedCallerSuppliedIds_AreOnlyNormalized_NotDeduplicated()
    {
        var gateway = new CommandGateway(new WorldClock());
        var a = gateway.Normalize(Request());
        var b = gateway.Normalize(Request());

        // ID allocation, uniqueness and deduplication belong to a later authoritative module.
        Assert.Equal(a.CommandId, b.CommandId);
        Assert.Equal(a.ResolutionId, b.ResolutionId);
    }

    [Fact]
    public void ProductionSurface_IsReadOnlyAndContainsNoCommitOrEventWriter()
    {
        Assert.All(typeof(NormalizedCommand).GetProperties(), property => Assert.Null(property.SetMethod));

        var methods = typeof(CommandGateway).GetMethods(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        var only = Assert.Single(methods);
        Assert.Equal(nameof(CommandGateway.Normalize), only.Name);
        Assert.Equal(typeof(NormalizedCommand), only.ReturnType);
    }
}
