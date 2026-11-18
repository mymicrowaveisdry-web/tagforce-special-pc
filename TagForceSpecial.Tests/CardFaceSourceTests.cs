using System.Buffers.Binary;
using System.Text;
using TagForceSpecial.Assets.Presentation;

namespace TagForceSpecial.Tests;

/// <summary>Synthetic CPM archives only; no original card data.</summary>
public class CardFaceSourceTests
{
    /// <summary>Card IDs 100..101: 100 has a 2,048-byte slot of DXT1 blocks, 101 is missing.</summary>
    private static byte[] Archive(string magic = "CPM", ushort colorA = 0xF800, ushort colorB = 0x001F, uint indices = 0)
    {
        var b = new byte[4096];
        Encoding.ASCII.GetBytes(magic).CopyTo(b, 0); b[3] = 0x1a;
        Put(b, 4, 4096); Put(b, 16, 72); Put(b, 20, 100); Put(b, 24, 101); Put(b, 28, 48); Put(b, 32, 1);
        Put(b, 48, 1); // card 100 -> sector 1; card 101 missing (0)
        Array.Fill(b, (byte)0xff, 64, 8);
        for (int block = 0; block < 256; block++)
        {
            int o = 2048 + block * 8;
            Put(b, o, indices);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o + 4), colorA);
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(o + 6), colorB);
        }
        return b;
    }

    private static void Put(byte[] b, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset), value);

    [Fact]
    public void DecodesA64x64PspDxt1FaceWithRedInTheHighBits()
    {
        var source = new CardFaceSource(Archive());
        Assert.True(source.Contains(100));
        var rgba = source.Decode(100)!;
        Assert.Equal(64 * 64 * 4, rgba.Length);
        // Index 0 everywhere selects the first colour, 0xF800: red in the high bits for PSP DXT.
        Assert.Equal(new byte[] { 248, 0, 0, 255 }, rgba[..4]);
        Assert.Equal(new byte[] { 248, 0, 0, 255 }, rgba[^4..]);
    }

    [Fact]
    public void FourColourAndTransparentModesFollowTheColourOrder()
    {
        // Index 3 for every pixel: with a > b it is (a + 2b) / 3; with a <= b it is transparent black.
        var opaque = new CardFaceSource(Archive(colorA: 0xF800, colorB: 0x001F, indices: 0xFFFFFFFF)).Decode(100)!;
        Assert.Equal(new byte[] { 82, 0, 165, 255 }, opaque[..4]);
        var transparent = new CardFaceSource(Archive(colorA: 0x001F, colorB: 0xF800, indices: 0xFFFFFFFF)).Decode(100)!;
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, transparent[..4]);
    }

    [Fact]
    public void MissingCardsReturnNullAndOtherArchiveKindsAreRejected()
    {
        var source = new CardFaceSource(Archive());
        Assert.False(source.Contains(101));
        Assert.Null(source.Decode(101));
        Assert.Throws<InvalidDataException>(() => new CardFaceSource(Archive("CIP")));
    }
}
