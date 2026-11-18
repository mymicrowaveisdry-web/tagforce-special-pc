using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

/// <summary>Draws the Normal Duel VERSUS screen from its measured profile into the Free Duel layer. Owner decision (18 Nov 2026): the
/// opponent's deck slot names the PC's own deck, labelled as such; the opponent character's name and portrait imply no character AI.</summary>
public sealed class FreeDuelVersusRenderer(GraphicsDevice device, TitleFlowVisuals visuals, FreeDuelVersusProfile profile, FreeDuelPickerProfile roster)
{
    /// <summary>PC-authored label of the deck the deterministic PC opponent plays (the INITIAL PREVIEW deck).</summary>
    public static string PcDeckLabel => "PC: " + Effects.InitialOpponentDecks.Names[0];
    public FreeDuelVersusProfile Profile => profile;
    public IReadOnlyList<string> Gaps { get; } = Array.AsReadOnly(new[]
    {
        "VERSUS streak drawn at its measured pose (its animation is not measured)",
        "the □ glyph in the hint is not in the Latin card font"
    });

    public void Render(RenderTarget2D layer, FreeDuelSession session, int backgroundFrame) => visuals.Within(layer, () =>
    {
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1, 0);
        device.RasterizerState = RasterizerState.CullNone;
        device.DepthStencilState = DepthStencilState.None;
        var p = profile; var versus = session.Versus!; var t = p.Text;
        visuals.DrawMenuBackdrop(backgroundFrame);
        Draw(p.Streak);
        Draw(p.Header);
        Run(t.Header);
        bool start = versus.Focus == FreeDuelVersusFocus.Start;
        Draw(start ? p.StartActive : p.StartIdle);
        string lp = session.Settings.Settings!.StartingLifePoints.ToString("00000", CultureInfo.InvariantCulture);
        Draw(p.PlayerPortrait); visuals.DrawFlow(p.PlayerBust); visuals.DrawFlow(p.PlayerLpBadge); Digits(p.PlayerLpDigits, lp);
        Draw(p.OpponentPortrait); visuals.DrawFlow(Bust(p.OpponentBust, roster.Characters[session.Opponent].Bust)); visuals.DrawFlow(p.OpponentLpBadge);
        Digits(p.OpponentLpDigits, lp);
        Draw(p.PlayerNamePanel); Draw(p.OpponentNamePanel); Draw(p.PlayerDeckPanel); Draw(p.OpponentDeckPanel);
        switch (versus.Focus)
        {
            case FreeDuelVersusFocus.OpponentName: Draw(p.Focus.OpponentName); break;
            case FreeDuelVersusFocus.OpponentDeck: Draw(p.Focus.OpponentDeck); break;
            case FreeDuelVersusFocus.PlayerDeck: Draw(p.Focus.PlayerDeck); break;
        }
        Line(Text(roster.Characters[session.Opponent].Name), t.OpponentName);
        Line(PcDeckLabel, t.OpponentDeck);
        Line(session.PlayerName, t.PlayerName);
        Line(session.PlayerDeck, t.PlayerDeck);
        // Measured: the character hint on the opponent's name, the deck hint on the player's deck, none elsewhere.
        if (versus.Focus == FreeDuelVersusFocus.OpponentName) Run(t.Hint);
        else if (versus.Focus == FreeDuelVersusFocus.PlayerDeck)
            visuals.DrawCardFontText(Text(p.DeckHint), t.Hint.X[0], t.Hint.Y, t.Hint.ScaleX, t.Hint.ScaleY, t.Hint.Color, t.Hint.ShadowAlpha);
        Run(t.Start);
    });

    /// <summary>Title of the PC notice: the focused part's own text.</summary>
    public string NoticeTitle(FreeDuelSession session) => session.Versus!.Focus switch
    {
        FreeDuelVersusFocus.OpponentName => Text(roster.Characters[session.Opponent].Name),
        FreeDuelVersusFocus.OpponentDeck => PcDeckLabel,
        FreeDuelVersusFocus.PlayerDeck => session.PlayerDeck,
        _ => Text(new(profile.Text.Start.Source, profile.Text.Start.Offset, profile.Text.Start.Start, profile.Text.Start.Chars))
    };

    /// <summary>Measured: busts are 64-pixel cells, 8 per row, 64 per atlas mdl_bup01 / 11 / 21 / 31 (Aki 16, Rinko Aomi 184, Mai Zenba 196 checked).</summary>
    private static FlowDraw Bust(FlowDraw d, int bust)
    {
        int atlas = bust / 64, cell = bust % 64;
        float height = atlas == 3 ? 64 : 512; // mdl_bup31 is 512 x 64 (one row); the others 512 x 512
        float u0 = cell % 8 / 8f, v0 = cell / 8 * 64 / height, minU = d.Uv.Min(q => q[0]), minV = d.Uv.Min(q => q[1]);
        return d with
        {
            AssetKey = $"title.flow.mdl-bup{atlas}1",
            Uv = d.Uv.Select(q => new[] { q[0] - minU + u0, (q[1] - minV) * 512 / height + v0 }).ToArray()
        };
    }

    private void Digits(FlowDraw[] cells, string value)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            var d = cells[i];
            float shift = profile.DigitU0 + (value[i] - '0') * profile.DigitStep - d.Uv[0][0];
            visuals.DrawFlow(d with { Uv = d.Uv.Select(uv => new[] { uv[0] + shift, uv[1] }).ToArray() });
        }
    }

    private void Line(string text, FreeDuelTextStyle style)
    {
        if (text.Length > 0) visuals.DrawCardFontText(text, style.X, style.Y, style.ScaleX, style.ScaleY, style.Color, style.ShadowAlpha);
    }

    private void Run(FlowText run) =>
        visuals.DrawCardFontText(Text(new(run.Source, run.Offset, run.Start, run.Chars)), run.X[0], run.Y, run.ScaleX, run.ScaleY, run.Color, run.ShadowAlpha);

    private string Text(FlowTextSource source)
    {
        string message = visuals.Message(source.Source, source.Offset);
        return source.Start + source.Chars <= message.Length ? message.Substring(source.Start, source.Chars)
            : throw new InvalidDataException("Free Duel versus text reference outside its message.");
    }

    private void Draw(IEnumerable<FlowDraw> draws) { foreach (var draw in draws) visuals.DrawFlow(draw); }
}
