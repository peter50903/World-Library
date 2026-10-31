using System.Globalization;
using System.Text;
using Godot;
using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08-10b 章 b 節：右側「編年史」分頁。依年份分組（新的在上，參考 FMS 國家歷史），預設只列重大事件，
/// 「一般事件」與各類型可開關（FMS 類型開關）；聚落名稱可點，地圖置中並開詳情（CK3 名稱底線）；
/// 最近 10 年以外每 10 年折疊成摘要，點開才看細節（CK3 記憶淡去，只作用在畫面）；出生老死每年一行，點開列各聚落。
/// 資料全部來自 Core <see cref="DemoWorldChronicle"/>，只讀不改世界。
/// </summary>
public partial class ObserverRoot
{
    private static readonly bool ChronicleSmoke = OS.HasEnvironment("WORLD_SIM_CHRONICLE_SMOKE");

    private PanelContainer _chroniclePanel = null!;
    private Button _chronicleToggle = null!;
    private RichTextLabel _chronicleText = null!;
    private CheckBox _chronicleShowNormal = null!;
    private MenuButton _chronicleTypes = null!;
    private readonly HashSet<DemoChronicleCategory> _chronicleHidden = new();
    private readonly HashSet<int> _chronicleExpanded = new();
    private readonly HashSet<int> _chronicleStatsOpen = new();
    private IReadOnlyList<DemoChronicleYear> _chronicleYears = Array.Empty<DemoChronicleYear>();
    private (DemoObserverWorld World, int Weeks)? _chronicleDataKey;
    private string _chronicleViewKey = string.Empty;

    private static string CategoryName(DemoChronicleCategory category) => category switch
    {
        DemoChronicleCategory.Era => "時代",
        DemoChronicleCategory.Knowledge => "技術",
        DemoChronicleCategory.Founding => "建立聚落",
        DemoChronicleCategory.Ruin => "遺跡",
        DemoChronicleCategory.Tier => "等級升降",
        DemoChronicleCategory.Farmland => "開墾",
        DemoChronicleCategory.Famine => "餓死",
        DemoChronicleCategory.Migration => "遷徙",
        DemoChronicleCategory.Hunger => "缺糧",
        DemoChronicleCategory.Representative => "代表人物",
        DemoChronicleCategory.Population => "人口里程碑",
        DemoChronicleCategory.Culture => "文化",
        DemoChronicleCategory.Faith => "信仰",
        DemoChronicleCategory.Wilds => "野外",
        _ => category.ToString()
    };

