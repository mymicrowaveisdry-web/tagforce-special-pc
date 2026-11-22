using System.Globalization;
using TagForceSpecial.PC.App.Visual.Flow;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>The Main Menu's two flow layers as host-neutral commands: the animated 480 × 272 background and the pre-rendered label slots
/// (both row states of every row), plus the measured bar atlas pieces. Port of the MonoGame host's Main Menu layer code.</summary>
public sealed class MainMenuLayers
{
    public const string PanelKey = "title.flow.m-menu-panel01";
    public const int Rows = 8, LabelWidth = 253, LabelHeight = 24, LabelLayerHeight = LabelHeight * 2 * Rows;
    private const string FrameId = "main-menu";
    private const int TintDraw = 2, BarLeft = 114, FirstHeaderDraw = 21, LastHeaderDraw = 36, TitleDraw = 37;
    private readonly FlowFrame frame;
    private readonly Dictionary<int, Func<int, FlowDraw>> animatedDraws = [];

    /// <summary>Atlas rectangles of the bar pieces, measured from GE dumps; they mirror the OriginalMenuBar Gum component.</summary>
    public static IReadOnlyList<(string Piece, int X, int Y, int Width, int Height)> BarPieces { get; } =
    [
        ("BaseLeft", 20, 0, 6, 24), ("BaseCenter", 26, 0, 1, 24), ("BaseRight", 37, 0, 3, 24),
        ("FocusLeft", 0, 0, 6, 24), ("FocusCenter", 6, 0, 1, 24), ("FocusRight", 17, 0, 3, 24)
    ];

    /// <summary>The label slot of a row in the label layer: unfocused first, focused below it.</summary>
    public static (int X, int Y, int Width, int Height) LabelSource(int row, bool focused) =>
        (0, (row * 2 + (focused ? 1 : 0)) * LabelHeight, LabelWidth, LabelHeight);

    public MainMenuAnimation Motion { get; }

    public MainMenuLayers(FlowProfile profile, MainMenuAnimation motion)
    {
        frame = profile.Frames.Single(f => f.Id == FrameId);
        if (frame.Menu?.Rows.Length != Rows) throw new InvalidDataException("Expected eight Main Menu rows.");
        Motion = motion;
        foreach (var spin in motion.Profile.Spins)
        {
            var source = frame.Draws[spin.Draw];
            animatedDraws[spin.Draw] = f => SpinDraw(source, spin, f);
        }
        var strip = frame.Draws[motion.Profile.StripDraw];
        animatedDraws[motion.Profile.StripDraw] = f => StripDraw(strip, f);
    }

    /// <summary>Number of measured draws in the Main Menu background (other frames drawn over the menu start after them).</summary>
    public int BackgroundDrawCount => frame.Draws.Length;

    /// <summary>Asset keys the background samples, plus the bar panel atlas.</summary>
    public IEnumerable<string> TextureKeys =>
        frame.Draws.Select(d => d.AssetKey).OfType<string>().Append(PanelKey).Distinct(StringComparer.Ordinal);

    /// <summary>Background commands for a cleared transparent layer at <paramref name="gameFrame"/> of the loop and
    /// <paramref name="entryFrame"/> of the entry sequence. Another screen can supply its own measured <paramref name="title"/> quad
    /// (Options uses the same header).</summary>
    public IReadOnlyList<FlowCommand> Background(int gameFrame, int entryFrame = MainMenuEntry.ReadyFrame, FlowQuad? title = null)
    {
        var commands = new List<FlowCommand>(frame.Draws.Length);
        void Add(FlowDraw draw) { if (FlowDrawCompiler.Compile(draw) is { } command) commands.Add(command); }
        for (int i = 0; i < frame.Draws.Length; i++)
        {
            if (i == TintDraw) Add(TintDrawAt(frame.Draws[i], gameFrame));
            else if (animatedDraws.TryGetValue(i, out var animated)) Add(animated(gameFrame));
            else if (i is >= FirstHeaderDraw and <= LastHeaderDraw)
            {
                if (MainMenuEntry.HeaderAlpha(entryFrame) > 0) Add(HeaderDrawAt(frame.Draws[i], gameFrame, entryFrame));
            }
            else if (i == TitleDraw)
            {
                int alpha = MainMenuEntry.TitleAlpha(entryFrame);
                var draw = frame.Draws[i];
                if (alpha > 0 && title is { } q)
                    commands.Add(FlowDrawCompiler.Quad(draw.AssetKey!, q, 0, 0, draw.LinearSampling, (q.Color[0], q.Color[1], q.Color[2], alpha * q.Color[3] / 255)));
                else if (alpha > 0) Add(frame.Draws[i] with { Color = [frame.Draws[i].Color[0], frame.Draws[i].Color[1], frame.Draws[i].Color[2], alpha] });
            }
            else Add(frame.Draws[i]);
        }
        return commands;
    }

