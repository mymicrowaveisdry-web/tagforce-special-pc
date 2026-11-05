using System.Text.Json;
using TagForceSpecial.PC.App.Input;

namespace TagForceSpecial.PC.App.Screens.Title;

public sealed record OptionsPixelQuad(float X0, float Y0, float X1, float Y1, float U0, float V0, float U1, float V1, int[] Color, bool Additive, int[] Source);
public sealed record OptionsValueRun(float[] X, float DeltaY, float ScaleX, float ScaleY, int Color, int ShadowAlpha);
public sealed record OptionsRow(int Id, string Kind, float BandTop, FlowText Label, FlowTextSource[] Values, FlowText Help);
public sealed record OptionsFrameProfile(int SchemaVersion, string BuildId, string Scope, string AtlasKey, OptionsPixelQuad[] Panel, OptionsPixelQuad[] Help,
    OptionsPixelQuad[] Focus, Dictionary<string, OptionsPixelQuad> Arrows, float ArrowOffset, float ArrowLeftX, float ArrowRightX,
    OptionsPixelQuad SegmentFull, OptionsPixelQuad SegmentEmpty, int SegmentStep, int SegmentCount, float SegmentOffset,
    int[] FocusPulse, FlowQuad Title, OptionsRow[] Rows, FlowText[] Groups, Dictionary<string, OptionsValueRun> ValueRuns, FlowFrame[] Dialogs)
{
    public const string CancelDialog = "cancel-ok-focus", SaveQuestion = "save-question";

    /// <summary>Measured: the focus band's alpha cycles every 30 game frames, counted from the Options entry.</summary>
    public int PulseAt(int entryFrame) => FocusPulse[((entryFrame % FocusPulse.Length) + FocusPulse.Length) % FocusPulse.Length];

    public static OptionsFrameProfile LoadEmbedded()
    {
        using var stream = typeof(OptionsFrameProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.Title.options-frame.json")
            ?? throw new InvalidDataException("Missing measured Options profile.");
        var value = JsonSerializer.Deserialize<OptionsFrameProfile>(stream) ?? throw new InvalidDataException("Invalid Options profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Options profile/source mismatch.");
        if (value.Rows.Length != OptionsCatalog.Rows.Count) throw new InvalidDataException("Options profile has the wrong row count.");
        if (value.FocusPulse.Length == 0 || value.FocusPulse.Any(a => a is < 0 or > 255)) throw new InvalidDataException("Options focus pulse is invalid.");
        for (int i = 0; i < value.Rows.Length; i++)
        {
            var row = OptionsCatalog.Rows[i];
            if (row.Kind == OptionKind.Choice && value.Rows[i].Values.Length != row.Count)
                throw new InvalidDataException($"Options row {i} measures {value.Rows[i].Values.Length} values, catalog expects {row.Count}.");
            foreach (var choice in value.Rows[i].Values)
                if (!value.ValueRuns.ContainsKey(choice.Offset.ToString(System.Globalization.CultureInfo.InvariantCulture)))
                    throw new InvalidDataException($"Options value at {choice.Offset} has no measured run.");
        }
        return value;
    }
}

public enum OptionKind { Choice, Slider }

/// <summary>One original Options row. <paramref name="PcEffect"/> says honestly what the PC does with the value today.</summary>
public sealed record OptionRowDefinition(string Id, OptionKind Kind, int Count, int Default, bool Applied, string PcEffect);

/// <summary>Observed on the same build (config/reference/options.json): 13 rows, values clamp at both ends, new-game defaults below.
/// Only Duelist Level reaches a PC system today; every other row is stored and saved but not yet applied.</summary>
public static class OptionsCatalog
{
    public const int DuelistLevelRow = 12, SliderLevels = 16;
    public static IReadOnlyList<OptionRowDefinition> Rows { get; } = Array.AsReadOnly(new OptionRowDefinition[]
    {
        new("message-speed", OptionKind.Choice, 3, 0, false, "Stored; no PC dialogue system uses it yet."),
        new("voice-volume", OptionKind.Slider, SliderLevels, SliderLevels - 1, false, "Stored; audio belongs to the separate audio phase."),
        new("bgm-volume", OptionKind.Slider, SliderLevels, SliderLevels - 1, false, "Stored; audio belongs to the separate audio phase."),
        new("se-volume", OptionKind.Slider, SliderLevels, SliderLevels - 1, false, "Stored; audio belongs to the separate audio phase."),
        new("card-animation", OptionKind.Choice, 4, 1, false, "Stored; duel presentation (Phase 9) will read it."),
        new("duel-effects", OptionKind.Choice, 3, 1, false, "Stored; duel presentation (Phase 9) will read it."),
        new("original-system", OptionKind.Choice, 2, 1, false, "Stored; Destiny Draw and Partner Synchro rules are not implemented, so it changes no rule."),
        new("detailed-confirmation", OptionKind.Choice, 2, 1, false, "Stored; duel prompt policy does not read it yet."),
        new("activation-confirmation", OptionKind.Choice, 4, 2, false, "Stored; chain prompt policy does not read it yet."),
        new("card-placement", OptionKind.Choice, 2, 0, false, "Stored; duel input does not read it yet."),
        new("auto-draw", OptionKind.Choice, 2, 0, false, "Stored; duel input does not read it yet."),
        new("skip-rock-paper-scissors", OptionKind.Choice, 2, 0, false, "Stored; no Rock, Paper, Scissors step exists yet."),
        new("duelist-level", OptionKind.Choice, 3, 0, true, "Saved as the profile's Duelist Level; Story Mode is not built yet.")
    });

    public static int[] Defaults(DuelistLevelChoice level)
    {
        var values = Rows.Select(r => r.Default).ToArray();
        values[DuelistLevelRow] = (int)level;
        return values;
    }

    public static bool Valid(IReadOnlyList<int> values) => values.Count == Rows.Count && values.Select((v, i) => v >= 0 && v < Rows[i].Count).All(ok => ok);
}

/// <summary>Versioned, app-owned copy of the original Options values, saved with the native profile. Duelist Level lives on the profile itself.</summary>
public sealed record PlayerOptions(int SchemaVersion, int[] Values)
{
    public const int CurrentSchema = 1;
    public static PlayerOptions Defaults() => new(CurrentSchema, OptionsCatalog.Defaults(DuelistLevelChoice.FirstTimer)[..OptionsCatalog.DuelistLevelRow]);

    public bool Valid() => SchemaVersion == CurrentSchema && Values.Length == OptionsCatalog.DuelistLevelRow &&
        Values.Select((v, i) => v >= 0 && v < OptionsCatalog.Rows[i].Count).All(ok => ok);

    public int[] WithLevel(DuelistLevelChoice level) => [.. Values, (int)level];
}

public enum OptionsOutcome { None, Accepted, Discarded }

/// <summary>Pure Options screen state. Observed: Up/Down wrap over the 13 rows (group titles are skipped); Left/Right change the value
/// and stop at either end; confirm leaves keeping the changes; back always asks "All changes will be canceled" with OK focused,
/// where OK discards, CANCEL stays and back does nothing; other buttons do nothing.</summary>
public sealed class OptionsSession(int[] initial)
{
    private readonly int[] initial = initial.Length == OptionsCatalog.Rows.Count ? (int[])initial.Clone() : throw new ArgumentException("Wrong value count.");
    private readonly int[] values = (int[])initial.Clone();
    public int Focus { get; private set; }
    public bool CancelQuestion { get; private set; }
    /// <summary>0 = OK (default), 1 = CANCEL.</summary>
    public int CancelFocus { get; private set; }
    public IReadOnlyList<int> Values => values;
    public bool Changed => !values.AsSpan().SequenceEqual(initial);

    public OptionsOutcome Handle(PspInputFrame input)
    {
        if (CancelQuestion)
        {
            if (input.IsPressed(PspAction.Left) || input.IsPressed(PspAction.Right)) CancelFocus = 1 - CancelFocus;
            if (!input.IsPressed(PspAction.Cross)) return OptionsOutcome.None;
            if (CancelFocus == 0) return OptionsOutcome.Discarded;
            CancelQuestion = false;
            return OptionsOutcome.None;
        }
        int rows = OptionsCatalog.Rows.Count;
        if (input.IsPressed(PspAction.Up)) Focus = (Focus + rows - 1) % rows;
        else if (input.IsPressed(PspAction.Down)) Focus = (Focus + 1) % rows;
        else if (input.IsPressed(PspAction.Left)) values[Focus] = Math.Max(0, values[Focus] - 1);
        else if (input.IsPressed(PspAction.Right)) values[Focus] = Math.Min(OptionsCatalog.Rows[Focus].Count - 1, values[Focus] + 1);
        if (input.IsPressed(PspAction.Cross)) return OptionsOutcome.Accepted;
        if (input.IsPressed(PspAction.Circle)) { CancelQuestion = true; CancelFocus = 0; }
        return OptionsOutcome.None;
    }
}
