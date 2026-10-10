using WorldSimulation.Core.World;
using WorldSimulation.Core.World.Demo;
using WorldSimulation.GodotClient;
using Xunit;

namespace WorldSimulation.Core.Tests;

/// <summary>#87：Godot 播放／暫停計時器（純 C#）。播放只決定「推進幾天」，世界仍由 Core 逐日推進。</summary>
public sealed class PlaybackClockTests
{
    [Fact]
    public void PausedClockNeverAdvances()
    {
        var clock = new PlaybackClock();
        Assert.False(clock.Playing);
        Assert.Equal(0, clock.Tick(10));
        Assert.Equal("已暫停", clock.StatusText);
    }

    [Fact]
    public void PlayingAccumulatesFractionsIntoWholeDays()
    {
        var clock = new PlaybackClock();
        clock.SetSpeed(5);
        clock.Play();
        // 每幀 0.25 秒 × 每秒 5 天＝1.25 天：零頭累積，8 幀共 10 天。
        var days = Enumerable.Range(0, 8).Sum(_ => clock.Tick(0.25));
        Assert.Equal(10, days);
        Assert.Equal("播放中（每秒 5 天，TEMP）", clock.StatusText);
    }

    [Fact]
    public void PauseDropsThePartialDayAndStopsAdvancing()
    {
        var clock = new PlaybackClock();
        clock.Play();
        Assert.Equal(0, clock.Tick(0.5)); // 0.5 天，還不到一天。
        clock.Pause();
        Assert.Equal(0, clock.Tick(100));
        clock.Play();
        Assert.Equal(0, clock.Tick(0.5)); // 零頭已清掉，不會因暫停前的 0.5 而湊成一天。
        Assert.Equal(1, clock.Tick(0.5));
    }

    [Fact]
    public void LongHitchIsCappedPerFrame()
    {
        var clock = new PlaybackClock();
        clock.SetSpeed(30);
        clock.Play();
        Assert.Equal(PlaybackClock.TempMaxDaysPerFrame, clock.Tick(60));
        Assert.Equal(0, clock.Tick(0.01)); // 超出的時間已丟棄，不會之後補跑。
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidFrameTimeAdvancesNothing(double delta)
    {
        var clock = new PlaybackClock();
        clock.Play();
        Assert.Equal(0, clock.Tick(delta));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(1000)]
    public void OnlyTempSpeedOptionsAreAccepted(int speed)
    {
        var clock = new PlaybackClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetSpeed(speed));
        Assert.Equal(1, clock.DaysPerSecond);
    }

    [Fact]
    public void PlaybackGivesTheSameWorldAsPressingNextDay()
    {
        // 播放＝把 Tick 回傳的天數逐日交給 Core；結果必須與逐日按「下一天」完全相同。
        var played = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.LocalFamineSample);
        var stepped = new DemoObserverWorld(DemoScenario.Sample(42), DemoWeeklyTempConfig.LocalFamineSample);
        var clock = new PlaybackClock();
        clock.SetSpeed(30);
        clock.Play();
        var total = 0;
        foreach (var delta in new[] { 0.016, 0.5, 0.033, 2.0, 0.25, 0.1 })
        {
            var days = clock.Tick(delta);
            for (var i = 0; i < days; i++)
                played.AdvanceOneDay();
            total += days;
        }

        for (var i = 0; i < total; i++)
            stepped.AdvanceOneDay();
        Assert.True(total > 7);
        Assert.Equal(stepped.ExportTempSave(), played.ExportTempSave());
    }
}
