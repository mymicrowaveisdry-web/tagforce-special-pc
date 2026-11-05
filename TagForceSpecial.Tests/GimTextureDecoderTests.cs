using System.Buffers.Binary;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.Tests;

public class GimTextureDecoderTests
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
    private static byte[] Plane(int type, int format, int width, int height, byte[] pixels,
        bool swizzled = false, int pitchAlign = 1, int heightAlign = 1)
    {
        byte[] header = new byte[64 + pixels.Length];
        Put16(header, 0, 48); Put16(header, 4, format); Put16(header, 6, swizzled ? 1 : 0);
        Put16(header, 8, width); Put16(header, 10, height);
        Put16(header, 12, format switch { 0 or 1 or 2 or 6 => 16, 3 or 7 => 32, 4 or 8 => 4, _ => 8 });
        Put16(header, 14, pitchAlign); Put16(header, 16, heightAlign);
        Put32(header, 24, 48); Put32(header, 28, 64); Put32(header, 32, header.Length);
        Put16(header, 40, type == 5 ? 2 : 1); Put16(header, 42, 1);
        Put16(header, 44, 3); Put16(header, 46, 1); Put32(header, 48, 64);
        pixels.CopyTo(header, 64); return Block(type, header);
    }
    private static byte[] File(params byte[][] planes)
    {
        byte[] picture = Block(3, planes.SelectMany(b => b).ToArray(), container: true);
        byte[] root = Block(2, picture, container: true);
        byte[] result = new byte[16 + root.Length];
        "MIG.00.1PSP\0\0\0\0\0"u8.CopyTo(result); root.CopyTo(result, 16); return result;
    }
    private static GimTexture Decode(byte[] bytes) => new GimTextureDecoder().Decode(bytes);
    private static byte[] Palette(int count) => Plane(5, 3, count, 1,
        Enumerable.Range(0, count).SelectMany(i => new byte[] { (byte)i, (byte)(i + 10), (byte)(i + 20), (byte)(i + 30) }).ToArray());

    [Fact]
    public void RgbaPreservesAlphaPaddingInputAndStreamPosition()
    {
        byte[] bytes = File(Plane(4, 3, 1, 2, [1, 2, 3, 0, 99, 99, 99, 99, 4, 5, 6, 128, 99, 99, 99, 99], pitchAlign: 8));
        byte[] before = (byte[])bytes.Clone();
        using var stream = new MemoryStream(bytes, writable: false); stream.Position = 7;
        var image = new GimTextureDecoder().Decode(stream);
        Assert.Equal(new byte[] { 1, 2, 3, 0, 4, 5, 6, 128 }, image.Rgba);
        Assert.Equal((1, 2), (image.Width, image.Height));
        Assert.Empty(image.Diagnostics); Assert.Equal(7, stream.Position); Assert.Equal(before, bytes);
    }

    [Theory]
    [InlineData(0, 0x001f, 255, 0, 0, 255)]
    [InlineData(0, 0x07e0, 0, 255, 0, 255)]
    [InlineData(1, 0x7c00, 0, 0, 255, 0)]
    [InlineData(1, 0x801f, 255, 0, 0, 255)]
    [InlineData(2, 0x4321, 17, 34, 51, 68)]
    public void DirectPackedColorsUsePspChannels(int format, int value, int r, int g, int b, int a)
    {
        byte[] pixel = new byte[2]; Put16(pixel, 0, value);
        Assert.Equal(new byte[] { (byte)r, (byte)g, (byte)b, (byte)a }, Decode(File(Plane(4, format, 1, 1, pixel))).Rgba);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Index4UsesLowNibbleFirstAndPaletteEitherSide(bool paletteFirst)
    {
        byte[] image = Plane(4, 4, 3, 1, [0x21, 0xf3]), palette = Palette(4);
        var result = Decode(paletteFirst ? File(palette, image) : File(image, palette));
        Assert.Equal(new byte[] { 1, 11, 21, 31, 2, 12, 22, 32, 3, 13, 23, 33 }, result.Rgba);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void WiderIndicesAndZeroAlphaArePreserved(int format)
    {
        int size = format == 5 ? 1 : format == 6 ? 2 : 4;
        byte[] indices = new byte[size]; indices[0] = 1;
        byte[] pal = Plane(5, 3, 2, 1, [0, 0, 0, 0, 9, 8, 7, 0]);
        Assert.Equal(new byte[] { 9, 8, 7, 0 }, Decode(File(Plane(4, format, 1, 1, indices), pal)).Rgba);
    }

    [Fact]
    public void SwizzleMapsBothTileAxesAndCropsPaddedRows()
    {
        byte[] tiled = Enumerable.Repeat((byte)1, 128).Concat(Enumerable.Repeat((byte)2, 128))
            .Concat(Enumerable.Repeat((byte)3, 128)).Concat(Enumerable.Repeat((byte)4, 128)).ToArray();
        var result = Decode(File(Plane(4, 5, 25, 10, tiled, true, 16, 8), Palette(5)));
        Assert.Equal(25 * 10 * 4, result.Rgba.Length);
        Assert.Equal(1, result.Rgba[0]); Assert.Equal(2, result.Rgba[16 * 4]);
        Assert.Equal(3, result.Rgba[(8 * 25) * 4]); Assert.Equal(4, result.Rgba[(9 * 25 + 24) * 4]);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(16)]
    public void NarrowSwizzledIndex4UsesPaddedBytePitch(int width)
    {
        byte[] tile = new byte[128]; Array.Fill(tile, (byte)0x21, 0, width / 2);
        var result = Decode(File(Plane(4, 4, width, 8, tile, true, 16, 8), Palette(3)));
        for (int x = 0; x < width; x++)
            Assert.Equal(x % 2 + 1, result.Rgba[x * 4]);
        Assert.Equal(0, result.Rgba[width * 4]);
    }

    [Fact]
    public void SwizzledRgbaAddressesTilesInBytesAndKeepsAlpha()
    {
        byte[] tile = new byte[128];
        for (int i = 0; i < tile.Length; i += 4)
        { tile[i] = 7; tile[i + 1] = 8; tile[i + 2] = 9; tile[i + 3] = 128; }
        var result = Decode(File(Plane(4, 3, 4, 8, tile, true, 16, 8)));
        Assert.Equal(Enumerable.Repeat(new byte[] { 7, 8, 9, 128 }, 32).SelectMany(x => x).ToArray(), result.Rgba);
    }

    [Fact]
    public void Dxt1UsesPspWordOrderAndFourColorInterpolation()
    {
        var result = Decode(File(Plane(4, 8, 4, 4, [0xe4, 0xe4, 0xe4, 0xe4, 0x00, 0xf8, 0xe0, 0x07])));
        Assert.Equal(new byte[] { 248, 0, 0, 255, 0, 252, 0, 255, 165, 84, 0, 255, 82, 168, 0, 255 }, result.Rgba[..16]);
    }

    [Fact]
    public void Dxt1HandlesTransparentSelectorAndPartialEdgeBlocks()
    {
        byte[] blocks = [255, 255, 255, 255, 0, 0, 255, 255, 0, 0, 0, 0, 0, 248, 0, 0];
        var result = Decode(File(Plane(4, 8, 5, 3, blocks)));
        Assert.Equal(60, result.Rgba.Length);
        Assert.Equal(new byte[4], result.Rgba[..4]);
        Assert.Equal(new byte[] { 248, 0, 0, 255 }, result.Rgba[16..20]);
    }

    [Fact]
    public void RejectsEveryTruncatedPrefixAndRestoresStreamOnFailure()
    {
        byte[] fixture = File(Plane(4, 3, 1, 1, [1, 2, 3, 4]));
        for (int length = 0; length < fixture.Length; length++)
        {
            using var stream = new MemoryStream(fixture[..length]); stream.Position = Math.Min(3, length);
            long before = stream.Position;
            Assert.Throws<InvalidDataException>(() => new GimTextureDecoder().Decode(stream));
            Assert.Equal(before, stream.Position);
        }
    }

    [Theory]
    [InlineData(0, 0)] // signature
    [InlineData(20, int.MaxValue)] // root size
    [InlineData(56, 0)] // leaf traversal
    [InlineData(88, int.MaxValue)] // frame table
    [InlineData(92, 1)] // payload start before frame table
    [InlineData(96, int.MaxValue)] // data end
    public void RejectsMalformedOffsetsAndSignature(int offset, int value)
    {
        byte[] bytes = File(Plane(4, 3, 1, 1, [1, 2, 3, 4])); Put32(bytes, offset, value);
        Assert.Throws<InvalidDataException>(() => Decode(bytes));
    }

    [Theory]
    [InlineData(68, 9)] // DXT3 explicitly unsupported
    [InlineData(70, 2)] // unknown pixel order
    [InlineData(72, 0)] // zero width
    [InlineData(74, 5000)] // allocation limit
    [InlineData(76, 16)] // wrong bpp
    [InlineData(78, 3)] // non-power-of-two alignment
    [InlineData(106, 2)] // mip count
    [InlineData(110, 2)] // frame count
    public void RejectsUnsupportedHeaders(int offset, int value)
    {
        byte[] bytes = File(Plane(4, 3, 1, 1, [1, 2, 3, 4])); Put16(bytes, offset, value);
        Assert.Throws<InvalidDataException>(() => Decode(bytes));
    }

    [Fact]
    public void RejectsMissingPaletteOutOfRangeIndexAndMultipleImages()
    {
        byte[] image = Plane(4, 5, 1, 1, [2]);
        Assert.Throws<InvalidDataException>(() => Decode(File(image)));
        Assert.Throws<InvalidDataException>(() => Decode(File(image, Palette(2))));
        Assert.Throws<InvalidDataException>(() => Decode(File(image, image, Palette(3))));
    }

    [Fact]
    public void TrailingBytesAreDiagnosedWithoutBeingInterpretedAsAnotherImage()
    {
        byte[] image = File(Plane(4, 3, 1, 1, [1, 2, 3, 4]));
        var result = Decode(image.Concat(image).ToArray());
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, result.Rgba);
        Assert.Contains("trailing bytes", Assert.Single(result.Diagnostics));
    }

    [Fact]
    public void MultiplePalettesRequireExplicitSelectionAndKeepSeparateColors()
    {
        byte[] palette = Plane(5, 3, 1, 1, [1, 2, 3, 4, 9, 8, 7, 6]);
        Put16(palette, 16 + 42, 2); Put32(palette, 16 + 52, 68);
        byte[] file = File(Plane(4, 5, 1, 1, [0]), palette);
        Assert.Throws<InvalidDataException>(() => Decode(file));
        var first = new GimTextureDecoder().Decode(file, paletteLevel: 0);
        var second = new GimTextureDecoder().Decode(file, paletteLevel: 1);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, first.Rgba);
        Assert.Equal(new byte[] { 9, 8, 7, 6 }, second.Rgba);
        Assert.Contains("level 1 of 2", Assert.Single(second.Diagnostics));
        Assert.Throws<InvalidDataException>(() => new GimTextureDecoder().Decode(file, paletteLevel: 2));
        Put32(palette, 16 + 52, 67);
        Assert.Throws<InvalidDataException>(() => new GimTextureDecoder().Decode(File(Plane(4, 5, 1, 1, [0]), palette), 0));
    }
}
