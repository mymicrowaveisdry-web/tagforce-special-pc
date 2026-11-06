using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Main Menu layers bound to the Gum screen: an animated background and pre-rendered label slots.</summary>
public sealed partial class TitleFlowVisuals
{
    private const string MainMenuFrame = "main-menu", EndGameFrame = "main-menu.quit";
    private const int LabelWidth = 253, LabelHeight = 24, TintDraw = 2, BarLeft = 114, FirstHeaderDraw = 21, LastHeaderDraw = 36, TitleDraw = 37;
    private readonly Dictionary<int, Func<int, FlowDraw>> animatedDraws = [];

    /// <summary>Atlas rectangles of the bar pieces, measured from GE dumps; they mirror the OriginalMenuBar Gum component.</summary>
    public static IReadOnlyList<(string Piece, Rectangle Source)> MenuBarPieces { get; } =
    [
        ("BaseLeft", new(20, 0, 6, 24)), ("BaseCenter", new(26, 0, 1, 24)), ("BaseRight", new(37, 0, 3, 24)),
        ("FocusLeft", new(0, 0, 6, 24)), ("FocusCenter", new(6, 0, 1, 24)), ("FocusRight", new(17, 0, 3, 24))
    ];
    public static Rectangle MenuLabelSource(int row, bool focused) => new(0, (row * 2 + (focused ? 1 : 0)) * LabelHeight, LabelWidth, LabelHeight);

    public MainMenuAnimation MainMenuMotion { get; } = MainMenuAnimation.LoadEmbedded();
    public Texture2D MenuPanel => textures["title.flow.m-menu-panel01"];
    public RenderTarget2D MainMenuBackground { get; private set; } = null!;
    public RenderTarget2D MainMenuLabels { get; private set; } = null!;

