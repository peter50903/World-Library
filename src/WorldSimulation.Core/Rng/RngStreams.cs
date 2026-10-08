namespace WorldSimulation.Core.Rng;

/// <summary>The fixed, case-exact RNG v1 stream IDs. No other stream ID is accepted.</summary>
public static class RngStreams
{
    public const string Geography = "GEOGRAPHY";
    public const string Resource = "RESOURCE";
    public const string EcologyInit = "ECOLOGY_INIT";
    public const string Character = "CHARACTER";
    public const string Pop = "POP";
    public const string Event = "EVENT";
    public const string Combat = "COMBAT";
    public const string Religion = "RELIGION";
    public const string Dungeon = "DUNGEON";
    public const string Threat = "THREAT";

    /// <summary>Streams derived from WorldSeed, in fixed order.</summary>
    public static IReadOnlyList<string> WorldStreams { get; } = Array.AsReadOnly(new[] { Geography, Resource, EcologyInit });

    /// <summary>Streams derived from the resolved HistorySeed, in fixed order.</summary>
    public static IReadOnlyList<string> HistoryStreams { get; } =
        Array.AsReadOnly(new[] { Character, Pop, Event, Combat, Religion, Dungeon, Threat });

    /// <summary>All 10 streams: world streams first, then history streams.</summary>
    public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(WorldStreams.Concat(HistoryStreams).ToArray());
}
