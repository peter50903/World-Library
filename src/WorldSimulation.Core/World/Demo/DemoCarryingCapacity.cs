namespace WorldSimulation.Core.World.Demo;

/// <summary>
/// #103 一個示範聚落「長期資源再生 vs 目前需求」的描述性估計（TEMP，未定案，不是正式承載力規則）。
/// 只讀世界目前狀態與 TEMP 週結算設定，不改動任何狀態、不產生糧食。
/// <para>
/// 推導（TEMP 週結算的實際規則，見 <see cref="DemoWeeklySettlement"/>）：每週資源點最多再生
/// <see cref="RegenPerWeek"/>，勞力每人可採 <see cref="YieldPerWorkerPerWeek"/>，每人每週吃
/// <see cref="FoodPerPersonPerWeek"/>。資源點存量與糧庫耗盡之後，每週可取得的糧食最多是
/// min(再生, 人口×每人產量)，所以人口 ≤ <see cref="SustainablePopulation"/> 時，每週採收一定 ≥ 需求、不會缺糧；
/// 超過時只能靠存量與糧庫撐一段時間，之後每週缺 <see cref="LongTermGap"/>（形成飢餓債→餓死或外移）。
/// </para>
/// 不含：資源點目前存量與糧庫（短期緩衝）、遷徙帶入帶出、出生（尚未實作）、腐敗（29 表未接入）。
/// </summary>
public sealed record DemoSettlementCapacity(
    int SettlementId,
    string SettlementName,
    string ResourceKinds,
    bool TempFamineSettlement,
    long Population,
    long RegenPerWeek,
    long YieldPerWorkerPerWeek,
    long FoodPerPersonPerWeek,
    long ReserveNow,
    long FoodNow,
    long HungerDebtNow)
{
    /// <summary>目前人口每週需要的糧食。</summary>
    public long DemandPerWeek => checked(Population * FoodPerPersonPerWeek);

    /// <summary>
    /// 存量與糧庫耗盡後，靠每週再生與勞力可長期養活的最多人數（TEMP 估計）。
    /// 每人產量低於每人消耗時，勞力本身就養不活任何人（0）；每人不吃糧時沒有上限（<see cref="long.MaxValue"/>）。
    /// </summary>
    public long SustainablePopulation
        => FoodPerPersonPerWeek == 0 ? long.MaxValue
        : YieldPerWorkerPerWeek < FoodPerPersonPerWeek ? 0
        : RegenPerWeek / FoodPerPersonPerWeek;

    /// <summary>存量與糧庫耗盡後，目前人口每週的長期缺口（0＝可長期維持）。</summary>
    public long LongTermGap
    {
        get
        {
            var sustainableIntake = Math.Min(RegenPerWeek, checked(Population * YieldPerWorkerPerWeek));
            return Math.Max(0, DemandPerWeek - sustainableIntake);
        }
    }

    /// <summary>目前人口是否不超過長期可養活人數（描述性估計）。</summary>
    public bool LongTermSustainable => LongTermGap == 0;

    /// <summary>玩家可讀的一行說明（繁體中文，標 TEMP）。</summary>
    public string Describe()
    {
        var limit = SustainablePopulation == long.MaxValue ? "不限" : $"{SustainablePopulation} 人";
        var verdict = Population == 0
            ? "無人"
            : LongTermSustainable
            ? "可長期維持"
            : $"長期每週缺 {LongTermGap}（靠存量 {ReserveNow}、糧庫 {FoodNow} 暫撐）";
        return $"{SettlementName}（#{SettlementId}，{ResourceKinds}{(TempFamineSettlement ? "，TEST 饑荒聚落：不再生" : string.Empty)}）"
            + $"｜人口 {Population}、需求 {DemandPerWeek}／週｜再生 {RegenPerWeek}／週｜長期可養活 {limit}｜{verdict}"
            + $"｜飢餓債 {HungerDebtNow}（TEMP 估計）";
    }
}

/// <summary>#103：由世界目前狀態計算各聚落的 TEMP 承載估計（純讀取）。</summary>
public static class DemoCarryingCapacity
{
    /// <summary>資源種類的中文名稱（TEMP）。</summary>
    public static string KindName(DemoResourceKind kind) => kind switch
    {
        DemoResourceKind.Farmland => "農地",
        DemoResourceKind.Woodland => "林地",
        DemoResourceKind.Fishery => "漁場",
        _ => "未知"
    };

    /// <summary>
    /// 依聚落編號排序回傳每個聚落的估計。每週再生量與 <see cref="DemoWeeklySettlement"/> 相同：
    /// TEMP 饑荒聚落（開局的指定聚落）為 0，其他依資源種類取 TEMP 再生量。
    /// </summary>
    public static IReadOnlyList<DemoSettlementCapacity> Of(
        DemoWorldSites sites,
        DemoWeeklyTempConfig config,
        Func<int, long> population,
        Func<int, long> resourceReserve,
        Func<int, long> settlementFood,
        Func<int, long> hungerDebt)
    {
        ArgumentNullException.ThrowIfNull(sites);
        ArgumentNullException.ThrowIfNull(config);
        var list = new List<DemoSettlementCapacity>(sites.Settlements.Count);
        foreach (var settlement in sites.Settlements.OrderBy(s => s.Id))
        {
            var points = sites.ResourcePointsOf(settlement.Id).OrderBy(p => p.Id).ToList();
            var famine = config.IsFamineSettlement(settlement);
            long regen = 0, reserve = 0;
            foreach (var p in points)
            {
                regen = checked(regen + (famine ? 0 : config.RegenFor(p.Kind)));
                reserve = checked(reserve + resourceReserve(p.Id));
            }

            var kinds = points.Count == 0 ? "無資源點" : string.Join("＋", points.Select(p => KindName(p.Kind)));
            list.Add(new DemoSettlementCapacity(
                settlement.Id, settlement.Name, kinds, famine,
                population(settlement.Id), regen, config.YieldPerWorkerPerWeek, config.FoodPerPersonPerWeek,
                reserve, settlementFood(settlement.Id), hungerDebt(settlement.Id)));
        }

        return list.AsReadOnly();
    }
}
