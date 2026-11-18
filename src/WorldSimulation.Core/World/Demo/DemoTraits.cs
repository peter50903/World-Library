namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-10g、08-10h 章：領袖的性格（參考十字軍之王的特質）。八組相反的性格，每位領袖在其中三組各有一個。
/// </summary>
[Flags]
public enum DemoTrait
{
    None = 0,

    /// <summary>好戰：更常宣戰。</summary>
    Warlike = 1,

    /// <summary>謹慎：兵力佔更大優勢才宣戰。</summary>
    Cautious = 2,

    /// <summary>寬厚：更容易接受下級的請願。</summary>
    Lenient = 4,

    /// <summary>嚴苛：很少接受下級的請願。</summary>
    Harsh = 8,

    /// <summary>公正：下級更忠誠。</summary>
    Just = 16,

    /// <summary>暴虐：下級更不忠誠。</summary>
    Cruel = 32,

    /// <summary>野心：自己是下級時，對上級更不忠誠。</summary>
    Ambitious = 64,

    /// <summary>知足：自己是下級時，對上級更忠誠。</summary>
    Content = 128,

    /// <summary>勇猛：交戰時讓對方死更多人。</summary>
    Brave = 256,

    /// <summary>怯懦：交戰時讓對方死較少人。</summary>
    Craven = 512,

    /// <summary>堅毅：兵力再懸殊也很少不戰而降。</summary>
    Stubborn = 1024,

    /// <summary>軟弱：兵力差距不大就不戰而降。</summary>
    Yielding = 2048,

    /// <summary>狡詐：不理會停戰照樣宣戰；下級不太信任。</summary>
    Deceitful = 4096,

    /// <summary>守信：下級信任。</summary>
    Honest = 8192,

    /// <summary>虔誠：更常對異教發動聖戰；異教的下級更不忠誠。</summary>
    Zealous = 16384,

    /// <summary>多疑：不為信仰開戰；不在意下級的信仰。</summary>
    Cynical = 32768,

    // ── 第 08-22c 章：身體特質（天生、會遺傳、效果小；參考 CK3 的先天特質） ──

    /// <summary>強壯：打仗時讓對方死傷多一成。</summary>
    Strong = 1 << 16,

    /// <summary>虛弱：打仗時讓對方死傷少一成。</summary>
    Frail = 1 << 17,

    /// <summary>聰慧：領導的聚落研究快一成。</summary>
    Clever = 1 << 18,

    /// <summary>遲鈍：領導的聚落研究慢一成。</summary>
    Dull = 1 << 19,

    /// <summary>俊美：下級稍微忠誠。</summary>
    Comely = 1 << 20,

    /// <summary>醜陋：下級稍微不忠誠。</summary>
    Homely = 1 << 21,

    // ── 第 08-22c 章：傳說特性（使用者 2026-11-18：不會遺傳、罕見、可以大幅增加各項數值） ──

    /// <summary>天命：下級忠誠大增，打仗時讓對方死傷多三成。</summary>
    Chosen = 1 << 22,

    /// <summary>龍血：打仗時讓對方死傷多六成。</summary>
    Dragonblood = 1 << 23,

    /// <summary>賢者：領導的聚落研究快五成。</summary>
    Sage = 1 << 24,

    /// <summary>聖者：下級忠誠大增。</summary>
    Saint = 1 << 25
}

/// <summary>第 08-10g、08-10h 章：性格的抽取、遺傳、名稱與效果。全部數值【TEMP】。</summary>
public static class DemoTraits
{
    /// <summary>【TEMP】每位領袖幾項性格；子女承襲父母一項性格的機率。</summary>
    public const int TempCount = 3, TempInheritPercent = 50;

    /// <summary>【TEMP】好戰的宣戰機率加幾 %；謹慎要有幾 % 的兵力才宣戰；寬厚、嚴苛對請願成功率的加減；公正、暴虐、守信、狡詐對下級忠誠的加減。</summary>
    public const int TempWarlikeChance = 20, TempCautiousStrengthPercent = 200, TempLenientPetition = 25, TempHarshPetition = -25,
        TempJustLoyalty = 10, TempCruelLoyalty = -10, TempHonestLoyalty = 5, TempDeceitfulLoyalty = -5;

