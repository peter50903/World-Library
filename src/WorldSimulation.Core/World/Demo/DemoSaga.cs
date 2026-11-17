using System.Globalization;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-10e 章：史書開頭要記的開局狀態（世界開始推進之前先取）。</summary>
public sealed record DemoSagaOpening(long Population, int Settlements);

/// <summary>
/// 第 08-10e 章：文字史書——把一個世界到目前為止的歷史寫成一頁讀得下去的文字（序、一個世界時代一卷、終章）。
/// 之後所有改動的驗收標準：這一頁有沒有變好看。
/// 只讀編年史（<see cref="DemoWorldChronicle"/>）與已提交的世界狀態，不改世界、不抽亂數；同一世界讀檔前後結果相同。
/// 口吻可以帶史詩語氣，但每一句都只陳述已經發生的事（卷名是世界時代與它的效果，不添加原因或評語）。
/// </summary>
public static class DemoSaga
{
    /// <summary>【TEMP】終章列幾個最大的聚落；每卷的技術一句最多點名幾項。</summary>
    public const int EndingSettlements = 3, NamedTechs = 4;

    /// <summary>世界開始推進之前呼叫：開局的人口與聚落數。</summary>
    public static DemoSagaOpening Opening(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new DemoSagaOpening(world.WorldPopulation, world.Sites.Settlements.Count(s => !s.IsRuin));
    }

