using System.Collections.Immutable;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-6 章：世界的時代（使用者 2026-10-23）。全世界共用，以第一個達成條件的聚落為準。</summary>
public enum DemoEra
{
    /// <summary>希望時代：不餓死、只有老死；採集為生。</summary>
    Hope = 0,

    /// <summary>開拓時代：任一聚落習得知識樹上任一技術時開始。</summary>
    Pioneer = 1
}

/// <summary>
/// 某聚落對某技術的研究進度；<see cref="LearnedWeek"/>＞0＝已習得（第幾週）。
/// 第 08-9 章 A2：<see cref="Boosted"/>＝已得到實踐加速（每聚落每技術一次）。
/// </summary>
public sealed record DemoKnowledgeRecord(int SettlementId, DemoTechnology Technology, long Progress, int LearnedWeek, bool Boosted = false)
{
    public bool Learned => LearnedWeek > 0;
}

/// <summary>
/// 第 08-6 章 a 節：知識樹的世界狀態——各聚落各技術的研究進度與習得週、目前時代與開始週。不可變。
/// 第 08-7a 章 A1：開局知識（第 0 階）每個聚落都已知，不另存紀錄。
/// </summary>
public sealed record DemoKnowledgeState(ImmutableList<DemoKnowledgeRecord> Records, DemoEra Era, int EraWeek)
{
    public static DemoKnowledgeState Initial { get; } = new(ImmutableList<DemoKnowledgeRecord>.Empty, DemoEra.Hope, 0);

    // 查詢索引：依 Records 的參照建立（with 換掉 Records 時自動重建）；整個物件一次替換，多執行緒讀取也不會讀到一半。
    private sealed record IndexCache(ImmutableList<DemoKnowledgeRecord> Source, Dictionary<(int, int), DemoKnowledgeRecord> Map);

    private IndexCache? _index;

    private Dictionary<(int, int), DemoKnowledgeRecord> Index
    {
        get
        {
            var cached = _index;
            if (cached is not null && ReferenceEquals(cached.Source, Records))
                return cached.Map;
            var map = new Dictionary<(int, int), DemoKnowledgeRecord>();
            foreach (var r in Records)
                map[(r.SettlementId, r.Technology.Index)] = r;
            _index = new IndexCache(Records, map);
            return map;
        }
    }

    public DemoKnowledgeRecord? Of(int settlementId, DemoTechnology technology)
        => Index.GetValueOrDefault((settlementId, technology.Index));

    public bool Knows(int settlementId, DemoTechnology technology)
        => technology.Node.IsOpening || Of(settlementId, technology)?.Learned == true;

    /// <summary>研究習得的技術（不含開局知識）。</summary>
    /// <summary>
    /// 第 08-13 章（試玩回饋三之 3）：聚落最擅長的領域——開局知識以外、習得最多項的領域（同數取最近習得者，再同取代號小）；沒有＝null。
    /// 知識地圖依它著色，看得出各地發展方向不同，而不只是技術數。
    /// </summary>
    public string? LeadingDomain(int settlementId)
        => Records.Where(r => r.SettlementId == settlementId && r.Learned && !DemoKnowledge.IsOpening(r.Technology))
            .GroupBy(r => r.Technology.Node.Domain)
            .OrderByDescending(g => g.Count()).ThenByDescending(g => g.Max(r => r.LearnedWeek)).ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key).FirstOrDefault();

    public IReadOnlyList<DemoTechnology> KnownBy(int settlementId)
        => Records.Where(r => r.SettlementId == settlementId && r.Learned).Select(r => r.Technology).ToList();

    public bool Equals(DemoKnowledgeState? other)
        => other is not null && Era == other.Era && EraWeek == other.EraWeek && Records.SequenceEqual(other.Records);

    public override int GetHashCode() => HashCode.Combine(Era, EraWeek, Records.Count);

    public string Fingerprint()
        => $"era={Era}@{EraWeek};" + string.Concat(Records.Select(r => $"know{r.SettlementId}/{r.Technology}={r.Progress}@{r.LearnedWeek}{(r.Boosted ? "+" : "")};"));
}

