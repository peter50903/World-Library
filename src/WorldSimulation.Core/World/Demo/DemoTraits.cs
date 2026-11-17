namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-10g 章：領袖的性格（參考十字軍之王的特質）。三組相反的性格，每位領袖在其中兩組各有一個。
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
    Cruel = 32
}

/// <summary>第 08-10g 章：性格的抽取、遺傳、名稱與效果。全部數值【TEMP】。</summary>
public static class DemoTraits
{
    /// <summary>【TEMP】好戰的宣戰機率加幾 %；謹慎要有幾 % 的兵力才宣戰；寬厚、嚴苛對請願成功率的加減；公正、暴虐對下級忠誠的加減；子女承襲父母一項性格的機率。</summary>
    public const int TempWarlikeChance = 20, TempCautiousStrengthPercent = 200, TempLenientPetition = 25, TempHarshPetition = -25,
        TempJustLoyalty = 10, TempCruelLoyalty = -10, TempInheritPercent = 50;

    private static readonly (DemoTrait A, DemoTrait B)[] Axes =
    {
        (DemoTrait.Warlike, DemoTrait.Cautious), (DemoTrait.Lenient, DemoTrait.Harsh), (DemoTrait.Just, DemoTrait.Cruel)
    };

    /// <summary>依顯示順序列出每一項。</summary>
    public static IReadOnlyList<DemoTrait> List(DemoTrait traits)
        => Axes.SelectMany(x => new[] { x.A, x.B }).Where(t => (traits & t) != 0).ToList();

    public static bool Has(DemoTrait traits, DemoTrait trait) => (traits & trait) != 0;

    public static string Name(DemoTrait trait) => trait switch
    {
        DemoTrait.Warlike => "好戰",
        DemoTrait.Cautious => "謹慎",
        DemoTrait.Lenient => "寬厚",
        DemoTrait.Harsh => "嚴苛",
        DemoTrait.Just => "公正",
        DemoTrait.Cruel => "暴虐",
        _ => ""
    };

    /// <summary>「好戰、嚴苛」；沒有性格回傳空字串。</summary>
    public static string Names(DemoTrait traits) => string.Join("、", List(traits).Select(Name));

    private static int AxisOf(DemoTrait trait) => Array.FindIndex(Axes, x => x.A == trait || x.B == trait);

    /// <summary>
    /// 抽兩項性格：三組裡挑兩組、每組挑一邊（<paramref name="draw"/>(0～3)）。有父母時 <see cref="TempInheritPercent"/>% 承襲父母的一項
    /// （<paramref name="draw"/>(4、5)），另一項留自己抽到的、不同組的那一項。<paramref name="draw"/> 是這個人自己的亂數（依實體編號，不推進計數器）。
    /// </summary>
    public static DemoTrait Roll(Func<int, ulong> draw, DemoTrait parent = DemoTrait.None)
    {
        ArgumentNullException.ThrowIfNull(draw);
        var first = (int)(draw(0) % 3UL);
        var second = (first + 1 + (int)(draw(1) % 2UL)) % 3;
        DemoTrait Side(int axis, int k) => draw(k) % 2UL == 0 ? Axes[axis].A : Axes[axis].B;
        var mine = new[] { Side(first, 2), Side(second, 3) };
        var inherited = List(parent);
        if (inherited.Count == 0 || (int)(draw(4) % 100UL) >= TempInheritPercent)
            return mine[0] | mine[1];
        var from = inherited[(int)(draw(5) % (ulong)inherited.Count)];
        return from | mine.First(t => AxisOf(t) != AxisOf(from));
    }

    /// <summary>請願成功率的加減（看上級領袖的性格）。</summary>
    public static int Petition(DemoTrait lord) => (Has(lord, DemoTrait.Lenient) ? TempLenientPetition : 0) + (Has(lord, DemoTrait.Harsh) ? TempHarshPetition : 0);

    /// <summary>下級忠誠的加減（看上級領袖的性格）。</summary>
    public static int Loyalty(DemoTrait lord) => (Has(lord, DemoTrait.Just) ? TempJustLoyalty : 0) + (Has(lord, DemoTrait.Cruel) ? TempCruelLoyalty : 0);
}
