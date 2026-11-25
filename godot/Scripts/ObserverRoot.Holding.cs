using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-33 章：領地面板（使用者 2026-11-18：聚落點開要像 CK3 一樣看得到建築——CK3 是領地面板，但沒有蓋的就不用了，
/// 只顯示有蓋、正在蓋的、被破壞耐久度歸零的；要寫細一點，從城牆、農田、城內建築、兵營、城堡……；要視覺化，
/// 不要九宮格塞滿插圖；插槽越多代表建築越多也代表城市越大）。
/// 第 08-38 章：改成 CK3 式「插圖＋建築卡片」（使用者 2026-11-19）：
/// <list type="bullet">
/// <item>大圖視窗：上面是這個聚落的領地圖當插圖（寬幅鋪滿，滾輪縮放、拖曳看其他地方，左上角是名稱與等級）；
///   左下是領地資訊（建築格一格一格畫出來、住宅、建材、城防、升級進度、建築加成），右下是依分區排的<b>建築卡片</b>——
///   每張有插圖（施工中是工地、毀損是廢墟）、名稱、進度或耐久度條、狀態與作用。</item>
/// <item>聚落詳情的「領地」分頁：縮圖、資訊與縮小的卡片（點縮圖或按鈕打開大圖）。</item>
/// </list>
/// 第 08-40 章（使用者 2026-11-19：領地圖不用拼出來的畫面，直接放符合等級的示意圖，保留建築插槽，詳情的領地參考 CK3）：
/// 插圖改成第十三批的等級示意圖（<see cref="HoldingArt"/>）；詳情分頁照 CK3 的領地欄——示意圖與名牌、一排數值、建築格、
/// 一格一格的建築插槽（不分區）；大圖保留分區卡片。
/// 只有蓋好、正在蓋、毀損的建築才有卡片（沒蓋的不畫）；卡片越多＝建築越多＝城越大。資料來自 Core 的 <c>DemoObserverWorld.HoldingScene</c>，只讀。
/// </summary>
public partial class ObserverRoot
{
    private HoldingArt _holdingArt = null!;
    private VBoxContainer _holdingInfo = null!;
    private VBoxContainer _holdingCards = null!;
    private VBoxContainer _holdingPage = null!;
    private int _holdingSettlement;
    private int? _holdingWeek;

    /// <summary>卡片上記的建築數（冒煙測試：每棟建築都要有卡片）。</summary>
    private const string CardCountMeta = "holding_count";

