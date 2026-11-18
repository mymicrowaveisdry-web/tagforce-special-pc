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
    private static readonly RasterizerState Scissored = new() { CullMode = CullMode.None, ScissorTestEnable = true };
    public RenderTarget2D Layer { get; }
    public CardAlbumProfile Profile => profile;
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
        var card = session.PreviewCard is uint id ? catalog[id] : null;
        if (card is not null) visuals.DrawCardComposite(card);
        else if (session.PreviewsCardBack) visuals.DrawFlow(profile.CardBack.Draw);
        // Measured order: strip, text box and scroll bar, then the text glyphs over the box.
        var glint = profile.Glint;
        foreach (var draw in profile.Panel)
            visuals.DrawFlow(draw.AssetKey == glint.AssetKey ? Shift(draw, glint.Left(session.GlintFrame) - glint.MeasuredLeft, 0) : draw);
        if (card is not null)
        {
            visuals.DrawCardStats(card); DrawIcons(card); DrawLevel(card);
            // Measured: the info text is clipped to the text box (GE scissor on its draws).
            var s = profile.InfoScissor;
            device.ScissorRectangle = new Rectangle(s[0], s[1], s[2] - s[0] + 1, s[3] - s[1] + 1);
            device.RasterizerState = Scissored;
            DrawInfo(card);
            device.RasterizerState = RasterizerState.CullNone;
        }
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

    /// <summary>"[Type/Suffix]" for monsters, "[Spell/Kind]" or "[Trap/Kind]" (no kind for Normal) from cardinfo_jpn.ehp words; null when a
    /// code has no word.</summary>
    internal string? TypeLine(CardDefinition card)
    {
        var w = profile.TypeLine;
        if (card.Codes is not { } codes) return null;
        string Word(int offset) => visuals.Message(w.Source, offset);
        string body;
        if (codes.RawKind is 13 or 14)
        {
            body = Word(codes.RawKind == 13 ? w.Spell : w.Trap);
            if (codes.Icon is > 0 && codes.Icon < w.IconWords.Length) body += "/" + Word(w.IconWords[codes.Icon]);
        }
        else
        {
            if (codes.Type >= w.TypeWords.Length) return null;
            body = Word(w.TypeWords[codes.Type]);
            if (codes.RawKind != 0)
            {
                if (!w.KindSuffix.TryGetValue(codes.RawKind.ToString(CultureInfo.InvariantCulture), out int suffix)) return null;
                body += Word(suffix);
            }
        }
        return Word(w.Open) + body + Word(w.Close);
    }

    /// <summary>Attribute icon, then the monster type or Spell/Trap kind icon, from the Deck Edit icon tables (same icon01 cells).</summary>
    private void DrawIcons(CardDefinition card)
    {
        var (attribute, type) = IconCells(card);
        if (attribute is not null) visuals.DrawFlow(Quad(profile.AttributeIconRect, attribute));
        if (type is not null) visuals.DrawFlow(Quad(profile.TypeIconRect, type));
    }

    /// <summary>icon01 cells of the attribute and of the monster type or Spell/Trap kind (the album preview and the detail use the same).</summary>
    internal (float[]? Attribute, float[]? Type) IconCells(CardDefinition card)
    {
        if (card.Codes is not { } codes) return (null, null);
        var tables = visuals.DeckEditProfile.Tables;
        float[]? Cell(string table, string key) =>
            tables.TryGetValue(table, out var t) && t.TryGetValue(key, out var e) ? e.EnumerateArray().Select(v => v.GetSingle()).ToArray() : null;
        var attribute = Cell("attributeIcon", codes.Attribute.ToString(CultureInfo.InvariantCulture));
        var type = codes.RawKind is 13 or 14
            ? Cell("spellTrapIcon", $"{codes.RawKind}:{codes.Icon}")
            : Cell("typeIcon", codes.Type.ToString(CultureInfo.InvariantCulture))
              ?? (profile.TypeIconExtra.TryGetValue(codes.Type.ToString(CultureInfo.InvariantCulture), out var extra) ? extra : null);
        return (attribute, type);
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

    /// <summary>Monsters: the star and "x" with the level above the strip (inferred: Rank and Link use the same marker; "x" is the drawn glyph).</summary>
    private void DrawLevel(CardDefinition card)
    {
        if (card.Codes?.RawKind is 13 or 14 or null || card.Statistics.Level == 0) return;
        visuals.DrawFlow(Quad(profile.LevelStar.Rect, profile.LevelStar.Uv));
        var t = profile.LevelText;
        string text = "x" + card.Statistics.Level.ToString(CultureInfo.InvariantCulture);
        float[] x = text.Length == t.X.Length ? t.X : [.. t.X, .. Enumerable.Range(1, text.Length - t.X.Length).Select(i => t.X[^1] + i * (t.X[^1] - t.X[0]))];
        for (int i = 0; i < text.Length; i++) visuals.DrawText(text[i].ToString(), x[i], t.Y, t.ScaleX, t.ScaleY, t.Color, t.ShadowAlpha);
    }

    internal static FlowDraw Quad(float[] r, float[] uv) => new("deck.all.icon01", 3,
        [[r[0], r[1]], [r[2], r[1]], [r[0], r[3]], [r[0], r[3]], [r[2], r[1]], [r[2], r[3]]],
        [[uv[0], uv[1]], [uv[2], uv[1]], [uv[0], uv[3]], [uv[0], uv[3]], [uv[2], uv[1]], [uv[2], uv[3]]],
        [255, 255, 255, 255], null, null, false, false, false, false, false);

    /// <summary>Name lines (yellow), the bracketed type line (green), then card text.</summary>
    private void DrawInfo(CardDefinition card)
    {
        var t = profile.InfoText;
        var lines = Wrap(card.Name, t.Width, t.ScaleX).Select(l => (l, t.NameColor)).Append((TypeLine(card) ?? "", t.TypeColor))
            .Concat(Wrap(card.Description, t.Width, t.ScaleX).Select(l => (l, t.TextColor))).Take(t.Lines).ToList();
        for (int i = 0; i < lines.Count; i++)
            if (lines[i].Item1.Length > 0)
                visuals.DrawText(lines[i].Item1, t.X, t.FirstY + i * t.LineHeight, t.ScaleX, t.ScaleY, lines[i].Item2, t.ShadowAlpha);
    }

    /// <summary>Greedy wrap at the measured width; measured on the album: a line may also break after a hyphen ("face-" / "up").</summary>
    internal List<string> Wrap(string text, float width, float scaleX)
    {
        var lines = new List<string>();
        foreach (string paragraph in text.Split('\n'))
        {
            string line = "";
            foreach (string word in paragraph.Split(' '))
            {
                var parts = System.Text.RegularExpressions.Regex.Split(word, "(?<=-)(?=.)");
                for (int i = 0; i < parts.Length; i++)
                {
                    string glue = line.Length == 0 || i > 0 ? "" : " ";
                    string next = line + glue + parts[i];
                    if (line.Length > 0 && visuals.MeasureText(next, scaleX) > width) { lines.Add(line); line = parts[i]; }
                    else line = next;
                }
            }
            lines.Add(line);
        }
        return lines;
    }

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
