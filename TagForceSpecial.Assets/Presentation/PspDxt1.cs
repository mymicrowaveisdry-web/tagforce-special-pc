using System.Buffers.Binary;

namespace TagForceSpecial.Assets.Presentation;

/// <summary>PSP-order DXT1: each 8-byte block holds four index bytes, then two RGB565 colours with red in the high bits.</summary>
public static class PspDxt1
{
    /// <summary>Decodes <paramref name="width"/> x <paramref name="height"/> pixels from blocks laid out <paramref name="pitch"/> bytes per
    /// block row into RGBA.</summary>
    public static void Decode(ReadOnlySpan<byte> pixels, int width, int height, int pitch, Span<byte> rgba)
    {
        Span<byte> colors = stackalloc byte[16];
        for (int by = 0; by < (height + 3) / 4; by++)
            for (int bx = 0; bx < (width + 3) / 4; bx++)
            {
                ReadOnlySpan<byte> block = pixels.Slice(by * pitch + bx * 8, 8);
                uint indices = BinaryPrimitives.ReadUInt32LittleEndian(block);
                int a = BinaryPrimitives.ReadUInt16LittleEndian(block[4..]), b = BinaryPrimitives.ReadUInt16LittleEndian(block[6..]);
                for (int c = 0; c < 2; c++)
                {
                    int word = c == 0 ? a : b;
                    // The DXT path uses left-shifted RGB565 components, unlike direct-color textures.
                    colors[c * 4] = (byte)(((word >> 11) & 31) * 8);
                    colors[c * 4 + 1] = (byte)(((word >> 5) & 63) * 4);
                    colors[c * 4 + 2] = (byte)((word & 31) * 8); colors[c * 4 + 3] = 255;
                }
                for (int c = 0; c < 3; c++)
                {
                    colors[8 + c] = (byte)(a > b ? (2 * colors[c] + colors[4 + c]) / 3 : (colors[c] + colors[4 + c]) / 2);
                    colors[12 + c] = (byte)(a > b ? (colors[c] + 2 * colors[4 + c]) / 3 : 0);
                }
                colors[11] = 255; colors[15] = (byte)(a > b ? 255 : 0);
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int dx = bx * 4 + x, dy = by * 4 + y;
                        if (dx < width && dy < height)
                            colors.Slice((int)((indices >> (2 * (y * 4 + x))) & 3) * 4, 4).CopyTo(rgba.Slice((dy * width + dx) * 4, 4));
                    }
            }
    }
}
