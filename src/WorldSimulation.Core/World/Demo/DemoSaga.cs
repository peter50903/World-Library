using System.Globalization;
using System.Text.RegularExpressions;

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
    /// <summary>【TEMP】終章列幾個最大的聚落；每卷的技術一句最多點名幾項；每卷最多寫幾位人物、事蹟（降服加失去的聚落）至少幾件才寫、降服幾個稱「征服者」。</summary>
    public const int EndingSettlements = 3, NamedTechs = 4, VolumeRulers = 3, RulerDeeds = 2, ConquerorDeeds = 3;

    /// <summary>
    /// 【TEMP】第 08-10m 節（大地圖的史書一卷兩百多行）：每卷逐條寫的、沒有傳說物品的英雄最多幾位（其餘只列名字）；
    /// 合成一句時最多列出幾個聚落名稱（其餘寫「等」）。
    /// </summary>
    public const int VolumeHeroes = 6, CondensedNames = 8;

    /// <summary>世界開始推進之前呼叫：開局的人口與聚落數。</summary>
    public static DemoSagaOpening Opening(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new DemoSagaOpening(world.WorldPopulation, world.Sites.Settlements.Count(s => !s.IsRuin));
    }

    /// <summary>整部史書（每個元素一行，純文字）。<paramref name="opening"/> 沒有（例：讀檔的世界）就不寫開局人數。</summary>
    public static IReadOnlyList<string> Write(DemoObserverWorld world, DemoSagaOpening? opening = null)
        => WriteMarked(world, opening).Select(Plain).ToList();

    /// <summary>第 05-2 章：連結標記的開頭、中段、結尾（控制字元，不會出現在正文裡）。</summary>
    public const char LinkStart = '\u0001', LinkMid = '\u0002', LinkEnd = '\u0003';

    /// <summary>第 05-2 章：把 <paramref name="text"/> 標成連結（<paramref name="kind"/>：s＝聚落、p＝人物、h＝家族（開創者的人物編號））。</summary>
    public static string Link(char kind, long id, string text)
        => text.Length == 0 ? text : string.Concat(LinkStart.ToString(), kind.ToString(), id.ToString(CultureInfo.InvariantCulture), LinkMid.ToString(), text, LinkEnd.ToString());

    /// <summary>去掉連結標記，留下正文。</summary>
    public static string Plain(string marked)
    {
        ArgumentNullException.ThrowIfNull(marked);
        if (marked.IndexOf(LinkStart, StringComparison.Ordinal) < 0)
            return marked;
        var sb = new System.Text.StringBuilder(marked.Length);
        for (var i = 0; i < marked.Length; i++)
        {
            var c = marked[i];
            if (c == LinkStart)
            {
                var mid = marked.IndexOf(LinkMid, i);
                if (mid < 0)
                    break;
                i = mid;
                continue;
            }
            if (c != LinkEnd)
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>第 05-2 章：史書一行裡的一段（Kind＝'\0' 是正文；否則是連結）。</summary>
    public sealed record Piece(string Text, char Kind = '\0', long Id = 0);

    /// <summary>把有連結標記的一行拆成正文與連結的段落（介面用來畫可以點的名稱）。</summary>
    public static IReadOnlyList<Piece> Pieces(string marked)
    {
        ArgumentNullException.ThrowIfNull(marked);
        var spans = new List<Piece>();
        var plain = new System.Text.StringBuilder();
        for (var i = 0; i < marked.Length;)
        {
            if (marked[i] != LinkStart)
            {
                plain.Append(marked[i++]);
                continue;
            }
            var mid = marked.IndexOf(LinkMid, i);
            var end = mid < 0 ? -1 : marked.IndexOf(LinkEnd, mid);
            if (mid < 0 || end < 0 || mid < i + 2)
            {
                plain.Append(Plain(marked[i..]));
                break;
            }
            if (plain.Length > 0)
            {
                spans.Add(new Piece(plain.ToString()));
                plain.Clear();
            }
            long.TryParse(marked.AsSpan(i + 2, mid - i - 2), NumberStyles.None, CultureInfo.InvariantCulture, out var id);
            spans.Add(new Piece(marked[(mid + 1)..end], marked[i + 1], id));
            i = end + 1;
        }
        if (plain.Length > 0)
            spans.Add(new Piece(plain.ToString()));
        return spans;
    }

    /// <summary>
    /// 第 05-2 章：整部史書，名稱帶連結標記（聚落、人物、家族；遊戲裡的史書分頁點得到）。去掉標記（<see cref="Plain"/>）就是 <see cref="Write"/>。
    /// </summary>
    public static IReadOnlyList<string> WriteMarked(DemoObserverWorld world, DemoSagaOpening? opening = null)
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
        var settlementEvents = world.SettlementEvents();
        var causes = DeathCauses(settlementEvents); // 第 08-10j 節：未得善終的死因
        // 第 08-10m 節：合成句子要看事件的種類與對象（獨立、被征服）。
        var realmEvents = settlementEvents.Where(e => e.EventId > 0 && e.Kind is DemoSettlementEventKind.Independence or DemoSettlementEventKind.Conquered)
            .ToDictionary(e => e.EventId);
        var nameOf = world.Sites.Settlements.ToDictionary(s => s.Id, s => s.Name);
        var toldEntries = ToldEntries(years).ToHashSet<DemoChronicleEntry>(ReferenceEqualityComparer.Instance);
        var cast = new DemoSagaCast(world); // 第 08-10f 章：人物進入史書
        var lastYear = years.Count == 0 ? 1 : years[^1].Year;
        // 第 08-6c 章：一個世界時代一卷（原本固定十年一卷）。一年歸在它最後一週（還沒過完的年＝目前這一週）的時代那一卷；
        // 世界法則視窗在年中指定的時代因此從下一卷開始，不到一年就換掉的時代沒有自己的一卷（更替仍寫在那年的大事裡）。
        var number = 0;
        var ages = world.Ages();
        var now = Math.Max(1, world.CommittedWeeks);
        int AgeIndexOf(int year)
        {
            var week = Math.Min(DemoAges.LastWeek(year), now);
            var i = ages.Count - 1;
            while (i > 0 && ages[i].FromWeek > week)
                i--;
            return i;
        }
        foreach (var (span, index) in ages.Select((s, i) => (s, i)))
        {
            var volume = years.Where(y => AgeIndexOf(y.Year) == index).ToList();
            if (volume.Count == 0)
                continue;
            lines.Add("");
            lines.Add(I($"卷 {++number}　{DemoAges.Name(span.Age)}　第 {volume[0].Year}～{volume[^1].Year} 年　{DemoAges.Description(span.Age)}"));

            // 大事：世界大事（技術另收成一句）、任何聚落淪為遺跡、城市以上的升降。一模一樣的句子合併年份。
            // 第 08-10m 節：同一卷的請願獨立合成一句、跟著首領脫離的不另寫、同一個征服者收服的聚落合成一句；勢力戰爭的死傷數不寫（每卷的死因已經有）。
            var entries = volume.SelectMany(y => y.Entries.Where(toldEntries.Contains).Select(e => (y.Year, Entry: e))).ToList();
            var (condensed, consumed) = Condense(entries, realmEvents, nameOf);
            var told = entries.Where(x => !consumed.Contains(x.Entry))
                .Select(x => (x.Year, Text: x.Entry.Category == DemoChronicleCategory.Realm ? Casualties.Replace(cast.MarkedSentence(x.Entry), "") : cast.MarkedSentence(x.Entry)))
                .GroupBy(x => Plain(x.Text), StringComparer.Ordinal)
                .Select(g => (Years: g.Select(x => x.Year).Distinct().ToList(), Text: g.First().Text)).ToList();
            foreach (var (_, text) in told.Select(t => (t.Years[0], I($"　第 {string.Join("、", t.Years)} 年，{t.Text}")))
                .Concat(condensed).OrderBy(x => x.Item1))
                lines.Add(text);
            var techs = Techs(volume);
            if (techs is not null)
                lines.Add("　" + techs);
            if (told.Count == 0 && condensed.Count == 0 && techs is null)
                lines.Add("　這些年沒有驚動世界的大事。");
            var local = DemoWorldChronicle.LocalTally(volume);
            if (local.Length > 0)
                lines.Add("　各地：" + local + "。");
            foreach (var ruler in cast.RulersMarked(volume[0].Year, volume[^1].Year, lastYear))
                lines.Add("　人物：" + ruler);
            foreach (var hero in cast.HeroesMarked(volume[0].Year, volume[^1].Year, VolumeHeroes)) // 第 08-10i 章；第 08-10m 節：其餘只列名字
                lines.Add("　英雄：" + hero);
            lines.Add("　" + People(volume, ledger, causes));
        }

        lines.Add("");
        lines.AddRange(Ending(world, years.Count, cast));
        var linker = new SettlementLinker(world); // 第 05-2 章：聚落名稱一律可以點
        return lines.Select(linker.Apply).ToList();
    }

    /// <summary>第 08-10m 節：勢力戰爭句子裡的死傷數（「（叛軍死 3、上級死 1）」「（攻方死 2、守方死 6）」）。</summary>
    private static readonly Regex Casualties = new("（(?:戰爭分數 -?\\d+；)?(?:叛軍|攻方)死 \\d+、(?:上級|守方)死 \\d+）", RegexOptions.CultureInvariant); // 第 08-22a 章：征服戰爭多寫戰爭分數

    /// <summary>
    /// 第 08-10m 節：把一卷裡的勢力大事合成幾句——請願獲准而獨立的（兩件以上）合成一句；跟著首領脫離的不另寫；
    /// 同一個征服者收服的聚落（兩處以上）合成一句。回傳合成的句子（依第一年排序用）與被合併掉的條目。
    /// </summary>
    internal static (List<(int FirstYear, string Text)> Lines, HashSet<DemoChronicleEntry> Consumed) Condense(
        IReadOnlyList<(int Year, DemoChronicleEntry Entry)> entries, IReadOnlyDictionary<long, DemoSettlementEvent> realm,
        IReadOnlyDictionary<int, string> nameOf)
    {
        static string I(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        string Name(int id) => nameOf.GetValueOrDefault(id, "某地");
        string Names(IEnumerable<int> ids)
        {
            var list = ids.Distinct().Select(Name).ToList();
            return string.Join("、", list.Take(CondensedNames)) + (list.Count > CondensedNames ? "等" : "");
        }
        static string Span(IReadOnlyCollection<int> years) => years.Min() == years.Max() ? I($"第 {years.Min()} 年") : I($"第 {years.Min()}～{years.Max()} 年");
        var lines = new List<(int, string)>();
        var consumed = new HashSet<DemoChronicleEntry>(ReferenceEqualityComparer.Instance);
        DemoSettlementEvent? Event(DemoChronicleEntry e) => e.Category == DemoChronicleCategory.Realm && realm.TryGetValue(e.EventId, out var x) ? x : null;

        var independent = entries.Where(x => Event(x.Entry)?.Kind == DemoSettlementEventKind.Independence).ToList();
        consumed.UnionWith(independent.Where(x => Event(x.Entry)!.Cause.StartsWith("隨", StringComparison.Ordinal)).Select(x => x.Entry));
        var petitions = independent.Where(x => Event(x.Entry)!.Cause.Contains("的請願", StringComparison.Ordinal)).ToList();
        if (petitions.Count >= 2)
        {
            var years = petitions.Select(x => x.Year).ToList();
            lines.Add((years.Min(), I($"　{Span(years)}，{petitions.Count} 處聚落請願獲准、脫離上級獨立：{Names(petitions.Select(x => Event(x.Entry)!.SettlementId))}。")));
            consumed.UnionWith(petitions.Select(x => x.Entry));
        }
        foreach (var conqueror in entries.Where(x => Event(x.Entry)?.Kind == DemoSettlementEventKind.Conquered)
            .GroupBy(x => (int)Event(x.Entry)!.Count).Where(g => g.Count() >= 2).OrderBy(g => g.Min(x => x.Year)).ThenBy(g => g.Key))
        {
            var years = conqueror.Select(x => x.Year).ToList();
            lines.Add((years.Min(), I($"　{Span(years)}，{Name(conqueror.Key)}先後收服{Names(conqueror.Select(x => Event(x.Entry)!.SettlementId))}（{conqueror.Count()} 處聚落）。")));
            consumed.UnionWith(conqueror.Select(x => x.Entry));
        }
        return (lines, consumed);
    }

    /// <summary>第 05-2 章：把一行裡出現的聚落名稱標成連結（聚落名稱不重複；長的先比對；已經是連結的部分不動）。</summary>
    private sealed class SettlementLinker
    {
        private readonly Dictionary<char, List<(string Name, int Id)>> _byFirst;

        public SettlementLinker(DemoObserverWorld world)
        {
            _byFirst = world.Sites.Settlements.Where(s => s.Name.Length > 0)
                .GroupBy(s => s.Name, StringComparer.Ordinal).Select(g => (Name: g.Key, Id: g.Min(s => s.Id)))
                .GroupBy(x => x.Name[0])
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Name.Length).ThenBy(x => x.Name, StringComparer.Ordinal).ToList());
        }

        public string Apply(string text)
        {
            var sb = new System.Text.StringBuilder(text.Length + 16);
            for (var i = 0; i < text.Length;)
            {
                if (text[i] == LinkStart)
                {
                    var end = text.IndexOf(LinkEnd, i);
                    if (end < 0)
                    {
                        sb.Append(text, i, text.Length - i);
                        break;
                    }
                    sb.Append(text, i, end - i + 1);
                    i = end + 1;
                    continue;
                }
                var matched = false;
                if (_byFirst.TryGetValue(text[i], out var names))
                    foreach (var (name, id) in names)
                        if (i + name.Length <= text.Length && string.CompareOrdinal(text, i, name, 0, name.Length) == 0)
                        {
                            sb.Append(Link('s', id, name));
                            i += name.Length;
                            matched = true;
                            break;
                        }
                if (!matched)
                    sb.Append(text[i++]);
            }
            return sb.ToString();
        }
    }

    /// <summary>終章：最後一年的世界——人口與聚落、最大的幾個聚落與領袖、各勢力。</summary>
    private static IEnumerable<string> Ending(DemoObserverWorld world, int year, DemoSagaCast cast)
    {
        static string I(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        var active = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        yield return I($"終章　第 {year} 年的世界");
        yield return I($"　世上有 {world.WorldPopulation} 人、{active.Count} 處有人煙的聚落、{world.Sites.Settlements.Count(world.Sites.IsStandingRuin)} 處遺跡。"); // 第 12-2i 章：重建過的不算
        yield return "　時代：" + DemoAges.Name(world.Age) + "。"; // 第 08-6c 章：世界時代

        string Titled(DemoSettlement s) => DemoSettlementHistory.TitledName(s, world.ChronicleOf(s.Id).Tier);
        string LeaderOf(DemoSettlement s)
            => world.RepresentativesOf(s.Id).FirstOrDefault(r => r.Role == DemoRepresentativeRole.Leader) is { } leader ? "，領袖" + Link('p', leader.Id, leader.Name) : "";
        var largest = active.OrderByDescending(s => world.SettlementPopulation(s.Id)).ThenBy(s => s.Id).Take(EndingSettlements)
            .Select(s => I($"{Titled(s)}（{world.SettlementPopulation(s.Id)} 人{LeaderOf(s)}）")).ToList();
        if (largest.Count > 0)
            yield return "　最大的聚落：" + string.Join("、", largest) + "。";

        var territory = world.Territory();
        var realms = active.Select(s => (Site: s, Vassals: territory.VassalsOf(s.Id).Count)).Where(x => x.Vassals > 0)
            .OrderByDescending(x => x.Vassals).ThenBy(x => x.Site.Id)
            .Select(x => I($"{Titled(x.Site)}統領 {x.Vassals} 個聚落")).ToList();
        var ruins = world.Sites.Settlements.Where(world.Sites.IsStandingRuin).OrderBy(x => x.RuinedWeek).ThenBy(x => x.Id) // 第 12-2i 章：重建過的不算
            .Select(x => I($"{x.Name}（全盛時 {world.ChronicleOf(x.Id).Peak} 人）")).ToList();
        if (ruins.Count > 0)
            yield return "　已成遺跡：" + string.Join("、", ruins) + "。";
        var relics = cast.RelicsMarked(); // 第 08-10i 章
        if (relics.Count > 0)
            yield return "　傳說物品：" + string.Join("；", relics) + "。";
        var houses = cast.HousesMarked(EndingSettlements); // 第 08-10g 章
        if (houses.Count > 0)
            yield return "　家族：" + string.Join("；", houses) + "。";
        yield return realms.Count > 0 ? "　勢力：" + string.Join("；", realms) + "。" : "　世上還沒有統領其他聚落的勢力。";

        // 第 12-2f 節：世上還有哪些險境、誰的軍隊最強（怪物討伐這條故事線的現況）。
        var dangers = world.MonsterNests(world.CommittedWeeks)
            .Select(n => I($"{n.Dungeon.Name}（{n.Dungeon.Layers} 層，{(n.Dormant ? "沉寂中" : I($"{DemoCombat.MonsterLevel(n.Dungeon)} 級怪物 {n.Monsters} 隻"))}）")).ToList();
        if (dangers.Count > 0)
            yield return "　險境：" + string.Join("、", dangers) + "。";
        var army = active.Select(s => (Site: s, Force: world.ExpeditionForceOf(s.Id))).Where(x => x.Force.Soldiers > 0)
            .OrderByDescending(x => x.Force.Soldiers * x.Force.SoldierLevel).ThenBy(x => x.Site.Id).FirstOrDefault();
        yield return army.Site is null ? "　世上還沒有軍隊。"
            : I($"　最強的軍隊：{Titled(army.Site)}（{army.Force.SoldierLevel} 級士兵 {army.Force.Soldiers} 人）。");
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

    /// <summary>這一筆要不要逐條寫進史書：世界大事（技術除外）、任何聚落淪為遺跡、城鎮以上的等級升降、戰爭的結局、地下城的出現與被殺光。</summary>
    public static bool IsTold(DemoChronicleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        // 第 08-19h 章：開了戰就要有下文——沒打贏的結局也寫。
        if (entry.Category == DemoChronicleCategory.Realm && entry.Level == DemoChronicleLevel.Normal)
            return WarEndings.Any(w => entry.Text.Contains(w, StringComparison.Ordinal));
        // 第 12-2e 節：地下城的出現與被殺光也寫（怪物討伐的故事）；怪物再生不寫（會一再重複）。
        if (entry.Category == DemoChronicleCategory.Wilds)
            return entry.Level == DemoChronicleLevel.Major && !entry.Text.Contains("又爬出了怪物", StringComparison.Ordinal);
        // 第 08-19i 章：開戰不另寫——戰爭的結局（征服、獨立、敗退、白和平）那一句已經說了誰打誰、結果如何。
        if (entry.Level != DemoChronicleLevel.Major || entry.Category == DemoChronicleCategory.Knowledge || entry.Weight == DemoWorldChronicle.WeightWar)
            return false;
        if (entry.IsWorld || entry.Category == DemoChronicleCategory.Ruin)
            return true;
        // 第 12-2i 章：在遺跡上重建（人類收復失去的土地）也寫——和「淪為遺跡」一來一往。
        if (entry.Category == DemoChronicleCategory.Founding && entry.Text.Contains(DemoWorldChronicle.RebuiltMarker.Trim(), StringComparison.Ordinal))
            return true;
        // 第 08-10m 節：城鎮成立不逐條寫（世上第一座是世界大事，上面已經寫了；其餘算在「各地」的等級升降裡）；
        // 城市、首都的成立與城市降為城鎮照寫。
        return entry.Category == DemoChronicleCategory.Tier
            && (new[] { DemoSettlementTier.City, DemoSettlementTier.Capital }
                    .Any(t => entry.Text.Contains(DemoSettlementHistory.TierName(t), StringComparison.Ordinal))
                || entry.Text.Contains(DemoSettlementChronicle.TierDownMarker + DemoSettlementHistory.TierName(DemoSettlementTier.Town), StringComparison.Ordinal));
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

    /// <summary>第 08-10j 節：史書列出的死因（依這個順序比大小相同時的先後）。</summary>
    public static readonly string[] DeathCauseNames = { "戰火", "餓死", "怪物", "天災", "野獸" };

    /// <summary>第 08-10j 節：這種事件的人數欄是哪一種死因的死亡人數；不是死亡事件＝null。</summary>
    public static string? DeathCause(DemoSettlementEventKind kind) => kind switch
    {
        DemoSettlementEventKind.SecessionBattle or DemoSettlementEventKind.ConquestBattle => "戰火",
        DemoSettlementEventKind.StarvationDeath or DemoSettlementEventKind.MigrantStarvationDeath => "餓死",
        DemoSettlementEventKind.MonsterRaid or DemoSettlementEventKind.MonsterHunt or DemoSettlementEventKind.MonsterSwarm => "怪物",
        DemoSettlementEventKind.Earthquake or DemoSettlementEventKind.Tornado or DemoSettlementEventKind.Meteorite
            or DemoSettlementEventKind.Flood => "天災",
        DemoSettlementEventKind.BeastAttack => "野獸",
        _ => null
    };

    /// <summary>第 08-10j 節：每年各種死因的死亡人數（由事件推得）。</summary>
    public static IReadOnlyDictionary<int, IReadOnlyDictionary<string, long>> DeathCauses(IEnumerable<DemoSettlementEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return events.Where(e => e.Count > 0 && DeathCause(e.Kind) is not null)
            .GroupBy(e => DemoSettlementHistory.YearOfDay(checked(Math.Max(1, e.Week) * 7)))
            .ToDictionary(g => g.Key, g => (IReadOnlyDictionary<string, long>)g.GroupBy(e => DeathCause(e.Kind)!)
                .ToDictionary(c => c.Key, c => c.Sum(e => e.Count)));
    }

    /// <summary>
    /// 這一卷的生死與卷末人口。優先用世界的年帳（出生、壽終、其餘死亡、年末人口）；年帳不齊（曆法不同的測試世界）時只寫編年史數得到的出生與壽終。
    /// 第 08-10j 節：未得善終的人數後面列出死因（多的在前；事件數不到的寫「其他」）。
    /// </summary>
    private static string People(IReadOnlyList<DemoChronicleYear> volume, IReadOnlyDictionary<int, DemoYearSummary> ledger,
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, long>> causes)
    {
        static string I(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        if (!volume.All(y => ledger.ContainsKey(y.Year)))
            return I($"這些年裡 {volume.Sum(y => y.Births)} 人出生，{volume.Sum(y => y.NaturalDeaths)} 人壽終。");
        var rows = volume.Select(y => ledger[y.Year]).ToList();
        var births = rows.Sum(r => r.Births);
        var natural = rows.Sum(r => r.NaturalDeaths);
        var other = rows.Sum(r => r.Deaths) - natural;
        var last = rows[^1];
        var tally = DeathCauseNames
            .Select((name, order) => (Name: name, Order: order,
                Count: volume.Sum(y => causes.TryGetValue(y.Year, out var t) ? t.GetValueOrDefault(name) : 0L)))
            .Where(x => x.Count > 0).OrderByDescending(x => x.Count).ThenBy(x => x.Order)
            .Select(x => (x.Name, x.Count)).ToList();
        if (other > tally.Sum(x => x.Count))
            tally.Add(("其他", other - tally.Sum(x => x.Count)));
        var detail = tally.Count == 0 ? "" : "（" + string.Join("、", tally.Select(x => I($"{x.Name} {x.Count}"))) + "）";
        return I($"這些年裡 {births} 人出生，{natural} 人壽終")
            + (other > 0 ? I($"，{other} 人未得善終") + detail : "")
            + I($"；第 {last.Year} 年末世上有 {last.PopulationClosing} 人。");
    }
}

/// <summary>
/// 第 08-10f 章：史書裡的人物——由人物登記表（第 14-1 章第 7 項）與已提交的事件推得，不改世界、不另存。
/// 勢力大事（征服、獨立、戰爭結局）的句子裡，每個聚落第一次出現時註明當時的領袖；
/// 每卷另寫事蹟夠多的領袖：在位年份、降服與失去的聚落數、怎麼離開的。只陳述事實。
/// </summary>
public sealed class DemoSagaCast
{
    private readonly Dictionary<int, List<DemoPersonRecord>> _leaders;
    private readonly List<(string Name, int Id)> _names;
    private readonly Dictionary<int, string> _nameOf;
    private readonly List<(int Week, int Attacker)> _conquests;
    private readonly List<(int Week, int Liege)> _losses;
    private readonly IReadOnlyList<DemoLegend> _legends;
    private readonly Dictionary<long, DemoPersonRecord> _people;

    public DemoSagaCast(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _leaders = world.PersonRecords.Where(p => p.Role == DemoRepresentativeRole.Leader)
            .GroupBy(p => p.SettlementId).ToDictionary(g => g.Key, g => g.OrderBy(p => p.AppointedWeek).ThenBy(p => p.Id).ToList());
        _nameOf = world.Sites.Settlements.ToDictionary(x => x.Id, x => x.Name);
        // 長的名稱先比對（「柯林菲爾」先於「柯林」），同名取編號小的。
        _names = world.Sites.Settlements.GroupBy(x => x.Name, StringComparer.Ordinal).Select(g => (g.Key, g.Min(x => x.Id)))
            .OrderByDescending(x => x.Key.Length).ThenBy(x => x.Key, StringComparer.Ordinal).ToList();
        var events = world.SettlementEvents();
        _conquests = events.Where(e => e.Kind == DemoSettlementEventKind.Conquered).Select(e => (e.Week, (int)e.Count)).ToList();
        _losses = events.Where(e => e.Kind == DemoSettlementEventKind.Independence).Select(e => (e.Week, (int)e.Count)).ToList();
        _legends = DemoHeroes.Legends(world);
        _tierAt = (id, week) => world.ChronicleOf(id).TierAt(week); // 第 08-10l 節：領袖的稱號依當時的等級
        _people = world.PersonRecords.ToDictionary(p => p.Id);
    }

    /// <summary>第 05-2 章：人名連結。</summary>
    private static string P(DemoPersonRecord p) => DemoSaga.Link('p', p.Id, p.Name);

    /// <summary>第 05-2 章：「某某家」連結到這個人所屬的家族（不屬於任何家族就只寫文字）。</summary>
    private string H(long personId, string family)
        => DemoHouses.HouseOf(_people, personId) is { } founder ? DemoSaga.Link('h', founder, family + "家") : family + "家";

    private readonly Func<int, int, DemoSettlementTier> _tierAt;

    private string Origin(DemoPersonRecord hero)
        => hero.ParentId > 0 && _people.TryGetValue(hero.ParentId, out var parent)
            ? $"{DemoRepresentative.RoleName(parent.Role, _tierAt(parent.SettlementId, hero.AppointedWeek))}{P(parent)}的{(hero.Sex == DemoTempSex.Female ? "女兒" : "兒子")}"
            : "士兵出身";

    /// <summary>
    /// 第 08-10i 章：這一卷成名的英雄（每人一句）：哪一年、哪個聚落的誰（出身、性格）立下什麼事蹟、得到什麼；已經過世的寫出結局。
    /// </summary>
    public IReadOnlyList<string> Heroes(int fromYear, int toYear) => HeroesMarked(fromYear, toYear).Select(DemoSaga.Plain).ToList();

    /// <summary>同 <see cref="Heroes"/>，名稱帶連結標記。</summary>
    /// <remarks>第 08-10m 節：沒有傳說物品的英雄超過 <paramref name="keepWithoutItem"/> 位時，其餘只在最後一句列名字。</remarks>
    internal IReadOnlyList<string> HeroesMarked(int fromYear, int toYear, int keepWithoutItem = int.MaxValue)
    {
        static string I(FormattableString s) => s.ToString(System.Globalization.CultureInfo.InvariantCulture);
        static int Year(int week) => DemoSettlementHistory.YearOfDay(checked(Math.Max(1, week) * 7));
        var lines = new List<string>();
        var others = new List<DemoPersonRecord>();
        foreach (var l in _legends.Where(l => Year(l.Week) >= fromYear && Year(l.Week) <= toYear))
        {
            if (l.Item.Length == 0 && lines.Count(x => !x.Contains("，得到「", StringComparison.Ordinal)) >= keepWithoutItem)
            {
                others.Add(l.Hero);
                continue;
            }
            var temper = DemoTraits.Names(l.Hero.Traits);
            var end = l.Hero.InOffice ? "" : I($"；第 {Year(l.Hero.ExitWeek)} 年{ExitText(l.Hero.ExitReason)}");
            var prize = l.Item.Length > 0 ? $"，得到「{l.Item}」" : "";
            lines.Add(I($"第 {Year(l.Week)} 年，{_nameOf.GetValueOrDefault(l.Hero.SettlementId, "某地")}的{P(l.Hero)}（{Origin(l.Hero)}{(temper.Length > 0 ? "；" + temper : "")}）{l.Deed.Text}{prize}{end}。"));
        }
        if (others.Count > 0)
            lines.Add(I($"另有 {others.Count} 位英雄在平亂與獨立戰爭中成名：{string.Join("、", others.Select(h => $"{_nameOf.GetValueOrDefault(h.SettlementId, "某地")}的{P(h)}"))}。"));
        return lines;
    }

    /// <summary>
    /// 第 08-10i 章：傳說物品的下落（每件一句）：英雄在世由英雄持有；領袖家族出身的英雄死後由家族收藏；士兵出身的英雄死後下落不明。
    /// </summary>
    public IReadOnlyList<string> Relics() => RelicsMarked().Select(DemoSaga.Plain).ToList();

    /// <summary>同 <see cref="Relics"/>，名稱帶連結標記。</summary>
    internal IReadOnlyList<string> RelicsMarked()
        => _legends.Where(l => l.Item.Length > 0).Select(l =>
        {
            var place = _nameOf.GetValueOrDefault(l.Hero.SettlementId, "某地");
            var holder = l.Hero.InOffice ? $"由{place}的英雄{P(l.Hero)}持有"
                : l.Hero.ParentId > 0 ? $"{P(l.Hero)}死後由{place}的{H(l.Hero.Id, DemoNames.FamilyOf(l.Hero.Name))}收藏"
                : $"{P(l.Hero)}死後下落不明";
            return $"「{l.Item}」{holder}";
        }).ToList();

    /// <summary>第 <paramref name="week"/> 週時這個聚落的領袖（那時沒有領袖＝null）。</summary>
    public DemoPersonRecord? LeaderAt(int settlementId, int week)
        => _leaders.TryGetValue(settlementId, out var list)
            ? list.LastOrDefault(p => p.AppointedWeek <= week && (p.InOffice || week <= p.ExitWeek))
            : null;

    /// <summary>史書的一句：勢力大事註明各聚落當時的領袖；其他條目照 <see cref="DemoSaga.Sentence"/>。</summary>
    public string Sentence(DemoChronicleEntry entry) => DemoSaga.Plain(MarkedSentence(entry));

    /// <summary>同 <see cref="Sentence"/>，領袖的名字帶連結標記（聚落名稱由史書最後統一標記）。</summary>
    internal string MarkedSentence(DemoChronicleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var text = DemoSaga.Sentence(entry);
        if (entry.Category != DemoChronicleCategory.Realm)
            return text;
        return Insert(text, _names.Select(n => (n.Name, Leader: LeaderAt(n.Id, entry.Week))).Where(n => n.Leader is not null)
            .Select(n => (n.Name, "（" + P(n.Leader!) + "）")).ToList());
    }

    /// <summary>
    /// 在句子裡每個聚落名稱第一次出現的地方後面加上「（領袖）」。<paramref name="cast"/> 要由長到短排（長名稱先佔位，短名稱不會切進長名稱裡）。
    /// </summary>
    public static string Annotate(string text, IReadOnlyList<(string Name, string Leader)> cast)
    {
        ArgumentNullException.ThrowIfNull(cast);
        return Insert(text, cast.Select(c => (c.Name, "（" + c.Leader + "）")).ToList());
    }

    /// <summary>在每個名稱第一次出現（不和已佔位的名稱重疊）的地方後面插入一段文字。</summary>
    private static string Insert(string text, IReadOnlyList<(string Name, string Insert)> cast)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(cast);
        var spans = new List<(int Start, int End, string Leader)>();
        foreach (var (name, leader) in cast)
        {
            if (name.Length == 0)
                continue;
            // 找第一個沒有和已佔位名稱重疊的出現位置。
            for (var at = text.IndexOf(name, StringComparison.Ordinal); at >= 0; at = text.IndexOf(name, at + 1, StringComparison.Ordinal))
            {
                var end = at + name.Length;
                if (spans.Any(x => at < x.End && x.Start < end))
                    continue;
                spans.Add((at, end, leader));
                break;
            }
        }
        var sb = new System.Text.StringBuilder(text);
        foreach (var x in spans.OrderByDescending(x => x.End))
            sb.Insert(x.End, x.Leader);
        return sb.ToString();
    }

    /// <summary>
    /// 這一卷要寫的領袖（每人一句）：任期在這一卷結束的，或到史書最後一年仍在位而這是最後一卷的；
    /// 降服加失去的聚落至少 <see cref="DemoSaga.RulerDeeds"/> 件才寫，降服多的在前，最多 <see cref="DemoSaga.VolumeRulers"/> 位。
    /// </summary>
    public IReadOnlyList<string> Rulers(int fromYear, int toYear, int lastYear) => RulersMarked(fromYear, toYear, lastYear).Select(DemoSaga.Plain).ToList();

    /// <summary>同 <see cref="Rulers"/>，名稱帶連結標記。</summary>
    internal IReadOnlyList<string> RulersMarked(int fromYear, int toYear, int lastYear)
    {
        static string I(FormattableString s) => s.ToString(System.Globalization.CultureInfo.InvariantCulture);
        static int Year(int week) => DemoSettlementHistory.YearOfDay(checked(Math.Max(1, week) * 7));
        var picked = new List<(int Conquered, int Lost, long Id, string Text)>();
        foreach (var (settlementId, list) in _leaders.OrderBy(kv => kv.Key))
            foreach (var p in list)
            {
                var endYear = p.InOffice ? lastYear : Year(p.ExitWeek);
                if (endYear < fromYear || endYear > toYear || (p.InOffice && toYear < lastYear))
                    continue;
                bool During(int week) => week >= p.AppointedWeek && (p.InOffice || week <= p.ExitWeek);
                var conquered = _conquests.Count(c => c.Attacker == settlementId && During(c.Week));
                var lost = _losses.Count(c => c.Liege == settlementId && During(c.Week));
                if (conquered + lost < DemoSaga.RulerDeeds)
                    continue;
                var deeds = new List<string>();
                if (conquered > 0) deeds.Add(I($"降服 {conquered} 個聚落"));
                if (lost > 0) deeds.Add(I($"失去 {lost} 個聚落"));
                var reign = p.AppointedWeek < 1 ? I($"開局～第 {endYear} 年") : I($"第 {Year(p.AppointedWeek)}～{endYear} 年");
                var exit = p.InOffice ? "至今仍在位" : I($"第 {endYear} 年{ExitText(p.ExitReason)}");
                var title = conquered >= DemoSaga.ConquerorDeeds ? "征服者" : "";
                // 第 08-10g 章：性格；誰接任——子女（同一家族）還是別的家族。
                var temper = DemoTraits.Names(p.Traits);
                var next = list.SkipWhile(x => x.Id != p.Id).Skip(1).FirstOrDefault();
                var heir = p.InOffice || next is null ? ""
                    : next.ParentId == p.Id ? I($"，由{(next.Sex == DemoTempSex.Female ? "女兒" : "兒子")}{P(next)}繼位")
                    : I($"，{H(p.Id, DemoNames.FamilyOf(p.Name))}就此失去權位，{H(next.Id, DemoNames.FamilyOf(next.Name))}的{P(next)}接掌");
                picked.Add((conquered, lost, p.Id,
                    I($"{_nameOf.GetValueOrDefault(settlementId, "某地")}的{title}{P(p)}（{(temper.Length > 0 ? temper + "；" : "")}{reign}在位）{string.Join("、", deeds)}，{exit}{heir}。")));
            }
        return picked.OrderByDescending(x => x.Conquered).ThenByDescending(x => x.Lost).ThenBy(x => x.Id).Take(DemoSaga.VolumeRulers).Select(x => x.Text).ToList();
    }

    /// <summary>
    /// 第 08-10g 章：掌權的家族（參考十字軍之王的家族）——一位沒有承襲父母的領袖是家族的第一代，之後一路世襲的都屬於這一家。
    /// 每個家族一句：在哪裡、姓什麼、傳了幾代、從哪一年掌權到哪一年（或至今）。代數多的在前，最多 <paramref name="take"/> 個；只有一代的不列。
    /// </summary>
    public IReadOnlyList<string> Houses(int take) => HousesMarked(take).Select(DemoSaga.Plain).ToList();

    /// <summary>同 <see cref="Houses"/>，家族與歷代家主帶連結標記。</summary>
    internal IReadOnlyList<string> HousesMarked(int take)
    {
        static string I(FormattableString s) => s.ToString(System.Globalization.CultureInfo.InvariantCulture);
        static int Year(int week) => DemoSettlementHistory.YearOfDay(checked(Math.Max(1, week) * 7));
        var houses = new List<(int Generations, int From, long Id, string Text)>();
        foreach (var (settlementId, list) in _leaders.OrderBy(kv => kv.Key))
        {
            var line = new List<DemoPersonRecord>();
            void Close()
            {
                if (line.Count >= 2)
                {
                    var last = line[^1];
                    var from = line[0].AppointedWeek < 1 ? "開局" : I($"第 {Year(line[0].AppointedWeek)} 年");
                    var to = last.InOffice ? "至今" : I($"第 {Year(last.ExitWeek)} 年");
                    houses.Add((line.Count, line[0].AppointedWeek, line[0].Id,
                        I($"{_nameOf.GetValueOrDefault(settlementId, "某地")}的{H(line[0].Id, DemoNames.FamilyOf(line[0].Name))}（傳 {line.Count} 代，{from}～{to}掌權；{string.Join("→", line.Select(x => DemoSaga.Link('p', x.Id, DemoNames.GivenOf(x.Name))))}）")));
                }
                line.Clear();
            }
            foreach (var p in list)
            {
                if (line.Count > 0 && p.ParentId != line[^1].Id)
                    Close();
                line.Add(p);
            }
            Close();
        }
        return houses.OrderByDescending(h => h.Generations).ThenBy(h => h.From).ThenBy(h => h.Id).Take(take).Select(h => h.Text).ToList();
    }

    /// <summary>登記表的卸任原因（「老死（63 歲）」「餓死（…）」「死於天災（…）」「聚落無人」「卸任」）寫成史書用語。</summary>
    public static string ExitText(string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (reason.StartsWith("老死", StringComparison.Ordinal)) return "壽終" + reason["老死".Length..];
        if (reason.StartsWith("死於天災", StringComparison.Ordinal)) return "死於災禍或兵燹" + reason["死於天災".Length..];
        if (reason.StartsWith("餓死", StringComparison.Ordinal)) return reason;
        return reason == "聚落無人" ? "隨聚落消亡" : "卸任";
    }
}

