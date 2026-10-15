using System.Globalization;
using WorldSimulation.Core.Config;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.Core.Time;

// C08（P1.0.02）：世界曆法移入 Core。年長、月數、二月天數與各月天數只從 01 表讀（經 ConfigRegistry），
// 程式不另存年長／月長常數（Notion 02 §O）。Core 不讀檔：呼叫端（CLI／Godot 邊界）把已驗證的 registry 傳進來。
// 一年 365 天、12 月、2 月 28 天、無閏年、各月採公曆平年是定案（Notion 02 §B；05 編號 19 第 1 題），不是暫定值。
// 換算只是把世界時鐘的「第 N 日」換成年月日，不持有世界、不碰亂數、不改排程。

/// <summary>曆法設定讀取或驗證失敗（訊息為繁體中文，含參數 ID，可直接給使用者看）。</summary>
public sealed class WorldCalendarException : Exception
{
    public WorldCalendarException(string message) : base(message)
    {
    }
}

/// <summary>世界日期：第 <paramref name="Year"/> 年 <paramref name="Month"/> 月 <paramref name="Day"/> 日（年內第 <paramref name="DayOfYear"/> 日）。</summary>
public readonly record struct WorldDate(int Year, int Month, int Day, int DayOfYear);

/// <summary>由 01 表參數組成的世界曆法；Godot、CLI、M1 驗收報告共用這一份換算。</summary>
public sealed class WorldCalendarRules
{
    public const string DaysPerYearId = "WORLD_DAYS_PER_YEAR";
    public const string MonthsPerYearId = "WORLD_MONTHS_PER_YEAR";
    public const string FebDaysId = "WORLD_FEB_DAYS";
    public const string MonthDaysId = "WORLD_MONTH_DAYS";

    private readonly int[] _monthDays;

    private WorldCalendarRules(int daysPerYear, int[] monthDays)
    {
        DaysPerYear = daysPerYear;
        _monthDays = monthDays;
        MonthDays = Array.AsReadOnly(monthDays);
    }

    /// <summary>一年日數（01 表 WORLD_DAYS_PER_YEAR）。</summary>
    public int DaysPerYear { get; }

    /// <summary>1～12 月各月天數（01 表 WORLD_MONTH_DAYS，依序）。</summary>
    public IReadOnlyList<int> MonthDays { get; }

    /// <summary>
    /// 從已驗證的 01 表 registry 取出曆法並交叉檢查。先做 WORLD_DAYS_PER_YEAR 全部檢查（訊息與 #82 相同），
    /// 再查 WORLD_MONTHS_PER_YEAR、WORLD_FEB_DAYS、WORLD_MONTH_DAYS：項數＝月數、每項正整數、合計＝年日數、第 2 項＝二月天數。
    /// </summary>
    /// <exception cref="WorldCalendarException">任一參數缺少、未啟用、型別錯誤或彼此不一致。</exception>
    public static WorldCalendarRules FromRegistry(ConfigRegistry registry, string source)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var days = RequirePositiveInt(registry, DaysPerYearId, source);
        var months = RequirePositiveInt(registry, MonthsPerYearId, source);
        var feb = RequirePositiveInt(registry, FebDaysId, source);

        if (!registry.Contains(MonthDaysId))
            throw new WorldCalendarException($"世界參數表沒有已啟用的 {MonthDaysId}：{source}");
        IReadOnlyList<string> items;
        try
        {
            items = registry.GetEnumList(MonthDaysId);
        }
        catch (InvalidOperationException)
        {
            throw new WorldCalendarException($"{MonthDaysId} 的資料型別必須是 enum_list：{source}");
        }

        if (items.Count != months)
            throw new WorldCalendarException(
                $"{MonthDaysId} 有 {items.Count} 項，與 {MonthsPerYearId}＝{months} 不符：{source}");

