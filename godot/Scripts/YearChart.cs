using Godot;
using WorldSimulation.Core.World.Demo;

namespace WorldSimulation.GodotClient;

/// <summary>
/// 第 08 章 b 節：年度長條圖。資料只來自 Core 由已提交週報彙整的年帳（<see cref="DemoYearLedger"/>），
/// 這裡只畫、不算世界；每根長條的數值就是 <see cref="Values"/> 回傳的年帳欄位（冒煙逐年比對）。
/// 點某一年的長條 → <see cref="YearSelected"/>。畫面尺寸與顏色為 TEMP 排版值。
/// </summary>
public partial class YearChart : Control
{
    /// <summary>一組圖表系列：名稱與各條長條（名稱、顏色、取值）。</summary>
    public sealed record Series(string Name, IReadOnlyList<(string Label, Color Color, Func<DemoYearSummary, long> Value)> Bars);

    /// <summary>四組系列（人口；出生與死亡；遷徙；糧食採收與缺口）。顏色為 TEMP。</summary>
    public static readonly IReadOnlyList<Series> AllSeries = new[]
    {
        new Series("人口", new (string, Color, Func<DemoYearSummary, long>)[]
        {
            ("年末人口", new Color(0.24f, 0.42f, 0.62f), y => y.PopulationClosing)
        }),
        new Series("出生與死亡", new (string, Color, Func<DemoYearSummary, long>)[]
        {
            ("出生", new Color(0.30f, 0.58f, 0.30f), y => y.Births),
            ("餓死", new Color(0.72f, 0.22f, 0.18f), y => y.StarvationDeaths),
            ("自然死亡", new Color(0.45f, 0.40f, 0.35f), y => y.NaturalDeaths)
        }),
        new Series("遷徙", new (string, Color, Func<DemoYearSummary, long>)[]
        {
            ("遷出", new Color(0.78f, 0.52f, 0.16f), y => y.Emigrants),
            ("遷入", new Color(0.30f, 0.50f, 0.70f), y => y.Immigrants)
        }),
        new Series("糧食", new (string, Color, Func<DemoYearSummary, long>)[]
        {
            ("採收", new Color(0.40f, 0.60f, 0.25f), y => y.Harvested),
            ("缺口", new Color(0.72f, 0.22f, 0.18f), y => y.FoodShortage)
        })
    };

    private const float TempLeft = 52f, TempRight = 12f, TempTop = 30f, TempBottom = 26f;

    private IReadOnlyList<DemoYearSummary> _rows = Array.Empty<DemoYearSummary>();
    private int _series;
    private int _selectedYear;

    /// <summary>點選某一年（參數＝年份）。</summary>
    public event Action<int>? YearSelected;

    public IReadOnlyList<DemoYearSummary> Rows
    {
        get => _rows;
        set
        {
            _rows = value ?? Array.Empty<DemoYearSummary>();
            if (_rows.All(r => r.Year != _selectedYear))
                _selectedYear = 0;
            QueueRedraw();
        }
    }

    public int SeriesIndex
    {
        get => _series;
        set
        {
            _series = Math.Clamp(value, 0, AllSeries.Count - 1);
            QueueRedraw();
        }
    }

    /// <summary>目前選取的年份（0＝沒有）。</summary>
    public int SelectedYear => _selectedYear;

    private long _capacity;

