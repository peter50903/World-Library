using System.Globalization;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.ObserverCli;

/// <summary>
/// 第 08-10e 章：文字史書。普通世界（人類採集）跑 N 年，印出一頁歷史（序、每十年一卷、終章）。只讀世界，不改規則。
/// 用法：--saga [--seed N] [--years 100] [--map small|medium|large]
/// </summary>
public static class Saga
{
    private const string Usage = "用法：--saga [--seed N] [--years 1～1000] [--map small|medium|large]";

    public static int Run(IReadOnlyList<string> args, TextWriter output, int daysPerYear)
    {
        ulong seed = 1;
        var years = 100;
        var size = DemoMapSize.Small;
        if (args.Count % 2 != 0)
        {
            output.WriteLine(Usage);
            return 2;
        }
        for (var i = 0; i + 1 < args.Count; i += 2)
        {
            if (args[i] == "--seed" && ulong.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var s))
                seed = s;
            else if (args[i] == "--years" && int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1 and <= 1000)
                years = y;
            else if (args[i] == "--map" && Enum.TryParse<DemoMapSize>(args[i + 1], ignoreCase: true, out var m) && Enum.IsDefined(m))
                size = m;
            else
            {
                output.WriteLine(Usage);
                return 2;
            }
        }

        var world = new DemoObserverWorld(WorldRecipe.ForSeeds(seed, null, size), DemoWeeklyTempConfig.LivingForagingSample);
        var opening = DemoSaga.Opening(world);
        var balance = new BalanceTracker(); // 第 12-2h 章：順便每 10 年取一次人類與怪物的消長（大地圖不必另跑一次）
        var blockedLaneYears = 0; // 第 08-48 章：每年年底被封鎖的航線數加總
        for (var year = 1; year <= years; year++)
        {
            for (var d = 0; d < daysPerYear; d++)
                world.AdvanceOneDay();
            balance.ObserveYearEnd(world, year, year == years);
            blockedLaneYears += world.SeaLanes().Count(l => l.Blocked);
        }
        output.WriteLine($"=== 史書｜種子 {seed}｜{years} 年｜地圖 {DemoMapSizes.Name(size)}（{world.Map.Columns}×{world.Map.Rows}）===");
        foreach (var line in DemoSaga.Write(world, opening))
            output.WriteLine(line);
        output.WriteLine();
        output.WriteLine("人類與怪物消長：" + balance.Line(seed, size));
        output.WriteLine("海與戰爭：" + SeaAndWar(world, blockedLaneYears));
        output.WriteLine("遠征與獸潮：" + Expeditions(world));
        return 0;
    }

    /// <summary>
    /// 第 08-50 章：大地圖驗證用的一行——遠征出發、殺光、撤回與攻打的層數，獸潮出巢與真的打到的次數、攻破幾個聚落。
    /// </summary>
    private static string Expeditions(DemoObserverWorld world)
    {
        var events = world.SettlementEvents();
        var setOut = events.Count(e => e.Kind == DemoSettlementEventKind.ExpeditionSetOut);
        var returned = events.Count(e => e.Kind == DemoSettlementEventKind.ExpeditionReturned);
        var repelled = events.Count(e => e.Kind == DemoSettlementEventKind.ExpeditionReturned && e.Cause.Contains("攻不下", StringComparison.Ordinal));
        var assaults = events.Count(e => e.Kind == DemoSettlementEventKind.MonsterHunt && e.Cause.StartsWith("遠征軍", StringComparison.Ordinal));
        var cleared = events.Count(e => e.Kind == DemoSettlementEventKind.DungeonCleared);
        var waves = events.Count(e => e.Kind == DemoSettlementEventKind.SwarmSetOut);
        var struck = events.Count(e => e.Kind == DemoSettlementEventKind.MonsterSwarm);
        var broken = events.Count(e => e.Kind == DemoSettlementEventKind.MonsterSwarm && e.Cause.Contains("攻破", StringComparison.Ordinal));
        var slain = events.Where(e => e.Kind == DemoSettlementEventKind.MonsterSlain).Sum(e => DemoMonsters.DecodeKills(e.Count).Kills);
        return string.Create(CultureInfo.InvariantCulture,
            $"遠征出發 {setOut} 次、攻打 {assaults} 層、撤回 {returned}（攻不下 {repelled}）｜地下城殺光 {cleared} 次｜獸潮出巢 {waves} 次、打到 {struck} 次、攻破 {broken} 個"
            + $"｜歷年殺死怪物 {slain} 隻");
    }

    /// <summary>
    /// 第 08-48 章：大地圖驗證用的一行——海上地下城、海戰、海怪襲擊沿岸、渡海開拓、港口與航線（封鎖），
    /// 征服戰爭的結局與平均週數、野戰、圍城糧盡、又佔的聚落。
    /// </summary>
    private static string SeaAndWar(DemoObserverWorld world, int blockedLaneYears)
    {
        var events = world.SettlementEvents();
        var map = world.Map;
        var seaDungeons = world.Dungeons.Count(d => DemoDungeons.AtSea(map, d.Cell));
        var openSea = world.Dungeons.Count(d => map.GetCell(d.Cell.Column, d.Cell.Row).Terrain == DemoTerrain.Water);
        var naval = events.Count(e => e.Kind == DemoSettlementEventKind.MonsterHunt && e.Cause.Contains("海戰", StringComparison.Ordinal));
        var sunk = events.Where(e => e.Kind == DemoSettlementEventKind.MonsterSlain && e.Cause.StartsWith("海戰", StringComparison.Ordinal))
            .Sum(e => DemoMonsters.DecodeKills(e.Count).Kills);
        var coastRaids = events.Count(e => e.Kind is DemoSettlementEventKind.MonsterRaid or DemoSettlementEventKind.MonsterSwarm
            && (e.Cause.Contains("從海上", StringComparison.Ordinal) || e.Cause.Contains("海怪潮", StringComparison.Ordinal)));
        var voyages = events.Count(e => e.Kind == DemoSettlementEventKind.Emigration && e.Cause.Contains("渡海", StringComparison.Ordinal));
        var lanes = world.SeaLanes();
        var wars = DemoWarReports.All(events, world.Sites);
        var ended = wars.Where(r => r.EndWeek > 0).ToList();
        int Ends(string text) => ended.Count(r => !r.Won && r.EndText.Contains(text, StringComparison.Ordinal));
        var conquered = ended.Count(r => r.Won && !r.EndText.Contains("議和", StringComparison.Ordinal));
        var ceded = ended.Count(r => r.Won && r.EndText.Contains("議和", StringComparison.Ordinal));
        var routed = Ends("敗退") + Ends("撤兵"); // 第 08-49 章：野戰潰敗、兵力不支撤兵也算敗退
        var white = Ends("白和平");
        var other = ended.Count - conquered - ceded - routed - white;
        var weeks = ended.Count == 0 ? 0 : ended.Average(r => r.EndWeek - r.StartWeek);
        var field = wars.Sum(r => r.Battles.Count(b => b.Field));
        var starved = events.Count(e => e.Kind == DemoSettlementEventKind.Occupied && e.Cause.Contains("城內糧盡", StringComparison.Ordinal));
        var holdings = wars.Sum(r => r.Occupations.Count(o => o.SettlementId != r.TargetId));
        return string.Create(CultureInfo.InvariantCulture,
            $"海上地下城 {seaDungeons}（外海 {openSea}）｜海戰 {naval} 場、擊沉 {sunk} 隻｜海怪襲擊沿岸 {coastRaids} 次｜渡海開拓 {voyages} 隊"
            + $"｜年末港口 {world.Ports().Count}、航線 {lanes.Count}（封鎖 {lanes.Count(l => l.Blocked)}；歷年年底封鎖合計 {blockedLaneYears}）"
            + $"｜征服戰爭 {wars.Count} 場：征服 {conquered}、議和割讓 {ceded}、敗退 {routed}、白和平 {white}、其他 {other}、還在打 {wars.Count - ended.Count}，平均 {weeks:0} 週"
            + $"｜野戰 {field} 場、圍城糧盡 {starved} 次、又佔 {holdings} 座");
    }
}
