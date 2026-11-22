using System.Globalization;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.Cards;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Screens.Album;

/// <summary>Draws the Card Album into its own 480x272 layer in the original's order: background, the captured 3D binder (pockets drawn only
/// for owned cards, faces from the card face archive), header, bottom bar and counter, hand cursor, then the preview card and info panel.
/// 2D parts use the title-flow layer's measured-draw rules and PSP font.</summary>
public sealed class CardAlbumRenderer : IDisposable
{
    /// <summary>GE cull direction 0 with culling on, as MonoGame sees the same screen-space winding (checked against the captured views).</summary>
    public static CullMode GeCullDirection0 { get; set; } = CullMode.CullClockwiseFace;
    private readonly GraphicsDevice device;
    private readonly TitleFlowVisuals visuals;
    private readonly CardAlbumBinder binder;
    private readonly CardAlbumProfile profile;
    private readonly CardFaceSource faces;
    private readonly CardCatalog catalog;
    private readonly BasicEffect effect;
    private readonly Dictionary<uint, Texture2D> faceTextures = [];
    private readonly Dictionary<(bool, int, bool, bool), SamplerState> samplers = [];
    private readonly Dictionary<(bool, int, bool), DepthStencilState> depths = [];
    private static readonly BlendState Additive = new() { ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One, AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.One };
    private static readonly BlendState AlphaAdditive = new() { ColorSourceBlend = Blend.SourceAlpha, ColorDestinationBlend = Blend.One, AlphaSourceBlend = Blend.SourceAlpha, AlphaDestinationBlend = Blend.One };
    private readonly RasterizerState cullOn;
    public RenderTarget2D Layer { get; }
    public CardAlbumProfile Profile => profile;
    private readonly CardInfoPanel panel;
    /// <summary>The shared left card panel (also drawn by the Duel Field).</summary>
    public CardInfoPanel Panel => panel;
    internal string? TypeLine(CardDefinition card) => panel.TypeLine(card);
    internal (float[]? Attribute, float[]? Type) IconCells(CardDefinition card) => panel.IconCells(card);
    /// <summary>The card detail drawn instead of the album while open.</summary>
    public CardAlbumDetailRenderer? Detail { get; set; }
    private VertexPositionColorTexture[] blended = [];
    /// <summary>Things this renderer cannot draw from measured data yet; reported, never substituted.</summary>
    public IReadOnlyList<string> Gaps { get; } = Array.AsReadOnly(new[]
    {
        "fast-flip, mid-turn and Page Jump arrival pockets show faces at once (owner decision)",
        "card detail: characters outside the Latin fonts (for example the star in names and full-width dashes in pack names) are not drawn"
    });