    /// <summary>
    /// 第 08-33 章：領地大圖的視窗頁（由 <see cref="BuildModalHost"/> 之後呼叫）。
    /// 第 08-38 章：上排左邊是插圖、右邊是領地資訊（CK3 的領地名稱與數值）；下排是整排的建築卡片。
    /// </summary>
    private void BuildHoldingPage()
    {
        _holdingPage = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _holdingPage.AddThemeConstantOverride("separation", 8);
        var top = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, TempHoldingBannerHeight) };
        top.AddThemeConstantOverride("separation", 12);
        _holdingArt = new HoldingArt
        {
            CustomMinimumSize = new Vector2(TempHoldingBannerWidth, TempHoldingBannerHeight),
            SizeFlagsVertical = SizeFlags.ShrinkBegin
        };
        top.AddChild(_holdingArt);
        var infoScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _holdingInfo = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        infoScroll.AddChild(_holdingInfo);
        top.AddChild(infoScroll);
        _holdingPage.AddChild(top);
        var cardScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _holdingCards = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cardScroll.AddChild(_holdingCards);
        _holdingPage.AddChild(cardScroll);
        AddModalPage("holding", "領地", _holdingPage);
    }

    /// <summary>【TEMP】大圖視窗：示意圖的寬與高（上排，2:1）、視窗最大寬高；詳情裡示意圖的高度。</summary>
    private const float TempHoldingBannerWidth = 640f, TempHoldingBannerHeight = 320f, TempHoldingWindowWidth = 1280f, TempHoldingWindowHeight = 820f,
        TempHoldingThumbHeight = 200f;

    /// <summary>【TEMP】建築卡片的寬與插圖高（大圖、詳情分頁的縮小版）。</summary>
    private const float TempCardWidth = 148f, TempCardArtHeight = 112f, TempCompactCardWidth = 98f, TempCompactArtHeight = 66f;

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
        _holdingArt.ShowScene(scene, TitledName(scene.Site),
            $"{DemoSettlementHistory.TierName(scene.Site, scene.Tier)}｜居民 {scene.Residents} 人｜建築格 {scene.SlotsUsed}/{scene.Slots}", clickable: false);
        FillHoldingInfo(_holdingInfo, scene, compact: false);
        FillHoldingCards(_holdingCards, scene, compact: false);
    }

    /// <summary>由 <see cref="Refresh"/> 呼叫：大圖開著、又過了一週就更新（第 08-39 章：快轉時滑鼠在視窗上或剛更新過就晚點再更新）。</summary>
    private void RefreshHoldingIfOpen()
    {
        if (_modalOpen == "holding" && _world.LatestWeek?.Week != _holdingWeek && !HoldingBusy())
        {
            RefreshHoldingWindow();
            _holdingRebuilt.Restart();
        }
    }

    /// <summary>
    /// 第 08-40 章：詳情「領地」分頁，照 CK3 的領地欄——上面是等級示意圖與名牌（點一下打開大圖），一排數值（居民、住宅、農田、守軍、城防、建材），
    /// 等級與建築格，升級進度一行，下面是一格一格的建築插槽（不分區，依分區順序排）。
    /// </summary>
    private Control HoldingTabContent(DemoHoldingScene scene)
    {
        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 6);
        var id = scene.Site.Id;
        var art = new HoldingArt { CustomMinimumSize = new Vector2(0, TempHoldingThumbHeight), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        // 第 08-52 章（試玩回饋 2026-11-26 之 3）：不再另開領地視窗，示意圖就在聚落頁最上面。
        art.ShowScene(scene, TitledName(scene.Site), $"{DemoSettlementHistory.TierName(scene.Site, scene.Tier)}｜建築格 {scene.SlotsUsed}/{scene.Slots}",
            clickable: false);
        column.AddChild(art);
        column.AddChild(HoldingStats(scene, _world.CombatForceOf(id).Soldiers));
        column.AddChild(SlotPips(scene.SlotsUsed, scene.Slots, compact: true));
        if (scene.Progress is { } progress)
            column.AddChild(IconRow("ic_stat_22", progress.Headline, null,
                string.Join("\n", progress.Steps.Select(s => s.Line)) + "\n（人口夠、本級設施任兩種解鎖並蓋好建築，下次週結算就升級）"));
        if (scene.Bonus.Line() is { } bonus)
            column.AddChild(IconRow("b12_ic_cat_admin", bonus, null, "完好的建築才有作用；同一種只算一次（城牆、塔樓、城堡看城防）"));
        var rows = DemoHoldingReport.Rows(scene);
        if (rows.Count > 0)
        {
            column.AddChild(IconRow("b12_ic_cat_industry", "建築", null, "只列蓋好、正在蓋、毀損的建築；住宅與農田不佔建築格（見上面的數值）", 20));
            var slots = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            slots.AddThemeConstantOverride("h_separation", 5);
            slots.AddThemeConstantOverride("v_separation", 5);
            foreach (var r in rows)
                slots.AddChild(BuildingCard(r, scene, compact: true));
            column.AddChild(slots);
        }
        else
            column.AddChild(new Label { Text = "（住宅以外還沒有任何建築）" });
        return column;
    }

    /// <summary>第 08-40 章：CK3 領地欄的一排數值（圖示＋數字，滑鼠移上去看說明）。</summary>
    private static HFlowContainer HoldingStats(DemoHoldingScene scene, long soldiers)
    {
        var h = scene.Holding;
        var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", 10);
        flow.AddThemeConstantOverride("v_separation", 4);
        void Stat(string icon, string text, string tip)
        {
            var box = new HBoxContainer { TooltipText = tip };
            box.AddThemeConstantOverride("separation", 3);
            if (PixelParts.Texture(icon) is { } texture)
                box.AddChild(Picture(texture, 18));
            box.AddChild(new Label { Text = text });
            flow.AddChild(box);
        }
        Stat("ic_stat_01", $"{scene.Residents}", "居民");
        Stat(scene.Residents > h.Housing ? "b12_ic_state_crowded" : "b12_ic_cat_housing", $"{h.Housing}", DemoHoldingReport.HousingLine(scene));
        if (scene.Fields > 0)
            Stat("b12_ic_cat_field", $"{scene.Fields}", $"附近已開墾的農田 {scene.Fields} 塊（不佔建築格）");
        if (soldiers > 0)
            Stat("b12_ic_cat_military", $"{soldiers}", "守軍（士兵）");
        if (DemoDefense.Of(h) is { Any: true } defense)
            Stat("b12_ic_cat_defense", $"+{defense.MonsterPercent}%", defense.Describe());
        Stat("ic_stat_25", $"{h.Wood}", "建材庫存：木");
        Stat("ic_stat_26", $"{h.Stone}", "建材庫存：石");
        if (h.Iron > 0)
            Stat("b12_ic_res_iron", $"{h.Iron}", "建材庫存：鐵");
        return flow;
    }

    private static void ClearChildren(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            node.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    /// 第 08-38 章：領地資訊——等級與建築格（一格一格畫出來：格越多城越大）、住宅、建材、城防、升級進度（第 08-36 章）、建築加成（第 08-37 章）。
    /// </summary>
    private static void FillHoldingInfo(VBoxContainer info, DemoHoldingScene scene, bool compact)
    {
        ClearChildren(info);
        var h = scene.Holding;
        var head = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        head.AddThemeConstantOverride("separation", 6);
        if (ArtAssets.Texture(ArtAssets.SettlementTierIcon(scene.Tier)) is { } crest)
            head.AddChild(Picture(crest, compact ? 22 : 28));
        var title = new Label
        {
            Text = $"{DemoSettlementHistory.TierName(scene.Site, scene.Tier)}｜建築格 {scene.SlotsUsed}/{scene.Slots}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            TooltipText = $"住宅、城牆、城門不佔格；同時施工最多 {DemoBuildings.Projects(scene.Tier)} 項；等級越高格越多（營 3、村 8、鎮 16、城 26、都 36）"
        };
        title.AddThemeFontSizeOverride("font_size", compact ? 15 : 17);
        head.AddChild(title);
        info.AddChild(head);
        info.AddChild(SlotPips(scene.SlotsUsed, scene.Slots, compact));
        info.AddChild(IconRow("b12_ic_cat_housing", DemoHoldingReport.HousingLine(scene),
            scene.Residents > h.Housing ? "b12_ic_state_crowded" : scene.HousePercent > 0 ? "b12_ic_state_building" : null));
        var stock = new HBoxContainer();
        stock.AddThemeConstantOverride("separation", 4);
        foreach (var (icon, label, amount) in new[] { ("ic_stat_25", "木", h.Wood), ("ic_stat_26", "石", h.Stone), ("b12_ic_res_iron", "鐵", h.Iron) })
        {
            if (PixelParts.Texture(icon) is { } texture)
                stock.AddChild(Picture(texture, 18));
            stock.AddChild(new Label { Text = $"{label} {amount}　", TooltipText = $"建材庫存：{label}" });
        }
        info.AddChild(stock);
        if (DemoDefense.Of(h) is { Any: true } defense)
            info.AddChild(IconRow("b12_ic_cat_defense", defense.Describe(), null,
                "第 08-34 章：城牆與城門、塔樓、城堡讓守方佔便宜（怪物來襲、大軍攻城）；被打壞的下週才少")); // 城防
        if (scene.Progress is { } progress)
            AddTierProgress(info, progress); // 第 08-36 章
        if (scene.Bonus.Any)
        {
            // 第 08-37 章：建築的作用（相加後），每項配分類圖示。
            info.AddChild(new HSeparator());
            info.AddChild(IconRow("b12_ic_cat_admin", "建築加成", null, "完好的建築才有作用；同一種蓋好幾座只算一次（城牆、塔樓、城堡看城防）", 22));
            foreach (var (kind, text) in scene.Bonus.Parts())
                info.AddChild(IconRow(BonusIcon(kind), text));
        }
    }

    /// <summary>建築格：用了的格子塗滿，空的格子留框（格子數＝等級的建築格）。</summary>
    private static HFlowContainer SlotPips(int used, int slots, bool compact)
    {
        var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, TooltipText = $"建築格：用了 {used} 格、共 {slots} 格" };
        flow.AddThemeConstantOverride("h_separation", 3);
        flow.AddThemeConstantOverride("v_separation", 3);
        var size = compact ? 9f : 11f;
        for (var i = 0; i < slots; i++)
        {
            var pip = new Panel { CustomMinimumSize = new Vector2(size, size), MouseFilter = MouseFilterEnum.Ignore };
            pip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = i < used ? TempGold : new Color(0.10f, 0.08f, 0.06f, 0.55f),
                BorderColor = TempPanelBorder,
                BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1
            });
            flow.AddChild(pip);
        }
        if (used > slots) // 降級後格子比建築少：多出來的照樣畫（紅框）
            for (var i = slots; i < used; i++)
            {
                var pip = new Panel { CustomMinimumSize = new Vector2(size, size), MouseFilter = MouseFilterEnum.Ignore };
                pip.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = TempGold.Darkened(0.3f), BorderColor = new Color(0.8f, 0.25f, 0.2f),
                    BorderWidthLeft = 1, BorderWidthTop = 1, BorderWidthRight = 1, BorderWidthBottom = 1
                });
                flow.AddChild(pip);
            }
        return flow;
    }

    /// <summary>
    /// 第 08-38 章：建築卡片依分區排——住宅、城牆與塔樓、農田與農舍、城內建築、神殿與學府、兵營與訓練場、城堡與官署、城外資源；
    /// 每區一個標題，下面是一排排卡片（放不下就換行）。
    /// </summary>
    private static void FillHoldingCards(VBoxContainer cards, DemoHoldingScene scene, bool compact)
    {
        ClearChildren(cards);
        var h = scene.Holding;
        var crowded = scene.Residents > h.Housing;
        AddCardZone(cards, $"住宅（居民 {scene.Residents} 人／住得下 {h.Housing} 人{(crowded ? "，不夠住" : "")}）", "b12_ic_cat_housing",
            HouseCards(scene, compact), compact);
        var rows = DemoHoldingReport.Rows(scene);
        foreach (var zone in Enum.GetValues<DemoHoldingZone>())
        {
            var inZone = rows.Where(r => r.Zone == zone).Select(r => (Control)BuildingCard(r, scene, compact)).ToList();
            if (zone == DemoHoldingZone.Fields && scene.Fields > 0)
                inZone.Insert(0, Card("農田", $"附近已開墾 {scene.Fields} 塊", null, "b11_g_field_wheat", null, true, ZoneBackdrop(zone), DemoBuildingStatus.Standing,
                    100, false, false, scene.Fields, "農田是開墾出來的地，不佔建築格；農舍照料附近的田", compact));
            if (inZone.Count > 0)
                AddCardZone(cards, DemoHoldingReport.ZoneName(zone), ZoneIcon(zone), inZone, compact);
        }
        if (rows.Count == 0)
            cards.AddChild(new Label { Text = "（住宅以外還沒有任何建築）" });
    }

    private static void AddCardZone(VBoxContainer cards, string title, string icon, IReadOnlyList<Control> zoneCards, bool compact)
    {
        if (zoneCards.Count == 0)
            return;
        if (cards.GetChildCount() > 0)
            cards.AddChild(new HSeparator());
        cards.AddChild(IconRow(icon, title, null, null, compact ? 18 : 22));
        var flow = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        flow.AddThemeConstantOverride("h_separation", compact ? 5 : 8);
        flow.AddThemeConstantOverride("v_separation", compact ? 5 : 8);
        foreach (var card in zoneCards)
            flow.AddChild(card);
        cards.AddChild(flow);
    }

    /// <summary>住宅的卡片：普通住宅（營地是棚屋）、大型住宅、毀損的住宅、正在蓋的那一棟。</summary>
    private static List<Control> HouseCards(DemoHoldingScene scene, bool compact)
    {
        var h = scene.Holding;
        var backdrop = new Color(0.50f, 0.38f, 0.26f);
        var list = new List<Control>();
        var basic = scene.Tier == DemoSettlementTier.Camp ? "棚屋" : "住宅";
        if (h.Dwellings > 0)
            list.Add(Card(basic, $"每棟 {DemoBuildings.HouseCapacity} 人", null, HoldingSprites.HouseCard(scene.Tier, false), null, false, backdrop,
                DemoBuildingStatus.Standing, 100, false, false, h.Dwellings, $"{basic}：住得下 {(long)h.Dwellings * DemoBuildings.HouseCapacity} 人（住宅不佔建築格）", compact));
        if (h.LargeDwellings > 0)
            list.Add(Card("大型住宅", $"每棟 {DemoBuildings.LargeHouseCapacity} 人", null, HoldingSprites.HouseCard(scene.Tier, true), null, false,
                backdrop, DemoBuildingStatus.Standing, 100, false, false, h.LargeDwellings,
                $"大型住宅：住得下 {(long)h.LargeDwellings * DemoBuildings.LargeHouseCapacity} 人", compact));
        if (h.RuinedDwellings > 0)
            list.Add(Card("毀損的住宅", "等待重建", null, "b12_ruin_wood_s", null, false, backdrop, DemoBuildingStatus.Ruined, 0, false, false,
                h.RuinedDwellings, "被怪物、戰火或天災毀掉的住宅；重建時先補回這些", compact));
        if (scene.HousePercent > 0)
        {
            var house = HoldingSprites.HouseCard(scene.Tier, scene.House.Id == DemoBuildings.HouseLarge);
            list.Add(Card($"新的{scene.House.Name.Split('/')[0]}", $"施工中 {scene.HousePercent}%", null, house,
                HoldingSprites.SiteSprite(HoldingSprites.SizeOf(house).X, scene.HousePercent / 100f), false, backdrop, DemoBuildingStatus.Building,
                scene.HousePercent, true, false, 1, $"住宅一次蓋一棟（{scene.House.Hours} 工時）", compact));
        }
        return list;
    }

    /// <summary>一棟（或同一種、完好的幾棟）建築的卡片。</summary>
    private static PanelContainer BuildingCard(DemoHoldingRow r, DemoHoldingScene scene, bool compact)
    {
        var progress = r.Status == DemoBuildingStatus.Building ? r.Percent / 100f : 1f;
        var (sprite, overlay, tiled) = HoldingSprites.CardArt(r.DefId, r.BuildingId, r.Status, progress, scene.Tier);
        var effect = DemoHoldingReport.EffectText(r); // 第 08-37 章
        var state = DemoHoldingReport.StateText(r.Status, r.Percent);
        var tip = DemoBuildingCatalog.Contains(r.DefId)
            ? $"{DemoBuildingCatalog.Get(r.DefId).Name}（{DemoBuildingCatalog.Get(r.DefId).Category}）\n{state}"
            : $"{r.Name}\n{state}";
        if (effect is not null)
            tip += $"\n作用：{effect}";
        if (DemoBuildings.IsWallwork(r.DefId) && DemoDefense.Of(scene.Holding) is { Any: true } defense)
            tip += "\n" + defense.Describe();
        var damaged = r.Status == DemoBuildingStatus.Standing && r.Percent < 100;
        var card = Card(r.Name, state, effect, sprite, overlay, tiled, ZoneBackdrop(r.Zone), r.Status, r.Percent,
            r.Status != DemoBuildingStatus.Ruined, damaged, r.Count, tip, compact);
        card.SetMeta(CardCountMeta, r.Count);
        return card;
    }

    /// <summary>
    /// 一張卡片：插圖（<see cref="HoldingCardArt"/>）、名稱、進度或耐久度條、狀態、作用。框的顏色看狀態：完好金框、受損橘框、施工中藍框、毀損暗紅框。
    /// </summary>
    private static PanelContainer Card(string title, string state, string? effect, string sprite, string? overlay, bool tiled, Color backdrop,
        DemoBuildingStatus status, int percent, bool showBar, bool damaged, int count, string? tooltip, bool compact)
    {
        var border = status switch
        {
            DemoBuildingStatus.Building => new Color(0.40f, 0.62f, 0.86f),
            DemoBuildingStatus.Ruined => new Color(0.62f, 0.22f, 0.17f),
            _ when damaged => new Color(0.86f, 0.55f, 0.22f),
            _ => TempPanelBorder
        };
        var card = new PanelContainer
        {
            CustomMinimumSize = new Vector2(compact ? TempCompactCardWidth : TempCardWidth, 0),
            TooltipText = tooltip ?? title,
            MouseFilter = MouseFilterEnum.Stop
        };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = status == DemoBuildingStatus.Ruined ? new Color(0.09f, 0.06f, 0.05f, 0.96f) : new Color(0.13f, 0.11f, 0.08f, 0.96f),
            BorderColor = border,
            BorderWidthLeft = 2, BorderWidthTop = 2, BorderWidthRight = 2, BorderWidthBottom = 2,
            CornerRadiusTopLeft = 5, CornerRadiusTopRight = 5, CornerRadiusBottomLeft = 5, CornerRadiusBottomRight = 5,
            ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 4, ContentMarginBottom = 4
        });
        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", compact ? 1 : 2);
        card.AddChild(column);
        var art = new HoldingCardArt { CustomMinimumSize = new Vector2(0, compact ? TempCompactArtHeight : TempCardArtHeight) };
        var badge = status switch
        {
            DemoBuildingStatus.Building => "b12_ic_state_building",
            DemoBuildingStatus.Ruined => "b12_ic_state_destroyed",
            _ when damaged => "b12_ic_state_damaged",
            _ => null
        };
        art.SetArt(sprite, overlay, tiled, backdrop, status, damaged, count, badge);
        column.AddChild(art);
        column.AddChild(CardLabel(title, compact ? 12 : 14, new Color(1f, 0.93f, 0.78f)));
        if (showBar)
            column.AddChild(CardBar(status, percent, compact));
        if (!compact || status != DemoBuildingStatus.Standing || damaged)
            column.AddChild(CardLabel(state, compact ? 10 : 12, status switch
            {
                DemoBuildingStatus.Building => new Color(0.66f, 0.82f, 1f),
                DemoBuildingStatus.Ruined => new Color(0.96f, 0.56f, 0.46f),
                _ when damaged => new Color(0.98f, 0.74f, 0.42f),
                _ => new Color(0.80f, 0.74f, 0.62f)
            }));
        if (effect is not null)
            column.AddChild(CardLabel(effect, compact ? 10 : 12, new Color(0.64f, 0.88f, 0.52f)));
        return card;
    }

    private static Label CardLabel(string text, int size, Color color)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.AddThemeFontSizeOverride("font_size", size);
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    /// <summary>進度條（施工中，藍色）或耐久度條（綠、黃、紅）。</summary>
    private static ProgressBar CardBar(DemoBuildingStatus status, int percent, bool compact)
    {
        var fill = status == DemoBuildingStatus.Building ? new Color(0.38f, 0.62f, 0.92f)
            : percent >= 70 ? new Color(0.42f, 0.75f, 0.35f) : percent >= 40 ? new Color(0.86f, 0.72f, 0.28f) : new Color(0.85f, 0.32f, 0.22f);
        var bar = new ProgressBar
        {
            MinValue = 0, MaxValue = 100, Value = Math.Clamp(percent, 0, 100), ShowPercentage = false,
            CustomMinimumSize = new Vector2(0, compact ? 4 : 6), MouseFilter = MouseFilterEnum.Ignore
        };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0.05f, 0.04f, 0.03f) });
        bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = fill });
        return bar;
    }

    /// <summary>第 08-38 章：卡片插圖的底色（依分區：城牆灰藍、農田綠、城內褐、神殿紫、兵營紅、城堡金、城外土色）。</summary>
    private static Color ZoneBackdrop(DemoHoldingZone zone) => zone switch
    {
        DemoHoldingZone.Walls => new Color(0.36f, 0.40f, 0.46f),
        DemoHoldingZone.Fields => new Color(0.38f, 0.52f, 0.28f),
        DemoHoldingZone.Town => new Color(0.52f, 0.42f, 0.28f),
        DemoHoldingZone.Sacred => new Color(0.42f, 0.36f, 0.52f),
        DemoHoldingZone.Military => new Color(0.52f, 0.30f, 0.26f),
        DemoHoldingZone.Keep => new Color(0.56f, 0.46f, 0.24f),
        _ => new Color(0.40f, 0.38f, 0.26f)
    };

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
    /// 第 08-33 章冒煙：最大的聚落打開「領地」分頁與大圖；第 08-40 章：分頁有等級示意圖（第十三批，沒交件時是暫代圖）、
    /// 分頁的建築插槽與大圖的卡片都涵蓋每一棟建築；Esc 關掉；全程不改世界。回傳摘要。
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
        var art = FindHoldingArt(_detailTabs.GetCurrentTabControl());
        if (art is null || !art.WantedPath.StartsWith("res://assets/holding/b13_", StringComparison.Ordinal))
            fail("holding tab has no tier illustration.");
        var tabCards = CardCount(_detailTabs.GetCurrentTabControl());
        if (tabCards != scene.Holding.Buildings.Count)
            fail($"holding tab shows {tabCards}/{scene.Holding.Buildings.Count} buildings as slots.");
        OpenHolding(site.Id);
        if (_modalOpen != "holding" || !_holdingPage.Visible || _holdingArt.WantedPath.Length == 0 || _holdingInfo.GetChildCount() < 3)
            fail("holding window did not open with the illustration and the info.");
        var cards = CardCount(_holdingCards);
        if (cards != scene.Holding.Buildings.Count)
            fail($"holding window shows {cards}/{scene.Holding.Buildings.Count} buildings as cards.");
        CloseModal();
        if (_world.ExportTempSave() != before)
            fail("holding panel changed the world.");
        var wanted = System.IO.Path.GetFileNameWithoutExtension(art?.WantedPath ?? "");
        return $"Holding={site.Id} Buildings={scene.Holding.Buildings.Count} Cards={cards} Art={wanted} Delivered={(art?.ArtPath is not null ? "yes" : "no")}";
    }

    private static HoldingArt? FindHoldingArt(Node? node)
    {
        if (node is null)
            return null;
        if (node is HoldingArt art)
            return art;
        foreach (var child in node.GetChildren())
            if (FindHoldingArt(child) is { } found)
                return found;
        return null;
    }

    /// <summary>第 08-38 章：一個節點底下的卡片共代表幾棟建築（住宅、農田的卡片不算）。</summary>
    private static int CardCount(Node? node)
    {
        if (node is null)
            return 0;
        var count = node.HasMeta(CardCountMeta) ? node.GetMeta(CardCountMeta).AsInt32() : 0;
        foreach (var child in node.GetChildren())
            count += CardCount(child);
        return count;
    }
}
