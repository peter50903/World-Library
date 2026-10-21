using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 07 章 a 節：混齡開局的唯一亂數來源。開局屬於世界（同世界種子、不同歷史種子必須得到同一批開局人口），
/// 所以走世界種子的 ECOLOGY_INIT 流衍生的實體局部亂數 v1（不推進全域流計數器）；
/// 之後的生死才走歷史種子的 POP 流（<see cref="DemoPopulationDraws"/>）。抽樣索引：1＝年齡層與年齡、2＝性別。
/// </summary>
public sealed class DemoOpeningDraws
{
    public const string Namespace = "TEMP-HUMAN-OPENING-v1";
    private const double TwoPow53 = 9007199254740992d;
    private readonly RngRegistry _registry;

    public DemoOpeningDraws(RngRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    public ulong Age(long personId) => Local(personId).GetUInt64(1);

    public bool IsFemale(long personId)
        => Local(personId).GetUInt64(2) >> 11 < (ulong)Math.Floor(0.5 * TwoPow53);

    private EntityLocalRandom Local(long personId)
    {
        if (personId <= 0)
            throw new ArgumentOutOfRangeException(nameof(personId));
        return EntityLocalRandom.Create(_registry, EntityId.From((ulong)personId), RngStreams.EcologyInit, Namespace);
    }
}
