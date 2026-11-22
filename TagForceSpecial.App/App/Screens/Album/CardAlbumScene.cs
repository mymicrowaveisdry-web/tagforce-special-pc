using System.Globalization;
using System.Numerics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual.Flow;
using TagForceSpecial.PC.App.Visual.Ge;

namespace TagForceSpecial.PC.App.Screens.Album;

/// <summary>One page turn in progress: direction, the view it started from and game frames elapsed.</summary>
public sealed record CardAlbumTurn(int Direction, int FromView, int Frames);

/// <summary>The Card Album layer as host-neutral GE commands, in the original's order: background, the captured 3D binder (pockets drawn only
/// for owned cards, faces by card id) with the pocket highlight, header, title and completion, bottom bar and counter, and the hand. The left
/// preview panel and Page Jump are not composed here yet. Port of the MonoGame host's album renderer.</summary>
public sealed class CardAlbumScene(CardAlbumProfile profile, CardAlbumBinder binder, FlowTextSources text, PgfGlyphAtlas font, string fontKey)
{
    /// <summary>Texture key under which a host supplies a card's decoded face (a pocket whose face cannot be decoded is skipped).</summary>
    public static string FaceKey(uint cardId) => GeTextureKeys.Face(cardId);

    /// <summary>Imported textures the layer samples (2D parts and binder members).</summary>
    public IEnumerable<string> TextureKeys => profile.AssetKeys()
        .Concat(binder.Meshes.Select(m => m.Member).OfType<string>().Distinct(StringComparer.Ordinal).Select(CardAlbumResources.KeyFor))
        .Distinct(StringComparer.Ordinal);

    /// <summary>The layer at <paramref name="view"/> with the hand over a pocket (and its highlight when settled), or mid-turn.</summary>
    public IReadOnlyList<GeCommand> Compose(IReadOnlyList<uint> order, CardAlbumCollection collection, int view, bool rightPage, int pocket,
        CardAlbumTurn? turn = null, bool handHidden = false)
    {
        var commands = new List<GeCommand> { new GeClear(true, true, true, 0, 0, 0, 255, 1) };
        void Flow(FlowDraw draw) { if (FlowDrawCompiler.Compile(draw) is { } c && GeGeometry.FromFlow(c) is { } g) commands.Add(g); }
        void Batches(IEnumerable<FlowBatch> batches) { foreach (var b in batches) commands.Add(GeGeometry.FromFlow(b)!); }
        void Text(string value, float x, float y, CardAlbumGlyphs g) => Batches(FlowTextLayout.Line(font, fontKey, value, x, y, g.ScaleX, g.ScaleY, g.Color, g.ShadowAlpha));

        foreach (var draw in profile.Background) Flow(draw);
        commands.AddRange(Binder(order, collection, view, rightPage, pocket, turn));
        foreach (var draw in profile.Header) Flow(draw);
        Batches(FlowTextLayout.Run(font, fontKey, text.Read(profile.Title), profile.Title));
        Batches(FlowTextLayout.Run(font, fontKey, text.Read(profile.CompleteLabel), profile.CompleteLabel));
        var p = profile.Percent;
        foreach (var (ch, x) in PercentPositions(collection.CompletionText(order), profile.PercentCells)) Text(ch.ToString(), x, p.Y, p);
        Text("%", profile.PercentCells.SignX, p.Y, p);
        foreach (var draw in profile.Bar) Flow(draw);
        var c = profile.Counter;
        var counter = new CardAlbumGlyphs([], c.Y, c.ScaleX, c.ScaleY, c.Color, c.ShadowAlpha);
        string current = CardAlbumLayout.CounterSheet(view, rightPage).ToString(CultureInfo.InvariantCulture);
        string total = CardAlbumLayout.Sheets(order.Count).ToString(CultureInfo.InvariantCulture);
        for (int i = 0; i < current.Length; i++) Text(current[i].ToString(), c.LastDigitX - (current.Length - 1 - i) * c.Pitch, c.Y, counter);
        Text("/", c.SeparatorX, c.Y, counter);
        for (int i = 0; i < total.Length && i < c.TotalX.Length; i++) Text(total[i].ToString(), c.TotalX[i], c.Y, counter);
        if (!handHidden && turn is null)
        {
            var offset = profile.HandOffset(rightPage, pocket);
            Flow(Shift(profile.Hand, offset[0], offset[1]));
        }
        return commands;
    }

