namespace WorldSimulation.Core.World.Demo;

/// <summary>代表人物人像的部件選擇（全部由程式依編號決定，畫面再染色疊圖）。</summary>
/// <param name="Face">臉型 1～<see cref="DemoHeraldry.FacesPerSex"/>。</param>
/// <param name="Hair">髮型 1～<see cref="DemoHeraldry.HairsPerSex"/>。</param>
/// <param name="Beard">鬍鬚 0＝沒有、1～3。</param>
/// <param name="Age">0＝年輕、1＝中年紋路、2＝老年皺紋。</param>
/// <param name="SkinTone">膚色 0（淺）～4（深）。</param>
/// <param name="HairColor">髮色：0 黑、1 深棕、2 棕、3 金、4 紅、5 灰白。</param>
/// <param name="Clothes">衣著檔名的職業（chief、farmer、carpenter、blacksmith、hunter、shaman、scholar、commoner）。</param>
public sealed record DemoPortrait(bool Female, int Face, int Hair, int Beard, int Age, int SkinTone, int HairColor, string Clothes, int CultureId);

/// <summary>文化紋章：盾形底紋（plain、per_pale、chevron、quarterly）與圖案（charge_*）。</summary>
public sealed record DemoEmblem(int CultureId, string Field, string Charge);

/// <summary>
/// 第 08-16 章（十字軍之王那一半：人物與文化有臉）：人像、文化紋章、信仰符號的選擇規則。
/// 不用亂數流——只用實體編號的固定雜湊，所以同一個人、同一個文化在任何時候、讀檔前後都長一樣，也不影響世界。
/// 膚色與髮色依文化決定基調（同文化相像），個人再小幅變化；年長者髮色灰白。奇幻髮色不在這裡（日後特殊出身才有）。
/// </summary>
public static class DemoHeraldry
{
    public const int FacesPerSex = 14;
    public const int HairsPerSex = 10;

    /// <summary>【TEMP】幾歲起畫中年紋路、老年皺紋與灰白頭髮。</summary>
    public const int TempMiddleAge = 40;
    public const int TempOldAge = 60;

    public static readonly string[] Fields = { "plain", "per_pale", "chevron", "quarterly" };

    public static readonly string[] Charges = { "tree", "wave", "wheat", "wolf", "deer", "fish", "mountain", "sun", "moon", "star", "hammer", "bird" };

    public static readonly string[] FaithSymbols = { "sun", "crescent", "tree", "flame", "eye", "spiral", "antler", "ancestor" };

    /// <summary>文化特性 → 紋章圖案（先列的優先）。</summary>
    private static readonly (string Trait, string Charge)[] TraitCharges =
    {
        ("CT_FOREST_FOLK", "tree"), ("CT_SEA_FOLK", "wave"), ("CT_RIVER_FOLK", "fish"), ("CT_STEPPE_FOLK", "deer"),
        ("CT_MARTIAL", "wolf"), ("CT_WANDERER", "bird"), ("CT_ROOTED", "mountain"), ("CT_ANCESTRAL_TOMBS", "mountain"),
        ("CT_RELIGIOUS", "sun"), ("CT_SCHOLARLY", "moon"), ("CT_MERCHANT", "star"), ("CT_FAMILY_EXTENDED", "wheat"),
        ("CT_HONOR", "hammer")
    };

    /// <summary>信條 → 信仰符號（先列的優先）。</summary>
    private static readonly (string Tenet, string Symbol)[] TenetSymbols =
    {
        ("DOC_ANCESTOR", "ancestor"), ("DOC_SPIRIT_MEDIUM", "ancestor"), ("DOC_SACRED_GROVE", "tree"), ("DOC_NATURE", "antler"),
        ("DOC_SACRIFICE", "flame"), ("DOC_WAR", "flame"), ("DOC_ORACLE", "eye"), ("DOC_KNOWLEDGE", "eye"), ("DOC_SCHOLASTIC", "eye"),
        ("DOC_DEATH", "crescent"), ("DOC_MARTYR", "crescent"), ("DOC_FERTILITY", "spiral"), ("DOC_FAMILY", "spiral"),
        ("DOC_ORDER", "sun"), ("DOC_PEACE", "sun")
    };

    /// <summary>固定雜湊（FNV-1a；只用於外觀，不是世界亂數）。</summary>
    public static uint Hash(long id, int salt)
    {
        var h = 2166136261u;
        foreach (var b in BitConverter.GetBytes(id).Concat(BitConverter.GetBytes(salt)))
            h = (h ^ b) * 16777619u;
        return h;
    }

    public static DemoEmblem EmblemOf(DemoCulture culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var charge = TraitCharges.FirstOrDefault(t => culture.Traits.Contains(t.Trait)).Charge
            ?? Charges[Hash(culture.Id, 1) % (uint)Charges.Length];
        return new DemoEmblem(culture.Id, Fields[Hash(culture.Id, 2) % (uint)Fields.Length], charge);
    }

    public static string FaithSymbolOf(DemoFaith faith)
    {
        ArgumentNullException.ThrowIfNull(faith);
        return TenetSymbols.FirstOrDefault(t => faith.Tenets.Contains(t.Tenet)).Symbol
            ?? FaithSymbols[Hash(faith.Id, 3) % (uint)FaithSymbols.Length];
    }

    public static string ClothesOf(DemoRepresentativeRole role) => role switch
    {
        DemoRepresentativeRole.Leader => "chief",
        DemoRepresentativeRole.Farmer => "farmer",
        DemoRepresentativeRole.Carpenter => "carpenter",
        DemoRepresentativeRole.Smith => "blacksmith",
        DemoRepresentativeRole.Hunter => "hunter",
        DemoRepresentativeRole.Shaman => "shaman",
        DemoRepresentativeRole.Scholar => "scholar",
        _ => "commoner"
    };

    /// <summary>某位代表人物在 <paramref name="age"/> 歲時的人像；<paramref name="cultureId"/>＝0 表示沒有文化紀錄。</summary>
    public static DemoPortrait PortraitOf(DemoRepresentative r, int age, int cultureId)
    {
        ArgumentNullException.ThrowIfNull(r);
        var female = r.Sex == DemoTempSex.Female;
        // 文化決定膚色與髮色的基調，個人上下差一階。
        var baseSkin = cultureId == 0 ? 2 : (int)(Hash(cultureId, 10) % 5);
        var skin = Math.Clamp(baseSkin + (int)(Hash(r.Id, 11) % 3) - 1, 0, 4);
        var baseHair = cultureId == 0 ? 1 : (int)(Hash(cultureId, 12) % 5);
        var hairColor = age >= TempOldAge ? 5 : (Hash(r.Id, 13) % 4 == 0 ? (int)(Hash(r.Id, 14) % 5) : baseHair);
        var beard = female ? 0 : (int)(Hash(r.Id, 15) % 4);
        return new DemoPortrait(female,
            1 + (int)(Hash(r.Id, 16) % FacesPerSex),
            1 + (int)(Hash(r.Id, 17) % HairsPerSex),
            beard,
            age >= TempOldAge ? 2 : age >= TempMiddleAge ? 1 : 0,
            skin, hairColor, ClothesOf(r.Role), cultureId);
    }
}
