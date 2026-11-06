using System.Text.Json;
using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.App.Screens.Title;

public sealed record DeckMenuRowProfile(string Id, float Y, FlowText Label);

/// <summary>Measured Deck Menu submenu (deck-menu-frame.json, numbers and source offsets only). Frames are game frames since the
/// first frame that shows the submenu state, which is also the first frame of the dimmed Main Menu.</summary>
public sealed record DeckMenuProfile(int SchemaVersion, string BuildId, string Scope, float BarLeft, int[][] BasePieces, int MenuDim,
    int FirstBarFrame, int FirstLabelFrame, int ReadyFrame, int CloseFrames, int[] FocusPulse, DeckMenuRowProfile[] Rows)
{
    /// <summary>Rows fade in one frame apart, 63, 127, 191, 255, at fixed positions, like the Main Menu entry.</summary>
    public int BarAlpha(int row, int frame) => Math.Clamp(64 * (frame - FirstBarFrame - row) - 1, 0, 255);

    /// <summary>A row's text appears at full alpha once its bar is opaque.</summary>
    public bool LabelVisible(int row, int frame) => frame >= FirstLabelFrame + row;

    /// <summary>Focus appears here, and only now does the submenu accept input.</summary>
    public bool Ready(int frame) => frame >= ReadyFrame;

    /// <summary>Measured: the focus band's pulse starts from the same value each time focus appears, then cycles.</summary>
    public int PulseAt(int frame) => PulseSinceFocus(frame - ReadyFrame);

    /// <summary>The same curve counted from the frame focus appeared; the Main Menu's focus restarts on it after the submenu closes.</summary>
    public int PulseSinceFocus(int frames) => FocusPulse[Math.Max(0, frames) % FocusPulse.Length];

    public static DeckMenuProfile LoadEmbedded()
    {
        using var stream = typeof(DeckMenuProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.Title.deck-menu-frame.json")
            ?? throw new InvalidDataException("Missing measured Deck Menu profile.");
        var value = JsonSerializer.Deserialize<DeckMenuProfile>(stream) ?? throw new InvalidDataException("Invalid Deck Menu profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Deck Menu profile/source mismatch.");
        if (value.Rows.Length != DeckMenuDestinations.All.Count || value.Rows.Select(r => r.Id).Where((id, i) => id != DeckMenuDestinations.All[i].Id).Any())
            throw new InvalidDataException("Deck Menu profile rows differ from the destination table.");
        if (value.BasePieces.Length != 3 || value.BasePieces.Any(p => p.Length != 4)) throw new InvalidDataException("Deck Menu bar needs three base pieces.");
        if (value.FocusPulse.Length == 0 || value.FocusPulse.Any(a => a is < 0 or > 255)) throw new InvalidDataException("Deck Menu focus pulse is invalid.");
        if (value.FirstLabelFrame < value.FirstBarFrame || value.ReadyFrame < value.FirstLabelFrame || value.CloseFrames < 0)
            throw new InvalidDataException("Deck Menu timing is out of order.");
        return value;
    }
}

/// <summary>One row of the original Deck Menu. <paramref name="OriginalBehavior"/> and <paramref name="OriginalReturn"/> are what the
/// same-build original was observed to do (config/reference/deck-menu.json); <paramref name="Status"/> is what the PC offers.
/// A row never opens a substitute PC feature such as the native deck editor.</summary>
public sealed record DeckMenuDestination(string Id, string Label, string Status, string OriginalBehavior, string OriginalReturn, string PlannedIn, string Notice);

public static class DeckMenuDestinations
{
    private const string NotBuilt = "This screen has not been recreated in the PC version yet.";
    public const string DeckEditCore = "original Deck Edit core screen (list layout): tabs, cursor, card moves and exit; menus, search, layouts and help are not recreated yet";

    public static IReadOnlyList<DeckMenuDestination> All { get; } = Array.AsReadOnly(new DeckMenuDestination[]
    {
        new("deck-edit", "Deck Edit", DeckEditCore,
            "Deck Edit screen; it first asks whether to view the \"Deck Edit Controls\" tutorial",
            "Exit asks to confirm, then asks whether to save; NO returns here with Deck Edit focused", "Phase 6, section 10.2", NotBuilt),
        new("partner-deck-edit", "Partner Deck Edit", MainMenuDestinations.SurfaceOnly,
            "Partner selection, grouped by series tabs (DM, GX, 5D's, ZEXAL, ARC-V)",
            "Back returns here with Partner Deck Edit focused", "not scheduled", NotBuilt),
        new("recipe-viewer", "Recipe Viewer", MainMenuDestinations.SurfaceOnly,
            "Recipe Viewer with User and Character tabs and numbered recipe slots",
            "Back asks whether to save; NO or back returns here with Recipe Viewer focused", "Phase 6, section 10.3", NotBuilt)
    });
}

public enum DeckMenuOutcome { None, Closing, Notice }

/// <summary>Observed Deck Menu input: a press moves one row and wraps; held repeats stop at the end rows; confirm opens the focused
/// destination; back closes the submenu. Every other button does nothing, and input is dropped until focus appears.
/// PC confirm is PSP Circle in the original and PSP Cross here; PC back is the reverse.</summary>
public sealed class DeckMenuSession(DeckMenuProfile profile)
{
    private readonly MenuHoldRepeat repeat = new(TitleFlow.MenuRepeatDelay, TitleFlow.MenuRepeatInterval);
    private TimeSpan clock, closing;
    private int heldDirection;
    public int Rows => DeckMenuDestinations.All.Count;
    /// <summary>Observed: every opening starts on Deck Edit, whatever was focused before.</summary>
    public int Focus { get; private set; }
    public bool NoticeOpen { get; private set; }
    /// <summary>Measured: after back, the submenu stays for a few frames with its focus hidden, over the still-dimmed menu.</summary>
    public bool Closing { get; private set; }
    public int Frame => (int)Math.Floor(clock.TotalSeconds * MainMenuEntry.GameFramesPerSecond);
    public bool Ready => profile.Ready(Frame) && !Closing;
    /// <summary>True once the close frames have passed; the flow then returns to the Main Menu.</summary>
    public bool Closed => Closing && closing.TotalSeconds * MainMenuEntry.GameFramesPerSecond >= profile.CloseFrames;
    public bool FocusVisible(int frame) => profile.Ready(frame) && !Closing;
    public DeckMenuDestination Focused => DeckMenuDestinations.All[Focus];
    /// <summary>Closes the PC notice without input, when a recreated destination opens instead.</summary>
    public void CloseNotice() => NoticeOpen = false;

    public void Update(TimeSpan delta)
    {
        clock += delta;
        if (Closing) { closing += delta; return; }
        if (heldDirection == 0 || NoticeOpen) return;
        for (int due = repeat.Advance(delta); due > 0; due--) Focus = Math.Clamp(Focus + heldDirection, 0, Rows - 1);
    }

    /// <summary>Exact captures fix the opening at a measured frame.</summary>
    public void SkipTo(int frame) => clock = TimeSpan.FromSeconds(frame / MainMenuEntry.GameFramesPerSecond);

    public DeckMenuOutcome Handle(PspInputFrame input)
    {
        if (NoticeOpen)
        {
            // The PC notice closes with either button and keeps focus, as returning from each original destination does.
            if (input.IsPressed(PspAction.Cross) || input.IsPressed(PspAction.Circle)) NoticeOpen = false;
            return DeckMenuOutcome.None;
        }
        if (!Ready) return DeckMenuOutcome.None;
        int held = (input.Held & (PspAction.Up | PspAction.Down)) switch { PspAction.Up => -1, PspAction.Down => 1, _ => 0 };
        if (held != heldDirection) heldDirection = 0;
        int pressed = input.IsPressed(PspAction.Up) ? -1 : input.IsPressed(PspAction.Down) ? 1 : 0;
        if (pressed != 0 && pressed == held)
        {
            Focus = (Focus + pressed + Rows) % Rows;
            heldDirection = pressed;
            repeat.Restart();
        }
        if (input.IsPressed(PspAction.Circle)) { Closing = true; heldDirection = 0; return DeckMenuOutcome.Closing; }
        if (!input.IsPressed(PspAction.Cross)) return DeckMenuOutcome.None;
        heldDirection = 0;
        NoticeOpen = true;
        return DeckMenuOutcome.Notice;
    }
}