    /// <summary>Label-layer commands (<see cref="LabelWidth"/> × <see cref="LabelLayerHeight"/>): glyphs for both row states at the measured
    /// sub-pixel positions relative to their bar. Focused rows use black text with a light shadow; others white with a heavier one.</summary>
    public IReadOnlyList<FlowCommand> Labels(FlowTextSources text, PgfGlyphAtlas font, string fontKey)
    {
        var commands = new List<FlowCommand>();
        var menu = frame.Menu!;
        for (int row = 0; row < menu.Rows.Length; row++)
            for (int state = 0; state < 2; state++)
            {
                var (y, label) = (menu.Rows[row].Y, menu.Rows[row].Label);
                float slot = (row * 2 + state) * LabelHeight - y;
                float[] x = Array.ConvertAll(label.X, v => v - BarLeft);
                foreach (var (quad, color) in font.Line(text.Read(label), x[0], label.Y + slot, label.ScaleX, label.ScaleY,
                    state == 1 ? 0 : 0xffffff, state == 1 ? 0x40 : 0x80, x))
                    commands.Add(FlowDrawCompiler.Quad(fontKey, quad, 0, 0, linear: true, color));
            }
        return commands;
    }

    /// <summary>Focus pulse at a loop frame: the measured value written to the focus pieces' colour and alpha.</summary>
    public int Pulse(int gameFrame) => Motion.ScalarsAt(gameFrame).Pulse;

    private FlowDraw SpinDraw(FlowDraw source, MainMenuSpin spin, int at)
    {
        var corners = Motion.Corners(spin, at);
        var scalars = Motion.ScalarsAt(at);
        int alpha = spin.Draw switch
        {
            5 or 6 => scalars.BeamAlpha, >= 11 and <= 14 => scalars.RingAlpha, >= 15 and <= 18 => scalars.GrowAlpha,
            _ when Motion.Profile.FixedAlpha.TryGetValue(spin.Draw.ToString(CultureInfo.InvariantCulture), out int fixedAlpha) => fixedAlpha,
            _ => source.Color[3]
        };
        return source with { Points = Array.ConvertAll(corners, c => new[] { c.X, c.Y }), Color = [source.Color[0], source.Color[1], source.Color[2], alpha] };
    }

    private FlowDraw StripDraw(FlowDraw source, int at)
    {
        float offset = Motion.StripOffset(at);
        int alpha = Motion.Profile.FixedAlpha[Motion.Profile.StripDraw.ToString(CultureInfo.InvariantCulture)];
        return source with
        {
            Points = Array.ConvertAll(Motion.Profile.StripPoints, p => new[] { p[0], p[1] + offset }),
            Color = [source.Color[0], source.Color[1], source.Color[2], alpha]
        };
    }

    /// <summary>Draw 2's texture is measured as opaque black, so tex x colour + specular reduces to a solid animated tint.</summary>
    private FlowDraw TintDrawAt(FlowDraw source, int at)
    {
        var scalars = Motion.ScalarsAt(at);
        return source with { AssetKey = null, Add = null, Uv = [], Color = [scalars.TintRed, scalars.TintGreen, scalars.TintBlue, 255] };
    }

    /// <summary>Lit header strip: emissive + ambient, plus the same emissive as post-texture specular, cycling continuously.</summary>
    private FlowDraw HeaderDrawAt(FlowDraw source, int at, int entryFrame)
    {
        var scalars = Motion.ScalarsAt(at);
        int light = Math.Min(255, scalars.HeaderEmissive + scalars.HeaderAmbient), add = scalars.HeaderEmissive;
        float slide = MainMenuEntry.HeaderOffset(entryFrame);
        return source with
        {
            Color = [light, light, light, MainMenuEntry.HeaderAlpha(entryFrame)], Add = add > 0 ? [add, add, add] : null,
            Points = slide == 0 ? source.Points : Array.ConvertAll(source.Points, p => new[] { p[0], p[1] + slide })
        };
    }
}
