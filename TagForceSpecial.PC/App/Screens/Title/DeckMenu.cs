namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Rows of the original Deck Menu, as observed on the same build (config/reference/deck-menu.json).</summary>
public static class DeckMenuDestinations
{
    private const string NotBuilt = "This screen has not been recreated in the PC version yet.";
    public const string DeckEditCore = "original Deck Edit core screen (list layout): tabs, cursor, card moves and exit; menus, search, layouts and help are not recreated yet";

    public static IReadOnlyList<SubmenuDestination> All { get; } = Array.AsReadOnly(new SubmenuDestination[]
    {
        new("deck-edit", "Deck Edit", DeckEditCore,
            "Deck Edit screen; it first asks whether to view the \"Deck Edit Controls\" tutorial",
            "Exit asks to confirm, then asks whether to save; NO returns here with Deck Edit focused", "Phase 6, section 10.2", NotBuilt),
        new("partner-deck-edit", "Partner Deck Edit", MainMenuDestinations.SurfaceOnly,
            "Partner selection, grouped by series tabs (DM, GX, 5D's, ZEXAL, ARC-V)",
            "Back returns here with Partner Deck Edit focused", "not scheduled", NotBuilt),
        new("recipe-viewer", "Recipe Viewer", "read-only source recipes; returned deck selection unverified",
            "Recipe Viewer with User and Character tabs and numbered recipe slots",
            "An unchanged viewer returns here with Recipe Viewer focused; recipe changes can ask to save", "Phase 6, section 10.3", "Recipe data has not been imported for this game source.")
    });
}
