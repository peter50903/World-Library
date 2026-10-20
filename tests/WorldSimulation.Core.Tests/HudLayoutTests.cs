using WorldSimulation.GodotClient;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>第 05 章：觀察者介面版面（純 C#）——各尺寸下面板都在視窗內、互不重疊、地圖保留足夠寬度。</summary>
public sealed class HudLayoutTests
{
    public static TheoryData<int, int> Sizes => new()
    {
        { 1024, 768 }, { 1280, 720 }, { 1440, 900 }, { 1920, 1080 }, { 2560, 1440 }, { 3840, 2160 }, { 800, 600 }, { 640, 480 }
    };

    [Theory]
    [MemberData(nameof(Sizes))]
    public void AllPanelsOpen_StayInsideTheWindow_WithoutOverlapping(int width, int height)
    {
        var f = HudLayout.Compute(width, height, detailOpen: true, consoleOpen: true);
        var panels = new[] { f.TopBar, f.Detail, f.Console, f.Time, f.Feed, f.Toolbar, f.MapModes };
        foreach (var p in panels)
            Assert.True(p.Inside(width, height), $"{p} 超出 {width}×{height}");
        for (var i = 0; i < panels.Length; i++)
            for (var j = i + 1; j < panels.Length; j++)
                Assert.False(panels[i].Overlaps(panels[j]), $"{panels[i]} 與 {panels[j]} 重疊");
        Assert.True(f.MapVisibleWidth >= width * HudLayout.TempMapMinShare, $"地圖可見寬度 {f.MapVisibleWidth}");
    }

    [Theory]
    [MemberData(nameof(Sizes))]
    public void ClosedSidePanels_TakeNoWidth_AndTheMapGetsTheWholeWidth(int width, int height)
    {
        var f = HudLayout.Compute(width, height, detailOpen: false, consoleOpen: false);
        Assert.Equal(0, f.Detail.Width);
        Assert.Equal(0, f.Console.Width);
        Assert.Equal(width, f.MapVisibleWidth);
        Assert.True(f.TopBar.Inside(width, height) && f.Time.Inside(width, height) && f.Feed.Inside(width, height)
            && f.Toolbar.Inside(width, height) && f.MapModes.Inside(width, height));
    }

    [Fact]
    public void TopBarSpansTheWidth_AndTimeControlsSitBottomRight()
    {
        var f = HudLayout.Compute(1920, 1080, true, true);
        Assert.Equal(new HudRect(0, 0, 1920, HudLayout.TempTopBarHeight), f.TopBar);
        Assert.Equal(1920 - HudLayout.TempMargin, f.Time.Right);
        Assert.Equal(1080 - HudLayout.TempMargin, f.Time.Bottom);
        Assert.Equal(f.Time.Bottom, f.Feed.Bottom);
        Assert.True(f.Detail.X < f.Console.X);
    }

    [Fact]
    public void ToolbarIsCentred_AndMapModesSitLeft_InTheRowAboveTheFeed()
    {
        var f = HudLayout.Compute(1920, 1080, true, true);
        Assert.Equal(new HudRect((1920 - HudLayout.TempToolbarWidth) / 2, f.Feed.Y - HudLayout.TempMargin - HudLayout.TempBarHeight,
            HudLayout.TempToolbarWidth, HudLayout.TempBarHeight), f.Toolbar);
        Assert.Equal(HudLayout.TempMargin, f.MapModes.X);
        Assert.Equal(f.Toolbar.Y, f.MapModes.Y);
        Assert.Equal(f.Toolbar.Bottom + HudLayout.TempMargin, f.Feed.Y);
        Assert.True(f.Detail.Bottom <= f.Toolbar.Y && f.Console.Bottom <= f.Toolbar.Y);
    }

    [Fact]
    public void WideScreens_UseTheMaximumPanelWidths()
    {
        var f = HudLayout.Compute(3840, 2160, true, true);
        Assert.Equal(HudLayout.TempDetailMax, f.Detail.Width);
        Assert.Equal(HudLayout.TempConsoleMax, f.Console.Width);
    }

    [Fact]
    public void DegenerateSizes_NeverThrow_AndStayInside()
    {
        foreach (var (w, h) in new[] { (1, 1), (0, 0), (320, 200), (200, 900) })
        {
            var f = HudLayout.Compute(w, h, true, true);
            var ww = Math.Max(w, 1);
            var hh = Math.Max(h, 1);
            Assert.True(f.TopBar.Right <= ww && f.Time.Right <= ww && f.Feed.Right <= ww);
            Assert.True(f.Time.Bottom <= hh && f.Feed.Bottom <= hh);
        }
    }
}
