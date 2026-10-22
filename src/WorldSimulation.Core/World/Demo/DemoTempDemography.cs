using System.Collections.Immutable;
using WorldSimulation.Core.Entity;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 人族性別與生死事件種類（TEMP）。第 07-1 章 a 節起人口是群體統計（<see cref="DemoCohortDemography"/>），
/// 舊的個體名冊（DemoTempPerson、DemoTempDemography、DemoFamilyMigration）已移除；規則仍由 <see cref="DemoTempDemographyRules"/> 提供。
/// </summary>
public enum DemoTempSex { Female, Male }

public enum DemoTempDemographicKind
{
    Conception,
    Birth,
    Miscarriage,
    NaturalDeath,
    StarvationDeath,
    TravelStarvationDeath
}

/// <summary>
/// 人族生死規則。第 07 章 a 節起由 <see cref="DemoHumanRulesSource"/> 從 02_種族、39C 與 config/demo 載入：
/// 自然死亡依 39C LIFE_DEATH_BANDS（年齡÷典型壽命 分段的年度機率，【TEMP】數值未定案），不再用自創曲線。
/// </summary>
public sealed record DemoTempDemographyRules(
    int AdultAgeYears,
    int TypicalLifespanYears,
    double FertilityMonthly,
    double MiscarriageMonthly,
    int GestationWeeks)
{
    /// <summary>人族：02 成年 18／典型壽命 75／月受孕 0.08；39C 流產 0.5%／月、死亡分段；孕期 39 週【TEMP】。</summary>
    public static DemoTempDemographyRules HumanFixture { get; } = DemoHumanRulesSource.Load();

    /// <summary>39C LIFE_DEATH_BANDS：(年齡÷典型壽命 的上界（不含）, 年度機率)。</summary>
    public IReadOnlyList<(double UpperRatio, double Annual)> DeathBands { get; init; } = Array.Empty<(double, double)>();

    /// <summary>育齡上限【TEMP】（config/demo）。</summary>
    public int FertileAgeMaxYears { get; init; } = 45;

    /// <summary>第 08-2 章：低於這個年齡不會自然死亡（希望時代只有老死；0＝照 39C 全年齡分段）。</summary>
    public int NaturalDeathMinAgeYears { get; init; }

    public void Validate()
    {
        if (AdultAgeYears < 1 || TypicalLifespanYears <= AdultAgeYears
            || FertilityMonthly is < 0 or > 1 || double.IsNaN(FertilityMonthly)
            || MiscarriageMonthly is < 0 or > 1 || double.IsNaN(MiscarriageMonthly)
            || GestationWeeks < 1 || DeathBands.Count == 0 || FertileAgeMaxYears <= AdultAgeYears)
            throw new ArgumentOutOfRangeException(nameof(DemoTempDemographyRules));
    }

    // TEMP: calendar uses 365 days/year and 12 months/year, 7 days per settlement week.
    public double WeeklyConceptionChance => 1 - Math.Pow(1 - FertilityMonthly, 84.0 / 365);
    public double WeeklyMiscarriageChance => 1 - Math.Pow(1 - MiscarriageMonthly, 84.0 / 365);

    /// <summary>39C 年度自然死亡機率（依年齡÷典型壽命 分段）；不是壽命到期強制死亡。</summary>
    public double AnnualNaturalDeathChance(int ageYears)
    {
        if (ageYears < NaturalDeathMinAgeYears)
            return 0;
        var ratio = (double)Math.Max(0, ageYears) / TypicalLifespanYears;
        foreach (var (upper, annual) in DeathBands)
            if (ratio < upper)
                return annual;
        return DeathBands[^1].Annual;
    }

    public double WeeklyNaturalDeathChance(int ageYears)
        => 1 - Math.Pow(1 - AnnualNaturalDeathChance(ageYears), 7.0 / 365);
}
