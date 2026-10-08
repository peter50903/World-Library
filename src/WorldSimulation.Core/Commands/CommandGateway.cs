using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Time;

namespace WorldSimulation.Core.Commands;

/// <summary>
/// Untrusted Command intent supplied by an external gateway caller. These identifiers are
/// externally assigned, opaque and NOT checked for global uniqueness in this partial module.
/// A Command is not a committed Event or historical fact.
/// </summary>
public sealed record CommandRequest(
    string? CommandId,
    string? ResolutionId,
    string? SourceId,
    string? CommandTypeId,
    EntityId? ActorId);

/// <summary>
/// An immutable, normalized Command intent with a snapshot of the single WorldClock's current
/// WorldTime. It does not prove actor existence, authority, preconditions or a world commit.
/// </summary>
public sealed record NormalizedCommand
{
    internal NormalizedCommand(
        string commandId,
        string resolutionId,
        string sourceId,
        string commandTypeId,
        EntityId? actorId,
        TimeSpan worldTime)
    {
        CommandId = commandId;
        ResolutionId = resolutionId;
        SourceId = sourceId;
        CommandTypeId = commandTypeId;
        ActorId = actorId;
        WorldTime = worldTime;
    }

    public string CommandId { get; }
    public string ResolutionId { get; }
    public string SourceId { get; }
    public string CommandTypeId { get; }
    public EntityId? ActorId { get; }
    public TimeSpan WorldTime { get; }
}

/// <summary>
/// The format-only Command portion of MOD_COMMAND. Reads the injected WorldClock but
/// never advances it, enqueues work, resolves a rule, draws RNG, or writes canonical state.
/// Actual ID allocation, authorization, deduplication, Trigger admission and Commit are later stages.
/// </summary>
public sealed class CommandGateway
{
    private readonly WorldClock _clock;

    public CommandGateway(WorldClock clock)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>
    /// Rejects malformed intent before creating any result. Identifiers are case-exact, untrimmed
    /// opaque strings; ActorId can be absent for system Commands, but cannot be the zero sentinel.
    /// </summary>
    public NormalizedCommand Normalize(CommandRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var commandId = RequireNonBlank(request.CommandId, nameof(request.CommandId));
        var resolutionId = RequireNonBlank(request.ResolutionId, nameof(request.ResolutionId));
        var sourceId = RequireNonBlank(request.SourceId, nameof(request.SourceId));
        var commandTypeId = RequireNonBlank(request.CommandTypeId, nameof(request.CommandTypeId));

        if (request.ActorId is { IsValid: false })
        {
            throw new ArgumentException("An explicitly supplied ActorId must be valid.", nameof(request));
        }

        // Capture a value, not a second timeline or a mutable WorldClock reference.
        return new NormalizedCommand(commandId, resolutionId, sourceId, commandTypeId, request.ActorId, _clock.Now);
    }

    private static string RequireNonBlank(string? value, string field)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"{field} cannot be blank.", field)
            : value;
}
