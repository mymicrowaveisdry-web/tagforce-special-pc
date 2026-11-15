namespace TagForceSpecial.Assets.Presentation;

/// <summary>Deck Edit resources beyond the pixel-verified pins (<see cref="DeckEditReferenceResources"/>): the archive itself as a stored
/// text source, and members the PC's supported pool needs that no recorded original screen has drawn yet (decoded, not verified).</summary>
public static class DeckEditResources
{
    public const string ArchiveKey = "deck.source.all-ehp";

    /// <summary>(asset key, member, frame, palette level, reason). Empty since the Card Frame captures recorded every previously unverified
    /// member (row-image sheets 2 and 3, the Normal-monster frame); kept for members a future screen needs before it is recorded.</summary>
    public static IReadOnlyList<(string AssetKey, string Member, int? Frame, int? PaletteLevel, string Reason)> Supplements { get; } =
        Array.AsReadOnly(Array.Empty<(string, string, int?, int?, string)>());
}
