namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// 第 08-19c 章：世界法則（參考 WorldBox 的世界法則）——玩家可以隨時開關幾條會改變世界走向的規則。
/// 預設全開（與加入世界法則前完全相同）；關掉後從下一個結算週起生效，存檔記下目前的法則。
/// </summary>
public sealed record DemoWorldLaws(bool Disasters = true, bool Monsters = true, bool Rebellions = true, bool BeastAttacks = true, bool Wars = true)
{
    public static DemoWorldLaws Default { get; } = new();

    public bool IsDefault => this == Default;

    /// <summary>介面列出的每一條法則：代碼、名稱、說明、目前是否開啟。</summary>
    public IReadOnlyList<(string Key, string Name, string Description, bool On)> Entries => new[]
    {
        ("disasters", "天災", "地震、龍捲風、隕石與洪水會襲擊聚落（關閉後不再發生；熱浪與野火照舊）", Disasters),
        ("monsters", "地下城與怪物", "會出現新的地下城，怪物會襲擊聚落、聚落會出兵討伐（關閉後不再出現新的地下城，怪物也不再出擊）", Monsters),
        ("rebellions", "叛亂與獨立", "不滿的下級會加入獨立派系、請願或掀起獨立戰爭（關閉後不再有新的派系與鬥爭；進行中的照常結束）", Rebellions),
        ("beasts", "野獸襲擊", "野獸會在路上襲擊遷徙隊伍", BeastAttacks),
        ("wars", "王國戰爭", "征服時代的王國會向鄰近勢力宣戰、征服聚落（關閉後不再宣戰；進行中的戰爭照常打完）", Wars)
    };

    public DemoWorldLaws With(string key, bool on) => key switch
    {
        "disasters" => this with { Disasters = on },
        "monsters" => this with { Monsters = on },
        "rebellions" => this with { Rebellions = on },
        "beasts" => this with { BeastAttacks = on },
        "wars" => this with { Wars = on }, // 第 08-19e 章
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "沒有這條世界法則")
    };
}
