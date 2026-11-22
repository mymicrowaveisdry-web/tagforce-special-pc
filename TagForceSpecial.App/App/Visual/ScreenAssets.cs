using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.PC.App.Visual;

/// <summary>A decoded straight-alpha texture from the validated import, with its atlas rectangle and the reference scope that approved it.</summary>
public sealed record ScreenTexture(string Key, int Width, int Height, byte[] Rgba, int[] SourceRectangle, string ContentSha256, string Scope);

/// <summary>Reads one validated presentation generation for any host; no public or source-path fallback.</summary>
public sealed class ScreenAssets(SemanticAssetRegistry registry)
{
    public const string UnverifiedScope = "decoded-unverified";

    /// <summary>A texture pinned to an original reference, or, with <paramref name="allowUnverified"/>, one that is only decoded
    /// (the returned scope says so; such resources stay labelled and are never presented as verified).</summary>
    public ScreenTexture Texture(string key, bool allowUnverified = false)
    {
        string scope = registry.VerifiedReferenceScope(key) ?? (allowUnverified ? UnverifiedScope : throw new InvalidDataException("Reference approval pending: " + key));
        var asset = registry.Resolve(key);
        int width = asset.Manifest.Output.Width ?? throw new InvalidDataException("Asset is not a decoded texture.");
        int height = asset.Manifest.Output.Height ?? throw new InvalidDataException("Asset is not a decoded texture.");
        if (width <= 0 || height <= 0 || asset.Content.Length != checked(width * height * 4)) throw new InvalidDataException("Invalid RGBA texture.");
        var rect = asset.Manifest.Output.SourceRectangle ?? throw new InvalidDataException("An explicit atlas rectangle is required.");
        return new(key, width, height, asset.Content, [.. rect], asset.Manifest.Output.ContentSha256, scope);
    }

    /// <summary>Exact stored source bytes from the validated generation (pinned by manifest hash, no pixel claim).</summary>
    public byte[] Bytes(string key) => registry.Resolve(key).Content;
}
