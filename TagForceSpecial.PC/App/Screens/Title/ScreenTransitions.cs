namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Measured Main Menu entry, as a function of game frames since the menu began (GE dump sequence, phase-4-main-menu.md).</summary>
public static class MainMenuEntry
{
    public const double GameFramesPerSecond = 29.97;
    /// <summary>The step where focus appears and the menu starts accepting input.</summary>
    public const int ReadyFrame = 34;
    private const int FirstBarFrame = 18, FirstLabelFrame = 22, HeaderStartFrame = 19, HeaderFrames = 5, TitleHalfFrame = 23;
    private const float HeaderHeight = 32;

    /// <summary>Full-screen black quad drawn last: 255, 239, ... 15, then gone.</summary>
    public static int OverlayAlpha(int frame) => Math.Clamp(255 - 16 * frame, 0, 255);

    /// <summary>Rows fade in one frame apart: 63, 127, 191, 255.</summary>
    public static int BarAlpha(int row, int frame) => Math.Clamp(64 * (frame - FirstBarFrame - row) - 1, 0, 255);

    /// <summary>A row's text is not faded; it appears at full alpha once its bar is opaque.</summary>
    public static bool LabelVisible(int row, int frame) => frame >= FirstLabelFrame + row;

    public static bool FocusVisible(int frame) => frame >= ReadyFrame;

    public static int HeaderAlpha(int frame) => Math.Clamp(51 * (frame - HeaderStartFrame), 0, 255);

    /// <summary>The header slides down from above the screen over the same frames as its fade.</summary>
    public static float HeaderOffset(int frame) => -HeaderHeight * Math.Clamp(HeaderFrames - (frame - HeaderStartFrame), 0, HeaderFrames) / HeaderFrames;

    public static int TitleAlpha(int frame) => frame < TitleHalfFrame ? 0 : frame == TitleHalfFrame ? 128 : 255;
}

public enum ScreenFadePhase { None, Lead, Out, Hold, In }

/// <summary>
/// Black fade between screens: 16 frames out, 3 at black, 16 in, ignoring input. The screen changes at black.
/// </summary>
public sealed class ScreenFade
{
    public const int FadeFrames = 16, HoldFrames = 3;
    /// <summary>Measured time between confirming a dialog and the fade starting.</summary>
    public const int DialogLeadFrames = 17;
    private TimeSpan phaseTime;
    private int leadFrames;
    private bool fadeInAfter;
    private Action? atBlack;

    public ScreenFadePhase Phase { get; private set; }
    public bool Active => Phase != ScreenFadePhase.None;

    public int Alpha => Phase switch
    {
        ScreenFadePhase.Out => Math.Clamp(16 * Frames - 1, 0, 255),
        ScreenFadePhase.Hold => 255,
        ScreenFadePhase.In => Math.Clamp(255 - 16 * Frames, 0, 255),
        _ => 0
    };

    private int Frames => (int)Math.Floor(phaseTime.TotalSeconds * MainMenuEntry.GameFramesPerSecond);

    public void Begin(Action action, bool fadeIn, int lead = 0)
    {
        if (Active) throw new InvalidOperationException("A screen fade is already running.");
        atBlack = action; fadeInAfter = fadeIn; leadFrames = lead; phaseTime = TimeSpan.Zero;
        Phase = lead > 0 ? ScreenFadePhase.Lead : ScreenFadePhase.Out;
    }

    public void Tick(TimeSpan delta)
    {
        if (!Active) return;
        phaseTime += delta;
        while (Active && Frames >= PhaseLength) Advance();
    }

    private int PhaseLength => Phase switch
    {
        ScreenFadePhase.Lead => leadFrames,
        ScreenFadePhase.Hold => HoldFrames,
        _ => FadeFrames
    };

    private void Advance()
    {
        phaseTime -= TimeSpan.FromSeconds(PhaseLength / MainMenuEntry.GameFramesPerSecond);
        if (phaseTime < TimeSpan.Zero) phaseTime = TimeSpan.Zero;
        switch (Phase)
        {
            case ScreenFadePhase.Lead: Phase = ScreenFadePhase.Out; break;
            case ScreenFadePhase.Out: Phase = ScreenFadePhase.Hold; break;
            case ScreenFadePhase.Hold:
                var action = atBlack;
                atBlack = null;
                Phase = fadeInAfter ? ScreenFadePhase.In : ScreenFadePhase.None;
                action?.Invoke();
                break;
            case ScreenFadePhase.In: Phase = ScreenFadePhase.None; break;
        }
    }
}