    /// <summary>【TEMP】野心、知足對上級忠誠的加減；勇猛、怯懦讓對方交戰死亡變成幾 %；堅毅、軟弱在對方兵力達幾 % 時不戰而降；虔誠的聖戰加成倍數與異教下級忠誠的加減。</summary>
    public const int TempAmbitiousLoyalty = -15, TempContentLoyalty = 15, TempBraveBattlePercent = 125, TempCravenBattlePercent = 75,
        TempStubbornSurrenderPercent = 1000, TempYieldingSurrenderPercent = 300, TempZealousHolyWarTimes = 2, TempZealousFaithLoyalty = -10;

    private static readonly (DemoTrait A, DemoTrait B)[] Axes =
    {
        (DemoTrait.Warlike, DemoTrait.Cautious), (DemoTrait.Lenient, DemoTrait.Harsh), (DemoTrait.Just, DemoTrait.Cruel),
        (DemoTrait.Ambitious, DemoTrait.Content), (DemoTrait.Brave, DemoTrait.Craven), (DemoTrait.Stubborn, DemoTrait.Yielding),
        (DemoTrait.Deceitful, DemoTrait.Honest), (DemoTrait.Zealous, DemoTrait.Cynical)
    };

    /// <summary>全部性格（依顯示順序；不含身體特質與傳說特性）。</summary>
    public static IReadOnlyList<DemoTrait> All { get; } = Axes.SelectMany(x => new[] { x.A, x.B }).ToList();

    /// <summary>依顯示順序列出每一項性格（不含身體特質與傳說特性）。</summary>
    public static IReadOnlyList<DemoTrait> List(DemoTrait traits) => All.Where(t => (traits & t) != 0).ToList();

    /// <summary>第 08-22c 章【TEMP】身體特質三組（體魄、才智、容貌）：每組各 <see cref="TempBodyPercent"/>% 是正面、負面，其餘沒有；
    /// 父母有同一組的特質時 <see cref="TempBodyInheritPercent"/>% 遺傳。</summary>
    public const int TempBodyPercent = 12, TempBodyInheritPercent = 50;

    /// <summary>第 08-22c 章【TEMP】傳說特性（不會遺傳）：新任領袖 <see cref="TempLeaderLegendPercent"/>%、英雄 <see cref="TempHeroLegendPercent"/>% 天生帶一項。</summary>
    public const int TempLeaderLegendPercent = 2, TempHeroLegendPercent = 40;

    /// <summary>【TEMP】身體特質與傳說特性的效果：強壯、虛弱讓對方交戰死亡變成幾 %；聰慧、遲鈍、賢者的研究加減 %；
    /// 俊美、醜陋、天命、聖者的下級忠誠加減；天命、龍血讓對方交戰死亡變成幾 %。</summary>
    public const int TempStrongBattlePercent = 110, TempFrailBattlePercent = 90, TempCleverResearch = 10, TempDullResearch = -10, TempSageResearch = 50,
        TempComelyLoyalty = 5, TempHomelyLoyalty = -5, TempChosenLoyalty = 20, TempSaintLoyalty = 25, TempChosenBattlePercent = 130, TempDragonbloodBattlePercent = 160;

    /// <summary>
    /// 第 08-22h 章【TEMP】體魄與壽命（08-22 第 2 節第 3 項：體魄影響會戰與壽命；使用者 2026-11-18：身體特質只做小幅變動）：
    /// 具名人物每週老死的機率乘以幾 %——強壯 <see cref="TempStrongLifePercent"/>%、虛弱 <see cref="TempFrailLifePercent"/>%、
    /// 龍血 <see cref="TempDragonbloodLifePercent"/>%（傳說特性，可以和體魄相乘）。
    /// </summary>
    public const int TempStrongLifePercent = 80, TempFrailLifePercent = 125, TempDragonbloodLifePercent = 60;

    /// <summary>第 08-22h 章：這個人每週老死的機率乘以幾 %（沒有相關特質＝100）。</summary>
    public static int LifePercent(DemoTrait traits)
        => (Has(traits, DemoTrait.Strong) ? TempStrongLifePercent : Has(traits, DemoTrait.Frail) ? TempFrailLifePercent : 100)
           * (Has(traits, DemoTrait.Dragonblood) ? TempDragonbloodLifePercent : 100) / 100;

    private static readonly (DemoTrait A, DemoTrait B)[] BodyAxes =
    {
        (DemoTrait.Strong, DemoTrait.Frail), (DemoTrait.Clever, DemoTrait.Dull), (DemoTrait.Comely, DemoTrait.Homely)
    };

