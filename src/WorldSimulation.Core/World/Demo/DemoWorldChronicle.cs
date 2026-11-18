namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-10b 章：編年史條目的重要度（預設只顯示重大）。</summary>
public enum DemoChronicleLevel
{
    Major = 0,
    Normal = 1
}

/// <summary>第 08-10b 章：編年史條目的類型（介面上每一類可個別開關，參考 FMS 歷史面板）。</summary>
public enum DemoChronicleCategory
{
    Era = 0,
    Knowledge = 1,
    Founding = 2,
    Ruin = 3,
    Tier = 4,
    Farmland = 5,
    Famine = 6,
    Migration = 7,
    Hunger = 8,
    Representative = 9,
    Population = 10,
    Culture = 11,
    Faith = 12,
    Wilds = 13,
    Disaster = 14,
    Realm = 15
}

/// <summary>條目文字的一段；<see cref="SettlementId"/>＞0 的段落是可點擊的聚落名稱（參考 CK3 名稱底線）。</summary>
public sealed record DemoChronicleSegment(string Text, int SettlementId = 0);

/// <summary>編年史的一筆（同一年內依週次排列）。</summary>
public sealed record DemoChronicleEntry(
    int Week,
    DemoChronicleLevel Level,
    DemoChronicleCategory Category,
    IReadOnlyList<DemoChronicleSegment> Segments,
    long EventId = 0,
    long ParentEventId = 0,
    int Weight = 0)
{
    /// <summary>
    /// 第 08-10d 章：世界大事（<see cref="Weight"/>＞0）——改變整個世界的事，編年史預設只列這一層；
    /// 其餘重大事件是「各地大事」，每年合計成一行。數字越大越重要（年代小結挑最重要的幾件）。
    /// </summary>
    public bool IsWorld => Weight > 0;


    /// <summary>第 08-16 章：同一年裡起因也在編年史上的條目（介面縮排在起因下面，合成一段）；沒有＝0。</summary>
    public bool IsFollowUp => ParentEventId > 0;

    public string Text => string.Concat(Segments.Select(s => s.Text));

    /// <summary>點這一筆時定位的聚落（第一個名稱）；沒有＝0。</summary>
    public int SettlementId => Segments.FirstOrDefault(s => s.SettlementId > 0)?.SettlementId ?? 0;
}

/// <summary>編年史的一年：當年條目、出生與老死合計（統計級，每年一行；可展開各聚落）、年底時代。</summary>
public sealed record DemoChronicleYear(
    int Year,
    DemoEra Era,
    bool EraStarted,
    IReadOnlyList<DemoChronicleEntry> Entries,
    long Births,
    long NaturalDeaths,
    IReadOnlyDictionary<int, (long Births, long NaturalDeaths)> BySettlement)
{
    /// <summary>每年一行的統計（全世界合計）。</summary>
    public string StatisticLine => $"本年出生 {Births} 人、老死 {NaturalDeaths} 人";
}

/// <summary>
/// 第 08-10b 章：世界編年史。只讀既有事件（含讀檔前的較早事件）、聚落小史與代表人物紀錄，
/// 不改世界；同一世界讀檔前後結果相同。
/// </summary>
public static class DemoWorldChronicle
{
    /// <summary>【TEMP】超過幾年的舊年份在介面上預設折疊成十年摘要。</summary>
    public const int TempFoldAfterYears = 10;

    /// <summary>【TEMP】第 08-10d 章：世界大事的輕重（時代＞征服、獨立、篡位＞開戰、內戰＞世界第一個某級聚落＞城鎮以上淪為遺跡＞信仰與文化＞世界首次的技術）。</summary>
    public const int WeightEra = 100, WeightConquest = 90, WeightWar = 80, WeightFirstTier = 70, WeightRuin = 60, WeightBelief = 50, WeightKnowledge = 40;

