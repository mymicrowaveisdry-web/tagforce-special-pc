using System.Buffers.Binary;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.Tests;

/// <summary>Multi-frame GIM decoding (deck/all_j.ehp cis_all.gim) and cardh_j.cip thumbnails with their measured zero tail.</summary>
public class GimFrameAndThumbnailTests
{
    private static void Put16(byte[] b, int at, int value) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), (ushort)value);
    private static void Put32(byte[] b, int at, int value) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at), value);

    private static byte[] Block(int type, byte[] payload, bool container = false)
    {
        byte[] result = new byte[16 + payload.Length];
        Put16(result, 0, type); Put32(result, 4, result.Length);
        Put32(result, 8, container ? 16 : result.Length); Put32(result, 12, 16);
        payload.CopyTo(result, 16); return result;
    }

    /// <summary>A plane whose offset table lists one entry per frame, each frame stored back to back.</summary>
    private static byte[] FramePlane(int type, int format, int width, int height, byte[][] frames)
    {
        int start = (48 + 4 * frames.Length + 15) & ~15, size = frames[0].Length;
        byte[] header = new byte[start + size * frames.Length];
        Put16(header, 0, 48); Put16(header, 4, format); Put16(header, 8, width); Put16(header, 10, height);
        Put16(header, 12, format switch { 3 => 32, 5 => 8, _ => 16 }); Put16(header, 14, 1); Put16(header, 16, 1);
        Put32(header, 24, 48); Put32(header, 28, start); Put32(header, 32, header.Length);
        Put16(header, 40, type == 5 ? 2 : 1); Put16(header, 42, 1); Put16(header, 44, 3); Put16(header, 46, frames.Length);
        for (int i = 0; i < frames.Length; i++)
        {
            Put32(header, 48 + 4 * i, start + i * size);
            frames[i].CopyTo(header, start + i * size);
        }
        return Block(type, header);
    }

    private static byte[] File(params byte[][] planes)
    {
        byte[] root = Block(2, Block(3, planes.SelectMany(b => b).ToArray(), container: true), container: true);
        byte[] result = new byte[16 + root.Length];
        "MIG.00.1PSP\0\0\0\0\0"u8.CopyTo(result); root.CopyTo(result, 16); return result;
    }

    private static byte[] TwoFrameSheet(int paletteFrames) => File(
        FramePlane(5, 3, 1, 1, Enumerable.Range(0, paletteFrames).Select(i => new byte[] { (byte)(10 * i + 1), (byte)(10 * i + 2), (byte)(10 * i + 3), 255 }).ToArray()),
        FramePlane(4, 5, 1, 1, [[0], [0]]));

    [Fact]
    public void EachImageFrameUsesThePaletteFrameOfTheSameIndex()
    {
        var frames = new GimTextureDecoder().DecodeAll(TwoFrameSheet(2));
        Assert.Equal(new[] { 0, 1 }, frames.Select(f => f.Index));
        Assert.Equal(new byte[] { 1, 2, 3, 255 }, frames[0].Rgba);
        Assert.Equal(new byte[] { 11, 12, 13, 255 }, frames[1].Rgba);
        Assert.Equal(2, new GimTextureDecoder().DecodeFrame(TwoFrameSheet(2), 1).Frames);
    }

    [Fact]
    public void SingleImageDecodingStillRejectsMultiFrameFilesAndBadSelections()
    {
        var decoder = new GimTextureDecoder();
        Assert.Throws<InvalidDataException>(() => decoder.Decode(TwoFrameSheet(2)));
        Assert.Throws<InvalidDataException>(() => decoder.DecodeFrame(TwoFrameSheet(2), 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => decoder.DecodeFrame(TwoFrameSheet(2), -1));
        // Palette frames must pair one-to-one with image frames.
        Assert.Throws<InvalidDataException>(() => decoder.DecodeAll(TwoFrameSheet(3)));
    }

    /// <summary>The observed cardh slot: a GIM declaring 4,224 bytes (DXT1 128x64 pixels at 128..4224) with only 4,096 stored.</summary>
    private static byte[] ThumbnailSlot(int stored)
    {
        byte[] pixels = new byte[4096];
        for (int at = 0; at < pixels.Length; at += 8) Put16(pixels, at + 4, 0xf800); // PSP order: indices, then colour0 (red), colour1.
        byte[] header = new byte[64 + pixels.Length];
        Put16(header, 0, 48); Put16(header, 4, 8); Put16(header, 8, 128); Put16(header, 10, 64); Put16(header, 12, 4);
        Put16(header, 14, 4); Put16(header, 16, 4); Put32(header, 24, 48); Put32(header, 28, 64); Put32(header, 32, header.Length);
        Put16(header, 40, 1); Put16(header, 42, 1); Put16(header, 44, 3); Put16(header, 46, 1); Put32(header, 48, 64);
        pixels.CopyTo(header, 64);
        byte[] gim = File(Block(4, header));
        Assert.Equal(4224, gim.Length);
        return gim[..stored];
    }

    [Fact]
    public void ThumbnailZeroFillsOnlyTheMeasuredMissingTail()
    {
        var texture = CardThumbnailDecoder.Decode(ThumbnailSlot(4096));
        Assert.Equal((128, 64, 8), (texture.Width, texture.Height, texture.Format));
        // Red sits in the high bits of the DXT1 colour, as PPSSPP and the original's displayed artwork show.
        Assert.Equal(new byte[] { 248, 0, 0, 255 }, texture.Rgba[..4]);
        // The zero-filled last 128 bytes are 16 blocks of the bottom-right block row; zero colours decode as opaque black.
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, texture.Rgba[^4..]);
        Assert.Contains(texture.Diagnostics, d => d.StartsWith("128 missing tail bytes zero-filled", StringComparison.Ordinal));
        Assert.Empty(CardThumbnailDecoder.Decode(ThumbnailSlot(4224)).Diagnostics);
    }

    [Fact]
    public void ThumbnailRejectsAnythingShorterThanTheObservedSlot()
    {
        Assert.Throws<InvalidDataException>(() => CardThumbnailDecoder.Decode(ThumbnailSlot(4092)));
        Assert.Throws<InvalidDataException>(() => CardThumbnailDecoder.Decode(ThumbnailSlot(100)));
    }
}
