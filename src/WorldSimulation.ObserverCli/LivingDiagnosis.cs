using System.Globalization;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.ObserverCli;

/// <summary>
/// 第 08-1 章：普通世界（人類採集）人口診斷。逐年列出世界與各聚落的人口結構、伴侶、採收／需求／勞力上限、
/// 可及來源供應、無缺糧週比例（受孕條件）與死因，用來找出人口成長不起來的環節。只讀世界，不改規則。
/// 用法：--diagnose-living --seed N [--years 100] [--map small|medium|large]（第 07-1 章 c 節：每 10 年列出耗時）
/// </summary>
public static class LivingDiagnosis
{
    public static int Run(IReadOnlyList<string> args, TextWriter output, int daysPerYear)
    {
        ulong seed = 1;
        var years = 100;
        var size = DemoMapSize.Small;
        for (var i = 0; i + 1 < args.Count; i += 2)
        {
            if (args[i] == "--seed" && ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var s)) seed = s;
            else if (args[i] == "--years" && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1 and <= 1000) years = y;
            else if (args[i] == "--map" && Enum.TryParse<DemoMapSize>(args[i + 1], ignoreCase: true, out var m) && Enum.IsDefined(m)) size = m;
            else
            {
                output.WriteLine("用法：--diagnose-living --seed N [--years 1～1000] [--map small|medium|large]");
                return 2;
            }
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(seed, null, size), DemoWeeklyTempConfig.LivingForagingSample);
        output.WriteLine($"=== 普通世界人口診斷｜種子 {seed}｜{years} 年｜地圖 {DemoMapSizes.Name(size)}（{world.Map.Columns}×{world.Map.Rows}）｜{world.MapCapacity.Describe()} ===");
        var lap = clock.Elapsed;
        var lapYear = 0;
        var seen = 0;
        for (var year = 1; year <= years; year++)
        {
            for (var d = 0; d < daysPerYear; d++)
                world.AdvanceOneDay();
            var weeks = world.WeekReports().Skip(seen).ToList();
            seen = world.WeekReports().Count;
            var events = weeks.SelectMany(w => w.Events).ToList();
            int Count(DemoSettlementEventKind kind, string? cause = null)
                => events.Where(e => e.Kind == kind && (cause is null || e.Cause.Contains(cause, StringComparison.Ordinal))).Sum(e => (int)e.Count);
            output.WriteLine($"Y{year} 人{world.WorldPopulation} 生{Count(DemoSettlementEventKind.Birth)} 餓{Count(DemoSettlementEventKind.StarvationDeath)}"
                + $" 途餓{Count(DemoSettlementEventKind.MigrantStarvationDeath)} 老{Count(DemoSettlementEventKind.NaturalDeath)}"
                + $" 缺糧走{Count(DemoSettlementEventKind.Emigration, "缺糧")} 擁擠走{Count(DemoSettlementEventKind.Emigration, "接近附近來源上限")}"
                + $" 婚走{Count(DemoSettlementEventKind.Emigration, "婚嫁")} 建村{events.Count(e => e.Kind == DemoSettlementEventKind.SettlementFounded)}"
                + $" 遺跡{events.Count(e => e.Kind == DemoSettlementEventKind.SettlementRuined)}");
            if (year % 10 != 0 && year != 1)
                continue;
            // 第 07-1 章 c 節：效能（這段年數的牆鐘時間、平均每年秒數）。
            var now = clock.Elapsed;
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"  耗時：到第 {year} 年共 {now.TotalSeconds:F1} 秒（這段每年 {(now - lap).TotalSeconds / (year - lapYear):F2} 秒）｜週報 {world.WeekReports().Count} 份｜事件 {world.WeekReports().Sum(w => w.Events.Count)} 筆"));
            lap = now;
            lapYear = year;
            var day = world.Snapshot().DaysElapsed;
            var births = weeks.Sum(w => w.Settlements.Sum(l => l.Births) + w.Migrations.Sum(m => m.Births));
            var starved = weeks.Sum(w => w.Settlements.Sum(l => l.StarvationDeaths) + w.Migrations.Sum(m => m.Deaths));
            var natural = weeks.Sum(w => w.Settlements.Sum(l => l.NaturalDeaths) + w.Migrations.Sum(m => m.NaturalDeaths));
            var emig = weeks.Sum(w => w.Settlements.Sum(l => l.Emigrants));
            // 第 08-3 章：每 10 年一行彙整（註記長度有限，逐聚落明細只在最後一年列出）。
            long shortVillages = 0, labourShort = 0, sourceShort = 0, fertileTotal = 0, couplesTotal = 0, pregnantTotal = 0, villages = 0;
            foreach (var s in world.Sites.Settlements.Where(s => world.SettlementPopulation(s.Id) > 0))
            {
                // 第 07-1 章：群體人口統計。
                var st = world.PopulationStats(s.Id) ?? new DemoCohortStats(0, 0, 0, 0, 0, 0, 0, 0);
                var adults = st.Adults + st.Elders;
                var fertileWomen = st.FertileWomen;
                var couples = st.Couples;
                var pregnant = st.Pregnant;
                var ledgers = weeks.SelectMany(w => w.Settlements).Where(l => l.SettlementId == s.Id).ToList();
                var n = Math.Max(1, ledgers.Count);
                var fed = ledgers.Count(l => l.FoodShortage == 0 && l.HungerDebtClosing == 0);
                var waterShort = ledgers.Count(l => l.WaterShortage > 0);
                var harvest = ledgers.Sum(l => l.FoodProduced) / n;
                var demand = ledgers.Sum(l => l.FoodConsumed + l.FoodShortage) / n;
                var labour = ledgers.Sum(l => Math.Max(0, (l.AdultWorkersOpening < 0 ? l.PopulationOpening : l.AdultWorkersOpening) - l.WaterCarriers) * l.YieldPerWorker(world.WeeklyConfig)) / n;
                var carriers = ledgers.Sum(l => l.WaterCarriers) / (double)n;
                var supply = weeks.Count == 0 ? 0 : weeks[^1].Sources.Where(x => (x.Shares ?? Array.Empty<DemoSourceShare>()).Any(sh => sh.SettlementId == s.Id)).Sum(x => x.FoodAvailable);
                var food = world.SettlementFood(s.Id);
                var ample = weeks.Count(w => w.Settlements.Any(l => l.SettlementId == s.Id
                    && DemoCrowding.Ample(true, l.FoodShortage, l.FoodConsumed + l.FoodShortage, l.SourceSlack)));
                villages++;
                fertileTotal += fertileWomen;
                couplesTotal += couples;
                pregnantTotal += pregnant;
                if (fed < ledgers.Count / 2)
                {
                    shortVillages++;
                    if (labour < demand) labourShort++;
                    if (supply < demand) sourceShort++;
                }
                // 第 08-4 章：缺糧聚落的成因（每 10 年列出過半週缺糧的聚落，最多 6 個）。
                if (fed < ledgers.Count / 2 && shortVillages <= 6)
                {
                    var cell = world.Map.GetCell(s.Column, s.Row);
                    var reach = weeks.Count == 0 ? new List<DemoSourceWeekLedger>() : weeks[^1].Sources
                        .Where(x => DemoHexFoodCatchment.IsInReach(world.Map.GetCell(x.Column, x.Row), cell,
                            DemoCatchmentSourceKinds.IncludesOrigin(x.Kind))).ToList();
                    var rivals = reach.SelectMany(x => x.Shares ?? Array.Empty<DemoSourceShare>()).Select(x => x.SettlementId)
                        .Where(id => id != s.Id).Distinct().Count();
                    var own = reach.Sum(x => (x.Shares ?? Array.Empty<DemoSourceShare>()).Where(sh => sh.SettlementId == s.Id).Sum(sh => sh.Food));
                    output.WriteLine($"    缺糧 #{s.Id}：人 {st.Total}（兒 {st.Total - adults}）｜本年 生 {ledgers.Sum(l => l.Births)} 遷入 {ledgers.Sum(l => l.Immigrants)} 遷出 {ledgers.Sum(l => l.Emigrants)}"
                        + $"｜可及來源 {reach.Count}（可採 {reach.Sum(x => x.FoodAvailable)}、自取 {own}、共用聚落 {rivals}）｜餘量 {ledgers[^1].SourceSlack}｜存糧 {world.SettlementFood(s.Id)}｜債 {world.SettlementHungerDebt(s.Id)}");
                }
                if (year != years)
                    continue;
                output.WriteLine($"  #{s.Id} 人 {st.Total}（成 {adults}／老 {st.Elders}／伴 {couples}／孕 {pregnant}／單女 {st.UnpairedWomen}／單男 {st.UnpairedMen}）｜採 {harvest}／需 {demand}／勞 {labour}｜可採 {supply}"
                    + $"｜飽 {fed}、寬裕 {ample}／{ledgers.Count} 週");
            }
            output.WriteLine($"  彙整：有人聚落 {villages}，過半週缺糧 {shortVillages}（勞力不足 {labourShort}、來源不足 {sourceShort}）"
                + $"｜育齡婦女 {fertileTotal}、育齡伴侶 {couplesTotal}、懷孕 {pregnantTotal}");
        }
        SaveSizes(world, output);
        return 0;
    }

    /// <summary>第 07-1 章 c 節：存檔大小與各部分佔比（找出存檔變大的來源）。</summary>
    private static void SaveSizes(DemoObserverWorld world, TextWriter output)
    {
        var json = world.ExportTempSave();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var state = doc.RootElement.GetProperty("State");
        long Bytes(System.Text.Json.JsonElement e) => System.Text.Encoding.UTF8.GetByteCount(e.GetRawText());
        var parts = state.EnumerateObject().Select(p => (p.Name, Size: Bytes(p.Value))).OrderByDescending(p => p.Size).Take(6);
        output.WriteLine($"  存檔：共 {System.Text.Encoding.UTF8.GetByteCount(json) / 1024} KB｜" + string.Join("、", parts.Select(p => $"{p.Name} {p.Size / 1024} KB")));
        var fields = new Dictionary<string, long>();
        foreach (var week in state.GetProperty("WeekReports").EnumerateArray())
            foreach (var f in week.EnumerateObject())
                fields[f.Name] = fields.GetValueOrDefault(f.Name) + Bytes(f.Value);
        output.WriteLine("  週報欄位：" + string.Join("、", fields.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Key} {kv.Value / 1024} KB")));
    }
}
