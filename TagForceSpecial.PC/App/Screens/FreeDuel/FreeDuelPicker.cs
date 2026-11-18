using System.Text.Json;
using TagForceSpecial.PC.App.Input;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

/// <summary>One listed character: series (1 DM … 5 ARC-V), rank inside its series, bust index, and name / team text by source offset.</summary>
public sealed record FreeDuelCharacter(int Series, int Rank, int Bust, FlowTextSource Name, FlowTextSource Team);
public sealed record FreeDuelPickerTab(FlowDraw[] Strip, float ActiveX);
public sealed record FreeDuelPickerTexts(FreeDuelTextStyle Number, FreeDuelTextStyle NameLabel, FreeDuelTextStyle Name, FreeDuelTextStyle Team,
    FreeDuelTextStyle[] Counters, FreeDuelTextStyle TabActive, FreeDuelTextStyle TabInactive);

/// <summary>The measured character picker (free-duel-picker-frame.json: numbers, asset keys and source offsets only). The roster is the EBOOT
/// character table in measured picker order (series, then main, cast, minor, then record): 192 characters, every one captured row matching.</summary>
public sealed record FreeDuelPickerProfile(int SchemaVersion, string BuildId, string Scope, FlowDraw[] Window, FreeDuelPickerTab[] Tabs, float[] TabLabelX,
    FlowTextSource[] TabLabels, FlowTextSource NameLabel, FlowDraw[] Row, FlowDraw Portrait, FlowDraw Sprite, FlowDraw[] Cursor, FlowDraw[] Scroll,
    float RowTop, float RowPitch, int VisibleRows, float TrackTop, float TrackLength, int PortraitCell, int PortraitColumns, int PortraitPerAtlas,
    int SpriteWidth, int SpriteHeight, int SpriteColumns, FreeDuelPickerTexts Text, FreeDuelCharacter[] Characters)
{
    public const int SeriesCount = 5;

    /// <summary>Characters of one series (1-based), in picker order.</summary>
    public IReadOnlyList<int> InSeries(int series) => Enumerable.Range(0, Characters.Length).Where(i => Characters[i].Series == series).ToArray();

    /// <summary>Measured: the character's number counts from No.001 across all series in picker order.</summary>
    public static int Number(int index) => index + 1;

    public IEnumerable<string> AssetKeys() => Window.Concat(Tabs.SelectMany(t => t.Strip)).Concat(Row).Concat(Cursor).Concat(Scroll)
        .Select(d => d.AssetKey).OfType<string>()
        .Concat(["title.flow.sml-bup01", "title.flow.sml-bup11", .. Enumerable.Range(1, SeriesCount).Select(s => $"title.flow.sddb-0{s}")])
        .Distinct(StringComparer.Ordinal);

    public static FreeDuelPickerProfile LoadEmbedded()
    {
        using var stream = typeof(FreeDuelPickerProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.FreeDuel.free-duel-picker-frame.json")
            ?? throw new InvalidDataException("Missing measured Free Duel picker profile.");
        var value = JsonSerializer.Deserialize<FreeDuelPickerProfile>(stream) ?? throw new InvalidDataException("Invalid Free Duel picker profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Free Duel picker profile/source mismatch.");
        if (value.Tabs.Length != SeriesCount || value.TabLabels.Length != SeriesCount || value.Characters.Length == 0 || value.VisibleRows <= 0
            || value.Characters.Any(c => c.Series is < 1 or > SeriesCount || c.Bust < 0))
            throw new InvalidDataException("Free Duel picker profile is incomplete.");
        return value;
    }
}

/// <summary>Observed picker input: L/R switch series tabs (wrapping; each tab keeps its own cursor, the opening tab starts on the current
/// opponent at the top row); Up/Down move one row and wrap within the tab, scrolling only past the shown rows; Right shows the next page with
/// the cursor on its first row (Left the previous one, inferred); Confirm picks; Back cancels.</summary>
public sealed class FreeDuelPickerSession
{
    private readonly FreeDuelPickerProfile profile;
    private readonly int[] focus = new int[FreeDuelPickerProfile.SeriesCount], top = new int[FreeDuelPickerProfile.SeriesCount];
    public int Series { get; private set; }
    public int Revision { get; private set; }

    public FreeDuelPickerSession(FreeDuelPickerProfile profile, int current)
    {
        this.profile = profile;
        var c = profile.Characters[current];
        Series = c.Series;
        focus[Series - 1] = c.Rank;
        top[Series - 1] = Math.Min(c.Rank, Math.Max(0, Count - profile.VisibleRows));
    }

    public IReadOnlyList<int> List => profile.InSeries(Series);
    public int Count => profile.InSeries(Series).Count;
    public int Focus => focus[Series - 1];
    public int Top => top[Series - 1];
    public int FocusedCharacter => List[Focus];

    public enum Outcome { None, Picked, Cancelled }

    public Outcome Handle(InputFrame input)
    {
        int s = Series - 1, n = Count, rows = profile.VisibleRows;
        var before = (Series, focus[s], top[s]);
        Outcome outcome = Outcome.None;
        if (input.IsPressed(InputAction.Up)) { focus[s] = (focus[s] + n - 1) % n; Reveal(s); }
        else if (input.IsPressed(InputAction.Down)) { focus[s] = (focus[s] + 1) % n; Reveal(s); }
        else if (input.IsPressed(InputAction.Right)) { top[s] = Math.Min(top[s] + rows, Math.Max(0, n - rows)); focus[s] = top[s]; }
        else if (input.IsPressed(InputAction.Left)) { top[s] = Math.Max(0, top[s] - rows); focus[s] = top[s]; }
        else if (input.IsPressed(InputAction.NextPage)) Series = Series % FreeDuelPickerProfile.SeriesCount + 1;
        else if (input.IsPressed(InputAction.PreviousPage)) Series = (Series + FreeDuelPickerProfile.SeriesCount - 2) % FreeDuelPickerProfile.SeriesCount + 1;
        else if (input.IsPressed(InputAction.Confirm)) outcome = Outcome.Picked;
        else if (input.IsPressed(InputAction.Back)) outcome = Outcome.Cancelled;
        if ((Series, focus[Series - 1], top[Series - 1]) != before) Revision++;
        return outcome;
    }

    private void Reveal(int s) => top[s] = Math.Clamp(top[s], Math.Max(0, focus[s] - profile.VisibleRows + 1), focus[s]);

    /// <summary>Measured scroll thumb: length ⌊track × shown / total⌋, top at track + ⌊first × (track − length) / (total − shown)⌋.</summary>
    public (float Top, float Length) Thumb()
    {
        float length = MathF.Floor(profile.TrackLength * profile.VisibleRows / Count);
        int span = Count - profile.VisibleRows;
        return (profile.TrackTop + (span <= 0 ? 0 : MathF.Floor(Top * (profile.TrackLength - length) / span)), length);
    }
}
