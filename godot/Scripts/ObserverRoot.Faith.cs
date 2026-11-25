using Godot;
using System.Globalization;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-55 章（試玩回饋 2026-11-26 第二次之 1：信仰要跟 CK3 一樣有自己獨立的頁面，才比較好辨識）：信仰視窗。
/// 參考 CK3 的信仰介面——左邊是全世界的信仰（依家族排：源頭在上、分裂出來的縮排在下面，每列有符號、顏色與信徒數），
/// 右邊是選到的信仰：大符號與名字、信徒與聚落數、狂熱度、聖地在誰手裡、家族（母信仰與分裂出去的），再來是信條與教義、信徒分布、歷史。
/// 從功能列「信仰」、信仰模式點地圖、圖例的信仰名、聚落頁的「信仰：」連結打開；「在地圖上看」切到信仰模式並選起這個信仰。只讀。
/// </summary>
public partial class ObserverRoot
{
    private readonly HBoxContainer _faithPage = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
    private VBoxContainer _faithList = null!;
    private VBoxContainer _faithBody = null!;
    private int _faithShown;
    private int? _faithWeek;

    /// <summary>【TEMP】信仰視窗的最大寬高、左邊清單的寬、右邊大符號的大小。</summary>
    private const float TempFaithWindowWidth = 980f, TempFaithWindowHeight = 680f, TempFaithListWidth = 280f, TempFaithSignSize = 84f;

    private void BuildFaithPage()
    {
        _faithPage.AddThemeConstantOverride("separation", 12);
        var listScroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(TempFaithListWidth, 0),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _faithList = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _faithList.AddThemeConstantOverride("separation", 2);
        listScroll.AddChild(_faithList);
        _faithPage.AddChild(listScroll);
        _faithPage.AddChild(new VSeparator());
        var bodyScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _faithBody = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _faithBody.AddThemeConstantOverride("separation", 6);
        bodyScroll.AddChild(_faithBody);
        _faithPage.AddChild(bodyScroll);
        AddModalPage("faith", "信仰", _faithPage);
    }

    /// <summary>打開信仰視窗，選到某個信仰（0＝信徒最多的那個）。</summary>
    private void OpenFaith(int faithId = 0)
    {
        _faithShown = faithId;
        if (_modalOpen != "faith")
            ToggleModal("faith");
        else
            RefreshFaithWindow();
    }

    /// <summary>視窗開著、又過了一週就更新。</summary>
    private void RefreshFaithIfOpen()
    {
        if (_modalOpen == "faith" && _world.LatestWeek?.Week != _faithWeek && !HoldingBusy())
            RefreshFaithWindow();
    }

    /// <summary>每個信仰的估計信徒（聚落人口×比例）與以它為主要信仰的聚落數。</summary>
    private Dictionary<int, (long People, int Places)> FaithFollowers()
    {
        var state = _world.Culture.Faith;
        var result = new Dictionary<int, (long, int)>();
        foreach (var site in _world.Sites.Settlements.Where(s => !s.IsRuin))
        {
            var people = _world.SettlementPopulation(site.Id);
            foreach (var share in state.SharesOf(site.Id).Where(x => x.CultureId != 0))
            {
                var (p, n) = result.GetValueOrDefault(share.CultureId);
                result[share.CultureId] = (p + people * share.Permille / 1000, n);
            }
            if (state.MainFaithOf(site.Id) is { } main && main != 0)
            {
                var (p, n) = result.GetValueOrDefault(main);
                result[main] = (p, n + 1);
            }
        }
        return result;
    }

