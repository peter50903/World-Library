using System.Globalization;
using System.Text.Json;
using WorldSimulation.Core.Config;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 07 章 a 節：開局年齡層（TEMP 比例）。</summary>
public sealed record DemoAgeBand(int MinAge, int MaxAge, int Percent);

/// <summary>第 07 章 a 節：config/demo/demography-temp.json。</summary>
public sealed record DemoDemographyTempSettings(
    int SchemaVersion,
    string Status,
    int GestationWeeks,
    int FertileAgeMaxYears,
    string KinExclusion,
    IReadOnlyList<DemoAgeBand> OpeningAgeBands)
{
    public static DemoDemographyTempSettings Current { get; } = Load();

    private static DemoDemographyTempSettings Load()
    {
        var settings = JsonSerializer.Deserialize<DemoDemographyTempSettings>(OfficialConfig.ReadDemoText("demography-temp.json"))
            ?? throw new InvalidOperationException("config/demo/demography-temp.json 讀不到。");
        if (settings.SchemaVersion != 1 || settings.Status != "TEMP" || settings.GestationWeeks < 1 || settings.FertileAgeMaxYears < 1
            || settings.KinExclusion != "PARENT_CHILD" || settings.OpeningAgeBands is not { Count: > 0 }
            || settings.OpeningAgeBands.Sum(b => b.Percent) != 100
            || settings.OpeningAgeBands.Any(b => b.MinAge < 0 || b.MaxAge < b.MinAge || b.Percent < 0))
            throw new InvalidOperationException("config/demo/demography-temp.json 內容不合法。");
        return settings;
    }
}

/// <summary>
/// 第 07 章 a 節：人族生死參數的唯一來源——02_種族 RACE_HUMAN（成年年齡、典型壽命、月受孕基率）、
/// 39C LIFE_DEATH_BANDS（自然死亡年度機率，依 年齡÷典型壽命 分段）、39C REP_MISCARRIAGE（流產月基準），
/// 以及 config/demo/demography-temp.json（孕期、育齡上限，TEMP）。程式內不再有自創曲線。
/// </summary>
public static class DemoHumanRulesSource
{
    public static DemoTempDemographyRules Load()
    {
        var race = Row("02_種族.csv", "RaceID", "RACE_HUMAN");
        var bands = ParseDeathBands(Row("39C_生存生育生命週期.csv", "RuleID", "LIFE_DEATH_BANDS")["V1值／公式"]);
        var miscarriage = ParsePercent(Row("39C_生存生育生命週期.csv", "RuleID", "REP_MISCARRIAGE")["V1值／公式"].Split('/')[0]);
        var settings = DemoDemographyTempSettings.Current;
        return new DemoTempDemographyRules(
            int.Parse(race["AdultAge"], CultureInfo.InvariantCulture),
            int.Parse(race["TypicalLifespanYears"], CultureInfo.InvariantCulture),
            double.Parse(race["FertilityBaseMonthly"], CultureInfo.InvariantCulture),
            miscarriage,
            settings.GestationWeeks)
        {
            DeathBands = bands,
            FertileAgeMaxYears = settings.FertileAgeMaxYears
        };
    }

    private static Dictionary<string, string> Row(string sheet, string idColumn, string id)
    {
        var rows = ParameterSheet.ParseCsv(OfficialConfig.ReadSheetText(sheet));
        var header = rows[0].Select(h => h.Trim().TrimStart('\uFEFF')).ToList();
        var row = rows.Skip(1).FirstOrDefault(r => r.Count > header.IndexOf(idColumn) && r[header.IndexOf(idColumn)] == id)
            ?? throw new InvalidOperationException($"{sheet} 找不到 {id}。");
        return header.Select((h, i) => (h, v: i < row.Count ? row[i] : "")).ToDictionary(x => x.h, x => x.v);
    }

    /// <summary>「Age/ExpectedLife:&lt;0.6=0.05%;0.6~0.8=0.2%;…;&gt;=1.5=35%」→ (上界（不含）, 年度機率)，最後一段上界為無限大。</summary>
    internal static IReadOnlyList<(double UpperRatio, double Annual)> ParseDeathBands(string formula)
    {
        var body = formula[(formula.IndexOf(':') + 1)..];
        var bands = new List<(double, double)>();
        foreach (var part in body.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var (range, value) = (part[..part.LastIndexOf('=')], part[(part.LastIndexOf('=') + 1)..]);
            var annual = ParsePercent(value);
            double upper = range.StartsWith(">=", StringComparison.Ordinal) ? double.PositiveInfinity
                : range.StartsWith("<", StringComparison.Ordinal) ? double.Parse(range[1..], CultureInfo.InvariantCulture)
                : double.Parse(range.Split('~')[1], CultureInfo.InvariantCulture);
            bands.Add((upper, annual));
        }
        if (bands.Count == 0 || !double.IsPositiveInfinity(bands[^1].Item1)
            || bands.Zip(bands.Skip(1)).Any(p => p.First.Item1 >= p.Second.Item1))
            throw new InvalidOperationException($"39C LIFE_DEATH_BANDS 格式無法解析：{formula}");
        return bands;
    }

    private static double ParsePercent(string text)
    {
        var t = text.Trim();
        return t.EndsWith('%')
            ? double.Parse(t[..^1], CultureInfo.InvariantCulture) / 100
            : double.Parse(t, CultureInfo.InvariantCulture);
    }
}
