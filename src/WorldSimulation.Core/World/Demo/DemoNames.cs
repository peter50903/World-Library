using System.Text.Json;
using WorldSimulation.Core.Config;
using WorldSimulation.Core.Entity;
using WorldSimulation.Core.Rng;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 07-1 章 b 節：TEMP 命名詞素（config/demo/names-temp.json）。</summary>
public sealed record DemoNameTempSettings(
    int SchemaVersion,
    string Status,
    int MaxLength,
    IReadOnlyList<string> FemaleGiven,
    IReadOnlyList<string> MaleGiven,
    IReadOnlyList<string> Family,
    IReadOnlyList<string> PlacePrefix,
    IReadOnlyList<string> ForestSuffix,
    IReadOnlyList<string> GrasslandSuffix,
    IReadOnlyList<string> CoastSuffix,
    IReadOnlyList<string> RiverSuffix,
    IReadOnlyList<string> DaughterPrefix)
{
    public static DemoNameTempSettings Current { get; } = Load();

    private static DemoNameTempSettings Load()
    {
        var s = JsonSerializer.Deserialize<DemoNameTempSettings>(OfficialConfig.ReadDemoText("names-temp.json"))
            ?? throw new InvalidOperationException("config/demo/names-temp.json 讀不到。");
        var lists = new[] { s.FemaleGiven, s.MaleGiven, s.Family, s.PlacePrefix, s.ForestSuffix, s.GrasslandSuffix, s.CoastSuffix, s.RiverSuffix, s.DaughterPrefix };
        if (s.SchemaVersion != 1 || s.Status != "TEMP" || s.MaxLength < 2 || lists.Any(l => l is null || l.Count == 0 || l.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidOperationException("config/demo/names-temp.json 內容不合法。");
        return s;
    }
}

/// <summary>
/// 第 07-1 章 b 節：西式奇幻音譯命名（使用者 2026-10-22：每個名字不超過六個字）。
/// 只組合設定檔的詞素（總綱第 4 節第 6 條），以實體局部亂數抽取（同種子同名）：
/// 開局聚落用世界種子 ECOLOGY_INIT 流、建立的新聚落與代表人物用歷史種子 CHARACTER 流。名字只是標籤，身分以實體 ID 為準。
/// </summary>
public sealed class DemoNames
{
    public const string Namespace = "TEMP-NAMES-v1";
    private readonly RngRegistry _registry;

    public DemoNames(RngRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    private static DemoNameTempSettings S => DemoNameTempSettings.Current;

    /// <summary>名字長度（不含間隔號）。</summary>
    public static int Length(string name) => name.Replace("·", string.Empty, StringComparison.Ordinal).Length;

    private ulong Draw(string stream, long entityId, ulong index)
        => EntityLocalRandom.Create(_registry, EntityId.From((ulong)entityId), stream, Namespace).GetUInt64(index);

    private static string Pick(IReadOnlyList<string> list, ulong raw) => list[(int)(raw % (ulong)list.Count)];

    /// <summary>地名後綴：臨海→海岸；臨河→河流；聚落格是森林→森林；其他→草原。</summary>
    private static IReadOnlyList<string> SuffixFor(DemoHexWorld map, DemoSettlement settlement)
    {
        var cell = map.GetCell(settlement.Column, settlement.Row);
        var neighbours = DemoRouteNetwork.Directions
            .Select(d => map.AtAxial(cell.AxialQ + d.Dq, cell.AxialR + d.Dr))
            .Where(c => c is not null).Select(c => c!).ToList();
        if (neighbours.Any(c => c.Sea)) return S.CoastSuffix;
        if (neighbours.Any(c => c.MainWaterway)) return S.RiverSuffix;
        return cell.Terrain == DemoTerrain.Forest ? S.ForestSuffix : S.GrasslandSuffix;
    }

    /// <summary>
    /// 聚落名（不超過 TEMP 長度、不與 <paramref name="taken"/> 重複）。開局聚落：前綴＋地形後綴；
    /// 新建聚落：一半機率沿用母村前綴加方位字（例：新索倫），否則同開局規則。
    /// </summary>
    public string Settlement(DemoHexWorld map, DemoSettlement settlement, string? motherPrefix, ISet<string> taken, bool opening)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(settlement);
        ArgumentNullException.ThrowIfNull(taken);
        var stream = opening ? RngStreams.EcologyInit : RngStreams.Character;
        var suffixes = SuffixFor(map, settlement);
        for (ulong attempt = 0; attempt < 64; attempt++)
        {
            var raw = Draw(stream, settlement.Id, 1 + attempt * 4);
            string name;
            if (!opening && motherPrefix is not null && raw % 2 == 0)
                name = Pick(S.DaughterPrefix, Draw(stream, settlement.Id, 2 + attempt * 4)) + motherPrefix;
            else
                name = Pick(S.PlacePrefix, Draw(stream, settlement.Id, 3 + attempt * 4)) + Pick(suffixes, Draw(stream, settlement.Id, 4 + attempt * 4));
            if (Length(name) <= S.MaxLength && !taken.Contains(name))
                return name;
        }
        return $"聚落{settlement.Id}";
    }

    /// <summary>聚落名的前綴部分（給新村沿用）；不是由前綴組成的名字回傳 null。</summary>
    public static string? PrefixOf(string settlementName)
        => S.PlacePrefix.FirstOrDefault(p => settlementName.StartsWith(p, StringComparison.Ordinal))
            ?? S.PlacePrefix.FirstOrDefault(p => settlementName.Length > 1 && settlementName[1..].StartsWith(p, StringComparison.Ordinal));

    /// <summary>人名「名·姓」（整體不超過 TEMP 長度，不含間隔號）。</summary>
    public string Person(long entityId, DemoTempSex sex)
    {
        var given = sex == DemoTempSex.Female ? S.FemaleGiven : S.MaleGiven;
        for (ulong attempt = 0; attempt < 32; attempt++)
        {
            var name = Pick(given, Draw(RngStreams.Character, entityId, 1 + attempt * 2)) + "·"
                + Pick(S.Family, Draw(RngStreams.Character, entityId, 2 + attempt * 2));
            if (Length(name) <= S.MaxLength)
                return name;
        }
        return $"無名者{entityId}";
    }
}