    /// <summary>整部史書（每個元素一行）。<paramref name="opening"/> 沒有（例：讀檔的世界）就不寫開局人數。</summary>
    public static IReadOnlyList<string> Write(DemoObserverWorld world, DemoSagaOpening? opening = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        static string I(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        var years = DemoWorldChronicle.Build(world);
        var lines = new List<string>
        {
            opening is null
                ? "序　這是一個世界留下的紀錄。"
                : I($"序　太初，{opening.Population} 人分居 {opening.Settlements} 處聚落，世上還沒有任何紀錄。")
        };

        var ledger = world.YearSummaries().ToDictionary(y => y.Year);
        var toldEntries = ToldEntries(years).ToHashSet<DemoChronicleEntry>(ReferenceEqualityComparer.Instance);
        // 第 08-6c 章：一個世界時代一卷（原本固定十年一卷）。
        var number = 0;
        foreach (var span in world.Ages())
        {
            var volume = years.Where(y => y.Year >= span.FromYear && y.Year <= span.ToYear).ToList();
            if (volume.Count == 0)
                continue;
            lines.Add("");
            lines.Add(I($"卷 {++number}　{DemoAges.Name(span.Age)}　第 {volume[0].Year}～{volume[^1].Year} 年　{DemoAges.Description(span.Age)}"));

            // 大事：世界大事（技術另收成一句）、任何聚落淪為遺跡、城鎮以上的升降。一模一樣的句子合併年份。
            var told = volume.SelectMany(y => y.Entries.Where(toldEntries.Contains).Select(e => (y.Year, Text: Sentence(e))))
                .GroupBy(x => x.Text, StringComparer.Ordinal)
                .Select(g => (Years: g.Select(x => x.Year).Distinct().ToList(), Text: g.Key)).ToList();
            foreach (var (toldYears, text) in told)
                lines.Add(I($"　第 {string.Join("、", toldYears)} 年，{text}"));
            var techs = Techs(volume);
            if (techs is not null)
                lines.Add("　" + techs);
            if (told.Count == 0 && techs is null)
                lines.Add("　這些年沒有驚動世界的大事。");
            var local = DemoWorldChronicle.LocalTally(volume);
            if (local.Length > 0)
                lines.Add("　各地：" + local + "。");
            lines.Add("　" + People(volume, ledger));
        }

        lines.Add("");
        lines.AddRange(Ending(world, years.Count));
        return lines;
    }

    /// <summary>終章：最後一年的世界——人口與聚落、最大的幾個聚落與領袖、各勢力。</summary>
    private static IEnumerable<string> Ending(DemoObserverWorld world, int year)
    {
        static string I(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        var active = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        yield return I($"終章　第 {year} 年的世界");
        yield return I($"　世上有 {world.WorldPopulation} 人、{active.Count} 處有人煙的聚落、{world.Sites.Settlements.Count(s => s.IsRuin)} 處遺跡。");
        yield return "　時代：" + DemoAges.Name(world.Age) + "。"; // 第 08-6c 章：世界時代

        string Titled(DemoSettlement s) => DemoSettlementHistory.TitledName(s, world.ChronicleOf(s.Id).Tier);
        string LeaderOf(DemoSettlement s)
            => world.RepresentativesOf(s.Id).FirstOrDefault(r => r.Role == DemoRepresentativeRole.Leader) is { } leader ? "，領袖" + leader.Name : "";
        var largest = active.OrderByDescending(s => world.SettlementPopulation(s.Id)).ThenBy(s => s.Id).Take(EndingSettlements)
            .Select(s => I($"{Titled(s)}（{world.SettlementPopulation(s.Id)} 人{LeaderOf(s)}）")).ToList();
        if (largest.Count > 0)
            yield return "　最大的聚落：" + string.Join("、", largest) + "。";

        var territory = world.Territory();
        var realms = active.Select(s => (Site: s, Vassals: territory.VassalsOf(s.Id).Count)).Where(x => x.Vassals > 0)
            .OrderByDescending(x => x.Vassals).ThenBy(x => x.Site.Id)
            .Select(x => I($"{Titled(x.Site)}統領 {x.Vassals} 個聚落")).ToList();
        var ruins = world.Sites.Settlements.Where(x => x.IsRuin).OrderBy(x => x.RuinedWeek).ThenBy(x => x.Id)
            .Select(x => I($"{x.Name}（全盛時 {world.ChronicleOf(x.Id).Peak} 人）")).ToList();
        if (ruins.Count > 0)
            yield return "　已成遺跡：" + string.Join("、", ruins) + "。";
        yield return realms.Count > 0 ? "　勢力：" + string.Join("；", realms) + "。" : "　世上還沒有統領其他聚落的勢力。";
    }

    /// <summary>
    /// 要逐條寫進史書的條目（依時間）：<see cref="IsTold"/> 的條目，但同一個聚落升到同一級只寫第一次
    /// （人口在門檻上下擺盪時，不重複寫「發展為城鎮」；降級照寫）。
    /// </summary>
    public static IReadOnlyList<DemoChronicleEntry> ToldEntries(IEnumerable<DemoChronicleYear> years)
    {
        ArgumentNullException.ThrowIfNull(years);
        var reached = new HashSet<(int Settlement, string Tier)>();
        var result = new List<DemoChronicleEntry>();
        foreach (var entry in years.OrderBy(y => y.Year).SelectMany(y => y.Entries).Where(IsTold))
        {
            var at = entry.Category == DemoChronicleCategory.Tier ? entry.Text.IndexOf(DemoSettlementChronicle.TierUpMarker, StringComparison.Ordinal) : -1;
            if (at >= 0 && !reached.Add((entry.SettlementId, entry.Text[(at + DemoSettlementChronicle.TierUpMarker.Length)..])))
                continue;
            result.Add(entry);
        }
        return result;
    }

    /// <summary>戰爭沒打贏的結局（編年史列為一般事件）在條目裡的用語：叛軍被擊潰、不分勝負、敗退、無力再戰、收兵。</summary>
    private static readonly string[] WarEndings = { "擊潰", "不分勝負", "敗退", "無力再戰", "收兵" };

    /// <summary>這一筆要不要逐條寫進史書：世界大事（技術除外）、任何聚落淪為遺跡、城鎮以上的等級升降、戰爭的結局。</summary>
    public static bool IsTold(DemoChronicleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // 第 08-19h 章：開了戰就要有下文——沒打贏的結局也寫。
        if (entry.Category == DemoChronicleCategory.Realm && entry.Level == DemoChronicleLevel.Normal)
            return WarEndings.Any(w => entry.Text.Contains(w, StringComparison.Ordinal));
        // 第 08-19i 章：開戰不另寫——戰爭的結局（征服、獨立、敗退、白和平）那一句已經說了誰打誰、結果如何。
        if (entry.Level != DemoChronicleLevel.Major || entry.Category == DemoChronicleCategory.Knowledge || entry.Weight == DemoWorldChronicle.WeightWar)
            return false;
        if (entry.IsWorld || entry.Category == DemoChronicleCategory.Ruin)
            return true;
        return entry.Category == DemoChronicleCategory.Tier
            && new[] { DemoSettlementTier.Town, DemoSettlementTier.City, DemoSettlementTier.Capital }
                .Any(t => entry.Text.Contains(DemoSettlementHistory.TierName(t), StringComparison.Ordinal));
    }

    /// <summary>
    /// 編年史條目收成一句：開頭的聚落名稱如果句子裡已經有了就不重複；去掉中文字之間的空白；補句號。
    /// </summary>
    public static string Sentence(DemoChronicleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var text = entry.Text;
        if (entry.Segments.Count >= 2 && entry.Segments[0].SettlementId > 0)
        {
            var rest = string.Concat(entry.Segments.Skip(1).Select(s => s.Text));
            if (rest.Contains(entry.Segments[0].Text, StringComparison.Ordinal))
                text = rest;
        }
        var t = Tidy(text);
        return t.EndsWith('。') || t.EndsWith('！') || t.EndsWith('？') ? t : t + "。";
    }

    /// <summary>去頭尾空白，並去掉夾在兩個非英數字（中文字、全形標點）之間的空白；數字與英文旁邊的空白留著。</summary>
    private static string Tidy(string text)
    {
        var t = text.Trim();
        var sb = new System.Text.StringBuilder(t.Length);
        for (var i = 0; i < t.Length; i++)
        {
            if (t[i] == ' ' && i > 0 && i + 1 < t.Length && t[i - 1] > 0x7F && t[i + 1] > 0x7F)
                continue;
            sb.Append(t[i]);
        }
        return sb.ToString();
    }

    /// <summary>這一卷世界首次習得的技術收成一句（共幾項、點名前幾項、各聚落首創幾項）；沒有＝null。</summary>
    private static string? Techs(IReadOnlyList<DemoChronicleYear> volume)
    {
        static string I(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        const string learned = "學會", firstMark = "（世界首次）";
        var firsts = volume.SelectMany(y => y.Entries)
            .Where(e => e.IsWorld && e.Category == DemoChronicleCategory.Knowledge && e.Segments.Count > 0).ToList();
        if (firsts.Count == 0)
            return null;
        var names = firsts.Select(e =>
        {
            var text = Tidy(e.Text);
            var at = text.IndexOf(learned, StringComparison.Ordinal);
            var name = at < 0 ? text : text[(at + learned.Length)..];
            return name.EndsWith(firstMark, StringComparison.Ordinal) ? name[..^firstMark.Length] : name;
        }).ToList();
        var by = firsts.GroupBy(e => e.Segments[0].Text, StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Count: g.Count())).OrderByDescending(x => x.Count).ThenBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => I($"{x.Name} {x.Count} 項"));
        return I($"世人學會了 {firsts.Count} 項前所未有的技術（{string.Join("、", names.Take(NamedTechs))}{(names.Count > NamedTechs ? "等" : "")}）；首創者：{string.Join("、", by)}。");
    }

    /// <summary>
    /// 這一卷的生死與卷末人口。優先用世界的年帳（出生、壽終、其餘死亡、年末人口）；年帳不齊（曆法不同的測試世界）時只寫編年史數得到的出生與壽終。
    /// </summary>
    private static string People(IReadOnlyList<DemoChronicleYear> volume, IReadOnlyDictionary<int, DemoYearSummary> ledger)
    {
        static string I(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        if (!volume.All(y => ledger.ContainsKey(y.Year)))
            return I($"這些年裡 {volume.Sum(y => y.Births)} 人出生，{volume.Sum(y => y.NaturalDeaths)} 人壽終。");
        var rows = volume.Select(y => ledger[y.Year]).ToList();
        var births = rows.Sum(r => r.Births);
        var natural = rows.Sum(r => r.NaturalDeaths);
        var other = rows.Sum(r => r.Deaths) - natural;
        var last = rows[^1];
        return I($"這些年裡 {births} 人出生，{natural} 人壽終")
            + (other > 0 ? I($"，{other} 人未得善終") : "")
            + I($"；第 {last.Year} 年末世上有 {last.PopulationClosing} 人。");
    }
}
