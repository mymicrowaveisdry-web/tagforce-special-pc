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
    public ResolvedScreenSprite Resolve(string key, SpriteSizing sizing = SpriteSizing.Native, string? stretchEvidenceId = null)
    {
        thread.Check(); ObjectDisposedException.ThrowIf(disposed, this);
        if (device is null || registry is null) throw new InvalidOperationException("No validated presentation generation is available.");
        string scope = registry.VerifiedReferenceScope(key) ?? throw new InvalidDataException("Reference approval pending: " + key);
        var asset = registry.Resolve(key);
        int width = asset.Manifest.Output.Width ?? throw new InvalidDataException("Asset is not a decoded texture.");
        int height = asset.Manifest.Output.Height ?? throw new InvalidDataException("Asset is not a decoded texture.");
        if (width <= 0 || height <= 0 || asset.Content.Length != checked(width * height * 4)) throw new InvalidDataException("Invalid RGBA texture.");
        var rect = asset.Manifest.Output.SourceRectangle;
        if (rect is null) throw new InvalidDataException("An explicit atlas rectangle is required.");
        var region = new SpriteRegion(new Rectangle(rect[0], rect[1], rect[2], rect[3]), sizing, stretchEvidenceId);
        region.Validate(width, height);
        string identity = asset.Manifest.Output.ContentSha256;
        if (!textures.TryGetValue(identity, out var texture))
        {
            texture = new(device, width, height);
            try { texture.SetData(asset.Content); textures.Add(identity, texture); }
            catch { texture.Dispose(); throw; }
        }
        return new(texture, region, scope);
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