        var monthDays = new int[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var text = items[i].Trim();
            if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 1)
                throw new WorldCalendarException(
                    $"{MonthDaysId} 第 {i + 1} 項必須是正整數，收到「{items[i]}」：{source}");
            monthDays[i] = value;
        }

        var sum = monthDays.Sum(d => (long)d);
        if (sum != days)
            throw new WorldCalendarException(
                $"{MonthDaysId} 合計 {sum.ToString(CultureInfo.InvariantCulture)} 日，與 {DaysPerYearId}＝{days} 不符：{source}");
        if (monthDays.Length < 2)
            throw new WorldCalendarException($"{MonthDaysId} 沒有第 2 項可與 {FebDaysId} 核對：{source}");
        if (monthDays[1] != feb)
            throw new WorldCalendarException(
                $"{MonthDaysId} 第 2 項為 {monthDays[1]}，與 {FebDaysId}＝{feb} 不符：{source}");

        return new WorldCalendarRules(days, monthDays);
    }

    /// <summary>第 <paramref name="elapsedDays"/> 日（≥1）的年月日；第 365 日屬第 1 年，第 366 日為第 2 年 1 月 1 日。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="elapsedDays"/> 小於 1。</exception>
    public WorldDate ToDate(int elapsedDays)
    {
        if (elapsedDays < 1)
            throw new ArgumentOutOfRangeException(nameof(elapsedDays), elapsedDays, "世界日期從第 1 日開始；第 0 日為開局，請用 Format。");

        var year = (elapsedDays - 1) / DaysPerYear + 1;
        var dayOfYear = (elapsedDays - 1) % DaysPerYear + 1;
        var remaining = dayOfYear;
        for (var m = 0; m < _monthDays.Length; m++)
        {
            if (remaining <= _monthDays[m])
                return new WorldDate(year, m + 1, remaining, dayOfYear);
            remaining -= _monthDays[m];
        }

        // FromRegistry 已保證各月合計＝一年日數，走不到這裡。
        throw new InvalidOperationException("各月天數合計與一年日數不一致。");
    }

    /// <summary>玩家可見日期：「第 Y 年 M 月 D 日」；第 0 日為「開局（第 1 年 1 月 1 日之前）」。</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="elapsedDays"/> 小於 0。</exception>
    public string Format(int elapsedDays)
    {
        if (elapsedDays < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedDays), elapsedDays, "世界日期不可為負。");
        if (elapsedDays == 0)
            return "開局（第 1 年 1 月 1 日之前）";
        var date = ToDate(elapsedDays);
        return $"第 {date.Year} 年 {date.Month} 月 {date.Day} 日";
    }

    /// <summary>第 <paramref name="week"/> 次週結算（第 7×week 日）歸屬的年份與該年內第幾次週結算。</summary>
    public (int Year, int WeekInYear) WeeklySettlementInYear(int week) => WeeklySettlementInYear(week, DaysPerYear);

    /// <summary>
    /// 同 <see cref="WeeklySettlementInYear(int)"/>，給只持有一年日數的既有呼叫端（M1 驗收）；
    /// 週結算每 7 日一次、跨年不重置，歸屬結算日所在年份（36B TICK_SETTLEMENT_WEEK；#82）。
    /// </summary>
    public static (int Year, int WeekInYear) WeeklySettlementInYear(int week, int daysPerYear)
    {
        if (week < 1)
            throw new ArgumentOutOfRangeException(nameof(week), week, "週結算從第 1 次開始。");
        if (daysPerYear < 1)
            throw new ArgumentOutOfRangeException(nameof(daysPerYear), daysPerYear, "一年日數必須是正整數。");
        var day = (long)week * DemoCenturyRun.DaysPerWeeklySettlement;
        var year = (day - 1) / daysPerYear + 1;
        var yearStart = (year - 1) * daysPerYear;
        var weekInYear = week - yearStart / DemoCenturyRun.DaysPerWeeklySettlement;
        return ((int)year, (int)weekInYear);
    }

    /// <summary>給報表標頭用：「各月 31/28/…（WORLD_MONTH_DAYS）」。</summary>
    public string DescribeMonths()
        => $"各月 {string.Join("/", _monthDays.Select(d => d.ToString(CultureInfo.InvariantCulture)))}（{MonthDaysId}）";

    private static int RequirePositiveInt(ConfigRegistry registry, string id, string source)
    {
        if (!registry.Contains(id))
            throw new WorldCalendarException($"世界參數表沒有已啟用的 {id}：{source}");

        int value;
        try
        {
            value = registry.GetInt(id);
        }
        catch (InvalidOperationException)
        {
            throw new WorldCalendarException($"{id} 的資料型別必須是 int：{source}");
        }

        if (value < 1)
            throw new WorldCalendarException(
                $"{id} 必須是正整數，收到 {value.ToString(CultureInfo.InvariantCulture)}：{source}");
        return value;
    }
}
