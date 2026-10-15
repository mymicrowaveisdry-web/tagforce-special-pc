using System.Buffers.Binary;
using System.Text;
using TagForceSpecial.Assets;

namespace TagForceSpecial.Tests;

public class CardImageIndexReaderTests
{
    private static byte[] Fixture(string magic = "CIP")
    {
        var b = new byte[8192];
        Encoding.ASCII.GetBytes(magic).CopyTo(b, 0); b[3] = 0x1a;
        Put(b, 4, 8192); Put(b, 16, 88); Put(b, 20, 100); Put(b, 24, 103);
        Put(b, 28, 48); Put(b, 32, 1);
        Put(b, 48, 1); Put(b, 64, 2); Put(b, 72, 3);
        Array.Fill(b, (byte)0xff, 80, 8);
        return b;
    }
    private static void Put(byte[] b, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset), value);

    [Theory]
    [InlineData("CIP")]
    [InlineData("CPJ")]
    [InlineData("CPM")]
    public void ListsSlotsAndMissingIdsWithoutChangingInput(string signature)
    {
        var b = Fixture(signature); var before = (byte[])b.Clone();
        using var stream = new MemoryStream(b, writable: false); stream.Position = 7;
        var result = new CardImageIndexReader().ReadIndex(stream);
        Assert.Equal(7, stream.Position); Assert.Equal(before, b);
        Assert.Equal(signature, result.Signature); Assert.Equal(1, result.MissingSlots);
        Assert.Equal(new uint[] { 100, 102, 103 }, result.Entries.Select(e => e.CardId));
        Assert.Equal(new long[] { 2048, 4096, 6144 }, result.Entries.Select(e => e.Offset));
    }

    [Theory]
    [InlineData(4, 8191u)]
    [InlineData(16, 80u)]
    [InlineData(20, 104u)]
    [InlineData(24, uint.MaxValue)]
    [InlineData(28, 0u)]
    [InlineData(32, 0u)]
    [InlineData(48, 0x80000030u)]
    [InlineData(48, uint.MaxValue)]
    [InlineData(48, 4u)]
    [InlineData(52, 1u)]
    [InlineData(64, 1u)]
    [InlineData(80, 0u)]
    public void RejectsUnsupportedOrOutOfBoundsLayouts(int offset, uint value)
    {
        var b = Fixture(); Put(b, offset, value);
        using var stream = new MemoryStream(b); stream.Position = 5;
        Assert.Throws<InvalidDataException>(() => new CardImageIndexReader().ReadIndex(stream));
        Assert.Equal(5, stream.Position);
    }

    [Fact]
    public void RejectsTruncatedHeader()
    { Assert.Throws<InvalidDataException>(() => new CardImageIndexReader().ReadIndex(new MemoryStream(new byte[47]))); }
}