/// <summary>
/// 第 08-6 章 a 節、第 08-7a 章 A1【TEMP 規則】知識的累積與傳播（自動累積、不選題、不抽亂數）。
/// <list type="bullet">
/// <item>節點讀正式設定 10B～10N 與 10R（<see cref="DemoTechCatalog"/>）；門檻＝ResearchTimeBase × <see cref="TempResearchScale"/>。</item>
/// <item>四道閘門全部成立才累積：前置已習得、聚落等級夠（<see cref="TierAllows"/>）、地區有從業者（<see cref="Practitioners"/>）、
/// 應用場所（第 3 階以上且列了建築者；建築系統出現前第 1～2 階不要求）。</item>
/// <item>每週進度＋＝從業者人數×1＋鄰近傳播＋遷入者帶來的經驗。</item>
/// <item>第一個習得開局知識以外技術的聚落讓世界進入開拓時代。</item>
/// </list>
/// </summary>
public static class DemoKnowledge
{
    /// <summary>
    /// 【TEMP】第 08-10a 章 V4：門檻＝正式設定 ResearchTimeBase × 本倍率（08-7a 原訂 325）。
    /// 使用者 2026-10-28 裁決希望時代是序章（10～20 年）；V0 量到 325 時第一項技術要 36～53 年，改 110。
    /// </summary>
    public const long TempResearchScale = 110;

    /// <summary>
    /// 【TEMP】第 08-10a 章 V4（08-7a 鄰近傳播，參考 CK3 革新傳播）：範圍內每有一個已習得的聚落，每週＋門檻的萬分之 5（0.05%），
    /// 最多算 <see cref="TempDiffusionMaxNeighbours"/> 個（0.25%）。
    /// </summary>
    public const long TempDiffusionPerNeighbourBasisPoints = 5;

    /// <summary>【TEMP】鄰近傳播最多計入的已習得聚落數。</summary>
    public const int TempDiffusionMaxNeighbours = 5;

    /// <summary>【TEMP】鄰近傳播的範圍（六角格距離；與開拓選址的 6 格相同，勢力範圍出現前的暫代）。以物易物的「有往來」也用它。</summary>
    public const int TempDiffusionRange = 6;

    /// <summary>【TEMP】每個從已習得聚落抵達的人帶來的進度（門檻的百分比）。</summary>
    public const long TempCarryPercent = 25;

    /// <summary>08-7b 第 1 節（使用者 2026-10-23 裁決）：開局五項知識裡不在 10R 第 0 階的那一項（10G 第 1 階）。</summary>
    public const string OpeningCookingId = "TECH_BASIC_COOKING";

    /// <summary>
    /// 【TEMP】08-7b 工具階梯（世紀帝國同產業升級）：石器改良＋10% → 基礎鍛造再＋15% → 熱處理再＋10%。
    /// </summary>
    public static readonly ImmutableArray<(string TechId, long Percent)> TempToolLadder = ImmutableArray.Create(
        ("TECH_IMPROVED_STONE_TOOLS", 10L), ("TECH_BASIC_FORGING", 15L), ("TECH_HEAT_TREATMENT", 10L));

    /// <summary>【TEMP】08-7a：每位相關專家（代表人物）每週加的研究進度。</summary>
    public const long TempExpertResearch = 5;

    /// <summary>【TEMP】08-7a：每位學者每週加的研究進度（所有領域）。</summary>
    public const long TempScholarResearch = 10;

    /// <summary>
    /// 【TEMP】08-7a 專家加速：哪一種專家加速哪些領域（十字軍之王文化領袖的學識加速）。村長加速社會制度。
    /// </summary>
    public static IReadOnlyList<string> ExpertDomains(DemoRepresentativeRole role) => role switch
    {
        DemoRepresentativeRole.Leader => new[] { "SOCIETY" },
        DemoRepresentativeRole.Farmer => new[] { "10B", "10G" },
        DemoRepresentativeRole.Carpenter => new[] { "10D", "10E", "10N" },
        DemoRepresentativeRole.Smith => new[] { "10C", "10M" },
        DemoRepresentativeRole.Hunter => new[] { "HUNT", "10F" },
        DemoRepresentativeRole.Shaman => new[] { "FAITH", "10H" },
        _ => Array.Empty<string>()
    };