    private void BuildChronicle()
    {
        _chroniclePanel = new PanelContainer();
        Skin(_chroniclePanel);
        _hud.AddChild(_chroniclePanel);
        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        _chroniclePanel.AddChild(column);

        var header = new HBoxContainer();
        column.AddChild(header);
        var title = new Label
        {
            Text = "編年史",
            TooltipText = "依年份列出世界的重大事件（新的在上）；點聚落名稱定位地圖；較早的年份每 10 年折疊，點一下展開",
            MouseFilter = MouseFilterEnum.Pass,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        title.AddThemeFontSizeOverride("font_size", 20);
        header.AddChild(title);
        _chronicleShowNormal = new CheckBox { Text = "一般事件", TooltipText = "也列出一般事件：技術傳入、遷徙、缺糧、人口里程碑、代表人物就任與卸任" };
        _chronicleShowNormal.Toggled += _ => RefreshChronicle();
        header.AddChild(_chronicleShowNormal);
        _chronicleTypes = new MenuButton { Text = "類型 ▾", TooltipText = "勾選要列出的事件類型", Flat = false };
        var popup = _chronicleTypes.GetPopup();
        popup.HideOnCheckableItemSelection = false;
        foreach (var category in Enum.GetValues<DemoChronicleCategory>())
        {
            popup.AddCheckItem(CategoryName(category), (int)category);
            popup.SetItemChecked(popup.GetItemIndex((int)category), true);
        }
        popup.IdPressed += id => ToggleChronicleCategory((DemoChronicleCategory)(int)id);
        header.AddChild(_chronicleTypes);

        _chronicleText = new RichTextLabel
        {
            BbcodeEnabled = true,
            ScrollActive = true,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SelectionEnabled = false
        };
        _chronicleText.MetaClicked += meta => OnChronicleMeta(meta.AsString());
        column.AddChild(_chronicleText);
    }

    private void ToggleChronicleCategory(DemoChronicleCategory category)
    {
        if (!_chronicleHidden.Remove(category))
            _chronicleHidden.Add(category);
        var popup = _chronicleTypes.GetPopup();
        popup.SetItemChecked(popup.GetItemIndex((int)category), !_chronicleHidden.Contains(category));
        RefreshChronicle();
    }

    /// <summary>點擊連結：s:聚落編號＝定位地圖；fold／unfold:起始年＝展開／收合十年段；stat:年＝展開出生老死明細。</summary>
    private void OnChronicleMeta(string meta)
    {
        var parts = meta.Split(':', 2);
        if (parts.Length != 2 || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            return;
        switch (parts[0])
        {
            case "s":
                FocusSettlement(value);
                return;
            case "fold":
                _chronicleExpanded.Add(value);
                break;
            case "unfold":
                _chronicleExpanded.Remove(value);
                break;
            case "stat":
                if (!_chronicleStatsOpen.Remove(value))
                    _chronicleStatsOpen.Add(value);
                break;
            default:
                return;
        }
        RefreshChronicle();
    }

    /// <summary>分頁開著才重算；世界資料只在週結算後重建，畫面只在顯示條件改變時重寫。</summary>
    private void RefreshChronicle()
    {
        if (_chronicleText is null || _rightView != RightView.Chronicle)
            return;
        var dataKey = (_world, _world.CommittedWeeks);
        if (_chronicleDataKey != dataKey)
        {
            _chronicleDataKey = dataKey;
            _chronicleYears = DemoWorldChronicle.Build(_world);
        }
        var viewKey = string.Join("|", dataKey.CommittedWeeks, _chronicleShowNormal.ButtonPressed,
            string.Join(",", _chronicleHidden.OrderBy(c => c)), string.Join(",", _chronicleExpanded.OrderBy(y => y)),
            string.Join(",", _chronicleStatsOpen.OrderBy(y => y)), _world.GetHashCode());
        if (viewKey == _chronicleViewKey)
            return;
        _chronicleViewKey = viewKey;
        _chronicleText.Text = RenderChronicle();
    }

    private bool ChronicleShows(DemoChronicleEntry entry)
        => (entry.Level == DemoChronicleLevel.Major || _chronicleShowNormal.ButtonPressed) && !_chronicleHidden.Contains(entry.Category);

    private static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);

    private string RenderChronicle()
    {
        var years = _chronicleYears;
        if (years.Count == 0 || _world.CommittedWeeks == 0)
            return "目前沒有已結算的年份。請推進世界。";
        var current = years[^1].Year;
        var folded = DemoWorldChronicle.FoldedDecades(current).Where(d => !_chronicleExpanded.Contains(d.FromYear)).ToList();
        var names = _world.Sites.Settlements.ToDictionary(s => s.Id, s => s.Name);
        var text = new StringBuilder();
        for (var i = years.Count - 1; i >= 0; i--)
        {
            var year = years[i];
            var fold = folded.FirstOrDefault(d => year.Year >= d.FromYear && year.Year <= d.ToYear);
            if (fold != default)
            {
                if (year.Year == fold.ToYear)
                {
                    var decade = years.Where(y => y.Year >= fold.FromYear && y.Year <= fold.ToYear).ToList();
                    text.Append("[url=fold:").Append(fold.FromYear.ToString(CultureInfo.InvariantCulture)).Append("]▶ ")
                        .Append(Escape(DemoWorldChronicle.DecadeSummary(decade))).Append("[/url]\n\n");
                }
                continue;
            }
            // 展開的十年段：在最新那一年（段尾）上方放收合連結。
            if (year.Year % 10 == 0 && _chronicleExpanded.Contains(year.Year - 9))
                text.Append("[url=unfold:").Append((year.Year - 9).ToString(CultureInfo.InvariantCulture))
                    .Append("]▲ 收合第 ").Append((year.Year - 9).ToString(CultureInfo.InvariantCulture)).Append('～')
                    .Append(year.Year.ToString(CultureInfo.InvariantCulture)).Append(" 年[/url]\n");

            text.Append("[b]第 ").Append(year.Year.ToString(CultureInfo.InvariantCulture)).Append(" 年[/b]");
            if (year.EraStarted)
                text.Append("　— 進入").Append(DemoKnowledge.EraName(year.Era)).Append(" —");
            text.Append('\n');
            foreach (var entry in year.Entries.Where(ChronicleShows))
            {
                text.Append(entry.Level == DemoChronicleLevel.Major ? "◆ " : "・");
                foreach (var segment in entry.Segments)
                {
                    if (segment.SettlementId > 0)
                        text.Append("[url=s:").Append(segment.SettlementId.ToString(CultureInfo.InvariantCulture)).Append(']')
                            .Append(Escape(segment.Text)).Append("[/url]");
                    else
                        text.Append(Escape(segment.Text));
                }
                text.Append('\n');
            }
            text.Append("[url=stat:").Append(year.Year.ToString(CultureInfo.InvariantCulture)).Append("]　")
                .Append(Escape(year.StatisticLine)).Append("[/url]\n");
            if (_chronicleStatsOpen.Contains(year.Year))
            {
                foreach (var (id, stat) in year.BySettlement.OrderBy(kv => kv.Key))
                    text.Append("　　[url=s:").Append(id.ToString(CultureInfo.InvariantCulture)).Append(']')
                        .Append(Escape(names.GetValueOrDefault(id, $"#{id}"))).Append("[/url]")
                        .Append(string.Create(CultureInfo.InvariantCulture, $"：出生 {stat.Births}、老死 {stat.NaturalDeaths}\n"));
            }
            text.Append('\n');
        }
        return text.ToString();
    }

    /// <summary>
    /// 第 08-10b 章冒煙：45 年的人類採集世界；分頁切換、Core 編年史逐年相同、最近 10 年展開、較早每 10 年折疊、
    /// 展開十年段、一般事件開關、類型開關、點聚落名稱定位地圖；全程不改世界。
    /// </summary>
    private void VerifyChronicleSmoke()
    {
        void Fail(string why) => throw new InvalidOperationException("Chronicle smoke: " + why);
        const int years = 45;
        InstallWorld(CreateTempWorld(TempScenario.LivingForaging), "編年史冒煙測試");
        for (var d = 0; d < years * (_calendar?.DaysPerYear ?? 365); d++)
            _world.AdvanceOneDay();
        Refresh();
        var before = _world.ExportTempSave();

        _chronicleToggle.EmitSignal(BaseButton.SignalName.Pressed);
        if (_rightView != RightView.Chronicle || !_chroniclePanel.Visible || _outlinerPanel.Visible || _consolePanel.Visible)
            Fail("chronicle tab did not replace the right panel.");
        var expected = DemoWorldChronicle.Build(_world);
        if (_chronicleYears.Count != years || !_chronicleYears.Select(y => y.Year).SequenceEqual(expected.Select(y => y.Year))
            || !_chronicleYears.SelectMany(y => y.Entries).Select(e => e.Text).SequenceEqual(expected.SelectMany(y => y.Entries).Select(e => e.Text)))
            Fail("panel years differ from Core's chronicle.");

        string Shown() => _chronicleText.GetParsedText();
        int Lines(string prefix) => Shown().Split('\n').Count(l => l.StartsWith(prefix, StringComparison.Ordinal));
        var text = Shown();
        if (!text.Contains($"第 {years} 年", StringComparison.Ordinal) || text.Contains("\n第 1 年", StringComparison.Ordinal)
            || !text.Contains("▶ 第 1～10 年：", StringComparison.Ordinal) || Lines("▶ ") != 3)
            Fail("recent years are not expanded or old decades are not folded.");
        var visibleMajor = expected.Where(y => y.Year > 30).Sum(y => y.Entries.Count(e => e.Level == DemoChronicleLevel.Major));
        if (Lines("◆ ") != visibleMajor || Lines("・") != 0)
            Fail($"expected {visibleMajor} major lines and no normal lines, saw {Lines("◆ ")}/{Lines("・")}.");

        OnChronicleMeta("fold:1");
        if (!Shown().Contains("第 1 年", StringComparison.Ordinal) || Lines("▶ ") != 2 || Lines("▲ ") != 1)
            Fail("expanding the first decade did not show its years.");

        _chronicleShowNormal.ButtonPressed = true;
        var visibleYears = expected.Where(y => y.Year <= 10 || y.Year > 30).ToList();
        if (Lines("・") != visibleYears.Sum(y => y.Entries.Count(e => e.Level == DemoChronicleLevel.Normal)))
            Fail("normal events toggle did not list Core's normal entries.");

        var founding = expected.SelectMany(y => y.Entries).Count(e => e.Category == DemoChronicleCategory.Founding);
        ToggleChronicleCategory(DemoChronicleCategory.Founding);
        if (Shown().Contains("的開拓隊建立", StringComparison.Ordinal))
            Fail("hiding the founding type still lists foundings.");
        ToggleChronicleCategory(DemoChronicleCategory.Founding);

        var target = _world.Sites.Settlements.First();
        OnChronicleMeta($"s:{target.Id}");
        if (!_detailPanel.Visible || _hexMap.Highlight != (target.Column, target.Row))
            Fail("clicking a settlement name did not focus the map and open details.");
        HideDetail();

        OnChronicleMeta($"stat:{years}");
        if (!Shown().Contains("：出生 ", StringComparison.Ordinal))
            Fail("statistics line did not expand per settlement.");

        if (_world.ExportTempSave() != before)
            Fail("the chronicle changed the Core world.");
        GD.Print(string.Create(CultureInfo.InvariantCulture,
            $"WORLD_SIM_CHRONICLE_OK: Years={years} Major={visibleMajor} Folded=3 Expand=1 Normal=1 Category={founding} Link=1 Stats=1 CoreUnchanged=1"));
    }
}
