namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #113 TEMP playable forest/river scenario. Source budgets come from the embedded
/// config/demo/terrain-food-temp.json. Existing terrain is a placeholder, so explicit
/// main-river tiles are picked deterministically from some river-bank-like water cells.
/// Ordinary water tiles without this explicit marker never generate food.
/// </summary>
public static class DemoForagingWorld
{
    public static DemoCatchmentTempSettings Settings { get; } = LoadSettings();

    // 第 02 章 b 節：TEMP 示範設定與正式設定一起編入（WorldSimulation.ConfigDemo/），納入 TEMP 設定雜湊。
    private static DemoCatchmentTempSettings LoadSettings()
        => DemoCatchmentTempSettings.FromJson(Config.OfficialConfig.ReadDemoText("terrain-food-temp.json"));

    /// <summary>
    /// 第 06 章 a 節：主水道格改讀地圖的正式主水道（連續佔格水域序列）；不再用顯示用的挑格規則。
    /// 其他水域是一般水域。
    /// </summary>
    public static IReadOnlyList<DemoCellRef> MainRiverTiles(DemoHexWorld map)
    {
        ArgumentNullException.ThrowIfNull(map);
        return map.MainWaterway;
    }

    public static IReadOnlyList<DemoCatchmentFoodSource> Sources(DemoHexWorld map, DemoWorldSites? sites = null)
        => Settings.BuildPotentialSources(map, MainRiverTiles(map),
            sites?.Settlements.Select(s => new DemoCellRef(s.Column, s.Row)).ToArray());

    /// <summary>
    /// Food harvest is a one-week, one-source budget. No local farm and no source-specific
    /// accumulated stores in this TEMP mode: unharvested forage is left in nature, and only
    /// gathered food is counted in settlement stocks. Other scenarios retain old resources.
    /// </summary>
    public static DemoCatchmentFoodResult Allocate(
        DemoHexWorld map, DemoWorldSites sites,
        IReadOnlyDictionary<int, long> openingPopulation,
        DemoWeeklyTempConfig weekly,
        IReadOnlyDictionary<int, long>? adultWorkers = null)
    {
        if (!weekly.SharedCatchmentEnabled)
            throw new InvalidOperationException("Not a shared catchment world.");
        var claims = sites.Settlements.OrderBy(s => s.Id).Select(s =>
            new DemoCatchmentFoodClaim(s.Id, s.Column, s.Row,
                checked(openingPopulation[s.Id] * weekly.FoodPerPersonPerWeek),
                checked((adultWorkers?.GetValueOrDefault(s.Id) ?? openingPopulation[s.Id]) * weekly.YieldPerWorkerPerWeek)));
        return DemoHexFoodCatchment.Allocate(map, Sources(map, sites), claims);
    }
}
