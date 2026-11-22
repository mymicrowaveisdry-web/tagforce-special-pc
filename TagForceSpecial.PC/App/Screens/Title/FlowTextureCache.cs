using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TagForceSpecial.PC.App.Screens.Title;

/// <summary>Owns decoded and baked textures while borrowing resolver textures without disposing them.</summary>
internal sealed class FlowTextureCache(GraphicsDevice device, byte[] eboot) : IDisposable
{
    private readonly Dictionary<string, Texture2D> textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> baked = new(StringComparer.Ordinal);
    private readonly List<Texture2D> owned = [];
    internal Texture2D this[string key] => textures[key];
    internal bool ContainsKey(string key) => textures.ContainsKey(key);
    internal void AddEmbedded(FlowEbootTexture source) => textures.Add(source.Key, DecodeEboot(source));
    internal void AddBorrowed(string key, Texture2D texture) => textures.TryAdd(key, texture);
    internal bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out Texture2D texture) => textures.TryGetValue(key, out texture);

    /// <summary>A texture this cache created and disposes, such as an imported recipe thumbnail.</summary>
    internal void AddOwned(string key, Texture2D texture)
    {
        textures.Add(key, texture);
        owned.Add(texture);
    }

    /// <summary>Points <paramref name="key"/> at the texture already stored under <paramref name="source"/>, replacing any earlier entry.</summary>
    internal void Alias(string key, string source) => textures[key] = textures[source];

    /// <summary>Bakes GE separate specular: texture x primary + specular, clamped, retaining texture alpha.</summary>
    internal Texture2D Specular(string key, int[] primary, int[] add)
    {
        string id = $"{key}|{primary[0]},{primary[1]},{primary[2]}|{add[0]},{add[1]},{add[2]}";
        if (baked.TryGetValue(id, out var existing)) return existing;
        var source = textures[key];
        var pixels = new byte[source.Width * source.Height * 4];
        source.GetData(pixels);
        var texture = new Texture2D(device, source.Width, source.Height);
        texture.SetData(Visual.Flow.FlowPixels.Specular(pixels, new(primary[0], primary[1], primary[2], add[0], add[1], add[2])));
        owned.Add(texture); baked.Add(id, texture);
        return texture;
    }

    private Texture2D DecodeEboot(FlowEbootTexture source)
    {
        var texture = new Texture2D(device, source.Width, source.Height);
        texture.SetData(Visual.Flow.FlowPixels.DecodeEboot(eboot, source));
        owned.Add(texture);
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in owned) texture.Dispose();
        owned.Clear();
    }
}
