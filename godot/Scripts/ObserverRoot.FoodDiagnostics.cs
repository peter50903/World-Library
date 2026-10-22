using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// #104：聚落糧食供需診斷。只讀 Core 的最新週報與 <see cref="DemoObserverWorld.SettlementCapacities"/>
/// （#103 承載估計），不自行計算再生、出生、死亡或糧食，也不改世界。全部標 TEMP。
/// 上方一行列出「目前人口超過長期可養活人數」的聚落，方便直接找到長期缺糧的聚落；
/// 點選聚落後顯示該聚落最新一週的資源、採收、需求、消耗、缺糧、糧庫與飢餓債。
/// </summary>
public partial class ObserverRoot
{
    private static readonly bool FoodSmoke = OS.HasEnvironment("WORLD_SIM_FOOD_SMOKE");

    private Label _foodInfo = null!;
    private int _foodSettlementId; // 0＝未選聚落（換世界時重設）。

    private void BuildFoodDiagnostics(Container side)
    {
        _foodInfo = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        side.AddChild(_foodInfo);
    }

    private void SelectFoodSettlement(int settlementId)
    {
        _foodSettlementId = settlementId;
        RefreshFoodDiagnostics();
    }

    private void RefreshFoodDiagnostics()
    {
        if (_foodInfo is null)
            return;
        _foodInfo.Text = FoodDiagnosticsText(_world, _foodSettlementId);
    }

    /// <summary>純函式：由 Core 世界產生顯示文字（同一世界永遠得到同一段文字）。</summary>
    private static string FoodDiagnosticsText(DemoObserverWorld world, int settlementId)
    {
        if (world.UsesSharedForaging)
            return SharedForagingDiagnosticsText(world, settlementId);
        var capacities = world.SettlementCapacities();
        var lines = new List<string> { "—— 糧食供需診斷（TEMP，只讀 Core 週帳）——" };
        var over = capacities.Where(c => c.Population > 0 && !c.LongTermSustainable).ToList();
        lines.Add(over.Count == 0
            ? "長期缺糧聚落：無（每個有人聚落的人口都不超過長期可養活人數）"
            : "長期缺糧聚落（人口＞長期可養活）：" + string.Join("、",
                over.Select(c => $"{c.SettlementName} {c.Population}＞{c.SustainablePopulation}（每週缺 {c.LongTermGap}）")));

        var cap = capacities.FirstOrDefault(c => c.SettlementId == settlementId);
        if (cap is null)
        {
            lines.Add("點選金色聚落可查看它的資源再生、採收、需求與缺糧。");
            return string.Join("\n", lines);
        }

        lines.Add($"聚落：{cap.SettlementName}（{cap.ResourceKinds}{(cap.TempFamineSettlement ? "，TEST 饑荒聚落：不再生" : "")}）");
        var week = world.LatestWeek;
        var l = week?.Settlements.FirstOrDefault(x => x.SettlementId == settlementId);
        if (week is null || l is null)
        {
            lines.Add(week is null
                ? "尚無週報：第 7 日第一次週結算後才有採收、消耗與缺糧數字。"
                : "本聚落在最新一週尚未存在（新建聚落下週才有週帳）。");
            lines.Add($"目前：人口 {cap.Population}、資源存量 {cap.ReserveNow}、糧庫 {cap.FoodNow}、飢餓債 {cap.HungerDebtNow}");
        }
        else
        {
            var harvestCap = checked(l.PopulationOpening * cap.YieldPerWorkerPerWeek);
            var demand = checked(l.PopulationOpening * cap.FoodPerPersonPerWeek);
            lines.Add($"第 {week.Week} 週（第 {(int)week.WorldTime.TotalDays} 日結算）：");
            lines.Add($"  資源點：期初 {l.ReserveOpening} ＋實際再生 {l.Regenerated} ＝可採 {l.ReserveOpening + l.Regenerated}");
            lines.Add($"  採收上限（勞力 {l.PopulationOpening} 人×每人 {cap.YieldPerWorkerPerWeek}）{harvestCap}；實際採收 {l.Harvested}");
            lines.Add($"  需求（{l.PopulationOpening} 人×每人 {cap.FoodPerPersonPerWeek}）{demand}；實際消耗 {l.FoodConsumed}；缺糧 {l.FoodShortage}");
            lines.Add($"  糧庫：期初 {l.FoodOpening} → 期末 {l.FoodClosing}；飢餓債期末 {l.HungerDebtClosing}；"
                + $"出生 {l.Births}、自然死亡 {l.NaturalDeaths}、餓死 {l.StarvationDeaths}");
        }

        var limit = cap.SustainablePopulation == long.MaxValue ? "不限" : $"{cap.SustainablePopulation} 人";
        lines.Add($"長期估計（描述性，不含資源庫存與遷徙／生育變化）：再生 {cap.RegenPerWeek}／週 vs 目前需求 {cap.DemandPerWeek}／週"
            + $" → 長期可養活 {limit}；"
            + (cap.Population == 0 ? "無人" : cap.LongTermSustainable ? "可長期維持" : $"長期每週缺 {cap.LongTermGap}"));
        return string.Join("\n", lines);
    }

