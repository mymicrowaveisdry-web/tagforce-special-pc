using System.Globalization;
using System.Text;
using TagForceSpecial.Cards;

namespace TagForceSpecial.PC.App.Screens.Album;

/// <summary>Card Album order and binder arithmetic, as measured on the same build: every catalog card except Tokens in name order,
/// 18 per sheet (9 on the front, 9 on the back), each view showing the back of sheet v on the left and the front of sheet v + 1 on the
/// right. The collation reproduces all 7,159 captured positions; it is inferred from them, not read from the game's own routine.</summary>
public static class CardAlbumLayout
{
    public const int PocketsPerPage = 9, PocketsPerSheet = 18, TokenRawKind = 10;

    /// <summary>Album order: Tokens left out; name collation, then same-name alternate artworks by descending card ID.</summary>
    public static IReadOnlyList<uint> Order(CardCatalog catalog) => Array.AsReadOnly(catalog.Cards
        .Where(c => c.Codes?.RawKind != TokenRawKind)
        .OrderBy(c => PrimaryKey(c.Name), StringComparer.Ordinal)
        .ThenBy(c => SecondaryKey(c.Name), StringComparer.Ordinal)
        .ThenByDescending(c => c.GameCardId)
        .Select(c => c.GameCardId).ToArray());

    /// <summary>Measured: case, accents, hyphens, apostrophes and colons are ignored; a star counts as a space.</summary>
    public static string PrimaryKey(string name)
    {
        var folded = Fold(name);
        var sb = new StringBuilder(folded.Length);
        foreach (char c in folded)
            if (c is not ('-' or '\'' or ':')) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>Tie-break between names equal under <see cref="PrimaryKey"/>: hyphens sort after letters ("Raimei" before "Rai-Mei").</summary>
    public static string SecondaryKey(string name) => Fold(name).Replace('-', '\u007f');

    private static string Fold(string name)
    {
        var decomposed = name.Replace('☆', ' ').Normalize(NormalizationForm.FormKD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString().ToUpperInvariant();
    }

    /// <summary>Sheets needed for <paramref name="cards"/> album positions (398 for the measured 7,159).</summary>
    public static int Sheets(int cards) => (cards + PocketsPerSheet - 1) / PocketsPerSheet;

    /// <summary>Views run 0..Sheets: view v shows the back of sheet v (none for v = 0) and the front of sheet v + 1 (none past the last sheet).</summary>
    public static int Views(int cards) => Sheets(cards) + 1;

    /// <summary>Album position of a pocket in view <paramref name="view"/>: left page (back of sheet v) or right page (front of sheet v + 1),
    /// pockets row-major left to right on both pages. Null when the page does not exist.</summary>
    public static int? Position(int view, bool rightPage, int pocket, int cards)
    {
        if (pocket is < 0 or >= PocketsPerPage) throw new ArgumentOutOfRangeException(nameof(pocket));
        int sheet = rightPage ? view + 1 : view;
        if (sheet < 1 || sheet > Sheets(cards)) return null;
        int position = (sheet - 1) * PocketsPerSheet + (rightPage ? 0 : PocketsPerPage) + pocket;
        return position < cards ? position : null;
    }

    /// <summary>Album position of a captured slot (relative to the first pocket of right sheet <paramref name="rightSheet"/>), or null outside the album.</summary>
    public static int? SlotPosition(int rightSheet, int slot, int cards)
    {
        int position = (rightSheet - 1) * PocketsPerSheet + slot;
        return position >= 0 && position < cards ? position : null;
    }

    /// <summary>Measured page counter: the number of the sheet under the cursor.</summary>
    public static int CounterSheet(int view, bool rightPage) => rightPage ? view + 1 : view;

    /// <summary>The view that shows <paramref name="position"/> and whether it is on the right page (front of a sheet).</summary>
    public static (int View, bool RightPage, int Pocket) Locate(int position)
    {
        int sheet = position / PocketsPerSheet + 1, inSheet = position % PocketsPerSheet;
        bool front = inSheet < PocketsPerPage;
        return front ? (sheet - 1, true, inSheet) : (sheet, false, inSheet - PocketsPerPage);
    }
}