    /// <summary>【TEMP】第 08-10d 章：年代小結最多引述幾件世界大事；一段年代裡幾場戰事算「戰亂」、幾處餓死紀錄算「饑荒」、建立幾個聚落算「擴張」、幾項首創技術算「創新」。</summary>
    public const int TempStoryHighlights = 3, TempWarEra = 2, TempFamineEra = 3, TempGrowthEra = 3, TempInventionEra = 3;

    /// <summary>從第 1 年到目前年份，每年一筆（沒有條目的年份也列出，方便看出空白）。</summary>
    public static IReadOnlyList<DemoChronicleYear> Build(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        var names = world.Sites.Settlements.ToDictionary(s => s.Id, s => s.Name);
        var parents = world.Sites.Settlements.Where(s => s.IsFounded).ToDictionary(s => s.Id, s => s.FoundedFromSettlementId);
        DemoChronicleSegment Name(int id) => new(names.GetValueOrDefault(id, $"#{id}"), id);
        static DemoChronicleSegment T(string text) => new(text);
        static int YearOf(int week) => DemoSettlementHistory.YearOfDay(checked(week * 7));

        var entries = new List<(int Year, DemoChronicleEntry Entry)>();
        void Add(int week, DemoChronicleLevel level, DemoChronicleCategory category, long eventId, params DemoChronicleSegment[] segments)
            => entries.Add((YearOf(week), new DemoChronicleEntry(week, level, category, segments, eventId)));
        // 第 08-10d 章：世界大事（一定是重大事件；weight＝0 時就是一般的重大事件）。
        void AddWorld(int week, int weight, DemoChronicleCategory category, long eventId, params DemoChronicleSegment[] segments)
            => entries.Add((YearOf(week), new DemoChronicleEntry(week, DemoChronicleLevel.Major, category, segments, eventId, 0, weight)));
        // 第 08-16 章：沒有事件編號的年度合計（餓死、缺糧）記下它們的起因事件（例：熱浪），之後接到起因下面。
        var aggregateCause = new Dictionary<int, long>();

        var events = world.SettlementEvents();
        var byId = events.Where(e => e.EventId > 0).GroupBy(e => e.EventId).ToDictionary(g => g.Key, g => g.First()); // 第 08-16 章：因果故事
        var knownTechs = new HashSet<DemoTechnology>();
        var eraStarts = new Dictionary<int, DemoEra>();
        foreach (var e in events.OrderBy(e => e.Week).ThenBy(e => e.EventId))
        {
            switch (e.Kind)
            {
                case DemoSettlementEventKind.EraBegan:
                    eraStarts[YearOf(e.Week)] = (DemoEra)e.Count;
                    // 第 08-6c 章：「時代」一詞留給世界時代；文明的進度寫成里程碑。
                    AddWorld(e.Week, WeightEra, DemoChronicleCategory.Era, e.EventId,
                        Name(e.SettlementId), T((DemoEra)e.Count == DemoEra.Conquest ? " 成立了世上第一個王國" : " 習得世上第一項技術，文明踏出第一步"));
                    break;
                case DemoSettlementEventKind.KnowledgeLearned:
                    var tech = (DemoTechnology)e.Count;
                    var first = knownTechs.Add(tech);
                    if (first)
                        AddWorld(e.Week, WeightKnowledge, DemoChronicleCategory.Knowledge, e.EventId,
                            Name(e.SettlementId), T($" 學會{DemoKnowledge.Name(tech)}（世界首次）"));
                    else
                        Add(e.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Knowledge, e.EventId,
                            Name(e.SettlementId), T($" 學會{DemoKnowledge.Name(tech)}"));
                    break;
                case DemoSettlementEventKind.SettlementFounded:
                    var from = parents.GetValueOrDefault(e.SettlementId);
                    // 第 08-16 章：接上起因（例：熱浪讓家鄉鬧饑荒，一群人帶著存糧出走）。
                    var why = DemoStories.WhyFounded(e, byId) is { } reason ? new[] { T("——" + reason) } : Array.Empty<DemoChronicleSegment>();
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Founding, e.EventId,
                        (from > 0 ? new[] { Name(from), T(" 的開拓隊建立 "), Name(e.SettlementId), T($"（{e.Count} 人）") }
                                  : new[] { T("開拓隊建立 "), Name(e.SettlementId), T($"（{e.Count} 人）") }).Concat(why).ToArray());
                    break;
                case DemoSettlementEventKind.SettlementRuined:
                    // 第 08-10d 章：曾經長到城鎮規模的聚落淪為遺跡是世界大事；小聚落的興廢是各地大事。
                    AddWorld(e.Week, world.ChronicleOf(e.SettlementId).Peak >= DemoSettlementHistory.TempUpPopulation(DemoSettlementTier.Town) ? WeightRuin : 0,
                        DemoChronicleCategory.Ruin, e.EventId,
                        Name(e.SettlementId), T(DemoStories.WhyRuined(e, events) is { } after ? $" 在{after}人口歸零，成為遺跡" : " 人口歸零，成為遺跡"));
                    break;
                // 第 08-18i 章（GPT 試玩回饋 08：日常紀錄把重大事件夾住）：開墾農田改在下面每年每聚落合計成一條一般事件。
                case DemoSettlementEventKind.FarmlandAbandoned:
                    Add(e.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Farmland, e.EventId,
                        Name(e.SettlementId), T(" 附近的一塊農田無人耕作，開始荒廢"));
                    break;
                case DemoSettlementEventKind.FaithFounded:
                case DemoSettlementEventKind.FaithSchism: // 第 08-22d 章：異端分裂和創立信仰一樣是世界大事
                    AddWorld(e.Week, WeightBelief, DemoChronicleCategory.Faith, e.EventId,
                        Name(e.SettlementId), T(" " + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.FaithExtinct:
                    AddWorld(e.Week, WeightBelief, DemoChronicleCategory.Faith, e.EventId, T(DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.Earthquake:
                case DemoSettlementEventKind.Tornado:
                case DemoSettlementEventKind.Meteorite:
                case DemoSettlementEventKind.Flood:
                    // 第 12-1 章 C3：天災都是重大事件。
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Disaster, e.EventId,
                        Name(e.SettlementId), T($" 發生{DemoDisasters.Name(e.Kind)}：" + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.Heatwave:
                case DemoSettlementEventKind.Wildfire:
                    // 第 12-1 章 C3b：熱浪、野火開始是重大事件；結束是一般事件。
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Disaster, e.EventId,
                        Name(e.SettlementId), T(" 一帶" + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.HeatwaveEnded:
                case DemoSettlementEventKind.ForestRegrown:
                    Add(e.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Disaster, e.EventId,
                        Name(e.SettlementId), T(" 一帶" + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.MeteoriteOre:
                    Add(e.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Disaster, e.EventId,
                        Name(e.SettlementId), T(" 附近的" + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.MiningBegan:
                case DemoSettlementEventKind.OreExhausted:
                    // 第 08-16 章第 14 節：採礦的開始與挖空是一般事件（伐木只在地圖上看得到，不進編年史）。
                    Add(e.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Farmland, e.EventId,
                        Name(e.SettlementId), T(" " + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.DungeonAppeared:
                    // 第 12-1 章 C4：地下城出現是重大事件。
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Wilds, e.EventId,
                        Name(e.SettlementId), T(" " + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.MonsterSwarm:
                    // 第 12-2h 章：獸潮是重大事件（人類與怪物的領地爭奪，進史書）。
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Wilds, e.EventId,
                        Name(e.SettlementId), T(" " + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.MonsterRaid:
                case DemoSettlementEventKind.MonsterHunt:
                    // 第 12-2 章：怪物襲擊、討伐是一般事件；地下城沉寂、怪物再生是重大事件（擊殺數已寫在襲擊與討伐裡，不另記）。
                    Add(e.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Wilds, e.EventId,
                        Name(e.SettlementId), T(" " + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.DungeonCleared:
                case DemoSettlementEventKind.DungeonAwakened:
                    Add(e.Week, DemoChronicleLevel.Major, DemoChronicleCategory.Wilds, e.EventId,
                        Name(e.SettlementId), T(" 附近，" + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.SecessionArmed:
                case DemoSettlementEventKind.SecessionPeaceful:
                case DemoSettlementEventKind.Independence:
                case DemoSettlementEventKind.WarDeclared: // 第 08-19e 章：宣戰、征服是重大事件；交戰、敗退與白和平是一般事件
                case DemoSettlementEventKind.Conquered:
                case DemoSettlementEventKind.Succession: // 第 08-19f 章：繼位、內戰、篡位是重大事件
                case DemoSettlementEventKind.CivilWar:
                case DemoSettlementEventKind.Usurped:
                    // 第 08-19b 章：獨立戰爭、請願的開始與獨立是重大事件；歸附、加入派系、交戰、失敗是一般事件（退出派系不記）。
                    // 第 08-10d 章：征服、獨立、篡位，以及開戰、內戰、武裝獨立是世界大事；繼位與和平請願是各地（勢力內）大事。
                    AddWorld(e.Week, e.Kind switch
                        {
                            DemoSettlementEventKind.Conquered or DemoSettlementEventKind.Independence or DemoSettlementEventKind.Usurped => WeightConquest,
                            DemoSettlementEventKind.WarDeclared or DemoSettlementEventKind.CivilWar or DemoSettlementEventKind.SecessionArmed => WeightWar,
                            _ => 0
                        }, DemoChronicleCategory.Realm, e.EventId,
                        Name(e.SettlementId), T(" " + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.VassalJoined:
                case DemoSettlementEventKind.SecessionBattle:
                case DemoSettlementEventKind.SecessionFailed:
                case DemoSettlementEventKind.FactionJoined:
                case DemoSettlementEventKind.ConquestBattle:
                case DemoSettlementEventKind.WarEnded:
                case DemoSettlementEventKind.Occupied: // 第 08-22a 章：攻陷是一般事件（征服才寫進史書）
                    Add(e.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Realm, e.EventId,
                        Name(e.SettlementId), T(" " + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.BeastAttack:
                    // 第 12-1 章 C2：獸襲為一般事件（攻擊至少留一人）。
                    Add(e.Week, DemoChronicleLevel.Normal,
                        DemoChronicleCategory.Wilds, e.EventId, Name(e.SettlementId), T(" 的" + DemoPlayerDetail.Clean(e.Cause)));
                    break;
                case DemoSettlementEventKind.CultureSplit:
                    AddWorld(e.Week, WeightBelief, DemoChronicleCategory.Culture, e.EventId,
                        Name(e.SettlementId), T(" " + DemoPlayerDetail.Clean(e.Cause)));
                    break;
            }
        }

        // 第 08-18i 章：每年每聚落開墾的農田合計成一條（一般）。
        foreach (var g in events.Where(e => e.Kind == DemoSettlementEventKind.FarmlandCleared).GroupBy(e => (Year: YearOf(e.Week), e.SettlementId)))
            Add(g.Max(e => e.Week), DemoChronicleLevel.Normal, DemoChronicleCategory.Farmland, 0, Name(g.Key.SettlementId), T($" 本年開墾 {g.Count()} 塊農田"));

        // 每年每聚落合計：餓死（重大）、缺糧週數、外移與遷入（一般）。
        foreach (var g in events.Where(e => e.Kind is DemoSettlementEventKind.StarvationDeath or DemoSettlementEventKind.MigrantStarvationDeath
                         or DemoSettlementEventKind.Hunger or DemoSettlementEventKind.Emigration or DemoSettlementEventKind.Arrival)
                     .GroupBy(e => (Year: YearOf(e.Week), e.SettlementId)))
        {
            var lastWeek = g.Max(e => e.Week);
            var heat = g.Where(e => e.Kind == DemoSettlementEventKind.Hunger && e.CauseEventId > 0)
                .Select(e => e.CauseEventId).FirstOrDefault(id => byId.TryGetValue(id, out var c) && DemoEnvironment.IsSpell(c.Kind));
            var firstAggregate = entries.Count;
            var starved = g.Where(e => e.Kind is DemoSettlementEventKind.StarvationDeath or DemoSettlementEventKind.MigrantStarvationDeath).Sum(e => e.Count);
            if (starved > 0)
                Add(lastWeek, DemoChronicleLevel.Major, DemoChronicleCategory.Famine, 0, Name(g.Key.SettlementId), T($" 本年餓死 {starved} 人"));
            var hungerWeeks = g.Count(e => e.Kind == DemoSettlementEventKind.Hunger);
            if (hungerWeeks > 0)
                Add(lastWeek, DemoChronicleLevel.Normal, DemoChronicleCategory.Hunger, 0, Name(g.Key.SettlementId), T($" 本年缺糧 {hungerWeeks} 週"));
            var left = g.Where(e => e.Kind == DemoSettlementEventKind.Emigration).Sum(e => e.Count);
            if (left > 0)
                Add(lastWeek, DemoChronicleLevel.Normal, DemoChronicleCategory.Migration, 0, Name(g.Key.SettlementId), T($" 本年外移 {left} 人"));
            var arrived = g.Where(e => e.Kind == DemoSettlementEventKind.Arrival).Sum(e => e.Count);
            if (arrived > 0)
                Add(lastWeek, DemoChronicleLevel.Normal, DemoChronicleCategory.Migration, 0, Name(g.Key.SettlementId), T($" 本年遷入 {arrived} 人"));
            if (heat > 0)
                for (var k = firstAggregate; k < entries.Count; k++)
                    if (entries[k].Entry.Category is DemoChronicleCategory.Famine or DemoChronicleCategory.Hunger)
                        aggregateCause[k] = heat;
        }

        // 第 08-16 章：同一年裡，起因（沿因果鏈最近的祖先）也是編年史條目的，記為它的後續，介面縮排接在起因下面。
        var entryYearOf = entries.Where(x => x.Entry.EventId > 0).GroupBy(x => x.Entry.EventId).ToDictionary(g => g.Key, g => g.First().Year);
        for (var k = 0; k < entries.Count; k++)
        {
            var (year, entry) = entries[k];
            long parent = 0;
            if (entry.EventId > 0 && byId.TryGetValue(entry.EventId, out var e))
                parent = DemoStories.CauseChain(e, byId).Select(c => c.EventId)
                    .FirstOrDefault(id => id != entry.EventId && entryYearOf.TryGetValue(id, out var y) && y == year);
            else if (aggregateCause.TryGetValue(k, out var cause) && entryYearOf.TryGetValue(cause, out var y2) && y2 == year)
                parent = cause;
            if (parent > 0)
                entries[k] = (year, entry with { ParentEventId = parent });
        }

        // 第 08-10d 章：全世界第一個長到村莊、城鎮、城市、首都的聚落是世界大事（同一週時取編號小的）。
        var firstOfTier = new Dictionary<DemoSettlementTier, (int Week, int SettlementId)>();
        foreach (var s in world.Sites.Settlements)
            foreach (var line in world.ChronicleOf(s.Id).Milestones)
                if (RaisedTo(line) is { } tier && (!firstOfTier.TryGetValue(tier, out var best) || (line.Week, s.Id).CompareTo(best) < 0))
                    firstOfTier[tier] = (line.Week, s.Id);

        // 聚落小史：等級升降（重大）、人口里程碑與代表人物就任／卸任（一般）。
        foreach (var s in world.Sites.Settlements)
        {
            var chronicle = world.ChronicleOf(s.Id);
            foreach (var line in chronicle.Milestones)
            {
                if (DemoSettlementChronicle.IsTierChange(line))
                    AddWorld(line.Week, RaisedTo(line) is { } tier && firstOfTier[tier] == (line.Week, s.Id) ? WeightFirstTier : 0,
                        DemoChronicleCategory.Tier, 0, Name(s.Id), T($" {line.Text}"));
                else if (DemoSettlementChronicle.IsPopulationMilestone(line))
                    Add(line.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Population, 0, Name(s.Id), T($" {line.Text}"));
            }
            foreach (var line in chronicle.Representatives)
                Add(line.Week, DemoChronicleLevel.Normal, DemoChronicleCategory.Representative, 0, Name(s.Id), T($"：{line.Text}"));
        }

        // 第 08-6c 章：世界時代的更替（第一段是開局，不記）。世界法則視窗指定的時代寫成「天命驟變」。
        foreach (var span in world.Ages().Skip(1).Where(a => a.FromWeek <= world.CommittedWeeks))
            AddWorld(span.FromWeek, WeightEra, DemoChronicleCategory.Era, 0,
                T($"{(span.Manual ? "天命驟變，" : "")}世界進入{DemoAges.Name(span.Age)}——{DemoAges.Description(span.Age)}"));

        var stats = events.Where(e => e.Kind is DemoSettlementEventKind.Birth or DemoSettlementEventKind.NaturalDeath)
            .GroupBy(e => YearOf(e.Week))
            .ToDictionary(g => g.Key, g => g.GroupBy(e => e.SettlementId).ToDictionary(
                s => s.Key,
                s => (Births: s.Where(e => e.Kind == DemoSettlementEventKind.Birth).Sum(e => e.Count),
                      NaturalDeaths: s.Where(e => e.Kind == DemoSettlementEventKind.NaturalDeath).Sum(e => e.Count))));

        var lastYear = Math.Max(1, DemoSettlementHistory.YearOfDay(world.Snapshot().DaysElapsed));
        var byYear = entries.ToLookup(x => x.Year, x => x.Entry);
        var era = DemoEra.Hope;
        var result = new List<DemoChronicleYear>(lastYear);
        for (var year = 1; year <= lastYear; year++)
        {
            var started = eraStarts.TryGetValue(year, out var newEra);
            if (started) era = newEra;
            var bySettlement = stats.GetValueOrDefault(year) ?? new Dictionary<int, (long Births, long NaturalDeaths)>();
            result.Add(new DemoChronicleYear(
                year, era, started,
                Threaded(byYear[year]).AsReadOnly(),
                bySettlement.Values.Sum(v => v.Births), bySettlement.Values.Sum(v => v.NaturalDeaths), bySettlement));
        }
        return result.AsReadOnly();
    }

    /// <summary>第 08-10d 章：這一筆小史是升到哪一級（不是升級回傳 null）。</summary>
    private static DemoSettlementTier? RaisedTo(DemoChronicleLine line)
    {
        var at = line.Text.IndexOf(DemoSettlementChronicle.TierUpMarker, StringComparison.Ordinal);
        if (at < 0)
            return null;
        var rest = line.Text[(at + DemoSettlementChronicle.TierUpMarker.Length)..];
        foreach (var tier in new[] { DemoSettlementTier.Capital, DemoSettlementTier.City, DemoSettlementTier.Town, DemoSettlementTier.Village })
            if (rest.StartsWith(DemoSettlementHistory.TierName(tier), StringComparison.Ordinal))
                return tier;
        return null;
    }

    /// <summary>第 08-16 章：起因依時間排列，每個起因的後續（遞迴）緊接在它下面，合成一段。</summary>
    private static List<DemoChronicleEntry> Threaded(IEnumerable<DemoChronicleEntry> yearEntries)
    {
        static IOrderedEnumerable<DemoChronicleEntry> Sort(IEnumerable<DemoChronicleEntry> xs)
            => xs.OrderBy(e => e.Week).ThenBy(e => e.Level).ThenBy(e => e.Category);
        var all = yearEntries.ToList();
        var ids = all.Where(e => e.EventId > 0).Select(e => e.EventId).ToHashSet();
        var children = all.Where(e => e.ParentEventId > 0 && ids.Contains(e.ParentEventId)).ToLookup(e => e.ParentEventId);
        var result = new List<DemoChronicleEntry>(all.Count);
        void Visit(DemoChronicleEntry e)
        {
            result.Add(e);
            if (e.EventId > 0)
                foreach (var c in Sort(children[e.EventId]))
                    Visit(c);
        }
        foreach (var root in Sort(all.Where(e => e.ParentEventId == 0 || !ids.Contains(e.ParentEventId))))
            Visit(root);
        return result;
    }

    /// <summary>十年摘要（舊年份折疊時顯示）：只數重大條目。</summary>
    public static string DecadeSummary(IReadOnlyList<DemoChronicleYear> years)
    {
        ArgumentNullException.ThrowIfNull(years);
        if (years.Count == 0) return "";
        var major = years.SelectMany(y => y.Entries).Where(e => e.Level == DemoChronicleLevel.Major).ToList();
        int Count(DemoChronicleCategory c) => major.Count(e => e.Category == c);
        var parts = new List<string>();
        void Part(DemoChronicleCategory c, string unit) { var n = Count(c); if (n > 0) parts.Add($"{unit} {n}"); }
        Part(DemoChronicleCategory.Era, "時代更替");
        Part(DemoChronicleCategory.Knowledge, "技術首創");
        Part(DemoChronicleCategory.Founding, "建立聚落");
        Part(DemoChronicleCategory.Ruin, "成為遺跡");
        Part(DemoChronicleCategory.Tier, "等級升降");
        Part(DemoChronicleCategory.Farmland, "開墾農田");
        Part(DemoChronicleCategory.Famine, "餓死紀錄");
        Part(DemoChronicleCategory.Culture, "文化分化");
        Part(DemoChronicleCategory.Faith, "信仰興衰");
        Part(DemoChronicleCategory.Wilds, "野外險境");
        Part(DemoChronicleCategory.Disaster, "天災");
        var head = $"第 {years[0].Year}～{years[^1].Year} 年：";
        return head + (parts.Count == 0 ? "沒有重大事件" : string.Join("、", parts))
               + $"｜出生 {years.Sum(y => y.Births)}、老死 {years.Sum(y => y.NaturalDeaths)}";
    }

    /// <summary>各類重大條目的件數（「建立聚落 2、天災 1」）；沒有回傳空字串。</summary>
    private static string Tally(IReadOnlyCollection<DemoChronicleEntry> major)
    {
        var parts = new List<string>();
        void Part(DemoChronicleCategory c, string unit)
        {
            var n = major.Count(e => e.Category == c);
            if (n > 0)
                parts.Add($"{unit} {n}");
        }
        Part(DemoChronicleCategory.Founding, "建立聚落");
        Part(DemoChronicleCategory.Ruin, "成為遺跡");
        Part(DemoChronicleCategory.Tier, "等級升降");
        Part(DemoChronicleCategory.Realm, "勢力變動");
        Part(DemoChronicleCategory.Famine, "餓死紀錄");
        Part(DemoChronicleCategory.Disaster, "天災");
        Part(DemoChronicleCategory.Wilds, "野外險境");
        return string.Join("、", parts);
    }

    /// <summary>
    /// 第 08-10d 章：這一年的「各地大事」合計（不是世界大事的重大條目，依類型數件數）；沒有回傳空字串。
    /// 編年史預設只列世界大事，各地的興廢用這一行帶過（點開才逐條列）。
    /// </summary>
    public static string LocalTally(DemoChronicleYear year)
    {
        ArgumentNullException.ThrowIfNull(year);
        return Tally(year.Entries.Where(e => e.Level == DemoChronicleLevel.Major && !e.IsWorld).ToList());
    }

    /// <summary>第 08-10e 章：一段年代的「各地大事」合計（文字史書每卷一行）。</summary>
    public static string LocalTally(IReadOnlyList<DemoChronicleYear> years)
    {
        ArgumentNullException.ThrowIfNull(years);
        return Tally(years.SelectMany(y => y.Entries).Where(e => e.Level == DemoChronicleLevel.Major && !e.IsWorld).ToList());
    }

    /// <summary>
    /// 第 08-10d 章：一段年代的定性（戰亂、衰退、饑荒、擴張、創新、轉折、平穩、平靜的年代），由件數依固定順序判定。
    /// 年代小結與文字史書（第 08-10e 章）共用。
    /// </summary>
    public static string DecadeCharacter(IReadOnlyList<DemoChronicleYear> years)
    {
        ArgumentNullException.ThrowIfNull(years);
        var major = years.SelectMany(y => y.Entries.Where(e => e.Level == DemoChronicleLevel.Major)).ToList();
        var world = major.Where(e => e.IsWorld).ToList();
        int Count(DemoChronicleCategory c) => major.Count(e => e.Category == c);
        // 第 08-10e 章：只數真的動武的（開戰、內戰、武裝獨立）；和平獲准的獨立不算戰事。
        var wars = world.Count(e => e.Weight == WeightWar);
        var founded = Count(DemoChronicleCategory.Founding);
        var ruined = Count(DemoChronicleCategory.Ruin);
        return wars >= TempWarEra ? "戰亂的年代"
            : ruined >= 2 && ruined >= founded ? "衰退的年代"
            : Count(DemoChronicleCategory.Famine) >= TempFamineEra ? "饑荒的年代"
            : founded >= TempGrowthEra && founded > ruined ? "擴張的年代"
            : world.Count(e => e.Weight == WeightKnowledge) >= TempInventionEra ? "創新的年代"
            : world.Any(e => e.Weight == WeightEra) ? "轉折的年代"
            : major.Count == 0 ? "平靜的年代"
            : "平穩的年代";
    }

    /// <summary>
    /// 第 08-10d 章：年代小結（舊年份折疊時顯示，取代只數件數的 <see cref="DecadeSummary"/>）。
    /// 一句定性（戰亂、衰退、饑荒、擴張、創新、轉折、平穩、平靜的年代）＋最重要的幾件世界大事（依時間）＋各地大事合計＋出生與老死。
    /// 只組合已經發生的事實：定性由件數依固定規則判定，引述的是編年史既有條目的原文，不添加任何原因或評語。
    /// </summary>
    public static string DecadeStory(IReadOnlyList<DemoChronicleYear> years)
    {
        ArgumentNullException.ThrowIfNull(years);
        if (years.Count == 0) return "";
        var major = years.SelectMany(y => y.Entries.Where(e => e.Level == DemoChronicleLevel.Major).Select(e => (y.Year, Entry: e))).ToList();
        var world = major.Where(x => x.Entry.IsWorld).ToList();
        var character = DecadeCharacter(years);
        var highlights = world.OrderByDescending(x => x.Entry.Weight).ThenBy(x => x.Year).ThenBy(x => x.Entry.Week).Take(TempStoryHighlights)
            .OrderBy(x => x.Year).ThenBy(x => x.Entry.Week)
            .Select(x => $"第 {x.Year} 年，{x.Entry.Text.Trim()}").ToList();
        var local = Tally(major.Where(x => !x.Entry.IsWorld).Select(x => x.Entry).ToList());
        return $"第 {years[0].Year}～{years[^1].Year} 年：{character}"
               + (highlights.Count > 0 ? "——" + string.Join("；", highlights) + "。" : "。")
               + (local.Length > 0 ? $"各地：{local}。" : "")
               + $"｜出生 {years.Sum(y => y.Births)}、老死 {years.Sum(y => y.NaturalDeaths)}";
    }

    /// <summary>介面預設折疊的十年分段：除了最近 <see cref="TempFoldAfterYears"/> 年，其餘每 10 年一段。</summary>
    public static IReadOnlyList<(int FromYear, int ToYear)> FoldedDecades(int currentYear)
    {
        var list = new List<(int, int)>();
        for (var from = 1; from + 9 <= currentYear - TempFoldAfterYears; from += 10)
            list.Add((from, from + 9));
        return list;
    }
}
