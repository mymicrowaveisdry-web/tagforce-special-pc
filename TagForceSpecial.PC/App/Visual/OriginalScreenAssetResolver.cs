using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.Rendering;

namespace TagForceSpecial.PC.App.Visual;

public sealed record ResolvedScreenSprite(Texture2D Texture, SpriteRegion Region, string ReferenceScope);

/// <summary>Owns GPU textures for one validated cache generation; no public/source path fallback.</summary>
public sealed class OriginalScreenAssetResolver : IDisposable
{
    private readonly GraphicsDevice? device;
    private readonly SemanticAssetRegistry? registry;
    private readonly GameThreadGuard thread = new();
    private readonly Dictionary<string, Texture2D> textures = new(StringComparer.Ordinal);
    private bool disposed;
    public OriginalScreenAssetResolver(GraphicsDevice? device = null, SemanticAssetRegistry? registry = null)
    { this.device = device; this.registry = registry; }
    public int TextureCount => textures.Count;
    public ResolvedScreenSprite Resolve(string key, SpriteSizing sizing = SpriteSizing.Native, string? stretchEvidenceId = null) =>
        Resolve(key, sizing, stretchEvidenceId, allowUnverified: false);

    /// <summary>Decoded resources that have no original-reference pin yet (card thumbnails, Deck Edit supplements). The returned scope says
    /// so; callers must keep such resources labelled and never present them as verified.</summary>
    public ResolvedScreenSprite ResolveUnverified(string key) => Resolve(key, SpriteSizing.Native, null, allowUnverified: true);
    public const string UnverifiedScope = ScreenAssets.UnverifiedScope;

    private ResolvedScreenSprite Resolve(string key, SpriteSizing sizing, string? stretchEvidenceId, bool allowUnverified)
    {
        thread.Check(); ObjectDisposedException.ThrowIf(disposed, this);
        if (device is null || registry is null) throw new InvalidOperationException("No validated presentation generation is available.");
        var asset = new ScreenAssets(registry).Texture(key, allowUnverified);
        var rect = asset.SourceRectangle;
        var region = new SpriteRegion(new Rectangle(rect[0], rect[1], rect[2], rect[3]), sizing, stretchEvidenceId);
        region.Validate(asset.Width, asset.Height);
        if (!textures.TryGetValue(asset.ContentSha256, out var texture))
        {
            texture = new(device, asset.Width, asset.Height);
            try { texture.SetData(asset.Rgba); textures.Add(asset.ContentSha256, texture); }
            catch { texture.Dispose(); throw; }
        }
        return new(texture, region, asset.Scope);
    }
    /// <summary>Exact stored source bytes from the validated generation (pinned by manifest hash, no pixel claim).</summary>
    public byte[] ResolveBytes(string key)
    {
        thread.Check(); ObjectDisposedException.ThrowIf(disposed, this);
        if (registry is null) throw new InvalidOperationException("No validated presentation generation is available.");
        return registry.Resolve(key).Content;
    }
    public void Dispose()
    {
        thread.Check(); if (disposed) return; disposed = true;
        foreach (var texture in textures.Values) texture.Dispose();
        textures.Clear();
    }
}