    /// <summary>身體特質（依顯示順序）。</summary>
    public static IReadOnlyList<DemoTrait> Body { get; } = BodyAxes.SelectMany(x => new[] { x.A, x.B }).ToList();

    /// <summary>傳說特性（依顯示順序）。</summary>
    public static IReadOnlyList<DemoTrait> Legends { get; } = new[] { DemoTrait.Chosen, DemoTrait.Dragonblood, DemoTrait.Sage, DemoTrait.Saint };

    /// <summary>性格、身體特質、傳說特性全部（依顯示順序：傳說在前，最醒目）。</summary>
    public static IReadOnlyList<DemoTrait> Everything(DemoTrait traits)
        => Legends.Concat(All).Concat(Body).Where(t => (traits & t) != 0).ToList();

    public static bool IsLegend(DemoTrait trait) => Legends.Contains(trait);

    /// <summary>身體特質：每組照機率抽，父母有同組的特質時可能遺傳（<paramref name="draw"/>(10～15)）。</summary>
    public static DemoTrait RollBody(Func<int, ulong> draw, DemoTrait parent = DemoTrait.None)
    {
        ArgumentNullException.ThrowIfNull(draw);
        var body = DemoTrait.None;
        for (var i = 0; i < BodyAxes.Length; i++)
        {
            var (a, b) = BodyAxes[i];
            var inherited = Has(parent, a) ? a : Has(parent, b) ? b : DemoTrait.None;
            if (inherited != DemoTrait.None && (int)(draw(10 + i) % 100UL) < TempBodyInheritPercent)
            {
                body |= inherited;
                continue;
            }
            var roll = (int)(draw(13 + i) % 100UL);
            body |= roll < TempBodyPercent ? a : roll < TempBodyPercent * 2 ? b : DemoTrait.None;
        }
        return body;
    }

    /// <summary>傳說特性：<paramref name="percent"/>% 帶一項（<paramref name="draw"/>(16、17)）；不看父母（不會遺傳）。</summary>
    public static DemoTrait RollLegend(Func<int, ulong> draw, int percent)
    {
        ArgumentNullException.ThrowIfNull(draw);
        return (int)(draw(16) % 100UL) < percent ? Legends[(int)(draw(17) % (ulong)Legends.Count)] : DemoTrait.None;
    }

    /// <summary>第 08-22c 章：一個具名人物的全部特質（性格＋身體特質＋傳說特性）。只承襲父母的性格與身體特質。</summary>
    public static DemoTrait RollPerson(Func<int, ulong> draw, DemoTrait parent, int legendPercent)
        => Roll(draw, parent) | RollBody(draw, parent) | RollLegend(draw, legendPercent);

    /// <summary>第 08-22c 章：領袖的特質讓他領導的聚落研究變成幾 %。</summary>
    public static int ResearchPercent(DemoTrait leader)
        => 100 + (Has(leader, DemoTrait.Clever) ? TempCleverResearch : 0) + (Has(leader, DemoTrait.Dull) ? TempDullResearch : 0)
            + (Has(leader, DemoTrait.Sage) ? TempSageResearch : 0);

    public static bool Has(DemoTrait traits, DemoTrait trait) => (traits & trait) != 0;

    /// <summary>同一組的相反性格。</summary>
    public static DemoTrait Opposite(DemoTrait trait)
    {
        var axis = Axes[AxisOf(trait)];
        return axis.A == trait ? axis.B : axis.A;
    }

    public static string Name(DemoTrait trait) => trait switch
    {
        DemoTrait.Warlike => "好戰",
        DemoTrait.Cautious => "謹慎",
        DemoTrait.Lenient => "寬厚",
        DemoTrait.Harsh => "嚴苛",
        DemoTrait.Just => "公正",
        DemoTrait.Cruel => "暴虐",
        DemoTrait.Ambitious => "野心",
        DemoTrait.Content => "知足",
        DemoTrait.Brave => "勇猛",
        DemoTrait.Craven => "怯懦",
        DemoTrait.Stubborn => "堅毅",
        DemoTrait.Yielding => "軟弱",
        DemoTrait.Deceitful => "狡詐",
        DemoTrait.Honest => "守信",
        DemoTrait.Zealous => "虔誠",
        DemoTrait.Cynical => "多疑",
        DemoTrait.Strong => "強壯",
        DemoTrait.Frail => "虛弱",
        DemoTrait.Clever => "聰慧",
        DemoTrait.Dull => "遲鈍",
        DemoTrait.Comely => "俊美",
        DemoTrait.Homely => "醜陋",
        DemoTrait.Chosen => "天命",
        DemoTrait.Dragonblood => "龍血",
        DemoTrait.Sage => "賢者",
        DemoTrait.Saint => "聖者",
        _ => ""
    };

