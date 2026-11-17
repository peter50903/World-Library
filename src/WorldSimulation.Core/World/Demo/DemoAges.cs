using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-6c 章：世界時代（參考 WorldBox 的時代）——世界的氣候與命運，每隔數十年輪替，改變既有世界法則的強弱。
/// 與文明的進度（<see cref="DemoEra"/>：第一項技術、第一個王國）無關。
/// </summary>
public enum DemoAge
{
    /// <summary>希望時代：風調雨順，沒有額外的災厄。世界的第一個時代。</summary>
    Hope = 0,

    /// <summary>烈日時代：熱浪與野火頻仍。</summary>
    Sun = 1,

    /// <summary>黑暗時代：地下城接連出現，數量上限加倍。</summary>
    Dark = 2,

    /// <summary>混沌時代：人心浮動，下級對上級的忠誠降低。</summary>
    Chaos = 3,

    /// <summary>第 08-6c 章：霜雪時代：寒冬漫長，採集與收成都減少。</summary>
    Frost = 4,

    /// <summary>第 08-6c 章：奇蹟時代：研究加快，士兵更強（地下城更容易被殺光，英雄與傳說物品更常出現）。</summary>
    Miracle = 5
}

/// <summary>一段世界時代：第 <see cref="FromYear"/>～<see cref="ToYear"/> 年。</summary>
public sealed record DemoAgeSpan(DemoAge Age, int FromYear, int ToYear)
{
    /// <summary>第 08-6c 章：從第幾週結算起（輪替表的時代從那一年的第一週起）。</summary>
    public int FromWeek { get; init; } = DemoAges.FirstWeek(FromYear);

    /// <summary>第 08-6c 章：到第幾週結算為止（指定而不再輪替的時代＝<see cref="DemoAges.Forever"/>）。</summary>
    public int ToWeek { get; init; } = DemoAges.LastWeek(ToYear);

    /// <summary>第 08-6c 章：在世界法則視窗指定的（不是輪替到的）。</summary>
    public bool Manual { get; init; }
}

/// <summary>
/// 第 08-6c 章：世界法則視窗指定時代的紀錄——第 <see cref="AfterWeek"/> 週結算之後起，時代固定是 <see cref="Age"/>，
/// 直到下一筆紀錄；<see cref="Age"/> 是 null＝回到自動輪替（接回輪替表在那一週的時代）。
/// </summary>
public sealed record DemoAgeChange(int AfterWeek, DemoAge? Age);

/// <summary>
/// 第 08-6c 章：世界時代的輪替表。由歷史種子決定（THREAT 流的實體局部亂數，不推進計數器），不另存：
/// 同一個世界任何時候重算都相同，讀檔與重播驗證不需要額外資料。
/// </summary>
public static class DemoAges
{
    /// <summary>【TEMP】每個時代持續 30～60 年。</summary>
    public const int TempMinYears = 30, TempMaxYears = 60;

    /// <summary>【TEMP】烈日時代熱浪與野火的機率 ×3；黑暗時代地下城出現的機率 ×3、數量上限 ×2；混沌時代忠誠 −15。</summary>
    public const int TempSunFirePercent = 300, TempDarkDungeonPercent = 300, TempDarkDungeonTargetPercent = 200, TempChaosLoyalty = -15;

    /// <summary>【TEMP】霜雪時代每個食物來源的產量 −25%；奇蹟時代研究 ×150%、士兵等級 +1。</summary>
    public const int TempFrostSourcePercent = -25, TempMiracleResearchPercent = 150, TempMiracleSoldierLevels = 1;

    private const string Namespace = "TEMP-AGE-v1";

    /// <summary>第 08-6c 章：指定而不再輪替的時代的結束年份與週次（永遠）。</summary>
    public const int Forever = int.MaxValue;

    /// <summary>第 <paramref name="year"/> 年的第一次週結算。</summary>
    internal static int FirstWeek(int year) => year >= Forever ? Forever : DemoObserverWorld.FirstWeekOfYear(Math.Max(1, year));

    /// <summary>第 <paramref name="year"/> 年的最後一次週結算。</summary>
    internal static int LastWeek(int year) => year >= Forever - 1 ? Forever : DemoObserverWorld.FirstWeekOfYear(year + 1) - 1;

    /// <summary>第 <paramref name="week"/> 週結算在第幾年。</summary>
    internal static int YearOfWeek(int week) => week >= Forever ? Forever : DemoSettlementHistory.YearOfDay(checked(Math.Max(1, week) * 7));

    private static ulong Draw(RngRegistry rng, int index, int k)
        => EntityLocalRandom.Create(rng, EntityId.From(1UL), RngStreams.Threat, Namespace).GetUInt64(checked((ulong)index * 4UL + (ulong)k));

    /// <summary>從第 1 年排到涵蓋 <paramref name="untilYear"/> 的那一段為止。第一段一定是希望時代；下一段從其他時代裡抽，不連續重複。</summary>
    public static IReadOnlyList<DemoAgeSpan> Schedule(RngRegistry rng, int untilYear)
    {
        ArgumentNullException.ThrowIfNull(rng);
        var spans = new List<DemoAgeSpan>();
        var all = Enum.GetValues<DemoAge>();
        var age = DemoAge.Hope;
        var from = 1;
        for (var index = 0; spans.Count == 0 || from <= untilYear; index++)
        {
            var years = TempMinYears + (int)(Draw(rng, index, 0) % (ulong)(TempMaxYears - TempMinYears + 1));
            spans.Add(new DemoAgeSpan(age, from, from + years - 1));
            from += years;
            var options = all.Where(a => a != age).ToArray();
            age = options[(int)(Draw(rng, index, 1) % (ulong)options.Length)];
        }
        return spans;
    }

