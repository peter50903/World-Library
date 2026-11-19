using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-33 章：領地面板（使用者 2026-11-18：聚落點開要像 CK3 一樣看得到建築——CK3 是領地面板，但沒有蓋的就不用了，
/// 只顯示有蓋、正在蓋的、被破壞耐久度歸零的；要寫細一點，從城牆、農田、城內建築、兵營、城堡……；要視覺化，
/// 不要九宮格塞滿插圖；插槽越多代表建築越多也代表城市越大）。
/// <list type="bullet">
/// <item>聚落詳情的「領地」分頁（排在現況之後）：上面是領地圖縮圖（點一下打開大圖），下面是依分區列出的建築，
///   每列有分類圖示、狀態圖示（施工中、受損、毀損）與狀態文字。</item>
/// <item>大圖：視窗左邊是領地圖（滾輪縮放、拖曳平移、滑鼠移上去看名稱與狀態），右邊是同一份列表。</item>
/// </list>
/// 資料來自 Core 的 <c>DemoObserverWorld.HoldingScene</c>，只讀。
/// </summary>
public partial class ObserverRoot
{
    private HoldingView _holdingView = null!;
    private VBoxContainer _holdingList = null!;
    private HBoxContainer _holdingPage = null!;
    private int _holdingSettlement;
    private int? _holdingWeek;

