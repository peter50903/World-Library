using System.Globalization;

namespace WorldSimulation.Core.World.Demo;

/// <summary>第 08-10e 章：史書開頭要記的開局狀態（世界開始推進之前先取）。</summary>
public sealed record DemoSagaOpening(long Population, int Settlements);

/// <summary>
/// 第 08-10e 章：文字史書——把一個世界到目前為止的歷史寫成一頁讀得下去的文字（序、每十年一卷、終章）。
/// 之後所有改動的驗收標準：這一頁有沒有變好看。
/// 只讀編年史（<see cref="DemoWorldChronicle"/>）與已提交的世界狀態，不改世界、不抽亂數；同一世界讀檔前後結果相同。
/// 口吻可以帶史詩語氣，但每一句都只陳述已經發生的事（卷名由年代定性對照而來，不添加原因或評語）。
/// </summary>
public static class DemoSaga
{
    /// <summary>【TEMP】每卷幾年；終章列幾個最大的聚落。</summary>
    public const int YearsPerVolume = 10, EndingSettlements = 3;

    /// <summary>世界開始推進之前呼叫：開局的人口與聚落數。</summary>
    public static DemoSagaOpening Opening(DemoObserverWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        return new DemoSagaOpening(world.WorldPopulation, world.Sites.Settlements.Count(s => !s.IsRuin));
    }

    /// <summary>卷名：年代定性（<see cref="DemoWorldChronicle.DecadeCharacter"/>）對照的四字標題。</summary>
    public static string VolumeTitle(string character) => character switch
    {
        "戰亂的年代" => "烽火連天",
        "衰退的年代" => "荒煙蔓草",
        "饑荒的年代" => "大地無糧",
        "擴張的年代" => "開枝散葉",
        "創新的年代" => "薪火初燃",
        "轉折的年代" => "天地更新",
        "平靜的年代" => "歲月無聲",
        _ => "休養生息"
    };

    /// <summary>整部史書（每個元素一行）。<paramref name="opening"/> 沒有（例：讀檔的世界）就不寫開局人數。</summary>
    public static IReadOnlyList<string> Write(DemoObserverWorld world, DemoSagaOpening? opening = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        static string I(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        var years = DemoWorldChronicle.Build(world);
        var lines = new List<string>
        {
            opening is null
                ? "序　這是一個世界留下的紀錄。"
                : I($"序　太初，{opening.Population} 人分居 {opening.Settlements} 處聚落，世上還沒有任何紀錄。")
        };

        for (var from = 0; from < years.Count; from += YearsPerVolume)
        {
            var volume = years.Skip(from).Take(YearsPerVolume).ToList();
            var character = DemoWorldChronicle.DecadeCharacter(volume);
            lines.Add("");
            lines.Add(I($"卷 {from / YearsPerVolume + 1}　第 {volume[0].Year}～{volume[^1].Year} 年　{VolumeTitle(character)}（{character}）"));
            var told = 0;
            foreach (var year in volume)
                foreach (var entry in year.Entries.Where(e => e.IsWorld))
                {
                    lines.Add(I($"　第 {year.Year} 年，{Sentence(entry.Text)}"));
                    told++;
                }
            if (told == 0)
                lines.Add("　這些年沒有驚動世界的大事。");
            var local = string.Join("；", volume.Select(y => (y.Year, Tally: DemoWorldChronicle.LocalTally(y)))
                .Where(x => x.Tally.Length > 0).Select(x => I($"第 {x.Year} 年 {x.Tally}")));
            if (local.Length > 0)
                lines.Add("　各地：" + local + "。");
            lines.Add(I($"　這些年裡 {volume.Sum(y => y.Births)} 人出生，{volume.Sum(y => y.NaturalDeaths)} 人壽終。"));
        }

        lines.Add("");
        lines.AddRange(Ending(world, years.Count));
        return lines;
    }

    /// <summary>終章：最後一年的世界——人口與聚落、最大的幾個聚落與領袖、各勢力。</summary>
    private static IEnumerable<string> Ending(DemoObserverWorld world, int year)
    {
        static string I(FormattableString s) => s.ToString(CultureInfo.InvariantCulture);
        var active = world.Sites.Settlements.Where(s => !s.IsRuin).ToList();
        yield return I($"終章　第 {year} 年的世界");
        yield return I($"　世上有 {world.WorldPopulation} 人、{active.Count} 處有人煙的聚落、{world.Sites.Settlements.Count(s => s.IsRuin)} 處遺跡。");
        if (world.WeeklyConfig.EraName is not null)
            yield return "　時代：" + DemoKnowledge.EraName(world.Era) + "。";

        string Titled(DemoSettlement s) => DemoSettlementHistory.TitledName(s, world.ChronicleOf(s.Id).Tier);
        string LeaderOf(DemoSettlement s)
            => world.RepresentativesOf(s.Id).FirstOrDefault(r => r.Role == DemoRepresentativeRole.Leader) is { } leader ? "，領袖" + leader.Name : "";
        var largest = active.OrderByDescending(s => world.SettlementPopulation(s.Id)).ThenBy(s => s.Id).Take(EndingSettlements)
            .Select(s => I($"{Titled(s)}（{world.SettlementPopulation(s.Id)} 人{LeaderOf(s)}）")).ToList();
        if (largest.Count > 0)
            yield return "　最大的聚落：" + string.Join("、", largest) + "。";

        var territory = world.Territory();
        var realms = active.Select(s => (Site: s, Vassals: territory.VassalsOf(s.Id).Count)).Where(x => x.Vassals > 0)
            .OrderByDescending(x => x.Vassals).ThenBy(x => x.Site.Id)
            .Select(x => I($"{Titled(x.Site)}統領 {x.Vassals} 個聚落")).ToList();
        yield return realms.Count > 0 ? "　勢力：" + string.Join("；", realms) + "。" : "　世上還沒有統領其他聚落的勢力。";
    }

    /// <summary>編年史條目收成一句：去頭尾空白，補句號。</summary>
    private static string Sentence(string text)
    {
        var t = text.Trim();
        return t.EndsWith('。') || t.EndsWith('！') || t.EndsWith('？') ? t : t + "。";
    }
}