    /// <summary>信仰依家族排：源頭依創立先後，分裂出來的接在母信仰下面（縮排一層）；失傳的排最後。</summary>
    private List<(DemoFaith Faith, int Depth)> FaithTree()
    {
        var faiths = _world.Culture.Faith.Faiths.ToList();
        var ids = faiths.Select(f => f.Id).ToHashSet();
        var children = faiths.Where(f => f.ParentId != 0 && ids.Contains(f.ParentId)).GroupBy(f => f.ParentId)
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f.FoundedWeek).ThenBy(f => f.Id).ToList());
        var order = new List<(DemoFaith, int)>();
        void Walk(DemoFaith f, int depth)
        {
            order.Add((f, depth));
            foreach (var child in children.GetValueOrDefault(f.Id) ?? new List<DemoFaith>())
                Walk(child, Math.Min(depth + 1, 4));
        }
        foreach (var root in faiths.Where(f => f.ParentId == 0 || !ids.Contains(f.ParentId)).OrderBy(f => f.FoundedWeek).ThenBy(f => f.Id))
            Walk(root, 0);
        return order.OrderBy(x => x.Item1.ExtinctWeek > 0).ToList(); // 失傳的排最後（各自保持家族順序）
    }

    private Control FaithSignControl(int faithId, float size)
    {
        var disc = new PanelContainer { SizeFlagsVertical = SizeFlags.ShrinkCenter };
        var color = _hexMap.FaithColor(faithId);
        var radius = (int)(size / 2f + 4f);
        disc.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.09f, 0.07f), BorderColor = color, BorderWidthLeft = 3, BorderWidthTop = 3, BorderWidthRight = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = radius, CornerRadiusTopRight = radius, CornerRadiusBottomLeft = radius, CornerRadiusBottomRight = radius,
            ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 4, ContentMarginBottom = 4
        });
        if (_hexMap.FaithSymbolsById.TryGetValue(faithId, out var symbol) && Heraldry.FaithSymbol(symbol, color) is { } sign)
            disc.AddChild(Picture(sign, size));
        else
            disc.AddChild(new Control { CustomMinimumSize = new Vector2(size, size) });
        return disc;
    }

    private void RefreshFaithWindow()
    {
        _faithWeek = _world.LatestWeek?.Week;
        foreach (var child in _faithList.GetChildren())
        {
            _faithList.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var child in _faithBody.GetChildren())
        {
            _faithBody.RemoveChild(child);
            child.QueueFree();
        }
        var state = _world.Culture.Faith;
        var followers = FaithFollowers();
        var world = Math.Max(1L, _world.SettledPopulation);
        var tree = FaithTree();
        var alive = tree.Count(x => x.Faith.ExtinctWeek == 0);
        _modalTitle.Text = alive == 0 ? "信仰" : $"信仰（現存 {alive} 個）";
        if (tree.Count == 0)
        {
            _faithBody.AddChild(WarLabel("世上還沒有任何信仰。信仰會在聚落裡誕生（文化與知識發展到一定程度時）。", 16));
            return;
        }
        if (_faithShown == 0 || state.Faith(_faithShown) is null)
            _faithShown = tree.Where(x => x.Faith.ExtinctWeek == 0).OrderByDescending(x => followers.GetValueOrDefault(x.Faith.Id).People)
                .Select(x => x.Faith.Id).FirstOrDefault(tree[0].Faith.Id);

        // 左：全世界的信仰（家族縮排、符號、顏色、信徒數）。
        foreach (var (faith, depth) in tree)
        {
            var id = faith.Id;
            var row = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            row.AddThemeConstantOverride("separation", 4);
            row.AddChild(new Control { CustomMinimumSize = new Vector2(depth * 16f, 0) });
            row.AddChild(FaithSignControl(id, 22f));
            var (people, places) = followers.GetValueOrDefault(id);
            var button = new Button
            {
                Text = faith.ExtinctWeek > 0 ? $"{faith.Name}（失傳）" : $"{faith.Name}　{people} 人",
                Alignment = HorizontalAlignment.Left,
                ToggleMode = true,
                ButtonPressed = id == _faithShown,
                FocusMode = FocusModeEnum.None,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true,
                TooltipText = faith.ExtinctWeek > 0 ? "已無信徒" : $"信徒約 {people} 人、{places} 個聚落以它為主要信仰" + (depth > 0 ? "｜由上一個信仰分裂出來" : "")
            };
            if (faith.ExtinctWeek > 0)
                button.Modulate = new Color(1f, 1f, 1f, 0.55f);
            button.Pressed += () =>
            {
                _faithShown = id;
                RefreshFaithWindow();
            };
            row.AddChild(button);
            _faithList.AddChild(row);
        }

        // 右：選到的信仰。
        if (state.Faith(_faithShown) is not { } shown)
            return;
        var head = new HBoxContainer();
        head.AddThemeConstantOverride("separation", 12);
        head.AddChild(FaithSignControl(shown.Id, TempFaithSignSize));
        var titles = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        titles.AddChild(WarLabel(shown.Name, 26, TempWarInk));
        var (count, homes) = followers.GetValueOrDefault(shown.Id);
        titles.AddChild(WarLabel(shown.ExtinctWeek > 0
            ? $"第 {WarYear(shown.ExtinctWeek)} 年起失傳"
            : $"信徒約 {count} 人（全世界的 {count * 100 / world}%）｜{homes} 個聚落以它為主要信仰", 16));
        var swatch = new ColorRect { Color = _hexMap.FaithColor(shown.Id), CustomMinimumSize = new Vector2(120, 10), TooltipText = "地圖上信仰模式的顏色" };
        titles.AddChild(swatch);
        head.AddChild(titles);
        _faithBody.AddChild(head);

        // 聖地：在誰手裡（CK3：聖地被誰控制）。
        if (_world.Sites.Settlements.FirstOrDefault(s => s.Id == shown.FounderSettlementId) is { } holy)
        {
            var owner = _hexMap.Territory.RealmOf(holy.Id);
            var ownerFaith = state.MainFaithOf(owner);
            var keeper = _world.Sites.Settlements.FirstOrDefault(s => s.Id == owner) is { } lord ? TitledName(lord) : "無人";
            var held = holy.IsRuin ? "已成遺跡" : ownerFaith == shown.Id ? "在信徒手中" : $"落在{keeper}（{(ownerFaith is { } of && state.Faith(of) is { } other ? other.Name : "無信仰")}）手中";
            var holyRow = new HBoxContainer();
            holyRow.AddChild(WarLabel($"聖地：{TitledName(holy)}｜{held}", 16, new Color(0.45f, 0.30f, 0.05f)));
            var go = new Button { Text = "看聖地", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkEnd };
            var (column, rowAt) = (holy.Column, holy.Row);
            go.Pressed += () =>
            {
                CloseModal();
                ShowFaithOnMap(shown.Id);
                _hexMap.FocusCell(_world.Map.GetCell(column, rowAt));
            };
            holyRow.AddChild(go);
            _faithBody.AddChild(holyRow);
        }

        // 家族：母信仰與分裂出去的信仰（可以點）。
        var family = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        family.AddThemeConstantOverride("h_separation", 6);
        void Link(string prefix, DemoFaith f)
        {
            var id = f.Id;
            var link = new Button { Text = $"{prefix}{f.Name}", FocusMode = FocusModeEnum.None, TooltipText = "看這個信仰" };
            link.Pressed += () =>
            {
                _faithShown = id;
                RefreshFaithWindow();
            };
            family.AddChild(link);
        }
        if (shown.ParentId != 0 && state.Faith(shown.ParentId) is { } parent)
            Link("母信仰：", parent);
        foreach (var child in state.Faiths.Where(f => f.ParentId == shown.Id).OrderBy(f => f.FoundedWeek))
            Link("分裂出：", child);
        if (family.GetChildCount() > 0)
            _faithBody.AddChild(family);

        var look = new Button { Text = "在地圖上看（信仰模式）", FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ShrinkBegin };
        var lookId = shown.Id;
        look.Pressed += () =>
        {
            CloseModal();
            ShowFaithOnMap(lookId);
        };
        _faithBody.AddChild(look);

        // 信條與教義、信徒分布、歷史（Core 的信仰頁，跟原本的詳情同一份內容）。
        if (_world.FaithDetail(shown.Id) is { } detail)
            foreach (var tab in detail.Tabs)
            {
                _faithBody.AddChild(new HSeparator());
                _faithBody.AddChild(WarLabel(tab.Title, 19, TempWarInk));
                var lines = tab.Title == "信條" ? tab.Lines.Skip(1) : tab.Lines; // 第一行是名字（上面已經有了）
                _faithBody.AddChild(WarLabel(string.Join("\n", lines).Trim('\n'), 15));
            }
    }

    /// <summary>切到信仰模式並選起這個信仰（它的地框起、聖地閃動）。</summary>
    private void ShowFaithOnMap(int faithId)
    {
        SetMapMode(DemoHexMap.Mode.Faith);
        _hexMap.SelectFaith(faithId);
    }

    /// <summary>第 08-55 章冒煙測試：打開信仰視窗、左右都有內容、關掉。</summary>
    private void VerifyFaithSmoke(Action<string> fail)
    {
        OpenFaith();
        if (_modalOpen != "faith")
            fail("信仰視窗打不開。");
        RefreshFaithWindow();
        if (_faithBody.GetChildCount() == 0)
            fail("信仰視窗是空的。");
        if (_world.Culture.Faith.Faiths.Count > 0 && _faithList.GetChildCount() != _world.Culture.Faith.Faiths.Count)
            fail($"信仰視窗左邊列了 {_faithList.GetChildCount()} 個，世界上有 {_world.Culture.Faith.Faiths.Count} 個。");
        CloseModal();
    }
}
