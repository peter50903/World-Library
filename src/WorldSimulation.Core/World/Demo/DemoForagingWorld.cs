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

    /// <summary>第 07 章 c 節：聚落所在格不是自然來源；遺跡格恢復。</summary>
    public static IReadOnlyList<DemoCatchmentFoodSource> Sources(DemoHexWorld map, DemoWorldSites? sites = null)
        => Sources(map, sites?.OccupiedCells());

    /// <summary>
    /// 第 08 章 a 節：農田格不再提供野採。第 08-6 章 c 節：農田格改為農田來源（同一格編號，供應較高），
    /// 可及規則與草原野採相同（本格與六鄰格）。
    /// </summary>
    public static IReadOnlyList<DemoCatchmentFoodSource> Sources(DemoHexWorld map, DemoWorldSites? sites, DemoLandDevelopmentMap? land)
        => Sources(map, sites?.OccupiedCells(), land?.FarmlandCells());

    /// <summary>第 08-6 章 c 節：佔格之外的自然來源，<paramref name="farmland"/> 格改為農田來源。</summary>
    public static IReadOnlyList<DemoCatchmentFoodSource> Sources(DemoHexWorld map, IReadOnlyList<DemoCellRef>? occupied,
        IReadOnlyCollection<DemoCellRef>? farmland)
    {
        var natural = Sources(map, occupied);
        if (farmland is null || farmland.Count == 0)
            return natural;
        var cells = farmland.ToHashSet();
        return natural.Select(s => cells.Contains(new DemoCellRef(s.Column, s.Row))
            ? s with { Kind = DemoCatchmentSourceKind.Farmland, FoodAvailable = DemoLandClearing.TempFarmlandWeeklySupply }
            : s).ToArray();
    }

    public static IReadOnlyList<DemoCatchmentFoodSource> Sources(DemoHexWorld map, IReadOnlyList<DemoCellRef>? occupied)
        => Settings.BuildPotentialSources(map, MainRiverTiles(map), occupied);

    /// <summary>第 07 章 c 節：<paramref name="cell"/> 可及至少一個未被佔用的自然來源（開拓選址條件）。</summary>
    public static bool ReachesFreeSource(DemoHexWorld map, IReadOnlyList<DemoCellRef> occupied, DemoCellRef cell)
    {
        // 第 07-1 章 c 節：佔用只會移除該格的來源，所以從全圖來源（每張地圖算一次）中排除被佔的格即可；
        // 只有本格與鄰格的來源可能可及。結果與原本「每次重建全部來源」相同。
        var blocked = occupied.Append(cell).ToHashSet();
        var here = map.GetCell(cell.Column, cell.Row);
        return AllSources.GetValue(map, m => Sources(m, Array.Empty<DemoCellRef>())).Any(source => source.FoodAvailable > 0
            && Math.Abs(source.Row - cell.Row) <= 1 && Math.Abs(source.Column - cell.Column) <= 1
            && !blocked.Contains(new DemoCellRef(source.Column, source.Row))
            && DemoHexFoodCatchment.IsInReach(map.GetCell(source.Column, source.Row), here,
                DemoCatchmentSourceKinds.IncludesOrigin(source.Kind))
            && here.Terrain != DemoTerrain.Water);
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DemoHexWorld, IReadOnlyList<DemoCatchmentFoodSource>> AllSources = new();

    /// <summary>
    /// Food harvest is a one-week, one-source budget. No local farm and no source-specific
    /// accumulated stores in this TEMP mode: unharvested forage is left in nature, and only
    /// gathered food is counted in settlement stocks. Other scenarios retain old resources.
    /// </summary>
    public static DemoCatchmentFoodResult Allocate(
        DemoHexWorld map, DemoWorldSites sites,
        IReadOnlyDictionary<int, long> openingPopulation,
        DemoWeeklyTempConfig weekly,
        IReadOnlyDictionary<int, long>? adultWorkers = null,
        IReadOnlyList<DemoCatchmentFoodSource>? availableSources = null,
        DemoCatchmentAllocation mode = DemoCatchmentAllocation.LabourProportional,
        IReadOnlyDictionary<int, long>? foodNeeded = null,
        IReadOnlyDictionary<int, long>? baseNeeds = null,
        Func<int, long>? yieldPercent = null)
    {
        if (!weekly.SharedCatchmentEnabled)
            throw new InvalidOperationException("Not a shared catchment world.");
        var claims = sites.Settlements.Where(s => !s.IsRuin).OrderBy(s => s.Id).Select(s => // 第 07 章 c 節：遺跡不採集
            new DemoCatchmentFoodClaim(s.Id, s.Column, s.Row,
                foodNeeded?.GetValueOrDefault(s.Id) ?? checked(openingPopulation[s.Id] * weekly.FoodPerPersonPerWeek),
                // 第 08-9 章 A4：每名勞力的可採量先取整（與週帳 YieldPerWorker、讀檔與 M1 檢查同一算法）。
                checked((adultWorkers?.GetValueOrDefault(s.Id) ?? openingPopulation[s.Id])
                    * (weekly.YieldPerWorkerPerWeek * (100 + (yieldPercent?.Invoke(s.Id) ?? 0)) / 100))));
        // 第 06 章 c 節：availableSources＝本週可採（期初存量＋再生）；不給時用每週供應（只供舊測試與診斷）。
        return DemoHexFoodCatchment.Allocate(map, availableSources ?? Sources(map, sites), claims, mode, baseNeeds);
    }
}
