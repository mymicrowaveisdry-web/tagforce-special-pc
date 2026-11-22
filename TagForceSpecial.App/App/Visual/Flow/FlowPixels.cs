using System.Security.Cryptography;
using TagForceSpecial.PC.App.Screens.Title;

namespace TagForceSpecial.PC.App.Visual.Flow;

/// <summary>CPU pixel rules shared by every host: EBOOT texture decoding and baked GE specular, on straight-alpha RGBA bytes.</summary>
public static class FlowPixels
{
    /// <summary>Texture × primary + specular, clamped per channel; alpha is the texture's.</summary>
    public static byte[] Specular(ReadOnlySpan<byte> rgba, FlowSpecular s)
    {
        var output = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4)
        {
            output[i] = (byte)Math.Min(255, rgba[i] * s.R / 255 + s.AddR);
            output[i + 1] = (byte)Math.Min(255, rgba[i + 1] * s.G / 255 + s.AddG);
            output[i + 2] = (byte)Math.Min(255, rgba[i + 2] * s.B / 255 + s.AddB);
            output[i + 3] = rgba[i + 3];
        }
        return output;
    }

    /// <summary>A swizzled 4/8-bit palette texture from the stored EBOOT, checked against its measured RGBA hash.</summary>
    public static byte[] DecodeEboot(byte[] eboot, FlowEbootTexture source)
    {
        if (source.Width is <= 0 or > 512 || source.Height is <= 0 or > 512 || source.Bpp is not (4 or 8) || source.Offset < 0 ||
            source.ClutOffset < 0 || source.Offset + source.Length > eboot.Length || source.ClutOffset + source.ClutBytes > eboot.Length)
            throw new InvalidDataException("Invalid embedded texture reference.");
        int pitch = source.Stride * source.Bpp / 8, rowsPerBlock = Math.Max(1, pitch / 16);
        var rgba = new byte[source.Width * source.Height * 4];
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
                eboot.AsSpan(clut, 4).CopyTo(rgba.AsSpan((y * source.Width + x) * 4, 4));
            }
        if (Convert.ToHexStringLower(SHA256.HashData(rgba)) != source.RgbaSha256)
            throw new InvalidDataException("Embedded texture pixels differ from the measured original: " + source.Key);
        return rgba;
    }
}