    /// <summary>這項性格做什麼（人物頁的一句說明）。</summary>
    public static string Effect(DemoTrait trait) => trait switch
    {
        DemoTrait.Warlike => "更常向鄰居宣戰",
        DemoTrait.Cautious => "兵力佔了大優勢才肯宣戰",
        DemoTrait.Lenient => "容易答應下級的請願",
        DemoTrait.Harsh => "很少答應下級的請願",
        DemoTrait.Just => "下級比較忠誠",
        DemoTrait.Cruel => "下級比較不忠誠",
        DemoTrait.Ambitious => "屈居人下時不安分",
        DemoTrait.Content => "屈居人下時很安分",
        DemoTrait.Brave => "打仗時讓對方死傷更重",
        DemoTrait.Craven => "打仗時對方死傷較輕",
        DemoTrait.Stubborn => "兵力再懸殊也很少不戰而降",
        DemoTrait.Yielding => "兵力差一截就不戰而降",
        DemoTrait.Deceitful => "不理會停戰照樣宣戰，下級不太信任",
        DemoTrait.Honest => "說話算話，下級信任",
        DemoTrait.Zealous => "更常對異教發動聖戰，容不下異教的下級",
        DemoTrait.Cynical => "不為信仰開戰，也不在意下級信什麼",
        DemoTrait.Strong => "身體特質：打仗時讓對方死傷多一成，比較長壽（會遺傳）",
        DemoTrait.Frail => "身體特質：打仗時讓對方死傷少一成，比較短命（會遺傳）",
        DemoTrait.Clever => "身體特質：領導的聚落研究快一成（會遺傳）",
        DemoTrait.Dull => "身體特質：領導的聚落研究慢一成（會遺傳）",
        DemoTrait.Comely => "身體特質：下級稍微忠誠（會遺傳）",
        DemoTrait.Homely => "身體特質：下級稍微不忠誠（會遺傳）",
        DemoTrait.Chosen => "傳說特性：天命所歸，下級忠誠大增，打仗時讓對方死傷多三成（不會遺傳）",
        DemoTrait.Dragonblood => "傳說特性：龍的血脈，打仗時讓對方死傷多六成，長壽（不會遺傳）",
        DemoTrait.Sage => "傳說特性：賢者，領導的聚落研究快五成（不會遺傳）",
        DemoTrait.Saint => "傳說特性：聖者，下級忠誠大增（不會遺傳）",
        _ => ""
    };

    /// <summary>「天命、好戰、嚴苛、野心、強壯」（第 08-22c 章：傳說特性在前、身體特質在後）；沒有特質回傳空字串。</summary>
    public static string Names(DemoTrait traits) => string.Join("、", Everything(traits).Select(Name));

    private static int AxisOf(DemoTrait trait) => Array.FindIndex(Axes, x => x.A == trait || x.B == trait);

    /// <summary>
    /// 抽 <see cref="TempCount"/> 項性格：八組裡挑三組（<paramref name="draw"/>(0～2)）、每組挑一邊（<paramref name="draw"/>(3～5)）。
    /// 有父母時 <see cref="TempInheritPercent"/>% 承襲父母的一項（<paramref name="draw"/>(6、7)）：自己同一組的那一項換成父母的；沒有同組的就換掉最後一項。
    /// <paramref name="draw"/> 是這個人自己的亂數（依實體編號，不推進計數器）。
    /// </summary>
    public static DemoTrait Roll(Func<int, ulong> draw, DemoTrait parent = DemoTrait.None)
    {
        ArgumentNullException.ThrowIfNull(draw);
        var free = Enumerable.Range(0, Axes.Length).ToList();
        var mine = new List<DemoTrait>(TempCount);
        for (var i = 0; i < TempCount; i++)
        {
            var at = (int)(draw(i) % (ulong)free.Count);
            var axis = Axes[free[at]];
            free.RemoveAt(at);
            mine.Add(draw(TempCount + i) % 2UL == 0 ? axis.A : axis.B);
        }
        var inherited = List(parent); // 只承襲性格（身體特質另外遺傳，傳說特性不遺傳）
        if (inherited.Count > 0 && (int)(draw(TempCount * 2) % 100UL) < TempInheritPercent)
        {
            var from = inherited[(int)(draw(TempCount * 2 + 1) % (ulong)inherited.Count)];
            var same = mine.FindIndex(t => AxisOf(t) == AxisOf(from));
            mine[same >= 0 ? same : mine.Count - 1] = from;
        }
        return mine.Aggregate(DemoTrait.None, (all, t) => all | t);
    }