    /// <summary>某聚落本週對某技術的專家與學者加速（每週研究點）。</summary>
    public static long ExpertResearch(DemoTechNode node, IEnumerable<DemoRepresentativeRole> roles)
        => roles.Sum(role => role == DemoRepresentativeRole.Scholar ? TempScholarResearch
            : ExpertDomains(role).Contains(node.Domain) ? TempExpertResearch : 0);

    /// <summary>
    /// 【TEMP】08-7a 實踐加速（文明帝國的靈光一現）：PracticeBoostCondition 在聚落真實發生時（目前世界裡做得到的幾項），
    /// 進度一次＋門檻的 PracticeBoostEffect。
    /// </summary>
    public sealed record Practice(bool HasFarmland, bool FoundedAVillage, bool HadNaturalDeath, bool HadVisitors, bool CarriesWater = false);

    public static bool PracticeHappened(DemoTechNode node, Practice p) => node.Id switch
    {
        "TECH_IRRIGATION" or "TECH_CROP_ROTATION" => p.HasFarmland,             // 已有自己開墾的農田
        "TECH_BASIC_CONSTRUCTION" or "TECH_ROAD_ENGINEERING" => p.FoundedAVillage, // 曾派開拓隊建村
        "TECH_ANCESTOR_RITES" => p.HadNaturalDeath,                             // 為逝者舉行祭祀
        "TECH_BARTER" => p.HadVisitors,                                          // 與其他聚落往來
        "TECH_WELL_DIGGING" => p.CarriesWater,                                   // 第 08-11 章：不鄰水、要挑水
        _ => false
    };

