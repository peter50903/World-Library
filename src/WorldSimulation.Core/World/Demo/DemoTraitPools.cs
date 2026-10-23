using System.Collections.Immutable;
using System.Globalization;
using WorldSimulation.Core.Config;

namespace WorldSimulation.Core.World.Demo;

/// <summary>正式設定特性池（P02、P04A～P04J…）的一列。</summary>
public sealed record DemoPoolEntry(string PoolId, string TraitId, string Name, long Weight, int DrawMin, int DrawMax,
    IReadOnlyList<string> Required, IReadOnlyList<string> Forbidden, string ExclusiveGroup);

/// <summary>
/// 第 08-9 章 B1、B3：特性池的讀取與抽選（世界法則第 6 條：只組合事先設計的特性池）。文化與信仰共用。
/// 條件：一般條件（NEAR_FOREST、OPENING…）看 <c>conditions</c>；「TRAIT:A|B」表示已抽到 A 或 B。
/// </summary>
public static class DemoTraitPools
{
    public static IReadOnlyList<DemoPoolEntry> Parse(string text, string sheet, Func<string, bool>? known = null)
    {
        var rows = ParameterSheet.ParseCsv(text);
        var h = rows[0].Select(x => x.Trim()).ToList();
        int C(string name) => h.IndexOf(name) is var i && i >= 0 ? i : throw new InvalidOperationException($"{sheet} 缺少欄位「{name}」。");
        var (pool, entry, entryName, weight, min, max, req, forb, excl, enabled) = (C("PoolID"), C("EntryID"), C("EntryName"), C("Weight"),
            C("PoolDrawMin"), C("PoolDrawMax"), C("RequiredConditions"), C("ForbiddenConditions"), C("ExclusiveGroup"), C("Enabled"));
        var list = new List<DemoPoolEntry>();
        foreach (var r in rows.Skip(1))
        {
            string Cell(int i) => i < r.Count ? r[i].Trim() : string.Empty;
            if (Cell(entry).Length == 0 || !string.Equals(Cell(enabled), "TRUE", StringComparison.OrdinalIgnoreCase))
                continue;
            if (known is not null && !known(Cell(entry)))
                throw new InvalidOperationException($"{sheet} 的 {Cell(entry)} 沒有對應的特性定義。");
            static IReadOnlyList<string> Split(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!long.TryParse(Cell(weight), NumberStyles.None, CultureInfo.InvariantCulture, out var w)
                || !int.TryParse(Cell(min), NumberStyles.None, CultureInfo.InvariantCulture, out var lo)
                || !int.TryParse(Cell(max), NumberStyles.None, CultureInfo.InvariantCulture, out var hi) || hi < lo)
                throw new InvalidOperationException($"{sheet} 的 {Cell(entry)}：權重與抽取上下限須為整數且上限不小於下限。");
            list.Add(new DemoPoolEntry(Cell(pool), Cell(entry), Cell(entryName), w, lo, hi, Split(Cell(req)), Split(Cell(forb)), Cell(excl)));
        }
        return list;
    }

    /// <summary>條件是否成立（「TRAIT:A|B」＝已抽到 A 或 B）。</summary>
    public static bool Holds(string condition, IReadOnlySet<string> conditions, IReadOnlyCollection<string> drawn)
        => condition.StartsWith("TRAIT:", StringComparison.Ordinal)
            ? condition["TRAIT:".Length..].Split('|').Any(drawn.Contains)
            : conditions.Contains(condition);

    /// <summary>
    /// 每個池依出現順序，抽 DrawMin～DrawMax 個（亂數決定個數），權重不放回；抽到後同互斥組的其他列不再抽；
    /// 每抽一次都重新判斷條件（可參照前面抽到的特性）。<paramref name="weightOf"/> 可改權重（例：依文化加權），回傳 0 表示不可抽。
    /// </summary>
    public static ImmutableList<string> Draw(IEnumerable<DemoPoolEntry> entries, IReadOnlySet<string> conditions, Func<ulong, ulong> draw,
        Func<DemoPoolEntry, long>? weightOf = null, IEnumerable<string>? already = null)
    {
        var result = new List<string>(already ?? Array.Empty<string>());
        var start = result.Count;
        var taken = new HashSet<string>();
        ulong index = 0;
        foreach (var pool in entries.GroupBy(e => e.PoolId))
        {
            var list = pool.ToList();
            var (min, max) = (list.Min(e => e.DrawMin), list.Max(e => e.DrawMax));
            var count = min + (int)(draw(index++) % (ulong)(max - min + 1));
            for (var i = 0; i < count; i++)
            {
                var eligible = list.Where(e => !result.Contains(e.TraitId)
                        && (e.ExclusiveGroup.Length == 0 || !taken.Contains(e.ExclusiveGroup))
                        && e.Required.All(c => Holds(c, conditions, result)) && !e.Forbidden.Any(c => Holds(c, conditions, result)))
                    .Select(e => (Entry: e, Weight: weightOf?.Invoke(e) ?? e.Weight)).Where(x => x.Weight > 0).ToList();
                if (eligible.Count == 0)
                    break;
                var pick = (long)(draw(index++) % (ulong)eligible.Sum(x => x.Weight));
                var chosen = eligible.First(x => (pick -= x.Weight) < 0).Entry;
                result.Add(chosen.TraitId);
                if (chosen.ExclusiveGroup.Length > 0)
                    taken.Add(chosen.ExclusiveGroup);
            }
        }
        return result.Skip(start).ToImmutableList();
    }
}
