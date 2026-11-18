namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Rows of the original Database submenu that the PC recreates, as observed on the same build with the reference save.
/// The original's last two rows (website link and downloaded data) are left out of the PC by owner decision.</summary>
public static class DatabaseMenuDestinations
{
    private const string NotBuilt = "This screen has not been recreated in the PC version yet.";

    public static IReadOnlyList<SubmenuDestination> All { get; } = Array.AsReadOnly(new SubmenuDestination[]
    {
        new("list-of-duelists", "List of Duelists", MainMenuDestinations.SurfaceOnly,
            "Duelist list grouped by series tabs (DM, GX, 5D's, ZEXAL, ARC-V) with per-duelist records",
            "Back returns here with List of Duelists focused", "not scheduled", NotBuilt),
        new("card-album", "Card Album", MainMenuDestinations.SurfaceOnly,
            "Binder of every card except Tokens in name order, 3x3 pockets per page; unowned cards show an empty pocket",
            "Back fades out and returns here with Card Album focused", "Phase 7", NotBuilt),
        new("photo-gallery", "Photo Gallery", MainMenuDestinations.SurfaceOnly,
            "No screen opened on the captured save; behaviour with gallery content is unobserved",
            "Back closes the submenu", "not scheduled", NotBuilt),
        new("music-player", "Music Player", MainMenuDestinations.SurfaceOnly,
            "Track list player; the row is disabled on a new save",
            "Back returns here with Music Player focused", "audio phase", NotBuilt),
        new("tutorial", "Tutorial", MainMenuDestinations.SurfaceOnly,
            "Numbered tutorial list with Duel and Deck Edit tabs",
            "Back returns here with Tutorial focused", "not scheduled", NotBuilt),
        new("duel-ranking", "Duel Ranking", MainMenuDestinations.SurfaceOnly,
            "High Score and Bonus record lists with a rate of accomplishment",
            "Back returns here with Duel Ranking focused", "not scheduled", NotBuilt),
        new("forbidden-limited", "Forbidden/Limited Card List", MainMenuDestinations.SurfaceOnly,
            "Forbidden, Limited and Semi-Limited card lists with a card preview",
            "Back returns here with Forbidden/Limited Card List focused", "not scheduled", NotBuilt)
    });
}
