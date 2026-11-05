using System.Globalization;
using WorldSimulation.Core.Config;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-16 章第 15 節（試玩回饋 2026-11-04 夜之 3：信仰、文化看不出核心內涵與傳統，各開一頁詳細查看）。
/// 十字軍之王式的文化頁與信仰頁：核心（傳統或信條逐條說明它是什麼、在這個世界裡實際起什麼作用）、分布（信徒）、歷史。
/// 只讀世界，不改狀態、不抽亂數。
/// </summary>
public static class DemoBeliefDetail
{
    /// <summary>分布頁最多列幾個聚落（人多的在前）。</summary>
    public const int MaxPlaces = 12;

    private static readonly Lazy<IReadOnlyDictionary<string, string>> Descriptions = new(LoadDescriptions);

    /// <summary>
    /// 特性、信條、教義的說明（04、06A～06J 的「說明」欄）；沒有＝空字串。
    /// 說明欄是開發註記（【TEMP】開頭，寫的是規則章節）的特性，改用 <see cref="PlayerNotes"/> 給玩家看的一句話（規則效果另列）。
    /// </summary>
    public static string Description(string id)
    {
        var raw = Descriptions.Value.GetValueOrDefault(id, "");
        if (raw.StartsWith("【TEMP】", StringComparison.Ordinal) && PlayerNotes.TryGetValue(id, out var note))
            return note;
        return DemoPlayerDetail.Clean(raw);
    }

    private static readonly IReadOnlyDictionary<string, string> PlayerNotes = new Dictionary<string, string>
    {
        ["CT_FOREST_FOLK"] = "世代住在林邊，懂得在森林裡採集與狩獵",
        ["CT_RIVER_FOLK"] = "傍河而居，擅長捕魚與在河上往來",
        ["CT_SEA_FOLK"] = "靠海為生，熟悉海潮與舟船",
        ["CT_STEPPE_FOLK"] = "在開闊的草原上採集、追逐獸群",
        ["CT_WANDERER"] = "習慣遷徙，帶著輕便的行囊走得很遠",
        ["CT_ROOTED"] = "守著祖先的土地，不輕易離鄉",
        ["CT_ELDER_COUNCIL"] = "大事由長老商議，最年長的人帶領眾人",
        ["CT_STRONG_RULE"] = "推舉最強壯能幹的人帶領眾人",
        ["CT_ORAL_EPIC"] = "祖先的事蹟靠吟唱的史詩代代相傳",
        ["CT_FACE_TATTOO"] = "成年時在臉上刺下家族的紋樣",
        ["CT_ANCESTRAL_TOMBS"] = "守護祖墳，每年祭掃先人"
    };

