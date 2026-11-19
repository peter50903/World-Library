namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 12-2c 節：一個聚落對付怪物的人力。戰鬥人員（士兵）有等級；其餘成年人是非戰鬥人員（農夫、漁夫、商人…）。
/// 第 12-2d 節（使用者 2026-11-17）：冒險者移除，戰鬥人員只有士兵。
/// </summary>
public sealed record DemoCombatForce(long Soldiers, int SoldierLevel, long Civilians)
{
    public static DemoCombatForce None { get; } = new(0, 0, 0);

    public long Fighters => Soldiers;

    /// <summary>戰鬥人員的等級（沒有戰鬥人員＝0）。</summary>
    public int BestLevel => Soldiers > 0 ? SoldierLevel : 0;
}

/// <summary>
/// 第 12-2c 節【TEMP 規則】討伐怪物分戰鬥人員與非戰鬥人員（使用者 2026-11-08：只有戰鬥職業跟怪物戰鬥可以獲得平等或高於怪物的戰鬥優勢，取決於雙方的等級）。
/// <list type="bullet">
/// <item><b>士兵</b>：鎮以上才有駐軍，成年人的 <see cref="TempTownSoldierPermille"/>‰（城以上 <see cref="TempCitySoldierPermille"/>‰）。
///   等級＝1＋弓術、武器鍛造、皮甲、基礎附魔各 +1。</item>
/// <item><b>非戰鬥人員</b>：每人戰力固定 <see cref="TempCivilianStrengthPercent"/>%（等級 1 的怪物是 100%），所以再多人也只能拖住、壓制。</item>
/// <item><b>怪物等級</b>＝地下城層數（1～3 級；第 12-2f 節：原本層數＋1，三層地下城的 4 級怪物要三項裝備技術的士兵，百年內沒有勢力打得贏）。每隻怪物的戰力＝等級；每名戰鬥人員的戰力＝自己的等級——等級相同就勢均力敵，高於怪物就佔優勢。</item>
/// <item><b>交戰</b>：我方戰力占雙方的比例決定擊殺（交戰怪物數 × 比例 × 0.6 × 運氣）與我方死亡（交戰怪物數 × 怪物佔比 × 怪物等級 × 0.5 × 運氣）。
///   沒有等級不低於怪物的戰鬥人員時，最多壓到壓制下限（第 12-2b 節）；有的話才能殺光（地下城沉寂）。</item>
/// </list>
/// </summary>
public static class DemoCombat
{
    /// <summary>【TEMP】士兵占成年人的千分比。</summary>
    public const int TempTownSoldierPermille = 30, TempCitySoldierPermille = 50;

    /// <summary>【TEMP】非戰鬥人員每人戰力（等級 1 的百分比）；守村時每隻怪物最多牽制幾名非戰鬥人員。</summary>
    public const int TempCivilianStrengthPercent = 20, TempCiviliansPerMonster = 3;

    /// <summary>士兵的裝備技術（各 +1 級）。</summary>
    public static readonly string[] GearTechs = { "TECH_ARCHERY", "TECH_WEAPON_SMITHING", "TECH_LEATHER_ARMOR", "TECH_BASIC_ENCHANTING" };

    public static int MonsterLevel(DemoDungeon dungeon) => dungeon.Layers; // 第 12-2f 節（原本層數＋1）

    /// <summary>一個聚落的人力：<paramref name="knows"/>＝會不會某項技術（TechID）。</summary>
    public static DemoCombatForce Force(long adults, DemoSettlementTier tier, Func<string, bool> knows, bool hero = false, int extraLevels = 0)
    {
        ArgumentNullException.ThrowIfNull(knows);
        if (adults <= 0)
            return DemoCombatForce.None;
        var soldiers = tier >= DemoSettlementTier.City ? adults * TempCitySoldierPermille / 1000
            : tier >= DemoSettlementTier.Town ? adults * TempTownSoldierPermille / 1000 : 0;
        var soldierLevel = soldiers > 0 ? 1 + GearTechs.Count(knows) + (hero ? 1 : 0) + extraLevels : 0; // 第 08-10i 章：有英雄帶隊 +1；第 08-6c 章：奇蹟時代 +1
        return new DemoCombatForce(soldiers, soldierLevel, Math.Max(0, adults - soldiers));
    }

    /// <summary>
    /// 一次交戰：<paramref name="engaged"/> 隻 <paramref name="monsterLevel"/> 級怪物對上 <paramref name="force"/>。
    /// <paramref name="defending"/>＝守村（戰鬥與非戰鬥人員都上，非戰鬥人員每隻怪物最多牽制 3 人）；否則是討伐（只派戰鬥人員，沒有才派非戰鬥人員組民兵）。
    /// <paramref name="luck"/>＝0～99 的運氣。回傳擊殺數與我方死亡。
    /// 第 08-34 章：<paramref name="defensePercent"/>＝守村時城牆、塔樓讓我方戰力增加幾 %（見 <see cref="DemoDefense"/>）。
    /// </summary>
    public static (int Kills, long Deaths) Clash(int engaged, int monsterLevel, DemoCombatForce force, bool defending, int luck, int defensePercent = 0)
    {
        ArgumentNullException.ThrowIfNull(force);
        if (engaged <= 0)
            return (0, 0);
        var fighters = force.Soldiers * force.SoldierLevel * 100;
        var civilians = defending || force.Fighters == 0 ? Math.Min(force.Civilians, (long)engaged * TempCiviliansPerMonster) : 0;
        var ours = fighters + civilians * TempCivilianStrengthPercent;
        if (defending && defensePercent > 0)
            ours = ours * (100 + defensePercent) / 100; // 第 08-34 章：據城牆、塔樓而守
        var theirs = (long)engaged * monsterLevel * 100;
        if (ours <= 0)
            return (0, Math.Min(force.Civilians, (long)engaged * monsterLevel / 2));
        var luckPercent = 75 + luck / 2; // 75～124
        // 擊殺的小數部分照運氣進位（非戰鬥人員偶爾也殺得到一隻）。
        var den = (ours + theirs) * 100 * 100;
        var kills = (int)Math.Min(engaged, ((long)engaged * ours * 60 * luckPercent + den * (luck * 37 % 100) / 100) / den);
        var party = defending ? force.Fighters + force.Civilians : force.Fighters > 0 ? force.Fighters : civilians;
        var deaths = Math.Min(party, (long)engaged * theirs * monsterLevel * 50 * luckPercent / ((ours + theirs) * 100 * 100));
        return (kills, deaths);
    }

    /// <summary>能不能殺光（有等級不低於怪物的戰鬥人員）。</summary>
    public static bool CanClear(DemoCombatForce force, int monsterLevel) => force.BestLevel >= monsterLevel;

    /// <summary>聚落詳情的人力說明。</summary>
    public static string Describe(DemoCombatForce force)
    {
        ArgumentNullException.ThrowIfNull(force);
        var parts = new List<string>();
        if (force.Soldiers > 0)
            parts.Add($"士兵 {force.Soldiers} 人（{force.SoldierLevel} 級）");
        return parts.Count == 0
            ? $"戰鬥人員：無｜非戰鬥人員 {force.Civilians} 人（只能壓制怪物）"
            : $"戰鬥人員：{string.Join("、", parts)}｜非戰鬥人員 {force.Civilians} 人";
    }
}