    /// <summary>宣戰機率的加成（%）。</summary>
    public static int WarChance(DemoTrait self) => Has(self, DemoTrait.Warlike) ? TempWarlikeChance : 0;

    /// <summary>請願成功率的加減（看上級領袖的性格）。</summary>
    public static int Petition(DemoTrait lord) => (Has(lord, DemoTrait.Lenient) ? TempLenientPetition : 0) + (Has(lord, DemoTrait.Harsh) ? TempHarshPetition : 0);

    /// <summary>上級領袖的性格對下級忠誠的影響（逐項，給忠誠明細）。</summary>
    public static IReadOnlyList<(string Text, int Value)> LordLoyalty(DemoTrait lord)
    {
        var list = new List<(string, int)>();
        if (Has(lord, DemoTrait.Just)) list.Add(("領袖公正", TempJustLoyalty));
        if (Has(lord, DemoTrait.Cruel)) list.Add(("領袖暴虐", TempCruelLoyalty));
        if (Has(lord, DemoTrait.Honest)) list.Add(("領袖守信", TempHonestLoyalty));
        if (Has(lord, DemoTrait.Deceitful)) list.Add(("領袖狡詐", TempDeceitfulLoyalty));
        if (Has(lord, DemoTrait.Comely)) list.Add(("領袖俊美", TempComelyLoyalty)); // 第 08-22c 章
        if (Has(lord, DemoTrait.Homely)) list.Add(("領袖醜陋", TempHomelyLoyalty));
        if (Has(lord, DemoTrait.Chosen)) list.Add(("領袖天命所歸", TempChosenLoyalty));
        if (Has(lord, DemoTrait.Saint)) list.Add(("領袖是聖者", TempSaintLoyalty));
        return list;
    }

    /// <summary>下級忠誠的加減（上級領袖性格的合計）。</summary>
    public static int Loyalty(DemoTrait lord) => LordLoyalty(lord).Sum(x => x.Value);

    /// <summary>下級自己的領袖的性格對忠誠的影響（野心、知足）。</summary>
    public static IReadOnlyList<(string Text, int Value)> VassalLoyalty(DemoTrait self)
    {
        var list = new List<(string, int)>();
        if (Has(self, DemoTrait.Ambitious)) list.Add(("本地領袖有野心", TempAmbitiousLoyalty));
        if (Has(self, DemoTrait.Content)) list.Add(("本地領袖知足", TempContentLoyalty));
        return list;
    }

    /// <summary>這位領袖帶兵時，對方交戰死亡變成幾 %。</summary>
    public static int BattlePercent(DemoTrait self)
    {
        var percent = Has(self, DemoTrait.Brave) ? TempBraveBattlePercent : Has(self, DemoTrait.Craven) ? TempCravenBattlePercent : 100;
        // 第 08-22c 章：身體特質（強壯、虛弱）與傳說特性（天命、龍血）相乘。
        percent = percent * (Has(self, DemoTrait.Strong) ? TempStrongBattlePercent : Has(self, DemoTrait.Frail) ? TempFrailBattlePercent : 100) / 100;
        percent = percent * (Has(self, DemoTrait.Dragonblood) ? TempDragonbloodBattlePercent : Has(self, DemoTrait.Chosen) ? TempChosenBattlePercent : 100) / 100;
        return percent;
    }

    /// <summary>守方領袖在對方兵力達幾 % 時不戰而降（<paramref name="normal"/>＝一般的門檻）。</summary>
    public static int SurrenderPercent(DemoTrait defender, int normal)
        => Has(defender, DemoTrait.Stubborn) ? TempStubbornSurrenderPercent : Has(defender, DemoTrait.Yielding) ? TempYieldingSurrenderPercent : normal;
}