    /// <summary>第 08-33 章：領地大圖的視窗頁（由 <see cref="BuildModalHost"/> 之後呼叫）。</summary>
    private void BuildHoldingPage()
    {
        _holdingPage = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _holdingPage.AddThemeConstantOverride("separation", 10);
        _holdingView = new HoldingView(false) { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _holdingPage.AddChild(_holdingView);
        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(TempHoldingListWidth, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _holdingList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(_holdingList);
        _holdingPage.AddChild(scroll);
        AddModalPage("holding", "領地", _holdingPage);
    }

    /// <summary>【TEMP】大圖視窗右邊列表的寬度、視窗最大寬高、詳情裡縮圖的高度。</summary>
    private const float TempHoldingListWidth = 300f, TempHoldingWindowWidth = 1280f, TempHoldingWindowHeight = 820f, TempHoldingThumbHeight = 190f;

    /// <summary>打開某聚落的領地大圖。</summary>
    private void OpenHolding(int settlementId)
    {
        if (_world.HoldingScene(settlementId) is null)
            return;
        _holdingSettlement = settlementId;
        if (_modalOpen != "holding")
            ToggleModal("holding");
        RefreshHoldingWindow();
        SizeModal();
    }

    /// <summary>大圖開著時，每週結算後更新一次（換世界、聚落消失就關掉）。</summary>
    private void RefreshHoldingWindow()
    {
        if (_modalOpen != "holding")
            return;
        if (_world.HoldingScene(_holdingSettlement) is not { } scene)
        {
            CloseModal();
            return;
        }
        _holdingWeek = _world.LatestWeek?.Week;
        _modalTitle.Text = $"領地：{TitledName(scene.Site)}（{DemoSettlementHistory.TierName(scene.Site, scene.Tier)}）";
        _holdingView.ShowScene(scene);
        FillHoldingList(_holdingList, scene);
    }

    /// <summary>由 <see cref="Refresh"/> 呼叫：大圖開著、又過了一週就更新。</summary>
    private void RefreshHoldingIfOpen()
    {
        if (_modalOpen == "holding" && _world.LatestWeek?.Week != _holdingWeek)
            RefreshHoldingWindow();
    }

    /// <summary>詳情「領地」分頁：縮圖（點一下打開大圖）＋分區列表。</summary>
    private Control HoldingTabContent(DemoHoldingScene scene)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6);
        var thumb = new HoldingView(true) { CustomMinimumSize = new Vector2(0, TempHoldingThumbHeight), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        thumb.ShowScene(scene);
        var id = scene.Site.Id;
        thumb.Opened += () => OpenHolding(id);
        column.AddChild(thumb);
        var open = new Button { Text = "打開領地圖", FocusMode = FocusModeEnum.None, TooltipText = "大圖：滾輪縮放、拖曳平移、雙擊回到全圖" };
        open.Pressed += () => OpenHolding(id);
        column.AddChild(open);
        var list = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        FillHoldingList(list, scene);
        column.AddChild(list);
        return column;
    }

    /// <summary>分區列表：建築格、住宅、建材，然後城牆與塔樓、農田與農舍、城內建築、神殿與學府、兵營與訓練場、城堡與官署、城外資源。</summary>
    private static void FillHoldingList(VBoxContainer list, DemoHoldingScene scene)
    {
        foreach (var child in list.GetChildren())
        {
            list.RemoveChild(child);
            child.QueueFree();
        }
        var h = scene.Holding;
        list.AddChild(IconRow("b12_ic_cat_admin", $"建築格 {scene.SlotsUsed}/{scene.Slots}（住宅、城牆、城門不佔格）", null,
            $"同時施工最多 {DemoBuildings.Projects(scene.Tier)} 項"));
        list.AddChild(IconRow("b12_ic_cat_housing", DemoHoldingReport.HousingLine(scene),
            scene.Residents > h.Housing ? "b12_ic_state_crowded" : scene.HousePercent > 0 ? "b12_ic_state_building" : null));
        var stock = new HBoxContainer();
        stock.AddThemeConstantOverride("separation", 4);
        foreach (var (icon, label, amount) in new[] { ("ic_stat_25", "木", h.Wood), ("ic_stat_26", "石", h.Stone), ("b12_ic_res_iron", "鐵", h.Iron) })
        {
            if (PixelParts.Texture(icon) is { } texture)
                stock.AddChild(Picture(texture, 18));
            stock.AddChild(new Label { Text = $"{label} {amount}　", TooltipText = $"建材庫存：{label}" });
        }
        list.AddChild(stock);
        if (scene.Progress is { } progress)
            AddTierProgress(list, progress); // 第 08-36 章
        if (scene.Bonus.Any)
        {
            // 第 08-37 章：建築的作用（相加後），每項配分類圖示。
            list.AddChild(new HSeparator());
            list.AddChild(IconRow("b12_ic_cat_admin", "建築加成", null, "完好的建築才有作用；同一種蓋好幾座只算一次（城牆、塔樓、城堡看城防）", 22));
            foreach (var (kind, text) in scene.Bonus.Parts())
                list.AddChild(IconRow(BonusIcon(kind), text));
        }
        var rows = DemoHoldingReport.Rows(scene);
        foreach (var zone in Enum.GetValues<DemoHoldingZone>())
        {
            var inZone = rows.Where(r => r.Zone == zone).ToList();
            var fields = zone == DemoHoldingZone.Fields && scene.Fields > 0;
            if (inZone.Count == 0 && !fields)
                continue;
            list.AddChild(new HSeparator());
            var header = IconRow(ZoneIcon(zone), DemoHoldingReport.ZoneName(zone), null, null, 22);
            list.AddChild(header);
            if (fields)
                list.AddChild(IconRow("b12_ic_cat_field", $"附近已開墾的農田 {scene.Fields} 塊", null, "農田是開墾出來的地，不佔建築格"));
            if (zone == DemoHoldingZone.Walls && DemoDefense.Of(h) is { Any: true } defense)
                list.AddChild(IconRow("b12_ic_cat_defense", defense.Describe(), null,
                    "第 08-34 章：城牆與城門、塔樓讓守方佔便宜（怪物來襲、大軍攻城）；被打壞的下週才少")); // 城防
            foreach (var r in inZone)
            {
                var state = r.Status switch
                {
                    DemoBuildingStatus.Building => "b12_ic_state_building",
                    DemoBuildingStatus.Ruined => "b12_ic_state_destroyed",
                    _ when r.Percent < 100 => "b12_ic_state_damaged",
                    _ => null
                };
                var effect = DemoHoldingReport.EffectText(r); // 第 08-37 章
                var name = DemoBuildingCatalog.Contains(r.DefId) ? DemoBuildingCatalog.Get(r.DefId).Name : null;
                list.AddChild(IconRow(CategoryIcon(r.DefId),
                    $"{r.Name}{(r.Count > 1 ? $" ×{r.Count}" : "")}：{DemoHoldingReport.StateText(r.Status, r.Percent)}{(effect is null ? "" : "｜" + effect)}",
                    state, effect is null ? name : $"{name}\n作用：{effect}"));
            }
        }
        if (rows.Count == 0)
            list.AddChild(new Label { Text = "（住宅以外還沒有任何建築）" });
    }

    /// <summary>
    /// 第 08-36 章：升下一級還差什麼——標題（下一級的圖示、人口與本級設施幾項）與每項本級設施
    /// （完成、施工中、毀損待重建、可以開工；還沒解鎖的變淡並寫缺什麼）。
    /// </summary>
    private static void AddTierProgress(VBoxContainer list, DemoTierProgress progress)
    {
        list.AddChild(new HSeparator());
        var head = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = "人口夠，本級設施任兩種「解鎖（有這種專家或學會技術）並蓋好建築」，下次週結算就升級；被打壞的建築不會讓等級馬上下降"
        };
        head.AddThemeConstantOverride("separation", 4);
        if (ArtAssets.Texture(ArtAssets.SettlementTierIcon(progress.Next)) is { } crest)
            head.AddChild(Picture(crest, 22));
        var title = new Label { Text = progress.Headline, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 16);
        head.AddChild(title);
        list.AddChild(head);
        foreach (var step in progress.Steps)
        {
            var state = step.State switch
            {
                DemoTierStepState.Building => "b12_ic_state_building",
                DemoTierStepState.Ruined => "b12_ic_state_destroyed",
                DemoTierStepState.Ready => "b12_ic_state_idle",
                _ => null
            };
            var row = IconRow(CategoryIcon(DemoBuildings.FacilityBuildings[step.Facility.Name].DefId), step.Line, state,
                step.State == DemoTierStepState.Locked ? "還沒解鎖：要有這種專家（代表人物），或學會這項技術" : null);
            if (step.State == DemoTierStepState.Locked)
                row.Modulate = new Color(1, 1, 1, 0.55f);
            list.AddChild(row);
        }
    }