    /// <summary>Only factual Core source-to-settlement harvest and accounts are shown.</summary>
    private static string SharedForagingDiagnosticsText(DemoObserverWorld world, int settlementId)
    {
        var settings = DemoForagingWorld.Settings;
        var allSources = world.SharedFoodSources();
        var people = world.Sites.Settlements.ToArray();
        var week = world.LatestWeek;
        var allocation = world.LatestSharedHarvest();
        var output = new List<string>
        {
            "—— 森林／河流採集生存（已擴充所有自然地形，人族 TEMP）——",
            $"森林{settings.ForestWeeklySupply}／草原野採{settings.GrasslandForageWeeklySupply}／主河道{settings.MainRiverWeeklySupply}／海洋{settings.OpenWaterWeeklySupply}（每來源格每週共享）；未開墾農田0",
            $"全圖森林來源 {allSources.Count(x => x.Kind == DemoCatchmentSourceKind.Forest)}；"
              + $"草原 {allSources.Count(x => x.Kind == DemoCatchmentSourceKind.GrasslandForage)}；"
              + $"主河道 {allSources.Count(x => x.Kind == DemoCatchmentSourceKind.MainRiver)}；"
              + $"海洋 {allSources.Count(x => x.Kind == DemoCatchmentSourceKind.OpenWater)}",
            "每塊未占用自然地形只產一份野採或漁獲；村莊格本身不再自然產糧，周圍來源可以供應。",
            "分配順序 TEMP：來源編號→聚落編號；有人的格才能實際採收。"
        };
        if (week is not null)
        {
            output.Add($"第 {week.Week} 週：自然來源（森林／草原／主河道／海洋）實採合計 {allocation?.TotalHarvested ?? 0}（同源每週只計一次）");
            if (world.HumanPopulationSnapshot is not null)
                output.Add($"人類本週出生 {week.Settlements.Sum(l => l.Births)}、自然死亡 {week.Settlements.Sum(l => l.NaturalDeaths)}、"
                    + $"餓死 {week.Settlements.Sum(l => l.StarvationDeaths)}；"
                    + $"目前人口 {world.WorldPopulationStats?.Total ?? 0}（群體統計；未成年不採集）");
            output.Add($"世界聚落人口 {world.SettledPopulation}，本週共缺糧 {week.Settlements.Sum(l => l.FoodShortage)}，"
                + $"聚落餓死 {week.Settlements.Sum(l => l.StarvationDeaths)}");
        }

        var place = people.FirstOrDefault(x => x.Id == settlementId);
        if (place is null)
        {
            output.Add("請點金色聚落，查看周圍哪片森林／河流供食及實際缺糧原因。");
            return string.Join("\n", output);
        }
        var position = world.Map.GetCell(place.Column, place.Row);
        var adjacentSources = allSources.Where(x =>
            DemoHexFoodCatchment.IsInReach(
                world.Map.GetCell(x.Column, x.Row), position,
                x.Kind is DemoCatchmentSourceKind.Forest or DemoCatchmentSourceKind.GrasslandForage)).OrderBy(x => x.Id).ToArray();
        output.Add($"聚落：{place.Name}；人口 {world.SettlementPopulation(place.Id)}；"
            + $"糧庫 {world.SettlementFood(place.Id)}");
        output.Add($"可採集來源 {adjacentSources.Length} 格："
            + (adjacentSources.Length == 0 ? "沒有鄰近自然來源（即使草原有野採，也須有人採集）"
              : string.Join("、", adjacentSources.Select(x =>
                  $"{(x.Kind switch { DemoCatchmentSourceKind.Forest => "森林", DemoCatchmentSourceKind.MainRiver => "主河道", DemoCatchmentSourceKind.GrasslandForage => "草原野採", _ => "海洋" })}({x.Column},{x.Row}) 共用{x.FoodAvailable}"))));
        var ledger = week?.Settlements.FirstOrDefault(l => l.SettlementId == place.Id);
        if (ledger is null)
            output.Add("尚無週報：快轉至第7日，才能看到真實採收與人口缺糧。");
        else
        {
            // 第 06 章 d 節：需求已含兒童半份（＝消耗＋缺糧）；另列腐敗、取水與老幼餓死分類。
            output.Add($"本週：需求 {ledger.FoodConsumed + ledger.FoodShortage}，"
                + $"實採 {ledger.Harvested}，腐敗 {ledger.FoodSpoiled}，消耗 {ledger.FoodConsumed}，缺糧 {ledger.FoodShortage}，"
                + $"期末糧庫 {ledger.FoodClosing}，飢餓債 {ledger.HungerDebtClosing}，"
                + $"出生 {ledger.Births}、自然死亡 {ledger.NaturalDeaths}、餓死 {ledger.StarvationDeaths}");
            // 第 04 章 c 節：讀已提交的來源帳（每源預算與未採），不重算。
            var origins = week!.Sources
                .Select(source => (source, share: source.Shares.FirstOrDefault(x => x.SettlementId == place.Id)))
                .Where(x => x.share is not null)
                .Select(x => $"({x.source.Column},{x.source.Row})→{x.share!.Food}（預算 {x.source.FoodAvailable}，未採 {x.source.Unharvested}）")
                .ToArray();
            output.Add("本週來源→採收：" + (origins.Length == 0 ? "無" : string.Join("、", origins)));
            output.Add(ledger.WaterShortage > 0
                ? $"水：不足（取水者 {ledger.WaterCarriers}；缺水生存債 +{ledger.WaterShortage}）"
                : $"水：可取（取水者 {ledger.WaterCarriers}）");
            var starvedTotal = ledger.StarvationDeaths;
            output.Add($"餓死分類：兒童 {ledger.ChildStarvationDeaths}、老人 {ledger.ElderStarvationDeaths}、"
                + $"成人 {starvedTotal - ledger.ChildStarvationDeaths - ledger.ElderStarvationDeaths}");
        }
        return string.Join("\n", output);
    }

