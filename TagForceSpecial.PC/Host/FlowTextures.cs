using Godot;
using TagForceSpecial.Assets.Presentation;
using TagForceSpecial.PC.App.Screens.Title;
using TagForceSpecial.PC.App.Visual;
using TagForceSpecial.PC.App.Visual.Flow;

namespace TagForceSpecial.PC.Host;

/// <summary>GPU copies of the straight-alpha textures flow layers sample: imported (verified only), decoded from the stored EBOOT, baked with
/// GE specular, or updated by the host (glyph atlases). One upload per pixel identity; owned for the life of the screen set.</summary>
internal sealed class FlowTextures(ScreenAssets assets, byte[] eboot)
{
    private readonly Dictionary<string, (byte[] Rgba, int Width, int Height)> sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ImageTexture> uploaded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> dynamicVersions = new(StringComparer.Ordinal);

    public int UploadedCount => uploaded.Count;

    public void AddEmbedded(FlowEbootTexture source)
    {
        if (!sources.ContainsKey(source.Key)) sources[source.Key] = (FlowPixels.DecodeEboot(eboot, source), source.Width, source.Height);
    }

    /// <param name="allowUnverified">Accept a texture that is only decoded, not pinned to an original reference (it stays labelled so).</param>
    public void AddImported(string key, bool allowUnverified = false)
    {
        if (sources.ContainsKey(key)) return;
        var texture = assets.Texture(key, allowUnverified);
        sources[key] = (texture.Rgba, texture.Width, texture.Height);
    }

    /// <summary>Host-updated pixels (a glyph atlas); re-uploaded in place when <paramref name="version"/> changes.</summary>
    public void SetDynamic(string key, byte[] rgba, int width, int height, int version)
    {
        if (dynamicVersions.TryGetValue(key, out int known) && known == version) return;
        dynamicVersions[key] = version;
        sources[key] = (rgba, width, height);
        var image = Image.CreateFromData(width, height, false, Image.Format.Rgba8, rgba);
        if (uploaded.TryGetValue(key, out var existing)) existing.Update(image);
        else uploaded[key] = ImageTexture.CreateFromImage(image);
    }

    /// <summary>Decoded card faces, served under <see cref="App.Visual.Ge.GeTextureKeys.Face"/> keys.</summary>
    public CardFaceSource? Faces { get; set; }

    /// <summary>Straight-alpha pixels for a GE layer (null when a card face cannot be decoded: that pocket is skipped).</summary>
    public (byte[] Rgba, int Width, int Height)? Pixels(FlowTexture texture)
    {
        (byte[] Rgba, int Width, int Height) source;
        if (App.Visual.Ge.GeTextureKeys.TryFace(texture.Key, out uint card))
        {
            if (Faces?.Decode(card) is not { } face) return null;
            source = (face, CardFaceSource.Width, CardFaceSource.Height);
        }
        else source = sources.GetValueOrDefault(texture.Key) is { Rgba: not null } known ? known
            : throw new InvalidDataException("Texture was not registered: " + texture.Key);
        return texture.Specular is { } specular ? (FlowPixels.Specular(source.Rgba, specular), source.Width, source.Height) : source;
    }

    public Texture2D Get(string key) => Get(new FlowTexture(key));

    public Texture2D Get(FlowTexture texture)
    {
        if (uploaded.TryGetValue(texture.Identity, out var known)) return known;
        var (rgba, width, height) = sources.GetValueOrDefault(texture.Key) is { Rgba: not null } source ? source
            : throw new InvalidDataException("Texture was not registered: " + texture.Key);
        byte[] pixels = texture.Specular is { } specular ? FlowPixels.Specular(rgba, specular) : rgba;
        var created = ImageTexture.CreateFromImage(Image.CreateFromData(width, height, false, Image.Format.Rgba8, pixels));
        uploaded[texture.Identity] = created;
        return created;
    }
}
