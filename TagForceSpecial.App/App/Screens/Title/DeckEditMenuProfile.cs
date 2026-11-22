using System.Text.Json;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>A measured text run: the source string (null for generated titles such as numbers) and where and how it was drawn;
/// <see cref="Pitch"/> is set for digits drawn in fixed cells.</summary>
public sealed record DeckEditMenuText(string? Source, int Offset, int Start, int? Chars, float X, float Y, float ScaleX, float ScaleY, int Color, int ShadowAlpha,
    float? Pitch = null)
{
    public DeckEditTextSource? Reference => Source is null ? null : new(Source, Offset);
}

public sealed record DeckEditMenuRow(DeckEditMenuText Text, FlowDraw[] Icons, bool Disabled);
/// <summary>Row band geometry: left edge relative to the row text, fixed right edge, top relative to the text baseline, height.</summary>
public sealed record DeckEditMenuBand(float DX0, float X1, float DY0, float Height);
public sealed record DeckEditMenuCell(int[] Box, FlowDraw[] Parts);
public sealed record DeckEditMenuRun(FlowDraw Button, DeckEditMenuText Label);

/// <summary>One recorded popup layer. List popups carry <see cref="Rows"/> and <see cref="Band"/>; grid popups carry <see cref="Cells"/>,
/// <see cref="CellBand"/> (offsets from the cell box) and usually a Run button. <see cref="InferredCells"/> name cells whose titles were not
/// seen on the original.</summary>
public sealed record DeckEditMenuPopup(string Id, string Evidence, FlowDraw Shade, FlowDraw Panel, FlowDraw[] Chrome, DeckEditMenuText? Title,
    DeckEditMenuText[] Texts, DeckEditMenuRow[]? Rows, DeckEditMenuBand? Band, DeckEditMenuCell[]? Cells, float[]? CellBand, DeckEditMenuRun? Run,
    DeckEditTextSource?[]? CellNames, int[]? InferredCells)
{
    public bool IsGrid => Cells is not null;
    public int Count => Rows?.Length ?? Cells?.Length ?? 0;
}

/// <summary>The nine dialog-panel pieces of the recorded focus band at <see cref="Rect"/>: corners keep their 4 px, edges stretch.</summary>
public sealed record DeckEditBandTemplate(float[] Rect, FlowDraw[] Pieces, int[] MarkerColor);

/// <summary>An OK notice ("%d card(s) have met these conditions."): the count is drawn at <see cref="Number"/> in its own colour, then the
/// message lines; <see cref="Button"/> holds the focused OK button pieces.</summary>
public sealed record DeckEditMenuNotice(string Id, string Evidence, FlowDraw Shade, FlowDraw Panel, FlowDraw[] Button, DeckEditMenuText? Number,
    DeckEditMenuText[] Lines, DeckEditMenuText Ok, DeckEditTextSource Message);

/// <summary>Grid templates: <see cref="SelectedCellTemplate.Draw"/> is the square behind a selected cell recorded at <see cref="SelectedCellTemplate.Cell"/>;
/// <see cref="RunFocusTemplate.Pieces"/> replace the plain Run button piece when Run is focused, recorded at <see cref="RunFocusTemplate.Rect"/>.</summary>
public sealed record SelectedCellTemplate(FlowDraw Draw, int[] Cell);
public sealed record RunFocusTemplate(FlowDraw[] Pieces, int[] Rect);

/// <summary>Focus band alpha per frame: <see cref="Opening"/> once when a popup opens, then <see cref="Cycle"/> repeating (66 frames).</summary>
public sealed record DeckEditMenuPulse(int[] Opening, int[] Cycle)
{
    public int AlphaAt(int frame) => frame < Opening.Length ? Opening[frame] : Cycle[(frame - Opening.Length) % Cycle.Length];
}

/// <summary>Measured Deck Edit menus (deck-edit-menu-frame.json: draws, asset keys and source text offsets only), from
/// analysis/reconstruction/phase62_menu_export.py over the menus-20261115 GE dumps. Behaviour lives in <see cref="DeckEditMenus"/>.</summary>
public sealed record DeckEditMenuProfile(int SchemaVersion, string BuildId, string Scope, DeckEditMenuPulse Pulse, DeckEditMenuPopup[] Popups,
    DeckEditBandTemplate BandTemplate, DeckEditMenuNotice RunNotice, SelectedCellTemplate SelectedCell, RunFocusTemplate RunFocus)
{
    public const float Corner = 4;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public DeckEditMenuPopup this[string id] => Popups.FirstOrDefault(p => p.Id == id) ?? throw new KeyNotFoundException($"No measured Deck Edit popup '{id}'.");

    public static DeckEditMenuProfile LoadEmbedded()
    {
        using var stream = typeof(DeckEditMenuProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.Title.deck-edit-menu-frame.json")
            ?? throw new InvalidDataException("Missing measured Deck Edit menu profile.");
        var value = JsonSerializer.Deserialize<DeckEditMenuProfile>(stream, Json) ?? throw new InvalidDataException("Invalid Deck Edit menu profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Deck Edit menu profile/source mismatch.");
        if (value.Pulse.Cycle.Length == 0 || value.BandTemplate.Pieces.Length != 9 || value.BandTemplate.Rect.Length != 4 ||
            DeckEditMenus.PopupIds.Any(id => value.Popups.All(p => p.Id != id)) ||
            value.Popups.Any(p => (p.Rows is null) == (p.Cells is null) || p.Rows is not null && p.Band is null || p.Cells is not null && p.CellBand?.Length != 4))
            throw new InvalidDataException("Deck Edit menu profile is incomplete.");
        return value;
    }

    /// <summary>The band pieces stretched to a rectangle: points within <see cref="Corner"/> px of a recorded edge keep that distance
    /// from the new edge; colour and alpha replaced.</summary>
    public IEnumerable<FlowDraw> Band(float x0, float y0, float x1, float y1, int[] rgb, int alpha)
    {
        var r = BandTemplate.Rect;
        float X(float x) => x <= r[0] + Corner ? x0 + (x - r[0]) : x1 - (r[2] - x);
        float Y(float y) => y <= r[1] + Corner ? y0 + (y - r[1]) : y1 - (r[3] - y);
        int[] color = [rgb[0], rgb[1], rgb[2], alpha];
        return BandTemplate.Pieces.Select(p => p with { Points = Array.ConvertAll(p.Points, q => new[] { X(q[0]), Y(q[1]) }), Color = color });
    }

    public int[] FocusColor => BandTemplate.Pieces[0].Color;

    public IEnumerable<string> AssetKeys() => Popups
        .SelectMany(p => new[] { p.Shade, p.Panel }.Concat(p.Chrome).Concat(p.Rows?.SelectMany(r => r.Icons) ?? [])
            .Concat(p.Cells?.SelectMany(c => c.Parts) ?? []).Concat(p.Run is { } run ? [run.Button] : []))
        .Concat(BandTemplate.Pieces).Concat([RunNotice.Shade, RunNotice.Panel]).Concat(RunNotice.Button).Concat(RunFocus.Pieces).Append(SelectedCell.Draw)
        .Select(d => d.AssetKey).OfType<string>()
        .Where(k => !k.StartsWith("deck.all.cis-all.", StringComparison.Ordinal))
        .Distinct(StringComparer.Ordinal);
}
