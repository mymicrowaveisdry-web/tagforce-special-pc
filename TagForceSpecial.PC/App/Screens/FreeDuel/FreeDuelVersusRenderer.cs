using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

/// <summary>Draws the Normal Duel VERSUS screen from its measured profile into the Free Duel layer. Owner decision (18 Nov 2026): the
/// opponent's deck slot names the PC's own deck, labelled as such; the opponent character's name and portrait imply no character AI.</summary>
public sealed class FreeDuelVersusRenderer(GraphicsDevice device, TitleFlowVisuals visuals, FreeDuelVersusProfile profile, FreeDuelPickerProfile roster)
{
    // Leave room for the glyph overhang, shadow, and panel border.
    private readonly float playerDeckLabelWidth = profile.PlayerDeckPanel.SelectMany(draw => draw.Points).Max(point => point[0])
        - profile.Text.PlayerDeck.X - 7;

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
        DrawBody(session, 0, () =>
        {
            switch (versus.Focus)
            {
                case FreeDuelVersusFocus.OpponentName: Draw(p.Focus.OpponentName); break;
                case FreeDuelVersusFocus.OpponentDeck: Draw(p.Focus.OpponentDeck); break;
                case FreeDuelVersusFocus.PlayerDeck: Draw(p.Focus.PlayerDeck); break;
            }
        });
        // Measured: the character hint on the opponent's name, the deck hint on the player's deck, none elsewhere.
        if (versus.Focus == FreeDuelVersusFocus.OpponentName) Run(t.Hint);
        else if (versus.Focus == FreeDuelVersusFocus.PlayerDeck)
            visuals.DrawCardFontText(Text(p.DeckHint), t.Hint.X[0], t.Hint.Y, t.Hint.ScaleX, t.Hint.ScaleY, t.Hint.Color, t.Hint.ShadowAlpha);
        Run(t.Start);
    });

    /// <summary>Portraits, LP badges, name and deck panels and their text, <paramref name="dy"/> pixels lower (the duel intro card draws the same
    /// body 25 pixels down, measured); <paramref name="overPanels"/> draws between the panels and the text.</summary>
    public void DrawBody(FreeDuelSession session, float dy, Action? overPanels = null)
    {
        var p = profile; var t = p.Text;
        string lp = session.Settings.Settings!.StartingLifePoints.ToString("00000", CultureInfo.InvariantCulture);
        Draw(p.PlayerPortrait, dy); visuals.DrawFlow(Shift(p.PlayerBust, dy)); visuals.DrawFlow(Shift(p.PlayerLpBadge, dy)); Digits(p.PlayerLpDigits, lp, dy);
        Draw(p.OpponentPortrait, dy); visuals.DrawFlow(Shift(Bust(p.OpponentBust, roster.Characters[session.Opponent].Bust), dy));
        visuals.DrawFlow(Shift(p.OpponentLpBadge, dy)); Digits(p.OpponentLpDigits, lp, dy);
        Draw(p.PlayerNamePanel, dy); Draw(p.OpponentNamePanel, dy); Draw(p.PlayerDeckPanel, dy); Draw(p.OpponentDeckPanel, dy);
        overPanels?.Invoke();
        Line(Text(roster.Characters[session.Opponent].Name), t.OpponentName, dy);
        Line(PcDeckLabel, t.OpponentDeck, dy);
        Line(session.PlayerName, t.PlayerName, dy);
        DrawPlayerDeckLabel(session.PlayerDeck, dy);
    }

    private void DrawPlayerDeckLabel(string label, float dy)
    {
        if (label.Length == 0 || playerDeckLabelWidth <= 0) return;
        var style = profile.Text.PlayerDeck;
        float width = visuals.MeasureCardFontText(label, style.ScaleX);
        float scale = Math.Min(style.ScaleX, style.ScaleX * playerDeckLabelWidth / width);
        float minimumScale = style.ScaleX * 0.85f;
        if (scale < minimumScale)
        {
            scale = minimumScale;
            const string ellipsis = "...";
            if (visuals.MeasureCardFontText(ellipsis, scale) > playerDeckLabelWidth) return;
            foreach (int cut in StringInfo.ParseCombiningCharacters(label).Reverse())
            {
                string shortened = label[..cut].TrimEnd() + ellipsis;
                if (visuals.MeasureCardFontText(shortened, scale) > playerDeckLabelWidth) continue;
                label = shortened;
                break;
            }
        }
        Line(label, style with { ScaleX = scale }, dy);
    }

    /// <summary>Title of the PC notice: the focused part's own text.</summary>
    public string NoticeTitle(FreeDuelSession session) => session.Versus!.Focus switch
    {
        FreeDuelVersusFocus.OpponentName => Text(roster.Characters[session.Opponent].Name),
        FreeDuelVersusFocus.OpponentDeck => PcDeckLabel,
        FreeDuelVersusFocus.PlayerDeck => session.PlayerDeck,
        _ => Text(new(profile.Text.Start.Source, profile.Text.Start.Offset, profile.Text.Start.Start, profile.Text.Start.Chars))
    };

    /// <summary>Measured: busts are 64-pixel cells, 8 per row, 64 per atlas mdl_bup01 / 11 / 21 / 31 (Aki 16, Rinko Aomi 184, Mai Zenba 196 checked).</summary>
    internal static FlowDraw Bust(FlowDraw d, int bust)
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

    private void Digits(FlowDraw[] cells, string value, float dy)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            var d = cells[i];
            float shift = profile.DigitU0 + (value[i] - '0') * profile.DigitStep - d.Uv[0][0];
            visuals.DrawFlow(Shift(d with { Uv = d.Uv.Select(uv => new[] { uv[0] + shift, uv[1] }).ToArray() }, dy));
        }
    }

    private void Line(string text, FreeDuelTextStyle style, float dy)
    {
        if (text.Length > 0) visuals.DrawCardFontText(text, style.X, style.Y + dy, style.ScaleX, style.ScaleY, style.Color, style.ShadowAlpha);
    }

    internal static FlowDraw Shift(FlowDraw d, float dy) =>
        dy == 0 ? d : d with { Points = d.Points.Select(q => new[] { q[0], q[1] + dy }).ToArray() };

    private void Draw(IEnumerable<FlowDraw> draws, float dy) { foreach (var draw in draws) visuals.DrawFlow(Shift(draw, dy)); }

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
