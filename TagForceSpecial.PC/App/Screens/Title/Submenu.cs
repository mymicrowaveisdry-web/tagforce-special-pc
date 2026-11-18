using System.Text.Json;
using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>The original submenus drawn over the dimmed Main Menu.</summary>
public enum SubmenuKind { Deck, Database }

public sealed record SubmenuRowProfile(string Id, float Y, FlowText Label);

/// <summary>A measured submenu (its embedded *-menu-frame.json, numbers and source offsets only). Frames are game frames since the
/// first frame that shows the submenu state, which is also the first frame of the dimmed Main Menu.</summary>
public sealed record SubmenuProfile(int SchemaVersion, string BuildId, string Scope, float BarLeft, int[][] BasePieces, int MenuDim,
    int FirstBarFrame, int FirstLabelFrame, int ReadyFrame, int CloseFrames, int[] FocusPulse, SubmenuRowProfile[] Rows)
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

    public static SubmenuProfile LoadEmbedded(SubmenuKind kind)
    {
        string resource = kind == SubmenuKind.Deck ? "deck-menu-frame.json" : "database-menu-frame.json";
        using var stream = typeof(SubmenuProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.Title." + resource)
            ?? throw new InvalidDataException($"Missing measured {kind} submenu profile.");
        var value = JsonSerializer.Deserialize<SubmenuProfile>(stream) ?? throw new InvalidDataException($"Invalid {kind} submenu profile.");
        var destinations = Submenus.Destinations(kind);
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException($"{kind} submenu profile/source mismatch.");
        if (value.Rows.Length != destinations.Count || value.Rows.Select(r => r.Id).Where((id, i) => id != destinations[i].Id).Any())
            throw new InvalidDataException($"{kind} submenu profile rows differ from the destination table.");
        if (value.BasePieces.Length != 3 || value.BasePieces.Any(p => p.Length != 4)) throw new InvalidDataException("Submenu bar needs three base pieces.");
        if (value.FocusPulse.Length == 0 || value.FocusPulse.Any(a => a is < 0 or > 255)) throw new InvalidDataException("Submenu focus pulse is invalid.");
        if (value.FirstLabelFrame < value.FirstBarFrame || value.ReadyFrame < value.FirstLabelFrame || value.CloseFrames < 0)
            throw new InvalidDataException("Submenu timing is out of order.");
        return value;
    }
}

/// <summary>One submenu row. <paramref name="OriginalBehavior"/> and <paramref name="OriginalReturn"/> are what the same-build original
/// was observed to do; <paramref name="Status"/> is what the PC offers. A row never opens a substitute PC feature.</summary>
public sealed record SubmenuDestination(string Id, string Label, string Status, string OriginalBehavior, string OriginalReturn, string PlannedIn, string Notice);

/// <summary>Destination tables and Gum screens of each submenu.</summary>
public static class Submenus
{
    public static IReadOnlyList<SubmenuDestination> Destinations(SubmenuKind kind) =>
        kind == SubmenuKind.Deck ? DeckMenuDestinations.All : DatabaseMenuDestinations.All;

    public static string GumScreen(SubmenuKind kind) => kind == SubmenuKind.Deck ? "OriginalDeckMenu" : "OriginalDatabaseMenu";
}

public enum SubmenuOutcome { None, Closing, Notice }

/// <summary>Observed submenu input: a press moves one row and wraps; held repeats stop at the end rows; confirm opens the focused
/// destination; back closes the submenu. Every other button does nothing, and input is dropped until focus appears.
/// Confirm does what the original does with Circle, Back what it does with Cross.</summary>
public sealed class SubmenuSession(SubmenuKind kind, SubmenuProfile profile)
{
    private readonly MenuHoldRepeat repeat = new(TitleFlow.MenuRepeatDelay, TitleFlow.MenuRepeatInterval);
    private readonly IReadOnlyList<SubmenuDestination> destinations = Submenus.Destinations(kind);
    private TimeSpan clock, closing;
    private int heldDirection;
    public SubmenuKind Kind => kind;
    public SubmenuProfile Profile => profile;
    public int Rows => destinations.Count;
    /// <summary>Observed: every opening starts on the first row, whatever was focused before.</summary>
    public int Focus { get; private set; }
    public bool NoticeOpen { get; private set; }
    /// <summary>Measured: after back, the submenu stays for a few frames with its focus hidden, over the still-dimmed menu.</summary>
    public bool Closing { get; private set; }
    public int Frame => MainMenuEntry.FramesIn(clock);
    public bool Ready => profile.Ready(Frame) && !Closing;
    /// <summary>True once the close frames have passed; the flow then returns to the Main Menu.</summary>
    public bool Closed => Closing && MainMenuEntry.FramesIn(closing) >= profile.CloseFrames;
    public bool FocusVisible(int frame) => profile.Ready(frame) && !Closing;
    public SubmenuDestination Focused => destinations[Focus];
    /// <summary>Closes the PC notice without input, when a recreated destination opens instead.</summary>
    public void CloseNotice() => NoticeOpen = false;
    public void SetFocus(int row) => Focus = row >= 0 && row < Rows ? row : throw new ArgumentOutOfRangeException(nameof(row));

    public void Update(TimeSpan delta)
    {
        clock += delta;
        if (Closing) { closing += delta; return; }
        if (heldDirection == 0 || NoticeOpen) return;
        for (int due = repeat.Advance(delta); due > 0; due--) Focus = Math.Clamp(Focus + heldDirection, 0, Rows - 1);
    }

    /// <summary>Exact captures fix the opening at a measured frame.</summary>
    public void SkipTo(int frame) => clock = TimeSpan.FromSeconds(frame / MainMenuEntry.GameFramesPerSecond);

    public SubmenuOutcome Handle(InputFrame input)
    {
        if (NoticeOpen)
        {
            // The PC notice closes with either button and keeps focus, as returning from each original destination does.
            if (input.IsPressed(InputAction.Confirm) || input.IsPressed(InputAction.Back)) NoticeOpen = false;
            return SubmenuOutcome.None;
        }
        if (!Ready) return SubmenuOutcome.None;
        int held = (input.Held & (InputAction.Up | InputAction.Down)) switch { InputAction.Up => -1, InputAction.Down => 1, _ => 0 };
        if (held != heldDirection) heldDirection = 0;
        int pressed = input.IsPressed(InputAction.Up) ? -1 : input.IsPressed(InputAction.Down) ? 1 : 0;
        if (pressed != 0 && pressed == held)
        {
            Focus = (Focus + pressed + Rows) % Rows;
            heldDirection = pressed;
            repeat.Restart();
        }
        if (input.IsPressed(InputAction.Back)) { Closing = true; heldDirection = 0; return SubmenuOutcome.Closing; }
        if (!input.IsPressed(InputAction.Confirm)) return SubmenuOutcome.None;
        heldDirection = 0;
        NoticeOpen = true;
        return SubmenuOutcome.Notice;
    }
}
