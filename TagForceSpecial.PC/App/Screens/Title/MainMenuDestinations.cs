namespace TagForceSpecial.PC.App.Screens.Title;

public enum MainMenuRoute { Unavailable, NativeSave, Options, DeckMenu }

/// <summary>One row of the original Main Menu. <paramref name="OriginalBehavior"/> is what the same-build original was observed
/// to do (config/reference/main-menu.json); <paramref name="Status"/> is what the PC offers. A row never substitutes another PC feature.</summary>
public sealed record MainMenuDestination(string Id, string Label, MainMenuRoute Route, string Status, string OriginalBehavior, string PlannedPhase, string Notice);

public static class MainMenuDestinations
{
    public const string SurfaceOnly = "surface implemented / feature unavailable";
    public const string DeckMenuSubmenu = "original Deck Menu submenu; its three destinations are not recreated yet and show a PC notice";
    public const string OptionsScreen = "original Options screen; values are kept and saved with the native profile, and only Duelist Level is applied today";
    public const string NativeProfileSave = "implemented against the native PC profile; original PSP save data is not read or written";
    private const string NotBuilt = "This mode has not been recreated in the PC version yet.";

    public static IReadOnlyList<MainMenuDestination> All { get; } = Array.AsReadOnly(new MainMenuDestination[]
    {
        new("story", "Story Mode", MainMenuRoute.Unavailable, SurfaceOnly, "Arc selection screen; Back returns with focus kept", "10-11", NotBuilt),
        new("free-duel", "Free Duel", MainMenuRoute.Unavailable, SurfaceOnly, "Duel format screen; Back returns with focus kept", "8", NotBuilt),
        new("network", "Network Mode", MainMenuRoute.Unavailable, SurfaceOnly, "PSP ad hoc notice; only its OK button closes it", "multiplayer",
            "This mode needs multiplayer, which has not been built for the PC version yet."),
        new("duel-puzzle", "Duel Puzzle", MainMenuRoute.Unavailable, SurfaceOnly, "Duel Mission list; Back asks whether to save", "12", NotBuilt),
        new("deck-menu", "Deck Menu", MainMenuRoute.DeckMenu, DeckMenuSubmenu, "Submenu over the Main Menu; Back returns with focus kept", "6", ""),
        new("database", "Database", MainMenuRoute.Unavailable, SurfaceOnly, "Submenu over the Main Menu; Back returns with focus kept", "7", NotBuilt),
        new("options", "Options", MainMenuRoute.Options, OptionsScreen, "Options screen; Back asks whether to cancel changes", "5", ""),
        new("save-data", "Save Data", MainMenuRoute.NativeSave, NativeProfileSave, "Firmware save dialog; Back returns with focus kept", "12", "")
    });
}

/// <summary>Held direction on the Main Menu. A press moves once and wraps; the repeats that follow stop at the end rows (measured, not assumed).</summary>
public sealed class MenuHoldRepeat(TimeSpan delay, TimeSpan interval)
{
    private TimeSpan held;
    private int repeats;

    public void Restart() { held = TimeSpan.Zero; repeats = 0; }

    /// <summary>Number of repeat steps that became due during <paramref name="delta"/>.</summary>
    public int Advance(TimeSpan delta)
    {
        held += delta;
        int due = 0;
        while (held >= delay + interval * repeats) { repeats++; due++; }
        return due;
    }
}
