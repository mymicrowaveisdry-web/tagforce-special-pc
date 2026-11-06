using System.Text.Json;

namespace TagForceSpecial.PC.App.Screens.Title;

public sealed record MainMenuSpin(int Draw, string Family, float[][] Points, float ReferenceSize, float BaseSize);
public sealed record MainMenuAnimationProfile(int SchemaVersion, string BuildId, string Scope, double GameFramesPerSecond, int EntryAnimationFrame, float CenterX, float CenterY,
    float FastDegreesPerFrame, float SlowDegreesPerFrame, float SlowBaseAngle, int LoopFrames, int StripDraw, float[][] StripPoints, float StripPixelsPerFrame,
    float StripMinY, float StripSpan, Dictionary<string, int> FixedAlpha, MainMenuSpin[] Spins, int ScalarPeriod, int ScalarStep, float[][] Scalars, string SourceSampleSha256);

/// <summary>Values of the measured scalars for one game frame.</summary>
public readonly record struct MainMenuScalars(int TintRed, int TintGreen, int TintBlue, int BeamAlpha, int RingAlpha, int GrowAlpha, float GrowScale, int Pulse, int HeaderEmissive, int HeaderAmbient);

/// <summary>Pure model of the Main Menu background and focus pulse, fitted to timed GE dumps. All state is a function of the
/// game frame number, so a frame can be reproduced exactly for comparison and the screen can resume after any interruption.</summary>
public sealed class MainMenuAnimation
{
    public MainMenuAnimationProfile Profile { get; }

    public MainMenuAnimation(MainMenuAnimationProfile profile)
    {
        if (profile.SchemaVersion != 1 || profile.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Main Menu animation profile/source mismatch.");
        if (profile.Scalars.Length != profile.ScalarPeriod / profile.ScalarStep || profile.Scalars.Any(row => row.Length != 10))
            throw new InvalidDataException("Main Menu animation table is incomplete.");
        Profile = profile;
    }

    public static MainMenuAnimation LoadEmbedded()
    {
        using var stream = typeof(MainMenuAnimation).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.Title.main-menu-animation.json")
            ?? throw new InvalidDataException("Missing measured Main Menu animation.");
        return new(JsonSerializer.Deserialize<MainMenuAnimationProfile>(stream) ?? throw new InvalidDataException("Invalid Main Menu animation."));
    }

    /// <summary>The original advances once per game frame, so the PC holds each frame rather than interpolating between them.</summary>
    public int FrameAt(TimeSpan time) => (int)(Math.Floor(time.TotalSeconds * Profile.GameFramesPerSecond) % Profile.LoopFrames);

    public MainMenuScalars ScalarsAt(int frame)
    {
        int period = Profile.ScalarPeriod, step = Profile.ScalarStep;
        int phase = Modulo(frame, period), index = phase / step;
        float[] low = Profile.Scalars[index], high = Profile.Scalars[(index + 1) % Profile.Scalars.Length];
        float weight = phase % step / (float)step;
        float Blend(int column) => low[column] + (high[column] - low[column]) * weight;
        // The grow scale is a sawtooth: never interpolate across its reset.
        float scale = Math.Abs(high[6] - low[6]) > .1f ? low[6] : Blend(6);
        return new(Round(Blend(0)), Round(Blend(1)), Round(Blend(2)), Round(Blend(3)), Round(Blend(4)), Round(Blend(5)), scale, Round(Blend(7)), Round(Blend(8)), Round(Blend(9)));
    }

    /// <summary>Corners of one spinning draw at <paramref name="frame"/>.</summary>
    public (float X, float Y)[] Corners(MainMenuSpin spin, int frame)
    {
        double degrees = spin.Family == "fast" ? Profile.FastDegreesPerFrame * (double)frame : SlowDelta(frame);
        double scale = spin.Family == "grow" ? ScalarsAt(frame).GrowScale * spin.ReferenceSize / spin.BaseSize : 1;
        double c = Math.Cos(degrees * Math.PI / 180), s = Math.Sin(degrees * Math.PI / 180);
        var corners = new (float, float)[spin.Points.Length];
        for (int i = 0; i < corners.Length; i++)
        {
            double x = spin.Points[i][0] - Profile.CenterX, y = spin.Points[i][1] - Profile.CenterY;
            corners[i] = ((float)(Profile.CenterX + (x * c - y * s) * scale), (float)(Profile.CenterY + (x * s + y * c) * scale));
        }
        return corners;
    }

    /// <summary>Measured: the slow family's angle runs in (-180, 0] and jumps by 180 degrees when it reaches zero.</summary>
    private double SlowDelta(int frame)
    {
        double angle = Profile.SlowBaseAngle + Profile.SlowDegreesPerFrame * (double)frame;
        double wrapped = -180 + ((angle + 180) % 180 + 180) % 180;
        return wrapped - Profile.SlowBaseAngle;
    }

    /// <summary>Vertical offset of the strip from its frame-zero corners; it re-enters from below after each wrap.</summary>
    public float StripOffset(int frame)
    {
        float baseY = Profile.StripPoints[0][1];
        float moved = baseY - Profile.StripPixelsPerFrame * frame;
        float wrapped = Profile.StripMinY + ((moved - Profile.StripMinY) % Profile.StripSpan + Profile.StripSpan) % Profile.StripSpan;
        return wrapped - baseY;
    }

    private static int Modulo(int value, int period) => (value % period + period) % period;
    private static int Round(float value) => (int)MathF.Round(Math.Clamp(value, 0, 255));
}
