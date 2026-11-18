using System.Text.Json;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

/// <summary>Where and how one kind of text line is drawn (first glyph x, baseline y, scale, colour, shadow), measured in every step dump.</summary>
public sealed record FreeDuelTextStyle(float X, float Y, float ScaleX, float ScaleY, int Color, int ShadowAlpha);

/// <summary>One settings step: its prompt, list rows, and description lines per row (one entry when every row shares it).</summary>
public sealed record FreeDuelStepProfile(string Step, FlowTextSource Prompt, FlowTextSource[] Rows, FlowTextSource[][] Descriptions)
{
    public FlowTextSource[] DescriptionFor(int row) => Descriptions.Length == 0 ? [] : Descriptions[Math.Min(row, Descriptions.Length - 1)];
}

/// <summary>The measured Free Duel settings screen (free-duel-settings-frame.json: numbers, asset keys and source offsets only). The
/// background is the Main Menu's own animated layers; everything here is drawn over it. The focus pulse is inferred (Options curve).</summary>
public sealed record FreeDuelSettingsProfile(int SchemaVersion, string BuildId, string Scope, FlowDraw[] Header, FlowText HeaderText,
    FlowDraw[] SummaryEmpty, FlowDraw[] SummaryFilled, FlowDraw FormatIcon, FlowDraw EndingIcon, FlowDraw[] ListPanel, FlowDraw[] PromptBand,
    FlowDraw[] DescriptionPanel, FlowDraw[] NextIdle, FlowDraw[] NextActive, FlowText NextIdleText, FlowText NextActiveText, FlowDraw[] Highlight,
    float HighlightTop, FlowDraw ArrowUp, FlowDraw ArrowDown, float RowY, float RowPitch, int VisibleRows, FreeDuelTextStyle Prompt,
    FreeDuelTextStyle Row, FreeDuelTextStyle Description, float DescriptionLineHeight, FreeDuelTextStyle SummaryFormat,
    FreeDuelTextStyle SummaryField, float[] SummaryFieldX, FlowTextSource[] SummaryFormats, FreeDuelStepProfile[] Steps, int[] FocusPulse)
{
    public FreeDuelStepProfile StepProfile(FreeDuelStep step) => Steps[(int)step];

    public IEnumerable<string> AssetKeys() => Header.Concat(SummaryEmpty).Concat(SummaryFilled).Append(FormatIcon).Append(EndingIcon)
        .Concat(ListPanel).Concat(PromptBand).Concat(DescriptionPanel).Concat(NextIdle).Concat(NextActive).Concat(Highlight)
        .Append(ArrowUp).Append(ArrowDown).Select(d => d.AssetKey).OfType<string>().Distinct(StringComparer.Ordinal);

    public static FreeDuelSettingsProfile LoadEmbedded()
    {
        using var stream = typeof(FreeDuelSettingsProfile).Assembly.GetManifestResourceStream("TagForceSpecial.PC.App.Screens.FreeDuel.free-duel-settings-frame.json")
            ?? throw new InvalidDataException("Missing measured Free Duel settings profile.");
        var value = JsonSerializer.Deserialize<FreeDuelSettingsProfile>(stream) ?? throw new InvalidDataException("Invalid Free Duel settings profile.");
        if (value.SchemaVersion != 1 || value.BuildId != Assets.GameSource.GameBuildProfile.English21.BuildId)
            throw new InvalidDataException("Free Duel settings profile/source mismatch.");
        var order = Enum.GetNames<FreeDuelStep>();
        if (value.Steps.Length != order.Length || value.Steps.Select(s => s.Step).Where((s, i) => s != order[i]).Any())
            throw new InvalidDataException("Free Duel settings steps differ from the session's steps.");
        if (value.SummaryFieldX.Length != 3 || value.SummaryFormats.Length != 3 || value.VisibleRows <= 0 || value.FocusPulse.Length == 0)
            throw new InvalidDataException("Free Duel settings profile is incomplete.");
        return value;
    }
}
