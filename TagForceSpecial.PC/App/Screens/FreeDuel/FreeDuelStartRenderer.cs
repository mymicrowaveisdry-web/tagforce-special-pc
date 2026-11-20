using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.FreeDuel;

/// <summary>Draws the duel start (rock-paper-scissors, turn order, intro card) into the Free Duel layer from its measured profile. Each state is
/// drawn at its measured settled pose; card flips, button slides, the intro slide-in and the floating particles are not animated.</summary>
public sealed class FreeDuelStartRenderer(GraphicsDevice device, TitleFlowVisuals visuals, FreeDuelStartProfile profile, FreeDuelVersusRenderer versus,
    FreeDuelPickerProfile roster)
{
    public FreeDuelStartProfile Profile => profile;
    public IReadOnlyList<string> Gaps { get; } = Array.AsReadOnly(new[]
    {
        "card flip, button slide and intro slide-in animations are not recreated; each state holds its settled pose for an inferred time",
        "floating particles drawn at one captured pose",
        "WIN and LOSE label cells taken from the atlas (DRAW measured)",
        "the CPU's hand and turn-order choice are random (the original's rule is not decoded)"
    });

    public void Render(RenderTarget2D layer, FreeDuelSession session, FreeDuelStartSession start, int backgroundFrame) => visuals.Within(layer, () =>
    {
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1, 0);
        device.RasterizerState = RasterizerState.CullNone;
        device.DepthStencilState = DepthStencilState.None;
        var p = profile;
        visuals.DrawMenuBackdrop(backgroundFrame);
        Draw(p.Particles);
        if (start.Step is FreeDuelStartStep.Intro or FreeDuelStartStep.Ready)
        {
            Draw(p.IntroStreak);
            Draw(p.IntroHeader);
            versus.DrawBody(session, p.IntroShift);
            return;
        }
        switch (start.Step)
        {
            case FreeDuelStartStep.Choose: DrawHand(start.Focus); break;
            case FreeDuelStartStep.Reveal: DrawReveal(start); break;
            default: DrawButtons(start.Step == FreeDuelStartStep.OpponentChoosing || start.FirstFocused); break;
        }
        Draw(p.Frame);
        visuals.DrawFlow(p.PlayerBust);
        visuals.DrawFlow(FreeDuelVersusRenderer.Bust(p.OpponentBust, roster.Characters[session.Opponent].Bust));
        Line(session.PlayerName, p.Text.PlayerName);
        Line(Text(roster.Characters[session.Opponent].Name), p.Text.OpponentName);
        var prompt = start.Step switch
        {
            FreeDuelStartStep.Choose => p.Prompts.ChooseCard,
            FreeDuelStartStep.ChooseOrder => p.Prompts.ChooseOrder,
            FreeDuelStartStep.OpponentChoosing => p.Prompts.OpponentChoosing,
            _ => null
        };
        if (prompt is not null) Line(Text(prompt), p.Text.Message);
    });

    /// <summary>Measured: the three hand cards side by side, the focused one raised and framed (captured with Paper focused); three backs above.</summary>
    private void DrawHand(FreeDuelHand focus)
    {
        var p = profile;
        Draw(p.OpponentBacks);
        float baseTop = p.Hand[0].Points.Min(q => q[1]);
        for (int k = 0; k < p.Hand.Length; k++)
        {
            var d = p.Hand[k];
            float top = baseTop - (k == (int)focus ? p.HandRaise : 0);
            visuals.DrawFlow(FreeDuelVersusRenderer.Shift(d, top - d.Points.Min(q => q[1])));
        }
        float dx = Left(p.Hand[(int)focus]) - Left(p.Hand[(int)FreeDuelHand.Paper]);
        foreach (var d in p.HandFocus) visuals.DrawFlow(d with { Points = d.Points.Select(q => new[] { q[0] + dx, q[1] }).ToArray() });
    }

    /// <summary>Measured with Rock against Rock: each card is its hand's cell (opponent's from the red row), with the result label below it.</summary>
    private void DrawReveal(FreeDuelStartSession start)
    {
        var p = profile;
        var result = start.Result;
        var opponent = result switch { FreeDuelRpsResult.Win => FreeDuelRpsResult.Lose, FreeDuelRpsResult.Lose => FreeDuelRpsResult.Win, _ => result };
        visuals.DrawFlow(Cell(p.RevealPlayer, (int)start.PlayerHand * p.CardCellWidth));
        visuals.DrawFlow(Cell(p.RevealOpponent, (int)start.OpponentHand * p.CardCellWidth));
        visuals.DrawFlow(Cell(p.LabelPlayer, (int)result * p.LabelCellWidth));
        visuals.DrawFlow(Cell(p.LabelOpponent, (int)opponent * p.LabelCellWidth));
    }

    /// <summary>Measured: both buttons always draw their dark face; the focused one also draws its light face (hidden on the other).</summary>
    private void DrawButtons(bool firstFocused)
    {
        var p = profile;
        Draw(p.SecondDark);
        if (!firstFocused) Draw(p.SecondLight);
        Draw(p.FirstButton.Take(3));
        if (firstFocused) Draw(p.FirstButton.Skip(3));
    }

    /// <summary>Moves a draw's texture window to the cell starting at <paramref name="u"/> atlas pixels (same row).</summary>
    private FlowDraw Cell(FlowDraw d, float u)
    {
        float shift = u / profile.AtlasWidth - d.Uv.Min(q => q[0]);
        return d with { Uv = d.Uv.Select(q => new[] { q[0] + shift, q[1] }).ToArray() };
    }

    private static float Left(FlowDraw d) => d.Points.Min(q => q[0]);

    private void Line(string text, FreeDuelTextStyle s)
    {
        if (text.Length > 0) visuals.DrawCardFontText(text, s.X, s.Y, s.ScaleX, s.ScaleY, s.Color, s.ShadowAlpha);
    }

    private string Text(FlowTextSource source)
    {
        string message = visuals.Message(source.Source, source.Offset);
        return source.Start + source.Chars <= message.Length ? message.Substring(source.Start, source.Chars)
            : throw new InvalidDataException("Free Duel start text reference outside its message.");
    }

    private void Draw(IEnumerable<FlowDraw> draws) { foreach (var d in draws) visuals.DrawFlow(d); }
}
