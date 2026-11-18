namespace TagForceSpecial.Assets.Presentation;

/// <summary>Deck Edit resources beyond the pixel-verified pins (<see cref="DeckEditReferenceResources"/>): the archive itself as a stored
/// text source, and members the PC's supported pool needs that no recorded original screen has drawn yet (decoded, not verified).</summary>
public static class DeckEditResources
{
    public const string ArchiveKey = "deck.source.all-ehp";

    /// <summary>(asset key, member, frame, palette level, reason). The Card Frame captures recorded the earlier unverified members; the Deck
    /// Edit menus draw two icon sheets that no pixel-verified screen covers yet.</summary>
    public static IReadOnlyList<(string AssetKey, string Member, int? Frame, int? PaletteLevel, string Reason)> Supplements { get; } =
        Array.AsReadOnly(new (string, string, int?, int?, string)[]
        {
            ("deck.all.icon05", "icon05.gim", null, null, "Deck Edit menu row icons; drawn in recorded menu dumps, pixels not yet verified"),
            ("deck.all.icon06", "icon06.gim", null, null, "Key Search cells; drawn in a recorded menu dump, pixels not yet verified")
        });
}
