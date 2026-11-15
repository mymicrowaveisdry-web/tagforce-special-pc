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

    /// <summary>Bakes GE separate specular: texture x primary + specular, clamped, retaining texture alpha.</summary>
    internal Texture2D Specular(string key, int[] primary, int[] add)
    {
        string id = $"{key}|{primary[0]},{primary[1]},{primary[2]}|{add[0]},{add[1]},{add[2]}";
        if (baked.TryGetValue(id, out var existing)) return existing;
        var source = textures[key];
        var pixels = new Color[source.Width * source.Height];
        source.GetData(pixels);
        for (int i = 0; i < pixels.Length; i++)
        {
            var p = pixels[i];
            pixels[i] = new Color(Math.Min(255, p.R * primary[0] / 255 + add[0]), Math.Min(255, p.G * primary[1] / 255 + add[1]),
                Math.Min(255, p.B * primary[2] / 255 + add[2]), (int)p.A);
        }
        var texture = new Texture2D(device, source.Width, source.Height);
        texture.SetData(pixels);
        owned.Add(texture); baked.Add(id, texture);
        return texture;
    }

    private Texture2D DecodeEboot(FlowEbootTexture source)
    {
        if (source.Width is <= 0 or > 512 || source.Height is <= 0 or > 512 || source.Bpp is not (4 or 8) || source.Offset < 0 ||
            source.ClutOffset < 0 || source.Offset + source.Length > eboot.Length || source.ClutOffset + source.ClutBytes > eboot.Length)
            throw new InvalidDataException("Invalid embedded texture reference.");
        int pitch = source.Stride * source.Bpp / 8, rowsPerBlock = Math.Max(1, pitch / 16);
        var pixels = new Color[source.Width * source.Height];
        for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                int byteX = x * source.Bpp / 8;
                int at = source.Offset + y / 8 * rowsPerBlock * 128 + byteX / 16 * 128 + y % 8 * 16 + byteX % 16;
                if (at >= eboot.Length) throw new InvalidDataException("Embedded texture exceeds source.");
                int index = eboot[at];
                if (source.Bpp == 4) index = index >> 4 * (x % 2) & 15;
                int clut = source.ClutOffset + index * 4;
                if (clut + 4 > source.ClutOffset + source.ClutBytes) throw new InvalidDataException("Embedded palette index out of range.");
                pixels[y * source.Width + x] = new Color(eboot[clut], eboot[clut + 1], eboot[clut + 2], eboot[clut + 3]);
            }
        byte[] rgba = new byte[pixels.Length * 4];
        Buffer.BlockCopy(Array.ConvertAll(pixels, p => p.PackedValue), 0, rgba, 0, rgba.Length);
        if (Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(rgba)) != source.RgbaSha256)
            throw new InvalidDataException("Embedded texture pixels differ from the measured original: " + source.Key);
        var texture = new Texture2D(device, source.Width, source.Height);
        texture.SetData(pixels);
        owned.Add(texture);
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in owned) texture.Dispose();
        owned.Clear();
    }
}
