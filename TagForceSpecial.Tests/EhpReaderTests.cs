using System.Buffers.Binary;
using TagForceSpecial.Assets;

namespace TagForceSpecial.Tests;

public class EhpReaderTests
{
    private static byte[] Fixture()
    {
        var bytes = new byte[86];
        "EHP\x03"u8.CopyTo(bytes);
        Put(bytes, 4, 86); Put(bytes, 8, 0x20544f4e); Put(bytes, 12, 2);
        Put(bytes, 16, 40); Put(bytes, 20, 64);
        Put(bytes, 24, 47); Put(bytes, 28, 80);
        "a\0"u8.CopyTo(bytes.AsSpan(40)); Put(bytes, 42, 3);
        "b\0"u8.CopyTo(bytes.AsSpan(47)); Put(bytes, 49, 6);
        "one"u8.CopyTo(bytes.AsSpan(64)); "second"u8.CopyTo(bytes.AsSpan(80));
        return bytes;
    }

    private static void Put(byte[] bytes, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    private static EhpIndex Read(byte[] bytes) => new EhpReader().ReadIndex(new MemoryStream(bytes, writable: false));

    [Fact]
    public void ListsKnownRangesWithoutChangingInputOrPosition()
    {
        byte[] bytes = Fixture();
        byte[] original = (byte[])bytes.Clone();
        using var stream = new MemoryStream(bytes, writable: false);
        stream.Position = 11;
        var index = new EhpReader().ReadIndex(stream);
        Assert.Equal(11, stream.Position);
        Assert.Equal(original, bytes);
        Assert.Equal(new EhpEntry(0, "a", 40, 64, 3), index.Entries[0]);
        Assert.Equal(new EhpEntry(1, "b", 47, 80, 6), index.Entries[1]);
        Assert.Empty(index.Diagnostics);
    }

    [Theory]
    [InlineData(4, 85u)]
    [InlineData(12, uint.MaxValue)]
    [InlineData(12, 12u)]
    [InlineData(16, 8u)]
    [InlineData(16, uint.MaxValue)]
    [InlineData(20, 8u)]
    [InlineData(20, uint.MaxValue)]
    [InlineData(42, uint.MaxValue)]
    [InlineData(28, 65u)]
    [InlineData(24, 40u)]
    public void RejectsMalformedIntegerFields(int offset, uint value)
    {
        byte[] bytes = Fixture(); Put(bytes, offset, value);
        Assert.Throws<InvalidDataException>(() => Read(bytes));
    }

    [Fact]
    public void RejectsTruncationAndRestoresPositionOnFailure()
    {
        for (int length = 0; length < Fixture().Length; length++)
        {
            using var stream = new MemoryStream(Fixture()[..length]);
            stream.Position = Math.Min(length, 3);
            long before = stream.Position;
            Assert.Throws<InvalidDataException>(() => new EhpReader().ReadIndex(stream));
            Assert.Equal(before, stream.Position);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void RejectsWrongMagicOrVersion(int position)
    {
        byte[] bytes = Fixture(); bytes[position] = 0;
        Assert.Throws<InvalidDataException>(() => Read(bytes));
    }

    [Fact]
    public void RejectsUnterminatedName()
    {
        byte[] bytes = Fixture(); Array.Fill(bytes, (byte)'x', 40, 24);
        Assert.Throws<InvalidDataException>(() => Read(bytes));
    }

    [Theory]
    [InlineData((byte)'/')]
    [InlineData((byte)'\\')]
    [InlineData((byte)':')]
    [InlineData((byte)0xff)]
    public void RejectsPathOrUnsupportedNameBytes(byte value)
    {
        byte[] bytes = Fixture(); bytes[40] = value;
        Assert.Throws<InvalidDataException>(() => Read(bytes));
    }

    [Fact]
    public void PreservesUnknownMarkerAndPermitsEmptyStoredEntries()
    {
        byte[] bytes = Fixture(); Put(bytes, 8, 0); Put(bytes, 49, 0);
        var index = Read(bytes);
        Assert.Equal(0u, index.Marker);
        Assert.Single(index.Diagnostics);
        Assert.Equal(0, index.Entries[1].Length);
    }

    [Fact]
    public void RejectsMetadataSizeFieldCrossingFirstPayload()
    {
        byte[] bytes = Fixture(); Put(bytes, 16, 62); bytes[62] = (byte)'x'; bytes[63] = 0;
        Assert.Throws<InvalidDataException>(() => Read(bytes));
    }
}
