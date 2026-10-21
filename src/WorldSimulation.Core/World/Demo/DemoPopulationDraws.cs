using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 04 章 a 節：人口抽樣動作代碼（抽樣索引＝週×8＋動作）。</summary>
public enum DemoPopulationAction
{
    NaturalDeath = 1,
    NewbornSex = 2,
    Miscarriage = 3,
    Conception = 4
}

/// <summary>
/// 第 04 章 a 節（41A SEED_POP、SEED_ENTITY_LOCAL、RNG_ENTITY_LOCAL_V1；44E MOD_RNG）：人口生死的唯一亂數來源。
/// 由歷史種子的 POP 流衍生、以實體局部索引亂數 v1 抽樣：純函式、與處理順序無關、不推進十條全域流的計數器。
/// 機率用整數門檻：<c>chance≤0</c>→否、<c>≥1</c>→是，否則 <c>(draw»11) &lt; floor(chance·2^53)</c>。
/// </summary>
public sealed class DemoPopulationDraws
{
    /// <summary>實體局部命名空間（TEMP 人類繁衍規則第 1 版）。</summary>
    public const string Namespace = "TEMP-HUMAN-DEMOGRAPHY-v1";

    private const double TwoPow53 = 9007199254740992d;
    private readonly RngRegistry _registry;
    private readonly Dictionary<long, EntityLocalRandom> _locals = new();

    public DemoPopulationDraws(RngRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>（人、週、動作）的原始 64 位元抽樣值。</summary>
    public ulong Raw(long personId, int week, DemoPopulationAction action)
    {
        if (personId <= 0)
            throw new ArgumentOutOfRangeException(nameof(personId), "人口抽樣的實體 ID 必須大於 0。");
        if (week < 0)
            throw new ArgumentOutOfRangeException(nameof(week));
        if (!Enum.IsDefined(action))
            throw new ArgumentOutOfRangeException(nameof(action));
        if (!_locals.TryGetValue(personId, out var local))
        {
            local = EntityLocalRandom.Create(_registry, EntityId.From((ulong)personId), RngStreams.Pop, Namespace);
            _locals[personId] = local;
        }
        return local.GetUInt64(checked((ulong)week * 8UL + (ulong)action));
    }

    /// <summary>以 <paramref name="chance"/> 的機率命中（整數門檻，無浮點相等問題）。</summary>
    public bool Hit(long personId, int week, DemoPopulationAction action, double chance)
    {
        if (double.IsNaN(chance) || chance <= 0)
            return false;
        if (chance >= 1)
            return true;
        var threshold = (ulong)Math.Floor(chance * TwoPow53);
        return Raw(personId, week, action) >> 11 < threshold;
    }
}
