using TagForceSpecial.Core.Decks;

namespace TagForceSpecial.PC.App.Screens.Album;

/// <summary>Which album cards are owned, and where that knowledge came from. The original draws neither backing nor face for an unowned
/// pocket; this projection only answers ownership and never invents counts.</summary>
public sealed class CardAlbumCollection
{
    private readonly HashSet<uint> owned;
    public string Source { get; }
    public int OwnedCount => owned.Count;

    private CardAlbumCollection(IEnumerable<uint> owned, string source) { this.owned = [.. owned]; Source = source; }

    public bool Owns(uint cardId) => owned.Contains(cardId);

    /// <summary>The PC profile's own collection: every card with a positive count.</summary>
    public static CardAlbumCollection FromPlayer(PlayerCollection collection) =>
        new(collection.Counts.Where(c => c.Value > 0).Select(c => c.Key), "player collection " + collection.Revision);

    /// <summary>Until the PC acquires cards, the measured everything-unlocked reference state: every album card owned.</summary>
    public static CardAlbumCollection ReferenceAllObtained(IEnumerable<uint> albumCards) => new(albumCards, "reference save state: all cards obtained");

    /// <summary>Album completion as the original shows it: one decimal, truncated (49 of 7,159 shows "0.6").</summary>
    public string CompletionText(IReadOnlyList<uint> order)
    {
        if (order.Count == 0) return "0";
        int have = order.Count(Owns);
        if (have == order.Count) return "100";
        int tenths = (int)((long)have * 1000 / order.Count);
        return (tenths / 10).ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + (tenths % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
