using System.Text.Json;

namespace TagForceSpecial.PC.App.Screens.Title;

public sealed record DeckEditTextSource(string Source, int Offset);
public sealed record DeckEditField(float X, float Y, float ScaleX, float ScaleY, int Color, int ShadowAlpha, string Align, float? Pitch, DeckEditTextSource? Source);
public sealed record DeckEditLines(float X, float FirstY, float LineHeight, float Width, float ScaleX, float ScaleY, int ShadowAlpha,
    int NameColor, int TypeColor, int TextColor);
public sealed record DeckEditFields(DeckEditField DeckName, DeckEditField PossessionLabel, DeckEditField PossessionCount, DeckEditField Help,
    DeckEditField RowName, DeckEditField RowOwned, DeckEditField RowAttack, DeckEditField RowSlash, DeckEditField RowDefense,
    DeckEditField RowAttackLabel, DeckEditField RowDefenseLabel, DeckEditField[] RowColumns, DeckEditLines PreviewLines,
    DeckEditField PreviewAttackLabel, DeckEditField PreviewAttack, DeckEditField PreviewDefenseLabel, DeckEditField PreviewDefense,
    DeckEditTextSource PreviewSlash);
/// <summary>One line of a measured message: characters [Start, Start + Chars) of the normalized source text (null Chars = to the end).</summary>
public sealed record DeckEditLine(float X, float Y, int Start, int? Chars);
public sealed record DeckEditMoveOverlay(FlowDraw Shade, FlowDraw Mask, FlowDraw Box, FlowDraw CardFrame, FlowDraw Mini, DeckEditLine[] Lines,
    float ScaleX, float ScaleY, int Color, int ShadowAlpha);
public sealed record DeckEditDialogButton(float X, float Y, float LabelX, FlowText Label);
/// <summary>The exit question. Only the OK-focused state was recorded; CANCEL focus draws the same focus pieces at its own button
/// (<see cref="CancelFocusInferred"/>), as both states were measured on the Options dialogs.</summary>
public sealed record DeckEditExitDialog(FlowDraw Mask, FlowDraw Panel, FlowDraw[] Focus, FlowDraw Plain, DeckEditDialogButton[] Buttons,
    float LabelY, float LabelScaleX, float LabelScaleY, int LabelColor, int LabelShadow, DeckEditLine[] Lines, float ScaleX, float ScaleY,
    int Color, int ShadowAlpha, bool CancelFocusInferred);
public sealed record DeckEditTabLabel(float X, DeckEditTextSource Source);
public sealed record DeckEditTabText(DeckEditTabLabel[] Labels, float[] CountRight, float Pitch, float ActiveY, float InactiveY,
    int ActiveColor, int InactiveColor, float ScaleX, float ScaleY, int ShadowAlpha);
public sealed record DeckEditMiniImage(int Base, int Sheet, int Grid, int Cell);
public sealed record DeckEditPreview(FlowDraw Frame, FlowDraw Art, FlowDraw Name, float[] Composite, FlowDraw[] Statics);

/// <summary>A lookup result: <see cref="Inferred"/> is true when the value was never seen on a recorded original row.</summary>
public readonly record struct DeckEditLookup<T>(T Value, bool Inferred);

