using System.Text.Json;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.Album;

public sealed record CardDetailTrack(float Top, float Length, float Visible);
/// <summary>Attribute word (yellow) at a fixed x; <see cref="Words"/> are source offsets by attribute code 1..7.</summary>
public sealed record CardDetailAttribute(float X, int Color, string Source, int[] Words);
/// <summary>Monster stats: "ATK/" and "DEF/" glyph cells, values right-aligned to the last digit cell at the digit pitch.</summary>
public sealed record CardDetailStats(float Y, float ScaleX, float ScaleY, int Color, int ShadowAlpha, float[] AtkLabelX, float AtkLastDigitX,
    float[] DefLabelX, float DefLastDigitX, float DigitPitch);
/// <summary>The description: wrapped at <see cref="Width"/> from pen x, its first glyph row <see cref="FirstBaselineOffset"/> below
/// <see cref="Top"/> minus the scroll, clipped by <see cref="Scissor"/> (x0, y0, x1, y1 inclusive). Glyph cells sit <see cref="GlyphOffset"/>
/// times the scale left of the pen.</summary>
public sealed record CardDetailBody(float PenX, float Top, float Width, float LineHeight, float FirstBaselineOffset, float ScaleX, float ScaleY,
    int Color, int ShadowAlpha, float GlyphOffset, int[] Scissor);
public sealed record CardDetailScroll(float PixelsPerFrame, float Visible, float GenreRow, float PackRowCounted, float PackTrailing);
public sealed record CardDetailGenreRows(FlowDraw Icon, float IconPitch, float LabelX, float LabelY, float ScaleX, float ScaleY, int Color, int ShadowAlpha);
/// <summary>Pack List rows: frame pieces and icon for row 0 (moved by <see cref="Pitch"/>), the name from pen x at the largest scale from
/// <see cref="StartScale"/> down in <see cref="ScaleStep"/> steps whose width stays under <see cref="MaxWidth"/>, and the rarity badges R, SR, UR.</summary>
public sealed record CardDetailPackRows(FlowDraw[] Frame, FlowDraw Icon, float Pitch, float PenX, float GlyphOffset, float Y, float ScaleY, int Color,
    int ShadowAlpha, float StartScale, float ScaleStep, float MaxWidth, float[] MeasuredScales, FlowDraw[] Badges);
/// <summary>Spell Speed panel: label, the speed sprite (speed 1 as measured; each further speed <see cref="SpriteStepV"/> lower in the
/// texture), and genre icons <see cref="IconPitch"/> apart, <see cref="IconsPerRow"/> per row, from the first icon's place.</summary>
public sealed record CardDetailSpeed(FlowText Label, FlowDraw Sprite, float SpriteStepV, FlowDraw GenreIcon, float IconPitch, int IconsPerRow);

/// <summary>Measured Card Album detail (card-album-detail.json: numbers, asset keys and EBOOT text offsets only).</summary>
public sealed record CardAlbumDetailProfile(int SchemaVersion, string BuildId, string Scope, FlowDraw[] Static, FlowDraw[] Thumb, CardDetailTrack Track,
    float[] AttributeIconRect, float[] TypeIconRect, CardAlbumGlyphs Name, CardAlbumGlyphs TypeLine, CardDetailAttribute Attribute, CardDetailStats Stats,
    CardAlbumIcon LevelStar, CardAlbumIcon RankStar, CardAlbumIcon ScaleIcon, CardAlbumGlyphs LevelText, CardAlbumGlyphs ScaleText,
    CardAlbumGlyphs Counters, Dictionary<string, FlowText> Titles, Dictionary<string, FlowText> Buttons, float[] ButtonY, CardDetailBody Body,
    CardDetailScroll Scroll, CardDetailGenreRows GenreRows, CardDetailPackRows PackRows, CardDetailSpeed Speed)
{
    /// <summary>Tab order as Right cycles it.</summary>
    public static readonly string[] Tabs = ["CARD DESCRIPTION", "CARD GENRE", "Pack List"];

    public IEnumerable<string> AssetKeys() => Static.Concat(Thumb).Concat(PackRows.Frame).Concat(PackRows.Badges)
        .Append(PackRows.Icon).Append(GenreRows.Icon).Append(Speed.Sprite).Append(Speed.GenreIcon)
        .Select(d => d.AssetKey).OfType<string>().Distinct(StringComparer.Ordinal);

    public static CardAlbumDetailProfile LoadEmbedded()
    {
        using var stream = typeof(CardAlbumDetailProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.Album.card-album-detail.json")
            ?? throw new InvalidDataException("Missing measured Card Album detail profile.");
        var value = JsonSerializer.Deserialize<CardAlbumDetailProfile>(stream) ?? throw new InvalidDataException("Invalid Card Album detail profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Card Album detail profile schema/build mismatch.");
        if (Tabs.Any(t => !value.Titles.ContainsKey(t) || !value.Buttons.ContainsKey(t)) || value.ButtonY.Length != 2 || value.Body.Scissor.Length != 4)
            throw new InvalidDataException("Card Album detail profile is incomplete.");
        return value;
    }
}