    private static IReadOnlyDictionary<string, string> LoadDescriptions()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var sheet in new[] { "04_文化特性定義", "06A_宗教核心信條定義", "06B_婚姻家庭教義定義", "06C_性別繼承教義定義", "06D_神職制度教義定義",
                     "06E_戰爭和平教義定義", "06F_財富戒律教義定義", "06G_死亡喪葬教義定義", "06H_改宗異端教義定義", "06I_魔法死靈教義定義", "06J_奴隸身分教義定義" })
        {
            var rows = ParameterSheet.ParseCsv(OfficialConfig.ReadSheetText(sheet + ".csv"));
            var header = rows[0].Select(h => h.Trim().TrimStart('﻿')).ToList();
            var note = header.IndexOf("說明");
            if (note < 0)
                continue;
            foreach (var row in rows.Skip(1).Where(r => r.Count > note && !string.IsNullOrWhiteSpace(r[0])))
                map.TryAdd(row[0].Trim(), row[note].Trim());
        }
        return map;
    }

    /// <summary>文化特性目前在模擬裡真的有的作用（沒有＝null，只是傳統的描述）。</summary>
    public static string? CultureEffect(string traitId) => traitId switch
    {
        "CT_SCHOLARLY" => "所有技術研究快 10%",
        "CT_MARTIAL" => "狩獵與求生的技術研究快 25%；創立信仰時偏向「戰爭」信條",
        "CT_SEA_FOLK" => "造船技術研究快 25%；海上漁撈多 10%",
        "CT_STEPPE_FOLK" => "狩獵、弓箭研究快 25%；草原採集多 10%",
        "CT_MERCHANT" => "以物易物、貨幣研究快 25%",
        "CT_FOREST_FOLK" => "森林採集多 10%；創立信仰時偏向「自然」信條",
        "CT_RIVER_FOLK" => "河流漁撈多 10%",
        "CT_TRADITIONAL" => "鄰近聚落的技術傳過來慢一半",
        "CT_OPEN" => "鄰近聚落的技術傳過來快一倍",
        DemoCultures.WandererTrait => "遷徙路上少吃兩成路糧；開拓隊走得更遠才定居",
        DemoCultures.RootedTrait => "不因擁擠分家，只有真的缺糧才外移；創立信仰時偏向「祖先」信條",
        "CT_STRONG_RULE" => "村長由最壯年的成年人擔任",
        "CT_ELDER_COUNCIL" => "村長由最年長的成年人擔任",
        _ => null
    };

    /// <summary>信條、教義目前在模擬裡真的有的作用（沒有＝null）。</summary>
    public static string? FaithEffect(string entryId) => entryId switch
    {
        "DOC_SPIRIT_MEDIUM" => "信仰與文化的技術研究快 25%",
        "RDX_MISSIONARY" => "向鄰近聚落傳教快一倍",
        "RDX_ISOLATIONIST" => "向鄰近聚落傳教慢一半",
        _ => null
    };

    private static int Year(int week) => DemoSettlementHistory.YearOfDay(checked(week * 7));

    private static string Percent(long part, long whole) => whole <= 0 ? "0%" : $"{(part * 100 + whole / 2) / whole}%";

    /// <summary>每個有人聚落裡這個文化（或信仰）的人數。</summary>
    private static List<(DemoSettlement Place, long People, int Permille)> Members(DemoObserverWorld world, Func<int, IReadOnlyList<DemoCultureShare>> sharesOf, int id)
        => world.Sites.Settlements.Where(s => !s.IsRuin)
            .Select(s => (Place: s, Population: world.SettlementPopulation(s.Id), Permille: sharesOf(s.Id).FirstOrDefault(x => x.CultureId == id)?.Permille ?? 0))
            .Where(x => x.Permille > 0 && x.Population > 0)
            .Select(x => (x.Place, People: x.Population * x.Permille / 1000, x.Permille))
            .OrderByDescending(x => x.People).ThenBy(x => x.Place.Id).ToList();

    private static string Name(DemoObserverWorld world, int settlementId)
        => world.Sites.Settlements.FirstOrDefault(s => s.Id == settlementId)?.Name ?? "某聚落";

    private static List<string> Spread(DemoObserverWorld world, List<(DemoSettlement Place, long People, int Permille)> members, Func<int, int?> mainOf, int id,
        string noun)
    {
        var lines = new List<string>();
        if (members.Count == 0)
        {
            lines.Add($"目前沒有任何{noun}");
            return lines;
        }
        var total = members.Sum(m => m.People);
        var everyone = world.Sites.Settlements.Where(s => !s.IsRuin).Sum(s => world.SettlementPopulation(s.Id));
        var main = members.Count(m => mainOf(m.Place.Id) == id);
        lines.Add($"{noun}約 {total} 人，佔世界人口 {Percent(total, everyone)}");
        lines.Add($"分布在 {members.Count} 個聚落，其中 {main} 個以它為主");
        lines.Add("");
        lines.Add("人數最多的聚落：");
        foreach (var m in members.Take(MaxPlaces))
            lines.Add($"・{m.Place.Name}　{m.People} 人（佔該地 {(m.Permille + 5) / 10}%）" + (mainOf(m.Place.Id) == id ? "" : "，非主要"));
        if (members.Count > MaxPlaces)
            lines.Add($"……另有 {members.Count - MaxPlaces} 個聚落");
        return lines;
    }

    /// <summary>文化頁：核心（出身與傳統）｜分布｜歷史。不存在的文化回傳 null。</summary>
    public static DemoPlayerDetail? Culture(DemoObserverWorld world, int cultureId)
    {
        ArgumentNullException.ThrowIfNull(world);
        var state = world.Culture;
        if (state.Culture(cultureId) is not { } culture)
            return null;

        var core = new List<string>
        {
            culture.Name,
            culture.ParentId > 0 && state.Culture(culture.ParentId) is { } parent
                ? $"第 {Year(culture.FoundedWeek)} 年自{parent.Name}分化而來"
                : "世界開始時就有的古老文化",
            "",
            "傳統："
        };
        foreach (var trait in culture.Traits)
        {
            var note = Description(trait);
            core.Add($"・{DemoCultures.TraitName(trait)}" + (note.Length > 0 ? $"：{note}" : ""));
            if (CultureEffect(trait) is { } effect)
                core.Add($"　（在這個世界裡：{effect}）");
        }
        // 這個文化的人多半信什麼。
        var members = Members(world, state.SharesOf, cultureId);
        var faiths = members.Where(m => state.MainCultureOf(m.Place.Id) == cultureId)
            .Select(m => state.Faith.MainFaithOf(m.Place.Id)).Where(f => f is not null).GroupBy(f => f!.Value)
            .OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => state.Faith.Faith(g.Key)?.Name).Where(n => n is not null).Take(3).ToList();
        core.Add("");
        core.Add(faiths.Count > 0 ? "多半信仰：" + string.Join("、", faiths) : "多半沒有信仰");

        var spread = Spread(world, members, state.MainCultureOf, cultureId, "這個文化的人");

        var history = world.SettlementEvents().Where(e => e.Kind == DemoSettlementEventKind.CultureSplit && e.Count == cultureId)
            .Select(e => $"第 {Year(e.Week)} 年　{e.SettlementName}：{DemoPlayerDetail.Clean(e.Cause)}").ToList();
        if (history.Count == 0 && culture.ParentId > 0) // 分化事件已壓縮進較早的紀錄時
            history.Add($"第 {Year(culture.FoundedWeek)} 年　自{state.Culture(culture.ParentId)?.Name ?? "母文化"}分化");
        foreach (var child in state.Cultures.Where(c => c.ParentId == cultureId).OrderBy(c => c.FoundedWeek).ThenBy(c => c.Id))
            history.Add($"第 {Year(child.FoundedWeek)} 年　分化出{child.Name}");
        if (history.Count == 0)
            history.Add("沒有分化的紀錄");

        return new DemoPlayerDetail($"文化：{culture.Name}", new[]
        {
            new DemoDetailTab("傳統", core) { Emblem = DemoHeraldry.EmblemOf(culture) },
            new DemoDetailTab("分布", spread),
            new DemoDetailTab("歷史", history)
        }, null);
    }

    /// <summary>信仰頁：核心（信條與教義）｜信徒｜歷史。不存在的信仰回傳 null。</summary>
    public static DemoPlayerDetail? Faith(DemoObserverWorld world, int faithId)
    {
        ArgumentNullException.ThrowIfNull(world);
        var state = world.Culture.Faith;
        if (state.Faith(faithId) is not { } faith)
            return null;

        var founder = world.RepresentativesOf(faith.FounderSettlementId).FirstOrDefault(r => r.Id == faith.FounderId);
        var core = new List<string>
        {
            faith.Name,
            $"第 {Year(faith.FoundedWeek)} 年由{Name(world, faith.FounderSettlementId)}" + (founder is null ? "的村長" : $"的{DemoRepresentative.RoleName(founder.Role)}{founder.Name}") + "創立",
            $"聖地：{Name(world, faith.FounderSettlementId)}",
            faith.ExtinctWeek > 0 ? $"第 {Year(faith.ExtinctWeek)} 年起已無信徒（失傳）" : "仍有人信奉",
            "",
            "核心信條："
        };
        void Entry(string id)
        {
            var note = Description(id);
            core.Add($"・{DemoFaiths.EntryName(id)}" + (note.Length > 0 ? $"：{note}" : ""));
            if (FaithEffect(id) is { } effect)
                core.Add($"　（在這個世界裡：{effect}）");
        }
        foreach (var tenet in faith.Tenets)
            Entry(tenet);
        core.Add("");
        core.Add("教義與戒律：");
        foreach (var doctrine in faith.Doctrines)
            Entry(doctrine);

        var members = Members(world, state.SharesOf, faithId);
        var believers = Spread(world, members, state.MainFaithOf, faithId, "信徒");
        var leaders = world.Sites.Settlements.Where(s => !s.IsRuin).SelectMany(s => world.RepresentativesOf(s.Id))
            .Count(r => state.FaithOfRepresentative(r.Id) == faithId);
        if (members.Count > 0)
            believers.Insert(2, $"代表人物中有 {leaders} 人信奉");

        var history = new List<string>();
        foreach (var e in world.SettlementEvents().Where(e => e.Kind is DemoSettlementEventKind.FaithFounded or DemoSettlementEventKind.FaithExtinct && e.Count == faithId))
            history.Add($"第 {Year(e.Week)} 年　" + (e.Kind == DemoSettlementEventKind.FaithFounded ? $"{e.SettlementName}：" : "") + DemoPlayerDetail.Clean(e.Cause));
        if (history.Count == 0)
            history.Add($"第 {Year(faith.FoundedWeek)} 年　在{Name(world, faith.FounderSettlementId)}創立");

        return new DemoPlayerDetail($"信仰：{faith.Name}", new[]
        {
            new DemoDetailTab("信條", core) { FaithSymbol = DemoHeraldry.FaithSymbolOf(faith), FaithId = faith.Id },
            new DemoDetailTab("信徒", believers),
            new DemoDetailTab("歷史", history)
        }, null);
    }
}
