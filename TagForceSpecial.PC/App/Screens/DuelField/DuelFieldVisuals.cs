using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.App.Visual;

namespace TagForceSpecial.PC.App.Screens.DuelField;

/// <summary>Original-font text drawing into the currently bound target, and its advance width.</summary>
public interface IDuelFieldTextSink
{
    void Draw(string text, float x, float y, float scaleX, float scaleY, int color, int shadowAlpha);
    float Measure(string text, float scaleX);
}

/// <summary>Renders the measured Duel Field draws into one 480×272 layer with a depth buffer, from the validated original textures.</summary>
public sealed class DuelFieldVisuals : IDisposable
{
    private readonly GraphicsDevice device;
    private readonly OriginalScreenAssetResolver assets;
    private readonly PspDrawRenderer renderer;
    private readonly Dictionary<string, Texture2D> opaque = new(StringComparer.Ordinal);
    public DuelFieldProfile Profile { get; }
    public RenderTarget2D Layer { get; }

    public DuelFieldVisuals(GraphicsDevice device, OriginalScreenAssetResolver assets, DuelFieldProfile? profile = null)
    {
        this.device = device; this.assets = assets;
        Profile = profile ?? DuelFieldProfile.LoadEmbedded();
        // Every texture the profile names must resolve before the field is shown; a partial original field is never drawn.
        foreach (string key in Profile.Textures.Keys) _ = assets.Resolve(key);
        renderer = new(device);
        // Preserved: the shared card panel composes its card in another target mid-frame and comes back to this one.
        Layer = new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8, 0, RenderTargetUsage.PreserveContents);
    }

    /// <summary>Draws original-font text into the bound target (the title flow's PGF renderer); without it field text is skipped.</summary>
    public IDuelFieldTextSink? Text { get; set; }

    /// <summary>Textures the title flow decodes from the stored EBOOT and checks by pixel hash ("title.eboot.*" keys).</summary>
    public Func<string, Texture2D?>? External { get; set; }

    /// <summary>Draws the shared left card panel (card id or null for a back, strip tint, back drawer) into the bound target.</summary>
    public Action<uint?, Color, Action>? Panel { get; set; }

    /// <summary>Draws the Show Details page (the Card Album's detail renderer) into the bound target.</summary>
    public Action<App.Screens.Album.CardAlbumDetail>? Detail { get; set; }

    /// <summary>Renders <paramref name="draws"/> in order into <see cref="Layer"/>, restoring the previous render targets.</summary>
    public void Render(IEnumerable<DuelDraw> draws)
    {
        var targets = device.GetRenderTargets();
        var viewport = device.Viewport;
        try
        {
            device.SetRenderTarget(Layer);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1, 0);
            // Measured 2D draws include rotated sprites with reversed winding: never inherit the culling SpriteBatch leaves behind.
            device.RasterizerState = RasterizerState.CullNone; device.DepthStencilState = DepthStencilState.None;
            foreach (var draw in draws) RenderOne(draw);
        }
        finally
        {
            device.SetRenderTargets(targets); device.Viewport = viewport;
            device.BlendState = BlendState.AlphaBlend; device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone; device.SamplerStates[0] = SamplerState.PointClamp;
        }
    }

    private void RenderOne(DuelDraw draw)
    {
        switch (draw)
        {
            case DuelFieldDraw d: renderer.Draw(Profile.Piece(d.Piece)[d.Index], Profile, Texture, d.Placement); break;
            case DuelFieldSprites s:
                var template = Profile.Piece(s.Piece)[s.Index];
                float[] rgba = template.Rgba?[^1] ?? [1, 1, 1, 1];
                renderer.Draw(template with { Vertices = s.Vertices, Uv = s.Uv, Rgba = s.Vertices.Select(_ => rgba).ToArray() }, Profile, Texture);
                break;
            case DuelFieldText t when Text is { } sink:
                float x = t.AlignRight ? t.X - sink.Measure(t.Text, t.LayoutScaleX ?? t.ScaleX) : t.X;
                sink.Draw(t.Text, x, t.Y, t.ScaleX, t.ScaleY, t.Color, t.ShadowAlpha);
                break;
            case DuelFieldMenu m when Text is { } measure:
                foreach (var part in DuelMenuWindow.Expand(m, Profile, measure.Measure, Message)) RenderOne(part);
                break;
            case DuelChainPrompt c when Text is { } chain:
                foreach (var part in DuelChainPromptWindow.Expand(c, Profile, chain.Measure, Message)) RenderOne(part);
                break;
            case DuelCardDetail page when Detail is { } detail:
                detail(page.Detail);
                break;
            case DuelCardPanel p when Panel is { } panel:
                // A back is the hidden-inspector capture's rotated cil_0000 sprite, composited 1:1 at (5, 4).
                panel(p.CardId, p.Tint, () => RenderOne(new DuelFieldDraw(p.Part, "panel.back", 0, new(Offset: new(5, 4)))));
                break;
            case DuelRichText r when Text is { } runs:
                foreach (var part in DuelRichTextLayout.Expand(r, Message(r.Source, r.Offset), runs.Measure)) RenderOne(part);
                break;
        }
    }

    private readonly Dictionary<string, byte[]> sources = new(StringComparer.Ordinal);
    /// <summary>A zero-terminated UTF-16 string of duel/basic_jj.ehp at <paramref name="offset"/>.</summary>
    public string Message(int offset) => Message(DuelFieldResources.BasicKey, offset);

    /// <summary>A zero-terminated UTF-16 string of a stored archive (by asset key) at <paramref name="offset"/>.</summary>
    public string Message(string source, int offset)
    {
        if (!sources.TryGetValue(source, out var bytes)) sources[source] = bytes = assets.ResolveBytes(source);
        int end = offset;
        while (end + 1 < bytes.Length && (bytes[end] != 0 || bytes[end + 1] != 0)) end += 2;
        return System.Text.Encoding.Unicode.GetString(bytes, offset, end - offset);
    }

    private static readonly HashSet<string> PinnedFaces = DuelFieldReferenceResources.Cases.Select(c => c.AssetKey)
        .Where(k => k.StartsWith("duel.face.", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);
    private CardFaceSource? faces;
    private readonly Dictionary<uint, Texture2D> cardFaces = [];

    private Texture2D Texture(string key, bool withAlpha)
    {
        if (key.StartsWith("title.eboot.", StringComparison.Ordinal))
            return External?.Invoke(key) ?? throw new InvalidDataException("EBOOT texture unavailable: " + key);
        if (key.StartsWith(CardSpriteRenderer.FaceKeyPrefix, StringComparison.Ordinal))
            return CardFace(uint.Parse(key.AsSpan(CardSpriteRenderer.FaceKeyPrefix.Length), System.Globalization.CultureInfo.InvariantCulture));
        // Face icons seen in a capture are pinned; the others are decoded with the same decoder and stay labelled unverified.
        var source = (key.StartsWith("duel.face.", StringComparison.Ordinal) && !Profile.Textures.ContainsKey(key) && !PinnedFaces.Contains(key)
            ? assets.ResolveUnverified(key) : assets.Resolve(key)).Texture;
        if (withAlpha) return source;
        if (opaque.TryGetValue(key, out var copy)) return copy;
        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);
        for (int i = 0; i < pixels.Length; i++) pixels[i].A = 255;
        copy = new Texture2D(device, source.Width, source.Height);
        copy.SetData(pixels);
        opaque.Add(key, copy);
        return copy;
    }

    /// <summary>A public card's 64 × 64 cardm face (PSP DXT1, opaque), decoded once per card from the stored archive.</summary>
    private Texture2D CardFace(uint cardId)
    {
        if (cardFaces.TryGetValue(cardId, out var texture)) return texture;
        faces ??= new CardFaceSource(assets.ResolveBytes(CardAlbumResources.FacesKey));
        byte[] rgba = faces.Decode(cardId) ?? throw new InvalidDataException("The card face archive has no slot for card " + cardId + ".");
        for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;
        texture = new Texture2D(device, CardFaceSource.Width, CardFaceSource.Height);
        texture.SetData(rgba);
        cardFaces.Add(cardId, texture);
        return texture;
    }

    public void Dispose()
    {
        renderer.Dispose(); Layer.Dispose();
        foreach (var texture in opaque.Values) texture.Dispose();
        foreach (var texture in cardFaces.Values) texture.Dispose();
    }
}
