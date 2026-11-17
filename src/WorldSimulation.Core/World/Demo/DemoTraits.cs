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
    Cynical = 32768
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

    /// <summary>全部性格（依顯示順序）。</summary>
    public static IReadOnlyList<DemoTrait> All { get; } = Axes.SelectMany(x => new[] { x.A, x.B }).ToList();

    /// <summary>依顯示順序列出每一項。</summary>
    public static IReadOnlyList<DemoTrait> List(DemoTrait traits) => All.Where(t => (traits & t) != 0).ToList();

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
        _ => ""
    };

    /// <summary>「好戰、嚴苛、野心」；沒有性格回傳空字串。</summary>
    public static string Names(DemoTrait traits) => string.Join("、", List(traits).Select(Name));

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
        var inherited = List(parent);
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
    public static int BattlePercent(DemoTrait self) => Has(self, DemoTrait.Brave) ? TempBraveBattlePercent : Has(self, DemoTrait.Craven) ? TempCravenBattlePercent : 100;

    /// <summary>守方領袖在對方兵力達幾 % 時不戰而降（<paramref name="normal"/>＝一般的門檻）。</summary>
    public static int SurrenderPercent(DemoTrait defender, int normal)
        => Has(defender, DemoTrait.Stubborn) ? TempStubbornSurrenderPercent : Has(defender, DemoTrait.Yielding) ? TempYieldingSurrenderPercent : normal;
}
