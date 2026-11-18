using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

/// <summary>Draws the Free Duel settings screen into its own layer from the measured profile, over the Main Menu's animated background.
/// Text comes from the original string tables by offset, in the card-text font (measured: its advances match every Free Duel line to
/// 0.1 px; the menu font's are 2.6–7.5 px wider), from each measured line anchor.</summary>
public sealed class FreeDuelSettingsRenderer : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly TitleFlowVisuals visuals;
    public FreeDuelSettingsProfile Profile { get; }
    /// <summary>The VERSUS screen drawn into the same layer after Next.</summary>
    public FreeDuelVersusRenderer Versus { get; }
    /// <summary>The character picker drawn into the same layer while open.</summary>
    public FreeDuelPickerRenderer Picker { get; }
    private readonly int defaultOpponent;
    public RenderTarget2D Layer { get; }
    /// <summary>Things drawn from inference rather than measurement; reported, never hidden.</summary>
    public IReadOnlyList<string> Gaps { get; } = Array.AsReadOnly(new[]
    {
        "focus highlight pulse uses the Options curve (Free Duel curve not measured)",
        "music scroll arrows drawn steadily (the original blinks them; period not measured)",
        "full-width parentheses in music titles are not drawn (the Latin card font has no glyph; Japanese system font not imported)"
    });

    public FreeDuelSettingsRenderer(GraphicsDevice device, TitleFlowVisuals visuals, FreeDuelSettingsProfile profile, FreeDuelVersusProfile versus,
        FreeDuelPickerProfile roster)
    {
        this.device = device; this.visuals = visuals; Profile = profile; Versus = new(device, visuals, versus, roster); Picker = new(device, visuals, roster);
        // Measured default opponent: the character whose name the VERSUS capture shows (Aki Izayoi).
        defaultOpponent = Array.FindIndex(roster.Characters, c => c.Name.Source == versus.DefaultOpponent.Name.Source && c.Name.Offset == versus.DefaultOpponent.Name.Offset);
        if (defaultOpponent < 0) throw new InvalidDataException("The measured default opponent is not in the roster.");
        Layer = new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
    }

    /// <summary>Loads the measured profile and its pinned textures from the import; null (reported) when any is missing.</summary>
    public static FreeDuelSettingsRenderer? TryCreate(GraphicsDevice device, TitleFlowVisuals visuals, App.Visual.OriginalScreenAssetResolver assets)
    {
        try
        {
            var profile = FreeDuelSettingsProfile.LoadEmbedded();
            var versus = FreeDuelVersusProfile.LoadEmbedded();
            var roster = FreeDuelPickerProfile.LoadEmbedded();
            visuals.UseTextures(assets, profile.AssetKeys().Concat(versus.AssetKeys()).Concat(roster.AssetKeys())
                .Concat(["title.flow.mdl-bup11", "title.flow.mdl-bup21", "title.flow.mdl-bup31"]).Where(k => !k.StartsWith("title.eboot.", StringComparison.Ordinal)));
            visuals.UseCharaList(assets, Assets.Presentation.FreeDuelReferenceResources.CharaListKey);
            return new(device, visuals, profile, versus, roster);
        }
        catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or IOException or System.Text.Json.JsonException)
        {
            Console.Error.WriteLine("FREE_DUEL_PENDING " + e.Message);
            return null;
        }
    }

    /// <summary>A new visit: every opening starts on the first step with Normal Duel focused (measured). The player side shows the PC
    /// profile's name and the PC's current deck (owner decision).</summary>
    public FreeDuelSession NewSession(string playerName, string playerDeck) =>
        new(new(Profile.StepProfile(FreeDuelStep.Music).Rows.Length, Profile.VisibleRows), playerName, playerDeck, Picker.Profile, defaultOpponent);

    /// <param name="backgroundFrame">Main Menu background animation frame.</param>
    /// <param name="pulseFrame">Game frames since the highlight appeared.</param>
    public void Render(FreeDuelSettingsSession session, int backgroundFrame, int pulseFrame) => visuals.Within(Layer, () =>
    {
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1, 0);
        device.RasterizerState = RasterizerState.CullNone;
        device.DepthStencilState = DepthStencilState.None;
        var p = Profile;
        visuals.DrawMenuBackdrop(backgroundFrame);
        Draw(p.Header);
        Run(p.HeaderText);
        DrawSummary(session);
        Draw(p.ListPanel);
        Draw(p.PromptBand);
        var step = p.StepProfile(session.Step);
        Line(Text(step.Prompt), p.Prompt, 0);
        if (session.Step == FreeDuelStep.Confirm) DrawConfirmation(session);
        else DrawList(session, step, pulseFrame);
        Draw(p.DescriptionPanel);
        if (session.Step != FreeDuelStep.Confirm)
        {
            var lines = step.DescriptionFor(session.Focus);
            for (int i = 0; i < lines.Length; i++) Line(Text(lines[i]), p.Description, i * p.DescriptionLineHeight);
        }
        bool active = session.Step == FreeDuelStep.Confirm;
        Draw(active ? p.NextActive : p.NextIdle);
        Run(active ? p.NextActiveText : p.NextIdleText);
    });

    private void DrawList(FreeDuelSettingsSession session, FreeDuelStepProfile step, int pulseFrame)
    {
        var p = Profile;
        int alpha = p.FocusPulse[Math.Max(0, pulseFrame) % p.FocusPulse.Length];
        float dy = (session.Focus - session.Top) * p.RowPitch;
        foreach (var draw in p.Highlight) visuals.DrawFlow(Shift(draw, dy) with { Color = [draw.Color[0], draw.Color[1], draw.Color[2], alpha] });
        int last = Math.Min(step.Rows.Length, session.Top + p.VisibleRows);
        for (int row = session.Top; row < last; row++) Line(Text(step.Rows[row]), p.Row, (row - session.Top) * p.RowPitch);
        if (session.Step != FreeDuelStep.Music) return;
        if (session.Top > 0) visuals.DrawFlow(p.ArrowUp);
        if (last < step.Rows.Length) visuals.DrawFlow(p.ArrowDown);
    }

    /// <summary>Measured: the confirmation lists the chosen format, ending, LP, time limit and music as rows, with no highlight.</summary>
    private void DrawConfirmation(FreeDuelSettingsSession session)
    {
        var settings = session.Settings!;
        string[] lines = [RowText(FreeDuelStep.Format, settings.Format), RowText(FreeDuelStep.Ending, settings.Ending),
            RowText(FreeDuelStep.LifePoints, settings.LifePoints), RowText(FreeDuelStep.TimeLimit, settings.TimeLimit), RowText(FreeDuelStep.Music, settings.Music)];
        for (int i = 0; i < lines.Length; i++) Line(lines[i], Profile.Row, i * Profile.RowPitch);
    }

    /// <summary>Measured: empty until a format is chosen; then the format icon and name, the ending icon and word, and LP / time once known
    /// (standard rules fill both at once).</summary>
    private void DrawSummary(FreeDuelSettingsSession session)
    {
        var p = Profile;
        if (session.Chosen(FreeDuelStep.Format) is not int format) { Draw(p.SummaryEmpty); return; }
        Draw(p.SummaryFilled);
        visuals.DrawFlow(p.FormatIcon);
        Line(Text(p.SummaryFormats[format]), p.SummaryFormat, 0);
        if (session.Chosen(FreeDuelStep.Ending) is not int ending) return;
        visuals.DrawFlow(p.EndingIcon);
        Field(RowText(FreeDuelStep.Ending, ending), 0);
        if (session.Chosen(FreeDuelStep.Rules) is not int rules) return;
        int? lp = rules == 0 ? 1 : session.Chosen(FreeDuelStep.LifePoints), time = rules == 0 ? 0 : session.Chosen(FreeDuelStep.TimeLimit);
        if (lp is int l) Field(RowText(FreeDuelStep.LifePoints, l), 1);
        if (time is int t) Field(RowText(FreeDuelStep.TimeLimit, t), 2);
    }

    /// <summary>Title of the PC notice: the focused row's own source label, or "Next" on the confirmation step.</summary>
    public string NoticeTitle(FreeDuelSettingsSession session)
    {
        var next = Profile.NextIdleText;
        return session.Step == FreeDuelStep.Confirm ? Text(new(next.Source, next.Offset, next.Start, next.Chars)) : RowText(session.Step, session.Focus);
    }

    private void Field(string text, int slot) => visuals.DrawCardFontText(text, Profile.SummaryFieldX[slot], Profile.SummaryField.Y, Profile.SummaryField.ScaleX,
        Profile.SummaryField.ScaleY, Profile.SummaryField.Color, Profile.SummaryField.ShadowAlpha);

    private void Line(string text, FreeDuelTextStyle style, float dy)
    {
        if (text.Length > 0) visuals.DrawCardFontText(text, style.X, style.Y + dy, style.ScaleX, style.ScaleY, style.Color, style.ShadowAlpha);
    }

    private void Run(FlowText run) =>
        visuals.DrawCardFontText(Text(new(run.Source, run.Offset, run.Start, run.Chars)), run.X[0], run.Y, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha);

    private string RowText(FreeDuelStep step, int row) => Text(Profile.StepProfile(step).Rows[row]);

    private string Text(FlowTextSource source)
    {
        string message = visuals.Message(source.Source, source.Offset);
        return source.Start + source.Chars <= message.Length ? message.Substring(source.Start, source.Chars)
            : throw new InvalidDataException("Free Duel text reference outside its message.");
    }

    private void Draw(IEnumerable<FlowDraw> draws) { foreach (var draw in draws) visuals.DrawFlow(draw); }

    private static FlowDraw Shift(FlowDraw draw, float dy) => draw with { Points = draw.Points.Select(q => new[] { q[0], q[1] + dy }).ToArray() };

    public void Dispose() => Layer.Dispose();
}