    private void InitializeMainMenu()
    {
        MainMenuBackground = new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
        MainMenuLabels = new(device, LabelWidth, LabelHeight * 2 * TitleFlow.MainMenuRows, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        var frame = frames[MainMenuFrame];
        foreach (var spin in MainMenuMotion.Profile.Spins)
        {
            var source = frame.Draws[spin.Draw];
            animatedDraws[spin.Draw] = f => SpinDraw(source, spin, f);
        }
        var strip = frame.Draws[MainMenuMotion.Profile.StripDraw];
        animatedDraws[MainMenuMotion.Profile.StripDraw] = f => StripDraw(strip, f);
    }

    private FlowDraw SpinDraw(FlowDraw source, MainMenuSpin spin, int frame)
    {
        var corners = MainMenuMotion.Corners(spin, frame);
        var scalars = MainMenuMotion.ScalarsAt(frame);
        int alpha = spin.Draw switch
        {
            5 or 6 => scalars.BeamAlpha, >= 11 and <= 14 => scalars.RingAlpha, >= 15 and <= 18 => scalars.GrowAlpha,
            _ when MainMenuMotion.Profile.FixedAlpha.TryGetValue(spin.Draw.ToString(System.Globalization.CultureInfo.InvariantCulture), out int fixedAlpha) => fixedAlpha,
            _ => source.Color[3]
        };
        return source with { Points = Array.ConvertAll(corners, c => new[] { c.X, c.Y }), Color = [source.Color[0], source.Color[1], source.Color[2], alpha] };
    }

    private FlowDraw StripDraw(FlowDraw source, int frame)
    {
        float offset = MainMenuMotion.StripOffset(frame);
        int alpha = MainMenuMotion.Profile.FixedAlpha[MainMenuMotion.Profile.StripDraw.ToString(System.Globalization.CultureInfo.InvariantCulture)];
        return source with
        {
            Points = Array.ConvertAll(MainMenuMotion.Profile.StripPoints, p => new[] { p[0], p[1] + offset }),
            Color = [source.Color[0], source.Color[1], source.Color[2], alpha]
        };
    }

    /// <summary>Draw 2's texture is measured as opaque black, so tex x colour + specular reduces to a solid animated tint.</summary>
    private FlowDraw TintDrawAt(FlowDraw source, int frame)
    {
        var scalars = MainMenuMotion.ScalarsAt(frame);
        return source with { AssetKey = null, Add = null, Uv = [], Color = [scalars.TintRed, scalars.TintGreen, scalars.TintBlue, 255] };
    }

    /// <summary>Lit header strip: emissive + ambient, plus the same emissive as post-texture specular, cycling continuously.</summary>
    private FlowDraw HeaderDrawAt(FlowDraw source, int frame, int entryFrame)
    {
        var scalars = MainMenuMotion.ScalarsAt(frame);
        int light = Math.Min(255, scalars.HeaderEmissive + scalars.HeaderAmbient), add = scalars.HeaderEmissive;
        float slide = MainMenuEntry.HeaderOffset(entryFrame);
        return source with
        {
            Color = [light, light, light, MainMenuEntry.HeaderAlpha(entryFrame)], Add = add > 0 ? [add, add, add] : null,
            Points = slide == 0 ? source.Points : Array.ConvertAll(source.Points, p => new[] { p[0], p[1] + slide })
        };
    }

    public void RenderMainMenuBackground(int gameFrame, int entryFrame = MainMenuEntry.ReadyFrame) => WithTarget(MainMenuBackground, () =>
    {
        device.Clear(ClearOptions.Target | ClearOptions.Stencil, Color.Transparent, 1, 0);
        device.RasterizerState = RasterizerState.CullNone;
        DrawMenuBackgroundLayers(gameFrame, entryFrame);
    });

    /// <summary>The menu background; another screen can supply its own measured title quad (Options uses the same header).</summary>
    private void DrawMenuBackgroundLayers(int gameFrame, int entryFrame, FlowQuad? title = null)
    {
        var frame = frames[MainMenuFrame];
        for (int i = 0; i < frame.Draws.Length; i++)
        {
            if (i == TintDraw) DrawMeasured(TintDrawAt(frame.Draws[i], gameFrame));
            else if (animatedDraws.TryGetValue(i, out var animated)) DrawMeasured(animated(gameFrame));
            else if (i is >= FirstHeaderDraw and <= LastHeaderDraw)
            {
                if (MainMenuEntry.HeaderAlpha(entryFrame) > 0) DrawMeasured(HeaderDrawAt(frame.Draws[i], gameFrame, entryFrame));
            }
            else if (i == TitleDraw)
            {
                int alpha = MainMenuEntry.TitleAlpha(entryFrame);
                if (alpha <= 0) continue;
                if (title is { } q) DrawQuad(textures[frame.Draws[i].AssetKey!], q, 0, 0, frame.Draws[i].LinearSampling, new Color(q.Color[0], q.Color[1], q.Color[2], alpha * q.Color[3] / 255));
                else DrawMeasured(frame.Draws[i] with { Color = [frame.Draws[i].Color[0], frame.Draws[i].Color[1], frame.Draws[i].Color[2], alpha] });
            }
            else DrawMeasured(frame.Draws[i]);
        }
    }

    /// <summary>Measured draw order for the end-game question: background, bars, their labels, then the dialog over them.
    /// With a game frame the background keeps animating; without one it stays on the dump's own instant for exact comparison.</summary>
    private void DrawEndGameBehindDialog(FlowFrame frame, int? menuBackgroundFrame)
    {
        int background = frames[MainMenuFrame].Draws.Length;
        if (menuBackgroundFrame is { } gameFrame) DrawMenuBackgroundLayers(gameFrame, MainMenuEntry.ReadyFrame);
        else for (int i = 0; i < background; i++) DrawMeasured(frame.Draws[i]);
        int dialog = Array.FindIndex(frame.Draws, background, d => d.Points.Length > 0 && d.Points.Min(p => p[0]) < BarLeft / 2f);
        if (dialog < 0) throw new InvalidDataException("End-game frame has no dialog panel.");
        for (int i = background; i < dialog; i++) DrawMeasured(frame.Draws[i]);
        foreach (var label in frame.Text.Where(IsMenuLabel))
            DrawMeasuredText(Source(label.Source, label.Offset, label.Start, label.Chars), label);
        for (int i = dialog; i < frame.Draws.Length; i++) DrawMeasured(frame.Draws[i]);
    }

    /// <summary>Menu rows start inside the bars; the dialog's own lines start at its left margin.</summary>
    private static bool IsMenuLabel(FlowText text) => text.X[0] > BarLeft;

    /// <summary>Glyphs for both row states, drawn once at the measured sub-pixel positions relative to their bar.</summary>
    public void RenderMainMenuLabels()
    {
        var menu = frames[MainMenuFrame].Menu ?? throw new InvalidDataException("Main Menu frame has no rows.");
        if (menu.Rows.Length != TitleFlow.MainMenuRows) throw new InvalidDataException("Expected eight Main Menu rows.");
        var projection = effect.Projection;
        WithTarget(MainMenuLabels, () =>
        {
            device.Clear(Color.Transparent);
            effect.Projection = Matrix.CreateOrthographicOffCenter(0, LabelWidth, MainMenuLabels.Height, 0, 0, 1);
            for (int row = 0; row < menu.Rows.Length; row++)
                for (int state = 0; state < 2; state++)
                {
                    var (y, label) = (menu.Rows[row].Y, menu.Rows[row].Label);
                    float slot = (row * 2 + state) * LabelHeight - y;
                    float[] x = Array.ConvertAll(label.X, v => v - BarLeft);
                    // Measured: focused rows use black text with a light shadow; others white with a heavier one.
                    DrawLine(Source(label.Source, label.Offset, label.Start, label.Chars), x[0], label.Y + slot, label.ScaleX, label.ScaleY,
                        state == 1 ? 0 : 0xffffff, state == 1 ? 0x40 : 0x80, x);
                }
        });
        effect.Projection = projection;
    }

    /// <summary>Authored text when no PSP font was imported; never presented as original typography.</summary>
    public void SetMainMenuFallback(int focus)
    {
        FallbackLines.Clear();
        if (OriginalFontAvailable) return;
        var menu = frames[MainMenuFrame].Menu!;
        for (int row = 0; row < menu.Rows.Length; row++)
        {
            var label = menu.Rows[row].Label;
            FallbackLines.Add((Source(label.Source, label.Offset, label.Start, label.Chars).ToUpperInvariant(), (int)label.X[0], (int)label.Y + 3, row == focus ? Color.Black : Color.White));
        }
    }

    private void WithTarget(RenderTarget2D target, Action draw)
    {
        var targets = device.GetRenderTargets();
        var viewport = device.Viewport;
        try { device.SetRenderTarget(target); draw(); }
        finally
        {
            device.SetRenderTargets(targets); device.Viewport = viewport;
            device.BlendState = BlendState.AlphaBlend; device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone;
        }
    }
}
