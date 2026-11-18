using System.Text.Json;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.Album;

public sealed record CardAlbumGlyphs(float[] X, float Y, float ScaleX, float ScaleY, int Color, int ShadowAlpha);
public sealed record CardAlbumCounter(float SeparatorX, float LastDigitX, float Pitch, float[] TotalX, float Y, float ScaleX, float ScaleY, int Color, int ShadowAlpha);
/// <summary>Completion cells: digits right-aligned at <see cref="Pitch"/>; measured on the fresh save ("0.6"), the point sits
/// <see cref="PointAdvance"/> before the digit after it and the digit before the point <see cref="BeforePoint"/> before the point.</summary>
public sealed record CardAlbumPercent(float LastDigitX, float Pitch, float SignX, float PointAdvance, float BeforePoint);
/// <summary>Source offsets of the bracketed type line's words: monster types by type code, Spell/Trap kinds by icon code, monster
/// suffixes by catalog raw kind (inferred from the table order; matches every captured card).</summary>
public sealed record CardAlbumTypeLine(string Source, int Open, int Close, int Spell, int Trap, int[] IconWords, int[] TypeWords, Dictionary<string, int> KindSuffix);
public sealed record CardAlbumIcon(float[] Rect, float[] Uv);
public sealed record CardAlbumJumpCell(int Column, char Char, float X);
public sealed record CardAlbumJumpRow(float Y, CardAlbumJumpCell[] Cells);
/// <summary>Measured Page Jump overlay: panel, label, a highlight drawn for the first cell and moved by the cell pitch, and the letters
/// (drawn glyphs; no string table holds them). Letters without an owned card use the disabled alphas.</summary>
public sealed record CardAlbumPageJump(FlowDraw Dim, FlowDraw Panel, FlowText Label, FlowDraw[] Highlight, float CellPitch, CardAlbumJumpRow[] Rows,
    float ScaleX, float ScaleY, int Color, int ShadowAlpha, int DisabledAlpha, int DisabledShadowAlpha, CardAlbumJumpClose Close)
{
    public const int Columns = 8, RowCount = 5;
    public char? CharAt(int row, int column) => Rows[row].Cells.FirstOrDefault(c => c.Column == column)?.Char;
}
/// <summary>Measured Page Jump close and arrival (frame-stepped, one sample per game frame): one frame with the label, letters and highlight
/// at <see cref="FadeAlpha"/>, one frame per collapsing panel in <see cref="Panels"/> (over the dim), <see cref="IdleFrames"/> with no
/// overlay, one frame of turn keyframe <see cref="TurnKey"/>, then <see cref="SettleFrames"/> on the target view with the old preview.
/// The hand is hidden throughout.</summary>
public sealed record CardAlbumJumpClose(float FadeAlpha, FlowDraw[][] Panels, int IdleFrames, int TurnKey, int SettleFrames)
{
    public int TurnFrame => 1 + Panels.Length + IdleFrames;
    public int Length => TurnFrame + 1 + SettleFrames;
}
/// <summary>Measured on the fresh save: an unowned pocket previews the EBOOT card back (and no text).</summary>
public sealed record CardAlbumCardBack(FlowEbootTexture Texture, FlowDraw Draw);
/// <summary>Measured glint on the info strip (turn captures at two phases, one sample per game frame): its left edge moves from 0 to
/// <see cref="Far"/> over <see cref="ForwardFrames"/> frames, back to 0 over <see cref="BackFrames"/>, and repeats. The panel draw holds it
/// at <see cref="MeasuredLeft"/>.</summary>
public sealed record CardAlbumGlint(string AssetKey, float MeasuredLeft, float Far, int ForwardFrames, int BackFrames)
{
    public int Period => ForwardFrames + BackFrames;

    public float Left(int frame)
    {
        int t = frame % Period;
        return t <= ForwardFrames ? Far * t / ForwardFrames : Far * (Period - t) / BackFrames;
    }
}
public sealed record CardAlbumInfoText(float X, float FirstY, float LineHeight, int Lines, float Width, float ScaleX, float ScaleY, int ShadowAlpha,
    int NameColor, int TypeColor, int TextColor);

/// <summary>Measured Card Album 2D layer (card-album-frame.json: numbers, asset keys and source text offsets only). Hand offsets are
/// relative to the hand drawn over the first right-page pocket.</summary>
public sealed record CardAlbumProfile(int SchemaVersion, string BuildId, string Scope, FlowDraw[] Background, FlowDraw[] Header, FlowDraw[] Bar,
    FlowDraw[] Panel, FlowText Title, FlowText CompleteLabel, CardAlbumGlyphs Percent, FlowDraw Hand, float[][] HandRight, float[] HandEdge,
    Dictionary<string, float[]> HandLeftMeasured, float[][] HandBar, CardAlbumCounter Counter, CardAlbumPercent PercentCells, CardAlbumInfoText InfoText,
    CardAlbumTypeLine TypeLine, float[] AttributeIconRect, float[] TypeIconRect, Dictionary<string, float[]> TypeIconExtra,
    CardAlbumIcon LevelStar, CardAlbumGlyphs LevelText, int[] InfoScissor, CardAlbumCardBack CardBack, CardAlbumPageJump PageJump, CardAlbumGlint Glint)
{
    private const float LeftColumnPitch = 45;

    /// <summary>Hand offset over a pocket. Left-page pockets 4, 7 and 8 are measured; the others follow their 45 px column pitch (inferred).</summary>
    public float[] HandOffset(bool rightPage, int pocket)
    {
        if (rightPage) return HandRight[pocket];
        if (HandLeftMeasured.TryGetValue(pocket.ToString(System.Globalization.CultureInfo.InvariantCulture), out var measured)) return measured;
        int row = pocket / 3, col = pocket % 3;
        // Inferred: column x from the measured middle column (pocket 4) and the 45 px pitch; row y as on the right page.
        return [HandLeftMeasured["4"][0] + (col - 1) * LeftColumnPitch, HandRight[row * 3][1]];
    }

    public IEnumerable<string> AssetKeys() => Background.Concat(Header).Concat(Bar).Concat(Panel).Append(Hand).Append(PageJump.Dim)
        .Select(d => d.AssetKey).OfType<string>().Distinct(StringComparer.Ordinal);

    public static CardAlbumProfile LoadEmbedded()
    {
        using var stream = typeof(CardAlbumProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.Album.card-album-frame.json")
            ?? throw new InvalidDataException("Missing measured Card Album profile.");
        var value = JsonSerializer.Deserialize<CardAlbumProfile>(stream) ?? throw new InvalidDataException("Invalid Card Album profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Card Album profile/source mismatch.");
        if (value.HandRight.Length != 9 || value.HandBar.Length != CardAlbumSession.BarButtons || value.HandEdge.Length != 2)
            throw new InvalidDataException("Card Album hand positions are incomplete.");
        return value;
    }
}