    private IEnumerable<GeCommand> Binder(IReadOnlyList<uint> order, CardAlbumCollection collection, int view, bool rightPage, int pocket, CardAlbumTurn? turn)
    {
        int views = CardAlbumLayout.Views(order.Count);
        CardAlbumFrame frame;
        CardAlbumFrame? next = null;
        float t = 0;
        int baseView;
        if (turn is { } active && binder.TurnFrames(active.Direction, active.FromView, views) is { Count: > 0 } keys)
        {
            // Captured poses every two game frames; odd frames blend the neighbouring poses.
            float at = Math.Min(active.Frames / 2f, keys.Count - 1);
            int key = (int)at;
            frame = keys[key];
            t = at - key;
            if (t > 0 && key + 1 < keys.Count && binder.Compatible(frame, keys[key + 1])) next = keys[key + 1];
            baseView = CardAlbumBinder.TurnBaseView(active.Direction, active.FromView);
        }
        else { frame = binder.Settled(view, views); baseView = view; }
        var viewMatrix = next is null ? frame.View : Matrix4x4.Lerp(frame.View, next.View, t);
        var projection = next is null ? frame.Projection : Matrix4x4.Lerp(frame.Projection, next.Projection, t);
        var transform = viewMatrix * projection;
        for (int i = 0; i < frame.Meshes.Length; i++)
            if (Mesh(binder.Meshes[frame.Meshes[i]], next is null ? null : binder.Meshes[next.Meshes[i]], t, transform, baseView, order, collection) is { } draw)
                yield return draw;
        if (turn is null && binder.Cursor(view, views, rightPage, pocket) is { } cursor)
            foreach (int id in cursor)
                if (Mesh(binder.Meshes[id], null, 0, transform, baseView, order, collection) is { } draw) yield return draw;
    }

    private static GeDraw? Mesh(CardAlbumMesh mesh, CardAlbumMesh? toward, float t, Matrix4x4 transform, int baseView, IReadOnlyList<uint> order,
        CardAlbumCollection collection)
    {
        FlowTexture? texture = null;
        if (mesh.Slot is int slot)
        {
            int position = baseView * CardAlbumLayout.PocketsPerSheet + slot;
            if (position < 0 || position >= order.Count || !collection.Owns(order[position])) return null;
            texture = new(mesh.Face ? FaceKey(order[position]) : CardAlbumResources.KeyFor(mesh.Member!));
        }
        else if (mesh.Member is { } member) texture = new(CardAlbumResources.KeyFor(member));
        var s = mesh.State;
        GeBlend? blend = !s.Blend ? null : (s.BlendSource, s.BlendDest) switch
        {
            (10, 10) => new GeBlend(GeFactor.One, GeFactor.One, GeFactor.One, GeFactor.One),
            (2, 10) => new GeBlend(GeFactor.SrcAlpha, GeFactor.One, GeFactor.SrcAlpha, GeFactor.One),
            _ => GeBlend.NonPremultiplied
        };
        // GE GEQUAL (larger is nearer) is LessEqual after the projection's depth remap; EQUAL stays Equal.
        var compare = s.DepthFunc switch { 6 => GeCompare.Less, 2 => GeCompare.Equal, 1 => GeCompare.Always, _ => GeCompare.LessEqual };
        var vertices = new GeVertex[mesh.Vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            var v = toward is null ? mesh.Vertices[i] : CardAlbumBinder.Lerp(mesh.Vertices[i], toward.Vertices[i], t);
            var clip = GeGeometry.Transform(v.Position, transform);
            vertices[i] = new(clip.X, clip.Y, clip.Z, clip.W, v.R, v.G, v.B, v.A, v.Uv.X, v.Uv.Y);
        }
        // Measured: GE cull direction 0 with culling on matches MonoGame's CullClockwiseFace on the captured views.
        if (s.Cull) vertices = GeGeometry.CullClockwise(vertices);
        return vertices.Length == 0 ? null : new GeDraw(texture, new GeState(blend, s.DepthTest, compare, s.DepthWrite, s.Linear, s.ClampU, s.ClampV), vertices);
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

    public static FlowDraw Shift(FlowDraw draw, float dx, float dy) =>
        draw with { Points = draw.Points.Select(p => new[] { p[0] + dx, p[1] + dy }).ToArray() };
}
