namespace TagForceSpecial.Assets.Presentation;

/// <summary>database/cardalbum.ehp members the Card Album draws (binder textures, background, header, hand cursor and bottom bar): each texture the original uploads for the binder is one whole
/// GIM with its own palette. This table names the members and their asset keys; <see cref="CardAlbumReferenceResources"/> pins their pixels.</summary>
public static class CardAlbumResources
{
    public const string Archive = "PSP_GAME/USRDIR/database/cardalbum.ehp";
    /// <summary>Stored copy of card/cardm_j.cip; <see cref="CardFaceSource"/> decodes pocket faces from it on demand.</summary>
    public const string FacesKey = "album.source.cardm";
    /// <summary>Stored copy of the archive itself: the screen's labels are read from its string table (strTbl_j.bin).</summary>
    public const string ArchiveKey = "album.source.cardalbum-ehp";
    /// <summary>Stored copy of card/cardh_j.cip: preview art for any card, decoded on demand with <see cref="CardThumbnailDecoder"/>.</summary>
    public const string ArtKey = "album.source.cardh";
    /// <summary>Stored copy of duelsys/cardinfo_jpn.ehp: the info panel's bracketed type line is built from its WORD_Text_R.bin words.</summary>
    public const string CardInfoKey = "album.source.cardinfo-ehp";
    public const string CardInfoPath = "PSP_GAME/USRDIR/duelsys/cardinfo_jpn.ehp";

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
            ("album.binder.album-cursor", "album_cursor.gim", null, null, "Pocket cursor highlight"),
            ("album.ui.bg", "bg.gim", null, null, "Screen background"),
            ("album.ui.misc", "misc.gim", null, null, "Header bar"),
            ("album.ui.hand", "hand.gim", null, null, "Hand cursor"),
            ("album.ui.skipmenu0001", "skipmenu0001.gim", null, null, "Bottom bar part"),
            ("album.ui.skipmenu0002", "skipmenu0002.gim", null, null, "Bottom bar part"),
            ("album.ui.skipmenu0003", "skipmenu0003.gim", null, null, "Bottom bar part"),
            ("album.ui.skipmenu0004", "skipmenu0004.gim", null, null, "Bottom bar part"),
            ("album.ui.skipmenu0005", "skipmenu0005.gim", null, null, "Bottom bar part"),
            ("album.ui.skipmenu0006", "skipmenu0006.gim", null, null, "Bottom bar part"),
            ("album.ui.skipmenu0007", "skipmenu0007.gim", null, null, "Bottom bar part"),
            ("album.ui.skipmenu0008", "skipmenu0008.gim", null, null, "Bottom bar part"),
            ("album.ui.skipmenu0010", "skipmenu0010.gim", null, null, "Bottom bar part"),
            ("album.ui.skipmenu0012", "skipmenu0012.gim", null, null, "Bottom bar part"),
            ("album.ui.skipmenu0014", "skipmenu0014.gim", null, null, "Bottom bar part")
        });

    /// <summary>Asset key for a binder texture named by its archive member.</summary>
    public static string KeyFor(string member) =>
        Supplements.SingleOrDefault(s => s.Member == member).AssetKey ?? throw new KeyNotFoundException("Card Album member is not imported: " + member);
}