    /// <summary>第 <paramref name="year"/> 年的世界時代。</summary>
    public static DemoAge At(RngRegistry rng, int year) => Schedule(rng, Math.Max(1, year))[^1].Age;

    /// <summary>第 <paramref name="week"/> 週結算時的世界時代。</summary>
    public static DemoAge AtWeek(RngRegistry rng, int week) => At(rng, DemoSettlementHistory.YearOfDay(checked(Math.Max(1, week) * 7)));

    /// <summary>第 08-6c 章：套用世界法則視窗的指定之後，第 <paramref name="week"/> 週結算時的世界時代。</summary>
    public static DemoAge AtWeek(RngRegistry rng, IReadOnlyList<DemoAgeChange> changes, int week)
    {
        ArgumentNullException.ThrowIfNull(changes);
        for (var i = changes.Count - 1; i >= 0; i--)
            if (changes[i].AfterWeek < week)
                return changes[i].Age ?? AtWeek(rng, week);
        return AtWeek(rng, week);
    }

    /// <summary>
    /// 第 08-6c 章：套用世界法則視窗的指定之後，從第 1 年排到涵蓋第 <paramref name="untilWeek"/> 週的那一段為止。
    /// 指定的時代從指定後的下一週起，一直持續到改回自動輪替；改回之後接回輪替表在那一週的時代。相鄰的同一個時代合成一段。
    /// 還沒生效的指定（下一週才開始）不列；目前這一段因此在本週結束。
    /// </summary>
    public static IReadOnlyList<DemoAgeSpan> Schedule(RngRegistry rng, IReadOnlyList<DemoAgeChange> changes, int untilWeek)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(changes);
        untilWeek = Math.Max(1, untilWeek);
        if (changes.Count == 0)
            return Schedule(rng, YearOfWeek(untilWeek));
        var spans = new List<DemoAgeSpan>();
        void Add(DemoAge age, int from, int to, bool manual)
        {
            if (from > to || from > untilWeek)
                return;
            if (spans.Count > 0 && spans[^1].Age == age)
                spans[^1] = spans[^1] with { ToYear = YearOfWeek(to), ToWeek = to };
            else
                spans.Add(new DemoAgeSpan(age, YearOfWeek(from), YearOfWeek(to)) { FromWeek = from, ToWeek = to, Manual = manual });
        }
        DemoAge? setting = null;
        var start = 1;
        for (var i = 0; i <= changes.Count; i++)
        {
            var end = i < changes.Count ? changes[i].AfterWeek : Forever;
            if (setting is { } fixedAge)
                Add(fixedAge, start, end, true);
            else if (start <= Math.Min(end, untilWeek))
                foreach (var s in Schedule(rng, YearOfWeek(Math.Min(end, untilWeek))))
                    Add(s.Age, Math.Max(s.FromWeek, start), Math.Min(s.ToWeek, end), false);
            if (i == changes.Count)
                break;
            setting = changes[i].Age;
            start = changes[i].AfterWeek + 1;
        }
        return spans;
    }

    public static string Name(DemoAge age) => age switch
    {
        DemoAge.Sun => "烈日時代",
        DemoAge.Dark => "黑暗時代",
        DemoAge.Chaos => "混沌時代",
        DemoAge.Frost => "霜雪時代",
        DemoAge.Miracle => "奇蹟時代",
        _ => "希望時代"
    };

    /// <summary>這個時代改變了什麼（玩家看得懂的一句）。</summary>
    public static string Description(DemoAge age) => age switch
    {
        DemoAge.Sun => "烈日當空，熱浪與野火頻仍",
        DemoAge.Dark => "長夜降臨，地下城接連出現",
        DemoAge.Chaos => "人心浮動，下級不再安分",
        DemoAge.Frost => "寒冬漫長，收成減少",
        DemoAge.Miracle => "靈光遍地，學問與英雄輩出",
        _ => "風調雨順，萬物滋長"
    };

    /// <summary>熱浪、野火機率的倍率（百分比）。</summary>
    public static int FirePercent(DemoAge age) => age == DemoAge.Sun ? TempSunFirePercent : 100;

    /// <summary>地下城出現機率的倍率（百分比）。</summary>
    public static int DungeonPercent(DemoAge age) => age == DemoAge.Dark ? TempDarkDungeonPercent : 100;

    /// <summary>地下城數量上限的倍率（百分比）。</summary>
    public static int DungeonTargetPercent(DemoAge age) => age == DemoAge.Dark ? TempDarkDungeonTargetPercent : 100;

    /// <summary>忠誠度的加減。</summary>
    public static int Loyalty(DemoAge age) => age == DemoAge.Chaos ? TempChaosLoyalty : 0;

    /// <summary>食物來源產量的加減（%；與技術、熱浪等其他修正相加）。</summary>
    public static long SourcePercent(DemoAge age) => age == DemoAge.Frost ? TempFrostSourcePercent : 0;

    /// <summary>研究速度（%）。</summary>
    public static int ResearchPercent(DemoAge age) => age == DemoAge.Miracle ? TempMiracleResearchPercent : 100;

    /// <summary>士兵多幾級。</summary>
    public static int SoldierLevels(DemoAge age) => age == DemoAge.Miracle ? TempMiracleSoldierLevels : 0;
}