/// <summary>Measured Deck Edit core screen (deck-edit-frame.json: draws, asset keys, UV tables and source text offsets only), from
/// analysis/reconstruction/phase62_export.py. Row draws are given for row 0; later rows add <see cref="RowPitch"/>. Per-card pieces come
/// from tables learned on recorded rows; <see cref="Inferred"/> holds the explicitly unobserved entries the PC's Normal monsters need.</summary>
public sealed record DeckEditProfile(int SchemaVersion, string BuildId, string Scope, float RowTop, float RowPitch, int VisibleRows,
    DeckEditMiniImage MiniImage, FlowDraw[] Background, FlowDraw[] Window, Dictionary<string, FlowDraw[]> Tabs, Dictionary<string, FlowDraw> Row,
    Dictionary<string, Dictionary<string, JsonElement>> Tables, FlowDraw[] Cursor, DeckEditPreview Preview, DeckEditFields Fields,
    DeckEditTabText TabText, DeckEditTextSource MovePrompt, DeckEditTextSource ExitQuestion, Dictionary<string, int> FramePaletteByKind,
    Dictionary<string, int> InferredFramePaletteByKind, Dictionary<string, Dictionary<string, JsonElement>> Inferred,
    DeckEditMoveOverlay MoveOverlay, DeckEditExitDialog ExitDialog)
{
    /// <summary>Tab order as the original cycles them with Square, starting on the Trunk.</summary>
    public static readonly string[] TabCycle = ["trunk", "deck", "extra", "side"];

    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public static DeckEditProfile LoadEmbedded()
    {
        using var stream = typeof(DeckEditProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.Title.deck-edit-frame.json")
            ?? throw new InvalidDataException("Missing measured Deck Edit profile.");
        var value = JsonSerializer.Deserialize<DeckEditProfile>(stream, Json) ?? throw new InvalidDataException("Invalid Deck Edit profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Deck Edit profile/source mismatch.");
        if (value.VisibleRows is < 1 or > 16 || value.RowPitch <= 0 || TabCycle.Any(t => !value.Tabs.ContainsKey(t)) || value.Cursor.Length == 0 ||
            value.Fields.RowColumns.Length != 3 || value.TabText.Labels.Length != 4 || value.TabText.CountRight.Length != 4)
            throw new InvalidDataException("Deck Edit profile is incomplete.");
        return value;
    }

    private DeckEditLookup<JsonElement>? Find(string table, string key)
    {
        if (Tables.TryGetValue(table, out var observed) && observed.TryGetValue(key, out var value)) return new(value, false);
        if (Inferred.TryGetValue(table, out var inferred) && inferred.TryGetValue(key, out var guess)) return new(guess, true);
        return null;
    }

    /// <summary>A UV rectangle [u0, v0, u1, v1] from a learned table, or null when neither observed nor inferred.</summary>
    public DeckEditLookup<float[]>? Uv(string table, uint key) =>
        Find(table, key.ToString(System.Globalization.CultureInfo.InvariantCulture)) is { } found
            ? new(found.Value.EnumerateArray().Select(v => v.GetSingle()).ToArray(), found.Inferred) : null;

    public DeckEditLookup<float[]>? Uv(string table, string key) =>
        Find(table, key) is { } found ? new(found.Value.EnumerateArray().Select(v => v.GetSingle()).ToArray(), found.Inferred) : null;

    public DeckEditLookup<string>? PanelKey(uint rawKind) =>
        Find("panelByKind", rawKind.ToString(System.Globalization.CultureInfo.InvariantCulture)) is { } found ? new(found.Value.GetString()!, found.Inferred) : null;

    public DeckEditLookup<int>? FramePalette(uint rawKind)
    {
        string key = rawKind.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (FramePaletteByKind.TryGetValue(key, out int level)) return new(level, false);
        if (InferredFramePaletteByKind.TryGetValue(key, out int guess)) return new(guess, true);
        return null;
    }

    /// <summary>Row image: cis_all.gim frame and cell. Measured on every recorded row: cell = internal index - <see cref="DeckEditMiniImage.Base"/>.</summary>
    public (string AssetKey, float[] Uv)? GetMiniImage(int internalIndex)
    {
        int cell = internalIndex - MiniImage.Base;

        if (cell < 0)
            return null;

        int sheet = cell / MiniImage.Sheet;
        int inSheet = cell % MiniImage.Sheet;

        if (sheet > 9)
            return null;

        float size = MiniImage.Cell / 512f;
        float u = inSheet % MiniImage.Grid * size;
        float v = inSheet / MiniImage.Grid * size;

        return (
            $"deck.all.cis-all.f{sheet:00}",
            [u, v, u + size, v + size]
        );
    }

    /// <summary>Every asset key a draw in this profile can reference (tables and palettes included).</summary>
    public IEnumerable<string> AssetKeys()
    {
        var keys = Background
            .Concat(Window)
            .Concat(Tabs.Values.SelectMany(t => t))
            .Concat(Row.Values)
            .Concat(Cursor)
            .Concat(Preview.Statics)
            .Concat([MoveOverlay.Shade, MoveOverlay.Mask, MoveOverlay.Box, MoveOverlay.CardFrame, ExitDialog.Mask, ExitDialog.Panel, ExitDialog.Plain])
            .Concat(ExitDialog.Focus)
            .Select(d => d.AssetKey)
            .OfType<string>()
            .Where(k => !k.StartsWith(
                "card.",
                StringComparison.Ordinal));

        IEnumerable<JsonElement> inferredPanels =
            Inferred.TryGetValue("panelByKind", out var inferred)
                ? inferred.Values
                : Enumerable.Empty<JsonElement>();

        var panels = Tables["panelByKind"]
            .Values
            .Concat(inferredPanels)
            .Select(v => v.GetString()!);

        var frames = FramePaletteByKind.Values
            .Concat(InferredFramePaletteByKind.Values)
            .Select(p => $"deck.all.frame.p{p:00}");

        var miniFrames = Enumerable.Range(0, 10)
            .Select(sheet => $"deck.all.cis-all.f{sheet:00}");

        return keys
            .Concat(panels)
            .Concat(frames)
            .Concat(miniFrames)
            .Distinct(StringComparer.Ordinal);
    }
}
