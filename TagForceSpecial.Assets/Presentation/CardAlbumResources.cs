namespace TagForceSpecial.Assets.Presentation;

/// <summary>database/cardalbum.ehp members the Card Album binder draws: each texture the original uploads for the binder is one whole
/// GIM with its own palette. Decoded with the shipping GIM decoder; pixel equality with the original uploads is checked separately.</summary>
public static class CardAlbumResources
{
    public const string Archive = "PSP_GAME/USRDIR/database/cardalbum.ehp";
    /// <summary>Stored copy of card/cardm_j.cip; <see cref="CardFaceSource"/> decodes pocket faces from it on demand.</summary>
    public const string FacesKey = "album.source.cardm";

    /// <summary>(asset key, member, frame, palette level, reason).</summary>
    public static IReadOnlyList<(string AssetKey, string Member, int? Frame, int? PaletteLevel, string Reason)> Supplements { get; } =
        Array.AsReadOnly(new (string, string, int?, int?, string)[]
        {
            ("album.binder.sheet01", "sheet01.gim", null, null, "Binder page sheet"),
            ("album.binder.kankyou", "kankyou.gim", null, null, "Page and ring reflection overlay"),
            ("album.binder.dmy-card00", "dmy_card00.gim", null, null, "Pocket backing"),
            ("album.binder.dmy-card01", "dmy_card01.gim", null, null, "Pocket sleeve"),
            ("album.binder.dmy-card02", "dmy_card02.gim", null, null, "Pocket sleeve (second layer)"),
            ("album.binder.cover-size-green", "cover_size_green.gim", null, null, "Binder cover"),
            ("album.binder.album-cursor", "album_cursor.gim", null, null, "Pocket cursor highlight")
        });

    /// <summary>Asset key for a binder texture named by its archive member.</summary>
    public static string KeyFor(string member) =>
        Supplements.SingleOrDefault(s => s.Member == member).AssetKey ?? throw new KeyNotFoundException("Card Album member is not imported: " + member);
}