    /// <summary>
    /// #104 CI 冒煙（真 Godot）：用真的地圖點擊與「下一天」按鈕，在一般與局部饑荒情境推進到第 7、35 日，
    /// 畫面每一項都要等於同種子 Core 週報；檢視不改世界；損毀存檔讀取被拒後世界與畫面不變；換世界不殘留。
    /// </summary>
    private void VerifyFoodSmoke(Button nextButton, Button[] loadButtons)
    {
        void Fail(string why) => throw new InvalidOperationException("Food diagnostics smoke: " + why);
        void Click(DemoSettlement s) => _hexMap._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = _hexMap.CellCenter(_world.Map.GetCell(s.Column, s.Row))
        });

        void CheckAgainstCore(string step, DemoSettlement s)
        {
            var text = _foodInfo.Text;
            if (text != FoodDiagnosticsText(_world, s.Id))
                Fail($"{step}: panel differs from Core-derived text");
            var cap = _world.SettlementCapacities().Single(c => c.SettlementId == s.Id);
            var l = _world.LatestWeek!.Settlements.Single(x => x.SettlementId == s.Id);
            foreach (var expected in new[]
                     {
                         $"聚落：{s.Name}", $"期初 {l.ReserveOpening} ＋實際再生 {l.Regenerated}", $"實際採收 {l.Harvested}",
                         $"實際消耗 {l.FoodConsumed}；缺糧 {l.FoodShortage}", $"期末 {l.FoodClosing}；飢餓債期末 {l.HungerDebtClosing}",
                         $"再生 {cap.RegenPerWeek}／週 vs 目前需求 {cap.DemandPerWeek}／週", "TEMP"
                     })
            {
                if (!text.Contains(expected, StringComparison.Ordinal))
                    Fail($"{step}: missing「{expected}」");
            }
        }

        void AdvanceTo(int day)
        {
            while (_world.Snapshot().DaysElapsed < day)
                nextButton.EmitSignal(BaseButton.SignalName.Pressed);
        }

        var checkedWeeks = 0;
        var shortageSeen = 0L;
        foreach (var scenario in new[] { TempScenario.Normal, TempScenario.LocalFamine })
        {
            InstallWorld(CreateTempWorld(scenario), "糧食診斷冒煙測試");
            var settlement = scenario == TempScenario.LocalFamine
                ? _world.Sites.Settlements.Single(s => s.Id == DemoWeeklyTempConfig.LocalFamineSample.TempFamineSettlementId)
                : _world.Sites.Settlements[0];

            var before = _world.ExportTempSave();
            Click(settlement);
            if (_foodSettlementId != settlement.Id || !_foodInfo.Text.Contains("尚無週報", StringComparison.Ordinal))
                Fail($"{scenario} day 0: no clear 'no report yet' hint");
            if (_world.ExportTempSave() != before)
                Fail("selecting a settlement changed the Core world");

            foreach (var day in new[] { 7, 35 })
            {
                AdvanceTo(day);
                CheckAgainstCore($"{scenario} day {day}", settlement);
                shortageSeen += _world.LatestWeek!.Settlements.Single(x => x.SettlementId == settlement.Id).FoodShortage;
                checkedWeeks++;
            }

            if (scenario == TempScenario.LocalFamine && !_foodInfo.Text.Contains("TEST 饑荒聚落", StringComparison.Ordinal))
                Fail("famine settlement is not marked TEST");

            // 檢視不改世界：同一世界重畫兩次文字相同、存檔相同。
            var saved = _world.ExportTempSave();
            var shown = _foodInfo.Text;
            Refresh();
            if (_foodInfo.Text != shown || _world.ExportTempSave() != saved)
                Fail("refreshing the view changed the world or the panel");
        }

        if (shortageSeen == 0)
            Fail("local famine settlement showed no shortage by day 35");

        // 損毀存檔被拒：世界與畫面都不變。
        System.IO.Directory.CreateDirectory(DemoSaveSlots.DefaultDirectory);
        System.IO.File.WriteAllText(DemoSaveSlots.PathFor(3), "{\"Version\":3,\"State\":");
        RefreshSlots();
        var worldBefore = _world.ExportTempSave();
        var panelBefore = _foodInfo.Text;
        loadButtons[3].EmitSignal(BaseButton.SignalName.Pressed);
        if (_world.ExportTempSave() != worldBefore || _foodInfo.Text != panelBefore)
            Fail("a rejected corrupt load changed the world or the food panel");

        // 換世界：不殘留前一個世界的聚落選取與數字。
        InstallWorld(CreateTempWorld(TempScenario.Normal), "冒煙測試結束");
        if (_foodSettlementId != 0 || _foodInfo.Text != FoodDiagnosticsText(_world, 0) || _foodInfo.Text.Contains("聚落：", StringComparison.Ordinal))
            Fail("switching worlds left the previous settlement's food data");

        GD.Print($"WORLD_SIM_FOOD_OK: Weeks={checkedWeeks} FamineShortage={shortageSeen} CoreUnchanged=1 CorruptRejected=1 SwitchReset=1");
    }
}