    /// <summary>
    /// 【TEMP】第 08-8 章 B2 文化效果（研究）：主要文化的特性讓從業者與專家的研究點增加（%）。
    /// 重學→全部＋10%；尚武→狩獵＋25%；海之民→造船＋25%；草原子民→狩獵、弓箭＋25%；重商→以物易物、貨幣＋25%。
    /// </summary>
    public static long CultureResearchPercent(DemoTechNode node, IReadOnlyCollection<string> traits)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(traits);
        long p = 0;
        if (traits.Contains("CT_SCHOLARLY")) p += 10;
        if (traits.Contains("CT_MARTIAL") && node.Domain == "HUNT") p += 25;
        if (traits.Contains("CT_SEA_FOLK") && node.Domain == "10E" && node.Branch == "造船") p += 25;
        if (traits.Contains("CT_STEPPE_FOLK") && node.Id is "TECH_HUNTING" or "TECH_ARCHERY") p += 25;
        if (traits.Contains("CT_MERCHANT") && node.Id is "TECH_BARTER" or "TECH_CURRENCY") p += 25;
        if (traits.Contains("DOC_SPIRIT_MEDIUM") && node.Domain == "FAITH") p += 25; // 第 08-8 章 B3：通靈者（信條與文化特性一起傳入）
        return p;
    }

    /// <summary>【TEMP】第 08-8 章 B2：鄰近傳播倍率（%）：守舊減半、開放加倍。</summary>
    public static long CultureDiffusionPercent(IReadOnlyCollection<string> traits)
        => traits.Contains("CT_TRADITIONAL") ? 50 : traits.Contains("CT_OPEN") ? 200 : 100;

    /// <summary>兩個聚落的六角格距離（軸向座標）。</summary>
    public static int Distance(DemoHexWorld map, DemoSettlement a, DemoSettlement b)
    {
        ArgumentNullException.ThrowIfNull(map);
        var x = map.GetCell(a.Column, a.Row);
        var y = map.GetCell(b.Column, b.Row);
        var dq = x.AxialQ - y.AxialQ;
        var dr = x.AxialR - y.AxialR;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    public static long ThresholdOf(DemoTechnology technology) => checked(technology.Node.ResearchTimeBase * TempResearchScale);

    /// <summary>開局知識：10R 第 0 階＋基礎烹調。</summary>
    public static bool IsOpening(DemoTechnology technology) => technology.Node.IsOpening || technology.Id == OpeningCookingId;

    /// <summary>第 08-6 章 e 節、08-7b 工具階梯：<paramref name="week"/> 結算時的工具加成（該週以前已習得的各級合計）。</summary>
    public static long ToolYieldPercent(DemoKnowledgeState state, int settlementId, int week)
        => TempToolLadder.Where(t => state.Of(settlementId, DemoTechnology.Of(t.TechId)) is { Learned: true } r && r.LearnedWeek < week)
            .Sum(t => t.Percent);

    public static string Name(DemoTechnology technology) => technology.Node.Name;

    /// <summary>
    /// 【TEMP】第 08-12 章技術效果：來源每週供應加成。農田：灌溉＋50%、輪作＋25%、土壤管理＋25%；
    /// 河流與開放水域：結網漁撈＋20%。取相鄰（來源格或六鄰格）聚落在該週以前已習得者的最大加成。
    /// </summary>
    public static readonly ImmutableArray<(DemoCatchmentSourceKind Kind, string TechId, long Percent)> TempSourceBonuses = ImmutableArray.Create(
        (DemoCatchmentSourceKind.Farmland, "TECH_IRRIGATION", 50L),
        (DemoCatchmentSourceKind.Farmland, "TECH_CROP_ROTATION", 25L),
        (DemoCatchmentSourceKind.Farmland, "TECH_SOIL_MANAGEMENT", 25L),
        (DemoCatchmentSourceKind.MainRiver, "TECH_NET_FISHING", 20L),
        (DemoCatchmentSourceKind.OpenWater, "TECH_NET_FISHING", 20L));

    /// <summary>【TEMP】第 08-8 章 B2：生計文化的來源加成（%）：林地子民森林、河岸子民河流、海之民開闊水域、草原子民草原野採各＋10%。</summary>
    public static readonly ImmutableArray<(DemoCatchmentSourceKind Kind, string TraitId, long Percent)> TempCultureSourceBonuses = ImmutableArray.Create(
        (DemoCatchmentSourceKind.Forest, "CT_FOREST_FOLK", 10L),
        (DemoCatchmentSourceKind.MainRiver, "CT_RIVER_FOLK", 10L),
        (DemoCatchmentSourceKind.OpenWater, "CT_SEA_FOLK", 10L),
        (DemoCatchmentSourceKind.GrasslandForage, "CT_STEPPE_FOLK", 10L));

    /// <summary>
    /// 第 08-12 章：<paramref name="week"/> 結算時某來源的技術加成（%）；第 08-8 章 B2 另加相鄰聚落主要文化的生計加成
    /// （<paramref name="traitsAt"/>＝聚落在該週結算時的主要文化特性），取相鄰聚落中最高者。
    /// </summary>
    public static long SourcePercent(DemoKnowledgeState state, DemoWorldSites sites, DemoHexWorld map, DemoCatchmentFoodSource source, int week,
        Func<int, int, IReadOnlyCollection<string>>? traitsAt = null, Func<int, int, bool>? huntingAt = null,
        IReadOnlyList<DemoEnvironmentSpell>? spells = null)
    {
        // 第 12-1 章 C3b：熱浪、野火的減產另加在技術與文化加成之後（不受「取相鄰聚落最高者」影響）。
        var environment = spells is null ? 0 : DemoEnvironment.SourcePercent(spells, source, week);
        return checked(Boosts(state, sites, map, source, week, traitsAt, huntingAt) + environment);
    }

    private static long Boosts(DemoKnowledgeState state, DemoWorldSites sites, DemoHexWorld map, DemoCatchmentFoodSource source, int week,
        Func<int, int, IReadOnlyCollection<string>>? traitsAt, Func<int, int, bool>? huntingAt)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(source);
        var bonuses = TempSourceBonuses.Where(b => b.Kind == source.Kind).ToList();
        var cultural = traitsAt is null ? new List<(DemoCatchmentSourceKind, string TraitId, long Percent)>()
            : TempCultureSourceBonuses.Where(b => b.Kind == source.Kind).ToList();
        var hunted = huntingAt is not null && source.Kind is DemoCatchmentSourceKind.Forest or DemoCatchmentSourceKind.GrasslandForage;
        if (bonuses.Count == 0 && cultural.Count == 0 && !hunted)
            return 0;
        var origin = map.GetCell(source.Column, source.Row);
        var nearby = DemoRouteNetwork.Directions.Select(d => map.AtAxial(origin.AxialQ + d.Dq, origin.AxialR + d.Dr)).Prepend(origin)
            .Where(c => c is not null).Select(c => sites.SettlementAt(c!.Column, c.Row)).Where(s => s is not null);
        long best = 0;
        foreach (var settlement in nearby)
        {
            var total = bonuses.Where(b => state.Of(settlement!.Id, DemoTechnology.Of(b.TechId)) is { Learned: true } r && r.LearnedWeek < week)
                .Sum(b => b.Percent);
            if (cultural.Count > 0)
            {
                var traits = traitsAt!(settlement!.Id, week);
                total += cultural.Where(b => traits.Contains(b.TraitId)).Sum(b => b.Percent);
            }
            if (hunted && huntingAt!(settlement!.Id, week))
                total += DemoWildlife.TempHuntingSourcePercent; // 第 12-1 章 C1：獵物豐富時的獸肉
            best = Math.Max(best, total);
        }
        return best;
    }

    /// <summary>第 08-11 章：掘井（10D）。</summary>
    public const string WellDiggingId = "TECH_WELL_DIGGING";

    /// <summary>
    /// 第 08-11 章：<paramref name="week"/> 結算時聚落會不會掘井——本聚落在該週以前習得，或它是開拓建立、
    /// 建立前來源聚落已會（開拓隊帶著做法）。
    /// </summary>
    public static bool Wells(DemoKnowledgeState state, IEnumerable<DemoSettlement> settlements, int settlementId, int week)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(settlements);
        bool Before(int id, int w) => state.Of(id, DemoTechnology.Of(WellDiggingId)) is { Learned: true } r && r.LearnedWeek < w;
        if (Before(settlementId, week))
            return true;
        var site = settlements.FirstOrDefault(s => s.Id == settlementId);
        return site is { IsFounded: true } && Before(site.FoundedFromSettlementId, site.FoundedWeek);
    }

    public static string EraName(DemoEra era) => era switch
    {
        DemoEra.Hope => "希望時代",
        DemoEra.Pioneer => "開拓時代",
        _ => era.ToString()
    };

    /// <summary>
    /// 【TEMP】08-7a 等級閘門（第 08-11 章五級）：第 1 階任何聚落；第 2 階村莊；第 3 階城鎮；第 4 階城市；第 5 階首都。
    /// </summary>
    public static bool TierAllows(DemoTechNode node, DemoSettlementTier tier) => node.Tier switch
    {
        <= 1 => true,
        2 => tier >= DemoSettlementTier.Village,
        3 => tier >= DemoSettlementTier.Town,
        4 => tier >= DemoSettlementTier.City,
        _ => tier >= DemoSettlementTier.Capital
    };

    /// <summary>
    /// 08-7a 場所閘門：列了應用場所時要有該建築。建築系統出現前（第 08-11 章）：場所以等級設施代替，等級閘門成立即視為有。
    /// </summary>
    public static bool FacilityAllows(DemoTechNode node) => true;

    /// <summary>某聚落本週的研究條件（地理、人口、等級；全部讀上週已提交的狀態）。</summary>
    public sealed record Context(
        DemoSettlementTier Tier,
        long Adults,
        long Elders,
        bool HasLeader,
        long Specialists,
        bool NearForest,
        bool NearGrassland,
        bool NearWater,
        bool NearSea,
        bool HasFarmland,
        bool HasNeighbour,
        long Shamans = 0,
        bool NearOre = false,
        bool NearHills = false);

    /// <summary>【TEMP】第 06-3 章：礦脈在聚落幾格內算「勢力範圍內有礦脈」（採礦冶金、珠寶精工的從業條件）。</summary>
    public const int TempOreRange = 2;

    /// <summary>
    /// 【TEMP】08-7b 第 2、3 節：每項技術的從業者人數（地理條件不成立＝0，不累積）。
    /// 礦脈、牧群、石材、守衛、魔法尚未出現的領域一律 0。
    /// </summary>
    public static long Practitioners(DemoTechNode node, Context c, Func<string, bool> knows)
    {
        long If(bool condition, long n) => condition ? n : 0;
        return node.Domain switch
        {
            "10B" => If(node.Branch == "作物", c.Adults),                      // 畜牧要野獸或牧群（尚無）
            "10C" => If(c.NearOre, c.Adults),                                   // 第 06-3 章：勢力範圍內有礦脈
            "10D" => If(node.Branch != "石造" || c.NearHills, c.Adults),       // 第 06-3 章：石造要可及丘陵（石材）
            "10E" => node.Branch == "造船" ? If(c.NearForest && c.NearWater, c.Adults) : If(c.NearForest, c.Adults),
            "10F" => node.Branch is "紡織" or "服裝" ? If(c.NearGrassland, c.Adults) : If(knows("TECH_HUNTING"), c.Adults),
            "10G" => node.Id switch
            {
                "TECH_MILLING" => If(c.HasFarmland, c.Adults),
                "TECH_SALTING" => If(c.NearSea, c.Adults),
                _ => c.Adults
            },
            "10H" => node.Branch is "藥學" or "疾病" ? If(c.NearForest || c.NearGrassland, c.Adults) : c.Adults,
            "10I" => 0,                                                         // 要有守衛（尚無）
            "10J" => If(c.Tier >= DemoSettlementTier.Village && c.HasLeader, c.Adults), // 學者另見 A2
            "10K" => 0,                                                         // 魔法法則章節開工前不出現
            "10L" => If(c.NearWater && knows("TECH_FIRE"), c.Adults),
            "10M" => If(c.NearOre, c.Adults),                                   // 第 06-3 章：要有礦脈（寶石尚無）
            "10N" => If(knows("TECH_BASIC_CARPENTRY"), c.Adults),
            "HUNT" => node.Id switch
            {
                "TECH_IMPROVED_STONE_TOOLS" => c.Adults,
                "TECH_HUNTING" or "TECH_ARCHERY" => If(c.NearForest || c.NearGrassland, c.Adults),
                "TECH_NET_FISHING" => If(c.NearWater, c.Adults),
                _ => 0                                                          // 巡守與圍獵：獵人（A2）
            },
            "SOCIETY" => node.Id switch
            {
                "TECH_ORAL_LAW" => c.Elders + (c.HasLeader ? 1 : 0),
                "TECH_BARTER" or "TECH_CURRENCY" => If(c.HasNeighbour, c.Adults),
                "TECH_CODIFIED_LAW" => c.HasLeader ? 1 : 0,
                // 行會：另需任一工藝第 2 階（10P 跨領域先修）；從業者＝各工藝專家。
                "TECH_CRAFT_GUILDS" => If(DemoTechCatalog.All.Any(n => n.Tier == 2 && n.Domain.StartsWith("10", StringComparison.Ordinal) && knows(n.Id)),
                    c.Specialists),
                _ => 0
            },
            "FAITH" => node.Id switch
            {
                "TECH_ANCESTOR_RITES" or "TECH_SHAMANISM" => c.Elders,
                "TECH_ORAL_TRADITION" or "TECH_FESTIVAL_CALENDAR" => c.Adults,
                "TECH_ORGANIZED_RELIGION" => c.Shamans,                         // 第 08-11 章：城鎮的神殿；從業者＝巫祝
                _ => 0
            },
            _ => 0
        };
    }

    /// <summary>
    /// 依本週週報（生死與遷徙之後）與群體人口更新研究進度；回傳新狀態與要追加的事件（習得技術、時代開始），
    /// 事件編號從 <paramref name="nextEventId"/> 起。只有人類繁衍世界有知識。
    /// <paramref name="tierOf"/>、<paramref name="land"/> 省略時視為聚落等級、沒有農田。
    /// </summary>
    public static (DemoKnowledgeState State, IReadOnlyList<DemoSettlementEvent> Events) Advance(
        DemoKnowledgeState state, DemoWeekReport report, DemoCohortDemography population, DemoWorldSites sites, long nextEventId,
        DemoHexWorld? map = null, Func<int, DemoSettlementTier>? tierOf = null, DemoLandDevelopmentMap? land = null,
        Func<int, IReadOnlyCollection<string>>? cultureTraits = null, IReadOnlySet<DemoCellRef>? extraOre = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(population);
        ArgumentNullException.ThrowIfNull(sites);
        var records = state.Records.ToDictionary(r => (r.SettlementId, r.Technology.Index));
        var events = new List<DemoSettlementEvent>();
        var era = state.Era;
        var eraWeek = state.EraWeek;
        var rules = DemoHumanLifecycle.Rules;
        var day = checked(population.LastWeek * 7);
        var living = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).ToList();
        var farmland = land?.FarmlandCells().ToHashSet() ?? new HashSet<DemoCellRef>();
        var catalog = DemoTechCatalog.All;
        var arrivals = report.Migrations.Where(m => m.Arrived).ToList();

        foreach (var settlement in living)
        {
            var neighbours = map is null ? new List<DemoSettlement>()
                : living.Where(o => o.Id != settlement.Id && Distance(map, o, settlement) <= TempDiffusionRange).ToList();
            var stats = population.StatsOf(settlement.Id, 0, day, rules);
            var near = map is null ? new HashSet<DemoTerrain>() : Around(map, settlement).Select(c => c.Terrain).ToHashSet();
            var context = new Context(
                tierOf?.Invoke(settlement.Id) ?? DemoSettlementTier.Camp,
                population.AdultsIn(settlement.Id, rules),
                stats.Elders,
                population.Representatives.Any(r => r.SettlementId == settlement.Id && r.Role == DemoRepresentativeRole.Leader),
                population.Representatives.Count(r => r.SettlementId == settlement.Id && DemoRepresentative.TechnologyOf(r.Role) is not null),
                near.Contains(DemoTerrain.Forest),
                near.Contains(DemoTerrain.Grassland),
                near.Contains(DemoTerrain.Water),
                map is not null && Around(map, settlement).Any(c => c.Sea),
                map is not null && Around(map, settlement).Any(c => farmland.Contains(new DemoCellRef(c.Column, c.Row))),
                neighbours.Count > 0,
                population.Representatives.Count(r => r.SettlementId == settlement.Id && r.Role == DemoRepresentativeRole.Shaman),
                map is not null && NearOre(map, settlement, extraOre),
                near.Contains(DemoTerrain.Hills));
            var roles = population.Representatives.Where(r => r.SettlementId == settlement.Id).Select(r => r.Role).ToList();
            var practice = new Practice(
                context.HasFarmland,
                sites.Settlements.Any(s => s.IsFounded && s.FoundedFromSettlementId == settlement.Id),
                report.Events.Any(e => e.SettlementId == settlement.Id && e.Kind == DemoSettlementEventKind.NaturalDeath),
                arrivals.Any(m => m.ToSettlementId == settlement.Id && m.FromSettlementId != settlement.Id && m.FromSettlementId > 0),
                map is not null && !DemoMigrationRoutes.HasWaterAccess(map, new DemoCellRef(settlement.Column, settlement.Row)));
            bool KnowsHere(string id) => state.Knows(settlement.Id, DemoTechnology.Of(id));
            var traits = cultureTraits?.Invoke(settlement.Id) ?? Array.Empty<string>(); // 第 08-8 章 B2

            foreach (var node in catalog)
            {
                var technology = new DemoTechnology(node.Index);
                if (IsOpening(technology))
                    continue;
                var key = (settlement.Id, node.Index);
                var current = records.GetValueOrDefault(key) ?? new DemoKnowledgeRecord(settlement.Id, technology, 0, 0);
                if (current.Learned)
                    continue;
                // 08-7a 四道閘門：前置、等級、場所、地區（從業者）。
                if (!node.Parents.All(KnowsHere) || !TierAllows(node, context.Tier) || !FacilityAllows(node))
                    continue;
                var practitioners = Practitioners(node, context, KnowsHere);
                if (practitioners == 0)
                    continue;
                var threshold = ThresholdOf(technology);
                var carried = arrivals.Where(m => m.ToSettlementId == settlement.Id && state.Knows(m.FromSettlementId, technology))
                    .Sum(m => m.PopulationClosing);
                // 第 08-10a 章 V4：鄰近傳播——上週已習得、範圍內的其他聚落。
                var known = neighbours.Count(o => state.Knows(o.Id, technology));
                var diffusion = checked(threshold * Math.Min(known, TempDiffusionMaxNeighbours) * TempDiffusionPerNeighbourBasisPoints / 10_000
                    * CultureDiffusionPercent(traits) / 100); // 第 08-8 章 B2：守舊減半、開放加倍
                // 第 08-9 章 A2：專家×5、學者×10；實踐加速一次＋門檻的 PracticeBoostEffect。
                var experts = ExpertResearch(node, roles);
                var boostNow = !current.Boosted && node.PracticeBoostPercent > 0 && PracticeHappened(node, practice);
                var boost = boostNow ? checked(threshold * node.PracticeBoostPercent / 100) : 0;
                var gain = checked((practitioners + experts) * (100 + CultureResearchPercent(node, traits)) / 100 // 第 08-8 章 B2
                    + diffusion + boost + carried * threshold * TempCarryPercent / 100);
                var progress = Math.Min(threshold, checked(current.Progress + gain));
                var learned = progress >= threshold;
                records[key] = current with { Progress = progress, LearnedWeek = learned ? report.Week : 0, Boosted = current.Boosted || boostNow };
                if (!learned)
                    continue;
                var cause = carried > 0
                    ? $"已習得的聚落有 {carried} 人遷入，帶來{node.Name}的經驗"
                    : known > 0
                    ? $"鄰近 {known} 個已習得{node.Name}的聚落傳來做法，加上 {practitioners} 人的經驗"
                    : $"{practitioners} 人長年累積的經驗";
                if (experts > 0)
                    cause += $"，{string.Join("、", roles.Where(r => ExpertResearch(node, new[] { r }) > 0).Select(DemoRepresentative.RoleName).Distinct())}帶領研究";
                if (current.Boosted || boostNow)
                    cause += $"；曾{node.PracticeCondition}，學得更快";
                var learnedEvent = new DemoSettlementEvent(report.Week, report.WorldTime, settlement.Id, settlement.Name,
                    DemoSettlementEventKind.KnowledgeLearned, node.Index, $"學會{node.Name}：{cause}【TEMP】",
                    EventId: nextEventId++);
                events.Add(learnedEvent);
                if (era == DemoEra.Hope)
                {
                    era = DemoEra.Pioneer;
                    eraWeek = report.Week;
                    events.Add(new DemoSettlementEvent(report.Week, report.WorldTime, settlement.Id, settlement.Name,
                        DemoSettlementEventKind.EraBegan, (long)DemoEra.Pioneer,
                        $"{settlement.Name}學會{node.Name}，世界進入{EraName(DemoEra.Pioneer)}",
                        EventId: nextEventId++, CauseEventId: learnedEvent.EventId));
                }
            }
        }

        var next = new DemoKnowledgeState(
            records.Values.OrderBy(r => r.SettlementId).ThenBy(r => r.Technology).ToImmutableList(), era, eraWeek);
        return (next, events);
    }

    /// <summary>聚落本格與六鄰格（地圖內）。</summary>
    /// <summary>第 06-3 章：聚落 <see cref="TempOreRange"/> 格內有礦脈。</summary>
    public static bool NearOre(DemoHexWorld map, DemoSettlement settlement, IReadOnlySet<DemoCellRef>? extraOre = null)
    {
        ArgumentNullException.ThrowIfNull(map);
        var origin = map.GetCell(settlement.Column, settlement.Row);
        return map.Cells.Any(c => (c.Ore || extraOre?.Contains(new DemoCellRef(c.Column, c.Row)) == true)
            && DemoWorldSites.HexDistance(origin, c) <= TempOreRange); // 第 12-1 章：含隕石留下的礦脈
    }

    private static IEnumerable<DemoHexCell> Around(DemoHexWorld map, DemoSettlement settlement)
    {
        var origin = map.GetCell(settlement.Column, settlement.Row);
        yield return origin;
        foreach (var d in DemoRouteNetwork.Directions)
            if (map.AtAxial(origin.AxialQ + d.Dq, origin.AxialR + d.Dr) is { } cell)
                yield return cell;
    }

    /// <summary>第 08-6 章 d 節：聚落本格或六鄰格有森林（木工的從業條件）。</summary>
    public static bool NearForest(DemoHexWorld map, DemoSettlement settlement)
        => Around(map, settlement).Any(c => c.Terrain == DemoTerrain.Forest);
}
