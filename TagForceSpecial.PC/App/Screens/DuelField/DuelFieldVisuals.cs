using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.App.Visual;

namespace TagForceSpecial.PC.App.Screens.DuelField;

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
        Layer = new(device, 480, 272, false, SurfaceFormat.Color, DepthFormat.Depth24Stencil8);
    }

    /// <summary>Renders <paramref name="draws"/> in order into <see cref="Layer"/>, restoring the previous render targets.</summary>
    public void Render(IEnumerable<DuelFieldDraw> draws)
    {
        var targets = device.GetRenderTargets();
        var viewport = device.Viewport;
        try
        {
            device.SetRenderTarget(Layer);
            device.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1, 0);
            foreach (var draw in draws) renderer.Draw(Profile.Piece(draw.Piece)[draw.Index], Profile, Texture, draw.Placement);
        }
        finally
        {
            device.SetRenderTargets(targets); device.Viewport = viewport;
            device.BlendState = BlendState.AlphaBlend; device.DepthStencilState = DepthStencilState.None;
            device.RasterizerState = RasterizerState.CullNone; device.SamplerStates[0] = SamplerState.PointClamp;
        }
    }

    private CardFaceSource? faces;
    private readonly Dictionary<uint, Texture2D> cardFaces = [];

    private Texture2D Texture(string key, bool withAlpha)
    {
        if (key.StartsWith(CardSpriteRenderer.FaceKeyPrefix, StringComparison.Ordinal))
            return CardFace(uint.Parse(key.AsSpan(CardSpriteRenderer.FaceKeyPrefix.Length), System.Globalization.CultureInfo.InvariantCulture));
        var source = assets.Resolve(key).Texture;
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
