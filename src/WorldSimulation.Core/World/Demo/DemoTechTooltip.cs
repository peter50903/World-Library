using System.Text.RegularExpressions;

namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 試玩回饋 2026-11-03 之 2：技術圖示的滑鼠說明（給玩家看）。
/// 說明欄去掉開發用的字句與數字（第 08-11 章：玩家不看數值）；「目前作用」只寫世界裡真的會發生的事，
/// 對照程式現有的效果逐項撰寫——效果改了要一起改這張表（測試會檢查表內的技術都存在）。
/// </summary>
public static partial class DemoTechTooltip
{
    /// <summary>已在世界中生效的技術作用（玩家用語、不含數字）。</summary>
    public static readonly IReadOnlyDictionary<string, string> Effects = new Dictionary<string, string>
    {
        ["TECH_FORAGING"] = "採集野外的食物，是最早的生計",
        ["TECH_FISHING"] = "鄰水的聚落可以捕魚",
        ["TECH_FIRE"] = "陶藝等需要火的知識都從這裡開始",
        ["TECH_STONE_TOOLS"] = "工具改良的起點",
        ["TECH_CROP_CULTIVATION"] = "聚落開始開墾附近的草原成為農田，農耕成為主要糧食；出現農夫代表",
        ["TECH_IRRIGATION"] = "農田收成增加",
        ["TECH_CROP_ROTATION"] = "農田收成增加",
        ["TECH_SOIL_MANAGEMENT"] = "農田收成增加",
        ["TECH_IMPROVED_STONE_TOOLS"] = "工具更好用，採集與耕作的收穫增加",
        ["TECH_BASIC_FORGING"] = "鐵製工具讓收穫再增加；出現鐵匠代表，可建鐵匠鋪",
        ["TECH_HEAT_TREATMENT"] = "工具更耐用，收穫再增加",
        ["TECH_BASIC_CARPENTRY"] = "出現木匠代表，可建木工坊；車輛知識的前提",
        ["TECH_HUNTING"] = "獵人從附近獵取野獸；獵物多的地方，野外的收穫增加；附近的狼群被驅散",
        ["TECH_ARCHERY"] = "出現獵人代表，可建獵屋；從這裡出發的遷徙隊伍較少遭狼群襲擊",
        ["TECH_NET_FISHING"] = "河流與水域的漁獲增加",
        ["TECH_WELL_DIGGING"] = "不臨水的聚落可以掘井，不必遠途挑水；開拓隊可以到離水較遠的地方建村",
        ["TECH_MILLING"] = "有農田的聚落研磨穀物（農夫代表可建磨坊）",
        ["TECH_SHAMANISM"] = "出現巫祝代表，可建祭壇",
        ["TECH_SCRIBAL_METHOD"] = "出現學者代表，可建抄寫所，所有研究都變快",
        ["TECH_ANCESTOR_RITES"] = "聚落創立第一個信仰，信仰開始在世界上流傳",
        ["TECH_CURRENCY"] = "村莊可以建市集（升為城鎮所需的設施之一）",
        ["TECH_POTTERY_FIRING"] = "村莊可以建陶窯（升為城鎮所需的設施之一）",
        ["TECH_DEFENSIVE_WORKS"] = "城鎮可以建城牆（升為城市所需的設施之一）",
        ["TECH_ORGANIZED_RELIGION"] = "城鎮可以建神殿（升為城市所需的設施之一）",
        ["TECH_FORMAL_SCHOOLING"] = "城鎮可以建學校（升為城市所需的設施之一）",
        ["TECH_CODIFIED_LAW"] = "城鎮可以建市政廳（升為城市所需的設施之一）",
        ["TECH_KINGSHIP"] = "城市升為首都所需"
    };

    /// <summary>名稱、說明、目前作用、怎麼學得快、前置（多行；不含習得年份與進度，由畫面接在後面）。</summary>
    public static string Text(DemoTechNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var lines = new List<string>
        {
            $"{node.Name}（{DemoPlayerDetail.DomainName(node.Domain)}{(node.Tier > 0 ? $"，第 {node.Tier} 階" : "，開局知識")}）"
        };
        if (Effects.TryGetValue(node.Id, out var effect))
            lines.Add("目前作用：" + effect);
        else
        {
            // 還沒有作用的技術：設定檔說明當「用途」（說明是設計備註，已有作用的技術以作用為準）。
            if (Summary(node.Description) is { Length: > 0 } summary)
                lines.Add("用途：" + summary);
            lines.Add("目前作用：還沒有直接影響，之後的時代才會用到");
        }
        if (!string.IsNullOrWhiteSpace(node.PracticeCondition))
            lines.Add("學得快的做法：" + node.PracticeCondition.Replace("真實", "", StringComparison.Ordinal));
        if (node.Parents.Length > 0)
            lines.Add("前置：" + string.Join("、", node.Parents.Select(p => DemoTechCatalog.Get(p).Name)));
        return string.Join("\n", lines);
    }

    /// <summary>設定檔說明欄 → 玩家可讀的一句：去掉章節括號與 TEMP，丟掉含數字百分比、從業者、研究時間等開發用的分句。</summary>
    public static string Summary(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return "";
        var text = ChapterNote().Replace(description, "").Replace("【TEMP】", "", StringComparison.Ordinal);
        var parts = text.Split('；', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !DevWords.Any(w => p.Contains(w, StringComparison.Ordinal)))
            .Select(p => p.StartsWith("開局知識：", StringComparison.Ordinal) ? p["開局知識：".Length..] : p)
            .Where(p => p.Length > 0);
        return string.Join("；", parts);
    }

    private static readonly string[] DevWords =
    {
        "從業者", "研究時間", "%", "％", "專家", "條件", "前置", "TEMP", "第 ", "真實", "不能", "仍需", "根技術", "抽", "不保證", "不生成", "合法", "希望時代", "階梯"
    };

    [GeneratedRegex("（[^）]*(章|節|08-|TEMP)[^）]*）")]
    private static partial Regex ChapterNote();
}