    public CardAlbumRenderer(GraphicsDevice device, TitleFlowVisuals visuals, CardAlbumBinder binder, CardAlbumProfile profile, CardFaceSource faces, CardCatalog catalog)
    {
        this.device = device; this.visuals = visuals; this.binder = binder; this.profile = profile; this.faces = faces; this.catalog = catalog;
        effect = new(device) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false, World = Matrix.Identity };
        cullOn = new() { CullMode = GeCullDirection0 };
        panel = new(device, visuals, profile);
        Layer = new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
    }

    public void Render(CardAlbumSession session) => visuals.Within(Layer, () =>
    {
        device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1, 0);
        // Measured 2D draws include rotated sprites with reversed winding; never inherit culling left by other renderers (Gum's sprites).
        device.RasterizerState = RasterizerState.CullNone;
        device.DepthStencilState = DepthStencilState.None;
        if (session.Detail is { } open && Detail is not null) { Detail.Draw(open); return; }
        foreach (var draw in profile.Background) visuals.DrawFlow(draw);
        DrawBinder(session);
        foreach (var draw in profile.Header) visuals.DrawFlow(draw);
        visuals.DrawRun(profile.Title);
        visuals.DrawRun(profile.CompleteLabel);
        DrawPercent(session.Collection.CompletionText(session.Order));
        foreach (var draw in profile.Bar) visuals.DrawFlow(draw);
        DrawCounter(session.CounterSheet, session.Sheets);
        var offset = session.Focus switch
        {
            CardAlbumFocus.Bar => profile.HandBar[session.BarButton],
            CardAlbumFocus.Edge => profile.HandEdge,
            _ => profile.HandOffset(session.RightPage, session.Pocket)
        };
        if (!session.HandHidden) visuals.DrawFlow(Shift(profile.Hand, offset[0], offset[1]));
        panel.Draw(session.PreviewCard is uint id ? catalog[id] : null, session.PreviewsCardBack, session.GlintFrame);
        device.RasterizerState = RasterizerState.CullNone;
        if (session.JumpOpen) DrawPageJump(session, 1);
        else if (session.JumpFrame is 0) DrawPageJump(session, profile.PageJump.Close.FadeAlpha);
        else if (session.JumpFrame is int closing && closing <= profile.PageJump.Close.Panels.Length)
            foreach (var piece in profile.PageJump.Close.Panels[closing - 1]) visuals.DrawFlow(piece);
    });

    private void DrawBinder(CardAlbumSession session)
    {
        int count = session.Order.Count;
        CardAlbumFrame frame;
        CardAlbumFrame? next = null;
        float t = 0;
        int baseView;
        bool posed = true;
        var close = profile.PageJump.Close;
        if (session.Turn is { } turn && binder.TurnFrames(turn.Direction, turn.FromView, session.Views) is { Count: > 0 } keys)
        {
            // Captured poses every two game frames; odd frames blend the neighbouring poses (a linear blend predicts the poses captured at
            // the other phase to 0.02-0.16 px mean vertex error, against up to 0.77 px for holding a pose).
            float at = Math.Min(turn.Frames / 2f, keys.Count - 1);
            int key = (int)at;
            frame = keys[key];
            t = at - key;
            if (t > 0 && key + 1 < keys.Count && binder.Compatible(frame, keys[key + 1])) next = keys[key + 1];
            baseView = CardAlbumBinder.TurnBaseView(turn.Direction, turn.FromView);
        }
        else if (session.JumpFrame == close.TurnFrame && session.JumpTargetView != session.JumpFromView &&
                 binder.TurnFrames(session.JumpDirection, session.JumpTargetView - session.JumpDirection, session.Views) is { Count: > 0 } jumpKeys)
        {
            // Measured: the jump shows one forward-turn pose over the target's pages (inferred: a backward jump mirrors it with a backward turn).
            frame = jumpKeys[Math.Min(close.TurnKey, jumpKeys.Count - 1)];
            baseView = CardAlbumBinder.TurnBaseView(session.JumpDirection, session.JumpTargetView - session.JumpDirection);
        }
        else { frame = binder.Settled(session.View, session.Views); baseView = session.View; posed = false; }
        effect.View = next is null ? frame.View : Matrix.Lerp(frame.View, next.View, t);
        effect.Projection = next is null ? frame.Projection : Matrix.Lerp(frame.Projection, next.Projection, t);
        for (int i = 0; i < frame.Meshes.Length; i++)
            DrawMesh(binder.Meshes[frame.Meshes[i]], session, baseView, count, next is null ? null : binder.Meshes[next.Meshes[i]], t);
        if (!posed && session.Focus == CardAlbumFocus.Pocket && binder.Cursor(session.View, session.Views, session.RightPage, session.Pocket) is { } cursor)
            foreach (int id in cursor) DrawMesh(binder.Meshes[id], session, baseView, count);
        device.DepthStencilState = DepthStencilState.None;
        device.RasterizerState = RasterizerState.CullNone;
    }

    private void DrawMesh(CardAlbumMesh mesh, CardAlbumSession session, int baseView, int count, CardAlbumMesh? toward = null, float t = 0)
    {
        Texture2D? texture;
        if (mesh.Slot is int slot)
        {
            int position = baseView * CardAlbumLayout.PocketsPerSheet + slot;
            if (position < 0 || position >= count || !session.Collection.Owns(session.Order[position])) return;
            texture = mesh.Face ? Face(session.Order[position]) : visuals.Texture(CardAlbumResources.KeyFor(mesh.Member!));
            if (texture is null) return;
        }
        else texture = mesh.Member is { } member ? visuals.Texture(CardAlbumResources.KeyFor(member)) : null;
        var s = mesh.State;
        device.BlendState = !s.Blend ? BlendState.Opaque : (s.BlendSource, s.BlendDest) switch
        {
            (2, 3) => BlendState.NonPremultiplied, (10, 10) => Additive, (2, 10) => AlphaAdditive, _ => BlendState.NonPremultiplied
        };
        device.DepthStencilState = Depth(s.DepthTest, s.DepthFunc, s.DepthWrite);
        device.RasterizerState = s.Cull ? cullOn : RasterizerState.CullNone;
        device.SamplerStates[0] = Sampler(s.Linear, 0, s.ClampU, s.ClampV);
        effect.TextureEnabled = texture is not null;
        effect.Texture = texture;
        foreach (var pass in effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            device.DrawUserPrimitives(PrimitiveType.TriangleList, toward is null ? mesh.Vertices : BlendVertices(mesh.Vertices, toward.Vertices, t), 0, mesh.Vertices.Length / 3);
        }
    }

    private VertexPositionColorTexture[] BlendVertices(VertexPositionColorTexture[] a, VertexPositionColorTexture[] b, float t)
    {
        if (blended.Length < a.Length) blended = new VertexPositionColorTexture[a.Length];
        for (int i = 0; i < a.Length; i++)
            blended[i] = new(Vector3.Lerp(a[i].Position, b[i].Position, t), Color.Lerp(a[i].Color, b[i].Color, t),
                Vector2.Lerp(a[i].TextureCoordinate, b[i].TextureCoordinate, t));
        return blended;
    }

    private Texture2D? Face(uint cardId)
    {
        if (faceTextures.TryGetValue(cardId, out var existing)) return existing;
        if (faces.Decode(cardId) is not { } rgba) return null;
        var texture = new Texture2D(device, CardFaceSource.Width, CardFaceSource.Height);
        texture.SetData(rgba);
        faceTextures[cardId] = texture;
        return texture;
    }

    private DepthStencilState Depth(bool test, int func, bool write)
    {
        var key = (test, func, write);
        if (depths.TryGetValue(key, out var state)) return state;
        // GE GEQUAL (larger is nearer) becomes LessEqual after the projection's depth remap; EQUAL stays Equal.
        state = new DepthStencilState
        {
            DepthBufferEnable = test, DepthBufferWriteEnable = write,
            DepthBufferFunction = func switch { 7 => CompareFunction.LessEqual, 6 => CompareFunction.Less, 2 => CompareFunction.Equal, 1 => CompareFunction.Always, _ => CompareFunction.LessEqual }
        };
        depths[key] = state;
        return state;
    }

    private SamplerState Sampler(bool linear, int _, bool clampU, bool clampV)
    {
        var key = (linear, 0, clampU, clampV);
        if (samplers.TryGetValue(key, out var state)) return state;
        state = new SamplerState
        {
            Filter = linear ? TextureFilter.Linear : TextureFilter.Point,
            AddressU = clampU ? TextureAddressMode.Clamp : TextureAddressMode.Wrap,
            AddressV = clampV ? TextureAddressMode.Clamp : TextureAddressMode.Wrap
        };
        samplers[key] = state;
        return state;
    }

    /// <summary>Measured: completion characters right-aligned to the last digit cell, then the percent sign.</summary>
    private void DrawPercent(string value)
    {
        var p = profile.Percent;
        var cells = profile.PercentCells;
        foreach (var (ch, x) in PercentPositions(value, cells))
            visuals.DrawText(ch.ToString(), x, p.Y, p.ScaleX, p.ScaleY, p.Color, p.ShadowAlpha);
        visuals.DrawText("%", cells.SignX, p.Y, p.ScaleX, p.ScaleY, p.Color, p.ShadowAlpha);
    }

    /// <summary>Right to left from the last digit cell: digits at the cell pitch, the point and the digit before it at their measured advances.</summary>
    public static IEnumerable<(char Char, float X)> PercentPositions(string value, CardAlbumPercent cells)
    {
        var placed = new List<(char, float)>();
        float x = cells.LastDigitX;
        for (int i = value.Length - 1; i >= 0; i--)
        {
            if (i < value.Length - 1)
                x -= value[i] == '.' ? cells.PointAdvance : value[i + 1] == '.' ? cells.BeforePoint : cells.Pitch;
            placed.Add((value[i], x));
        }
        placed.Reverse();
        return placed;
    }

    /// <summary>Measured: the current sheet right-aligned in 9 px cells before the slash; the total in its recorded cells after it.</summary>
    private void DrawCounter(int sheet, int sheets)
    {
        var c = profile.Counter;
        string current = sheet.ToString(CultureInfo.InvariantCulture), total = sheets.ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < current.Length; i++)
            visuals.DrawText(current[i].ToString(), c.LastDigitX - (current.Length - 1 - i) * c.Pitch, c.Y, c.ScaleX, c.ScaleY, c.Color, c.ShadowAlpha);
        visuals.DrawText("/", c.SeparatorX, c.Y, c.ScaleX, c.ScaleY, c.Color, c.ShadowAlpha);
        for (int i = 0; i < total.Length && i < c.TotalX.Length; i++)
            visuals.DrawText(total[i].ToString(), c.TotalX[i], c.Y, c.ScaleX, c.ScaleY, c.Color, c.ShadowAlpha);
    }

    /// <summary>Page Jump overlay over everything else, as drawn last in the original frame. <paramref name="fade"/> scales the label,
    /// highlight and letters (measured: half on the first closing frame; the panel stays opaque).</summary>
    private void DrawPageJump(CardAlbumSession session, float fade)
    {
        var j = profile.PageJump;
        int Faded(int alpha) => (int)MathF.Round(alpha * fade);
        visuals.DrawFlow(j.Panel);
        if (fade >= 1) visuals.DrawRun(j.Label);
        else
        {
            var l = j.Label;
            string text = visuals.Message(l.Source, l.Offset).Substring(l.Start, l.Chars);
            for (int i = 0; i < text.Length && i < l.X.Length; i++)
                visuals.DrawText(text[i].ToString(), l.X[i], l.Y, l.ScaleX, l.ScaleY, l.Color, Faded(l.ShadowAlpha), Faded(255));
        }
        foreach (var piece in j.Highlight)
            visuals.DrawFlow(Shift(piece, session.JumpColumn * j.CellPitch, session.JumpRow * j.CellPitch) with { Color = [piece.Color[0], piece.Color[1], piece.Color[2], Faded(piece.Color[3])] });
        foreach (var row in j.Rows)
            foreach (var cell in row.Cells)
            {
                bool enabled = session.JumpEnabled(cell.Char);
                visuals.DrawText(cell.Char.ToString(), cell.X, row.Y, j.ScaleX, j.ScaleY, j.Color, Faded(enabled ? j.ShadowAlpha : j.DisabledShadowAlpha),
                    Faded(enabled ? 255 : j.DisabledAlpha));
            }
    }

    internal static FlowDraw Quad(float[] r, float[] uv) => new("deck.all.icon01", 3,
        [[r[0], r[1]], [r[2], r[1]], [r[0], r[3]], [r[0], r[3]], [r[2], r[1]], [r[2], r[3]]],
        [[uv[0], uv[1]], [uv[2], uv[1]], [uv[0], uv[3]], [uv[0], uv[3]], [uv[2], uv[1]], [uv[2], uv[3]]],
        [255, 255, 255, 255], null, null, false, false, false, false, false);

    internal static FlowDraw Shift(FlowDraw draw, float dx, float dy) =>
        draw with { Points = draw.Points.Select(p => new[] { p[0] + dx, p[1] + dy }).ToArray() };

    public void Dispose()
    {
        foreach (var t in faceTextures.Values) t.Dispose();
        foreach (var s in samplers.Values) s.Dispose();
        foreach (var d in depths.Values) d.Dispose();
        cullOn.Dispose(); effect.Dispose(); Layer.Dispose();
    }
}
