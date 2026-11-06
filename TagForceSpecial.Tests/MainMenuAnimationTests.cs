using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.Tests;

/// <summary>Properties of the fitted Main Menu animation; expected values come from the measured GE samples, not from this code.</summary>
public class MainMenuAnimationTests
{
    private static readonly MainMenuAnimation Motion = MainMenuAnimation.LoadEmbedded();

    [Fact]
    public void ProfileCoversTheMeasuredDrawFamiliesWithNumbersOnly()
    {
        var profile = Motion.Profile;
        Assert.Equal(16, profile.Spins.Length);
        Assert.Equal(new[] { "fast", "grow", "slow" }, profile.Spins.Select(s => s.Family).Distinct().Order());
        Assert.Equal(120, profile.Scalars.Length);
        Assert.Equal(1200, profile.LoopFrames);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(331)]
    [InlineData(977)]
    public void EverySpinAndScalarRepeatsAfterTheMeasuredLoop(int frame)
    {
        int later = frame + Motion.Profile.LoopFrames;
        Assert.Equal(Motion.ScalarsAt(frame), Motion.ScalarsAt(later));
        foreach (var spin in Motion.Profile.Spins)
        {
            var (a, b) = (Motion.Corners(spin, frame), Motion.Corners(spin, later));
            for (int i = 0; i < a.Length; i++)
            {
                Assert.Equal(a[i].X, b[i].X, 1);
                Assert.Equal(a[i].Y, b[i].Y, 1);
            }
        }
        Assert.Equal(Motion.StripOffset(frame), Motion.StripOffset(frame + 300), 1);
    }

    [Fact]
    public void FastFamilyTurnsAboutTheScreenCentreWithoutChangingRadius()
    {
        var spin = Motion.Profile.Spins.First(s => s.Family == "fast");
        var (start, turned) = (Motion.Corners(spin, 0), Motion.Corners(spin, 150));
        for (int i = 0; i < start.Length; i++)
        {
            double r0 = Math.Sqrt(Math.Pow(start[i].X - 240, 2) + Math.Pow(start[i].Y - 136, 2));
            double r1 = Math.Sqrt(Math.Pow(turned[i].X - 240, 2) + Math.Pow(turned[i].Y - 136, 2));
            Assert.Equal(r0, r1, 1);
        }
        Assert.NotEqual(start[0].X, turned[0].X, 0);
    }

    [Fact]
    public void StripStaysInsideItsMeasuredRangeAndMovesUp()
    {
        float baseY = Motion.Profile.StripPoints[0][1];
        for (int frame = 0; frame < 300; frame++)
        {
            float y = baseY + Motion.StripOffset(frame);
            Assert.InRange(y, Motion.Profile.StripMinY - 0.01f, Motion.Profile.StripMinY + Motion.Profile.StripSpan + 0.01f);
        }
        Assert.True(Motion.StripOffset(1) < Motion.StripOffset(0));
    }

    [Fact]
    public void PulseStaysInItsMeasuredRangeAndPeaksAtFullIntensity()
    {
        var pulses = Enumerable.Range(0, Motion.Profile.ScalarPeriod).Select(f => Motion.ScalarsAt(f).Pulse).ToArray();
        Assert.Equal(255, pulses.Max());
        Assert.InRange(pulses.Min(), 200, 220);
    }

    [Fact]
    public void GameFrameIsHeldBetweenFlipsInsteadOfInterpolated()
    {
        double perFrame = 1 / Motion.Profile.GameFramesPerSecond;
        Assert.Equal(Motion.FrameAt(TimeSpan.FromSeconds(perFrame * 10.1)), Motion.FrameAt(TimeSpan.FromSeconds(perFrame * 10.9)));
        Assert.Equal(11, Motion.FrameAt(TimeSpan.FromSeconds(perFrame * 11.1)));
    }

    [Fact]
    public void HoldRepeatReportsEachDueStepOnce()
    {
        var repeat = new MenuHoldRepeat(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(10));
        Assert.Equal(0, repeat.Advance(TimeSpan.FromMilliseconds(99)));
        Assert.Equal(1, repeat.Advance(TimeSpan.FromMilliseconds(1)));
        Assert.Equal(3, repeat.Advance(TimeSpan.FromMilliseconds(30)));
        repeat.Restart();
        Assert.Equal(0, repeat.Advance(TimeSpan.FromMilliseconds(50)));
    }
}