    /// <summary>一列：分類圖示＋文字＋（狀態圖示）。</summary>
    private static HBoxContainer IconRow(string icon, string text, string? state = null, string? tooltip = null, float size = 18)
    {
        var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = tooltip ?? "" };
        row.AddThemeConstantOverride("separation", 4);
        if (PixelParts.Texture(icon) is { } texture)
            row.AddChild(Picture(texture, size));
        var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (size > 18)
            label.AddThemeFontSizeOverride("font_size", 16);
        row.AddChild(label);
        if (state is not null && PixelParts.Texture(state) is { } badge)
            row.AddChild(Picture(badge, 18));
        return row;
    }

    /// <summary>第 08-37 章：建築加成的圖示（第十二批分類圖示）。</summary>
    private static string BonusIcon(DemoBonusKind kind) => kind switch
    {
        DemoBonusKind.Research => "b12_ic_cat_edu",
        DemoBonusKind.Storage => "b12_ic_cat_storage",
        DemoBonusKind.Construction => "b12_ic_cat_industry",
        DemoBonusKind.Gathering => "b12_ic_cat_resource",
        DemoBonusKind.Soldiers => "b12_ic_cat_military",
        _ => "b12_ic_cat_health"
    };

    private static string ZoneIcon(DemoHoldingZone zone) => zone switch
    {
        DemoHoldingZone.Walls => "b12_ic_cat_defense",
        DemoHoldingZone.Fields => "b12_ic_cat_field",
        DemoHoldingZone.Town => "b12_ic_cat_industry",
        DemoHoldingZone.Sacred => "b12_ic_cat_religion",
        DemoHoldingZone.Military => "b12_ic_cat_military",
        DemoHoldingZone.Keep => "b12_ic_cat_admin",
        _ => "b12_ic_cat_resource"
    };

    /// <summary>建築的分類圖示（第十二批）。</summary>
    private static string CategoryIcon(string defId) => defId switch
    {
        DemoBuildings.WallWood or DemoBuildings.WallStone or DemoBuildings.GateWood or DemoBuildings.GateStone
            or DemoBuildings.Outpost or DemoBuildings.StoneTower => "b12_ic_cat_defense",
        "BUILD_FARM" or "BUILD_KITCHEN" => "b12_ic_cat_field",
        "BUILD_MARKET" or "BUILD_INN" or "BUILD_BANK" => "b12_ic_cat_trade",
        "BUILD_GRANARY" or "BUILD_WAREHOUSE" => "b12_ic_cat_storage",
        "BUILD_CHURCH" => "b12_ic_cat_religion",
        "BUILD_SCHOOL" or "BUILD_ACADEMY" or "BUILD_MAGIC_ACADEMY" or "BUILD_SCRIPTORIUM" => "b12_ic_cat_edu",
        "BUILD_CLINIC" or "BUILD_HOSPITAL" => "b12_ic_cat_health",
        "BUILD_PRISON" => "b12_ic_cat_justice",
        "BUILD_BARRACK" or "BUILD_STABLE" or "BUILD_ARCHERY_RANGE" => "b12_ic_cat_military",
        "BUILD_SIEGE_WORKSHOP" => "b12_ic_cat_siege",
        "BUILD_CASTLE" or "BUILD_TOWN_HALL" => "b12_ic_cat_admin",
        "BUILD_LUMBERYARD" or "BUILD_QUARRY" or "BUILD_MINE" => "b12_ic_cat_resource",
        "BUILD_PORT" => "b12_ic_cat_transport",
        "BUILD_SHELTER" or "BUILD_HOUSE_BASIC" or "BUILD_HOUSE_LARGE" => "b12_ic_cat_housing",
        _ => "b12_ic_cat_industry"
    };

    /// <summary>
    /// 第 08-33 章冒煙：最大的聚落打開「領地」分頁——縮圖畫出每一棟建築（有排版、有烤圖），按鈕打開大圖、列表列出每一棟、
    /// Esc 關掉；全程不改世界。回傳摘要。
    /// </summary>
    private string VerifyHoldingSmoke(Action<string> fail)
    {
        var before = _world.ExportTempSave();
        var site = _world.Sites.Settlements.Where(s => !s.IsRuin).OrderByDescending(s => _world.HoldingOf(s.Id)?.Buildings.Count ?? 0)
            .ThenByDescending(s => _world.SettlementPopulation(s.Id)).ThenBy(s => s.Id).First();
        ShowDetailAt(site.Column, site.Row);
        SelectDetailTab("領地");
        if (_detailTabs.GetTabTitle(_detailTabs.CurrentTab) != "領地")
            fail("settlement detail has no 領地 tab.");
        var scene = _world.HoldingScene(site.Id) ?? throw new InvalidOperationException("no holding scene");
        var thumb = FindHoldingView(_detailTabs.GetCurrentTabControl());
        var picture = thumb?.Picture;
        if (thumb is null || picture is null || picture.Pieces.Count == 0 || picture.Size.X <= 0)
        {
            fail("holding tab did not draw a holding picture.");
            return "";
        }
        var shown = thumb.ShownBuildings;
        var missing = scene.Holding.Buildings.Select(b => b.Id).Where(id => !shown.Contains(id)).ToList();
        if (missing.Count > 0)
            fail($"holding picture is missing buildings: {string.Join(",", missing)}");
        var houses = picture.Pieces.Count(p => p.Kind == HoldingLayout.Kind.House);
        if (houses == 0 && scene.Holding.Dwellings + scene.Holding.LargeDwellings > 0)
            fail("holding picture shows no dwellings.");
        OpenHolding(site.Id);
        if (_modalOpen != "holding" || !_holdingPage.Visible || _holdingView.Picture is null || _holdingList.GetChildCount() < 3)
            fail("holding window did not open with the picture and the list.");
        var listed = DemoHoldingReport.Rows(scene).Sum(r => r.Count);
        if (listed != scene.Holding.Buildings.Count)
            fail($"holding list shows {listed}/{scene.Holding.Buildings.Count} buildings.");
        _holdingView.ZoomAt(_holdingView.Size / 2, 2f);
        CloseModal();
        if (_world.ExportTempSave() != before)
            fail("holding panel changed the world.");
        return $"Holding={site.Id} Buildings={scene.Holding.Buildings.Count} Houses={houses} Pieces={picture.Pieces.Count} Size={picture.Size.X}x{picture.Size.Y} LayoutMs={thumb.LayoutMs:0}";
    }

    private static HoldingView? FindHoldingView(Node? node)
    {
        if (node is null)
            return null;
        if (node is HoldingView view)
            return view;
        foreach (var child in node.GetChildren())
            if (FindHoldingView(child) is { } found)
                return found;
        return null;
    }
}