    /// <summary>第 08-1 章：地圖理論承載上限（人口系列畫成虛線；0＝不畫）。</summary>
    public long Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Max(0, value);
            QueueRedraw();
        }
    }

    /// <summary>目前系列每一年每一條長條的數值（與畫面一致；冒煙用來和年帳比對）。</summary>
    public IReadOnlyList<long[]> Values()
        => _rows.Select(r => AllSeries[_series].Bars.Select(b => b.Value(r)).ToArray()).ToList();

    public void SelectYear(int year)
    {
        if (_rows.All(r => r.Year != year))
            return;
        _selectedYear = year;
        QueueRedraw();
        YearSelected?.Invoke(year);
    }

    /// <summary>某一年長條群的水平中心（本控制項座標）；冒煙用來送真的滑鼠點擊。</summary>
    public float CenterXOf(int year)
    {
        var index = _rows.ToList().FindIndex(r => r.Year == year);
        var group = GroupWidth();
        return TempLeft + (index + 0.5f) * group;
    }

    /// <summary>點擊位置所在的年份（0＝不在任何一年上）。</summary>
    public int YearAt(Vector2 position)
    {
        if (_rows.Count == 0 || position.X < TempLeft || position.X > Size.X - TempRight
            || position.Y < TempTop || position.Y > Size.Y - TempBottom)
            return 0;
        var index = (int)((position.X - TempLeft) / GroupWidth());
        return index >= 0 && index < _rows.Count ? _rows[index].Year : 0;
    }

    private float GroupWidth() => Math.Max(1f, Size.X - TempLeft - TempRight) / Math.Max(1, _rows.Count);

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click
            && YearAt(click.Position) is var year and > 0)
        {
            SelectYear(year);
            AcceptEvent();
        }
    }

    public override void _Draw()
    {
        var font = GetThemeDefaultFont();
        var fontSize = 13;
        var ink = new Color(0.20f, 0.15f, 0.10f);
        var plot = new Rect2(TempLeft, TempTop, Math.Max(1f, Size.X - TempLeft - TempRight), Math.Max(1f, Size.Y - TempTop - TempBottom));
        var series = AllSeries[_series];

        // 圖例。
        var x = TempLeft;
        foreach (var bar in series.Bars)
        {
            DrawRect(new Rect2(x, 8, 12, 12), bar.Color);
            DrawString(font, new Vector2(x + 16, 19), bar.Label, HorizontalAlignment.Left, -1, fontSize, ink);
            x += 24 + font.GetStringSize(bar.Label, HorizontalAlignment.Left, -1, fontSize).X;
        }

        DrawLine(plot.Position + new Vector2(0, plot.Size.Y), plot.End, ink, 1.5f);
        DrawLine(plot.Position, plot.Position + new Vector2(0, plot.Size.Y), ink, 1.5f);
        if (_rows.Count == 0)
        {
            DrawString(font, plot.Position + new Vector2(12, plot.Size.Y / 2), "第一次週結算（第 7 日）後才有年帳。",
                HorizontalAlignment.Left, -1, fontSize, ink);
            return;
        }

        var values = Values();
        var showCapacity = _series == 0 && _capacity > 0;
        var max = Math.Max(1L, Math.Max(values.SelectMany(v => v).DefaultIfEmpty(0).Max(), showCapacity ? _capacity : 0));
        DrawString(font, new Vector2(4, plot.Position.Y + 10), max.ToString(System.Globalization.CultureInfo.InvariantCulture),
            HorizontalAlignment.Left, TempLeft - 8, fontSize, ink);
        DrawString(font, new Vector2(4, plot.End.Y), "0", HorizontalAlignment.Left, -1, fontSize, ink);

        var group = GroupWidth();
        var barWidth = Math.Max(1f, group * 0.8f / series.Bars.Count);
        for (var i = 0; i < _rows.Count; i++)
        {
            var left = plot.Position.X + i * group;
            if (_rows[i].Year == _selectedYear)
                DrawRect(new Rect2(left, plot.Position.Y, group, plot.Size.Y), new Color(0.95f, 0.80f, 0.35f, 0.35f));
            for (var b = 0; b < series.Bars.Count; b++)
            {
                var h = plot.Size.Y * values[i][b] / max;
                DrawRect(new Rect2(left + group * 0.1f + b * barWidth, plot.End.Y - h, barWidth, h), series.Bars[b].Color);
            }
        }

        if (showCapacity)
        {
            // 第 08-1 章：地圖理論承載上限（TEMP 估計）虛線。
            var y = plot.End.Y - plot.Size.Y * _capacity / max;
            DrawDashedLine(new Vector2(plot.Position.X, y), new Vector2(plot.End.X, y), new Color(0.55f, 0.25f, 0.10f), 2f, 6f);
            DrawString(font, new Vector2(plot.Position.X + 4, y - 4), $"理論上限 {_capacity}", HorizontalAlignment.Left, -1, fontSize, ink);
        }

        // 年份刻度：第一年與最後一年。
        DrawString(font, new Vector2(plot.Position.X, Size.Y - 6), $"第 {_rows[0].Year} 年", HorizontalAlignment.Left, -1, fontSize, ink);
        if (_rows.Count > 1)
        {
            var lastText = $"第 {_rows[^1].Year} 年";
            var w = font.GetStringSize(lastText, HorizontalAlignment.Left, -1, fontSize).X;
            DrawString(font, new Vector2(plot.End.X - w, Size.Y - 6), lastText, HorizontalAlignment.Left, -1, fontSize, ink);
        }
    }
}
