namespace TagForceSpecial.Assets.Presentation;

/// <summary>Deck Edit resources beyond the pixel-verified pins (<see cref="DeckEditReferenceResources"/>): the archive itself as a stored
/// text source, and members the PC's supported pool needs that no recorded original screen has drawn yet (decoded, not verified).</summary>
public static class DeckEditResources
{
    public const string ArchiveKey = "deck.source.all-ehp";

    /// <summary>(asset key, member, frame, palette level, reason). Frames 2 and 3 hold the row images of pool cards 5644 and 6960;
    /// frame.gim palette 0 is the Normal-monster frame by its colour, never observed in a dump.</summary>
    public static IReadOnlyList<(string AssetKey, string Member, int? Frame, int? PaletteLevel, string Reason)> Supplements { get; } = Array.AsReadOnly(new (string, string, int?, int?, string)[]
    {
        ("deck.all.cis-all.f02", "cis_all.gim", 2, null, "row images of supported cards; frame not yet drawn in a recorded screen"),
        ("deck.all.cis-all.f03", "cis_all.gim", 3, null, "row images of supported cards; frame not yet drawn in a recorded screen"),
        ("deck.all.frame.p00", "frame.gim", null, 0, "Normal-monster preview frame inferred from its colour; not yet drawn in a recorded screen")
    });
}
